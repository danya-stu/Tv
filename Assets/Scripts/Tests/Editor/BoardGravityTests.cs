using System.Collections.Generic;
using NUnit.Framework;
using PotionCraft.Core;
using UnityEngine;

namespace PotionCraft.Tests.Editor
{
	[TestFixture]
	public sealed class BoardGravityTests
	{
		private const int PatternColorCount = 5;

		private List<TileMoveAction> _moves;
		private List<TileSpawnAction> _spawns;
		private List<CascadeStep> _steps;
		private PatternSpawnProvider _provider;

		[SetUp]
		public void SetUp()
		{
			_moves = new List<TileMoveAction>(32);
			_spawns = new List<TileSpawnAction>(32);
			_steps = new List<CascadeStep>(8);
			_provider = new PatternSpawnProvider();
		}

		// --- helpers -------------------------------------------------------

		/// <summary>
		/// Deterministic checkerboard-like provider. Horizontal neighbours differ by
		/// one step and vertical neighbours by two steps modulo five, so a board built
		/// purely from this pattern can never contain a match.
		/// </summary>
		private sealed class PatternSpawnProvider : ISpawnWeightProvider
		{
			public int Calls { get; private set; }

			public ItemColor GetNextColor(int x, int y, int totalColors)
			{
				Calls++;
				return PatternColor(x, y);
			}
		}

		private static ItemColor PatternColor(int x, int y)
		{
			return (ItemColor)(1 + ((x + (2 * y)) % PatternColorCount));
		}

