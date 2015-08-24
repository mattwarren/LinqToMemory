﻿using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Voron.Impl;
using Voron.Platform.Win32;
using Xunit;
using Xunit.Extensions;
using Mono.Unix.Native;

namespace Voron.Tests.Storage
{
	public unsafe class MemoryMapWithoutBackingPagerTest : StorageTest
	{
		private readonly string dummyData;
		private const string LoremIpsum = "Lorem ipsum dolor sit amet, consectetur adipisicing elit, sed do eiusmod tempor incididunt ut labore et dolore magna aliqua. Ut enim ad minim veniam, quis nostrud exercitation ullamco laboris nisi ut aliquip ex ea commodo consequat. Duis aute irure dolor in reprehenderit in voluptate velit esse cillum dolore eu fugiat nulla pariatur. Excepteur sint occaecat cupidatat non proident, sunt in culpa qui officia deserunt mollit anim id est laborum.";
		private const string TestTreeName = "tree";
		private const long PagerInitialSize = 64 * 1024;
		public MemoryMapWithoutBackingPagerTest()
			: base(StorageEnvironmentOptions.CreateMemoryOnly())
		{
			dummyData = GenerateLoremIpsum(1024);
		}

		[PrefixesFact]
		public void Should_be_able_to_read_and_write_lots_of_data()
		{
			CreatTestSchema();
			var writeBatch = new WriteBatch();
			var testData = GenerateTestData().ToList();

			foreach (var dataPair in testData)
				writeBatch.Add(dataPair.Key, StreamFor(dataPair.Value), TestTreeName);				

			Env.Writer.Write(writeBatch);

			using (var snapshot = Env.CreateSnapshot())
			{
				using (var iterator = snapshot.Iterate(TestTreeName))
				{
					Assert.True(iterator.Seek(Slice.BeforeAllKeys));

					do
					{
						var value = iterator.CreateReaderForCurrent().ToStringValue();
						var extractedDataPair = new KeyValuePair<string, string>(iterator.CurrentKey.ToString(), value);
						Assert.Contains(extractedDataPair,testData);

					} while (iterator.MoveNext());
				}
				
			}
			
		}

		private string GenerateLoremIpsum(int count)
		{
			return String.Join(Environment.NewLine, Enumerable.Repeat(LoremIpsum, count));
		}

		private IEnumerable<KeyValuePair<string,string>> GenerateTestData()
		{
			for(int i = 0; i < 1000; i++)
				yield return new KeyValuePair<string, string>("Key " + i, "Data:" + dummyData);
		}

		[PrefixesFact]
		public void Should_be_able_to_read_and_write_small_values()
		{
			CreatTestSchema();
			var writeBatch = new WriteBatch();
			writeBatch.Add("key",StreamFor("value"),TestTreeName);
			Env.Writer.Write(writeBatch);

			using (var snapshot = Env.CreateSnapshot())
			{
				var storedValue = Encoding.UTF8.GetString(snapshot.Read(TestTreeName, "key").Reader.AsStream().ReadData());
				Assert.Equal("value",storedValue);
			}
		}

		[PrefixesTheory]
		[InlineData(2)]
		[InlineData(5)]
		[InlineData(15)]
		[InlineData(100)]
		[InlineData(250)]
		public void Should_be_able_to_allocate_new_pages(int growthMultiplier)
		{
			var numberOfPagesBeforeAllocation = Env.Options.DataPager.NumberOfAllocatedPages;
			Assert.DoesNotThrow(() => Env.Options.DataPager.AllocateMorePages(null, PagerInitialSize * growthMultiplier));
			Assert.Equal(numberOfPagesBeforeAllocation * growthMultiplier, Env.Options.DataPager.NumberOfAllocatedPages);
		}

		[PrefixesTheory]
		[InlineData(2)]
		[InlineData(5)]
		[InlineData(15)]
		[InlineData(100)]
		[InlineData(250)]
		public void Should_be_able_to_allocate_new_pages_with_apply_logs_to_data_file(int growthMultiplier)
		{
		    _options.ManualFlushing = true;
		    Assert.DoesNotThrow(() => Env.Options.DataPager.AllocateMorePages(null, PagerInitialSize*growthMultiplier));
		    var testData = GenerateTestData().ToList();
		    CreatTestSchema();
		    using (var tx = Env.NewTransaction(TransactionFlags.ReadWrite))
		    {
		        var tree = tx.ReadTree(TestTreeName);
		        foreach (var dataPair in testData)
		            tree.Add(dataPair.Key, StreamFor(dataPair.Value));

		        tx.Commit();
		    }
            Env.FlushLogToDataFile();
		}


