using System;
using System.Collections.Generic;
using NUnit.Framework;
using PotionCraft.Core;

namespace PotionCraft.Tests.Editor
{
	/// <summary>
	/// Deterministic stand-in for a real ad SDK, so AdRewardController's
	/// orchestration logic can be tested without Unity, network access, or
	/// any actual ad provider.
	/// </summary>
	private sealed class FakeRewardedAdService : IRewardedAdService
	{
		public bool IsReady { get; set; } = true;
		public bool NextResultIsWatched { get; set; } = true;
		public int ShowCallCount { get; private set; }

		public void Show(Action<bool> onComplete)
		{
			ShowCallCount++;
			onComplete?.Invoke(NextResultIsWatched);
		}
	}

	[TestFixture]
	public class AdRewardControllerTests
	{
		private sealed class FakeAd : IRewardedAdService
		{
			public bool IsReady { get; set; } = true;
			public bool NextResultIsWatched { get; set; } = true;
			public int ShowCallCount { get; private set; }

			public void Show(Action<bool> onComplete)
			{
				ShowCallCount++;
				onComplete?.Invoke(NextResultIsWatched);
			}
		}

		private static GameSession MakeLostSession(int maxContinues = 1)
		{
			var session = new GameSession(targetScore: 1000, maxMoves: 1, orderBook: null, maxContinues: maxContinues);
			session.RegisterCascade(new CascadeReport(0, 0, new List<MatchGroup>()));
			return session;
		}

		[Test]
		public void RequestContinue_GrantsBonusMovesWhenAdIsWatched()
		{
			var session = MakeLostSession();
			var ad = new FakeAd { NextResultIsWatched = true };
			bool? result = null;

			AdRewardController.RequestContinue(session, ad, bonusMoves: 5, r => result = r);

			Assert.AreEqual(1, ad.ShowCallCount);
			Assert.IsTrue(result);
			Assert.AreEqual(GameSessionState.InProgress, session.State);
			Assert.AreEqual(5, session.MovesRemaining);
		}

		[Test]
		public void RequestContinue_DoesNotGrantWhenAdIsSkipped()
		{
			var session = MakeLostSession();
			var ad = new FakeAd { NextResultIsWatched = false };
			bool? result = null;

			AdRewardController.RequestContinue(session, ad, bonusMoves: 5, r => result = r);

			Assert.AreEqual(1, ad.ShowCallCount);
			Assert.IsFalse(result);
			Assert.AreEqual(GameSessionState.Lost, session.State);
		}

		[Test]
		public void RequestContinue_NeverShowsAdWhenSessionCannotContinue()
		{
			var session = new GameSession(1000, 5); // still InProgress
			var ad = new FakeAd();
			bool? result = null;

			AdRewardController.RequestContinue(session, ad, bonusMoves: 5, r => result = r);

			Assert.AreEqual(0, ad.ShowCallCount);
			Assert.IsFalse(result);
		}

		[Test]
		public void RequestContinue_NeverShowsAdOnceContinueBudgetIsUsedUp()
		{
			var session = MakeLostSession(maxContinues: 1);
			var ad = new FakeAd { NextResultIsWatched = true };

			AdRewardController.RequestContinue(session, ad, bonusMoves: 5, _ => { });
			for (int i = 0; i < 5; i++)
				session.RegisterCascade(new CascadeReport(0, 0, new List<MatchGroup>()));

			Assert.AreEqual(GameSessionState.Lost, session.State);

			bool? secondResult = null;
			AdRewardController.RequestContinue(session, ad, bonusMoves: 5, r => secondResult = r);

			Assert.AreEqual(1, ad.ShowCallCount);
			Assert.IsFalse(secondResult);
		}
	}
}
