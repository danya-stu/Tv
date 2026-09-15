using System.Collections.Generic;
using PotionCraft.Core;
using UnityEngine;

namespace PotionCraft.Gameplay.View
{
	/// <summary>
	/// Procedural art foundry for the alchemical flasks. Every tile sprite is
	/// baked from signed distance fields at runtime, so the project ships without
	/// a single imported texture and without any third-party art package.
	///
	/// Each essence color owns a unique silhouette, and each silhouette carries a
	/// high-contrast rune at its center: shape plus rune means the board stays
	/// readable for color-blind and low-vision players, who never have to rely on
	/// hue alone.
	///
	/// Sprites are baked at most once per (color, catalyst) pair and cached for
	/// the whole session; the pixel buffer is shared between bakes. After warm-up
	/// the factory allocates nothing, so cascades never touch the GC.
	/// </summary>
	public static class PotionSpriteFactory
	{
		/// <summary>Baked texture resolution, in pixels, per flask.</summary>
		public const int SpriteSize = 128;

		/// <summary>
		/// Pixels-per-unit equals the texture size, so an unscaled sprite spans
		/// exactly one world unit and BoardView can drive the fit with a plain
		/// localScale of cellSize * tileScaleFactor.
		/// </summary>
		public const float PixelsPerUnit = SpriteSize;

		// Roughly one pixel wide in normalized [-1, 1] space; used for edge fade.
		private const float EdgeSoftness = 2f / SpriteSize;

		// Width of the translucent white glass rim, in normalized units.
		private const float RimWidth = 0.075f;

		private static readonly Dictionary<(ItemColor, CatalystType), Sprite> Cache =
			new Dictionary<(ItemColor, CatalystType), Sprite>(32);

		private static Color32[] _buffer;

		private static readonly Color GlassTint = new Color(1f, 1f, 1f, 0.22f);
		private static readonly Color RimColor = new Color(1f, 1f, 1f, 0.85f);
		private static readonly Color BrassColor = new Color(0.78f, 0.6f, 0.26f, 1f);
		private static readonly Color RuneColor = new Color(1f, 0.98f, 0.92f, 1f);
		private static readonly Color CoreColor = new Color(1f, 0.62f, 0.18f, 1f);
		private static readonly Color CrackColor = new Color(0.12f, 0.08f, 0.14f, 1f);
		private static readonly Color GoldColor = new Color(1f, 0.84f, 0.38f, 1f);

		/// <summary>
		/// Returns the flask sprite for an item, baking it on first request.
		/// Returns null for empty cells, which renders nothing.
		/// </summary>
		public static Sprite GetPotionSprite(ItemColor color, CatalystType catalyst)
		{
			// The Philosopher's Elixir is colorless by design, so it is cached once
			// under a single key instead of once per essence color.
			if (catalyst == CatalystType.ColorBomb)
			{
				color = ItemColor.None;
			}
			else if (color == ItemColor.None)
			{
				return null;
			}

			(ItemColor, CatalystType) key = (color, catalyst);

			if (Cache.TryGetValue(key, out Sprite cached) && cached != null)
			{
				return cached;
			}

			Sprite baked = Bake(color, catalyst);
			Cache[key] = baked;
			return baked;
		}

		/// <summary>Convenience overload for a full item.</summary>
		public static Sprite GetPotionSprite(Item item)
		{
			return GetPotionSprite(item.Color, item.Catalyst);
		}

		/// <summary>
		/// Bakes every flask variant up front, so the first cascade of a level never
		/// pays for texture generation. Call once while the level is loading.
		/// </summary>
		public static void Prewarm()
		{
			for (int colorIndex = (int)ItemColor.Red; colorIndex <= (int)ItemColor.Purple; colorIndex++)
			{
				ItemColor color = (ItemColor)colorIndex;

				GetPotionSprite(color, CatalystType.None);
				GetPotionSprite(color, CatalystType.HorizontalLine);
				GetPotionSprite(color, CatalystType.VerticalLine);
				GetPotionSprite(color, CatalystType.BombArea);
			}

			GetPotionSprite(ItemColor.None, CatalystType.ColorBomb);
		}

