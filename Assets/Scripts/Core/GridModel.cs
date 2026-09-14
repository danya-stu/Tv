using System;
using System.Collections.Generic;

namespace PotionCraft.Core
{
	/// <summary>
	/// Pure C# match-3 grid model. Lives in the PotionCraft.Core assembly,
	/// which has noEngineReferences enabled, so this class is physically
	/// unable to depend on UnityEngine.dll. That keeps simulation logic
	/// deterministic and unit-testable without a running Unity scene.
	/// </summary>
	public sealed class GridModel
	{
		private const int MinDimension = 3;

		public int Width { get; }
		public int Height { get; }

		private readonly Item[,] _grid;

		// Preallocated scratch buffers reused across calls (Fisher-Yates shuffle)
		// so board reshuffles never allocate on the heap.
		private readonly int[] _scratchX;
		private readonly int[] _scratchY;

		private readonly Random _random;

		public GridModel(int width, int height, int? seed = null)
		{
			if (width < MinDimension)
				throw new ArgumentOutOfRangeException(nameof(width), width, $"Width must be at least {MinDimension}.");
			if (height < MinDimension)
				throw new ArgumentOutOfRangeException(nameof(height), height, $"Height must be at least {MinDimension}.");

			Width = width;
			Height = height;
			_grid = new Item[width, height];

			int cellCount = width * height;
			_scratchX = new int[cellCount];
			_scratchY = new int[cellCount];

			_random = seed.HasValue ? new Random(seed.Value) : new Random();
		}

		public bool IsValidCoordinate(int x, int y)
		{
			return x >= 0 && x < Width && y >= 0 && y < Height;
		}

		public Item GetItem(int x, int y)
		{
			if (!IsValidCoordinate(x, y))
				throw new ArgumentOutOfRangeException($"Coordinate ({x},{y}) is outside grid bounds {Width}x{Height}.");

			return _grid[x, y];
		}

		public void SetItem(int x, int y, Item item)
		{
			if (!IsValidCoordinate(x, y))
				throw new ArgumentOutOfRangeException($"Coordinate ({x},{y}) is outside grid bounds {Width}x{Height}.");

			_grid[x, y] = item;
		}

		public void Swap(int x1, int y1, int x2, int y2)
		{
			if (!IsValidCoordinate(x1, y1))
				throw new ArgumentOutOfRangeException($"Coordinate ({x1},{y1}) is outside grid bounds {Width}x{Height}.");
			if (!IsValidCoordinate(x2, y2))
				throw new ArgumentOutOfRangeException($"Coordinate ({x2},{y2}) is outside grid bounds {Width}x{Height}.");

			Item temp = _grid[x1, y1];
			_grid[x1, y1] = _grid[x2, y2];
			_grid[x2, y2] = temp;
		}

		/// <summary>
		/// Fills the grid with random items such that no three-in-a-row exists
		/// horizontally or vertically at the moment of initialization.
		/// </summary>
		public void InitializeGrid(int colorCount = 5)
		{
			if (colorCount < 3 || colorCount > 5)
				throw new ArgumentOutOfRangeException(nameof(colorCount), colorCount, "colorCount must be between 3 and 5.");

			for (int y = 0; y < Height; y++)
			{
				for (int x = 0; x < Width; x++)
				{
					ItemColor color = PickNonMatchingColor(x, y, colorCount);
					_grid[x, y] = Item.Create(color);
				}
			}
		}

		private ItemColor PickNonMatchingColor(int x, int y, int colorCount)
		{
			// Try random colors first; a bounded linear scan of all colors is the
			// guaranteed fallback so this method can never loop indefinitely.
			int maxRandomAttempts = colorCount * 4;
			for (int attempt = 0; attempt < maxRandomAttempts; attempt++)
			{
				ItemColor candidate = (ItemColor)(_random.Next(colorCount) + 1);
				if (!CreatesInitialMatch(x, y, candidate))
					return candidate;
			}

			for (int c = 1; c <= colorCount; c++)
			{
				ItemColor candidate = (ItemColor)c;
				if (!CreatesInitialMatch(x, y, candidate))
					return candidate;
			}

			// Mathematically unreachable when colorCount >= 3 (a cell only has two
			// prior neighbours to conflict with), but returns deterministically
			// instead of leaving the cell undefined if that ever changes.
			return (ItemColor)1;
		}

