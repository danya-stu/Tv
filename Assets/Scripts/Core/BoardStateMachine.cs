using System;

namespace PotionCraft.Core
{
	/// <summary>
	/// Lifecycle states of the Match-3 game board.
	/// </summary>
	public enum BoardState
	{
		Idle = 0,
		Swapping = 1,
		Validating = 2,
		ResolvingCascades = 3,
		Shuffling = 4
	}

	/// <summary>
	/// Pure C# state machine controlling the Match-3 board lifecycle and enforcing
	/// transition validity without relying on Unity engine lifecycle or MonoBehaviour.
	/// </summary>
	public sealed class BoardStateMachine
	{
		/// <summary>Current state of the board.</summary>
		public BoardState CurrentState { get; private set; }

		/// <summary>Invoked whenever the state transitions to a new value.</summary>
		public event Action<BoardState> OnStateChanged;

		public BoardStateMachine(BoardState initialState = BoardState.Idle)
		{
			CurrentState = initialState;
		}

		/// <summary>Whether the board is idle and ready to accept player input.</summary>
		public bool IsIdle => CurrentState == BoardState.Idle;

		/// <summary>Whether the board is performing actions (swapping, validating, cascading, shuffling).</summary>
		public bool IsBusy => CurrentState != BoardState.Idle;

		/// <summary>
		/// Determines if transitioning from CurrentState to targetState is logically valid.
		/// </summary>
		public bool CanTransitionTo(BoardState targetState)
		{
			if (CurrentState == targetState)
			{
				return true;
			}

			switch (CurrentState)
			{
				case BoardState.Idle:
					return targetState == BoardState.Swapping
						|| targetState == BoardState.Shuffling
						|| targetState == BoardState.ResolvingCascades;

				case BoardState.Swapping:
					return targetState == BoardState.Validating
						|| targetState == BoardState.Idle;

				case BoardState.Validating:
					return targetState == BoardState.ResolvingCascades
						|| targetState == BoardState.Swapping
						|| targetState == BoardState.Idle;

				case BoardState.ResolvingCascades:
					return targetState == BoardState.Idle
						|| targetState == BoardState.Shuffling;

				case BoardState.Shuffling:
					return targetState == BoardState.Idle
						|| targetState == BoardState.ResolvingCascades;

				default:
					return false;
			}
		}

		/// <summary>
		/// Attempts to transition to targetState. Returns true if transition was allowed and executed.
		/// </summary>
		public bool TryTransitionTo(BoardState targetState)
		{
			if (!CanTransitionTo(targetState))
			{
				return false;
			}

			if (CurrentState == targetState)
			{
				return true;
			}

			CurrentState = targetState;
			OnStateChanged?.Invoke(CurrentState);
			return true;
		}

		/// <summary>
		/// Transitions to targetState. Throws InvalidOperationException if transition is disallowed.
		/// </summary>
		public void TransitionTo(BoardState targetState)
		{
			if (!TryTransitionTo(targetState))
			{
				throw new InvalidOperationException($"Cannot transition board state from {CurrentState} to {targetState}.");
			}
		}

		/// <summary>
		/// Resets state machine back to Idle.
		/// </summary>
		public void Reset()
		{
			if (CurrentState != BoardState.Idle)
			{
				CurrentState = BoardState.Idle;
				OnStateChanged?.Invoke(CurrentState);
			}
		}
	}
}
