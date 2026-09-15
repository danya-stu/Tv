using System;
using System.Collections.Generic;
using UnityEngine;

namespace PotionCraft.Core
{
	/// <summary>
	/// Outcome of a swap between two tiles where at least one carries a special
	/// effect. The collections are owned by the caller (pooled buffers), this
	/// struct only describes what happened so the presentation layer can pick
	/// the right VFX for the combination.
	/// </summary>
	public readonly struct ComboResult
	{
		public bool IsCombo { get; }
		public SpecialType ResultType { get; }
		public HashSet<Vector2Int> AffectedCells { get; }
		public List<Vector2Int> TransmutedCells { get; }
		public ItemColor TargetTransmuteColor { get; }

		public ComboResult(
			bool isCombo,
			SpecialType resultType,
			HashSet<Vector2Int> affectedCells,
			List<Vector2Int> transmutedCells,
			ItemColor targetTransmuteColor)
		{
			IsCombo = isCombo;
			ResultType = resultType;
			AffectedCells = affectedCells;
			TransmutedCells = transmutedCells;
			TargetTransmuteColor = targetTransmuteColor;
		}

		public int AffectedCount => AffectedCells == null ? 0 : AffectedCells.Count;
		public int TransmutedCount => TransmutedCells == null ? 0 : TransmutedCells.Count;

		public override string ToString() => $"ComboResult(IsCombo={IsCombo}, {ResultType}, Affected={AffectedCount}, Transmuted={TransmutedCount}, Target={TargetTransmuteColor})";
	}

	/// <summary>
	/// Pure C# activation engine for special tiles, pair combinations and the
	/// "Reaction Catalyst" color transmutation feature. No MonoBehaviour, no
	/// Transform, no coroutines: the engine mutates the logical grid and reports
	/// affected coordinates through caller-owned buffers.
	///
	/// GC Zero Allocation: no LINQ, no collection allocated in any hot method.
	/// The only internal state is a fixed color histogram reused via Array.Clear.
	///
	/// Not thread-safe by design: call from the game thread.
	/// </summary>
	public static class SpecialActivationEngine
	{
		/// <summary>Histogram over ItemColor (index 0 is None and stays unused).</summary>
		private static readonly int[] s_colorCounts = new int[6];

		/// <summary>Maps the effect stored on an item to its match-shape enum.</summary>
		public static SpecialType ToSpecialType(CatalystType catalyst)
		{
			switch (catalyst)
			{
				case CatalystType.HorizontalLine:
					return SpecialType.HorizontalLine;
				case CatalystType.VerticalLine:
					return SpecialType.VerticalLine;
				case CatalystType.BombArea:
					return SpecialType.BombArea;
				case CatalystType.ColorBomb:
					return SpecialType.ColorBomb;
				default:
					return SpecialType.None;
			}
		}

		/// <summary>Maps a match shape onto the effect carried by a spawned item.</summary>
		public static CatalystType ToCatalystType(SpecialType special)
		{
			switch (special)
			{
				case SpecialType.HorizontalLine:
					return CatalystType.HorizontalLine;
				case SpecialType.VerticalLine:
					return CatalystType.VerticalLine;
				case SpecialType.BombArea:
					return CatalystType.BombArea;
				case SpecialType.ColorBomb:
					return CatalystType.ColorBomb;
				default:
					return CatalystType.None;
			}
		}

		/// <summary>The special effect an item currently carries, if any.</summary>
		public static SpecialType GetSpecial(Item item)
		{
			return ToSpecialType(item.Catalyst);
		}

		/// <summary>True when at least one of the two swapped tiles carries a special effect.</summary>
		public static bool IsSpecialSwap(Item itemA, Item itemB)
		{
			return GetSpecial(itemA) != SpecialType.None || GetSpecial(itemB) != SpecialType.None;
		}