		private bool CreatesInitialMatch(int x, int y, ItemColor candidate)
		{
			if (x >= 2 && _grid[x - 1, y].Color == candidate && _grid[x - 2, y].Color == candidate)
				return true;

			if (y >= 2 && _grid[x, y - 1].Color == candidate && _grid[x, y - 2].Color == candidate)
				return true;

			return false;
		}

		/// <summary>
		/// Returns true if at least one adjacent swap on the board would create
		/// a match of three or more. Only checks the right and up neighbours per
		/// cell, which covers every unordered adjacent pair on the grid exactly
		/// once, with no heap allocation.
		/// </summary>
		public bool HasPossibleMoves()
		{
			for (int y = 0; y < Height; y++)
			{
				for (int x = 0; x < Width; x++)
				{
					if (x + 1 < Width && SwapCreatesMatch(x, y, x + 1, y))
						return true;

					if (y + 1 < Height && SwapCreatesMatch(x, y, x, y + 1))
						return true;
				}
			}

			return false;
		}

		private bool SwapCreatesMatch(int x1, int y1, int x2, int y2)
		{
			Swap(x1, y1, x2, y2);
			bool createsMatch = HasMatchAt(x1, y1) || HasMatchAt(x2, y2);
			Swap(x1, y1, x2, y2); // revert the probe swap

			return createsMatch;
		}

		private bool HasMatchAt(int x, int y)
		{
			ItemColor color = _grid[x, y].Color;

			int horizontalRun = 1;
			for (int step = x - 1; step >= 0 && _grid[step, y].Color == color; step--)
				horizontalRun++;
			for (int step = x + 1; step < Width && _grid[step, y].Color == color; step++)
				horizontalRun++;
			if (horizontalRun >= 3)
				return true;

			int verticalRun = 1;
			for (int step = y - 1; step >= 0 && _grid[x, step].Color == color; step--)
				verticalRun++;
			for (int step = y + 1; step < Height && _grid[x, step].Color == color; step++)
				verticalRun++;

			return verticalRun >= 3;
		}

		/// <summary>
		/// Re-shuffles the existing items in place until the board has at least
		/// one possible move, bounded by (Width * Height * 2) attempts as
		/// required by the design standard. If the limit is reached, forces a
		/// guaranteed valid move near the centre of the board instead of
		/// looping indefinitely.
		/// </summary>
		public void ShuffleUntilSolvable()
		{
			int maxAttempts = Width * Height * 2;

			for (int attempt = 0; attempt < maxAttempts; attempt++)
			{
				if (HasPossibleMoves())
					return;

				ShuffleInPlace();
			}

			if (!HasPossibleMoves())
				ForceGuaranteedMove();
		}

		private void ShuffleInPlace()
		{
			// Fisher-Yates shuffle over the flattened grid, reusing the scratch
			// coordinate buffers allocated once in the constructor.
			int cellCount = Width * Height;

			for (int i = 0; i < cellCount; i++)
			{
				_scratchX[i] = i % Width;
				_scratchY[i] = i / Width;
			}

			for (int i = cellCount - 1; i > 0; i--)
			{
				int j = _random.Next(i + 1);

				int ax = _scratchX[i], ay = _scratchY[i];
				int bx = _scratchX[j], by = _scratchY[j];

				Item temp = _grid[ax, ay];
				_grid[ax, ay] = _grid[bx, by];
				_grid[bx, by] = temp;
			}
		}

		/// <summary>
		/// Deterministically writes a guaranteed valid move: a horizontal pair
		/// A-B-A at row y plus a matching A directly below the middle cell, so
		/// swapping (x+1,y) with (x+1,y+1) always completes an A-A-A row match.
		/// </summary>
		private void ForceGuaranteedMove()
		{
			int x = Math.Max(0, Math.Min(Width - 3, (Width / 2) - 1));
			int y = Math.Max(0, Math.Min(Height - 2, Height / 2));

			ItemColor color = _grid[x, y].Color;
			if (color == ItemColor.None)
				color = ItemColor.Red;

			ItemColor other = NextDifferentColor(color);

			_grid[x, y] = Item.Create(color);
			_grid[x + 1, y] = Item.Create(other);
			_grid[x + 2, y] = Item.Create(color);
			_grid[x + 1, y + 1] = Item.Create(color);
		}

