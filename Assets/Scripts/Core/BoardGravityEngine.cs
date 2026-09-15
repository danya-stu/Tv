using System;
using System.Collections.Generic;
using UnityEngine;

namespace PotionCraft.Core
{
	/// <summary>
	/// Pure C# gravity, refill and cascade resolver. No MonoBehaviour, no coroutines,
	/// no GameObject: the engine mutates the logical grid and reports every movement
	/// as plain data for the animation layer to replay.
	///
	/// GC Zero Allocation: all scratch state (match report, cascade step pool, special
	/// buffer) is static and reused via Clear(). Nothing is allocated inside the
	/// cascade loop after the first warm-up turn.
	///
	/// Not thread-safe by design (shared static buffers): call from the game thread.
	/// </summary>
	public static class BoardGravityEngine
	{
		private static readonly MatchReport s_report = new MatchReport(128);
		private static readonly List<CascadeStep> s_stepPool = new List<CascadeStep>(8);
		private static readonly List<SpecialCreationInfo> s_pendingSpecials = new List<SpecialCreationInfo>(8);

		/// <summary>
		/// Collapses every column downwards, compacting existing tiles towards y = 0
		/// and leaving Item.Empty in the vacated cells. Each displacement is appended
		/// to outMoves in bottom-up order, which is also a valid animation order.
		/// The list is not cleared: the caller owns its lifetime.
		/// </summary>
		public static void CollapseColumns(Item[,] grid, int width, int height, List<TileMoveAction> outMoves)
		{
			if (grid == null)
				throw new ArgumentNullException(nameof(grid));
			if (outMoves == null)
				throw new ArgumentNullException(nameof(outMoves));

			int w = ClampWidth(grid, width);
			int h = ClampHeight(grid, height);

			for (int x = 0; x < w; x++)
			{
				int writeY = 0;

				for (int y = 0; y < h; y++)
				{
					Item item = grid[x, y];
					if (item.Color == ItemColor.None)
					{
						continue;
					}

					if (writeY != y)
					{
						grid[x, writeY] = item;
						grid[x, y] = Item.Empty;
						outMoves.Add(new TileMoveAction(new Vector2Int(x, y), new Vector2Int(x, writeY), item));
					}

					writeY++;
				}
			}
		}

		/// <summary>
		/// Fills every remaining empty cell with a new item from the spawn provider.
		/// Within a column the lowest hole gets SpawnRow = height, the next one
		/// height + 1 and so on, so the animator can drop a queue of tiles from above
		/// the board without them overlapping. The list is not cleared by this method.
		/// </summary>
		public static void RefillEmptyCells(Item[,] grid, int width, int height, ISpawnWeightProvider weightProvider, int colorCount, List<TileSpawnAction> outSpawns)
		{
			if (grid == null)
				throw new ArgumentNullException(nameof(grid));
			if (weightProvider == null)
				throw new ArgumentNullException(nameof(weightProvider));
			if (outSpawns == null)
				throw new ArgumentNullException(nameof(outSpawns));

			int w = ClampWidth(grid, width);
			int h = ClampHeight(grid, height);

			for (int x = 0; x < w; x++)
			{
				int spawnOffset = 0;

				for (int y = 0; y < h; y++)
				{
					if (grid[x, y].Color != ItemColor.None)
					{
						continue;
					}

					ItemColor color = weightProvider.GetNextColor(x, y, colorCount);
					if (color == ItemColor.None)
					{
						color = ItemColor.Red;
					}

					Item spawned = Item.Create(color);
					grid[x, y] = spawned;

					outSpawns.Add(new TileSpawnAction(x, h + spawnOffset, new Vector2Int(x, y), spawned));
					spawnOffset++;
				}
			}
		}

