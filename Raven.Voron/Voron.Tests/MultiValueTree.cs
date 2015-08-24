﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Voron.Impl;
using Voron.Trees;
using Xunit;

namespace Voron.Tests
{
	public unsafe class MultiValueTree : StorageTest
	{
		[PrefixesFact]
		public void Single_MultiAdd_And_Read_DataStored()
		{
			var random = new Random();
			var buffer = new byte[1000];
			random.NextBytes(buffer);

			using (var tx = Env.NewTransaction(TransactionFlags.ReadWrite))
			{
				Env.CreateTree(tx, "foo");

				tx.Commit();
			}

			using (var tx = Env.NewTransaction(TransactionFlags.ReadWrite))
			{
				tx.Environment.State.GetTree(tx,"foo").MultiAdd("ChildTreeKey", new Slice(buffer));
				tx.Commit();
			}

			using (var tx = Env.NewTransaction(TransactionFlags.Read))
			{
				using (var fetchedDataIterator = tx.Environment.State.GetTree(tx,"foo").MultiRead("ChildTreeKey"))
				{
					fetchedDataIterator.Seek(Slice.BeforeAllKeys);

					Assert.True(fetchedDataIterator.CurrentKey.Compare(new Slice(buffer)) == 0);
				}
			}
		}

		[PrefixesFact]
		public void MultiDelete_Remains_One_Entry_The_Data_Is_Retrieved_With_MultiRead()
		{
		    const int INPUT_COUNT = 3;
		    const int INPUT_DATA_SIZE = 1000;
		    const string CHILDTREE_KEY = "ChildTree";

		    var inputData = new List<string>();
		    for (int i = 0; i < INPUT_COUNT; i++)
		    {
		        inputData.Add(RandomString(INPUT_DATA_SIZE));
		    }

		    using (var tx = Env.NewTransaction(TransactionFlags.ReadWrite))
		    {
		        for (int i = 0; i < INPUT_COUNT; i++)
		        {
		            tx.State.Root.MultiAdd(CHILDTREE_KEY, inputData[i]);
		        }
		        tx.Commit();
		    }

		    using (var tx = Env.NewTransaction(TransactionFlags.ReadWrite))
		    {
		        for (int i = 0; i < INPUT_COUNT - 1; i++)
		        {
		            tx.State.Root.MultiDelete(CHILDTREE_KEY, inputData[i]);
		            inputData.Remove(inputData[i]);
		        }
		        tx.Commit();
		    }
		    
            ValidateInputExistence(inputData, CHILDTREE_KEY, INPUT_DATA_SIZE, Constants.RootTreeName);
		}

	    [PrefixesFact]
		public void MultiDelete_Remains_No_Entries_ChildTreeKey_Doesnt_Exist()
		{
			const int INPUT_COUNT = 3;
			const int INPUT_DATA_SIZE = 1000;
			const string CHILDTREE_KEY = "ChildTree";

			var inputData = new List<string>();
			for (int i = 0; i < INPUT_COUNT; i++)
			{
				inputData.Add(RandomString(INPUT_DATA_SIZE));
			}

			using (var tx = Env.NewTransaction(TransactionFlags.ReadWrite))
			{
				for (int i = 0; i < INPUT_COUNT; i++)
				{
                    tx.State.Root.MultiAdd(CHILDTREE_KEY, inputData[i]);
				}
				tx.Commit();
			}

			using (var tx = Env.NewTransaction(TransactionFlags.ReadWrite))
			{
				for (int i = 0; i < INPUT_COUNT; i++)
				{
                    tx.State.Root.MultiDelete(CHILDTREE_KEY, inputData[i]);
				}
				tx.Commit();
			}

			using (var tx = Env.NewTransaction(TransactionFlags.Read))
			{
				var iterator = tx.State.Root.MultiRead(CHILDTREE_KEY);
				iterator.Seek(Slice.BeforeAllKeys);
				Assert.False(iterator.MoveNext());
			}
		}

		[PrefixesFact]
		public void Single_MultiAdd_And_Single_MultiDelete_DataDeleted()
		{
			var random = new Random();
			var buffer = new byte[1000];
			random.NextBytes(buffer);

			using (var tx = Env.NewTransaction(TransactionFlags.ReadWrite))
			{
				Env.CreateTree(tx, "foo");
				tx.Commit();
			}

			using (var tx = Env.NewTransaction(TransactionFlags.ReadWrite))
			{
				tx.Environment.State.GetTree(tx,"foo").MultiAdd("ChildTreeKey", new Slice(buffer));
				tx.Commit();
			}

			using (var tx = Env.NewTransaction(TransactionFlags.ReadWrite))
			{
				tx.Environment.State.GetTree(tx,"foo").MultiDelete("ChildTreeKey", new Slice(buffer));
				tx.Commit();
			}

			using (var tx = Env.NewTransaction(TransactionFlags.Read))
			{
				Assert.Equal(typeof(EmptyIterator), tx.Environment.State.GetTree(tx,"foo").MultiRead("ChildTreeKey").GetType());
			}
		}

