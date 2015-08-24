﻿using Voron.Trees;

namespace Voron.Impl
{
	public unsafe class Constants
	{
        public const ulong MagicMarker = 0xB16BAADC0DEF0015;

        public const ulong TransactionHeaderMarker = 0x1A4C92AD90ABC123; 

		/// <summary>
		/// If there are less than 2 keys in a page, we no longer have a tree
		/// This impacts the MakKeySize available
		/// </summary>
		public const int MinKeysInPage = 2;

		public static readonly int PageHeaderSize = sizeof(PageHeader);

		public static readonly int NodeHeaderSize = sizeof(NodeHeader);

		public static readonly int PrefixNodeHeaderSize = sizeof(PrefixNodeHeader);

		public static readonly int PrefixedSliceHeaderSize = sizeof (PrefixedSliceHeader);

		public static readonly int PrefixInfoSectionSize = sizeof (PrefixInfoSection);

		public static int PageNumberSize = sizeof(long);

		public static int NodeOffsetSize = sizeof(ushort);

		public static ushort SizeOfUInt = sizeof(uint);

		public const int CurrentVersion = 4;

		public const string RootTreeName = "Root";
		public const string FreeSpaceTreeName = "Free Space";

		public const string DatabaseFilename = "Raven.voron";

		public const int DefaultMaxLogLengthBeforeCompaction = 64; //how much entries in log to keep before compacting it into snapshot
	}
}