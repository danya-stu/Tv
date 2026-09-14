using System.Collections.Generic;

namespace PotionCraft.Core
{
	/// <summary>
	/// Summarizes one full ResolveCascade call: how many chain-reaction steps
	/// it took to settle the board, how many cells were cleared in total, and
	/// every match group that fired (in resolution order), for scoring and
	/// cascade-animation triggers on the Unity side later.
	/// </summary>
	public sealed class CascadeReport
	{
		public int StepCount { get; }
		public int TotalCellsCleared { get; }
		public IReadOnlyList<MatchGroup> AllMatches { get; }

		public CascadeReport(int stepCount, int totalCellsCleared, IReadOnlyList<MatchGroup> allMatches)
		{
			StepCount = stepCount;
			TotalCellsCleared = totalCellsCleared;
			AllMatches = allMatches;
		}
	}
}
