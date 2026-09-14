using System.Collections.Generic;
using UnityEngine;

namespace PotionCraft.Gameplay
{
	/// <summary>
	/// Generates simple rounded-rect sprites at runtime, with proper 9-slice
	/// borders so they scale cleanly at any size. Used for both world-space
	/// grid tiles and UI panels/buttons/swatches, so the whole game reads as
	/// more polished than flat, sharp-cornered rectangles while still
	/// requiring zero imported art assets. Swap callers to real art sprites
	/// later without changing any public API here.
	/// </summary>
	public static class UISpriteFactory
	{
		private static readonly Dictionary<(int size, int radius, int border), Sprite> _cache =
			new Dictionary<(int, int, int), Sprite>();

		/// <summary>
		/// Creates (and caches) a square rounded-rect sprite. The base fill is
		/// opaque white so callers can tint it to any color via
		/// SpriteRenderer.color or Image.color; an optional darker inset ring
		/// reads as a subtle border/bevel. Results are cached per unique
		/// (size, cornerRadius, borderThickness) combination.
		/// </summary>
		public static Sprite GetRoundedSquare(int size = 64, int cornerRadius = 14, int borderThickness = 0)
		{
			var key = (size, cornerRadius, borderThickness);
			if (_cache.TryGetValue(key, out Sprite cached) && cached != null)
				return cached;

			var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
			{
				filterMode = FilterMode.Bilinear,
				wrapMode = TextureWrapMode.Clamp
			};

			for (int y = 0; y < size; y++)
			{
				for (int x = 0; x < size; x++)
				{
					float px = x + 0.5f;
					float py = y + 0.5f;

					float outerCoverage = Coverage(px, py, 0f, 0f, size, size, cornerRadius);
					Color color = Color.white;

					if (borderThickness > 0 && outerCoverage > 0f)
					{
						float innerRadius = Mathf.Max(0f, cornerRadius - borderThickness);
						float innerCoverage = Coverage(
							px, py,
							borderThickness, borderThickness,
							size - borderThickness * 2f, size - borderThickness * 2f,
							innerRadius);

						float borderMix = (1f - innerCoverage) * 0.35f;
						color = Color.Lerp(Color.white, Color.black, borderMix);
					}

					texture.SetPixel(x, y, new Color(color.r, color.g, color.b, outerCoverage));
				}
			}

			texture.Apply();

			float b = Mathf.Max(cornerRadius, 1);
			var sprite = Sprite.Create(
				texture,
				new Rect(0f, 0f, size, size),
				new Vector2(0.5f, 0.5f),
				100f,
				0,
				SpriteMeshType.FullRect,
				new Vector4(b, b, b, b));

			_cache[key] = sprite;
			return sprite;
		}

		/// <summary>
		/// Anti-aliased coverage (0..1) of a point against a rounded rect
		/// positioned at (x0, y0) with the given width/height/corner radius.
		/// Used both for the sprite's outer silhouette and, with an inset
		/// origin/size, for the inner edge of an optional border ring.
		/// </summary>
		private static float Coverage(float px, float py, float x0, float y0, float w, float h, float radius)
		{
			float x1 = x0 + w;
			float y1 = y0 + h;

			bool inCornerX = px < x0 + radius || px > x1 - radius;
			bool inCornerY = py < y0 + radius || py > y1 - radius;

			float edgeDist;
			if (inCornerX && inCornerY)
			{
				float cx = px < x0 + radius ? x0 + radius : x1 - radius;
				float cy = py < y0 + radius ? y0 + radius : y1 - radius;
				float dist = Vector2.Distance(new Vector2(px, py), new Vector2(cx, cy));
				edgeDist = radius - dist;
			}
			else
			{
				edgeDist = Mathf.Min(Mathf.Min(px - x0, x1 - px), Mathf.Min(py - y0, y1 - py));
			}

			return Mathf.Clamp01(edgeDist + 0.5f);
		}
	}
}
