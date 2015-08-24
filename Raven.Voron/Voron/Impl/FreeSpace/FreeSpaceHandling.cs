using System.Collections.Generic;
using Voron.Trees;
using Voron.Util.Conversion;

namespace Voron.Impl.FreeSpace
{
	public class FreeSpaceHandling : IFreeSpaceHandling
	{
		internal const int NumberOfPagesInSection = 256 * 8; // 256 bytes, 8 bits per byte = 2,048 - each section 8 MB in size

		public long? TryAllocateFromFreeSpace(Transaction tx, int num)
		{
			if (tx.State.FreeSpaceRoot == null)
				return null; // initial setup

		    if (tx.State.FreeSpaceRoot.State.EntriesCount == 0)
		        return null;

			using (var it = tx.State.FreeSpaceRoot.Iterate())
			{
				if (it.Seek(Slice.BeforeAllKeys) == false)
					return null;

				if (num < NumberOfPagesInSection)
				{
					return TryFindSmallValue(tx, it, num);
				}
				return TryFindLargeValue(tx, it, num);
			}
		}

		private long? TryFindLargeValue(Transaction tx, TreeIterator it, int num)
		{
			int numberOfNeededFullSections = num / NumberOfPagesInSection;
			int numberOfExtraBitsNeeded = num % NumberOfPagesInSection;
			int foundSections = 0;
			MemorySlice startSection = null;
			long? startSectionId = null;
			var sections = new List<Slice>();

			do
			{
				var stream = it.CreateReaderForCurrent();
				{
					var current = new StreamBitArray(stream);
				    var currentSectionId = it.CurrentKey.CreateReader().ReadBigEndianInt64();

					//need to find full free pages
					if (current.SetCount < NumberOfPagesInSection)
					{
						ResetSections(ref foundSections, sections, ref startSection, ref startSectionId);
						continue;
					}

					//those sections are not following each other in the memory
					if (startSectionId != null && currentSectionId != startSectionId + foundSections)
					{
						ResetSections(ref foundSections, sections, ref startSection, ref startSectionId);
					}

					//set the first section of the sequence
					if (startSection == null)
					{
						startSection = it.CurrentKey;
						startSectionId = currentSectionId;
					}

					sections.Add(it.CurrentKey);
					foundSections++;

					if (foundSections != numberOfNeededFullSections)
						continue;

					//we found enough full sections now we need just a bit more
					if (numberOfExtraBitsNeeded == 0)
					{
						foreach (var section in sections)
						{
							tx.State.FreeSpaceRoot.Delete(section);
						}

						return startSectionId * NumberOfPagesInSection;
					}

					var nextSectionId = currentSectionId + 1;
					var nextId = new Slice(EndianBitConverter.Big.GetBytes(nextSectionId));
					var read = tx.State.FreeSpaceRoot.Read(nextId);
					if (read == null)
					{
						//not a following next section
						ResetSections(ref foundSections, sections, ref startSection, ref startSectionId);
						continue;
					}

					var next = new StreamBitArray(read.Reader);

					if (next.HasStartRangeCount(numberOfExtraBitsNeeded) == false)
					{
						//not enough start range count
						ResetSections(ref foundSections, sections, ref startSection, ref startSectionId);
						continue;
					}

					//mark selected bits to false
					if (next.SetCount == numberOfExtraBitsNeeded)
					{
						tx.State.FreeSpaceRoot.Delete(nextId);
					}
					else
					{
						for (int i = 0; i < numberOfExtraBitsNeeded; i++)
						{
							next.Set(i, false);
						}
						tx.State.FreeSpaceRoot.Add(nextId, next.ToStream());
					}

					foreach (var section in sections)
					{
						tx.State.FreeSpaceRoot.Delete(section);
					}

					return startSectionId * NumberOfPagesInSection;
				}
			} while (it.MoveNext());

			return null;
		}

		private static void ResetSections(ref int foundSections, List<Slice> sections, ref MemorySlice startSection, ref long? startSectionId)
		{
			foundSections = 0;
			startSection = null;
			startSectionId = null;
			sections.Clear();
		}

