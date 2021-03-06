﻿using Sparrow;
using Sparrow.Platform;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using Voron.Impl.Paging;
using Voron.Util;

namespace Voron.Impl.Journal
{
	public unsafe class JournalReader
	{
		public class RecoveryPagePosition
		{
			public long JournalPos;
			public long TransactionId;
			public bool IsOverflow;
			public int NumberOfOverflowPages = -1;
		}

		private readonly IVirtualPager _pager;
		private readonly IVirtualPager _recoveryPager;

		private readonly long _lastSyncedTransactionId;
		private long _readingPage;

		private readonly Dictionary<long, RecoveryPagePosition> _transactionPageTranslation = new Dictionary<long, RecoveryPagePosition>();
		private int _recoveryPage;

		public bool RequireHeaderUpdate { get; private set; }

		public long NextWritePage
		{
			get { return _readingPage; }
		}

		public JournalReader(IVirtualPager pager, IVirtualPager recoveryPager, long lastSyncedTransactionId, TransactionHeader* previous, int recoverPage = 0)
		{
			RequireHeaderUpdate = false;
			_pager = pager;
			_recoveryPager = recoveryPager;
			_lastSyncedTransactionId = lastSyncedTransactionId;
			_readingPage = 0;
			_recoveryPage = recoverPage;
			LastTransactionHeader = previous;
		}

		public TransactionHeader* LastTransactionHeader { get; private set; }

		public long? MaxPageToRead { get; set; }

		public bool ReadOneTransaction(StorageEnvironmentOptions options, bool checkCrc = true)
		{
			if (_readingPage >= _pager.NumberOfAllocatedPages)
				return false;

			if (MaxPageToRead != null && _readingPage >= MaxPageToRead.Value)
				return false;

			TransactionHeader* current;
			if (!TryReadAndValidateHeader(options, out current))
				return false;

			var transactionSize = GetNumberOfPagesFromSize(current->Compressed ? current->CompressedSize : current->UncompressedSize);

			if (current->TransactionId <= _lastSyncedTransactionId)
			{
				LastTransactionHeader = current;
				_readingPage += transactionSize;
				return true; // skipping
			}

			if (checkCrc && !ValidatePagesCrc(options, transactionSize, current))
				return false;

			_recoveryPager.EnsureContinuous(null, _recoveryPage, (current->PageCount + current->OverflowPageCount) + 1);
			var dataPage = _recoveryPager.AcquirePagePointer(_recoveryPage);

			UnmanagedMemory.Set(dataPage, 0, (current->PageCount + current->OverflowPageCount) * AbstractPager.PageSize);
			if (current->Compressed)
			{
				if (TryDecompressTransactionPages(options, current, dataPage) == false)
					return false;
			}
			else
			{
                Memory.Copy(dataPage, _pager.AcquirePagePointer(_readingPage), (current->PageCount + current->OverflowPageCount) * AbstractPager.PageSize);
			}

			var tempTransactionPageTranslaction = new Dictionary<long, RecoveryPagePosition>();

			for (var i = 0; i < current->PageCount; i++)
			{
				Debug.Assert(_pager.Disposed == false);
				Debug.Assert(_recoveryPager.Disposed == false);

				var page = _recoveryPager.Read(_recoveryPage);

				var pagePosition = new RecoveryPagePosition
				{
					JournalPos = _recoveryPage,
					TransactionId = current->TransactionId
				};

				if (page.IsOverflow)
				{
					var numOfPages = _recoveryPager.GetNumberOfOverflowPages(page.OverflowSize);

					pagePosition.IsOverflow = true;
					pagePosition.NumberOfOverflowPages = numOfPages;

					_recoveryPage += numOfPages;
				}
				else
				{
					_recoveryPage++;
				}

				tempTransactionPageTranslaction[page.PageNumber] = pagePosition;
			}

			_readingPage += transactionSize;

			LastTransactionHeader = current;

			foreach (var pagePosition in tempTransactionPageTranslaction)
			{
				_transactionPageTranslation[pagePosition.Key] = pagePosition.Value;

				if (pagePosition.Value.IsOverflow)
				{
					Debug.Assert(pagePosition.Value.NumberOfOverflowPages != -1);

					for (int i = 1; i < pagePosition.Value.NumberOfOverflowPages; i++)
					{
						_transactionPageTranslation.Remove(pagePosition.Key + i);
					}
				}
			}

			return true;
		}

