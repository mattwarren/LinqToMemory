﻿using System.IO;
using Xunit;

namespace Voron.Tests.Trees
{
	public class ItemCount : StorageTest
	{
		[PrefixesFact]
		public void ItemCountIsConsistentWithAdditionsAndRemovals()
		{
			using (var tx = Env.NewTransaction(TransactionFlags.ReadWrite))
			{
				for (int i = 0; i < 80; ++i)
				{
					tx.State.Root.Add(string.Format("{0}1", i), new MemoryStream(new byte[1472]));
					tx.State.Root.Add(string.Format("{0}2", i), new MemoryStream(new byte[992]));
					tx.State.Root.Add(string.Format("{0}3", i), new MemoryStream(new byte[1632]));
					tx.State.Root.Add(string.Format("{0}4", i), new MemoryStream(new byte[632]));
					tx.State.Root.Add(string.Format("{0}5", i), new MemoryStream(new byte[824]));
					tx.State.Root.Add(string.Format("{0}6", i), new MemoryStream(new byte[1096]));
					tx.State.Root.Add(string.Format("{0}7", i), new MemoryStream(new byte[2048]));
					tx.State.Root.Add(string.Format("{0}8", i), new MemoryStream(new byte[1228]));
					tx.State.Root.Add(string.Format("{0}9", i), new MemoryStream(new byte[8192]));

					Assert.Equal(tx.State.Root.State.EntriesCount, 9 * (i + 1));
				}

				//RenderAndShow(tx, 1);

				for (int i = 79; i >= 0; --i)
				{
					tx.State.Root.Delete(string.Format("{0}1", i));
					tx.State.Root.Delete(string.Format("{0}2", i));
					tx.State.Root.Delete(string.Format("{0}3", i));
					tx.State.Root.Delete(string.Format("{0}4", i));
					tx.State.Root.Delete(string.Format("{0}5", i));
					tx.State.Root.Delete(string.Format("{0}6", i));
					tx.State.Root.Delete(string.Format("{0}7", i));
					tx.State.Root.Delete(string.Format("{0}8", i));
					tx.State.Root.Delete(string.Format("{0}9", i));

					Assert.Equal(tx.State.Root.State.EntriesCount, 9 * i);
				}

				tx.Commit();
			}
		}

		[PrefixesFact]
		public void ItemCountIsConsistentWithUpdates()
		{
			using (var tx = Env.NewTransaction(TransactionFlags.ReadWrite))
			{
				for (int i = 0; i < 80; ++i)
				{
					tx.State.Root.Add(string.Format("{0}1", i), new MemoryStream(new byte[1472]));
					tx.State.Root.Add(string.Format("{0}2", i), new MemoryStream(new byte[992]));
					tx.State.Root.Add(string.Format("{0}3", i), new MemoryStream(new byte[1632]));
					tx.State.Root.Add(string.Format("{0}4", i), new MemoryStream(new byte[632]));
					tx.State.Root.Add(string.Format("{0}5", i), new MemoryStream(new byte[824]));
					tx.State.Root.Add(string.Format("{0}6", i), new MemoryStream(new byte[1096]));
					tx.State.Root.Add(string.Format("{0}7", i), new MemoryStream(new byte[2048]));
					tx.State.Root.Add(string.Format("{0}8", i), new MemoryStream(new byte[1228]));
					tx.State.Root.Add(string.Format("{0}9", i), new MemoryStream(new byte[8192]));

					Assert.Equal(tx.State.Root.State.EntriesCount, 9 * (i + 1));
				}

				//RenderAndShow(tx, 1);

				for (int i = 0; i < 80; ++i)
				{
					tx.State.Root.Add(string.Format("{0}9", i), new MemoryStream(new byte[1472]));
					tx.State.Root.Add(string.Format("{0}8", i), new MemoryStream(new byte[992]));
					tx.State.Root.Add(string.Format("{0}7", i), new MemoryStream(new byte[1632]));
					tx.State.Root.Add(string.Format("{0}6", i), new MemoryStream(new byte[632]));
					tx.State.Root.Add(string.Format("{0}5", i), new MemoryStream(new byte[824]));
					tx.State.Root.Add(string.Format("{0}4", i), new MemoryStream(new byte[1096]));
					tx.State.Root.Add(string.Format("{0}3", i), new MemoryStream(new byte[2048]));
					tx.State.Root.Add(string.Format("{0}2", i), new MemoryStream(new byte[1228]));
					tx.State.Root.Add(string.Format("{0}1", i), new MemoryStream(new byte[8192]));

					Assert.Equal(tx.State.Root.State.EntriesCount, 9 * 80);
				}

				tx.Commit();
			}
		}
	}
}