		private long? TryFindSmallValue(Transaction tx, TreeIterator it, int num)
		{
			do
			{
			    var stream = it.CreateReaderForCurrent();
				{
					var current = new StreamBitArray(stream);
				    var currentSectionId = it.CurrentKey.CreateReader().ReadBigEndianInt64();

					long? page;
					if (current.SetCount < num)
					{
						if (TryFindSmallValueMergingTwoSections(tx, it.CurrentKey, num, current, currentSectionId, out page))
							return page;
						continue;
					}

					if (TryFindContinuousRange(tx, it, num, current, currentSectionId, out page))
						return page;

					//could not find a continuous so trying to merge
					if (TryFindSmallValueMergingTwoSections(tx, it.CurrentKey, num, current, currentSectionId, out page))
						return page;
				}
			} while (it.MoveNext());

			return null;
		}

		private bool TryFindContinuousRange(Transaction tx, TreeIterator it, int num, StreamBitArray current, long currentSectionId, out long? page)
		{
			page = -1;
			var start = -1;
			var count = 0;
			for (int i = 0; i < NumberOfPagesInSection; i++)
			{
				if (current.Get(i))
				{
					if (start == -1)
						start = i;
					count++;
					if (count == num)
					{
						page = currentSectionId * NumberOfPagesInSection + start;
						break;
					}
				}
				else
				{
					start = -1;
					count = 0;
				}
			}

			if (count != num)
				return false;

			if (current.SetCount == num)
			{
				tx.State.FreeSpaceRoot.Delete(it.CurrentKey);
			}
			else
			{
				for (int i = 0; i < num; i++)
				{
					current.Set(i + start, false);
				}

				tx.State.FreeSpaceRoot.Add(it.CurrentKey, current.ToStream());
			}

			return true;
		}

		private static bool TryFindSmallValueMergingTwoSections(Transaction tx, Slice currentSectionIdSlice, int num, StreamBitArray current, long currentSectionId, out long? result)
		{
			result = -1;
			var currentEndRange = current.GetEndRangeCount();
			if (currentEndRange == 0)
				return false;

			var nextSectionId = currentSectionId + 1;

			var nextId = new Slice(EndianBitConverter.Big.GetBytes(nextSectionId));
			var read = tx.State.FreeSpaceRoot.Read(nextId);
			if (read == null)
				return false;

			var next = new StreamBitArray(read.Reader);

			var nextRange = num - currentEndRange;
			if (next.HasStartRangeCount(nextRange) == false)
				return false;

			if (next.SetCount == nextRange)
			{
				tx.State.FreeSpaceRoot.Delete(nextId);
			}
			else
			{
				for (int i = 0; i < nextRange; i++)
				{
					next.Set(i, false);
				}
				tx.State.FreeSpaceRoot.Add(nextId, next.ToStream());
			}

			if (current.SetCount == currentEndRange)
			{
				tx.State.FreeSpaceRoot.Delete(currentSectionIdSlice);
			}
			else
			{
				for (int i = 0; i < currentEndRange; i++)
				{
					current.Set(NumberOfPagesInSection - 1 - i, false);
				}
				tx.State.FreeSpaceRoot.Add(currentSectionIdSlice, current.ToStream());
			}


			result = currentSectionId * NumberOfPagesInSection + (NumberOfPagesInSection - currentEndRange);
			return true;
		}

		public List<long> AllPages(Transaction tx)
		{
			return tx.State.FreeSpaceRoot.AllPages();
		}

		public void FreePage(Transaction tx, long pageNumber)
		{
			var section = pageNumber / NumberOfPagesInSection;
			var sectionKey = new Slice(EndianBitConverter.Big.GetBytes(section));
			var result = tx.State.FreeSpaceRoot.Read(sectionKey);
			var sba = result == null ? new StreamBitArray() : new StreamBitArray(result.Reader);
			sba.Set((int)(pageNumber % NumberOfPagesInSection), true);
			tx.State.FreeSpaceRoot.Add(sectionKey, sba.ToStream());
		}
	}
}
