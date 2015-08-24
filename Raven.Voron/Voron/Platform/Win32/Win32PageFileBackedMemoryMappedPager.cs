﻿using Sparrow;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO.MemoryMappedFiles;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using Voron.Impl;
using Voron.Impl.Paging;
using Voron.Trees;
using Voron.Util;

namespace Voron.Platform.Win32
{
	public unsafe class Win32PageFileBackedMemoryMappedPager : AbstractPager
	{
		private readonly string _name;
		public readonly long AllocationGranularity;
		private long _totalAllocationSize;
		private const int MaxAllocationRetries = 100;
	    private static int _counter;
	    private readonly int _instanceId;


	    public Win32PageFileBackedMemoryMappedPager(string name, long? initialFileSize = null)
		{
		    _name = name;
		    Win32NativeMethods.SYSTEM_INFO systemInfo;
			Win32NativeMethods.GetSystemInfo(out systemInfo);

			AllocationGranularity = systemInfo.allocationGranularity;
			_totalAllocationSize = initialFileSize.HasValue ? NearestSizeToAllocationGranularity(initialFileSize.Value) : systemInfo.allocationGranularity;

		    _instanceId = Interlocked.Increment(ref _counter);

			PagerState.Release();
			Debug.Assert(AllocationGranularity % PageSize == 0);
			NumberOfAllocatedPages = _totalAllocationSize / PageSize;
			PagerState = CreateInitialPagerState(_totalAllocationSize);
		}

		protected override string GetSourceName()
		{
			return "MemMapInSystemPage: " +  _name  + " " + _instanceId + ", Size : " + _totalAllocationSize;
		}

		public override byte* AcquirePagePointer(long pageNumber, PagerState pagerState = null)
		{
		    return (pagerState ?? PagerState).MapBase + (pageNumber * PageSize);
		}

		public override void Sync()
		{
			// nothing to do here, we are already synced to memory, and we 
            // don't go anywhere
		}

		public override int Write(Page page, long? pageNumber)
		{
			long startPage = pageNumber ?? page.PageNumber;

			//note: GetNumberOfOverflowPages and WriteDirect can throw ObjectDisposedException if the pager is already disposed
			int toWrite = page.IsOverflow ? GetNumberOfOverflowPages(page.OverflowSize) : 1;

			return WriteDirect(page, startPage, toWrite);
		}

		public override void AllocateMorePages(Transaction tx, long newLength)
		{
			ThrowObjectDisposedIfNeeded();
			var newLengthAfterAdjustment = NearestSizeToAllocationGranularity(newLength);

			if (newLengthAfterAdjustment <= _totalAllocationSize)
				return;

			var allocationSize = newLengthAfterAdjustment - _totalAllocationSize;

		    if (TryAllocateMoreContinuousPages(allocationSize) == false)
		    {
		        var newPagerState = AllocateMorePagesAndRemapContinuously(allocationSize);
		        if (newPagerState == null)
		        {
		            var errorMessage = string.Format(
		                "Unable to allocate more pages - unsuccessfully tried to allocate continuous block of virtual memory with size = {0:##,###;;0} bytes",
		                (_totalAllocationSize + allocationSize));

		            throw new OutOfMemoryException(errorMessage);
		        }
                newPagerState.DebugVerify(newLengthAfterAdjustment);

		        newPagerState.AddRef();
		        if (tx != null)
		        {
		            newPagerState.AddRef();
		            tx.AddPagerState(newPagerState);
		        }
                // we always share the same memory mapped files references between all pages, since to close them 
                // would be to lose all the memory associated with them
		        PagerState.DisposeFilesOnDispose = false;
		        var tmp = PagerState;
                PagerState = newPagerState;
                tmp.Release(); //replacing the pager state --> so one less reference for it
		    }

		    _totalAllocationSize += allocationSize;
            NumberOfAllocatedPages = _totalAllocationSize / PageSize;
		}

	
		public override int WriteDirect(Page start, long pagePosition, int pagesToWrite)
		{
			ThrowObjectDisposedIfNeeded();

			int toCopy = pagesToWrite * PageSize;
            Memory.BulkCopy(PagerState.MapBase + pagePosition * PageSize, start.Base, toCopy);

			return toCopy;
		}

