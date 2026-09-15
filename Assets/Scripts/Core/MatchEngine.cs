using System;
using System.Collections.Generic;
using UnityEngine;

namespace PotionCraft.Core
{
	/// <summary>
	/// Pure C# high-performance Match-3 detection engine.
	/// Zero GC allocations during search: uses reusable scratch buffers and no LINQ.
	/// Completely decoupled from UI, GameObject, and Monobehaviour.
	/// </summary>
	public static class MatchEngine
	{
		private readonly struct RawLine
		{
			public readonly int StartX;
			public readonly int StartY;
			public readonly int Length;
			public readonly bool IsHorizontal;
			public readonly ItemColor Color;

			public RawLine(int startX, int startY, int length, bool isHorizontal, ItemColor color)
			{
				StartX = startX;
				StartY = startY;
				Length = length;
				IsHorizontal = isHorizontal;
				Color = color;
			}
		}

		// Pre-allocated scratch buffers to guarantee GC Zero Allocation on hot paths
		private static readonly List<RawLine> s_horizontalLines = new List<RawLine>(64);
		private static readonly List<RawLine> s_verticalLines = new List<RawLine>(64);
		private static readonly List<RawLine> s_allLines = new List<RawLine>(128);
		private static readonly List<int> s_uniqueRoots = new List<int>(64);
		private static readonly List<RawLine> s_componentLines = new List<RawLine>(16);
		private static readonly List<Vector2Int> s_componentCells = new List<Vector2Int>(32);
		private static readonly List<Vector2Int> s_componentIntersections = new List<Vector2Int>(8);
		private static readonly HashSet<Vector2Int> s_matchedTilesSet = new HashSet<Vector2Int>(128);

		private static int[] s_parent = new int[128];

