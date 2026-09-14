using System;

namespace PotionCraft.Core
{
	/// <summary>
	/// The five collectible essence colors used across the match-3 grid.
	/// None represents an empty cell and must never appear on a populated board.
	/// </summary>
	public enum ItemColor
	{
		None = 0,
		Red = 1,
		Blue = 2,
		Green = 3,
		Yellow = 4,
		Purple = 5
	}

	/// <summary>
	/// Alchemical glyph rendered on top of each item's color, independent of
	/// hue, so color-blind players (deuteranopia/protanopia, up to ~8% of the
	/// male audience) can distinguish every item by shape and symbol alone.
	/// </summary>
	public enum RuneType
	{
		None = 0,
		Flame = 1,
		Droplet = 2,
		Crystal = 3,
		Star = 4,
		Eye = 5
	}

	/// <summary>
	/// A single grid cell's payload: a color paired with its accessibility
	/// rune. Immutable and allocation-free (struct) so a full grid of items
	/// never triggers per-cell heap allocations.
	/// </summary>
	public readonly struct Item : IEquatable<Item>
	{
		public ItemColor Color { get; }
		public RuneType Rune { get; }

		public Item(ItemColor color, RuneType rune)
		{
			Color = color;
			Rune = rune;
		}

		/// <summary>
		/// Creates an item for the given color, automatically resolving its
		/// paired rune so callers can never construct a color/rune mismatch.
		/// </summary>
		public static Item Create(ItemColor color)
		{
			return new Item(color, RuneForColor(color));
		}

		/// <summary>
		/// The single source of truth mapping each essence color to its glyph.
		/// Any new color added to ItemColor must be handled here or this will
		/// throw, preventing a silently un-accessible item from shipping.
		/// </summary>
		public static RuneType RuneForColor(ItemColor color)
		{
			switch (color)
			{
				case ItemColor.None:
					return RuneType.None;
				case ItemColor.Red:
					return RuneType.Flame;
				case ItemColor.Blue:
					return RuneType.Droplet;
				case ItemColor.Green:
					return RuneType.Crystal;
				case ItemColor.Yellow:
					return RuneType.Star;
				case ItemColor.Purple:
					return RuneType.Eye;
				default:
					throw new ArgumentOutOfRangeException(nameof(color), color, "Unknown ItemColor; add a RuneType mapping before shipping a new color.");
			}
		}

		public static readonly Item Empty = new Item(ItemColor.None, RuneType.None);

		public bool Equals(Item other)
		{
			return Color == other.Color && Rune == other.Rune;
		}

		public override bool Equals(object obj)
		{
			return obj is Item other && Equals(other);
		}

		public override int GetHashCode()
		{
			return ((int)Color * 31) + (int)Rune;
		}

		public static bool operator ==(Item left, Item right) => left.Equals(right);
		public static bool operator !=(Item left, Item right) => !left.Equals(right);
	}
}