		public override string ToString()
		{
			return GetSourceName() + ", Length: " + _totalAllocationSize;
		}

		private PagerState AllocateMorePagesAndRemapContinuously(long allocationSize)
		{
			var retryCount = 0;

			while (retryCount++ < MaxAllocationRetries)
			{
				byte* newBaseAddress;
				if (TryFindContinuousMemory((ulong)(_totalAllocationSize + allocationSize), out newBaseAddress) == false)
				{
					var message =
						string.Format(
							"Unable to allocate more pages - unsuccessfully tried to allocate continuous block of size = {0} bytes\r\n" +
							"It is likely that we are suffering from virtual memory exhaustion or memory fragmentation.\r\n" +
							"64 bits process: {1}\r\n" +
							"If you are running in 32 bits, this is expected, and you need to run in 64 bits to resume normal operations.\r\n" +
							"If you are running in 64 bits, this is likely an error and should be reported."
							, (_totalAllocationSize + allocationSize), Environment.Is64BitProcess);
					throw new OutOfMemoryException(message);
				}

				bool failedToAllocate = false;
				long offset = 0;
				var allocationInfoAfterReallocation = new List<PagerState.AllocationInfo>();
				foreach (var allocationInfo in PagerState.AllocationInfos)
				{
					var newAlloctedBaseAddress = Win32MemoryMapNativeMethods.MapViewOfFileEx(allocationInfo.MappedFile.SafeMemoryMappedFileHandle.DangerousGetHandle(),
					                                                                         Win32MemoryMapNativeMethods.NativeFileMapAccessType.Read | 
					                                                                         Win32MemoryMapNativeMethods.NativeFileMapAccessType.Write,
						0, 0,
						UIntPtr.Zero, 
						newBaseAddress + offset);

					if (newAlloctedBaseAddress == null || newAlloctedBaseAddress == (byte*)0)
					{
						Debug.WriteLine("Failed to remap file continuously. Unmapping already mapped files and re-trying");
						UndoMappings(allocationInfoAfterReallocation);
						failedToAllocate = true;
						break;
					}

					offset += allocationInfo.Size;
					allocationInfoAfterReallocation.Add(new PagerState.AllocationInfo
					{
						BaseAddress = newAlloctedBaseAddress,
						MappedFile = allocationInfo.MappedFile,
						Size = allocationInfo.Size
					});
				}

			    if (failedToAllocate) 
                    continue;

                var newAllocationInfo = TryCreateNewFileMappingAtAddress(allocationSize, newBaseAddress + _totalAllocationSize);
                if (newAllocationInfo == null)
			    {
                    UndoMappings(allocationInfoAfterReallocation); 
                    continue;
			    }

			    var newPagerState = new PagerState(this)
			    {
                    Files = PagerState.Files.Concat(newAllocationInfo.MappedFile),
                    AllocationInfos = allocationInfoAfterReallocation.Concat(newAllocationInfo),
                    MapBase = newBaseAddress
			    };
			    return newPagerState;
			}

		    throw new InvalidOperationException(
		        string.Format(
		            "Something bad has happened, after {0} tries, could not find any spot in virtual memory to remap continuous virtual memory for {1:##,###;;0} bytes",
		            MaxAllocationRetries, allocationSize));
		}

		private static void UndoMappings(IEnumerable<PagerState.AllocationInfo> newAllocationInfos)
		{
			foreach (var newAllocationInfo in newAllocationInfos)
				Win32MemoryMapNativeMethods.UnmapViewOfFile(newAllocationInfo.BaseAddress);
		}

		private bool TryAllocateMoreContinuousPages(long allocationSize)
		{
			Debug.Assert(PagerState != null);
			Debug.Assert(PagerState.Files != null && PagerState.Files.Any());

            var allocationInfo = TryCreateNewFileMappingAtAddress(allocationSize, PagerState.MapBase + _totalAllocationSize);

			if (allocationInfo == null)
				return false;

			PagerState.Files = PagerState.Files.Concat(allocationInfo.MappedFile);
            PagerState.AllocationInfos = PagerState.AllocationInfos.Concat(allocationInfo);

			return true;
		}