		/// <summary>
		/// True for a swap that must resolve through ResolveSpecialSwap even though
		/// it forms no classic match: any special pair, or a ColorBomb paired with a
		/// plain tile (the ColorBomb consumes that tile's color).
		/// </summary>
		public static bool IsActivatingSwap(Item itemA, Item itemB)
		{
			SpecialType specialA = GetSpecial(itemA);
			SpecialType specialB = GetSpecial(itemB);

			if (specialA != SpecialType.None && specialB != SpecialType.None)
			{
				return true;
			}

			if (specialA == SpecialType.ColorBomb && itemB.Color != ItemColor.None)
			{
				return true;
			}

			return specialB == SpecialType.ColorBomb && itemA.Color != ItemColor.None;
		}

		/// <summary>
		/// Resolves a swap involving special tiles. Every cell that must be removed
		/// is added to outDestroyedCells (a set, so overlapping blasts never double
		/// count), every cell rewritten by a ColorBomb combination is appended to
		/// outTransmutedCells, and outTransmuteColor receives the consumed color.
		/// The buffers are owned by the caller and are not cleared here.
		/// </summary>
		public static void ResolveSpecialSwap(
			Item[,] grid,
			int width,
			int height,
			Vector2Int posA,
			Vector2Int posB,
			HashSet<Vector2Int> outDestroyedCells,
			List<Vector2Int> outTransmutedCells,
			ref ItemColor outTransmuteColor)
		{
			if (grid == null)
				throw new ArgumentNullException(nameof(grid));
			if (outDestroyedCells == null)
				throw new ArgumentNullException(nameof(outDestroyedCells));
			if (outTransmutedCells == null)
				throw new ArgumentNullException(nameof(outTransmutedCells));

			int w = ClampWidth(grid, width);
			int h = ClampHeight(grid, height);

			if (!InBounds(posA, w, h) || !InBounds(posB, w, h))
			{
				return;
			}

			Item itemA = grid[posA.x, posA.y];
			Item itemB = grid[posB.x, posB.y];
			SpecialType specialA = GetSpecial(itemA);
			SpecialType specialB = GetSpecial(itemB);

			// Both swapped tiles are always consumed by the activation.
			outDestroyedCells.Add(posA);
			outDestroyedCells.Add(posB);

			// 1. ColorBomb + ColorBomb: the whole board is wiped.
			if (specialA == SpecialType.ColorBomb && specialB == SpecialType.ColorBomb)
			{
				AddWholeBoard(w, h, outDestroyedCells);
				outTransmuteColor = ItemColor.None;
				return;
			}

			// 2. ColorBomb + Line/Bomb: every tile of the partner's color becomes that
			// special and detonates immediately.
			if (specialA == SpecialType.ColorBomb && IsLineOrBomb(specialB))
			{
				ResolveColorBombWithSpecial(grid, w, h, posB, specialB, itemB.Color, outDestroyedCells, outTransmutedCells, ref outTransmuteColor);
				return;
			}

			if (specialB == SpecialType.ColorBomb && IsLineOrBomb(specialA))
			{
				ResolveColorBombWithSpecial(grid, w, h, posA, specialA, itemA.Color, outDestroyedCells, outTransmutedCells, ref outTransmuteColor);
				return;
			}

			// 3. ColorBomb + Regular: every tile of the plain tile's color is removed.
			if (specialA == SpecialType.ColorBomb && specialB == SpecialType.None)
			{
				outTransmuteColor = itemB.Color;
				AddAllOfColor(grid, w, h, itemB.Color, outDestroyedCells);
				return;
			}

			if (specialB == SpecialType.ColorBomb && specialA == SpecialType.None)
			{
				outTransmuteColor = itemA.Color;
				AddAllOfColor(grid, w, h, itemA.Color, outDestroyedCells);
				return;
			}

			bool lineA = IsLine(specialA);
			bool lineB = IsLine(specialB);
			bool bombA = specialA == SpecialType.BombArea;
			bool bombB = specialB == SpecialType.BombArea;

			// 4. Line + Line: a full cross centered on the destination cell.
			if (lineA && lineB)
			{
				AddRow(w, posB.y, h, outDestroyedCells);
				AddColumn(h, posB.x, w, outDestroyedCells);
				return;
			}

			// 5. Line + Bomb: a thick cross, three rows by three columns.
			if ((lineA && bombB) || (bombA && lineB))
			{
				for (int y = posB.y - 1; y <= posB.y + 1; y++)
				{
					AddRow(w, y, h, outDestroyedCells);
				}

				for (int x = posB.x - 1; x <= posB.x + 1; x++)
				{
					AddColumn(h, x, w, outDestroyedCells);
				}

				return;
			}

			// 6. Bomb + Bomb: a 5x5 blast around the destination cell.
			if (bombA && bombB)
			{
				AddRect(w, h, posB, 2, outDestroyedCells);
				return;
			}

			// Fallback: a single special swapped with a plain tile simply detonates.
			if (specialA != SpecialType.None)
			{
				ExplodeSingleSpecial(grid, w, h, posA, specialA, itemA.Color, outDestroyedCells);
			}

			if (specialB != SpecialType.None)
			{
				ExplodeSingleSpecial(grid, w, h, posB, specialB, itemB.Color, outDestroyedCells);
			}
		}

