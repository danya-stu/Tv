using System.Collections;
using System.Collections.Generic;
using PotionCraft.Core;
using UnityEngine;

namespace PotionCraft.Gameplay.View
{
	/// <summary>
	/// Presentation layer for the board: owns the visual tile pool, listens to
	/// BoardController events and replays the CascadeStep data produced by
	/// BoardGravityEngine as coroutine-driven tweens.
	///
	/// No third-party tween plugin is used (see TweenEasing) and no tile is ever
	/// destroyed during cascades: cleared tiles shrink to zero and go back to the
	/// pool, refills are rented from it.
	/// </summary>
	[DisallowMultipleComponent]
	public sealed class BoardView : MonoBehaviour
	{
		[Header("References")]
		[SerializeField] private BoardController _controller;
		[SerializeField] private GameObject _tilePrefab;
		[SerializeField] private Transform _tilesParent;

		[Header("Layout")]
		[SerializeField] private float _cellSize = 1.2f;
		[SerializeField] private Vector2 _originOffset = Vector2.zero;
		[SerializeField] private float _tileScaleFactor = 0.9f;

		[Header("Board setup (used only when the controller has no model yet)")]
		[SerializeField] private int _boardWidth = 8;
		[SerializeField] private int _boardHeight = 8;
		[SerializeField] private int _colorCount = 5;
		[SerializeField] private int _initialMoves = 30;

		[Header("Timings")]
		[SerializeField] private float _swapDuration = 0.15f;
		[SerializeField] private float _revertDuration = 0.12f;
		[SerializeField] private float _destroyDuration = 0.12f;
		[SerializeField] private float _transmuteDuration = 0.2f;
		[SerializeField] private float _specialSpawnDuration = 0.18f;
		[SerializeField] private float _fallBaseDuration = 0.16f;
		[SerializeField] private float _fallPerCellDuration = 0.035f;
		[SerializeField] private float _fallMaxDuration = 0.5f;
		[SerializeField] private float _interCascadePause = 0.05f;

		private readonly Queue<TileView> _pool = new Queue<TileView>(64);
		private readonly Dictionary<Vector2Int, TileView> _activeTiles = new Dictionary<Vector2Int, TileView>(128);
		private readonly List<Coroutine> _running = new List<Coroutine>(128);
		private readonly List<TileView> _movingTiles = new List<TileView>(128);
		private readonly List<Vector2Int> _movingTargets = new List<Vector2Int>(128);

		private Coroutine _swapRoutine;
		private Coroutine _cascadeRoutine;
		private WaitForSeconds _pauseBetweenCascades;
		private int _createdTileCount;

		public BoardController Controller => _controller;
		public int PooledTileCount => _pool.Count;
		public int ActiveTileCount => _activeTiles.Count;

		private void Awake()
		{
			if (_tilesParent == null)
			{
				_tilesParent = transform;
			}

			_pauseBetweenCascades = new WaitForSeconds(_interCascadePause);
			ResolveController();
		}

		private void Start()
		{
			ResolveController();

			if (_controller == null)
			{
				Debug.LogError("BoardView could not find a BoardController in the scene; the board will not be built.", this);
				return;
			}

			EnsureModelInitialized();
			Subscribe();
			BuildBoard();
		}

		private void OnDestroy()
		{
			Unsubscribe();
		}

		// --- scene wiring --------------------------------------------------

		/// <summary>
		/// Finds the controller even when the scene was assembled by hand and the
		/// inspector reference was left empty: same object, then parents/children,
		/// then the whole scene.
		/// </summary>
		private void ResolveController()
		{
			if (_controller != null)
			{
				return;
			}

			_controller = GetComponent<BoardController>();

			if (_controller == null)
			{
				_controller = GetComponentInParent<BoardController>();
			}

			if (_controller == null)
			{
				_controller = GetComponentInChildren<BoardController>();
			}

			if (_controller == null)
			{
				_controller = FindAnyObjectByType<BoardController>();
			}
		}

		/// <summary>
		/// Guarantees a playable logical board exists before the first frame: if the
		/// controller was not initialized by a bootstrap object, the view creates and
		/// injects a match-free, solvable GridModel itself.
		/// </summary>
		private void EnsureModelInitialized()
		{
			if (_controller.GridModel != null)
			{
				return;
			}

			GridModel model = new GridModel(_boardWidth, _boardHeight);
			model.InitializeGrid(_colorCount);

			if (!model.HasPossibleMoves())
			{
				model.ShuffleUntilSolvable();
			}

			_controller.Initialize(
				model,
				null,
				_controller.InputReader,
				new DefaultSpawnWeightProvider(),
				_colorCount,
				_initialMoves);
		}

