using System;
using System.Collections.Generic;
using NUnit.Framework;
using PotionCraft.Core;

namespace PotionCraft.Tests.Editor
{
	[TestFixture]
	public class GameSessionTests
	{
		[Test]
		public void Constructor_InitializesState()
		{
			var session = new GameSession(targetScore: 100, maxMoves: 10);

			Assert.AreEqual(0, session.Score);
			Assert.AreEqual(10, session.MovesRemaining);
			Assert.AreEqual(GameSessionState.InProgress, session.State);
		}

		[TestCase(0)]
		[TestCase(-5)]
		public void Constructor_ThrowsForNonPositiveTargetScore(int targetScore)
		{
			Assert.Throws<ArgumentOutOfRangeException>(() => new GameSession(targetScore, maxMoves: 10));
		}

		[TestCase(0)]
		[TestCase(-1)]
		public void Constructor_ThrowsForNonPositiveMaxMoves(int maxMoves)
		{
			Assert.Throws<ArgumentOutOfRangeException>(() => new GameSession(targetScore: 100, maxMoves));
		}

		[Test]
		public void RegisterCascade_DecrementsMovesAndAddsScore()
		{
			var session = new GameSession(targetScore: 1000, maxMoves: 5);
			var report = new CascadeReport(stepCount: 1, totalCellsCleared: 3, allMatches: new List<MatchGroup>());

			session.RegisterCascade(report);

			Assert.AreEqual(30, session.Score);
			Assert.AreEqual(4, session.MovesRemaining);
			Assert.AreEqual(GameSessionState.InProgress, session.State);
		}

		[Test]
		public void RegisterCascade_WinsWhenScoreReachesTarget()
		{
			var session = new GameSession(targetScore: 30, maxMoves: 5);
			var report = new CascadeReport(stepCount: 1, totalCellsCleared: 3, allMatches: new List<MatchGroup>());

			session.RegisterCascade(report);

			Assert.AreEqual(GameSessionState.Won, session.State);
		}

		[Test]
		public void RegisterCascade_LosesWhenMovesRunOutWithoutReachingTarget()
		{
			var session = new GameSession(targetScore: 10000, maxMoves: 1);
			var report = new CascadeReport(stepCount: 1, totalCellsCleared: 3, allMatches: new List<MatchGroup>());

			session.RegisterCascade(report);

			Assert.AreEqual(0, session.MovesRemaining);
			Assert.AreEqual(GameSessionState.Lost, session.State);
		}

		[Test]
		public void RegisterCascade_IgnoresCallsAfterSessionEnded()
		{
			var session = new GameSession(targetScore: 30, maxMoves: 5);
			var report = new CascadeReport(stepCount: 1, totalCellsCleared: 3, allMatches: new List<MatchGroup>());

			session.RegisterCascade(report); // Wins here.
			int scoreAfterWin = session.Score;
			int movesAfterWin = session.MovesRemaining;

			session.RegisterCascade(report); // Should be ignored.

			Assert.AreEqual(scoreAfterWin, session.Score);
			Assert.AreEqual(movesAfterWin, session.MovesRemaining);
			Assert.AreEqual(GameSessionState.Won, session.State);
		}
	}
}
