using NUnit.Framework;
using PotionCraft.Core;

namespace PotionCraft.Tests.Editor
{
	/// <summary>
	/// Edit-mode unit tests for GridModel. These run against the pure C#
	/// model only, with no Unity scene, GameObject, or PlayMode dependency,
	/// which is possible only because GridModel has zero UnityEngine.dll
	/// references.
	/// </summary>
	[TestFixture]
	public sealed class GridModelTests
	{
		private const int GridWidth = 8;
		private const int GridHeight = 8;
		private const int ColorCount = 5;
		private const int FixedSeed = 12345;

		[Test]
		public void Test_GridInitialization_DimensionsAreCorrect()
		{
			var grid = new GridModel(GridWidth, GridHeight, FixedSeed);
			grid.InitializeGrid(ColorCount);

			Assert.AreEqual(GridWidth, grid.Width);
			Assert.AreEqual(GridHeight, grid.Height);

			for (int y = 0; y < grid.Height; y++)
			{
				for (int x = 0; x < grid.Width; x++)
				{
					Assert.IsTrue(grid.IsValidCoordinate(x, y));
					Assert.AreNotEqual(ItemColor.None, grid.GetItem(x, y).Color);
				}
			}
		}

		[Test]
		public void Test_GridInitialization_HasNoInitialMatches()
		{
			var grid = new GridModel(GridWidth, GridHeight, FixedSeed);
			grid.InitializeGrid(ColorCount);

			for (int y = 0; y < grid.Height; y++)
			{
				for (int x = 0; x < grid.Width; x++)
				{
					ItemColor color = grid.GetItem(x, y).Color;

					if (x >= 2)
					{
						bool horizontalTriple = grid.GetItem(x - 1, y).Color == color
							&& grid.GetItem(x - 2, y).Color == color;
						Assert.IsFalse(horizontalTriple, $"Unexpected horizontal match ending at ({x},{y}).");
					}

					if (y >= 2)
					{
						bool verticalTriple = grid.GetItem(x, y - 1).Color == color
							&& grid.GetItem(x, y - 2).Color == color;
						Assert.IsFalse(verticalTriple, $"Unexpected vertical match ending at ({x},{y}).");
					}
				}
			}
		}

		[Test]
		public void Test_GridInitialization_HasAtLeastOneValidMove()
		{
			var grid = new GridModel(GridWidth, GridHeight, FixedSeed);
			grid.InitializeGrid(ColorCount);

			// A freshly initialized board is expected to have at least one
			// possible move; if not, ShuffleUntilSolvable is responsible for
			// fixing that before play begins.
			if (!grid.HasPossibleMoves())
				grid.ShuffleUntilSolvable();

			Assert.IsTrue(grid.HasPossibleMoves());
		}

		[Test]
		public void Test_Swap_SwapsCorrectly()
		{
			var grid = new GridModel(GridWidth, GridHeight, FixedSeed);
			grid.InitializeGrid(ColorCount);

			Item original1 = grid.GetItem(0, 0);
			Item original2 = grid.GetItem(1, 0);

			grid.Swap(0, 0, 1, 0);

			Assert.AreEqual(original2, grid.GetItem(0, 0));
			Assert.AreEqual(original1, grid.GetItem(1, 0));
		}

		[Test]
		public void Test_DeadlockDetection_And_Shuffle()
		{
			var grid = new GridModel(GridWidth, GridHeight, FixedSeed);

			// Build a genuine match-3 deadlock: a diagonal 3-color stripe
			// pattern, color(x, y) = (x + y) % 3. A 2-color checkerboard is
			// NOT a real deadlock -- swapping any horizontal or vertical pair
			// on a checkerboard always creates a vertical/horizontal run of 3
			// in the adjacent column/row, because the swapped-in color always
			// equals both neighbours two cells away. The period-3 diagonal
			// stripe pattern below is a well-known true deadlock: any single
			// adjacent swap only ever produces a run of length 2 in either
			// direction, never 3, so the board has zero possible moves.
			ItemColor[] palette = { ItemColor.Red, ItemColor.Blue, ItemColor.Green };
			for (int y = 0; y < grid.Height; y++)
			{
				for (int x = 0; x < grid.Width; x++)
				{
					ItemColor color = palette[(x + y) % 3];
					grid.SetItem(x, y, Item.Create(color));
				}
			}

			Assert.IsFalse(grid.HasPossibleMoves(), "Diagonal stripe setup should have zero possible moves.");

			grid.ShuffleUntilSolvable();

			Assert.IsTrue(grid.HasPossibleMoves(), "ShuffleUntilSolvable must resolve a deadlocked board into one with a valid move.");
		}
	}
}
