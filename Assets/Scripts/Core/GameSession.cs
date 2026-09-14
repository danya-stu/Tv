using System;

namespace PotionCraft.Core
{
	public enum GameSessionState
	{
		InProgress,
		Won,
		Lost
	}

	/// <summary>
	/// Tracks score, remaining moves, and win/lose state for one level
	/// attempt. Pure C# so it runs identically in tests and in the Unity
	/// gameplay layer. Optionally drives an OrderBook (the "Книга Заказов"
	/// killer feature); passing null (the default) keeps the exact original
	/// scoring behavior with no orders in play. Also supports a bounded
	/// number of rewarded-ad "continues" after a loss (see
	/// TryContinueWithBonusMoves / AdRewardController).
	/// </summary>
	public sealed class GameSession
	{
		public int TargetScore { get; }
		public int MaxMoves { get; }
		public int Score { get; private set; }
		public int MovesRemaining { get; private set; }
		public GameSessionState State { get; private set; }
		public OrderBook OrderBook { get; }
		public int MaxContinues { get; }
		public int ContinuesUsed { get; private set; }

		/// <summary>True only when the session has ended in a loss and the per-attempt continue budget isn't used up yet.</summary>
		public bool CanContinueWithAd => State == GameSessionState.Lost && ContinuesUsed < MaxContinues;

		public GameSession(int targetScore, int maxMoves, OrderBook orderBook = null, int maxContinues = 1)
		{
			if (targetScore <= 0)
				throw new ArgumentOutOfRangeException(nameof(targetScore), targetScore, "targetScore must be positive.");
			if (maxMoves <= 0)
				throw new ArgumentOutOfRangeException(nameof(maxMoves), maxMoves, "maxMoves must be positive.");
			if (maxContinues < 0)
				throw new ArgumentOutOfRangeException(nameof(maxContinues), maxContinues, "maxContinues cannot be negative.");

			TargetScore = targetScore;
			MaxMoves = maxMoves;
			Score = 0;
			MovesRemaining = maxMoves;
			State = GameSessionState.InProgress;
			OrderBook = orderBook;
			MaxContinues = maxContinues;
			ContinuesUsed = 0;
		}

		/// <summary>
		/// Call once per valid swap (a swap that produced at least one match),
		/// after its cascade has fully resolved. Consumes one move, applies
		/// the cascade's score plus any Order Book bonus earned from this
		/// cascade's matches, and updates win/lose state. Calls made after
		/// the session has already ended are ignored so a lingering coroutine
		/// can never resurrect a finished session.
		/// </summary>
		public void RegisterCascade(CascadeReport report)
		{
			if (State != GameSessionState.InProgress)
				return;

			MovesRemaining = Math.Max(0, MovesRemaining - 1);

			int score = ScoreCalculator.CalculateScore(report);
			if (OrderBook != null)
				score += OrderBook.RegisterMatches(report.AllMatches);

			Score += score;

			if (Score >= TargetScore)
				State = GameSessionState.Won;
			else if (MovesRemaining <= 0)
				State = GameSessionState.Lost;
		}

		/// <summary>
		/// Grants extra moves and resumes play after a Lost session, as the
		/// reward for watching a rewarded ad (the monetization "continue"
		/// hook). Bounded by MaxContinues so a single level attempt cannot be
		/// extended indefinitely for free. Returns false and changes nothing
		/// if the session isn't Lost, the continue budget is already used up,
		/// or bonusMoves isn't positive. Callers should gate showing the ad
		/// itself on CanContinueWithAd (see AdRewardController) rather than
		/// calling this blindly.
		/// </summary>
		public bool TryContinueWithBonusMoves(int bonusMoves)
		{
			if (!CanContinueWithAd || bonusMoves <= 0)
				return false;

			MovesRemaining += bonusMoves;
			State = GameSessionState.InProgress;
			ContinuesUsed++;
			return true;
		}
	}
}
