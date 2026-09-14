namespace PotionCraft.Core
{
	/// <summary>
	/// Pure scoring rules for a resolved cascade. Kept separate from
	/// GameSession so the scoring formula can be tuned and tested in
	/// isolation from move/win-loss bookkeeping.
	/// </summary>
	public static class ScoreCalculator
	{
		private const int PointsPerCell = 10;
		private const int ComboStepBonus = 25;
		private const int Line4Bonus = 20;
		private const int Line5PlusOrCrossBonus = 40;

		public static int CalculateScore(CascadeReport report)
		{
			int score = report.TotalCellsCleared * PointsPerCell;

			// Each additional chain-reaction step beyond the first is a
			// player-triggered combo (gravity refill causing a new match on
			// its own), so it earns a flat bonus on top of the cleared cells.
			if (report.StepCount > 1)
				score += (report.StepCount - 1) * ComboStepBonus;

			foreach (MatchGroup match in report.AllMatches)
			{
				switch (match.Shape)
				{
					case MatchShape.Line4:
						score += Line4Bonus;
						break;
					case MatchShape.Line5Plus:
					case MatchShape.Cross:
						score += Line5PlusOrCrossBonus;
						break;
				}
			}

			return score;
		}
	}
}
