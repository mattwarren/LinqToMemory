﻿using System;
using Voron.Trees;

namespace Voron.Impl
{
	public class StorageEnvironmentState
	{
		public Tree Root { get; set; }
		public Tree FreeSpaceRoot { get; set; }
	    public StorageEnvironmentOptions Options { get; set; }

	    public long NextPageNumber;

		public StorageEnvironmentState() { }

		public StorageEnvironmentState(Tree freeSpaceRoot, Tree root, long nextPageNumber)
		{
			FreeSpaceRoot = freeSpaceRoot;
			Root = root;
			NextPageNumber = nextPageNumber;
		}

		public StorageEnvironmentState Clone(Transaction tx)
		{
			return new StorageEnvironmentState
				{
					Root = Root != null ? Root.Clone(tx) : null,
					FreeSpaceRoot = FreeSpaceRoot != null ? FreeSpaceRoot.Clone(tx) : null,
					NextPageNumber = NextPageNumber,
                    Options = Options
				};
		}

		public Tree GetTree(Transaction tx, string treeName)
		{
			if (String.IsNullOrEmpty(treeName))
				throw new InvalidOperationException("Cannot fetch tree with empty name");

			Tree tree = tx.ReadTree(treeName);

			if (tree != null)
				return tree;

			if (treeName.Equals(Constants.RootTreeName, StringComparison.InvariantCultureIgnoreCase))
				return Root;

			if (treeName.Equals(Constants.FreeSpaceTreeName, StringComparison.InvariantCultureIgnoreCase))
				return FreeSpaceRoot;

			if (tx.Flags == TransactionFlags.ReadWrite)
				return tx.Environment.CreateTree(tx, treeName, Options.ShouldUseKeyPrefix(treeName));

			throw new InvalidOperationException("No such tree: " + treeName);
		}
	}
}
