using System.Collections;
using UnityEngine;
using PotionCraft.Core;

namespace PotionCraft.Gameplay
{
	/// <summary>
	/// Placeholder visual for a single grid cell. Renders a plain tinted
	/// square generated at runtime (no art assets required yet) and exposes
	/// the move/shrink animations GridView needs while swaps and cascades
	/// resolve. Swap this out for real sprites later without touching
	/// GridView's public API: only SetColor/SetItem and the sprite creation
	/// need to change.
	/// </summary>
	[RequireComponent(typeof(SpriteRenderer))]
	public sealed class TileView : MonoBehaviour
	{
		private static Sprite _placeholderSprite;

		private SpriteRenderer _spriteRenderer;

		public int GridX { get; private set; }
		public int GridY { get; private set; }

		private void Awake()
		{
			_spriteRenderer = GetComponent<SpriteRenderer>();
			_spriteRenderer.sprite = GetOrCreatePlaceholderSprite();
		}

		private static Sprite GetOrCreatePlaceholderSprite()
		{
			if (_placeholderSprite != null)
				return _placeholderSprite;

			// A single white pixel, tinted per-tile via SpriteRenderer.color.
			// This is intentionally the simplest possible placeholder so the
			// gameplay layer never depends on any imported art asset.
			var texture = new Texture2D(1, 1, TextureFormat.RGBA32, false);
			texture.SetPixel(0, 0, Color.white);
			texture.Apply();
			texture.filterMode = FilterMode.Point;

			_placeholderSprite = Sprite.Create(texture, new Rect(0f, 0f, 1f, 1f), new Vector2(0.5f, 0.5f), 1f);
			return _placeholderSprite;
		}

		public void SetGridPosition(int x, int y)
		{
			GridX = x;
			GridY = y;
		}

		public void SetColor(ItemColor color)
		{
			_spriteRenderer.color = ColorForItemColor(color);
		}

		/// <summary>
		/// Sets the tile's visual from a full Item, including a brightened
		/// "glow" tint when the item carries a Reaction Catalyst effect, so
		/// catalyst tiles are visually distinguishable from plain items even
		/// before dedicated catalyst art/icons exist.
		/// </summary>
		public void SetItem(Item item)
		{
			SetColor(item.Color);

			if (item.Catalyst != CatalystType.None)
				_spriteRenderer.color = Color.Lerp(_spriteRenderer.color, Color.white, 0.45f);
		}

		public static Color ColorForItemColor(ItemColor color)
		{
			switch (color)
			{
				case ItemColor.Red:
					return new Color(0.85f, 0.2f, 0.2f);
				case ItemColor.Blue:
					return new Color(0.2f, 0.45f, 0.85f);
				case ItemColor.Green:
					return new Color(0.25f, 0.7f, 0.3f);
				case ItemColor.Yellow:
					return new Color(0.9f, 0.8f, 0.2f);
				case ItemColor.Purple:
					return new Color(0.6f, 0.3f, 0.75f);
				default:
					return Color.clear;
			}
		}

		public IEnumerator MoveTo(Vector3 targetLocalPosition, float duration)
		{
			Vector3 start = transform.localPosition;

			if (duration <= 0f)
			{
				transform.localPosition = targetLocalPosition;
				yield break;
			}

			float elapsed = 0f;
			while (elapsed < duration)
			{
				elapsed += Time.deltaTime;
				float t = Mathf.Clamp01(elapsed / duration);
				transform.localPosition = Vector3.Lerp(start, targetLocalPosition, t);
				yield return null;
			}

			transform.localPosition = targetLocalPosition;
		}

		public IEnumerator ShrinkAndDisable(float duration)
		{
			Vector3 startScale = transform.localScale;

			if (duration <= 0f)
			{
				transform.localScale = Vector3.zero;
				gameObject.SetActive(false);
				yield break;
			}

			float elapsed = 0f;
			while (elapsed < duration)
			{
				elapsed += Time.deltaTime;
				float t = Mathf.Clamp01(elapsed / duration);
				transform.localScale = Vector3.Lerp(startScale, Vector3.zero, t);
				yield return null;
			}

			transform.localScale = Vector3.zero;
			gameObject.SetActive(false);
		}
	}
}
