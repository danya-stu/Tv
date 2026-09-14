using System.Collections.Generic;
using NUnit.Framework;
using PotionCraft.Core;

namespace PotionCraft.Tests.Editor
{
	/// <summary>
	/// Covers the Sprint 5 "Reaction Catalyst" killer feature: big matches
	/// (Line4, Line5Plus, Cross) spawn a catalyst item instead of clearing
	/// one of their cells, and later sweeping up a catalyst in another match
	/// activates its row/column/color-bomb effect, including chains where one
	/// catalyst's blast sweeps up another catalyst.
	/// </summary>
	[TestFixture]
	public class CatalystTests
	{
		private const int Size = 8;

		private static void ClearGrid(GridModel grid)
		{
			for (int y = 0; y < Size; y++)
			{
				for (int x = 0; x < Size; x++)
					grid.SetItem(x, y, Item.Empty);
			}
		}

		[Test]
		public void Test_RemoveMatches_Line4Horizontal_SpawnsRowClearCatalyst()
		{
			var grid = new GridModel(Size, Size, 5);
			ClearGrid(grid);

			grid.SetItem(1, 2, Item.Create(ItemColor.Blue));
			grid.SetItem(2, 2, Item.Create(ItemColor.Blue));
			grid.SetItem(3, 2, Item.Create(ItemColor.Blue));
			grid.SetItem(4, 2, Item.Create(ItemColor.Blue));

			List<MatchGroup> matches = MatchFinder.FindAllMatches(grid);
			Assert.AreEqual(1, matches.Count);
			Assert.AreEqual(MatchShape.Line4, matches[0].Shape);

			int clearedCount = grid.RemoveMatches(matches);

			Assert.AreEqual(4, clearedCount, "All 4 matched cells should count toward the cleared total, even though one becomes a catalyst instead of emptying.");

			int catalystCount = 0;
			for (int x = 0; x < Size; x++)
			{
				Item item = grid.GetItem(x, 2);
				if (item.Catalyst == CatalystType.RowClear)
				{
					catalystCount++;
					Assert.AreEqual(ItemColor.Blue, item.Color);
				}
			}

			Assert.AreEqual(1, catalystCount, "Exactly one RowClear catalyst should be spawned on the matched horizontal row.");
		}

		[Test]
		public void Test_RemoveMatches_Line4Vertical_SpawnsColumnClearCatalyst()
		{
			var grid = new GridModel(Size, Size, 5);
			ClearGrid(grid);

			grid.SetItem(3, 1, Item.Create(ItemColor.Purple));
			grid.SetItem(3, 2, Item.Create(ItemColor.Purple));
			grid.SetItem(3, 3, Item.Create(ItemColor.Purple));
			grid.SetItem(3, 4, Item.Create(ItemColor.Purple));

			List<MatchGroup> matches = MatchFinder.FindAllMatches(grid);
			Assert.AreEqual(1, matches.Count);
			Assert.AreEqual(MatchShape.Line4, matches[0].Shape);

			grid.RemoveMatches(matches);

			int catalystCount = 0;
			for (int y = 1; y <= 4; y++)
			{
				if (grid.GetItem(3, y).Catalyst == CatalystType.ColumnClear)
					catalystCount++;
			}

			Assert.AreEqual(1, catalystCount, "Exactly one ColumnClear catalyst should be spawned on the matched vertical column.");
		}

		[Test]
		public void Test_RemoveMatches_Line5Plus_SpawnsColorBombCatalyst()
		{
			var grid = new GridModel(Size, Size, 5);
			ClearGrid(grid);

			for (int x = 0; x < 5; x++)
				grid.SetItem(x, 3, Item.Create(ItemColor.Green));

			List<MatchGroup> matches = MatchFinder.FindAllMatches(grid);
			Assert.AreEqual(1, matches.Count);
			Assert.AreEqual(MatchShape.Line5Plus, matches[0].Shape);

			grid.RemoveMatches(matches);

			int catalystCount = 0;
			for (int x = 0; x < 5; x++)
			{
				if (grid.GetItem(x, 3).Catalyst == CatalystType.ColorBomb)
					catalystCount++;
			}

			Assert.AreEqual(1, catalystCount, "Exactly one ColorBomb catalyst should be spawned by a 5-in-a-row match.");
		}

