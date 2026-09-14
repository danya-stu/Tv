using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using PotionCraft.Core;

namespace PotionCraft.Gameplay
{
	/// <summary>
	/// Bridges the pure C# GridModel/MatchFinder simulation to a visible,
	/// swipe-controlled Unity scene using placeholder tiles (see TileView).
	/// Attach to an empty GameObject in a scene with a camera tagged
	/// MainCamera; it builds the board and every tile at runtime, so no
	/// prefabs or art assets are required yet.
	/// </summary>
	public sealed class GridView : MonoBehaviour
	{
		[SerializeField] private int _width = 8;
		[SerializeField] private int _height = 8;
		[SerializeField] private float _cellSize = 1f;
		[SerializeField] private int _colorCount = 5;
		[SerializeField] private float _swapAnimationDuration = 0.15f;
		[SerializeField] private float _removeAnimationDuration = 0.12f;
		[SerializeField] private float _fallAnimationDuration = 0.2f;
		[SerializeField] private int _targetScore = 500;
		[SerializeField] private int _maxMoves = 20;

		private GridModel _model;
		private GameSession _session;
		private TileView[,] _tiles;
		private bool _isResolving;
		private Vector2Int? _dragStartCell;
		private Camera _mainCamera;

		// Mutable accumulator used only while a single ResolveCascadeVisual
		// call is in flight, so the visual cascade loop (which yields across
		// many frames) can still build one CascadeReport for scoring at the
		// end, matching the shape GridModel.ResolveCascade produces on the
		// pure-model side.
		private sealed class CascadeAccumulator
		{
			public int StepCount;
			public int TotalCellsCleared;
			public readonly List<MatchGroup> AllMatches = new List<MatchGroup>();
		}

		private void Awake()
		{
			_mainCamera = Camera.main;

			_model = new GridModel(_width, _height);
			_model.InitializeGrid(_colorCount);

			// InitializeGrid avoids matches at creation time but does not
			// itself guarantee a possible move exists; this mirrors the same
			// safety check GridModelTests exercises on the pure model.
			if (!_model.HasPossibleMoves())
				_model.ShuffleUntilSolvable();

			_session = new GameSession(_targetScore, _maxMoves);

			_tiles = new TileView[_width, _height];
			BuildTiles();
		}

		private void BuildTiles()
		{
			for (int y = 0; y < _height; y++)
			{
				for (int x = 0; x < _width; x++)
				{
					_tiles[x, y] = CreateTile(x, y, _model.GetItem(x, y));
				}
			}
		}

		private TileView CreateTile(int x, int y, Item item)
		{
			var go = new GameObject($"Tile_{x}_{y}");
			go.transform.SetParent(transform, false);
			go.transform.localPosition = CellToLocalPosition(x, y);
			go.transform.localScale = Vector3.one * (_cellSize * 0.9f);

			go.AddComponent<SpriteRenderer>();
			var tile = go.AddComponent<TileView>();
			tile.SetGridPosition(x, y);
			tile.SetItem(item);

			return tile;
		}

		private Vector3 CellToLocalPosition(int x, int y)
		{
			return new Vector3(x * _cellSize, y * _cellSize, 0f);
		}

		private void Update()
		{
			// Once the level has been won or lost, stop accepting input
			// entirely rather than letting the player keep swapping tiles on
			// a finished board.
			if (_isResolving || _session.State != GameSessionState.InProgress)
				return;

			if (Input.GetMouseButtonDown(0))
			{
				_dragStartCell = ScreenToCell(Input.mousePosition);
			}
			else if (Input.GetMouseButtonUp(0) && _dragStartCell.HasValue)
			{
				Vector2Int? endCell = ScreenToCell(Input.mousePosition);
				if (endCell.HasValue)
					TryHandleSwipe(_dragStartCell.Value, endCell.Value);

				_dragStartCell = null;
			}
		}

		private Vector2Int? ScreenToCell(Vector3 screenPosition)
		{
			if (_mainCamera == null)
				return null;

			Vector3 world = _mainCamera.ScreenToWorldPoint(screenPosition);
			Vector3 local = transform.InverseTransformPoint(world);

			int x = Mathf.RoundToInt(local.x / _cellSize);
			int y = Mathf.RoundToInt(local.y / _cellSize);

			return _model.IsValidCoordinate(x, y) ? new Vector2Int(x, y) : (Vector2Int?)null;
		}

		private void TryHandleSwipe(Vector2Int from, Vector2Int to)
		{
			int dx = to.x - from.x;
			int dy = to.y - from.y;

			// Only single-step adjacent swaps are valid moves; a tap with no
			// movement or a swipe spanning multiple cells is ignored rather
			// than guessed at.
			bool isAdjacent = (Mathf.Abs(dx) == 1 && dy == 0) || (dx == 0 && Mathf.Abs(dy) == 1);
			if (!isAdjacent)
				return;

			StartCoroutine(ResolveSwap(from.x, from.y, to.x, to.y));
		}

		private IEnumerator ResolveSwap(int x1, int y1, int x2, int y2)
		{
			_isResolving = true;

			_model.Swap(x1, y1, x2, y2);
			List<MatchGroup> matches = MatchFinder.FindAllMatches(_model);

			if (matches.Count == 0)
			{
				// Invalid move: animate there and back, then revert the model
				// so simulation state always matches what the player sees. An
				// invalid swap does not consume a move.
				yield return AnimateSwapVisual(x1, y1, x2, y2);
				yield return AnimateSwapVisual(x1, y1, x2, y2);
				_model.Swap(x1, y1, x2, y2);

				_isResolving = false;
				yield break;
			}

			yield return AnimateSwapVisual(x1, y1, x2, y2);

			var accumulator = new CascadeAccumulator();
			yield return ResolveCascadeVisual(accumulator);

			var report = new CascadeReport(accumulator.StepCount, accumulator.TotalCellsCleared, accumulator.AllMatches);
			_session.RegisterCascade(report);

			if (!_model.HasPossibleMoves())
			{
				_model.ShuffleUntilSolvable();
				RefreshAllTileColors();
			}

			_isResolving = false;
		}