		private void Subscribe()
		{
			_controller.OnSwapExecuted += HandleSwapExecuted;
			_controller.OnSwapReverted += HandleSwapReverted;
			_controller.OnCascadesDispatched += HandleCascadesDispatched;
		}

		private void Unsubscribe()
		{
			if (_controller == null)
			{
				return;
			}

			_controller.OnSwapExecuted -= HandleSwapExecuted;
			_controller.OnSwapReverted -= HandleSwapReverted;
			_controller.OnCascadesDispatched -= HandleCascadesDispatched;
		}

		// --- layout --------------------------------------------------------

		public Vector3 CellToWorld(int x, int y)
		{
			Vector3 local = new Vector3(_originOffset.x + (x * _cellSize), _originOffset.y + (y * _cellSize), 0f);
			return _tilesParent == null ? local : _tilesParent.TransformPoint(local);
		}

		public Vector3 CellToWorld(Vector2Int cell)
		{
			return CellToWorld(cell.x, cell.y);
		}

		// --- board construction --------------------------------------------

		/// <summary>Builds (or rebuilds) every visual tile from the logical model.</summary>
		public void BuildBoard()
		{
			GridModel model = _controller.GridModel;
			if (model == null)
			{
				return;
			}

			ReleaseAllTiles();

			for (int x = 0; x < model.Width; x++)
			{
				for (int y = 0; y < model.Height; y++)
				{
					Item item = model.GetItem(x, y);
					if (item.Color == ItemColor.None)
					{
						continue;
					}

					Vector2Int cell = new Vector2Int(x, y);
					TileView tile = RentTile();
					tile.GridPosition = cell;
					tile.SetData(item, CellToWorld(cell));
					_activeTiles[cell] = tile;
				}
			}
		}

		private void ReleaseAllTiles()
		{
			foreach (KeyValuePair<Vector2Int, TileView> entry in _activeTiles)
			{
				ReleaseTile(entry.Value);
			}

			_activeTiles.Clear();
		}

		// --- pooling -------------------------------------------------------

		private TileView RentTile()
		{
			TileView tile = _pool.Count > 0 ? _pool.Dequeue() : CreateTile();
			tile.gameObject.SetActive(true);
			tile.SetBaseScale(Vector3.one * (_cellSize * _tileScaleFactor));
			tile.transform.localScale = tile.BaseScale;
			return tile;
		}

		private void ReleaseTile(TileView tile)
		{
			if (tile == null)
			{
				return;
			}

			tile.ResetState();
			tile.gameObject.SetActive(false);
			_pool.Enqueue(tile);
		}

		private TileView CreateTile()
		{
			GameObject go;

			if (_tilePrefab != null)
			{
				go = Instantiate(_tilePrefab, _tilesParent);
			}
			else
			{
				go = new GameObject("Tile");
				go.transform.SetParent(_tilesParent, false);
			}

			go.name = $"Tile_{_createdTileCount++}";

			TileView tile = go.GetComponent<TileView>();
			if (tile == null)
			{
				tile = go.AddComponent<TileView>();
			}

			tile.SetBaseScale(Vector3.one * (_cellSize * _tileScaleFactor));
			return tile;
		}

		// --- swap animations -----------------------------------------------

		private void HandleSwapExecuted(Vector2Int from, Vector2Int to)
		{
			_swapRoutine = StartCoroutine(AnimateSwapAfter(_swapRoutine, from, to, _swapDuration));
		}

		private void HandleSwapReverted(Vector2Int from, Vector2Int to)
		{
			// The controller can revert inside the very same frame it swapped, so the
			// revert has to queue behind the outgoing swap animation.
			_swapRoutine = StartCoroutine(AnimateSwapAfter(_swapRoutine, from, to, _revertDuration));
		}

		private IEnumerator AnimateSwapAfter(Coroutine previous, Vector2Int from, Vector2Int to, float duration)
		{
			if (previous != null)
			{
				yield return previous;
			}

			yield return AnimateSwap(from, to, duration);
		}