		/// <summary>Drops every cached sprite; only needed when unloading the game.</summary>
		public static void ClearCache()
		{
			Cache.Clear();
			_buffer = null;
		}

		// --- baking ---------------------------------------------------------

		private static Sprite Bake(ItemColor color, CatalystType catalyst)
		{
			Texture2D texture = new Texture2D(SpriteSize, SpriteSize, TextureFormat.RGBA32, false)
			{
				filterMode = FilterMode.Bilinear,
				wrapMode = TextureWrapMode.Clamp,
			};

			if (_buffer == null || _buffer.Length != SpriteSize * SpriteSize)
			{
				_buffer = new Color32[SpriteSize * SpriteSize];
			}

			for (int y = 0; y < SpriteSize; y++)
			{
				// Normalized coordinates: origin at the sprite center, +v pointing up.
				float v = (((y + 0.5f) / SpriteSize) * 2f) - 1f;

				for (int x = 0; x < SpriteSize; x++)
				{
					float u = (((x + 0.5f) / SpriteSize) * 2f) - 1f;

					Color pixel = catalyst == CatalystType.ColorBomb
						? SamplePhilosopherElixir(u, v)
						: SampleFlask(u, v, color, catalyst);

					_buffer[(y * SpriteSize) + x] = ToColor32(pixel);
				}
			}

			texture.SetPixels32(_buffer);
			texture.Apply();

			return Sprite.Create(
				texture,
				new Rect(0f, 0f, SpriteSize, SpriteSize),
				new Vector2(0.5f, 0.5f),
				PixelsPerUnit);
		}

		/// <summary>
		/// Shades one pixel of a regular (or line/bomb) flask: glass body, liquid
		/// gradient, rim light, rune and finally the catalyst overlay.
		/// </summary>
		private static Color SampleFlask(float u, float v, ItemColor color, CatalystType catalyst)
		{
			float bodyDistance = BodySdf(color, u, v);
			float corkDistance = CorkSdf(color, u, v);
			float shapeDistance = Mathf.Min(bodyDistance, corkDistance);

			float coverage = Coverage(shapeDistance);
			if (coverage <= 0f)
			{
				return Color.clear;
			}

			Color result;

			if (corkDistance < 0f && corkDistance <= bodyDistance)
			{
				// Brass stopper: lit from the upper left, darker toward the bottom.
				float shade = Mathf.Lerp(1.15f, 0.7f, Mathf.Clamp01((0.75f - v) / 0.5f));
				result = new Color(
					Mathf.Clamp01(BrassColor.r * shade),
					Mathf.Clamp01(BrassColor.g * shade),
					Mathf.Clamp01(BrassColor.b * shade),
					1f);
			}
			else
			{
				result = ShadeGlassAndLiquid(u, v, bodyDistance, color);
				result = ApplyRune(result, u, v, color);
				result = ApplyCatalystOverlay(result, u, v, bodyDistance, catalyst);
			}

			result.a *= coverage;
			return result;
		}