		[PrefixesFact]
		public void Multiple_MultiAdd_And_MultiDelete_InTheSame_Transaction_EntryDeleted()
		{
		    const int INPUT_COUNT = 25;
		    const int INPUT_DATA_SIZE = 1000;
		    const string CHILDTREE_KEY = "ChildTree";

		    var inputData = new List<string>();
		    for (int i = 0; i < INPUT_COUNT; i++)
		    {
		        inputData.Add(RandomString(INPUT_DATA_SIZE));
		    }

		    var indexToDelete = new Random(1234).Next(0, INPUT_COUNT - 1);
		    using (var tx = Env.NewTransaction(TransactionFlags.ReadWrite))
		    {
		        for (int i = 0; i < INPUT_COUNT; i++)
		        {
		            tx.State.Root.MultiAdd(CHILDTREE_KEY, inputData[i]);
		        }

		        tx.State.Root.MultiDelete(CHILDTREE_KEY, inputData[indexToDelete]);
		        tx.Commit();
		    }

		    inputData.Remove(inputData[indexToDelete]);
		    ValidateInputExistence(inputData, CHILDTREE_KEY, INPUT_DATA_SIZE, Constants.RootTreeName);
		}

	    [PrefixesFact]
		public void NamedTree_Multiple_MultiAdd_And_MultiDelete_InTheSame_Transaction_EntryDeleted()
		{
			const int INPUT_COUNT = 25;
			const int INPUT_DATA_SIZE = 1000;
			const string CHILDTREE_KEY = "ChildTree";

			var inputData = new List<string>();
			for (int i = 0; i < INPUT_COUNT; i++)
			{
				inputData.Add(RandomString(INPUT_DATA_SIZE));
			}

			using (var tx = Env.NewTransaction(TransactionFlags.ReadWrite))
			{
				Env.CreateTree(tx, "foo");
				tx.Commit();
			}

			var indexToDelete = new Random(1234).Next(0, INPUT_COUNT - 1);
			using (var tx = Env.NewTransaction(TransactionFlags.ReadWrite))
			{
				for (int i = 0; i < INPUT_COUNT; i++)
				{
					tx.Environment.State.GetTree(tx,"foo").MultiAdd(CHILDTREE_KEY, inputData[i]);
				}

				tx.Environment.State.GetTree(tx,"foo").MultiDelete(CHILDTREE_KEY, inputData[indexToDelete]);
				tx.Commit();
			}

			inputData.Remove(inputData[indexToDelete]);

			ValidateInputExistence(inputData, CHILDTREE_KEY, INPUT_DATA_SIZE, "foo");
		}

		[PrefixesFact]
		public void NamedTree_Multiple_MultiAdd_MultiDelete_Once_And_Read_EntryDeleted()
		{
			const int INPUT_COUNT = 25;
			const int INPUT_DATA_SIZE = 1000;
			const string CHILDTREE_KEY = "ChildTree";

			var inputData = new List<string>();
			for (int i = 0; i < INPUT_COUNT; i++)
			{
				inputData.Add(RandomString(INPUT_DATA_SIZE));
			}

			using (var tx = Env.NewTransaction(TransactionFlags.ReadWrite))
			{
				Env.CreateTree(tx, "foo");
				tx.Commit();
			}

			using (var tx = Env.NewTransaction(TransactionFlags.ReadWrite))
			{
				for (int i = 0; i < INPUT_COUNT; i++)
				{
					tx.Environment.State.GetTree(tx,"foo").MultiAdd(CHILDTREE_KEY, inputData[i]);
				}
				tx.Commit();
			}

			var indexToDelete = new Random(1234).Next(0, INPUT_COUNT - 1);

			using (var tx = Env.NewTransaction(TransactionFlags.ReadWrite))
			{
				tx.Environment.State.GetTree(tx,"foo").MultiDelete(CHILDTREE_KEY, inputData[indexToDelete]);
				tx.Commit();
			}

			inputData.Remove(inputData[indexToDelete]);

            ValidateInputExistence(inputData, CHILDTREE_KEY, INPUT_DATA_SIZE, "foo");
		}