	    private void CreatTestSchema()
		{
			using(var tx = Env.NewTransaction(TransactionFlags.ReadWrite))
			{
				Env.CreateTree(tx, TestTreeName);
				tx.Commit();
			}
		}

		[PrefixesFact]
		public void Should_be_able_to_allocate_new_pages_multiple_times()
		{
			var pagerSize = PagerInitialSize;
			for (int allocateMorePagesCount = 0; allocateMorePagesCount < 5; allocateMorePagesCount++)
			{
				pagerSize *= 2;
				var numberOfPagesBeforeAllocation = Env.Options.DataPager.NumberOfAllocatedPages;
				Assert.DoesNotThrow(() => Env.Options.DataPager.AllocateMorePages(null, pagerSize));
				Assert.Equal(numberOfPagesBeforeAllocation*2, Env.Options.DataPager.NumberOfAllocatedPages);
			}
		}

		byte* AllocateMemoryAtEndOfPager (long totalAllocationSize)
		{
			if(StorageEnvironmentOptions.RunningOnPosix)
			{
				var p = Syscall.mmap (new IntPtr (Env.Options.DataPager.PagerState.MapBase + totalAllocationSize), 16,
					MmapProts.PROT_READ | MmapProts.PROT_WRITE, MmapFlags.MAP_ANONYMOUS, -1, 0);
				if(p.ToInt64() == -1)
				{
					return null;
				}
				return (byte*)p.ToPointer ();
			}
			return Win32NativeMethods.VirtualAlloc (Env.Options.DataPager.PagerState.MapBase + totalAllocationSize, new UIntPtr (16), 
				Win32NativeMethods.AllocationType.RESERVE, Win32NativeMethods.MemoryProtection.EXECUTE_READWRITE);
		}

		static void FreeMemoryAtEndOfPager (byte* adjacentBlockAddress)
		{
			if (adjacentBlockAddress == null || adjacentBlockAddress == (byte*)0)
				return;
			if(StorageEnvironmentOptions.RunningOnPosix)
			{
				Syscall.munmap (new IntPtr (adjacentBlockAddress), 16);
				return;
			}
			Win32NativeMethods.VirtualFree (adjacentBlockAddress, UIntPtr.Zero, Win32NativeMethods.FreeType.MEM_RELEASE);
		}

		[PrefixesFact]
		public void Should_be_able_to_allocate_new_pages_with_remapping()
		{
			var pagerSize = PagerInitialSize;

			//first grow several times the pager
			for (int allocateMorePagesCount = 0; allocateMorePagesCount < 2; allocateMorePagesCount++)
			{
				pagerSize *= 2;
				Assert.DoesNotThrow(() => Env.Options.DataPager.AllocateMorePages(null, pagerSize));
			}

			var totalAllocationSize = Env.Options.DataPager.PagerState.AllocationInfos.Sum(info => info.Size);

			//prevent continuous allocation and force remapping on next pager growth			
			byte* adjacentBlockAddress = null;
			try
			{
				//if this fails and adjacentBlockAddress == 0 or null --> this means the remapping will occur anyway. 
				//the allocation is here to make sure the remapping does happen in any case
				adjacentBlockAddress = AllocateMemoryAtEndOfPager (totalAllocationSize);

				pagerSize *= 2;
				var numberOfPagesBeforeAllocation = Env.Options.DataPager.NumberOfAllocatedPages;
				Assert.DoesNotThrow(() => Env.Options.DataPager.AllocateMorePages(null, pagerSize));
				Assert.Equal(numberOfPagesBeforeAllocation*2, Env.Options.DataPager.NumberOfAllocatedPages);

			}
			finally
			{
				FreeMemoryAtEndOfPager (adjacentBlockAddress);
			}
		}
	}
}
