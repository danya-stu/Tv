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
	/// GC Zero Allocation: all scratch state (match report, cascade step pool,
	/// destruction set, chain-reaction work list) is static and reused via Clear().
	/// Nothing is allocated inside the cascade loop after the first warm-up turn.
	///
	/// Not thread-safe by design (shared static buffers): call from the game thread.
	/// </summary>
	public static class BoardGravityEngine
	{
		private static readonly MatchReport s_report = new MatchReport(128);
		private static readonly List<CascadeStep> s_stepPool = new List<CascadeStep>(8);
		private static readonly List<SpecialCreationInfo> s_pendingSpecials = new List<SpecialCreationInfo>(8);
		private static readonly HashSet<Vector2Int> s_destroyedSet = new HashSet<Vector2Int>();
		private static readonly HashSet<Vector2Int> s_blastBuffer = new HashSet<Vector2Int>();
		private static readonly List<Vector2Int> s_chainWorkList = new List<Vector2Int>(256);
		private static readonly List<Vector2Int> s_transmutedBuffer = new List<Vector2Int>(128);

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
		/// Resolves a whole turn: optional special-swap activation, then
		/// match -> chain detonation -> clear -> spawn specials -> collapse -> refill,
		/// repeated until the board is stable or maxCascadesLimit is reached.
		/// Every iteration is recorded as a CascadeStep appended to outCascadeSteps.
		///
		/// When specialSwapSource and initialSwapTarget both point at a swap that
		/// involves a special tile, that activation becomes cascade step 0 even if it
		/// forms no classic match. Any catalyst swept up by a later match detonates
		/// through ExplodeSingleSpecial, and its blast can trigger further catalysts.
		///
		/// The returned steps come from an internal pool and stay valid only until the
		/// next call, so the presentation layer must consume them before resolving the
		/// following turn.
		/// </summary>
		/// <returns>True if the turn produced at least one match or special activation.</returns>
		public static bool ResolveFullTurnCascades(
			Item[,] grid,
			int width,
			int height,
			ISpawnWeightProvider weightProvider,
			int colorCount,
			List<CascadeStep> outCascadeSteps,
			Vector2Int? initialSwapTarget = null,
			int maxCascadesLimit = 20,
			Vector2Int? specialSwapSource = null)
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

			bool anyResolution = false;
			int cascadeIndex = 0;

			// Step 0: a swap of two specials, or a ColorBomb with a plain tile, resolves
			// before any match scan and consumes the player's move on its own.
			if (TryResolveOpeningSpecialSwap(grid, w, h, weightProvider, colorCount, outCascadeSteps, specialSwapSource, initialSwapTarget))
			{
				anyResolution = true;
				cascadeIndex = 1;
			}

			while (cascadeIndex < maxCascadesLimit)
			{
				// The swap hint only applies to the player's own move, never to cascades.
				Vector2Int? swapTarget = cascadeIndex == 0 ? initialSwapTarget : null;

				if (!MatchEngine.FindMatches(grid, w, h, s_report, swapTarget))
				{
					break;
				}

				anyResolution = true;
				CascadeStep step = RentStep(cascadeIndex);

				// Collect the matched cells, then let every catalyst caught by the match
				// detonate, chaining into further catalysts inside the blast.
				s_destroyedSet.Clear();
				List<Vector2Int> matched = s_report.MatchedTiles;
				for (int i = 0; i < matched.Count; i++)
				{
					s_destroyedSet.Add(matched[i]);
				}

				ExpandChainReactions(grid, w, h, s_destroyedSet);
				CommitDestruction(grid, w, h, step, s_destroyedSet);

				// Materialize the specials created by this match on top of the cleared cells.
				s_pendingSpecials.Clear();
				List<SpecialCreationInfo> specials = s_report.CreatedSpecials;
				for (int i = 0; i < specials.Count; i++)
				{
					SpecialCreationInfo info = specials[i];
					step.CreatedSpecials.Add(info);
					s_pendingSpecials.Add(info);
				}

				PlacePendingSpecials(grid, w, h);

				// Gravity, then refill of everything still empty.
				CollapseColumns(grid, w, h, step.Moves);
				RefillEmptyCells(grid, w, h, weightProvider, colorCount, step.Spawns);

				outCascadeSteps.Add(step);
				cascadeIndex++;
			}

			return anyResolution;
		}

		/// <summary>
		/// Runs the special activation for the player's swap when it involves a
		/// special tile, recording it as cascade step 0.
		/// </summary>
		private static bool TryResolveOpeningSpecialSwap(
			Item[,] grid,
			int w,
			int h,
			ISpawnWeightProvider weightProvider,
			int colorCount,
			List<CascadeStep> outCascadeSteps,
			Vector2Int? specialSwapSource,
			Vector2Int? initialSwapTarget)
		{
			if (!specialSwapSource.HasValue || !initialSwapTarget.HasValue)
			{
				return false;
			}

			Vector2Int posA = specialSwapSource.Value;
			Vector2Int posB = initialSwapTarget.Value;

			if (!InBounds(posA, w, h) || !InBounds(posB, w, h) || posA == posB)
			{
				return false;
			}

			Item itemA = grid[posA.x, posA.y];
			Item itemB = grid[posB.x, posB.y];

			if (!SpecialActivationEngine.IsSpecialSwap(itemA, itemB))
			{
				return false;
			}

			CascadeStep step = RentStep(0);

			s_destroyedSet.Clear();
			s_transmutedBuffer.Clear();
			ItemColor transmuteColor = ItemColor.None;

			SpecialActivationEngine.ResolveSpecialSwap(grid, w, h, posA, posB, s_destroyedSet, s_transmutedBuffer, ref transmuteColor);

			if (s_destroyedSet.Count == 0)
			{
				return false;
			}

			// Catalysts caught inside the activation blast detonate as well.
			ExpandChainReactions(grid, w, h, s_destroyedSet);

			step.TransmuteColor = transmuteColor;
			for (int i = 0; i < s_transmutedBuffer.Count; i++)
			{
				step.TransmutedTiles.Add(s_transmutedBuffer[i]);
			}

			CommitDestruction(grid, w, h, step, s_destroyedSet);
			CollapseColumns(grid, w, h, step.Moves);
			RefillEmptyCells(grid, w, h, weightProvider, colorCount, step.Spawns);

			outCascadeSteps.Add(step);
			return true;
		}

		/// <summary>
		/// Iteratively detonates every special tile inside the destruction set and
		/// whatever new specials its blast reaches. Implemented as a work list rather
		/// than real recursion, so a long chain can never blow the stack, and each
		/// catalyst is consumed exactly once.
		/// </summary>
		private static void ExpandChainReactions(Item[,] grid, int w, int h, HashSet<Vector2Int> destroyed)
		{
			s_chainWorkList.Clear();
			foreach (Vector2Int cell in destroyed)
			{
				s_chainWorkList.Add(cell);
			}

			for (int i = 0; i < s_chainWorkList.Count; i++)
			{
				Vector2Int cell = s_chainWorkList[i];
				if (!InBounds(cell, w, h))
				{
					continue;
				}

				Item item = grid[cell.x, cell.y];
				SpecialType special = SpecialActivationEngine.ToSpecialType(item.Catalyst);
				if (special == SpecialType.None)
				{
					continue;
				}

				// Strip the catalyst first so it cannot detonate twice through a loop.
				grid[cell.x, cell.y] = Item.Create(item.Color);

				s_blastBuffer.Clear();
				SpecialActivationEngine.ExplodeSingleSpecial(grid, w, h, cell, special, item.Color, s_blastBuffer);

				foreach (Vector2Int blastCell in s_blastBuffer)
				{
					if (destroyed.Add(blastCell))
					{
						s_chainWorkList.Add(blastCell);
					}
				}
			}
		}

		/// <summary>
		/// Clears every collected cell, recording the destroyed coordinates and the
		/// essence harvested from them into the step.
		/// </summary>
		private static void CommitDestruction(Item[,] grid, int w, int h, CascadeStep step, HashSet<Vector2Int> cells)
		{
			foreach (Vector2Int cell in cells)
			{
				if (!InBounds(cell, w, h))
				{
					continue;
				}

				Item item = grid[cell.x, cell.y];
				if (item.Color == ItemColor.None)
				{
					continue;
				}

				step.DestroyedTiles.Add(cell);
				step.AddEssence(item.Color, 1);
				grid[cell.x, cell.y] = Item.Empty;
			}
		}

		private static void PlacePendingSpecials(Item[,] grid, int w, int h)
		{
			for (int i = 0; i < s_pendingSpecials.Count; i++)
			{
				SpecialCreationInfo info = s_pendingSpecials[i];
				Vector2Int position = info.Position;

				if (!InBounds(position, w, h))
				{
					continue;
				}

				if (info.Color == ItemColor.None)
				{
					continue;
				}

				grid[position.x, position.y] = Item.Create(info.Color, ToCatalyst(info.Type));
			}
		}

		/// <summary>
		/// Maps a detected match shape onto the catalyst effect carried by the item.
		/// </summary>
		public static CatalystType ToCatalyst(SpecialType specialType)
		{
			return SpecialActivationEngine.ToCatalystType(specialType);
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

		private static bool InBounds(Vector2Int pos, int w, int h)
		{
			return pos.x >= 0 && pos.x < w && pos.y >= 0 && pos.y < h;
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