		private PagerState.AllocationInfo TryCreateNewFileMappingAtAddress(long allocationSize, byte* baseAddress)
		{
			var newMemoryMappedFile = MemoryMappedFile.CreateNew(null, allocationSize);
			var newFileMappingHandle = newMemoryMappedFile.SafeMemoryMappedFileHandle.DangerousGetHandle();
			var newMappingBaseAddress = Win32MemoryMapNativeMethods.MapViewOfFileEx(newFileMappingHandle,
			                                                                        Win32MemoryMapNativeMethods.NativeFileMapAccessType.Read | 
			                                                                        Win32MemoryMapNativeMethods.NativeFileMapAccessType.Write,
				0, 0,
				UIntPtr.Zero,
				baseAddress);

			var hasMappingSucceeded = newMappingBaseAddress != null && newMappingBaseAddress != (byte*)0;
			if (!hasMappingSucceeded)
			{
				newMemoryMappedFile.Dispose();
				return null;
			}

			return new PagerState.AllocationInfo
			{
				BaseAddress = newMappingBaseAddress,
				Size = allocationSize,
				MappedFile = newMemoryMappedFile
			};
		}


		private bool TryFindContinuousMemory(ulong size, out byte* foundAddressPtr)
		{
			foundAddressPtr = null;
			try
			{
				foundAddressPtr = Win32NativeMethods.VirtualAlloc(null, new UIntPtr(size), Win32NativeMethods.AllocationType.RESERVE,
				                                                  Win32NativeMethods.MemoryProtection.READWRITE);

				return (foundAddressPtr != null && foundAddressPtr != (byte*)0);
			}
			finally
			{
				if (foundAddressPtr != null && foundAddressPtr != (byte*)0)
					Win32NativeMethods.VirtualFree(foundAddressPtr, UIntPtr.Zero, Win32NativeMethods.FreeType.MEM_RELEASE);
			}
		}
		
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		private long NearestSizeToAllocationGranularity(long size)
		{
		    var modulos = size % AllocationGranularity;
		    if (modulos == 0)
				return Math.Max(size, AllocationGranularity);

		    return ((size/AllocationGranularity) + 1)*AllocationGranularity;
		}

		private PagerState CreateInitialPagerState(long size)
		{
			var allocationSize = NearestSizeToAllocationGranularity(size);
			var mmf = MemoryMappedFile.CreateNew(null, allocationSize, MemoryMappedFileAccess.ReadWrite);

			var fileMappingHandle = mmf.SafeMemoryMappedFileHandle.DangerousGetHandle();

			var startingBaseAddressPtr = Win32MemoryMapNativeMethods.MapViewOfFileEx(fileMappingHandle,
			                                                                         Win32MemoryMapNativeMethods.NativeFileMapAccessType.Read | 
			                                                                         Win32MemoryMapNativeMethods.NativeFileMapAccessType.Write,
				0, 0,
				UIntPtr.Zero, //map all what was "reserved" in CreateFileMapping on previous row
				null);

			if (startingBaseAddressPtr == (byte*)0) //system didn't succeed in mapping the address where we wanted
				throw new Win32Exception();

			var allocationInfo = new PagerState.AllocationInfo
			{
				BaseAddress = startingBaseAddressPtr,
				Size = allocationSize,
				MappedFile = mmf
			};

			var newPager = new PagerState(this)
			{
				Files = new[] { mmf },
				MapBase = startingBaseAddressPtr,
				AllocationInfos = new[] { allocationInfo }
			};

			newPager.AddRef();

			return newPager;
		}

		public override void ReleaseAllocationInfo(byte* baseAddress, long size)
		{
			if(Win32MemoryMapNativeMethods.UnmapViewOfFile(baseAddress) ==false)
				throw new Win32Exception();
		}
	}
}