		/// <summary>
		/// Liquid fill with a vertical gradient (lighter at the surface, saturated at
		/// the bottom), empty tinted glass above the liquid line, and a translucent
		/// white rim hugging the silhouette.
		/// </summary>
		private static Color ShadeGlassAndLiquid(float u, float v, float bodyDistance, ItemColor color)
		{
			Color liquid = LiquidColor(color);
			float liquidTop = LiquidTop(color);

			Color result;

			if (v <= liquidTop)
			{
				// 0 at the liquid surface, 1 at the very bottom of the sprite.
				float depth = Mathf.Clamp01((liquidTop - v) / (liquidTop + 1f));
				Color bright = Color.Lerp(liquid, Color.white, 0.38f);
				Color deep = Color.Lerp(liquid, Color.black, 0.32f);
				result = Color.Lerp(bright, deep, depth);
				result.a = 1f;

				// Thin bright meniscus right at the surface of the potion.
				float meniscus = 1f - Mathf.Clamp01(Mathf.Abs(v - liquidTop) / 0.05f);
				result = Color.Lerp(result, Color.Lerp(liquid, Color.white, 0.75f), meniscus * 0.8f);
			}
			else
			{
				// Empty part of the vessel: faintly tinted glass.
				result = new Color(
					Mathf.Lerp(GlassTint.r, liquid.r, 0.25f),
					Mathf.Lerp(GlassTint.g, liquid.g, 0.25f),
					Mathf.Lerp(GlassTint.b, liquid.b, 0.25f),
					0.5f);
			}

			// Specular streak on the upper-left of the glass.
			float highlight = 1f - Mathf.Clamp01((Mathf.Abs(u + 0.34f) / 0.12f) + Mathf.Clamp01((0.1f - v) / 0.7f));
			if (highlight > 0f)
			{
				result = Color.Lerp(result, Color.white, highlight * 0.35f);
				result.a = Mathf.Max(result.a, highlight * 0.6f);
			}

			// Glass rim: strongest exactly on the silhouette, fading inward.
			float rim = Mathf.Clamp01(1f + (bodyDistance / RimWidth));
			if (bodyDistance <= 0f && rim > 0f)
			{
				result = Color.Lerp(result, RimColor, rim * RimColor.a);
				result.a = Mathf.Max(result.a, rim);
			}

			return result;
		}

		/// <summary>Stamps the accessibility rune in the middle of the liquid.</summary>
		private static Color ApplyRune(Color baseColor, float u, float v, ItemColor color)
		{
			float runeDistance = RuneSdf(color, u, v - RuneCenterY(color));
			float runeCoverage = Coverage(runeDistance);

			if (runeCoverage <= 0f)
			{
				return baseColor;
			}

			// Soft dark halo just outside the glyph keeps it readable on light liquid.
			float halo = Mathf.Clamp01(1f - (runeDistance / 0.05f)) * 0.35f;
			Color result = Color.Lerp(baseColor, Color.Lerp(baseColor, Color.black, 0.45f), halo);

			result = Color.Lerp(result, RuneColor, runeCoverage);
			result.a = Mathf.Max(result.a, runeCoverage);
			return result;
		}

		/// <summary>Line belts and the cracked bomb shell drawn on top of the flask.</summary>
		private static Color ApplyCatalystOverlay(Color baseColor, float u, float v, float bodyDistance, CatalystType catalyst)
		{
			switch (catalyst)
			{
				case CatalystType.HorizontalLine:
					return ApplyEnergyBelt(baseColor, v, bodyDistance);

				case CatalystType.VerticalLine:
					return ApplyEnergyBelt(baseColor, u, bodyDistance);

				case CatalystType.BombArea:
					return ApplyCrackedShell(baseColor, u, v);

				default:
					return baseColor;
			}
		}

		/// <summary>
		/// White energy belt across the middle of the vessel. The axis argument is
		/// v for a horizontal belt and u for a vertical one.
		/// </summary>
		private static Color ApplyEnergyBelt(Color baseColor, float axis, float bodyDistance)
		{
			const float coreHalfWidth = 0.055f;
			const float glowHalfWidth = 0.16f;

			float distance = Mathf.Abs(axis);
			if (distance > glowHalfWidth || bodyDistance > 0f)
			{
				return baseColor;
			}

			float core = 1f - Mathf.Clamp01(distance / coreHalfWidth);
			float glow = 1f - Mathf.Clamp01(distance / glowHalfWidth);

			Color result = Color.Lerp(baseColor, Color.white, (glow * glow) * 0.55f);
			result = Color.Lerp(result, Color.white, core);
			result.a = Mathf.Max(result.a, Mathf.Max(core, glow * 0.7f));
			return result;
		}

