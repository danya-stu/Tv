using System;
using System.Collections.Generic;
using UnityEngine;

namespace PotionCraft.Core
{
	/// <summary>
	/// Types of special bonuses created by matching 4 or more tiles or forming shapes.
	/// </summary>
	public enum SpecialType
	{
		None = 0,
		HorizontalLine = 1,
		VerticalLine = 2,
		BombArea = 3,
		ColorBomb = 4
	}

	/// <summary>
	/// Value type describing a special tile to be spawned as a result of a match combination.
	/// </summary>
	public readonly struct SpecialCreationInfo : IEquatable<SpecialCreationInfo>
	{
		public Vector2Int Position { get; }
		public Vector2Int Tile => Position;
		public SpecialType Type { get; }
		public ItemColor Color { get; }

		public SpecialCreationInfo(Vector2Int position, SpecialType type, ItemColor color)
		{
			Position = position;
			Type = type;
			Color = color;
		}

		public bool Equals(SpecialCreationInfo other)
		{
			return Position.Equals(other.Position) && Type == other.Type && Color == other.Color;
		}

		public override bool Equals(object obj)
		{
			return obj is SpecialCreationInfo other && Equals(other);
		}

		public override int GetHashCode()
		{
			unchecked
			{
				int hashCode = Position.GetHashCode();
				hashCode = (hashCode * 397) ^ (int)Type;
				hashCode = (hashCode * 397) ^ (int)Color;
				return hashCode;
			}
		}

		public static bool operator ==(SpecialCreationInfo left, SpecialCreationInfo right) => left.Equals(right);
		public static bool operator !=(SpecialCreationInfo left, SpecialCreationInfo right) => !left.Equals(right);

		public override string ToString() => $"SpecialCreationInfo({Type} at {Position}, Color={Color})";
	}

	/// <summary>
	/// Reusable output container for match detection results.
	/// Collects destroyed tile coordinates, spawned specials, and total gathered essences per color.
	/// Call Clear() to reset without triggering garbage collection allocations.
	/// </summary>
	public sealed class MatchReport
	{
		public List<Vector2Int> MatchedTiles { get; }
		public List<SpecialCreationInfo> CreatedSpecials { get; }
		public Dictionary<ItemColor, int> GatheredEssences { get; }

		public MatchReport(int initialCapacity = 64)
		{
			MatchedTiles = new List<Vector2Int>(initialCapacity);
			CreatedSpecials = new List<SpecialCreationInfo>(8);
			GatheredEssences = new Dictionary<ItemColor, int>(8);
		}

		/// <summary>
		/// Resets all internal collections so the report can be reused across turns with zero GC allocations.
		/// </summary>
		public void Clear()
		{
			MatchedTiles.Clear();
			CreatedSpecials.Clear();
			GatheredEssences.Clear();
		}
	}
}
