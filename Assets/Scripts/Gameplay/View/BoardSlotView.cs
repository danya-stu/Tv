using UnityEngine;

namespace PotionCraft.Gameplay.View
{
	/// <summary>
	/// Static backdrop cell that sits under a gameplay tile: a softly rounded,
	/// recessed socket carved into the alchemist's wooden table.
	///
	/// Slots never move and never animate, so they carry no coroutines. The two
	/// checkerboard sprites (dark / light) are baked procedurally exactly once per
	/// session and shared by every instance, which keeps per-level allocation at
	/// zero: BoardView preallocates the whole slot pool at level start and the
	/// slots only ever get repositioned afterwards.
	/// </summary>
	[DisallowMultipleComponent]
	public sealed class BoardSlotView : MonoBehaviour
	{
		/// <summary>Sorting order for slots; tiles must render above this value.</summary>
		public const int SlotSortingOrder = -100;

		private const int SlotPixelSize = 64;

		private static readonly Color DarkShade = new Color(38f / 255f, 30f / 255f, 49f / 255f, 1f);
		private static readonly Color LightShade = new Color(52f / 255f, 41f / 255f, 66f / 255f, 1f);

		private static Sprite _darkSlotSprite;
		private static Sprite _lightSlotSprite;

		[SerializeField] private SpriteRenderer _renderer;

		private Vector2Int _gridPosition;
		private bool _isDarkShade;

		public Vector2Int GridPosition => _gridPosition;
		public bool IsDarkShade => _isDarkShade;
		public SpriteRenderer Renderer => _renderer;

		private void Awake()
		{
			EnsureRenderer();
		}

		/// <summary>
		/// Places the slot under cell <paramref name="gridPos"/> at
		/// <paramref name="worldPos"/> and scales it so it spans exactly one cell
		/// of <paramref name="size"/> world units. The checkerboard shade is derived
		/// from the cell parity so rows stay easy to read.
		/// </summary>
		public void Init(Vector2Int gridPos, Vector3 worldPos, float size)
		{
			EnsureRenderer();

			_gridPosition = gridPos;
			_isDarkShade = ((gridPos.x + gridPos.y) & 1) == 0;

			name = $"Slot_{gridPos.x}_{gridPos.y}";
			transform.position = worldPos;
			transform.localRotation = Quaternion.identity;
			transform.localScale = new Vector3(size, size, 1f);

			_renderer.sprite = _isDarkShade ? GetDarkSlotSprite() : GetLightSlotSprite();
			_renderer.color = Color.white;
			_renderer.sortingOrder = SlotSortingOrder;
			_renderer.enabled = true;
		}

		/// <summary>Explicit shade override, used when a level wants a custom pattern.</summary>
		public void SetShade(bool dark)
		{
			EnsureRenderer();

			_isDarkShade = dark;
			_renderer.sprite = dark ? GetDarkSlotSprite() : GetLightSlotSprite();
		}

		/// <summary>Hides the slot without destroying it, for pooled reuse.</summary>
		public void SetVisible(bool visible)
		{
			EnsureRenderer();
			_renderer.enabled = visible;
		}

		private void EnsureRenderer()
		{
			if (_renderer != null)
			{
				return;
			}

			_renderer = GetComponent<SpriteRenderer>();
			if (_renderer == null)
			{
				_renderer = gameObject.AddComponent<SpriteRenderer>();
			}

			_renderer.sortingOrder = SlotSortingOrder;
		}

		private static Sprite GetDarkSlotSprite()
		{
			if (_darkSlotSprite == null)
			{
				_darkSlotSprite = BakeSlotSprite(DarkShade);
			}

			return _darkSlotSprite;
		}

		private static Sprite GetLightSlotSprite()
		{
			if (_lightSlotSprite == null)
			{
				_lightSlotSprite = BakeSlotSprite(LightShade);
			}

			return _lightSlotSprite;
		}

		/// <summary>
		/// Bakes one rounded socket: a signed-distance rounded square with an inner
		/// shadow along the rim and a faint highlight at the bottom, so the cell
		/// reads as a depression in wood rather than a flat square. Transparent
		/// outside the socket, so the table texture shows through the gutters.
		/// </summary>
		private static Sprite BakeSlotSprite(Color baseColor)
		{
			const int size = SlotPixelSize;

			Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
			{
				filterMode = FilterMode.Bilinear,
				wrapMode = TextureWrapMode.Clamp,
			};

			Color32[] pixels = new Color32[size * size];

			float half = size * 0.5f;
			float inset = size * 0.06f;
			float radius = size * 0.16f;
			float innerHalf = half - inset - radius;
			float rimWidth = size * 0.12f;

			for (int y = 0; y < size; y++)
			{
				float v = (y + 0.5f) - half;

				for (int x = 0; x < size; x++)
				{
					float u = (x + 0.5f) - half;

					float dx = Mathf.Max(Mathf.Abs(u) - innerHalf, 0f);
					float dy = Mathf.Max(Mathf.Abs(v) - innerHalf, 0f);
					float dist = Mathf.Sqrt((dx * dx) + (dy * dy)) - radius;

					float coverage = Mathf.Clamp01(0.5f - dist);
					int index = (y * size) + x;

					if (coverage <= 0f)
					{
						pixels[index] = new Color32(0, 0, 0, 0);
						continue;
					}

					// Darker along the rim (inner shadow), lighter toward the middle.
					float rim = Mathf.Clamp01(-dist / rimWidth);
					float shade = Mathf.Lerp(1.08f, 0.72f, rim);

					// Faint bounce light on the lower half of the socket.
					float bounce = Mathf.Clamp01((-v) / half) * 0.06f;
					shade += bounce;

					float r = Mathf.Clamp01(baseColor.r * shade);
					float g = Mathf.Clamp01(baseColor.g * shade);
					float b = Mathf.Clamp01(baseColor.b * shade);

					pixels[index] = new Color32(
						(byte)Mathf.RoundToInt(r * 255f),
						(byte)Mathf.RoundToInt(g * 255f),
						(byte)Mathf.RoundToInt(b * 255f),
						(byte)Mathf.RoundToInt(Mathf.Clamp01(coverage) * 255f));
				}
			}

			texture.SetPixels32(pixels);
			texture.Apply();

			// Pixels-per-unit equals the texture size, so the sprite is exactly one
			// world unit wide and localScale can be set straight to the cell size.
			return Sprite.Create(texture, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), size);
		}
	}
}
