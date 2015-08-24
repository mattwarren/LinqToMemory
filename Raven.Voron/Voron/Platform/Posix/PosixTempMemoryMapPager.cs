using System;
using System.Runtime.InteropServices;
using System.Threading;
using Mono.Unix.Native;
using Voron.Impl;
using Voron.Impl.Paging;
using Voron.Trees;
using Voron.Util;
using Sparrow;

namespace Voron.Platform.Posix
{
	/// <summary>
	/// In Windows, we use page file based memory mapped file pager.
	/// In Linux, we cannot use the same approach, because using shm_open (similar to anonymous memory mapped file in Windows)
	/// will not actually commit the memory reservation, and it is actually possible that we might allocate memory "successfully"
	/// but only get around to actually using it later. In this case, Linux will realize that it can't meet its promise about using
	/// this memory, and it will throw its hands up in the air and give up. At this point, the OOM killer will get involved and assasinate
	/// us in cold blood, all for believing what the OS said. 
	/// This has to do with overcommit and the process model for Linux requiring duplicate memory at fork().
	/// 
	/// In short, it means that to be reliable, we cannot use swap space in Linux for anything that may be large. We have to create temporary
	/// files for that purpose (so we'll get assured allocation of space on disk, and then be able to mmap them).
	/// 
	/// </summary>
	public unsafe class PosixTempMemoryMapPager : AbstractPager
	{
		private readonly string _file;
		private int _fd;
		public readonly long SysPageSize;
		private long _totalAllocationSize;
		private static int _counter;

		public PosixTempMemoryMapPager(string file, long? initialFileSize = null)
		{
			var instanceId = Interlocked.Increment(ref _counter);
			_file = "/var/tmp/ravendb-" + Syscall.getpid() + "-" + instanceId + "-" + file;
            _fd = Syscall.open(_file, OpenFlags.O_RDWR | OpenFlags.O_CREAT | OpenFlags.O_EXCL, 
                FilePermissions.S_IWUSR | FilePermissions.S_IRUSR);
			if (_fd == -1)
				PosixHelper.ThrowLastError(Marshal.GetLastWin32Error());
		    DeleteOnClose = true;

			SysPageSize = Syscall.sysconf(SysconfName._SC_PAGESIZE);

			_totalAllocationSize = NearestSizeToPageSize(initialFileSize ?? _totalAllocationSize);
			PosixHelper.AllocateFileSpace(_fd, (ulong)_totalAllocationSize);

			NumberOfAllocatedPages = _totalAllocationSize / PageSize;
			PagerState.Release();
			PagerState = CreatePagerState();
		}

		private long NearestSizeToPageSize(long size)
		{
			if (size == 0)
				return SysPageSize * 16;

			var mod = size%SysPageSize;
			if (mod == 0)
			{
				return size;
			}
			return ((size/SysPageSize) + 1)*SysPageSize;
		}

		protected override string GetSourceName()
		{
			return "shm_open mmap: " + _fd + " " + _file;
		}

		public override void AllocateMorePages(Transaction tx, long newLength)
		{
			ThrowObjectDisposedIfNeeded();

			var newLengthAfterAdjustment = NearestSizeToPageSize(newLength);

			if (newLengthAfterAdjustment <= _totalAllocationSize) //nothing to do
				return;

			var allocationSize = newLengthAfterAdjustment - _totalAllocationSize;

            PosixHelper.AllocateFileSpace(_fd, (ulong)(_totalAllocationSize + allocationSize));
			_totalAllocationSize += allocationSize;

			PagerState newPagerState = CreatePagerState();
			if (newPagerState == null)
			{
				var errorMessage = string.Format(
					"Unable to allocate more pages - unsuccessfully tried to allocate continuous block of virtual memory with size = {0:##,###;;0} bytes",
					(_totalAllocationSize + allocationSize));

				throw new OutOfMemoryException(errorMessage);
			}

			newPagerState.DebugVerify(newLengthAfterAdjustment);

			if (tx != null)
			{
				newPagerState.AddRef();
				tx.AddPagerState(newPagerState);
			}

			var tmp = PagerState;
			PagerState = newPagerState;
			tmp.Release(); //replacing the pager state --> so one less reference for it

			NumberOfAllocatedPages = _totalAllocationSize / PageSize;
		}

		private PagerState CreatePagerState()
		{
			var startingBaseAddressPtr = Syscall.mmap(IntPtr.Zero, (ulong)_totalAllocationSize,
			                                          MmapProts.PROT_READ | MmapProts.PROT_WRITE,
			                                          MmapFlags.MAP_SHARED, _fd, 0);

			if (startingBaseAddressPtr.ToInt64() == -1) //system didn't succeed in mapping the address where we wanted
				PosixHelper.ThrowLastError(Marshal.GetLastWin32Error());

			var allocationInfo = new PagerState.AllocationInfo
			{
				BaseAddress = (byte*)startingBaseAddressPtr.ToPointer(),
				Size = _totalAllocationSize,
				MappedFile = null
			};

			var newPager = new PagerState(this)
			{
				Files = null, // unused
				MapBase = allocationInfo.BaseAddress,
				AllocationInfos = new[] { allocationInfo }
			};

			newPager.AddRef(); // one for the pager
			return newPager;
		}

		
		public override byte* AcquirePagePointer(long pageNumber, PagerState pagerState = null)
		{
			ThrowObjectDisposedIfNeeded();
			return (pagerState ?? PagerState).MapBase + (pageNumber * PageSize);
		}

		
		public override  void Sync()
		{
			//nothing to do here
		}


		public override int Write(Page page, long? pageNumber)
		{
			long startPage = pageNumber ?? page.PageNumber;

			int toWrite = page.IsOverflow ? GetNumberOfOverflowPages(page.OverflowSize) : 1;

			return WriteDirect(page, startPage, toWrite);
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
			return _file;
		}

		public override void ReleaseAllocationInfo(byte* baseAddress, long size)
		{
			var result = Syscall.munmap(new IntPtr(baseAddress), (ulong) size);
			if (result == -1)
				PosixHelper.ThrowLastError(Marshal.GetLastWin32Error());
		}

		public override void Dispose ()
		{
			base.Dispose ();
			if (_fd != -1) 
			{
				// note that the orders of operations is important here, we first unlink the file
				// we are supposed to be the only one using it, so Linux would be ready to delete it
				// and hopefully when we close it, won't waste any time trying to sync the memory state
				// to disk just to discard it
				if (DeleteOnClose) {
					Syscall.unlink (_file);
					// explicitly ignoring the result here, there isn't
					// much we can do to recover from being unable to delete it
				}
				Syscall.close (_fd);
				_fd = -1;
			}		
		}
	}
}