		/// <summary>Swaps two tiles in parallel and waits for the slower one.</summary>
		private IEnumerator AnimateSwap(Vector2Int from, Vector2Int to, float duration)
		{
			_activeTiles.TryGetValue(from, out TileView tileA);
			_activeTiles.TryGetValue(to, out TileView tileB);

			if (tileA == null && tileB == null)
			{
				yield break;
			}

			if (tileA != null)
			{
				tileA.GridPosition = to;
				_activeTiles[to] = tileA;
			}
			else
			{
				_activeTiles.Remove(to);
			}

			if (tileB != null)
			{
				tileB.GridPosition = from;
				_activeTiles[from] = tileB;
			}
			else
			{
				_activeTiles.Remove(from);
			}

			_running.Clear();

			if (tileA != null)
			{
				_running.Add(StartCoroutine(tileA.AnimateMove(CellToWorld(to), duration, TweenEasing.EaseInOutQuad)));
			}

			if (tileB != null)
			{
				_running.Add(StartCoroutine(tileB.AnimateMove(CellToWorld(from), duration, TweenEasing.EaseInOutQuad)));
			}

			yield return WaitForAllRunning();
		}

		// --- cascade playback ----------------------------------------------

		private void HandleCascadesDispatched(IReadOnlyList<CascadeStep> steps)
		{
			_cascadeRoutine = StartCoroutine(PlayCascadeSequence(steps));
		}

		/// <summary>
		/// Replays every CascadeStep in order: destruction, transmutation, bonus
		/// spawn, then falls and refills in parallel, with a micro pause between
		/// cascades. Hands control back to the controller when finished.
		/// </summary>
		public IEnumerator PlayCascadeSequence(IReadOnlyList<CascadeStep> steps)
		{
			// The opening swap must land before the first cascade starts.
			if (_swapRoutine != null)
			{
				yield return _swapRoutine;
				_swapRoutine = null;
			}

			if (steps != null)
			{
				for (int i = 0; i < steps.Count; i++)
				{
					CascadeStep step = steps[i];
					if (step == null)
					{
						continue;
					}

					yield return PlayDestruction(step);
					yield return PlayTransmutation(step);
					yield return PlaySpecialSpawns(step);
					yield return PlayFallsAndSpawns(step);

					if (i < steps.Count - 1)
					{
						yield return _pauseBetweenCascades;
					}
				}
			}

			// Defensive re-sync: after a long chain the dictionary must match the
			// authoritative logical grid exactly before input is unlocked.
			SyncWithModel();

			_cascadeRoutine = null;
			_controller.CompleteCascades();
		}

		/// <summary>1. Shrink cleared tiles to zero and return them to the pool.</summary>
		private IEnumerator PlayDestruction(CascadeStep step)
		{
			if (step.DestroyedTiles.Count == 0)
			{
				yield break;
			}

			_running.Clear();

			for (int i = 0; i < step.DestroyedTiles.Count; i++)
			{
				Vector2Int cell = step.DestroyedTiles[i];
				if (!_activeTiles.TryGetValue(cell, out TileView tile) || tile == null)
				{
					continue;
				}

				_activeTiles.Remove(cell);
				_running.Add(StartCoroutine(ShrinkAndRelease(tile)));
			}

			yield return WaitForAllRunning();
		}

		private IEnumerator ShrinkAndRelease(TileView tile)
		{
			yield return tile.AnimateScale(Vector3.zero, _destroyDuration, TweenEasing.EaseInQuad);
			ReleaseTile(tile);
		}

		/// <summary>2. Pulse and recolor tiles touched by a transmutation.</summary>
		private IEnumerator PlayTransmutation(CascadeStep step)
		{
			if (step.TransmutedTiles.Count == 0 || step.TransmuteColor == ItemColor.None)
			{
				yield break;
			}

			Item newItem = Item.Create(step.TransmuteColor);
			Color targetColor = TileView.ColorForItem(newItem);

			_running.Clear();

			for (int i = 0; i < step.TransmutedTiles.Count; i++)
			{
				Vector2Int cell = step.TransmutedTiles[i];
				if (!_activeTiles.TryGetValue(cell, out TileView tile) || tile == null)
				{
					continue;
				}

				_running.Add(StartCoroutine(tile.AnimateTransmute(newItem, targetColor, _transmuteDuration)));
			}

			yield return WaitForAllRunning();
		}

		/// <summary>3. Pop the bonus tiles created by this step out of the pool.</summary>
		private IEnumerator PlaySpecialSpawns(CascadeStep step)
		{
			if (step.CreatedSpecials.Count == 0)
			{
				yield break;
			}

			_running.Clear();

			for (int i = 0; i < step.CreatedSpecials.Count; i++)
			{
				SpecialCreationInfo info = step.CreatedSpecials[i];
				Vector2Int cell = info.Position;

				if (_activeTiles.TryGetValue(cell, out TileView existing) && existing != null)
				{
					_activeTiles.Remove(cell);
					ReleaseTile(existing);
				}

				Item specialItem = Item.Create(info.Color, SpecialActivationEngine.ToCatalystType(info.Type));
				TileView tile = RentTile();
				tile.GridPosition = cell;
				tile.SetData(specialItem, CellToWorld(cell));
				tile.transform.localScale = Vector3.zero;
				_activeTiles[cell] = tile;

				_running.Add(StartCoroutine(tile.AnimateScale(tile.BaseScale, _specialSpawnDuration, TweenEasing.EaseOutBackDefault)));
			}

			yield return WaitForAllRunning();
		}