		private unsafe bool TryDecompressTransactionPages(StorageEnvironmentOptions options, TransactionHeader* current, byte* dataPage)
		{
			try
			{
				LZ4.Decode64(_pager.AcquirePagePointer(_readingPage), current->CompressedSize, dataPage, current->UncompressedSize, true);
			}
			catch (Exception e)
			{
				options.InvokeRecoveryError(this, "Could not de-compress, invalid data", e);
				RequireHeaderUpdate = true;

				return false;
			}
			return true;
		}

		internal static int GetNumberOfPagesFromSize(int size)
		{
			return (size / AbstractPager.PageSize) + (size % AbstractPager.PageSize == 0 ? 0 : 1);
		}

		public void RecoverAndValidate(StorageEnvironmentOptions options)
		{
			while (ReadOneTransaction(options))
			{
			}
		}

		public Dictionary<long, RecoveryPagePosition> TransactionPageTranslation
		{
			get { return _transactionPageTranslation; }
		}
		public int RecoveryPage { get { return _recoveryPage; } }

		public void SetStartPage(long value)
		{
			_readingPage = value;
		}

		private bool TryReadAndValidateHeader(StorageEnvironmentOptions options, out TransactionHeader* current)
		{
			current = (TransactionHeader*)_pager.Read(_readingPage).Base;

			if (current->HeaderMarker != Constants.TransactionHeaderMarker)
			{
				// not a transaction page, 

				// if the header marker is zero, we are probably in the area at the end of the log file, and have no additional log records
				// to read from it. This can happen if the next transaction was too big to fit in the current log file. We stop reading
				// this log file and move to the next one. 

				RequireHeaderUpdate = current->HeaderMarker != 0;
				if (RequireHeaderUpdate)
				{
					options.InvokeRecoveryError(this,
						"Transaction " + current->TransactionId +
						" header marker was set to garbage value, file is probably corrupted", null);
				}

				return false;
			}

			ValidateHeader(current, LastTransactionHeader);

			if (current->TxMarker.HasFlag(TransactionMarker.Commit) == false)
			{
				// uncommitted transaction, probably
				RequireHeaderUpdate = true;
				options.InvokeRecoveryError(this,
						"Transaction " + current->TransactionId +
						" was not committed", null);
				return false;
			}

			_readingPage++;
			return true;
		}

		private void ValidateHeader(TransactionHeader* current, TransactionHeader* previous)
		{
			if (current->TransactionId < 0)
				throw new InvalidDataException("Transaction id cannot be less than 0 (Tx: " + current->TransactionId + " )");
			if (current->TxMarker.HasFlag(TransactionMarker.Commit) && current->LastPageNumber < 0)
				throw new InvalidDataException("Last page number after committed transaction must be greater than 0");
			if (current->TxMarker.HasFlag(TransactionMarker.Commit) && current->PageCount > 0 && current->Crc == 0)
				throw new InvalidDataException("Committed and not empty transaction checksum can't be equal to 0");
			if (current->Compressed)
			{
				if (current->CompressedSize <= 0)
					throw new InvalidDataException("Compression error in transaction.");
			}

			if (previous == null)
				return;

			if (current->TransactionId != 1 &&
				// 1 is a first storage transaction which does not increment transaction counter after commit
				current->TransactionId - previous->TransactionId != 1)
				throw new InvalidDataException("Unexpected transaction id. Expected: " + (previous->TransactionId + 1) +
											   ", got:" + current->TransactionId);
		}

		private bool ValidatePagesCrc(StorageEnvironmentOptions options, int compressedPages, TransactionHeader* current)
		{
			uint crc = Crc.Value(_pager.AcquirePagePointer(_readingPage), 0, compressedPages * AbstractPager.PageSize);

			if (crc != current->Crc)
			{
				RequireHeaderUpdate = true;
				options.InvokeRecoveryError(this, "Invalid CRC signature for transaction " + current->TransactionId, null);

				return false;
			}
			return true;
		}

		public override string ToString()
		{
			return _pager.ToString();
		}
	}
}
