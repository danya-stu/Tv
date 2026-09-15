using NUnit.Framework;
using PotionCraft.Core;
using UnityEngine;

namespace PotionCraft.Tests.Editor
{
	/// <summary>
	/// NUnit test suite for MatchEngine match detection and special tile creation logic.
	/// </summary>
	[TestFixture]
	public sealed class MatchEngineTests
	{
		private static readonly ItemColor[] Palette =
		{
			ItemColor.Red,
			ItemColor.Blue,
			ItemColor.Green,
			ItemColor.Yellow,
			ItemColor.Purple
		};

		/// <summary>
		/// Builds a grid guaranteed to be free of 3-in-a-row matches using a diagonal modulo-5 pattern.
		/// </summary>
		private static Item[,] BuildMatchFreeGrid(int width, int height)
		{
			var grid = new Item[width, height];
			for (int y = 0; y < height; y++)
			{
				for (int x = 0; x < width; x++)
				{
					grid[x, y] = Item.Create(Palette[(x + y) % Palette.Length]);
				}
			}
			return grid;
		}

		private static Item[,] CreateEmptyGrid(int width, int height)
		{
			var grid = new Item[width, height];
			for (int y = 0; y < height; y++)
			{
				for (int x = 0; x < width; x++)
				{
					grid[x, y] = Item.Empty;
				}
			}
			return grid;
		}

		[Test]
		public void Test_NoMatch_Returns_False()
		{
			const int size = 6;
			Item[,] grid = BuildMatchFreeGrid(size, size);
			var report = new MatchReport();

			bool result = MatchEngine.FindMatches(grid, size, size, report);

			Assert.IsFalse(result);
			Assert.AreEqual(0, report.MatchedTiles.Count);
			Assert.AreEqual(0, report.CreatedSpecials.Count);
			Assert.AreEqual(0, report.GatheredEssences.Count);
		}

		[Test]
		public void Test_Horizontal_Match3_Detected()
		{
			const int size = 6;
			Item[,] grid = CreateEmptyGrid(size, size);
			grid[1, 2] = Item.Create(ItemColor.Red);
			grid[2, 2] = Item.Create(ItemColor.Red);
			grid[3, 2] = Item.Create(ItemColor.Red);

			var report = new MatchReport();
			bool result = MatchEngine.FindMatches(grid, size, size, report);

			Assert.IsTrue(result);
			Assert.AreEqual(3, report.MatchedTiles.Count);
			Assert.IsTrue(report.MatchedTiles.Contains(new Vector2Int(1, 2)));
			Assert.IsTrue(report.MatchedTiles.Contains(new Vector2Int(2, 2)));
			Assert.IsTrue(report.MatchedTiles.Contains(new Vector2Int(3, 2)));
			Assert.AreEqual(0, report.CreatedSpecials.Count);
			Assert.AreEqual(3, report.GatheredEssences[ItemColor.Red]);
		}

		[Test]
		public void Test_Vertical_Match4_Creates_LineSpecial()
		{
			const int size = 6;
			Item[,] grid = CreateEmptyGrid(size, size);
			grid[2, 1] = Item.Create(ItemColor.Green);
			grid[2, 2] = Item.Create(ItemColor.Green);
			grid[2, 3] = Item.Create(ItemColor.Green);
			grid[2, 4] = Item.Create(ItemColor.Green);

			var swappedCell = new Vector2Int(2, 3);
			var report = new MatchReport();
			bool result = MatchEngine.FindMatches(grid, size, size, report, swappedCell);

			Assert.IsTrue(result);
			Assert.AreEqual(4, report.MatchedTiles.Count);
			Assert.AreEqual(1, report.CreatedSpecials.Count);
			// Vertical match 4 creates HorizontalLine special to clear rows
			Assert.AreEqual(SpecialType.HorizontalLine, report.CreatedSpecials[0].Type);
			Assert.AreEqual(ItemColor.Green, report.CreatedSpecials[0].Color);
			// Bonus placed at lastSwappedTile coordinate
			Assert.AreEqual(swappedCell, report.CreatedSpecials[0].Position);
			Assert.AreEqual(4, report.GatheredEssences[ItemColor.Green]);
		}

		[Test]
		public void Test_TShape_Match5_Creates_BombArea()
		{
			const int size = 6;
			Item[,] grid = CreateEmptyGrid(size, size);

			// T-Shape: Horizontal run (1,1)-(3,1) and Vertical run (2,1)-(2,3) sharing (2,1)
			grid[1, 1] = Item.Create(ItemColor.Yellow);
			grid[2, 1] = Item.Create(ItemColor.Yellow);
			grid[3, 1] = Item.Create(ItemColor.Yellow);
			grid[2, 2] = Item.Create(ItemColor.Yellow);
			grid[2, 3] = Item.Create(ItemColor.Yellow);

			var report = new MatchReport();
			bool result = MatchEngine.FindMatches(grid, size, size, report);

			Assert.IsTrue(result);
			Assert.AreEqual(5, report.MatchedTiles.Count);
			Assert.AreEqual(1, report.CreatedSpecials.Count);
			Assert.AreEqual(SpecialType.BombArea, report.CreatedSpecials[0].Type);
			Assert.AreEqual(ItemColor.Yellow, report.CreatedSpecials[0].Color);
			// Cascade center should be at the intersection of the T-Shape
			Assert.AreEqual(new Vector2Int(2, 1), report.CreatedSpecials[0].Position);
			Assert.AreEqual(5, report.GatheredEssences[ItemColor.Yellow]);
		}