		private static Item[,] EmptyGrid(int width, int height)
		{
			Item[,] grid = new Item[width, height];
			for (int x = 0; x < width; x++)
			{
				for (int y = 0; y < height; y++)
				{
					grid[x, y] = Item.Empty;
				}
			}

			return grid;
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

		private static bool HasEmptyCells(Item[,] grid, int width, int height)
		{
			for (int x = 0; x < width; x++)
			{
				for (int y = 0; y < height; y++)
				{
					if (grid[x, y].Color == ItemColor.None)
					{
						return true;
					}
				}
			}

			return false;
		}

		// --- gravity -------------------------------------------------------

		[Test]
		public void Test_SingleHole_TileFallsDown()
		{
			Item[,] grid = EmptyGrid(3, 4);
			grid[0, 2] = Item.Create(ItemColor.Red);

			BoardGravityEngine.CollapseColumns(grid, 3, 4, _moves);

			Assert.AreEqual(1, _moves.Count, "Exactly one tile should have moved.");
			Assert.AreEqual(new Vector2Int(0, 2), _moves[0].From);
			Assert.AreEqual(new Vector2Int(0, 0), _moves[0].To);
			Assert.AreEqual(2, _moves[0].Distance);
			Assert.AreEqual(ItemColor.Red, _moves[0].MovedItem.Color);
			Assert.AreEqual(ItemColor.Red, grid[0, 0].Color, "Tile must land on the floor of its column.");
			Assert.AreEqual(ItemColor.None, grid[0, 2].Color, "Source cell must be emptied.");
		}

		[Test]
		public void Test_MultipleHolesInColumn_FallCorrectly()
		{
			Item[,] grid = EmptyGrid(3, 5);
			// Column 1 layout bottom-up: hole, Red, hole, Blue, hole
			grid[1, 1] = Item.Create(ItemColor.Red);
			grid[1, 3] = Item.Create(ItemColor.Blue);

			BoardGravityEngine.CollapseColumns(grid, 3, 5, _moves);

			Assert.AreEqual(2, _moves.Count);

			// Moves are reported bottom-up, which is a valid animation order.
			Assert.AreEqual(new Vector2Int(1, 1), _moves[0].From);
			Assert.AreEqual(new Vector2Int(1, 0), _moves[0].To);
			Assert.AreEqual(1, _moves[0].Distance);

			Assert.AreEqual(new Vector2Int(1, 3), _moves[1].From);
			Assert.AreEqual(new Vector2Int(1, 1), _moves[1].To);
			Assert.AreEqual(2, _moves[1].Distance);

			Assert.AreEqual(ItemColor.Red, grid[1, 0].Color, "Relative order inside the column must be preserved.");
			Assert.AreEqual(ItemColor.Blue, grid[1, 1].Color);
			Assert.AreEqual(ItemColor.None, grid[1, 2].Color);
			Assert.AreEqual(ItemColor.None, grid[1, 3].Color);
			Assert.AreEqual(ItemColor.None, grid[1, 4].Color);
		}

		// --- refill --------------------------------------------------------

		[Test]
		public void Test_Refill_FillsAllHoles()
		{
			const int width = 4;
			const int height = 3;
			Item[,] grid = EmptyGrid(width, height);
			grid[0, 0] = Item.Create(ItemColor.Purple);

			BoardGravityEngine.RefillEmptyCells(grid, width, height, _provider, PatternColorCount, _spawns);

			Assert.IsFalse(HasEmptyCells(grid, width, height), "Refill must leave no empty cell behind.");
			Assert.AreEqual((width * height) - 1, _spawns.Count, "Only empty cells are spawned into.");
			Assert.AreEqual(ItemColor.Purple, grid[0, 0].Color, "Occupied cells must not be overwritten.");

			for (int i = 0; i < _spawns.Count; i++)
			{
				TileSpawnAction spawn = _spawns[i];
				Assert.GreaterOrEqual(spawn.SpawnRow, height, "Spawn row must sit above the visible board.");
				Assert.AreEqual(spawn.Column, spawn.TargetPosition.x, "Spawns never change column.");
				Assert.Greater(spawn.DropDistance, 0, "Every spawned tile has to travel down.");
				Assert.AreNotEqual(ItemColor.None, spawn.SpawnedItem.Color);
			}

			// Within one column the virtual spawn rows must be unique so drops do not overlap.
			for (int i = 0; i < _spawns.Count; i++)
			{
				for (int j = i + 1; j < _spawns.Count; j++)
				{
					if (_spawns[i].Column == _spawns[j].Column)
					{
						Assert.AreNotEqual(_spawns[i].SpawnRow, _spawns[j].SpawnRow);
					}
				}
			}
		}

		// --- cascades ------------------------------------------------------

		[Test]
		public void Test_ResolveCascades_StopsWhenStable()
		{
			const int width = 5;
			const int height = 7;
			const int maxCascades = 20;

			Item[,] stableGrid = PatternGrid(width, height);
			MatchReport report = new MatchReport();
			Assert.IsFalse(MatchEngine.FindMatches(stableGrid, width, height, report), "Test fixture must start from a match-free pattern.");

			bool resolvedOnStable = BoardGravityEngine.ResolveFullTurnCascades(stableGrid, width, height, _provider, PatternColorCount, _steps, null, maxCascades);
			Assert.IsFalse(resolvedOnStable, "A stable board must not produce any cascade.");
			Assert.AreEqual(0, _steps.Count);

			Item[,] grid = PatternGrid(width, height);
			grid[0, 0] = Item.Create(ItemColor.Red);
			grid[1, 0] = Item.Create(ItemColor.Red);
			grid[2, 0] = Item.Create(ItemColor.Red);

			bool resolved = BoardGravityEngine.ResolveFullTurnCascades(grid, width, height, _provider, PatternColorCount, _steps, new Vector2Int(1, 0), maxCascades);

			Assert.IsTrue(resolved, "A seeded match must be resolved.");
			Assert.GreaterOrEqual(_steps.Count, 1);
			Assert.LessOrEqual(_steps.Count, maxCascades, "The cascade loop must respect its hard limit.");
			Assert.AreEqual(3, _steps[0].DestroyedTiles.Count, "The seeded horizontal triple is the first cascade.");
			Assert.AreEqual(3, _steps[0].GetEssence(ItemColor.Red));
			Assert.AreEqual(3, _steps[0].Spawns.Count, "Each destroyed cell has to be refilled.");
			Assert.IsFalse(HasEmptyCells(grid, width, height), "The board must be fully populated after a turn.");

			if (_steps.Count < maxCascades)
			{
				report.Clear();
				Assert.IsFalse(MatchEngine.FindMatches(grid, width, height, report), "Below the limit the loop must only stop on a stable board.");
			}
		}

		[Test]
		public void Test_CascadeIndex_IncrementsProperly()
		{
			const int width = 5;
			const int height = 7;

			Item[,] grid = PatternGrid(width, height);
			grid[0, 0] = Item.Create(ItemColor.Red);
			grid[1, 0] = Item.Create(ItemColor.Red);
			grid[2, 0] = Item.Create(ItemColor.Red);

			bool resolved = BoardGravityEngine.ResolveFullTurnCascades(grid, width, height, _provider, PatternColorCount, _steps);

			Assert.IsTrue(resolved);
			Assert.GreaterOrEqual(_steps.Count, 1);

			for (int i = 0; i < _steps.Count; i++)
			{
				Assert.AreEqual(i, _steps[i].CascadeIndex, "Cascade indices must be sequential and zero based.");
				Assert.Greater(_steps[i].DestroyedTiles.Count, 0, "A recorded cascade always destroys something.");
			}

			// A second turn must reset the output list instead of appending to it.
			int firstTurnSteps = _steps.Count;
			BoardGravityEngine.ResolveFullTurnCascades(grid, width, height, _provider, PatternColorCount, _steps);
			Assert.LessOrEqual(_steps.Count, firstTurnSteps + _steps.Count, "Sanity guard against unbounded growth.");

			for (int i = 0; i < _steps.Count; i++)
			{
				Assert.AreEqual(i, _steps[i].CascadeIndex, "Pooled steps must be re-indexed from zero on every turn.");
			}
		}
	}
}