		[PrefixesFact]
		public void MultiAdd_Twice_TheSame_KeyValue_MultiDelete_NotThrowsException_MultiTree_Deleted()
		{
			const string CHILDTREE_KEY = "ChildTree";
			const string CHILDTREE_VALUE = "Foo";
			using (var tx = Env.NewTransaction(TransactionFlags.ReadWrite))
			{
				tx.State.Root.MultiAdd(CHILDTREE_KEY, CHILDTREE_VALUE);
				tx.State.Root.MultiAdd(CHILDTREE_KEY, CHILDTREE_VALUE);
				tx.Commit();
			}

			using (var tx = Env.NewTransaction(TransactionFlags.ReadWrite))
			{
				Assert.DoesNotThrow(() => tx.State.Root.MultiDelete(CHILDTREE_KEY, CHILDTREE_VALUE));
				tx.Commit();
			}

			using (var tx = Env.NewTransaction(TransactionFlags.Read))
			{
				Assert.Equal(0, tx.State.Root.ReadVersion(CHILDTREE_KEY));
			}
		}
		
		[PrefixesFact]
		public void Multiple_MultiAdd_MultiDelete_Once_And_Read_EntryDeleted()
		{
			const int INPUT_COUNT = 25;
			const int INPUT_DATA_SIZE = 1000;
			const string CHILDTREE_KEY = "ChildTree";

			var inputData = new List<string>();
			for (int i = 0; i < INPUT_COUNT; i++)
			{
				inputData.Add(RandomString(INPUT_DATA_SIZE));
			}

			using (var tx = Env.NewTransaction(TransactionFlags.ReadWrite))
			{
				for (int i = 0; i < INPUT_COUNT; i++)
				{
					tx.State.Root.MultiAdd(CHILDTREE_KEY, inputData[i]);
				}
				tx.Commit();
			}

            ValidateInputExistence(inputData.ToList(), CHILDTREE_KEY, INPUT_DATA_SIZE, Constants.RootTreeName);

			var indexToDelete = new Random(1234).Next(0, INPUT_COUNT - 1);

			using (var tx = Env.NewTransaction(TransactionFlags.ReadWrite))
			{
				tx.State.Root.MultiDelete(CHILDTREE_KEY, inputData[indexToDelete]);
				tx.Commit();
			}

			inputData.Remove(inputData[indexToDelete]);

            ValidateInputExistence(inputData, CHILDTREE_KEY, INPUT_DATA_SIZE, Constants.RootTreeName);
		}

		[PrefixesFact]
		public void Multiple_MultiAdd_And_Read_DataStored()
		{
			const int INPUT_COUNT = 3;
			const int INPUT_DATA_SIZE = 1000;
			const string CHILDTREE_KEY = "ChildTree";

			var inputData = new List<string>();
			for (int i = 0; i < INPUT_COUNT; i++)
			{
				inputData.Add(RandomString(INPUT_DATA_SIZE));
			}

			using (var tx = Env.NewTransaction(TransactionFlags.ReadWrite))
			{
				for (int i = 0; i < INPUT_COUNT; i++)
				{
					tx.State.Root.MultiAdd(CHILDTREE_KEY, inputData[i]);
				}
				tx.Commit();
			}

            ValidateInputExistence(inputData, CHILDTREE_KEY, INPUT_DATA_SIZE, Constants.RootTreeName);
		}

		private void ValidateInputExistence(List<string> inputData, string childtreeKey, int inputDataSize, string treeName)
		{
			using (var tx = Env.NewTransaction(TransactionFlags.Read))
			{
			    var targetTree = Env.State.GetTree(tx, treeName);

				int fetchedEntryCount = 0;
				var inputEntryCount = inputData.Count;
				using (var fetchedDataIterator = targetTree.MultiRead(childtreeKey))
				{
					fetchedDataIterator.Seek(Slice.BeforeAllKeys);
					do
					{
						Assert.Equal(inputDataSize, fetchedDataIterator.CurrentKey.Size);

						var value = fetchedDataIterator.CurrentKey.ToString();
						Assert.True(inputData.Contains(value));
						inputData.Remove(value);
						fetchedEntryCount++;
					} while (fetchedDataIterator.MoveNext());

					Assert.Equal(inputEntryCount, fetchedEntryCount);
					Assert.Empty(inputData);
				}
			}
		}

		private readonly Random _rng = new Random(123746);
		private const string Chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz";

		private string RandomString(int size)
		{
			var buffer = new char[size];

			for (int i = 0; i < size; i++)
			{
				buffer[i] = Chars[_rng.Next(Chars.Length)];
			}
			return new string(buffer);
		}

	}
}
