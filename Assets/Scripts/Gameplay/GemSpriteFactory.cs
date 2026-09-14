using System.Collections.Generic;
using UnityEngine;

namespace PotionCraft.Gameplay
{
	/// <summary>
	/// Procedurally renders a faceted "gem" icon sprite used for match-3
	/// tiles, replacing the plain rounded-square placeholder with something
	/// that reads as an alchemical reagent/crystal instead of a flat shape.
	/// The sprite is baked in neutral gray/white tones (facet shading +
	/// specular highlight + a tint-invariant near-black outline), so
	/// TileView's existing SpriteRenderer.color tint (see TileView.SetColor)
	/// still produces the correct per-color result without any other
	/// call-site changes. No external art assets or packages required.
	/// </summary>
	public static class GemSpriteFactory
	{
		private static readonly Dictionary<int, Sprite> Cache = new Dictionary<int, Sprite>();

		// Kite-shaped gem silhouette: pointed top and bottom, wide "belt"
		// near the top third, matching a classic cut-crystal icon rather
		// than a square. All coordinates are in a [-1, 1] normalized space.
		private static readonly Vector2 Top = new Vector2(0f, 0.95f);
		private static readonly Vector2 Right = new Vector2(0.8f, 0.12f);
		private static readonly Vector2 Bottom = new Vector2(0f, -0.95f);
		private static readonly Vector2 Left = new Vector2(-0.8f, 0.12f);
		private const float BeltY = 0.12f;

		/// <summary>
		/// Returns a cached (or newly baked) gem icon sprite of the given
		/// pixel size. The sprite is neutral-colored; tint it via
		/// SpriteRenderer.color / Image.color to get a specific gem color.
		/// </summary>
		public static Sprite GetGem(int size = 64)
		{
			if (Cache.TryGetValue(size, out Sprite cached) && cached != null)
				return cached;

			var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
			{
				filterMode = FilterMode.Bilinear,
				wrapMode = TextureWrapMode.Clamp,
			};

			var pixels = new Color32[size * size];

			// One pixel's width in the [-1, 1] normalized space, used to
			// feather the silhouette edge by roughly 1.5 pixels so it
			// anti-aliases instead of looking jagged at small tile sizes.
			float pixelUnit = 2f / size;
			float edgeFeather = pixelUnit * 1.5f;
			const float outlineWidth = 0.09f;

			var highlightCenter = new Vector2(-0.28f, 0.5f);

			for (int y = 0; y < size; y++)
			{
				float v = (y + 0.5f) / size * 2f - 1f;
				for (int x = 0; x < size; x++)
				{
					float u = (x + 0.5f) / size * 2f - 1f;
					var p = new Vector2(u, v);

					float dist = SignedDistanceToQuad(p);
					float coverage = Mathf.Clamp01(dist / edgeFeather + 0.5f);

					if (coverage <= 0f)
					{
						pixels[y * size + x] = new Color32(0, 0, 0, 0);
						continue;
					}

					float brightness = FacetBrightness(u, v);

					// Specular highlight: a soft bright blob near the upper
					// left, like light catching one facet of a cut gem.
					float hDist = Vector2.Distance(p, highlightCenter);
					float highlight = Mathf.Clamp01(1f - hDist / 0.22f);
					highlight *= highlight;
					brightness = Mathf.Clamp01(brightness + highlight * 0.35f);

					// Tint-invariant dark outline: pushes brightness toward
					near zero right at the silhouette edge so the gem keeps
					a crisp rim regardless of the color it's tinted with.
					float outlineT = Mathf.Clamp01(dist / outlineWidth);
					float outlineMul = Mathf.Lerp(0.08f, 1f, outlineT);
					brightness *= outlineMul;

					byte c = (byte)Mathf.RoundToInt(Mathf.Clamp01(brightness) * 255f);
					byte a = (byte)Mathf.RoundToInt(coverage * 255f);
					pixels[y * size + x] = new Color32(c, c, c, a);
				}
			}

			texture.SetPixels32(pixels);
			texture.Apply();

			var sprite = Sprite.Create(texture, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), size);
			Cache[size] = sprite;
			return sprite;
		}

		/// <summary>
		/// Approximate signed distance from a point to the gem's convex
		/// quad silhouette: positive inside (magnitude ~ distance to the
		/// nearest edge), negative outside. Computed as the minimum of the
		/// four inward half-plane distances, which is exact whenever the
		/// point's closest feature is an edge (the common case used here,
		/// since this only drives a ~1-2px anti-aliasing band).
		/// </summary>
		private static float SignedDistanceToQuad(Vector2 p)
		{
			Span<Vector2> verts = stackalloc Vector2[4] { Top, Right, Bottom, Left };
			Vector2 centroid = (Top + Right + Bottom + Left) / 4f;

			float minDist = float.MaxValue;
			for (int i = 0; i < 4; i++)
			{
				Vector2 a = verts[i];
				Vector2 b = verts[(i + 1) % 4];
				Vector2 edge = b - a;
				Vector2 normal = new Vector2(edge.y, -edge.x).normalized;

				if (Vector2.Dot(centroid - a, normal) < 0f)
					normal = -normal;

				float dist = Vector2.Dot(p - a, normal);
				if (dist < minDist)
					minDist = dist;
			}

			return minDist;
		}

		/// <summary>
		/// Splits the gem into four shaded facets (upper/lower x left/right)
		/// plus a subtle darkened seam where facets meet, so the icon reads
		/// as a faceted crystal rather than one flat tint.
		/// </summary>
		private static float FacetBrightness(float u, float v)
		{
			bool right = u >= 0f;
			bool upper = v >= BeltY;

			float baseBrightness;
			if (upper && !right)
				baseBrightness = 0.95f; // upper-left: brightest, faces the light
			else if (upper && right)
				baseBrightness = 0.75f;
			else if (!upper && !right)
				baseBrightness = 0.55f;
			else
				baseBrightness = 0.4f; // lower-right: darkest facet

			float seamU = Mathf.Abs(u);
			float seamV = Mathf.Abs(v - BeltY);
			float seam = Mathf.Min(seamU, seamV);
			float seamT = Mathf.Clamp01(1f - seam / 0.035f);
			float seamDarken = 1f - seamT * 0.25f;

			return baseBrightness * seamDarken;
		}
	}
}
