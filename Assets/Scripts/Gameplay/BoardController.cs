using System;
using System.Collections.Generic;
using PotionCraft.Core;
using UnityEngine;

namespace PotionCraft.Gameplay
{
	/// <summary>
	/// Core gameplay coordinator. Connects the logical GridModel, the BoardStateMachine,
	/// and hardware SwipeInputReader to orchestrate player moves, validation, cascades,
	/// and hardware input locking.
	/// </summary>
	public class BoardController : MonoBehaviour
	{
		[Header("References")]
		[SerializeField] private SwipeInputReader _inputReader;

		[Header("Rules & Configuration")]
		[SerializeField] private int _colorCount = 5;
		[SerializeField] private int _initialMoves = 30;

		public GridModel GridModel { get; private set; }
		public BoardStateMachine StateMachine { get; private set; }
		public SwipeInputReader InputReader => _inputReader;
		public int RemainingMoves { get; set; }

		public event Action<Vector2Int, Vector2Int> OnSwapExecuted;
		public event Action<Vector2Int, Vector2Int> OnSwapReverted;
		public event Action<IReadOnlyList<CascadeStep>> OnCascadesDispatched;
		public event Action OnTurnCompleted;

		private ISpawnWeightProvider _spawnWeightProvider;
		private List<CascadeStep> _cascadeStepsBuffer = new List<CascadeStep>(16);

		/// <summary>
		/// Initializes or injects dependencies for the board coordinator.
		/// Enables testability in editor test suites without running a full scene.
		/// </summary>
		public void Initialize(
			GridModel gridModel,
			BoardStateMachine stateMachine = null,
			SwipeInputReader inputReader = null,
			ISpawnWeightProvider spawnWeightProvider = null,
			int colorCount = 5,
			int initialMoves = 30)
		{
			GridModel = gridModel ?? throw new ArgumentNullException(nameof(gridModel));
			StateMachine = stateMachine ?? new BoardStateMachine(BoardState.Idle);

			if (inputReader != null)
			{
				SetInputReader(inputReader);
			}

			_spawnWeightProvider = spawnWeightProvider ?? new DefaultSpawnWeightProvider();
			_colorCount = colorCount;
			RemainingMoves = initialMoves;

			if (_cascadeStepsBuffer == null)
			{
				_cascadeStepsBuffer = new List<CascadeStep>(16);
			}

			StateMachine.OnStateChanged += HandleStateChanged;
		}

		public void SetInputReader(SwipeInputReader reader)
		{
			if (_inputReader != null)
			{
				_inputReader.OnSwipeDetected -= OnSwipe;
			}

			_inputReader = reader;

			if (_inputReader != null)
			{
				_inputReader.OnSwipeDetected += OnSwipe;
				_inputReader.SetInputActive(StateMachine == null || StateMachine.IsIdle);
			}
		}

		private void Awake()
		{
			if (StateMachine == null)
			{
				StateMachine = new BoardStateMachine(BoardState.Idle);
				StateMachine.OnStateChanged += HandleStateChanged;
			}

			if (_inputReader != null)
			{
				_inputReader.OnSwipeDetected += OnSwipe;
			}

			if (_spawnWeightProvider == null)
			{
				_spawnWeightProvider = new DefaultSpawnWeightProvider();
			}

			RemainingMoves = _initialMoves;
		}

		private void OnDestroy()
		{
			if (StateMachine != null)
			{
				StateMachine.OnStateChanged -= HandleStateChanged;
			}

			if (_inputReader != null)
			{
				_inputReader.OnSwipeDetected -= OnSwipe;
			}
		}

		private void HandleStateChanged(BoardState state)
		{
			// Hardware input lock: unlock only when board is completely idle
			if (_inputReader != null)
			{
				_inputReader.SetInputActive(state == BoardState.Idle);
			}
		}

		/// <summary>
		/// Processes a player's swipe attempt between adjacent cells:
		/// 1. Transitions to Swapping.
		/// 2. Performs virtual swap in GridModel.
		/// 3. Validates move through BoardGravityEngine.ResolveFullTurnCascades.
		/// 4. If invalid: reverts swap and restores Idle state.
		/// 5. If valid: consumes a move, dispatches cascade steps to View, and transitions to ResolvingCascades.
		/// </summary>
		public void OnSwipe(Vector2Int from, Vector2Int to)
		{
			if (StateMachine == null || !StateMachine.CanTransitionTo(BoardState.Swapping))
			{
				return;
			}

			// Validate orthogonal adjacency (Manhattan distance == 1)
			int dx = Math.Abs(from.x - to.x);
			int dy = Math.Abs(from.y - to.y);
			if (dx + dy != 1)
			{
				return;
			}

			if (GridModel == null || !GridModel.IsValidCoordinate(from.x, from.y) || !GridModel.IsValidCoordinate(to.x, to.y))
			{
				return;
			}

			// Hardware input lock: disable further inputs immediately
			_inputReader?.SetInputActive(false);

			// 1. Transition state machine to Swapping
			StateMachine.TransitionTo(BoardState.Swapping);
			OnSwapExecuted?.Invoke(from, to);

			// 2. Perform virtual swap in GridModel
			GridModel.Swap(from.x, from.y, to.x, to.y);
			StateMachine.TransitionTo(BoardState.Validating);

			// 3. Validate matches and compute full turn cascades
			_cascadeStepsBuffer.Clear();
			ISpawnWeightProvider provider = _spawnWeightProvider ?? new DefaultSpawnWeightProvider();

			bool hasMatches = BoardGravityEngine.ResolveFullTurnCascades(
				GridModel.RawGrid,
				GridModel.Width,
				GridModel.Height,
				provider,
				_colorCount,
				_cascadeStepsBuffer,
				to);

			if (!hasMatches)
			{
				// 4. If invalid: revert virtual swap and return to Idle
				GridModel.Swap(from.x, from.y, to.x, to.y);
				OnSwapReverted?.Invoke(from, to);

				StateMachine.TransitionTo(BoardState.Idle);
				_inputReader?.SetInputActive(true);
			}
			else
			{
				// 5. If valid: consume move, dispatch steps to View, transition to ResolvingCascades
				if (RemainingMoves > 0)
				{
					RemainingMoves--;
				}

				StateMachine.TransitionTo(BoardState.ResolvingCascades);
				OnCascadesDispatched?.Invoke(_cascadeStepsBuffer);
				OnTurnCompleted?.Invoke();
			}
		}

		/// <summary>
		/// Notifies the controller that presentation-layer cascade animations have fully concluded,
		/// returning the state machine to Idle and releasing the hardware input lock.
		/// </summary>
		public void CompleteCascades()
		{
			if (StateMachine != null && StateMachine.CurrentState == BoardState.ResolvingCascades)
			{
				StateMachine.TransitionTo(BoardState.Idle);
				_inputReader?.SetInputActive(true);
			}
		}
	}
}
