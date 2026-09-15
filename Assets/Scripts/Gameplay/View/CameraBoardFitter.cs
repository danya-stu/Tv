using UnityEngine;

namespace PotionCraft.Gameplay.View
{
	/// <summary>
	/// Keeps the whole board inside the viewport on any aspect ratio, including
	/// tall phone screens such as 20:9. Attach to the Main Camera.
	///
	/// The board is centered on the world origin by BoardView.CalculateOriginOffset,
	/// so the camera only has to sit at the origin and pick an orthographic size
	/// that makes the required world width fit the current aspect ratio. The fit
	/// runs in LateUpdate as well, so a device rotation or a resized Game view is
	/// picked up immediately.
	/// </summary>
	[RequireComponent(typeof(Camera))]
	[DisallowMultipleComponent]
	public sealed class CameraBoardFitter : MonoBehaviour
	{
		[SerializeField] private float _cellSize = 1.2f;
		[SerializeField] private int _boardWidth = 8;
		[SerializeField] private float _padding = 0.5f;

		private Camera _camera;

		/// <summary>World width the camera guarantees to show.</summary>
		public float TargetWidth => (_boardWidth * _cellSize) + (_padding * 2f);

		private void Awake()
		{
			_camera = GetComponent<Camera>();
		}

		private void Start()
		{
			Fit();
		}

		private void LateUpdate()
		{
			Fit();
		}

		/// <summary>
		/// Centers the camera on the board and derives the orthographic size from
		/// the required world width: orthographicSize is a half-height, so the
		/// half-width must be divided by the aspect ratio.
		/// </summary>
		public void Fit()
		{
			if (_camera == null)
			{
				_camera = GetComponent<Camera>();
				if (_camera == null)
				{
					return;
				}
			}

			_camera.orthographic = true;
			transform.position = new Vector3(0f, 0f, -10f);

			float aspect = _camera.aspect;
			if (aspect <= 0f)
			{
				return;
			}

			float targetWidth = (_boardWidth * _cellSize) + (_padding * 2f);
			float targetHeight = (_boardWidth * _cellSize) + (_padding * 2f); // для квадратного поля 8x8
			float sizeByWidth = (targetWidth / 2f) / aspect;
			float sizeByHeight = targetHeight / 2f;
			_camera.orthographicSize = Mathf.Max(sizeByWidth, sizeByHeight);
		}

		/// <summary>Lets a bootstrap object push the real board metrics at runtime.</summary>
		public void Configure(int boardWidth, float cellSize, float padding)
		{
			_boardWidth = boardWidth;
			_cellSize = cellSize;
			_padding = padding;
			Fit();
		}
	}
}