		/// <summary>
		/// Convenience wrapper returning a descriptive ComboResult over the same
		/// caller-owned buffers. The buffers are cleared before resolving.
		/// </summary>
		public static ComboResult ResolveSpecialSwapDetailed(
			Item[,] grid,
			int width,
			int height,
			Vector2Int posA,
			Vector2Int posB,
			HashSet<Vector2Int> destroyedBuffer,
			List<Vector2Int> transmutedBuffer)
		{
			if (grid == null)
				throw new ArgumentNullException(nameof(grid));
			if (destroyedBuffer == null)
				throw new ArgumentNullException(nameof(destroyedBuffer));
			if (transmutedBuffer == null)
				throw new ArgumentNullException(nameof(transmutedBuffer));

			destroyedBuffer.Clear();
			transmutedBuffer.Clear();

			int w = ClampWidth(grid, width);
			int h = ClampHeight(grid, height);

			SpecialType resultType = SpecialType.None;
			bool isCombo = false;

			if (InBounds(posA, w, h) && InBounds(posB, w, h))
			{
				Item itemA = grid[posA.x, posA.y];
				Item itemB = grid[posB.x, posB.y];
				SpecialType specialA = GetSpecial(itemA);
				SpecialType specialB = GetSpecial(itemB);

				isCombo = specialA != SpecialType.None && specialB != SpecialType.None;
				resultType = specialA == SpecialType.ColorBomb || specialB == SpecialType.ColorBomb
					? SpecialType.ColorBomb
					: (specialA == SpecialType.BombArea || specialB == SpecialType.BombArea
						? SpecialType.BombArea
						: (specialA != SpecialType.None ? specialA : specialB));
			}

			ItemColor transmuteColor = ItemColor.None;
			ResolveSpecialSwap(grid, w, h, posA, posB, destroyedBuffer, transmutedBuffer, ref transmuteColor);

			return new ComboResult(isCombo, resultType, destroyedBuffer, transmutedBuffer, transmuteColor);
		}