		/// <summary>
		/// Scans the grid for contiguous matches of 3 or more same-colored items.
		/// Populates outReport with matched coordinates, spawned special tiles, and gathered essence counts.
		/// </summary>
		/// <param name="grid">2D array of items [width, height].</param>
		/// <param name="width">Width of the grid.</param>
		/// <param name="height">Height of the grid.</param>
		/// <param name="outReport">Reusable report to receive match results.</param>
		/// <param name="lastSwappedTile">Optional destination coordinate of the player's swipe.</param>
		/// <returns>True if at least one match of 3+ tiles was found; false otherwise.</returns>
		public static bool FindMatches(Item[,] grid, int width, int height, MatchReport outReport, Vector2Int? lastSwappedTile = null)
		{
			if (grid == null)
				throw new ArgumentNullException(nameof(grid));
			if (outReport == null)
				throw new ArgumentNullException(nameof(outReport));

			outReport.Clear();

			if (width < 3 && height < 3)
				return false;

			s_horizontalLines.Clear();
			s_verticalLines.Clear();
			s_allLines.Clear();
			s_uniqueRoots.Clear();
			s_matchedTilesSet.Clear();

			// 1. Scan horizontally for contiguous lines of >= 3 same-colored items
			for (int y = 0; y < height; y++)
			{
				int runStart = 0;
				ItemColor runColor = ItemColor.None;

				for (int x = 0; x <= width; x++)
				{
					ItemColor currentColor = (x < width) ? grid[x, y].Color : ItemColor.None;
					bool continuesRun = (x < width) && (currentColor != ItemColor.None) && (currentColor == runColor);

					if (!continuesRun)
					{
						int runLength = x - runStart;
						if (runLength >= 3 && runColor != ItemColor.None)
						{
							s_horizontalLines.Add(new RawLine(runStart, y, runLength, true, runColor));
						}

						runStart = x;
						runColor = currentColor;
					}
				}
			}

			// 2. Scan vertically for contiguous lines of >= 3 same-colored items
			for (int x = 0; x < width; x++)
			{
				int runStart = 0;
				ItemColor runColor = ItemColor.None;

				for (int y = 0; y <= height; y++)
				{
					ItemColor currentColor = (y < height) ? grid[x, y].Color : ItemColor.None;
					bool continuesRun = (y < height) && (currentColor != ItemColor.None) && (currentColor == runColor);

					if (!continuesRun)
					{
						int runLength = y - runStart;
						if (runLength >= 3 && runColor != ItemColor.None)
						{
							s_verticalLines.Add(new RawLine(x, runStart, runLength, false, runColor));
						}

						runStart = y;
						runColor = currentColor;
					}
				}
			}

			int hCount = s_horizontalLines.Count;
			int vCount = s_verticalLines.Count;
			int totalLines = hCount + vCount;

			if (totalLines == 0)
				return false;

			// Ensure capacity for disjoint-set forest
			if (s_parent.Length < totalLines)
			{
				int newSize = Math.Max(totalLines, s_parent.Length * 2);
				s_parent = new int[newSize];
			}

			for (int i = 0; i < totalLines; i++)
			{
				s_parent[i] = i;
			}

			s_allLines.AddRange(s_horizontalLines);
			s_allLines.AddRange(s_verticalLines);

			// 3. Connect intersecting horizontal and vertical runs of the same color (L, T, cross shapes)
			for (int h = 0; h < hCount; h++)
			{
				RawLine hLine = s_horizontalLines[h];
				for (int v = 0; v < vCount; v++)
				{
					RawLine vLine = s_verticalLines[v];
					if (LinesIntersect(in hLine, in vLine, out _))
					{
						Union(s_parent, h, hCount + v);
					}
				}
			}

			// Find all unique component roots
			for (int i = 0; i < totalLines; i++)
			{
				int root = Find(s_parent, i);
				if (!s_uniqueRoots.Contains(root))
				{
					s_uniqueRoots.Add(root);
				}
			}

			// Process each match group component
			for (int r = 0; r < s_uniqueRoots.Count; r++)
			{
				int root = s_uniqueRoots[r];
				s_componentLines.Clear();
				s_componentCells.Clear();
				s_componentIntersections.Clear();

				for (int i = 0; i < totalLines; i++)
				{
					if (Find(s_parent, i) == root)
					{
						s_componentLines.Add(s_allLines[i]);
					}
				}

				if (s_componentLines.Count == 0)
					continue;

				ItemColor groupColor = s_componentLines[0].Color;

				// Collect all cells belonging to this component
				for (int l = 0; l < s_componentLines.Count; l++)
				{
					RawLine line = s_componentLines[l];
					for (int step = 0; step < line.Length; step++)
					{
						Vector2Int cell = line.IsHorizontal
							? new Vector2Int(line.StartX + step, line.StartY)
							: new Vector2Int(line.StartX, line.StartY + step);

						if (!s_componentCells.Contains(cell))
						{
							s_componentCells.Add(cell);
						}
					}
				}

				// Find intersection coordinates for multi-line combinations
				if (s_componentLines.Count > 1)
				{
					for (int i = 0; i < s_componentLines.Count; i++)
					{
						RawLine lineA = s_componentLines[i];
						if (!lineA.IsHorizontal)
							continue;

						for (int j = 0; j < s_componentLines.Count; j++)
						{
							RawLine lineB = s_componentLines[j];
							if (lineB.IsHorizontal)
								continue;

							if (LinesIntersect(in lineA, in lineB, out Vector2Int intersection))
							{
								if (!s_componentIntersections.Contains(intersection))
								{
									s_componentIntersections.Add(intersection);
								}
							}
						}
					}
				}

				// Check if any line in the component has Length >= 5
				bool hasLine5Plus = false;
				RawLine line5 = default;
				for (int l = 0; l < s_componentLines.Count; l++)
				{
					if (s_componentLines[l].Length >= 5)
					{
						hasLine5Plus = true;
						line5 = s_componentLines[l];
						break;
					}
				}

				// Determine created special bonus
				SpecialType specialType = SpecialType.None;
				if (hasLine5Plus)
				{
					// Technical Supervisor Priority: Length >= 5 ALWAYS creates ColorBomb, even in an intersecting cluster
					specialType = SpecialType.ColorBomb;
				}
				else if (s_componentLines.Count > 1)
				{
					// BombArea is created ONLY if intersecting lines are 3-4 tiles long
					specialType = SpecialType.BombArea;
				}
				else
				{
					RawLine singleLine = s_componentLines[0];
					if (singleLine.Length == 4)
					{
						// Line of 4 items: horizontal match spawns VerticalLine (to clear column),
						// and vertical match spawns HorizontalLine (to clear row)
						specialType = singleLine.IsHorizontal ? SpecialType.VerticalLine : SpecialType.HorizontalLine;
					}
				}

				// Determine the position for the special bonus
				if (specialType != SpecialType.None)
				{
					Vector2Int spawnPosition;

					// If lastSwappedTile is provided and inside this match, spawn exactly at the swapped cell
					if (lastSwappedTile.HasValue && s_componentCells.Contains(lastSwappedTile.Value))
					{
						spawnPosition = lastSwappedTile.Value;
					}
					else
					{
						// Otherwise (cascade or unswapped match), place in the geometric center of the combination
						if (hasLine5Plus)
						{
							spawnPosition = line5.IsHorizontal
								? new Vector2Int(line5.StartX + line5.Length / 2, line5.StartY)
								: new Vector2Int(line5.StartX, line5.StartY + line5.Length / 2);
						}
						else if (s_componentLines.Count > 1 && s_componentIntersections.Count > 0)
						{
							// Center of an L/T shape is its intersection point
							spawnPosition = s_componentIntersections[0];
						}
						else if (s_componentLines.Count == 1)
						{
							RawLine line = s_componentLines[0];
							spawnPosition = line.IsHorizontal
								? new Vector2Int(line.StartX + line.Length / 2, line.StartY)
								: new Vector2Int(line.StartX, line.StartY + line.Length / 2);
						}
						else
						{
							spawnPosition = s_componentCells[s_componentCells.Count / 2];
						}
					}

					outReport.CreatedSpecials.Add(new SpecialCreationInfo(spawnPosition, specialType, groupColor));
				}

				// Add all unique cells to outReport and increment essence counts
				for (int c = 0; c < s_componentCells.Count; c++)
				{
					Vector2Int cell = s_componentCells[c];
					if (s_matchedTilesSet.Add(cell))
					{
						outReport.MatchedTiles.Add(cell);

						ItemColor cellColor = grid[cell.x, cell.y].Color;
						if (outReport.GatheredEssences.TryGetValue(cellColor, out int count))
						{
							outReport.GatheredEssences[cellColor] = count + 1;
						}
						else
						{
							outReport.GatheredEssences[cellColor] = 1;
						}
					}
				}
			}

			return outReport.MatchedTiles.Count > 0;
		}

		private static bool LinesIntersect(in RawLine h, in RawLine v, out Vector2Int intersection)
		{
			if (h.Color == v.Color &&
				v.StartX >= h.StartX && v.StartX < h.StartX + h.Length &&
				h.StartY >= v.StartY && h.StartY < v.StartY + v.Length)
			{
				intersection = new Vector2Int(v.StartX, h.StartY);
				return true;
			}

			intersection = default;
			return false;
		}

		private static int Find(int[] parent, int i)
		{
			while (parent[i] != i)
			{
				parent[i] = parent[parent[i]];
				i = parent[i];
			}

			return i;
		}

		private static void Union(int[] parent, int a, int b)
		{
			int rootA = Find(parent, a);
			int rootB = Find(parent, b);
			if (rootA != rootB)
			{
				parent[rootA] = rootB;
			}
		}
	}
}