		[Test]
		public void Test_RemoveMatches_ActivatingRowClearCatalyst_ClearsEntireRow()
		{
			var grid = new GridModel(Size, Size, 5);
			ClearGrid(grid);

			// Fill row 4 with alternating background colors (Purple/Blue) so no
			// unintended 3-in-a-row forms outside the deliberate Red match
			// below; only the RowClear catalyst's activation should clear
			// these cells, not a coincidental second match.
			ItemColor[] background = { ItemColor.Purple, ItemColor.Blue, ItemColor.None, ItemColor.None, ItemColor.None, ItemColor.Blue, ItemColor.Purple, ItemColor.Blue };
			for (int x = 0; x < Size; x++)
			{
				if (background[x] != ItemColor.None)
					grid.SetItem(x, 4, Item.Create(background[x]));
			}

			// A fresh 3-match sweeps up a pre-placed RowClear catalyst.
			grid.SetItem(2, 4, Item.Create(ItemColor.Red));
			grid.SetItem(3, 4, Item.Create(ItemColor.Red, CatalystType.RowClear));
			grid.SetItem(4, 4, Item.Create(ItemColor.Red));

			List<MatchGroup> matches = MatchFinder.FindAllMatches(grid);
			Assert.AreEqual(1, matches.Count);

			grid.RemoveMatches(matches);

			for (int x = 0; x < Size; x++)
				Assert.AreEqual(ItemColor.None, grid.GetItem(x, 4).Color, $"Cell ({x},4) should have been cleared by the RowClear catalyst activation.");
		}

		[Test]
		public void Test_RemoveMatches_ActivatingColorBombCatalyst_ClearsAllCellsOfItsColor()
		{
			var grid = new GridModel(Size, Size, 5);
			ClearGrid(grid);

			// Scatter Yellow cells across the board that are not part of any
			// match themselves.
			grid.SetItem(0, 0, Item.Create(ItemColor.Yellow));
			grid.SetItem(5, 6, Item.Create(ItemColor.Yellow));
			grid.SetItem(7, 1, Item.Create(ItemColor.Yellow));

			// A 3-match sweeps up a pre-placed Yellow ColorBomb catalyst.
			grid.SetItem(2, 2, Item.Create(ItemColor.Yellow));
			grid.SetItem(3, 2, Item.Create(ItemColor.Yellow, CatalystType.ColorBomb));
			grid.SetItem(4, 2, Item.Create(ItemColor.Yellow));

			// Unrelated color, must survive the color-bomb activation.
			grid.SetItem(6, 6, Item.Create(ItemColor.Green));

			List<MatchGroup> matches = MatchFinder.FindAllMatches(grid);
			Assert.AreEqual(1, matches.Count);

			grid.RemoveMatches(matches);

			Assert.AreEqual(ItemColor.None, grid.GetItem(0, 0).Color);
			Assert.AreEqual(ItemColor.None, grid.GetItem(5, 6).Color);
			Assert.AreEqual(ItemColor.None, grid.GetItem(7, 1).Color);
			Assert.AreEqual(ItemColor.None, grid.GetItem(2, 2).Color);
			Assert.AreEqual(ItemColor.None, grid.GetItem(4, 2).Color);

			Assert.AreEqual(ItemColor.Green, grid.GetItem(6, 6).Color, "Unrelated color must survive the color-bomb activation.");
		}

		[Test]
		public void Test_RemoveMatches_ChainedCatalystActivations_ExpandBothEffects()
		{
			var grid = new GridModel(Size, Size, 5);
			ClearGrid(grid);

			// A RowClear catalyst on row 1 sweeps a ColumnClear catalyst placed
			// elsewhere on that same row; the ColumnClear catalyst should then
			// also activate and clear its entire column.
			grid.SetItem(1, 1, Item.Create(ItemColor.Red));
			grid.SetItem(2, 1, Item.Create(ItemColor.Red, CatalystType.RowClear));
			grid.SetItem(3, 1, Item.Create(ItemColor.Red));

			grid.SetItem(6, 1, Item.Create(ItemColor.Blue, CatalystType.ColumnClear));
			grid.SetItem(6, 5, Item.Create(ItemColor.Purple));

			List<MatchGroup> matches = MatchFinder.FindAllMatches(grid);
			Assert.AreEqual(1, matches.Count);

			grid.RemoveMatches(matches);

			Assert.AreEqual(ItemColor.None, grid.GetItem(6, 1).Color, "The column-clear catalyst swept up by the row clear should itself be cleared.");
			Assert.AreEqual(ItemColor.None, grid.GetItem(6, 5).Color, "The chained column-clear activation should clear its full column.");
		}
	}
}
