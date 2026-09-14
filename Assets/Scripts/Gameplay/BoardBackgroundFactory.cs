using UnityEngine;

namespace PotionCraft.Gameplay
{
	/// <summary>
	/// Procedurally bakes a full-board backdrop texture: one softly rounded
	/// "slot" per grid cell in an alternating muted purple/plum checker,
	/// evoking a wooden alchemist's shelf rather than gems floating on an
	/// empty background. Baked once per GridView at runtime; no imported
	/// art assets or packages required.
	/// </summary>
	public static class BoardBackgroundFactory
	{
		private static readonly Color32 SlotColorA = new Color32(46, 36, 58, 255);
		private static readonly Color32 SlotColorB = new Color32(38, 30, 49, 255);
		private static readonly Color32 GutterColor = new Color32(18, 14, 24, 255);

		/// <summary>
		/// Builds a <paramref name="width"/> x <paramref name="height"/> cell
		/// backdrop sprite. Pixels-per-unit matches <paramref name="cellPixelSize"/>
		/// so the sprite spans exactly one world unit per cell when its
		/// transform scale equals GridView's cellSize.
		/// </summary>
		public static Sprite GetBoardBackground(int width, int height, int cellPixelSize = 64)
		{
			int texWidth = Mathf.Max(1, width) * cellPixelSize;
			int texHeight = Mathf.Max(1, height) * cellPixelSize;

			var texture = new Texture2D(texWidth, texHeight, TextureFormat.RGBA32, false)
			{
				filterMode = FilterMode.Bilinear,
				wrapMode = TextureWrapMode.Clamp,
			};

			var pixels = new Color32[texWidth * texHeight];
			for (int i = 0; i < pixels.Length; i++)
				pixels[i] = GutterColor;

			for (int cellY = 0; cellY < height; cellY++)
			{
				for (int cellX = 0; cellX < width; cellX++)
				{
					bool checker = (cellX + cellY) % 2 == 0;
					Color32 slotColor = checker ? SlotColorA : SlotColorB;
					DrawSlot(pixels, texWidth, cellX * cellPixelSize, cellY * cellPixelSize, cellPixelSize, slotColor);
				}
			}

			texture.SetPixels32(pixels);
			texture.Apply();

			return Sprite.Create(texture, new Rect(0f, 0f, texWidth, texHeight), new Vector2(0.5f, 0.5f), cellPixelSize);
		}

		private static void DrawSlot(Color32[] pixels, int texWidth, int originX, int originY, int size, Color32 baseColor)
		{
			float half = size / 2f;
			float inset = size * 0.06f;
			float radius = size * 0.16f;
			float innerHalf = half - inset - radius;

			for (int ly = 0; ly < size; ly++)
			{
				float v = (ly + 0.5f) - half;
				for (int lx = 0; lx < size; lx++)
				{
					float u = (lx + 0.5f) - half;

					float dx = Mathf.Max(Mathf.Abs(u) - innerHalf, 0f);
					float dy = Mathf.Max(Mathf.Abs(v) - innerHalf, 0f);
					float dist = Mathf.Sqrt(dx * dx + dy * dy) - radius;

					float coverage = Mathf.Clamp01(0.5f - dist);
					if (coverage <= 0f)
						continue;

					// Slight inner-shadow: darken pixels near the slot's own
					// edge so each cell reads as a subtly recessed socket.
					float shadow = Mathf.Clamp01(-dist / (size * 0.12f));
					float shade = Mathf.Lerp(1f, 0.75f, shadow);

					int px = originX + lx;
					int py = originY + ly;
					int idx = py * texWidth + px;

					byte existingAlpha = pixels[idx].a;
					byte r = (byte)Mathf.RoundToInt(baseColor.r * shade);
					byte g = (byte)Mathf.RoundToInt(baseColor.g * shade);
					byte b = (byte)Mathf.RoundToInt(baseColor.b * shade);

					if (coverage >= 1f)
					{
						pixels[idx] = new Color32(r, g, b, 255);
					}
					else
					{
						// Blend toward the gutter color at the slot's rounded
						// corners instead of leaving a hard-edged square.
						byte br = (byte)Mathf.RoundToInt(Mathf.Lerp(GutterColor.r, r, coverage));
						byte bg = (byte)Mathf.RoundToInt(Mathf.Lerp(GutterColor.g, g, coverage));
						byte bb = (byte)Mathf.RoundToInt(Mathf.Lerp(GutterColor.b, b, coverage));
						pixels[idx] = new Color32(br, bg, bb, 255);
					}

					_ = existingAlpha;
				}
			}
		}
	}
}
