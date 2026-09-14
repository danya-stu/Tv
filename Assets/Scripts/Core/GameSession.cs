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
	/// gameplay layer.
	/// </summary>
	public sealed class GameSession
	{
		public int TargetScore { get; }
		public int MaxMoves { get; }
		public int Score { get; private set; }
		public int MovesRemaining { get; private set; }
		public GameSessionState State { get; private set; }

		public GameSession(int targetScore, int maxMoves)
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
		}

		/// <summary>
		/// Call once per valid swap (a swap that produced at least one match),
		/// after its cascade has fully resolved. Consumes one move, applies
		/// the cascade's score, and updates win/lose state. Calls made after
		/// the session has already ended are ignored so a lingering coroutine
		/// can never resurrect a finished session.
		/// </summary>
		public void RegisterCascade(CascadeReport report)
		{
			if (State != GameSessionState.InProgress)
				return;

			MovesRemaining = Math.Max(0, MovesRemaining - 1);
			Score += ScoreCalculator.CalculateScore(report);

			if (Score >= TargetScore)
				State = GameSessionState.Won;
			else if (MovesRemaining <= 0)
				State = GameSessionState.Lost;
		}
	}
}
