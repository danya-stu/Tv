using System;
using System.Collections.Generic;
using UnityEngine;

namespace PotionCraft.Core
{
	/// <summary>
	/// A single tile displacement produced by gravity. Pure data for the animator:
	/// the logic layer never touches transforms, it only reports where a tile went.
	/// </summary>
	public readonly struct TileMoveAction : IEquatable<TileMoveAction>
	{
		public Vector2Int From { get; }
		public Vector2Int To { get; }
		public Item MovedItem { get; }

		public TileMoveAction(Vector2Int from, Vector2Int to, Item movedItem)
		{
			From = from;
			To = to;
			MovedItem = movedItem;
		}

		/// <summary>Number of rows the tile falls. Always positive for a downward collapse.</summary>
		public int Distance => Math.Abs(From.y - To.y);

		public bool Equals(TileMoveAction other)
		{
			return From.Equals(other.From) && To.Equals(other.To) && MovedItem.Equals(other.MovedItem);
		}

		public override bool Equals(object obj)
		{
			return obj is TileMoveAction other && Equals(other);
		}

		public override int GetHashCode()
		{
			unchecked
			{
				int hashCode = From.GetHashCode();
				hashCode = (hashCode * 397) ^ To.GetHashCode();
				hashCode = (hashCode * 397) ^ MovedItem.GetHashCode();
				return hashCode;
			}
		}

		public static bool operator ==(TileMoveAction left, TileMoveAction right) => left.Equals(right);
		public static bool operator !=(TileMoveAction left, TileMoveAction right) => !left.Equals(right);

		public override string ToString() => $"TileMoveAction({From} -> {To}, {MovedItem.Color}, Distance={Distance})";
	}

	/// <summary>
	/// A newly spawned tile falling into the board from above the visible grid.
	/// SpawnRow is a virtual row (>= height) so the animator can stagger drops
	/// without the logic layer knowing anything about time or easing.
	/// </summary>
	public readonly struct TileSpawnAction : IEquatable<TileSpawnAction>
	{
		public int Column { get; }
		public int SpawnRow { get; }
		public Vector2Int TargetPosition { get; }
		public Item SpawnedItem { get; }

		public TileSpawnAction(int column, int spawnRow, Vector2Int targetPosition, Item spawnedItem)
		{
			Column = column;
			SpawnRow = spawnRow;
			TargetPosition = targetPosition;
			SpawnedItem = spawnedItem;
		}

		/// <summary>Number of rows the new tile travels from its virtual spawn row.</summary>
		public int DropDistance => Math.Abs(SpawnRow - TargetPosition.y);

		public bool Equals(TileSpawnAction other)
		{
			return Column == other.Column
				&& SpawnRow == other.SpawnRow
				&& TargetPosition.Equals(other.TargetPosition)
				&& SpawnedItem.Equals(other.SpawnedItem);
		}

		public override bool Equals(object obj)
		{
			return obj is TileSpawnAction other && Equals(other);
		}

		public override int GetHashCode()
		{
			unchecked
			{
				int hashCode = Column;
				hashCode = (hashCode * 397) ^ SpawnRow;
				hashCode = (hashCode * 397) ^ TargetPosition.GetHashCode();
				hashCode = (hashCode * 397) ^ SpawnedItem.GetHashCode();
				return hashCode;
			}
		}

		public static bool operator ==(TileSpawnAction left, TileSpawnAction right) => left.Equals(right);
		public static bool operator !=(TileSpawnAction left, TileSpawnAction right) => !left.Equals(right);

		public override string ToString() => $"TileSpawnAction(col={Column}, {SpawnRow} -> {TargetPosition}, {SpawnedItem.Color}, Drop={DropDistance})";
	}

	/// <summary>
	/// One iteration of a turn: what was destroyed, what specials appeared,
	/// which tiles were recolored by a Reaction Catalyst, how existing tiles fell,
	/// and what was spawned to refill the board.
	/// Instances are pooled and reused by BoardGravityEngine, so callers must
	/// consume a step before the next ResolveFullTurnCascades call.
	/// </summary>
	public sealed class CascadeStep
	{
		public List<Vector2Int> DestroyedTiles { get; }
		public List<SpecialCreationInfo> CreatedSpecials { get; }
		public List<TileMoveAction> Moves { get; }
		public List<TileSpawnAction> Spawns { get; }
		public Dictionary<ItemColor, int> StepEssences { get; }
		public int CascadeIndex { get; set; }

		/// <summary>Cells recolored by a ColorBomb combination or a Reaction Catalyst.</summary>
		public List<Vector2Int> TransmutedTiles { get; }

		/// <summary>Color consumed by the transmutation, or None when nothing was recolored.</summary>
		public ItemColor TransmuteColor { get; set; }

		public CascadeStep(int initialCapacity = 32)
		{
			DestroyedTiles = new List<Vector2Int>(initialCapacity);
			CreatedSpecials = new List<SpecialCreationInfo>(8);
			Moves = new List<TileMoveAction>(initialCapacity);
			Spawns = new List<TileSpawnAction>(initialCapacity);
			StepEssences = new Dictionary<ItemColor, int>(8);
			TransmutedTiles = new List<Vector2Int>(initialCapacity);
			TransmuteColor = ItemColor.None;
			CascadeIndex = 0;
		}

		/// <summary>Resets the step for reuse without releasing any buffer capacity.</summary>
		public void Clear()
		{
			DestroyedTiles.Clear();
			CreatedSpecials.Clear();
			Moves.Clear();
			Spawns.Clear();
			StepEssences.Clear();
			TransmutedTiles.Clear();
			TransmuteColor = ItemColor.None;
			CascadeIndex = 0;
		}

		/// <summary>Accumulates gathered essence for a color without allocating.</summary>
		public void AddEssence(ItemColor color, int amount)
		{
			if (color == ItemColor.None || amount == 0)
			{
				return;
			}

			if (StepEssences.TryGetValue(color, out int current))
			{
				StepEssences[color] = current + amount;
			}
			else
			{
				StepEssences[color] = amount;
			}
		}

		public int GetEssence(ItemColor color)
		{
			return StepEssences.TryGetValue(color, out int value) ? value : 0;
		}
	}

	/// <summary>
	/// Strategy for choosing the color of a freshly spawned tile. Injecting this
	/// keeps the gravity engine deterministic and testable, and lets level design
	/// bias drops later without touching the engine.
	/// </summary>
	public interface ISpawnWeightProvider
	{
		ItemColor GetNextColor(int x, int y, int totalColors);
	}

	/// <summary>
	/// Uniform distribution over the first totalColors entries of ItemColor.
	/// Uses System.Random (not UnityEngine.Random) so the Core assembly stays
	/// free of engine state and tests can pin a seed for reproducible boards.
	/// </summary>
	public sealed class DefaultSpawnWeightProvider : ISpawnWeightProvider
	{
		/// <summary>Number of playable colors in ItemColor, excluding None.</summary>
		public const int MaxColors = 5;

		private readonly System.Random _random;

		public DefaultSpawnWeightProvider()
			: this(Environment.TickCount)
		{
		}

		public DefaultSpawnWeightProvider(int seed)
		{
			_random = new System.Random(seed);
		}

		public ItemColor GetNextColor(int x, int y, int totalColors)
		{
			int clamped = totalColors < 1 ? 1 : (totalColors > MaxColors ? MaxColors : totalColors);
			return (ItemColor)(1 + _random.Next(clamped));
		}
	}
}