		private static ItemColor NextDifferentColor(ItemColor color)
		{
			int next = (int)color + 1;
			if (next > 5)
				next = 1;

			return (ItemColor)next;
		}

		/// <summary>
		/// Clears every cell referenced by the given matches (sets them to
		/// Item.Empty), except that a big enough match (Line4, Line5Plus, or
		/// Cross) spawns a Reaction Catalyst item at one cell of the match
		/// instead of clearing it -- see CatalystTypeForMatch. Before clearing,
		/// any catalyst item swept up among the matched cells activates first
		/// (via ExpandForCatalystActivations), expanding the cleared area with
		/// its row-clear, column-clear, or color-bomb effect, chaining through
		/// any further catalysts that effect sweeps up. Typically called
		/// immediately after MatchFinder detects matches, so gravity/refill
		/// can process the vacated cells.
		/// </summary>
		public int RemoveMatches(IReadOnlyList<MatchGroup> matches)
		{
			var catalystSpawns = new Dictionary<(int X, int Y), Item>();
			var initialCells = new HashSet<(int X, int Y)>();

			foreach (MatchGroup match in matches)
			{
				foreach ((int X, int Y) cell in match.Cells)
					initialCells.Add(cell);

				CatalystType catalystType = CatalystTypeForMatch(match);
				if (catalystType != CatalystType.None)
				{
					(int X, int Y) spawnCell = CatalystSpawnCell(match);
					catalystSpawns[spawnCell] = Item.Create(match.Color, catalystType);
				}
			}

			HashSet<(int X, int Y)> cellsToClear = ExpandForCatalystActivations(initialCells);

			int clearedCount = 0;
			foreach ((int X, int Y) cell in cellsToClear)
			{
				if (_grid[cell.X, cell.Y].Color != ItemColor.None)
					clearedCount++;

				_grid[cell.X, cell.Y] = catalystSpawns.TryGetValue(cell, out Item spawn) ? spawn : Item.Empty;
			}

			return clearedCount;
		}

		/// <summary>
		/// Decides which Reaction Catalyst (if any) a match should spawn: a
		/// horizontal Line4 spawns RowClear, a vertical Line4 spawns
		/// ColumnClear, and a Line5Plus or Cross spawns a ColorBomb. A plain
		/// Line3 spawns nothing.
		/// </summary>
		private static CatalystType CatalystTypeForMatch(MatchGroup match)
		{
			switch (match.Shape)
			{
				case MatchShape.Line4:
					return IsHorizontalRun(match.Cells) ? CatalystType.RowClear : CatalystType.ColumnClear;
				case MatchShape.Line5Plus:
				case MatchShape.Cross:
					return CatalystType.ColorBomb;
				default:
					return CatalystType.None;
			}
		}

		private static bool IsHorizontalRun(IReadOnlyList<(int X, int Y)> cells)
		{
			int firstY = cells[0].Y;
			foreach ((int X, int Y) cell in cells)
			{
				if (cell.Y != firstY)
					return false;
			}

			return true;
		}

		/// <summary>
		/// Picks a deterministic cell (the geometric middle of the match, by
		/// sorted coordinate order rather than set-enumeration order) to host a
		/// newly spawned catalyst item.
		/// </summary>
		private static (int X, int Y) CatalystSpawnCell(MatchGroup match)
		{
			var sorted = new List<(int X, int Y)>(match.Cells);
			sorted.Sort((a, b) => a.X != b.X ? a.X.CompareTo(b.X) : a.Y.CompareTo(b.Y));
			return sorted[sorted.Count / 2];
		}