		/// <summary>
		/// "Reaction Catalyst": inside the square area of the given radius the most
		/// common plain color wins, and every other plain tile in that area is
		/// rewritten to it. Tiles carrying a special effect are neither counted nor
		/// overwritten, so the player never loses a catalyst to a transmutation.
		/// Ties are resolved towards the lowest ItemColor value, which keeps the
		/// outcome deterministic and reproducible in tests.
		/// </summary>
		public static void TriggerTransmutationCatalyst(
			Item[,] grid,
			int width,
			int height,
			Vector2Int center,
			int radius,
			List<Vector2Int> outTransmutedCells,
			out ItemColor dominantColor)
		{
			if (grid == null)
				throw new ArgumentNullException(nameof(grid));
			if (outTransmutedCells == null)
				throw new ArgumentNullException(nameof(outTransmutedCells));

			dominantColor = ItemColor.None;

			int w = ClampWidth(grid, width);
			int h = ClampHeight(grid, height);

			if (w <= 0 || h <= 0 || radius < 0)
			{
				return;
			}

			int minX = center.x - radius < 0 ? 0 : center.x - radius;
			int maxX = center.x + radius > w - 1 ? w - 1 : center.x + radius;
			int minY = center.y - radius < 0 ? 0 : center.y - radius;
			int maxY = center.y + radius > h - 1 ? h - 1 : center.y + radius;

			if (minX > maxX || minY > maxY)
			{
				return;
			}

			Array.Clear(s_colorCounts, 0, s_colorCounts.Length);

			// 1 and 2. Count the plain colors present in the area.
			for (int x = minX; x <= maxX; x++)
			{
				for (int y = minY; y <= maxY; y++)
				{
					Item item = grid[x, y];
					if (item.Color == ItemColor.None)
					{
						continue;
					}

					if (GetSpecial(item) != SpecialType.None)
					{
						continue;
					}

					int colorIndex = (int)item.Color;
					if (colorIndex > 0 && colorIndex < s_colorCounts.Length)
					{
						s_colorCounts[colorIndex]++;
					}
				}
			}

			int bestCount = 0;
			for (int colorIndex = 1; colorIndex < s_colorCounts.Length; colorIndex++)
			{
				if (s_colorCounts[colorIndex] > bestCount)
				{
					bestCount = s_colorCounts[colorIndex];
					dominantColor = (ItemColor)colorIndex;
				}
			}

			if (dominantColor == ItemColor.None)
			{
				return;
			}

			// 3 and 4. Rewrite every other plain tile to the dominant color.
			for (int x = minX; x <= maxX; x++)
			{
				for (int y = minY; y <= maxY; y++)
				{
					Item item = grid[x, y];
					if (item.Color == ItemColor.None || item.Color == dominantColor)
					{
						continue;
					}

					if (GetSpecial(item) != SpecialType.None)
					{
						continue;
					}

					grid[x, y] = Item.Create(dominantColor);
					outTransmutedCells.Add(new Vector2Int(x, y));
				}
			}
		}

		/// <summary>
		/// Applies the blast pattern of one special tile: a full row, a full column,
		/// a 3x3 area, or every tile of the given color for a ColorBomb. The origin
		/// cell is always included. Cells are only reported, never cleared here, so
		/// the caller can chain further detonations before mutating the grid.
		/// </summary>
		public static void ExplodeSingleSpecial(
			Item[,] grid,
			int width,
			int height,
			Vector2Int pos,
			SpecialType special,
			ItemColor color,
			HashSet<Vector2Int> outDestroyedCells)
		{
			if (grid == null)
				throw new ArgumentNullException(nameof(grid));
			if (outDestroyedCells == null)
				throw new ArgumentNullException(nameof(outDestroyedCells));

			int w = ClampWidth(grid, width);
			int h = ClampHeight(grid, height);

			if (!InBounds(pos, w, h))
			{
				return;
			}

			outDestroyedCells.Add(pos);

			switch (special)
			{
				case SpecialType.HorizontalLine:
					AddRow(w, pos.y, h, outDestroyedCells);
					break;
				case SpecialType.VerticalLine:
					AddColumn(h, pos.x, w, outDestroyedCells);
					break;
				case SpecialType.BombArea:
					AddRect(w, h, pos, 1, outDestroyedCells);
					break;
				case SpecialType.ColorBomb:
					AddAllOfColor(grid, w, h, color, outDestroyedCells);
					break;
				default:
					break;
			}
		}