		/// <summary>
		/// Resolves a whole turn: match -> spawn specials -> clear -> collapse -> refill,
		/// repeated until the board is stable or maxCascadesLimit is reached.
		/// Every iteration is recorded as a CascadeStep appended to outCascadeSteps.
		///
		/// The returned steps come from an internal pool and stay valid only until the
		/// next call, so the presentation layer must consume them before resolving the
		/// following turn.
		/// </summary>
		/// <returns>True if at least one match occurred during the turn.</returns>
		public static bool ResolveFullTurnCascades(
			Item[,] grid,
			int width,
			int height,
			ISpawnWeightProvider weightProvider,
			int colorCount,
			List<CascadeStep> outCascadeSteps,
			Vector2Int? initialSwapTarget = null,
			int maxCascadesLimit = 20)
		{
			if (grid == null)
				throw new ArgumentNullException(nameof(grid));
			if (weightProvider == null)
				throw new ArgumentNullException(nameof(weightProvider));
			if (outCascadeSteps == null)
				throw new ArgumentNullException(nameof(outCascadeSteps));

			outCascadeSteps.Clear();

			int w = ClampWidth(grid, width);
			int h = ClampHeight(grid, height);

			if (w <= 0 || h <= 0 || maxCascadesLimit <= 0)
			{
				return false;
			}

			bool anyMatch = false;
			int cascadeIndex = 0;

			while (cascadeIndex < maxCascadesLimit)
			{
				// Step 1: the swap hint only applies to the player's own move, never to cascades.
				Vector2Int? swapTarget = cascadeIndex == 0 ? initialSwapTarget : null;

				if (!MatchEngine.FindMatches(grid, w, h, s_report, swapTarget))
				{
					break;
				}

				anyMatch = true;
				CascadeStep step = RentStep(cascadeIndex);

				// Step 2a: record the destroyed cells and clear them.
				List<Vector2Int> matched = s_report.MatchedTiles;
				for (int i = 0; i < matched.Count; i++)
				{
					Vector2Int cell = matched[i];
					step.DestroyedTiles.Add(cell);
					grid[cell.x, cell.y] = Item.Empty;
				}

				foreach (KeyValuePair<ItemColor, int> essence in s_report.GatheredEssences)
				{
					step.AddEssence(essence.Key, essence.Value);
				}

				// Step 2b: materialize the specials on top of the cleared cells.
				s_pendingSpecials.Clear();
				List<SpecialCreationInfo> specials = s_report.CreatedSpecials;
				for (int i = 0; i < specials.Count; i++)
				{
					SpecialCreationInfo info = specials[i];
					step.CreatedSpecials.Add(info);
					s_pendingSpecials.Add(info);
				}

				for (int i = 0; i < s_pendingSpecials.Count; i++)
				{
					SpecialCreationInfo info = s_pendingSpecials[i];
					Vector2Int position = info.Position;

					if (position.x < 0 || position.x >= w || position.y < 0 || position.y >= h)
					{
						continue;
					}

					if (info.Color == ItemColor.None)
					{
						continue;
					}

					grid[position.x, position.y] = Item.Create(info.Color, ToCatalyst(info.Type));
				}

				// Step 3 and 4: gravity, then refill of everything still empty.
				CollapseColumns(grid, w, h, step.Moves);
				RefillEmptyCells(grid, w, h, weightProvider, colorCount, step.Spawns);

				// Step 5: publish the step and continue scanning the new board state.
				outCascadeSteps.Add(step);
				cascadeIndex++;
			}

			return anyMatch;
		}

		/// <summary>
		/// Maps a detected match shape onto the catalyst effect carried by the item.
		/// BombArea currently has no dedicated CatalystType, so it falls back to
		/// ColorBomb until the catalyst enum is extended.
		/// </summary>
		public static CatalystType ToCatalyst(SpecialType specialType)
		{
			switch (specialType)
			{
				case SpecialType.HorizontalLine:
					return CatalystType.RowClear;
				case SpecialType.VerticalLine:
					return CatalystType.ColumnClear;
				case SpecialType.BombArea:
					return CatalystType.ColorBomb;
				case SpecialType.ColorBomb:
					return CatalystType.ColorBomb;
				default:
					return CatalystType.None;
			}
		}

		private static CascadeStep RentStep(int index)
		{
			while (s_stepPool.Count <= index)
			{
				s_stepPool.Add(new CascadeStep());
			}

			CascadeStep step = s_stepPool[index];
			step.Clear();
			step.CascadeIndex = index;
			return step;
		}

		private static int ClampWidth(Item[,] grid, int width)
		{
			int gridWidth = grid.GetLength(0);
			if (width < 0)
			{
				return 0;
			}

			return width < gridWidth ? width : gridWidth;
		}

		private static int ClampHeight(Item[,] grid, int height)
		{
			int gridHeight = grid.GetLength(1);
			if (height < 0)
			{
				return 0;
			}

			return height < gridHeight ? height : gridHeight;
		}
	}
}
