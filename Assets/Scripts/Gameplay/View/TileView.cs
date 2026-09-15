using System;
using System.Collections;
using PotionCraft.Core;
using UnityEngine;
#if TMP_PRESENT
using TMPro;
#endif

namespace PotionCraft.Gameplay.View
{
	/// <summary>
	/// Visual representation of a single board cell, driven entirely by
	/// coroutines and TweenEasing curves (no tween plugin). Instances are pooled
	/// by BoardView and never destroyed during cascades: ResetState() returns the
	/// tile to a clean, reusable state.
	///
	/// Artwork comes from PotionSpriteFactory: every color owns a unique flask
	/// silhouette with a baked-in accessibility rune, so the renderer tint stays
	/// white and never washes the baked art out. The tint is only animated as a
	/// transient flash during a transmutation.
	///
	/// The TextMeshPro glyph is an optional extra label. TextMeshPro is not
	/// referenced by the PotionCraft.Gameplay assembly definition, so that field
	/// is compiled only when the TMP_PRESENT define is set; the runes baked into
	/// the sprites already cover the accessibility requirement without it.
	/// </summary>
	[DisallowMultipleComponent]
	public sealed class TileView : MonoBehaviour
	{
		/// <summary>Tiles must render above the board slots (which use -100).</summary>
		public const int TileSortingOrder = 0;

		[SerializeField] private SpriteRenderer _renderer;
#if TMP_PRESENT
		[SerializeField] private TextMeshPro _glyphText; // Optional rune glyph.
#endif

		private Vector3 _baseScale = Vector3.one;

		/// <summary>Logical cell this tile currently represents.</summary>
		public Vector2Int GridPosition { get; set; }

		/// <summary>The item last pushed into this tile.</summary>
		public Item CurrentItem { get; private set; }

		/// <summary>Scale the tile returns to after any pop or shrink animation.</summary>
		public Vector3 BaseScale => _baseScale;

		public SpriteRenderer Renderer
		{
			get
			{
				EnsureRenderer();
				return _renderer;
			}
		}

		private void Awake()
		{
			EnsureRenderer();
		}

		private void EnsureRenderer()
		{
			if (_renderer == null)
			{
				_renderer = GetComponent<SpriteRenderer>();
			}

			if (_renderer == null)
			{
				_renderer = gameObject.AddComponent<SpriteRenderer>();
			}

			_renderer.sortingOrder = TileSortingOrder;
		}

		/// <summary>Sets the base scale the tile animates back to (board cell size).</summary>
		public void SetBaseScale(Vector3 scale)
		{
			_baseScale = scale;
		}

		/// <summary>Places the tile at a world position and paints the given item.</summary>
		public void SetData(Item item, Vector3 worldPos)
		{
			EnsureRenderer();

			CurrentItem = item;
			transform.position = worldPos;
			ApplyItemVisual(item);
		}

		/// <summary>Repaints the tile without moving it.</summary>
		public void SetItem(Item item)
		{
			EnsureRenderer();

			CurrentItem = item;
			ApplyItemVisual(item);
		}

		/// <summary>
		/// Pulls the baked flask sprite for this item and resets the tint to white.
		/// The sprite already carries the liquid color, the glass and the rune, so
		/// tinting it would only dull the artwork.
		/// </summary>
		private void ApplyItemVisual(Item item)
		{
			Sprite sprite = PotionSpriteFactory.GetPotionSprite(item.Color, item.Catalyst);

			_renderer.sprite = sprite;
			_renderer.color = sprite == null ? Color.clear : Color.white;
			ApplyGlyph(item);
		}

		/// <summary>Moves the tile to a world position along the given easing curve.</summary>
		public IEnumerator AnimateMove(Vector3 targetWorldPos, float duration, Func<float, float> easing)
		{
			Vector3 start = transform.position;

			if (duration <= 0f)
			{
				transform.position = targetWorldPos;
				yield break;
			}

			float elapsed = 0f;
			while (elapsed < duration)
			{
				elapsed += Time.deltaTime;
				float t = Mathf.Clamp01(elapsed / duration);
				float eased = easing == null ? t : easing(t);
				transform.position = Vector3.LerpUnclamped(start, targetWorldPos, eased);
				yield return null;
			}

			transform.position = targetWorldPos;
		}

