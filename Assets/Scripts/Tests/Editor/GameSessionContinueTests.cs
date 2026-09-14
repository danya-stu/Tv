using System.Collections.Generic;
using NUnit.Framework;
using PotionCraft.Core;

namespace PotionCraft.Tests.Editor
{
	[TestFixture]
	public class GameSessionContinueTests
	{
		private static GameSession MakeLostSession(int maxContinues = 1)
		{
			// 1 move, and an empty cascade report scores 0, so registering a
			// single no-match cascade immediately exhausts moves and ends the
			// session Lost without needing a real board.
			var session = new GameSession(targetScore: 1000, maxMoves: 1, orderBook: null, maxContinues: maxContinues);
			session.RegisterCascade(EmptyReport());
			return session;
		}

		private static CascadeReport EmptyReport()
		{
			return new CascadeReport(0, 0, new List<MatchGroup>());
		}

		[Test]
		public void CanContinueWithAd_FalseWhileInProgress()
		{
			var session = new GameSession(1000, 5);

			Assert.IsFalse(session.CanContinueWithAd);
		}

		[Test]
		public void TryContinueWithBonusMoves_FailsWhileInProgress()
		{
			var session = new GameSession(1000, 5);

			bool granted = session.TryContinueWithBonusMoves(5);

			Assert.IsFalse(granted);
			Assert.AreEqual(GameSessionState.InProgress, session.State);
		}

		[Test]
		public void TryContinueWithBonusMoves_GrantsMovesAndResumesAfterLoss()
		{
			var session = MakeLostSession();
			Assert.AreEqual(GameSessionState.Lost, session.State);

			bool granted = session.TryContinueWithBonusMoves(5);

			Assert.IsTrue(granted);
			Assert.AreEqual(GameSessionState.InProgress, session.State);
			Assert.AreEqual(5, session.MovesRemaining);
			Assert.AreEqual(1, session.ContinuesUsed);
		}

		[Test]
		public void TryContinueWithBonusMoves_RespectsMaxContinues()
		{
			var session = MakeLostSession(maxContinues: 1);
			session.TryContinueWithBonusMoves(5);

			// Burn the granted bonus moves so the session goes Lost again.
			for (int i = 0; i < 5; i++)
				session.RegisterCascade(EmptyReport());

			Assert.AreEqual(GameSessionState.Lost, session.State);

			bool grantedAgain = session.TryContinueWithBonusMoves(5);

			Assert.IsFalse(grantedAgain);
			Assert.AreEqual(GameSessionState.Lost, session.State);
		}

		[Test]
		public void TryContinueWithBonusMoves_IgnoresNonPositiveBonus()
		{
			var session = MakeLostSession();

			bool granted = session.TryContinueWithBonusMoves(0);

			Assert.IsFalse(granted);
			Assert.AreEqual(GameSessionState.Lost, session.State);
		}

		[Test]
		public void Constructor_ThrowsForNegativeMaxContinues()
		{
			Assert.Throws<System.ArgumentOutOfRangeException>(() => new GameSession(1000, 5, null, -1));
		}
	}
}
