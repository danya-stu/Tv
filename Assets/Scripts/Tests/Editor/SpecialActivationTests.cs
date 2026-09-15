using System.Collections.Generic;
using NUnit.Framework;
using PotionCraft.Core;
using UnityEngine;

namespace PotionCraft.Tests.Editor
{
	[TestFixture]
	public sealed class SpecialActivationTests
	{
		private const int PatternColorCount = 5;

		private HashSet<Vector2Int> _destroyed;
		private List<Vector2Int> _transmuted;
		private List<CascadeStep> _steps;
		private PatternSpawnProvider _provider;
		private ItemColor _transmuteColor;

		[SetUp]
		public void SetUp()
		{
			_destroyed = new HashSet<Vector2Int>();
			_transmuted = new List<Vector2Int>(64);
			_steps = new List<CascadeStep>(8);
			_provider = new PatternSpawnProvider();
			_transmuteColor = ItemColor.None;
		}

		// --- helpers -------------------------------------------------------

		/// <summary>
		/// Deterministic provider whose pattern can never form a match:
		/// horizontal neighbours differ by one step and vertical neighbours by two
		/// steps modulo five.
		/// </summary>
		private sealed class PatternSpawnProvider : ISpawnWeightProvider
		{
			public ItemColor GetNextColor(int x, int y, int totalColors)
			{
				return PatternColor(x, y);
			}
		}

		private static ItemColor PatternColor(int x, int y)
		{
			return (ItemColor)(1 + ((x + (2 * y)) % PatternColorCount));
		}

		private static Item[,] PatternGrid(int width, int height)
		{
			Item[,] grid = new Item[width, height];
			for (int x = 0; x < width; x++)
			{
				for (int y = 0; y < height; y++)
				{
					grid[x, y] = Item.Create(PatternColor(x, y));
				}
			}

			return grid;
		}

		private static Item[,] UniformGrid(int width, int height, ItemColor color)
		{
			Item[,] grid = new Item[width, height];
			for (int x = 0; x < width; x++)
			{
				for (int y = 0; y < height; y++)
				{
					grid[x, y] = Item.Create(color);
				}
			}

			return grid;
		}

		// --- pair combinations ---------------------------------------------

		[Test]
		public void Test_ColorBomb_Plus_ColorBomb_WipesBoard()
		{
			const int width = 5;
			const int height = 5;
			Item[,] grid = PatternGrid(width, height);
			grid[2, 2] = Item.Create(ItemColor.Red, CatalystType.ColorBomb);
			grid[2, 3] = Item.Create(ItemColor.Blue, CatalystType.ColorBomb);

			Assert.IsTrue(SpecialActivationEngine.IsSpecialSwap(grid[2, 2], grid[2, 3]));

			SpecialActivationEngine.ResolveSpecialSwap(grid, width, height, new Vector2Int(2, 2), new Vector2Int(2, 3), _destroyed, _transmuted, ref _transmuteColor);

			Assert.AreEqual(width * height, _destroyed.Count, "Two color bombs must wipe the entire board.");
		}

		[Test]
		public void Test_Line_Plus_Line_ClearsCross()
		{
			const int width = 5;
			const int height = 5;
			Item[,] grid = PatternGrid(width, height);
			Vector2Int posA = new Vector2Int(1, 2);
			Vector2Int posB = new Vector2Int(2, 2);
			grid[posA.x, posA.y] = Item.Create(ItemColor.Red, CatalystType.HorizontalLine);
			grid[posB.x, posB.y] = Item.Create(ItemColor.Blue, CatalystType.VerticalLine);

			SpecialActivationEngine.ResolveSpecialSwap(grid, width, height, posA, posB, _destroyed, _transmuted, ref _transmuteColor);

			Assert.AreEqual(width + height - 1, _destroyed.Count, "A line pair clears one full row plus one full column.");

			for (int x = 0; x < width; x++)
			{
				Assert.IsTrue(_destroyed.Contains(new Vector2Int(x, posB.y)), "Row of the destination cell must be cleared.");
			}

			for (int y = 0; y < height; y++)
			{
				Assert.IsTrue(_destroyed.Contains(new Vector2Int(posB.x, y)), "Column of the destination cell must be cleared.");
			}
		}

