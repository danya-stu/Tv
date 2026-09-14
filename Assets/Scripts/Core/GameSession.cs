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
	/// scoring behavior with no orders in play.
	/// </summary>
	public sealed class GameSession
	{
		public int TargetScore { get; }
		public int MaxMoves { get; }
		public int Score { get; private set; }
		public int MovesRemaining { get; private set; }
		public GameSessionState State { get; private set; }
		public OrderBook OrderBook { get; }

		public GameSession(int targetScore, int maxMoves, OrderBook orderBook = null)
		{
			if (targetScore <= 0)
				throw new ArgumentOutOfRangeException(nameof(targetScore), targetScore, "targetScore must be positive.");
			if (maxMoves <= 0)
				throw new ArgumentOutOfRangeException(nameof(maxMoves), maxMoves, "maxMoves must be positive.");

			TargetScore = targetScore;
			MaxMoves = maxMoves;
			Score = 0;
			MovesRemaining = maxMoves;
			State = GameSessionState.InProgress;
			OrderBook = orderBook;
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
	}
}