		private static void ResolveColorBombWithSpecial(
			Item[,] grid,
			int w,
			int h,
			Vector2Int partnerPos,
			SpecialType partnerSpecial,
			ItemColor partnerColor,
			HashSet<Vector2Int> outDestroyedCells,
			List<Vector2Int> outTransmutedCells,
			ref ItemColor outTransmuteColor)
		{
			outTransmuteColor = partnerColor;

			if (partnerColor == ItemColor.None)
			{
				ExplodeSingleSpecial(grid, w, h, partnerPos, partnerSpecial, partnerColor, outDestroyedCells);
				return;
			}

			CatalystType catalyst = ToCatalystType(partnerSpecial);
			int firstIndex = outTransmutedCells.Count;

			// Upgrade every plain tile of the partner's color into the same special.
			for (int x = 0; x < w; x++)
			{
				for (int y = 0; y < h; y++)
				{
					Item item = grid[x, y];
					if (item.Color != partnerColor)
					{
						continue;
					}

					if (GetSpecial(item) != SpecialType.None)
					{
						continue;
					}

					grid[x, y] = Item.Create(partnerColor, catalyst);
					outTransmutedCells.Add(new Vector2Int(x, y));
				}
			}

			// Then detonate all of them, plus the partner tile itself.
			for (int i = firstIndex; i < outTransmutedCells.Count; i++)
			{
				ExplodeSingleSpecial(grid, w, h, outTransmutedCells[i], partnerSpecial, partnerColor, outDestroyedCells);
			}

			ExplodeSingleSpecial(grid, w, h, partnerPos, partnerSpecial, partnerColor, outDestroyedCells);
		}

		private static bool IsLine(SpecialType special)
		{
			return special == SpecialType.HorizontalLine || special == SpecialType.VerticalLine;
		}

		private static bool IsLineOrBomb(SpecialType special)
		{
			return IsLine(special) || special == SpecialType.BombArea;
		}

		private static void AddWholeBoard(int w, int h, HashSet<Vector2Int> cells)
		{
			for (int x = 0; x < w; x++)
			{
				for (int y = 0; y < h; y++)
				{
					cells.Add(new Vector2Int(x, y));
				}
			}
		}

		private static void AddAllOfColor(Item[,] grid, int w, int h, ItemColor color, HashSet<Vector2Int> cells)
		{
			if (color == ItemColor.None)
			{
				return;
			}

			for (int x = 0; x < w; x++)
			{
				for (int y = 0; y < h; y++)
				{
					if (grid[x, y].Color == color)
					{
						cells.Add(new Vector2Int(x, y));
					}
				}
			}
		}

		private static void AddRow(int w, int y, int h, HashSet<Vector2Int> cells)
		{
			if (y < 0 || y >= h)
			{
				return;
			}

			for (int x = 0; x < w; x++)
			{
				cells.Add(new Vector2Int(x, y));
			}
		}

		private static void AddColumn(int h, int x, int w, HashSet<Vector2Int> cells)
		{
			if (x < 0 || x >= w)
			{
				return;
			}

			for (int y = 0; y < h; y++)
			{
				cells.Add(new Vector2Int(x, y));
			}
		}

		private static void AddRect(int w, int h, Vector2Int center, int radius, HashSet<Vector2Int> cells)
		{
			int minX = center.x - radius < 0 ? 0 : center.x - radius;
			int maxX = center.x + radius > w - 1 ? w - 1 : center.x + radius;
			int minY = center.y - radius < 0 ? 0 : center.y - radius;
			int maxY = center.y + radius > h - 1 ? h - 1 : center.y + radius;

			for (int x = minX; x <= maxX; x++)
			{
				for (int y = minY; y <= maxY; y++)
				{
					cells.Add(new Vector2Int(x, y));
				}
			}
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
