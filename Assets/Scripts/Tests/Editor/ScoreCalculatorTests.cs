using System.Collections.Generic;
using NUnit.Framework;
using PotionCraft.Core;

namespace PotionCraft.Tests.Editor
{
	[TestFixture]
	public class ScoreCalculatorTests
	{
		[Test]
		public void CalculateScore_BasicClear_AwardsPointsPerCellOnly()
		{
			var match = new MatchGroup(ItemColor.Red, new List<(int X, int Y)> { (0, 0), (1, 0), (2, 0) }, MatchShape.Line3);
			var report = new CascadeReport(stepCount: 1, totalCellsCleared: 3, allMatches: new List<MatchGroup> { match });

			int score = ScoreCalculator.CalculateScore(report);

			Assert.AreEqual(30, score);
		}

		[Test]
		public void CalculateScore_MultipleSteps_AddsComboBonusPerExtraStep()
		{
			var report = new CascadeReport(stepCount: 3, totalCellsCleared: 6, allMatches: new List<MatchGroup>());

			int score = ScoreCalculator.CalculateScore(report);

			// 6 cells * 10 + 2 extra steps * 25
			Assert.AreEqual(110, score);
		}

		[Test]
		public void CalculateScore_Line4Match_AddsLine4Bonus()
		{
			var match = new MatchGroup(ItemColor.Blue, new List<(int X, int Y)> { (0, 0), (1, 0), (2, 0), (3, 0) }, MatchShape.Line4);
			var report = new CascadeReport(stepCount: 1, totalCellsCleared: 4, allMatches: new List<MatchGroup> { match });

			int score = ScoreCalculator.CalculateScore(report);

			// 4 cells * 10 + 20 bonus
			Assert.AreEqual(60, score);
		}

		[Test]
		public void CalculateScore_CrossMatch_AddsLine5PlusOrCrossBonus()
		{
			var match = new MatchGroup(ItemColor.Green, new List<(int X, int Y)> { (0, 0), (1, 0), (2, 0), (2, 1), (2, 2) }, MatchShape.Cross);
			var report = new CascadeReport(stepCount: 1, totalCellsCleared: 5, allMatches: new List<MatchGroup> { match });

			int score = ScoreCalculator.CalculateScore(report);

			// 5 cells * 10 + 40 bonus
			Assert.AreEqual(90, score);
		}
	}
}
