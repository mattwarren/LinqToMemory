﻿using Sparrow;
using System;
using System.Diagnostics;
using System.IO;
using Voron.Impl.Paging;
using Voron.Util;

namespace Voron.Impl.Journal
{
	public unsafe class TransactionToShip
	{
		private byte[] _copiedPages;
		public TransactionHeader Header { get; private set; }

		public byte[] PagesSnapshot
		{
			get
			{
				if(_copiedPages == null)
					CreatePagesSnapshot();
				
				return _copiedPages;
			}
		}
		public IntPtr[] CompressedPages { get; set; }

		public TransactionToShip(TransactionHeader header)
		{
			Header = header;
		}

		public void CreatePagesSnapshot()
	    {
			_copiedPages = new byte[CompressedPages.Length * AbstractPager.PageSize];
	        fixed (byte* p = PagesSnapshot)
	        {
				for (int i = 0; i < CompressedPages.Length; i++)
	            {
					Memory.Copy(p + (i * AbstractPager.PageSize), (byte*)CompressedPages[i].ToPointer(), AbstractPager.PageSize);
	            }
	        }
	    }
	}
}