		[Test]
		public void Test_Line_Plus_Bomb_ClearsTripleBand()
		{
			const int width = 5;
			const int height = 5;
			Item[,] grid = PatternGrid(width, height);
			Vector2Int posA = new Vector2Int(2, 2);
			Vector2Int posB = new Vector2Int(2, 3);
			grid[posA.x, posA.y] = Item.Create(ItemColor.Red, CatalystType.HorizontalLine);
			grid[posB.x, posB.y] = Item.Create(ItemColor.Blue, CatalystType.BombArea);

			SpecialActivationEngine.ResolveSpecialSwap(grid, width, height, posA, posB, _destroyed, _transmuted, ref _transmuteColor);

			// Rows 2..4 and columns 1..3 of a 5x5 board: 15 + 15 - 9 overlapping cells.
			Assert.AreEqual(21, _destroyed.Count, "Line + Bomb clears a three-row band crossed by a three-column band.");

			for (int y = posB.y - 1; y <= posB.y + 1; y++)
			{
				for (int x = 0; x < width; x++)
				{
					Assert.IsTrue(_destroyed.Contains(new Vector2Int(x, y)), "Every cell of the three rows must be cleared.");
				}
			}

			for (int x = posB.x - 1; x <= posB.x + 1; x++)
			{
				for (int y = 0; y < height; y++)
				{
					Assert.IsTrue(_destroyed.Contains(new Vector2Int(x, y)), "Every cell of the three columns must be cleared.");
				}
			}

			Assert.IsFalse(_destroyed.Contains(new Vector2Int(0, 0)), "Cells outside both bands must survive.");
		}

		[Test]
		public void Test_Bomb_Plus_Bomb_Clears5x5()
		{
			const int width = 7;
			const int height = 7;
			Item[,] grid = PatternGrid(width, height);
			Vector2Int posA = new Vector2Int(3, 3);
			Vector2Int posB = new Vector2Int(3, 4);
			grid[posA.x, posA.y] = Item.Create(ItemColor.Red, CatalystType.BombArea);
			grid[posB.x, posB.y] = Item.Create(ItemColor.Blue, CatalystType.BombArea);

			SpecialActivationEngine.ResolveSpecialSwap(grid, width, height, posA, posB, _destroyed, _transmuted, ref _transmuteColor);

			Assert.AreEqual(25, _destroyed.Count, "Two bombs clear a 5x5 area around the destination cell.");

			for (int x = posB.x - 2; x <= posB.x + 2; x++)
			{
				for (int y = posB.y - 2; y <= posB.y + 2; y++)
				{
					Assert.IsTrue(_destroyed.Contains(new Vector2Int(x, y)), "The whole 5x5 block must be inside the blast.");
				}
			}

			Assert.IsFalse(_destroyed.Contains(new Vector2Int(0, 0)), "Cells outside the 5x5 block must survive.");
		}

		[Test]
		public void Test_ColorBomb_Plus_Regular_ClearsAllOfColor()
		{
			const int width = 5;
			const int height = 5;
			Item[,] grid = UniformGrid(width, height, ItemColor.Green);
			grid[0, 0] = Item.Create(ItemColor.Red);
			grid[4, 4] = Item.Create(ItemColor.Red);

			Vector2Int bombPos = new Vector2Int(2, 2);
			Vector2Int regularPos = new Vector2Int(2, 1);
			grid[bombPos.x, bombPos.y] = Item.Create(ItemColor.Purple, CatalystType.ColorBomb);
			grid[regularPos.x, regularPos.y] = Item.Create(ItemColor.Red);

			SpecialActivationEngine.ResolveSpecialSwap(grid, width, height, bombPos, regularPos, _destroyed, _transmuted, ref _transmuteColor);

			Assert.AreEqual(ItemColor.Red, _transmuteColor, "The consumed color must be reported back.");
			Assert.AreEqual(4, _destroyed.Count, "Every red tile plus the color bomb itself.");
			Assert.IsTrue(_destroyed.Contains(new Vector2Int(0, 0)));
			Assert.IsTrue(_destroyed.Contains(new Vector2Int(4, 4)));
			Assert.IsTrue(_destroyed.Contains(regularPos));
			Assert.IsTrue(_destroyed.Contains(bombPos));
			Assert.IsFalse(_destroyed.Contains(new Vector2Int(1, 1)), "Tiles of other colors must be untouched.");
		}

		// --- Reaction Catalyst --------------------------------------------

