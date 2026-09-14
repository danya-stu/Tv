using System.Collections.Generic;
using NUnit.Framework;
using PotionCraft.Core;

namespace PotionCraft.Tests.Editor
{
	/// <summary>
	/// Unit tests for MatchFinder. Boards are built cell-by-cell on a
	/// period-5 diagonal background (color(x, y) = palette[(x + y) % 5]),
	/// which is provably match-free on its own (any 3 consecutive cells in a
	/// row or column are always 3 distinct colors since the period exceeds
	/// the run length), so every match these tests observe comes only from
	/// the specific cells each test deliberately overrides.
	/// </summary>
	[TestFixture]
	public sealed class MatchFinderTests
	{
		private const int Size = 6;

		private static readonly ItemColor[] Palette =
		{
			ItemColor.Red, ItemColor.Blue, ItemColor.Green, ItemColor.Yellow, ItemColor.Purple
		};

		private static GridModel BuildBackgroundGrid()
		{
			var grid = new GridModel(Size, Size, 1);
			for (int y = 0; y < Size; y++)
			{
				for (int x = 0; x < Size; x++)
				{
					grid.SetItem(x, y, Item.Create(Palette[(x + y) % 5]));
				}
			}

			return grid;
		}

		[Test]
		public void Test_BackgroundGrid_HasNoMatches()
		{
			// Sanity check for the shared test fixture itself: if this ever
			// fails, every other test in this file is built on a false premise
			// and must be re-derived.
			GridModel grid = BuildBackgroundGrid();

			List<MatchGroup> matches = MatchFinder.FindAllMatches(grid);

			Assert.AreEqual(0, matches.Count);
		}

		[Test]
		public void Test_FindAllMatches_DetectsHorizontalLine3()
		{
			GridModel grid = BuildBackgroundGrid();
			grid.SetItem(1, 0, Item.Create(ItemColor.Blue));
			grid.SetItem(2, 0, Item.Create(ItemColor.Blue));
			grid.SetItem(3, 0, Item.Create(ItemColor.Blue));

			List<MatchGroup> matches = MatchFinder.FindAllMatches(grid);

			Assert.AreEqual(1, matches.Count);
			Assert.AreEqual(ItemColor.Blue, matches[0].Color);
			Assert.AreEqual(MatchShape.Line3, matches[0].Shape);
			Assert.AreEqual(3, matches[0].Cells.Count);
		}

		[Test]
		public void Test_FindAllMatches_DetectsVerticalLine4()
		{
			GridModel grid = BuildBackgroundGrid();
			grid.SetItem(0, 1, Item.Create(ItemColor.Green));
			grid.SetItem(0, 2, Item.Create(ItemColor.Green));
			grid.SetItem(0, 3, Item.Create(ItemColor.Green));
			grid.SetItem(0, 4, Item.Create(ItemColor.Green));

			List<MatchGroup> matches = MatchFinder.FindAllMatches(grid);

			Assert.AreEqual(1, matches.Count);
			Assert.AreEqual(ItemColor.Green, matches[0].Color);
			Assert.AreEqual(MatchShape.Line4, matches[0].Shape);
			Assert.AreEqual(4, matches[0].Cells.Count);
		}

		[Test]
		public void Test_FindAllMatches_DetectsCrossShape()
		{
			GridModel grid = BuildBackgroundGrid();

			// Horizontal run (1,0)-(3,0) crosses vertical run (2,0)-(2,2) at the
			// shared cell (2,0), forming one merged L/T-shaped group.
			grid.SetItem(1, 0, Item.Create(ItemColor.Yellow));
			grid.SetItem(2, 0, Item.Create(ItemColor.Yellow));
			grid.SetItem(3, 0, Item.Create(ItemColor.Yellow));
			grid.SetItem(2, 1, Item.Create(ItemColor.Yellow));
			grid.SetItem(2, 2, Item.Create(ItemColor.Yellow));

			List<MatchGroup> matches = MatchFinder.FindAllMatches(grid);

			Assert.AreEqual(1, matches.Count);
			Assert.AreEqual(ItemColor.Yellow, matches[0].Color);
			Assert.AreEqual(MatchShape.Cross, matches[0].Shape);
			Assert.AreEqual(5, matches[0].Cells.Count);
		}

		[Test]
		public void Test_FindAllMatches_IgnoresRunOfTwo()
		{
			GridModel grid = BuildBackgroundGrid();
			grid.SetItem(1, 0, Item.Create(ItemColor.Purple));
			grid.SetItem(2, 0, Item.Create(ItemColor.Purple));

			List<MatchGroup> matches = MatchFinder.FindAllMatches(grid);

			Assert.AreEqual(0, matches.Count);
		}

		[Test]
		public void Test_FindAllMatches_InitializedGridHasNoMatches()
		{
			var grid = new GridModel(8, 8, 42);
			grid.InitializeGrid();

			List<MatchGroup> matches = MatchFinder.FindAllMatches(grid);

			Assert.AreEqual(0, matches.Count);
		}
	}
}
