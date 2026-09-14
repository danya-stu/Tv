using System;

namespace PotionCraft.Core
{
	/// <summary>
	/// A single "Книга Заказов" (Order Book) request: collect a target
	/// number of items of one color via matches. Progress accumulates from
	/// matched cells only (not catalyst-expanded clears), so orders reward
	/// deliberately matching the requested color. Pure C# and mutable only
	/// through its own progress counter, so it is trivially unit testable
	/// without any Unity dependency.
	/// </summary>
	public sealed class Order
	{
		public ItemColor Color { get; }
		public int RequiredCount { get; }
		public int CollectedCount { get; private set; }

		public bool IsComplete => CollectedCount >= RequiredCount;

		public Order(ItemColor color, int requiredCount)
		{
			if (color == ItemColor.None)
				throw new ArgumentException("An order cannot request ItemColor.None.", nameof(color));
			if (requiredCount <= 0)
				throw new ArgumentOutOfRangeException(nameof(requiredCount), requiredCount, "requiredCount must be positive.");

			Color = color;
			RequiredCount = requiredCount;
			CollectedCount = 0;
		}

		/// <summary>
		/// Adds matched cells of this order's color toward its progress.
		/// Ignored once the order is already complete, and clamped so
		/// CollectedCount never exceeds RequiredCount.
		/// </summary>
		public void Register(int count)
		{
			if (count <= 0 || IsComplete)
				return;

			CollectedCount = Math.Min(RequiredCount, CollectedCount + count);
		}
	}
}