		private IEnumerator AnimateSwapVisual(int x1, int y1, int x2, int y2)
		{
			TileView tileA = _tiles[x1, y1];
			TileView tileB = _tiles[x2, y2];

			_tiles[x1, y1] = tileB;
			_tiles[x2, y2] = tileA;
			tileA.SetGridPosition(x2, y2);
			tileB.SetGridPosition(x1, y1);

			Coroutine moveA = StartCoroutine(tileA.MoveTo(CellToLocalPosition(x2, y2), _swapAnimationDuration));
			Coroutine moveB = StartCoroutine(tileB.MoveTo(CellToLocalPosition(x1, y1), _swapAnimationDuration));

			yield return moveA;
			yield return moveB;
		}

		private IEnumerator ResolveCascadeVisual(CascadeAccumulator accumulator)
		{
			// Hard safety cap matching GridModel.ResolveCascade, so a
			// pathological board can never loop forever on the visual side
			// either.
			int maxSteps = _width * _height;

			for (int step = 0; step < maxSteps; step++)
			{
				List<MatchGroup> matches = MatchFinder.FindAllMatches(_model);
				if (matches.Count == 0)
					yield break;

				accumulator.StepCount++;
				accumulator.AllMatches.AddRange(matches);
				foreach (MatchGroup match in matches)
					accumulator.TotalCellsCleared += match.Cells.Count;

				yield return AnimateRemoval(matches);
				_model.RemoveMatches(matches);

				_model.ApplyGravity();
				yield return AnimateGravity();

				_model.RefillEmptyCells(_colorCount);
				yield return AnimateRefill();
			}
		}

		private IEnumerator AnimateRemoval(List<MatchGroup> matches)
		{
			var toRemove = new HashSet<Vector2Int>();
			foreach (MatchGroup match in matches)
			{
				foreach ((int X, int Y) cell in match.Cells)
					toRemove.Add(new Vector2Int(cell.X, cell.Y));
			}

			Coroutine last = null;
			foreach (Vector2Int cell in toRemove)
			{
				TileView tile = _tiles[cell.x, cell.y];
				if (tile == null)
					continue;

				_tiles[cell.x, cell.y] = null;
				last = StartCoroutine(ShrinkAndDestroy(tile));
			}

			if (last != null)
				yield return last;
		}

		private IEnumerator ShrinkAndDestroy(TileView tile)
		{
			yield return tile.ShrinkAndDisable(_removeAnimationDuration);
			Destroy(tile.gameObject);
		}

		private IEnumerator AnimateGravity()
		{
			Coroutine last = null;

			for (int x = 0; x < _width; x++)
			{
				var survivors = new List<TileView>();
				for (int y = 0; y < _height; y++)
				{
					if (_tiles[x, y] != null)
						survivors.Add(_tiles[x, y]);
				}

				for (int y = 0; y < _height; y++)
					_tiles[x, y] = null;

				for (int newY = 0; newY < survivors.Count; newY++)
				{
					TileView tile = survivors[newY];
					_tiles[x, newY] = tile;

					if (tile.GridY != newY)
					{
						tile.SetGridPosition(x, newY);
						last = StartCoroutine(tile.MoveTo(CellToLocalPosition(x, newY), _fallAnimationDuration));
					}
				}
			}

			if (last != null)
				yield return last;
		}

		private IEnumerator AnimateRefill()
		{
			Coroutine last = null;

			for (int x = 0; x < _width; x++)
			{
				for (int y = 0; y < _height; y++)
				{
					if (_tiles[x, y] != null)
						continue;

					Item item = _model.GetItem(x, y);
					if (item.Color == ItemColor.None)
						continue;

					TileView tile = CreateTile(x, y, item);
					// Spawn one row above the top of the board and fall into
					// place, instead of popping straight into the final slot.
					tile.transform.localPosition = CellToLocalPosition(x, _height);
					_tiles[x, y] = tile;

					last = StartCoroutine(tile.MoveTo(CellToLocalPosition(x, y), _fallAnimationDuration));
				}
			}

			if (last != null)
				yield return last;
		}

		private void RefreshAllTileColors()
		{
			for (int y = 0; y < _height; y++)
			{
				for (int x = 0; x < _width; x++)
					_tiles[x, y].SetItem(_model.GetItem(x, y));
			}
		}

		// Minimal IMGUI HUD so score/moves/win-lose are visible without a
		// Canvas or UI prefabs yet. Intended to be replaced by real UI in a
		// later polish pass.
		private void OnGUI()
		{
			if (_session == null)
				return;

			var style = new GUIStyle(GUI.skin.label)
			{
				fontSize = 28,
				normal = { textColor = Color.white }
			};

			GUI.Label(new Rect(20, 20, 500, 40), $"Score: {_session.Score} / {_session.TargetScore}", style);
			GUI.Label(new Rect(20, 60, 500, 40), $"Moves: {_session.MovesRemaining}", style);

			if (_session.State == GameSessionState.Won)
				GUI.Label(new Rect(20, 110, 500, 60), "LEVEL COMPLETE", style);
			else if (_session.State == GameSessionState.Lost)
				GUI.Label(new Rect(20, 110, 500, 60), "OUT OF MOVES", style);
		}
	}
}