		/// <summary>4. Falls and refills in parallel, waiting for the longest drop.</summary>
		private IEnumerator PlayFallsAndSpawns(CascadeStep step)
		{
			if (step.Moves.Count == 0 && step.Spawns.Count == 0)
			{
				yield break;
			}

			_running.Clear();
			_movingTiles.Clear();
			_movingTargets.Clear();

			// Detach every falling tile from the dictionary first, so a chain of
			// moves inside one column cannot overwrite a tile that has not moved yet.
			for (int i = 0; i < step.Moves.Count; i++)
			{
				TileMoveAction move = step.Moves[i];
				if (!_activeTiles.TryGetValue(move.From, out TileView tile) || tile == null)
				{
					continue;
				}

				_activeTiles.Remove(move.From);
				_movingTiles.Add(tile);
				_movingTargets.Add(move.To);
			}

			for (int i = 0; i < _movingTiles.Count; i++)
			{
				TileView tile = _movingTiles[i];
				Vector2Int target = _movingTargets[i];
				int distance = Mathf.Abs(tile.GridPosition.y - target.y);

				tile.GridPosition = target;
				_activeTiles[target] = tile;

				_running.Add(StartCoroutine(tile.AnimateMove(CellToWorld(target), FallDuration(distance), TweenEasing.EaseOutBounce)));
			}

			for (int i = 0; i < step.Spawns.Count; i++)
			{
				TileSpawnAction spawn = step.Spawns[i];
				Vector2Int target = spawn.TargetPosition;

				if (_activeTiles.TryGetValue(target, out TileView occupant) && occupant != null)
				{
					_activeTiles.Remove(target);
					ReleaseTile(occupant);
				}

				TileView tile = RentTile();
				tile.GridPosition = target;
				tile.SetData(spawn.SpawnedItem, CellToWorld(spawn.Column, spawn.SpawnRow));
				_activeTiles[target] = tile;

				_running.Add(StartCoroutine(tile.AnimateMove(CellToWorld(target), FallDuration(spawn.DropDistance), TweenEasing.EaseOutBounce)));
			}

			yield return WaitForAllRunning();
		}

		private float FallDuration(int distance)
		{
			float duration = _fallBaseDuration + (_fallPerCellDuration * Mathf.Max(0, distance));
			return Mathf.Min(duration, _fallMaxDuration);
		}

		/// <summary>
		/// Waits for every coroutine started for the current phase. They all run in
		/// parallel, so waiting on them in sequence takes exactly as long as the
		/// slowest one.
		/// </summary>
		private IEnumerator WaitForAllRunning()
		{
			for (int i = 0; i < _running.Count; i++)
			{
				if (_running[i] != null)
				{
					yield return _running[i];
				}
			}

			_running.Clear();
		}

		/// <summary>
		/// Forces the visual board to match the logical grid: repaints mismatched
		/// tiles, rents tiles for cells that have none and releases orphans.
		/// </summary>
		public void SyncWithModel()
		{
			GridModel model = _controller == null ? null : _controller.GridModel;
			if (model == null)
			{
				return;
			}

			for (int x = 0; x < model.Width; x++)
			{
				for (int y = 0; y < model.Height; y++)
				{
					Vector2Int cell = new Vector2Int(x, y);
					Item item = model.GetItem(x, y);
					bool hasTile = _activeTiles.TryGetValue(cell, out TileView tile) && tile != null;

					if (item.Color == ItemColor.None)
					{
						if (hasTile)
						{
							_activeTiles.Remove(cell);
							ReleaseTile(tile);
						}

						continue;
					}

					if (!hasTile)
					{
						tile = RentTile();
						tile.GridPosition = cell;
						tile.SetData(item, CellToWorld(cell));
						_activeTiles[cell] = tile;
						continue;
					}

					if (tile.CurrentItem != item)
					{
						tile.SetItem(item);
					}

					tile.transform.position = CellToWorld(cell);
					tile.transform.localScale = tile.BaseScale;
				}
			}
		}
	}
}