		/// <summary>Scales the tile along the given easing curve.</summary>
		public IEnumerator AnimateScale(Vector3 targetScale, float duration, Func<float, float> easing)
		{
			Vector3 start = transform.localScale;

			if (duration <= 0f)
			{
				transform.localScale = targetScale;
				yield break;
			}

			float elapsed = 0f;
			while (elapsed < duration)
			{
				elapsed += Time.deltaTime;
				float t = Mathf.Clamp01(elapsed / duration);
				float eased = easing == null ? t : easing(t);
				transform.localScale = Vector3.LerpUnclamped(start, targetScale, eased);
				yield return null;
			}

			transform.localScale = targetScale;
		}

		/// <summary>
		/// Reaction Catalyst transmutation: the tile pulses up and flashes toward the
		/// incoming essence color, swaps to the new flask sprite at the peak of the
		/// pulse, then settles back to its base scale and a clean white tint.
		/// </summary>
		public IEnumerator AnimateTransmute(Item newItem, Color targetColor, float duration)
		{
			EnsureRenderer();

			if (duration <= 0f)
			{
				CurrentItem = newItem;
				ApplyItemVisual(newItem);
				transform.localScale = _baseScale;
				yield break;
			}

			Color startColor = _renderer.color;
			Color flashColor = Color.Lerp(targetColor, Color.white, 0.55f);
			Vector3 peakScale = _baseScale * 1.25f;
			float half = duration * 0.5f;

			float elapsed = 0f;
			while (elapsed < half)
			{
				elapsed += Time.deltaTime;
				float t = Mathf.Clamp01(elapsed / half);
				float eased = TweenEasing.EaseOutBack(t);
				transform.localScale = Vector3.LerpUnclamped(_baseScale, peakScale, eased);
				_renderer.color = Color.Lerp(startColor, flashColor, TweenEasing.EaseInOutQuad(t));
				yield return null;
			}

			// Peak of the pulse: the vessel itself becomes the new essence.
			CurrentItem = newItem;
			ApplyItemVisual(newItem);
			_renderer.color = flashColor;

			elapsed = 0f;
			while (elapsed < half)
			{
				elapsed += Time.deltaTime;
				float t = Mathf.Clamp01(elapsed / half);
				float eased = TweenEasing.EaseInOutQuad(t);
				transform.localScale = Vector3.LerpUnclamped(peakScale, _baseScale, eased);
				_renderer.color = Color.Lerp(flashColor, Color.white, eased);
				yield return null;
			}

			transform.localScale = _baseScale;
			_renderer.color = Color.white;
		}

		/// <summary>Clean state for the pool: base scale, no item, nothing rendered.</summary>
		public void ResetState()
		{
			EnsureRenderer();

			CurrentItem = Item.Empty;
			GridPosition = new Vector2Int(-1, -1);
			transform.localScale = _baseScale;
			transform.localRotation = Quaternion.identity;
			_renderer.sprite = null;
			_renderer.color = Color.clear;
			ApplyGlyph(Item.Empty);
		}

		/// <summary>
		/// Board palette for the five essence colors, used for particle tints and
		/// transmutation flashes. None renders invisible.
		/// </summary>
		public static Color ColorForItem(Item item)
		{
			if (item.Color == ItemColor.None)
			{
				return Color.clear;
			}

			Color color = PotionSpriteFactory.LiquidColor(item.Color);

			// Catalyst tiles read brighter, matching their energized artwork.
			if (item.Catalyst != CatalystType.None)
			{
				color = Color.Lerp(color, Color.white, 0.45f);
			}

			return color;
		}

		/// <summary>Palette entry for a bare color, used by transmutation targets.</summary>
		public static Color ColorForItemColor(ItemColor color)
		{
			return ColorForItem(Item.Create(color));
		}

		private void ApplyGlyph(Item item)
		{
#if TMP_PRESENT
			if (_glyphText == null)
			{
				return;
			}

			_glyphText.text = GlyphForRune(item.Rune);
#endif
		}

		/// <summary>
		/// Color-blind-safe glyph for each rune. Kept outside the TMP_PRESENT guard
		/// so the mapping can also feed sprite-based glyphs later.
		/// </summary>
		public static string GlyphForRune(RuneType rune)
		{
			switch (rune)
			{
				case RuneType.Flame:
					return "^";
				case RuneType.Droplet:
					return "~";
				case RuneType.Crystal:
					return "#";
				case RuneType.Star:
					return "*";
				case RuneType.Eye:
					return "o";
				default:
					return string.Empty;
			}
		}
	}
}
