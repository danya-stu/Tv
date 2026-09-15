using System;
using UnityEngine;

namespace PotionCraft.Gameplay
{
	/// <summary>
	/// Hardware swipe reader supporting mobile Touch and editor/desktop Mouse inputs.
	/// Strictly clamps swipes into orthogonal directions (Up, Down, Left, Right)
	/// and enforces input lock when animations or cascades are running.
	/// </summary>
	public class SwipeInputReader : MonoBehaviour
	{
		[Header("Swipe Thresholds")]
		[SerializeField] private float _swipeDeadzone = 15f;
		[SerializeField] private float _swipeThreshold = 40f;

		[Header("Optional Camera")]
		[SerializeField] private Camera _camera;

		/// <summary>
		/// Invoked when a valid orthogonal swipe from one cell to an adjacent cell is detected.
		/// Parameters: Vector2Int fromCell, Vector2Int toCell.
		/// </summary>
		public event Action<Vector2Int, Vector2Int> OnSwipeDetected;

		private bool _isInputActive = true;
		private bool _isDragging;
		private Vector2 _dragStartPosition;
		private Vector2Int? _startCell;
		private Func<Vector2, Vector2Int?> _cellResolver;

		public bool IsInputActive => _isInputActive;
		public float SwipeDeadzone => _swipeDeadzone;
		public float SwipeThreshold => _swipeThreshold;

		/// <summary>
		/// Configures the callback used to map screen coordinates to grid cell coordinates.
		/// </summary>
		public void SetCellResolver(Func<Vector2, Vector2Int?> resolver)
		{
			_cellResolver = resolver;
		}

		/// <summary>
		/// Hardware input lock. When set to false, all touch/mouse tracking is immediately
		/// discarded, preventing player input during animations, cascades, or shuffles.
		/// </summary>
		public void SetInputActive(bool active)
		{
			_isInputActive = active;
			if (!_isInputActive)
			{
				CancelDrag();
			}
		}

		private void Update()
		{
			if (!_isInputActive)
			{
				if (_isDragging)
				{
					CancelDrag();
				}
				return;
			}

			// Mobile touch input takes precedence when touches exist
			if (UnityEngine.Input.touchCount > 0)
			{
				HandleTouchInput();
			}
			else
			{
				HandleMouseInput();
			}
		}

		private void HandleTouchInput()
		{
			Touch touch = UnityEngine.Input.GetTouch(0);
			Vector2 touchPos = touch.position;

			switch (touch.phase)
			{
				case TouchPhase.Began:
					BeginDrag(touchPos);
					break;

				case TouchPhase.Moved:
				case TouchPhase.Stationary:
					if (_isDragging)
					{
						ProcessDrag(touchPos);
					}
					break;

				case TouchPhase.Ended:
				case TouchPhase.Canceled:
					CancelDrag();
					break;
			}
		}

		private void HandleMouseInput()
		{
			if (UnityEngine.Input.GetMouseButtonDown(0))
			{
				BeginDrag(UnityEngine.Input.mousePosition);
			}
			else if (UnityEngine.Input.GetMouseButton(0))
			{
				if (_isDragging)
				{
					ProcessDrag(UnityEngine.Input.mousePosition);
				}
			}
			else if (UnityEngine.Input.GetMouseButtonUp(0))
			{
				CancelDrag();
			}
		}

		private void BeginDrag(Vector2 screenPosition)
		{
			_startCell = ResolveCell(screenPosition);
			if (_startCell.HasValue)
			{
				_dragStartPosition = screenPosition;
				_isDragging = true;
			}
			else
			{
				_isDragging = false;
			}
		}

		private void ProcessDrag(Vector2 currentScreenPosition)
		{
			if (!_startCell.HasValue)
			{
				CancelDrag();
				return;
			}

			Vector2 delta = currentScreenPosition - _dragStartPosition;
			float magnitude = delta.magnitude;

			// Inside deadzone: wait for more definitive movement
			if (magnitude < _swipeDeadzone)
			{
				return;
			}

			// Threshold reached: clamp to strictly one orthogonal direction and trigger swipe
			if (magnitude >= _swipeThreshold)
			{
				Vector2Int direction = ClampToOrthogonalDirection(delta);
				Vector2Int from = _startCell.Value;
				Vector2Int to = from + direction;

				// Consume drag so a single stroke does not fire repeatedly
				CancelDrag();

				OnSwipeDetected?.Invoke(from, to);
			}
		}

		private void CancelDrag()
		{
			_isDragging = false;
			_startCell = null;
		}

		/// <summary>
		/// Clamps any 2D swipe vector strictly to one of the 4 cardinal directions (Up, Down, Left, Right).
		/// </summary>
		public static Vector2Int ClampToOrthogonalDirection(Vector2 delta)
		{
			if (Mathf.Abs(delta.x) > Mathf.Abs(delta.y))
			{
				return delta.x > 0 ? Vector2Int.right : Vector2Int.left;
			}
			else
			{
				return delta.y > 0 ? Vector2Int.up : Vector2Int.down;
			}
		}

		private Vector2Int? ResolveCell(Vector2 screenPosition)
		{
			if (_cellResolver != null)
			{
				return _cellResolver(screenPosition);
			}

			Camera cam = _camera != null ? _camera : Camera.main;
			if (cam == null)
			{
				return null;
			}

			Vector3 worldPos = cam.ScreenToWorldPoint(screenPosition);
			Vector3 localPos = transform.InverseTransformPoint(worldPos);
			return new Vector2Int(Mathf.RoundToInt(localPos.x), Mathf.RoundToInt(localPos.y));
		}
	}
}