		/// <summary>
		/// Expands a set of just-matched cells to include every cell that any
		/// catalyst among those cells would additionally clear (a full row, a
		/// full column, or every cell sharing its color for a color bomb),
		/// following chained catalyst activations until no new catalyst is
		/// found. Bounded by total cell count so a pathological chain of
		/// catalysts can never loop forever.
		/// </summary>
		private HashSet<(int X, int Y)> ExpandForCatalystActivations(IEnumerable<(int X, int Y)> initialCells)
		{
			var cellsToClear = new HashSet<(int X, int Y)>(initialCells);
			var processed = new HashSet<(int X, int Y)>();
			int maxIterations = Width * Height;

			for (int iteration = 0; iteration < maxIterations; iteration++)
			{
				(int X, int Y)? next = null;
				foreach ((int X, int Y) cell in cellsToClear)
				{
					if (processed.Contains(cell))
						continue;

					if (_grid[cell.X, cell.Y].Catalyst != CatalystType.None)
					{
						next = cell;
						break;
					}
				}

				if (!next.HasValue)
					break;

				processed.Add(next.Value);
				AddCatalystEffectCells(next.Value, cellsToClear);
			}

			return cellsToClear;
		}

		private void AddCatalystEffectCells((int X, int Y) cell, HashSet<(int X, int Y)> cellsToClear)
		{
			Item item = _grid[cell.X, cell.Y];

			switch (item.Catalyst)
			{
				case CatalystType.RowClear:
					for (int x = 0; x < Width; x++)
						cellsToClear.Add((x, cell.Y));
					break;

				case CatalystType.ColumnClear:
					for (int y = 0; y < Height; y++)
						cellsToClear.Add((cell.X, y));
					break;

				case CatalystType.ColorBomb:
					for (int x = 0; x < Width; x++)
					{
						for (int y = 0; y < Height; y++)
						{
							if (_grid[x, y].Color == item.Color)
								cellsToClear.Add((x, y));
						}
					}
					break;
			}
		}

		/// <summary>
		/// Collapses each column so every non-empty item falls toward y = 0
		/// (the bottom row), leaving any leftover empty cells stacked at the top
		/// (highest y) of that column, ready for RefillEmptyCells.
		/// </summary>
		public void ApplyGravity()
		{
			for (int x = 0; x < Width; x++)
			{
				int writeY = 0;

				for (int readY = 0; readY < Height; readY++)
				{
					if (_grid[x, readY].Color == ItemColor.None)
						continue;

					if (writeY != readY)
					{
						_grid[x, writeY] = _grid[x, readY];
						_grid[x, readY] = Item.Empty;
					}

					writeY++;
				}
			}
		}

		/// <summary>
		/// Fills every remaining empty cell (expected to be stacked at the top
		/// of each column after ApplyGravity) with a freshly generated item,
		/// reusing the same non-matching-color guarantee as initial grid
		/// generation.
		/// </summary>
		public void RefillEmptyCells(int colorCount = 5)
		{
			for (int y = 0; y < Height; y++)
			{
				for (int x = 0; x < Width; x++)
				{
					if (_grid[x, y].Color != ItemColor.None)
						continue;

					ItemColor color = PickNonMatchingColor(x, y, colorCount);
					_grid[x, y] = Item.Create(color);
				}
			}
		}

		/// <summary>
		/// Runs the full match -> remove -> gravity -> refill loop until the
		/// board is stable (no more matches), bounded by (Width * Height) as a
		/// hard safety cap so a pathological board can never loop forever. Call
		/// this once after a player swap that created at least one match, to
		/// resolve the entire chain reaction (including cascades) in one call.
		/// </summary>
		public CascadeReport ResolveCascade(int colorCount = 5)
		{
			var allMatches = new List<MatchGroup>();
			int totalCellsCleared = 0;
			int stepCount = 0;
			int maxSteps = Width * Height;

			while (stepCount < maxSteps)
			{
				List<MatchGroup> matches = MatchFinder.FindAllMatches(this);
				if (matches.Count == 0)
					break;

				totalCellsCleared += RemoveMatches(matches);
				allMatches.AddRange(matches);
				ApplyGravity();
				RefillEmptyCells(colorCount);
				stepCount++;
			}

			return new CascadeReport(stepCount, totalCellsCleared, allMatches);
		}
	}
}