		/// <summary>Fiery core plus radial fractures for the area bomb flask.</summary>
		private static Color ApplyCrackedShell(Color baseColor, float u, float v)
		{
			Color result = baseColor;

			// Molten core glowing out of the middle of the bottle.
			float radius = Mathf.Sqrt((u * u) + ((v + 0.08f) * (v + 0.08f)));
			float core = 1f - Mathf.Clamp01(radius / 0.42f);
			if (core > 0f)
			{
				Color glow = Color.Lerp(CoreColor, Color.white, core * core);
				result = Color.Lerp(result, glow, core * 0.85f);
				result.a = Mathf.Max(result.a, core);
			}

			// Three straight fractures radiating from the core.
			float crack = Mathf.Min(
				CrackDistance(u, v + 0.08f, 0.5f),
				Mathf.Min(
					CrackDistance(u, v + 0.08f, -1.4f),
					CrackDistance(u, v + 0.08f, 2.4f)));

			float crackCoverage = 1f - Mathf.Clamp01(crack / 0.02f);
			if (crackCoverage > 0f)
			{
				result = Color.Lerp(result, CrackColor, crackCoverage * 0.8f);
				result.a = Mathf.Max(result.a, crackCoverage);
			}

			return result;
		}

		/// <summary>
		/// Distance from a point to a straight fracture starting at the core and
		/// running outward at the given angle.
		/// </summary>
		private static float CrackDistance(float u, float v, float angle)
		{
			float dirX = Mathf.Cos(angle);
			float dirY = Mathf.Sin(angle);

			// Projection onto the crack direction, clamped to a finite segment.
			float projection = Mathf.Clamp((u * dirX) + (v * dirY), 0f, 0.85f);
			float dx = u - (dirX * projection);
			float dy = v - (dirY * projection);
			return Mathf.Sqrt((dx * dx) + (dy * dy));
		}

		/// <summary>
		/// The Philosopher's Elixir: a colorless sphere swirling with white and gold
		/// light, deliberately unlike any of the five essence flasks.
		/// </summary>
		private static Color SamplePhilosopherElixir(float u, float v)
		{
			float radius = Mathf.Sqrt((u * u) + (v * v));
			float distance = radius - 0.82f;

			float coverage = Coverage(distance);
			if (coverage <= 0f)
			{
				return Color.clear;
			}

			// Spiral of alternating white and gold bands.
			float angle = Mathf.Atan2(v, u);
			float swirl = Mathf.Sin((angle * 3f) + (radius * 9f));
			float band = (swirl * 0.5f) + 0.5f;

			Color result = Color.Lerp(Color.white, GoldColor, band * 0.85f);

			// Bright nucleus fading toward the shell.
			float nucleus = 1f - Mathf.Clamp01(radius / 0.3f);
			result = Color.Lerp(result, Color.white, nucleus * 0.9f);

			// Golden rim around the sphere.
			float rim = Mathf.Clamp01(1f + (distance / RimWidth));
			result = Color.Lerp(result, GoldColor, rim * 0.7f);

			// Specular highlight on the upper left.
			float highlightDistance = Mathf.Sqrt(((u + 0.3f) * (u + 0.3f)) + ((v - 0.32f) * (v - 0.32f)));
			float highlight = 1f - Mathf.Clamp01(highlightDistance / 0.2f);
			result = Color.Lerp(result, Color.white, highlight * 0.85f);

			result.a = coverage;
			return result;
		}

		// --- silhouettes ----------------------------------------------------

		/// <summary>
		/// Signed distance to the vessel body. Negative inside. Every color gets a
		/// distinct silhouette so the board is readable without any hue at all.
		/// </summary>
		private static float BodySdf(ItemColor color, float u, float v)
		{
			switch (color)
			{
				case ItemColor.Red:
					// Fire: round bellied retort with a short neck.
					return Mathf.Min(
						SdCircle(u, v + 0.16f, 0.66f),
						SdRoundedBox(u, v - 0.62f, 0.17f, 0.26f, 0.07f));

				case ItemColor.Blue:
					// Water: elongated teardrop, wide at the bottom, tapering upward.
					return Mathf.Min(
						SdCircle(u, v + 0.34f, 0.56f),
						SdTaper(u, v, -0.34f, 0.88f, 0.56f, 0.07f));

				case ItemColor.Green:
					// Earth: square apothecary bottle with a wide shoulder.
					return Mathf.Min(
						SdRoundedBox(u, v + 0.12f, 0.56f, 0.56f, 0.14f),
						SdRoundedBox(u, v - 0.66f, 0.22f, 0.22f, 0.06f));

				case ItemColor.Yellow:
					// Air: triangular beaker with a narrow neck.
					return Mathf.Min(
						SdTaper(u, v, -0.78f, 0.36f, 0.72f, 0.16f),
						SdRoundedBox(u, v - 0.6f, 0.14f, 0.3f, 0.05f));

				case ItemColor.Purple:
					// Ether: slender elliptical vial (the brass cork is a separate shape).
					return SdEllipse(u, v + 0.12f, 0.48f, 0.7f);

				default:
					return SdCircle(u, v, 0.7f);
			}
		}