		[Test]
		public void Test_ReactionCatalyst_TransmutesToDominantColor()
		{
			const int width = 5;
			const int height = 5;
			Item[,] grid = UniformGrid(width, height, ItemColor.Green);

			// Area of radius 1 around (2, 2): five reds, three blues and one catalyst.
			grid[1, 1] = Item.Create(ItemColor.Red);
			grid[2, 1] = Item.Create(ItemColor.Red);
			grid[3, 1] = Item.Create(ItemColor.Red);
			grid[1, 2] = Item.Create(ItemColor.Red);
			grid[2, 2] = Item.Create(ItemColor.Red);
			grid[3, 2] = Item.Create(ItemColor.Blue);
			grid[1, 3] = Item.Create(ItemColor.Blue);
			grid[2, 3] = Item.Create(ItemColor.Blue);
			grid[3, 3] = Item.Create(ItemColor.Blue, CatalystType.BombArea);

			SpecialActivationEngine.TriggerTransmutationCatalyst(grid, width, height, new Vector2Int(2, 2), 1, _transmuted, out ItemColor dominantColor);

			Assert.AreEqual(ItemColor.Red, dominantColor, "Red is the most common plain color in the area.");
			Assert.AreEqual(3, _transmuted.Count, "Only the three plain blue tiles change color.");

			Assert.AreEqual(ItemColor.Red, grid[3, 2].Color);
			Assert.AreEqual(ItemColor.Red, grid[1, 3].Color);
			Assert.AreEqual(ItemColor.Red, grid[2, 3].Color);
			Assert.AreEqual(RuneType.Flame, grid[3, 2].Rune, "Transmutation must keep the color/rune pairing consistent.");

			Assert.AreEqual(ItemColor.Blue, grid[3, 3].Color, "A catalyst tile is never transmuted.");
			Assert.AreEqual(CatalystType.BombArea, grid[3, 3].Catalyst);

			Assert.AreEqual(ItemColor.Green, grid[0, 0].Color, "Tiles outside the radius must be untouched.");
			Assert.AreEqual(ItemColor.Green, grid[4, 4].Color);
		}

		// --- chain reaction ------------------------------------------------

		[Test]
		public void Test_ChainReaction_SpecialTriggersAnotherSpecial()
		{
			const int width = 7;
			const int height = 7;
			Item[,] grid = PatternGrid(width, height);

			// A plain triple of reds on row 0, where the middle tile carries a row clear.
			grid[0, 0] = Item.Create(ItemColor.Red);
			grid[1, 0] = Item.Create(ItemColor.Red, CatalystType.HorizontalLine);
			grid[2, 0] = Item.Create(ItemColor.Red);

			// The row clear will sweep up this column clear, which must detonate too.
			grid[5, 0] = Item.Create(PatternColor(5, 0), CatalystType.VerticalLine);

			bool resolved = BoardGravityEngine.ResolveFullTurnCascades(grid, width, height, _provider, PatternColorCount, _steps);

			Assert.IsTrue(resolved);
			Assert.GreaterOrEqual(_steps.Count, 1);

			CascadeStep first = _steps[0];
			Assert.Greater(first.DestroyedTiles.Count, 3, "The chain must destroy far more than the three matched tiles.");
			Assert.IsTrue(first.DestroyedTiles.Contains(new Vector2Int(6, 0)), "The row clear must reach the far end of row 0.");
			Assert.IsTrue(first.DestroyedTiles.Contains(new Vector2Int(5, 3)), "The chained column clear must reach the middle of column 5.");
			Assert.IsTrue(first.DestroyedTiles.Contains(new Vector2Int(5, 6)), "The chained column clear must reach the top of column 5.");
			Assert.IsFalse(first.DestroyedTiles.Contains(new Vector2Int(0, 6)), "Unrelated columns must not be cleared.");

			for (int x = 0; x < width; x++)
			{
				for (int y = 0; y < height; y++)
				{
					Assert.AreNotEqual(ItemColor.None, grid[x, y].Color, "The board must be fully refilled after the chain.");
				}
			}
		}

		// --- special swap detection ----------------------------------------

		[Test]
		public void Test_SpecialSwapDetection()
		{
			Item plainRed = Item.Create(ItemColor.Red);
			Item plainBlue = Item.Create(ItemColor.Blue);
			Item lineRed = Item.Create(ItemColor.Red, CatalystType.HorizontalLine);
			Item colorBomb = Item.Create(ItemColor.Blue, CatalystType.ColorBomb);

			Assert.IsFalse(SpecialActivationEngine.IsSpecialSwap(plainRed, plainBlue));
			Assert.IsTrue(SpecialActivationEngine.IsSpecialSwap(plainRed, lineRed));
			Assert.IsTrue(SpecialActivationEngine.IsSpecialSwap(colorBomb, plainBlue));

			Assert.IsFalse(SpecialActivationEngine.IsActivatingSwap(plainRed, lineRed), "A single line tile still needs a classic match to fire.");
			Assert.IsTrue(SpecialActivationEngine.IsActivatingSwap(colorBomb, plainRed), "A color bomb always activates on swap.");
			Assert.IsTrue(SpecialActivationEngine.IsActivatingSwap(lineRed, colorBomb));
		}
	}
}
