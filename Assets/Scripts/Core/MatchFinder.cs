using System.Collections.Generic;

namespace PotionCraft.Core
{
	/// <summary>
	/// Classifies the geometry of a detected match. Used later to decide
	/// which special item (if any) a match should spawn.
	/// </summary>
	public enum MatchShape
	{
		Line3,
		Line4,
		Line5Plus,
		Cross // A horizontal run and a vertical run intersecting at a shared cell (L, T, or plus shape).
	}

	/// <summary>
	/// One detected group of matched cells: every cell shares Color, and the
	/// group as a whole forms a single connected match (a straight line, or a
	/// horizontal+vertical run crossing at a shared cell).
	/// </summary>
	public sealed class MatchGroup
	{
		public ItemColor Color { get; }
		public IReadOnlyList<(int X, int Y)> Cells { get; }
		public MatchShape Shape { get; }

		public MatchGroup(ItemColor color, IReadOnlyList<(int X, int Y)> cells, MatchShape shape)
		{
			Color = color;
			Cells = cells;
			Shape = shape;
		}
	}

	/// <summary>
	/// Pure C# match detection over a GridModel. Scans for maximal runs of
	/// three or more same-colored cells in a row/column, then merges any
	/// horizontal and vertical runs that cross at a shared cell into a single
	/// Cross-shaped group, matching the classic match-3 T/L bonus.
	/// </summary>
	public static class MatchFinder
	{
		public static List<MatchGroup> FindAllMatches(GridModel grid)
		{
			List<Run> horizontalRuns = FindRuns(grid, isHorizontal: true);
			List<Run> verticalRuns = FindRuns(grid, isHorizontal: false);

			int runCount = horizontalRuns.Count + verticalRuns.Count;
			var allRuns = new List<Run>(runCount);
			allRuns.AddRange(horizontalRuns);
			allRuns.AddRange(verticalRuns);

			var parent = new int[runCount];
			for (int i = 0; i < runCount; i++)
				parent[i] = i;

			// A horizontal run and a vertical run can only ever share a cell
			// with each other (two maximal horizontal runs never overlap, and
			// likewise for two vertical runs), so this is the only pairing we
			// need to check when merging into cross-shaped groups.
			for (int i = 0; i < horizontalRuns.Count; i++)
			{
				for (int j = 0; j < verticalRuns.Count; j++)
				{
					if (RunsShareCell(horizontalRuns[i], verticalRuns[j]))
						Union(parent, i, horizontalRuns.Count + j);
				}
			}

			var membersByRoot = new Dictionary<int, List<int>>();
			for (int i = 0; i < runCount; i++)
			{
				int root = Find(parent, i);
				if (!membersByRoot.TryGetValue(root, out List<int> members))
				{
					members = new List<int>();
					membersByRoot[root] = members;
				}

				members.Add(i);
			}

			var result = new List<MatchGroup>(membersByRoot.Count);
			foreach (List<int> members in membersByRoot.Values)
			{
				var cellSet = new HashSet<(int X, int Y)>();
				foreach (int index in members)
				{
					foreach ((int X, int Y) cell in allRuns[index].Cells)
						cellSet.Add(cell);
				}

				ItemColor color = allRuns[members[0]].Color;

				MatchShape shape;
				if (members.Count > 1)
				{
					shape = MatchShape.Cross;
				}
				else
				{
					int length = allRuns[members[0]].Cells.Count;
					shape = length >= 5 ? MatchShape.Line5Plus : (length == 4 ? MatchShape.Line4 : MatchShape.Line3);
				}

				result.Add(new MatchGroup(color, new List<(int X, int Y)>(cellSet), shape));
			}

			return result;
		}

		private readonly struct Run
		{
			public ItemColor Color { get; }
			public List<(int X, int Y)> Cells { get; }

			public Run(ItemColor color, List<(int X, int Y)> cells)
			{
				Color = color;
				Cells = cells;
			}
		}

		private static List<Run> FindRuns(GridModel grid, bool isHorizontal)
		{
			var runs = new List<Run>();
			int outerCount = isHorizontal ? grid.Height : grid.Width;
			int innerCount = isHorizontal ? grid.Width : grid.Height;

			for (int outer = 0; outer < outerCount; outer++)
			{
				int runStart = 0;
				ItemColor runColor = ItemColor.None;

				// One extra virtual step (inner == innerCount) with a sentinel
				// None color flushes any run still open at the end of the row
				// or column, so the closing logic only needs to live in one place.
				for (int inner = 0; inner <= innerCount; inner++)
				{
					ItemColor currentColor = inner < innerCount
						? GetColor(grid, isHorizontal, outer, inner)
						: ItemColor.None;

					bool continuesRun = inner < innerCount && currentColor != ItemColor.None && currentColor == runColor;

					if (!continuesRun)
					{
						int runLength = inner - runStart;
						if (runLength >= 3 && runColor != ItemColor.None)
							runs.Add(BuildRun(isHorizontal, outer, runStart, runLength, runColor));

						runStart = inner;
						runColor = currentColor;
					}
				}
			}

			return runs;
		}

		private static ItemColor GetColor(GridModel grid, bool isHorizontal, int outer, int inner)
		{
			return isHorizontal ? grid.GetItem(inner, outer).Color : grid.GetItem(outer, inner).Color;
		}

		private static Run BuildRun(bool isHorizontal, int outer, int start, int length, ItemColor color)
		{
			var cells = new List<(int X, int Y)>(length);
			for (int i = 0; i < length; i++)
			{
				int inner = start + i;
				cells.Add(isHorizontal ? (inner, outer) : (outer, inner));
			}

			return new Run(color, cells);
		}

		private static bool RunsShareCell(Run a, Run b)
		{
			foreach ((int X, int Y) cellA in a.Cells)
			{
				foreach ((int X, int Y) cellB in b.Cells)
				{
					if (cellA.X == cellB.X && cellA.Y == cellB.Y)
						return true;
				}
			}

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
				parent[rootA] = rootB;
		}
	}
}
