using System.Collections.Generic;
using NUnit.Framework;
using PotionCraft.Core;

namespace PotionCraft.Tests.Editor
{
	/// <summary>
	/// Edit-mode tests for the remove/gravity/refill/cascade pipeline added
	/// on top of GridModel and MatchFinder.
	/// </summary>
	[TestFixture]
	public sealed class CascadeTests
	{
		private const int Size = 6;

		[Test]
		public void Test_RemoveMatches_ClearsMatchedCellsAndReturnsCount()
		{
			var grid = new GridModel(Size, Size, 5);
			for (int y = 0; y < Size; y++)
			{
				for (int x = 0; x < Size; x++)
				{
					grid.SetItem(x, y, Item.Empty);
				}
			}

			grid.SetItem(1, 0, Item.Create(ItemColor.Red));
			grid.SetItem(2, 0, Item.Create(ItemColor.Red));
			grid.SetItem(3, 0, Item.Create(ItemColor.Red));

			List<MatchGroup> matches = MatchFinder.FindAllMatches(grid);
			int clearedCount = grid.RemoveMatches(matches);

			Assert.AreEqual(3, clearedCount);
			Assert.AreEqual(Item.Empty, grid.GetItem(1, 0));
			Assert.AreEqual(Item.Empty, grid.GetItem(2, 0));
			Assert.AreEqual(Item.Empty, grid.GetItem(3, 0));
		}

		[Test]
		public void Test_ApplyGravity_CompactsColumnTowardZero()
		{
			var grid = new GridModel(Size, Size, 7);
			for (int y = 0; y < Size; y++)
			{
				for (int x = 0; x < Size; x++)
				{
					grid.SetItem(x, y, Item.Empty);
				}
			}

			grid.SetItem(0, 1, Item.Create(ItemColor.Red));
			grid.SetItem(0, 3, Item.Create(ItemColor.Blue));
			grid.SetItem(0, 5, Item.Create(ItemColor.Green));

			grid.ApplyGravity();

			Assert.AreEqual(ItemColor.Red, grid.GetItem(0, 0).Color);
			Assert.AreEqual(ItemColor.Blue, grid.GetItem(0, 1).Color);
			Assert.AreEqual(ItemColor.Green, grid.GetItem(0, 2).Color);
			Assert.AreEqual(ItemColor.None, grid.GetItem(0, 3).Color);
			Assert.AreEqual(ItemColor.None, grid.GetItem(0, 4).Color);
			Assert.AreEqual(ItemColor.None, grid.GetItem(0, 5).Color);
		}

		[Test]
		public void Test_RefillEmptyCells_FillsEveryEmptyCellWithoutCreatingMatches()
		{
			// A freshly constructed grid starts with every cell defaulted to
			// Item.Empty (ItemColor.None), so this exercises RefillEmptyCells
			// on a fully empty board, just like it would be used after gravity
			// vacates cells during a real cascade.
			var grid = new GridModel(8, 8, 11);

			grid.RefillEmptyCells();

			for (int y = 0; y < grid.Height; y++)
			{
				for (int x = 0; x < grid.Width; x++)
				{
					Assert.AreNotEqual(ItemColor.None, grid.GetItem(x, y).Color);
				}
			}

			List<MatchGroup> matches = MatchFinder.FindAllMatches(grid);
			Assert.AreEqual(0, matches.Count);
		}

		[Test]
		public void Test_ResolveCascade_SettlesBoardWithNoRemainingMatches()
		{
			var grid = new GridModel(8, 8, 99);
			grid.InitializeGrid();

			// Force a deliberate match so ResolveCascade has guaranteed work to
			// do, regardless of what InitializeGrid happened to generate.
			grid.SetItem(0, 0, Item.Create(ItemColor.Red));
			grid.SetItem(1, 0, Item.Create(ItemColor.Red));
			grid.SetItem(2, 0, Item.Create(ItemColor.Red));

			CascadeReport report = grid.ResolveCascade();

			Assert.GreaterOrEqual(report.StepCount, 1);
			Assert.GreaterOrEqual(report.TotalCellsCleared, 3);

			List<MatchGroup> remainingMatches = MatchFinder.FindAllMatches(grid);
			Assert.AreEqual(0, remainingMatches.Count);

			for (int y = 0; y < grid.Height; y++)
			{
				for (int x = 0; x < grid.Width; x++)
				{
					Assert.AreNotEqual(ItemColor.None, grid.GetItem(x, y).Color);
				}
			}
		}
	}
}