		/// <summary>
		/// Signed distance to the brass stopper. Only the ether vial has one; every
		/// other flask returns a large positive value, meaning "nowhere near".
		/// </summary>
		private static float CorkSdf(ItemColor color, float u, float v)
		{
			if (color != ItemColor.Purple)
			{
				return 10f;
			}

			return Mathf.Min(
				SdRoundedBox(u, v - 0.7f, 0.2f, 0.16f, 0.05f),
				SdRoundedBox(u, v - 0.52f, 0.26f, 0.06f, 0.03f));
		}

		/// <summary>Height of the liquid surface for each vessel, in normalized units.</summary>
		private static float LiquidTop(ItemColor color)
		{
			switch (color)
			{
				case ItemColor.Red:
					return 0.34f;
				case ItemColor.Blue:
					return 0.42f;
				case ItemColor.Green:
					return 0.3f;
				case ItemColor.Yellow:
					return 0.18f;
				case ItemColor.Purple:
					return 0.44f;
				default:
					return 0.35f;
			}
		}

		/// <summary>Vertical center of the rune inside each vessel.</summary>
		private static float RuneCenterY(ItemColor color)
		{
			switch (color)
			{
				case ItemColor.Red:
					return -0.16f;
				case ItemColor.Blue:
					return -0.3f;
				case ItemColor.Green:
					return -0.12f;
				case ItemColor.Yellow:
					return -0.3f;
				case ItemColor.Purple:
					return -0.12f;
				default:
					return 0f;
			}
		}

		/// <summary>
		/// Accessibility rune per element: triangle up (fire), triangle down (water),
		/// square (earth), circle (air), eight-point star (ether).
		/// </summary>
		private static float RuneSdf(ItemColor color, float u, float v)
		{
			switch (color)
			{
				case ItemColor.Red:
					return SdTriangle(u, v, 0.3f);

				case ItemColor.Blue:
					return SdTriangle(u, -v, 0.3f);

				case ItemColor.Green:
					return SdRoundedBox(u, v, 0.23f, 0.23f, 0.03f);

				case ItemColor.Yellow:
					return SdCircle(u, v, 0.24f);

				case ItemColor.Purple:
					return SdStar(u, v, 0.32f);

				default:
					return 10f;
			}
		}

		// --- signed distance primitives -------------------------------------

		private static float SdCircle(float u, float v, float radius)
		{
			return Mathf.Sqrt((u * u) + (v * v)) - radius;
		}

		/// <summary>Approximate signed distance to an axis-aligned ellipse.</summary>
		private static float SdEllipse(float u, float v, float halfWidth, float halfHeight)
		{
			float nx = u / halfWidth;
			float ny = v / halfHeight;
			float normalized = Mathf.Sqrt((nx * nx) + (ny * ny));
			return (normalized - 1f) * Mathf.Min(halfWidth, halfHeight);
		}

		private static float SdRoundedBox(float u, float v, float halfWidth, float halfHeight, float radius)
		{
			float dx = Mathf.Abs(u) - (halfWidth - radius);
			float dy = Mathf.Abs(v) - (halfHeight - radius);

			float outsideX = Mathf.Max(dx, 0f);
			float outsideY = Mathf.Max(dy, 0f);
			float outside = Mathf.Sqrt((outsideX * outsideX) + (outsideY * outsideY));
			float inside = Mathf.Min(Mathf.Max(dx, dy), 0f);

			return outside + inside - radius;
		}