		[Test]
		public void Test_Line5_Creates_ColorBomb()
		{
			const int size = 7;
			Item[,] grid = CreateEmptyGrid(size, size);
			grid[1, 3] = Item.Create(ItemColor.Purple);
			grid[2, 3] = Item.Create(ItemColor.Purple);
			grid[3, 3] = Item.Create(ItemColor.Purple);
			grid[4, 3] = Item.Create(ItemColor.Purple);
			grid[5, 3] = Item.Create(ItemColor.Purple);

			var report = new MatchReport();
			bool result = MatchEngine.FindMatches(grid, size, size, report);

			Assert.IsTrue(result);
			Assert.AreEqual(5, report.MatchedTiles.Count);
			Assert.AreEqual(1, report.CreatedSpecials.Count);
			Assert.AreEqual(SpecialType.ColorBomb, report.CreatedSpecials[0].Type);
			Assert.AreEqual(ItemColor.Purple, report.CreatedSpecials[0].Color);
			// Center of horizontal line of 5: StartX (1) + 5/2 = 3
			Assert.AreEqual(new Vector2Int(3, 3), report.CreatedSpecials[0].Position);
			Assert.AreEqual(5, report.GatheredEssences[ItemColor.Purple]);
		}

		[Test]
		public void Test_Horizontal_Match4_Creates_VerticalLineSpecial()
		{
			const int size = 6;
			Item[,] grid = CreateEmptyGrid(size, size);
			grid[1, 2] = Item.Create(ItemColor.Blue);
			grid[2, 2] = Item.Create(ItemColor.Blue);
			grid[3, 2] = Item.Create(ItemColor.Blue);
			grid[4, 2] = Item.Create(ItemColor.Blue);

			var report = new MatchReport();
			bool result = MatchEngine.FindMatches(grid, size, size, report);

			Assert.IsTrue(result);
			Assert.AreEqual(4, report.MatchedTiles.Count);
			Assert.AreEqual(1, report.CreatedSpecials.Count);
			// Horizontal match 4 creates VerticalLine special to clear columns
			Assert.AreEqual(SpecialType.VerticalLine, report.CreatedSpecials[0].Type);
			Assert.AreEqual(ItemColor.Blue, report.CreatedSpecials[0].Color);
			// Geometric center for line of 4: StartX (1) + 4/2 = 3
			Assert.AreEqual(new Vector2Int(3, 2), report.CreatedSpecials[0].Position);
			Assert.AreEqual(4, report.GatheredEssences[ItemColor.Blue]);
		}

		[Test]
		public void Test_LShape_Match5_Creates_BombArea()
		{
			const int size = 6;
			Item[,] grid = CreateEmptyGrid(size, size);

			// L-Shape: Horizontal run (1,1)-(3,1) and Vertical run (1,1)-(1,3) sharing (1,1)
			grid[1, 1] = Item.Create(ItemColor.Red);
			grid[2, 1] = Item.Create(ItemColor.Red);
			grid[3, 1] = Item.Create(ItemColor.Red);
			grid[1, 2] = Item.Create(ItemColor.Red);
			grid[1, 3] = Item.Create(ItemColor.Red);

			var report = new MatchReport();
			bool result = MatchEngine.FindMatches(grid, size, size, report);

			Assert.IsTrue(result);
			Assert.AreEqual(5, report.MatchedTiles.Count);
			Assert.AreEqual(1, report.CreatedSpecials.Count);
			Assert.AreEqual(SpecialType.BombArea, report.CreatedSpecials[0].Type);
			Assert.AreEqual(ItemColor.Red, report.CreatedSpecials[0].Color);
			Assert.AreEqual(new Vector2Int(1, 1), report.CreatedSpecials[0].Position);
			Assert.AreEqual(5, report.GatheredEssences[ItemColor.Red]);
		}

		[Test]
		public void Test_MatchReport_Clear_AllowsReuseWithoutAllocations()
		{
			var report = new MatchReport();
			report.MatchedTiles.Add(new Vector2Int(1, 1));
			report.CreatedSpecials.Add(new SpecialCreationInfo(new Vector2Int(1, 1), SpecialType.BombArea, ItemColor.Red));
			report.GatheredEssences[ItemColor.Red] = 10;

			report.Clear();

			Assert.AreEqual(0, report.MatchedTiles.Count);
			Assert.AreEqual(0, report.CreatedSpecials.Count);
			Assert.AreEqual(0, report.GatheredEssences.Count);
		}
	}
}