		/// <summary>
		/// Body that linearly narrows from <paramref name="bottomHalfWidth"/> at
		/// <paramref name="bottom"/> to <paramref name="topHalfWidth"/> at
		/// <paramref name="top"/>. Used for the teardrop and the conical beaker.
		/// </summary>
		private static float SdTaper(float u, float v, float bottom, float top, float bottomHalfWidth, float topHalfWidth)
		{
			float t = Mathf.Clamp01((v - bottom) / (top - bottom));
			float halfWidth = Mathf.Lerp(bottomHalfWidth, topHalfWidth, t);

			float horizontal = Mathf.Abs(u) - halfWidth;
			float vertical = Mathf.Max(bottom - v, v - top);

			float outsideX = Mathf.Max(horizontal, 0f);
			float outsideY = Mathf.Max(vertical, 0f);
			float outside = Mathf.Sqrt((outsideX * outsideX) + (outsideY * outsideY));
			float inside = Mathf.Min(Mathf.Max(horizontal, vertical), 0f);

			return outside + inside;
		}

		/// <summary>Equilateral triangle pointing up, inscribed in the given size.</summary>
		private static float SdTriangle(float u, float v, float size)
		{
			// Three half-planes: two slanted sides and one flat base.
			const float cos30 = 0.8660254f;
			const float sin30 = 0.5f;

			float left = ((-u * cos30) + (v * sin30)) - (size * 0.5f);
			float right = ((u * cos30) + (v * sin30)) - (size * 0.5f);
			float bottom = -v - (size * 0.5f);

			return Mathf.Max(Mathf.Max(left, right), bottom);
		}

		/// <summary>Eight-point ether star: two rhombi rotated 45 degrees apart.</summary>
		private static float SdStar(float u, float v, float size)
		{
			float straight = SdRhombus(u, v, size, size);

			const float invSqrt2 = 0.70710678f;
			float ru = (u + v) * invSqrt2;
			float rv = (v - u) * invSqrt2;
			float diagonal = SdRhombus(ru, rv, size * 0.62f, size * 0.62f);

			return Mathf.Min(straight, diagonal);
		}

		private static float SdRhombus(float u, float v, float halfWidth, float halfHeight)
		{
			// |u| / w + |v| / h = 1 describes the rhombus outline.
			float value = (Mathf.Abs(u) / halfWidth) + (Mathf.Abs(v) / halfHeight) - 1f;
			return value * Mathf.Min(halfWidth, halfHeight);
		}

		// --- helpers ---------------------------------------------------------

		/// <summary>Antialiased inside/outside coverage from a signed distance.</summary>
		private static float Coverage(float distance)
		{
			return Mathf.Clamp01(0.5f - (distance / EdgeSoftness));
		}

		private static Color32 ToColor32(Color color)
		{
			return new Color32(
				(byte)Mathf.RoundToInt(Mathf.Clamp01(color.r) * 255f),
				(byte)Mathf.RoundToInt(Mathf.Clamp01(color.g) * 255f),
				(byte)Mathf.RoundToInt(Mathf.Clamp01(color.b) * 255f),
				(byte)Mathf.RoundToInt(Mathf.Clamp01(color.a) * 255f));
		}

		/// <summary>Base liquid color for each essence.</summary>
		public static Color LiquidColor(ItemColor color)
		{
			switch (color)
			{
				case ItemColor.Red:
					return new Color(0.86f, 0.22f, 0.2f, 1f);
				case ItemColor.Blue:
					return new Color(0.2f, 0.47f, 0.87f, 1f);
				case ItemColor.Green:
					return new Color(0.26f, 0.71f, 0.32f, 1f);
				case ItemColor.Yellow:
					return new Color(0.93f, 0.8f, 0.2f, 1f);
				case ItemColor.Purple:
					return new Color(0.62f, 0.32f, 0.78f, 1f);
				default:
					return Color.white;
			}
		}
	}
}
