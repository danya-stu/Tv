using System.Collections.Generic;

namespace PotionCraft.Core
{
	/// <summary>
	/// Tracks a fixed set of active Orders for one level attempt (the
	/// "Книга Заказов" killer feature) and awards a flat score bonus the
	/// moment each order is completed. Progress is driven by the matches
	/// found in each resolved cascade step, so GridModel/MatchFinder never
	/// need to know orders exist.
	/// </summary>
	public sealed class OrderBook
	{
		public const int RewardPerOrder = 150;

		private readonly List<Order> _orders;

		public IReadOnlyList<Order> Orders => _orders;
		public int CompletedOrderCount { get; private set; }

		public bool AllOrdersComplete
		{
			get
			{
				if (_orders.Count == 0)
					return false;

				for (int i = 0; i < _orders.Count; i++)
				{
					if (!_orders[i].IsComplete)
						return false;
				}

				return true;
			}
		}

		public OrderBook(IReadOnlyList<Order> orders)
		{
			_orders = new List<Order>(orders);
		}

		/// <summary>
		/// Registers every matched cell from one resolved cascade step (or the
		/// whole cascade's matches) toward any active order sharing that
		/// match's color. Returns the total score bonus earned from orders
		/// that completed as a result of this call (0 if none did). Safe to
		/// call multiple times; already-complete orders are skipped and never
		/// award a second bonus.
		/// </summary>
		public int RegisterMatches(IReadOnlyList<MatchGroup> matches)
		{
			int bonus = 0;

			foreach (MatchGroup match in matches)
			{
				for (int i = 0; i < _orders.Count; i++)
				{
					Order order = _orders[i];
					if (order.Color != match.Color || order.IsComplete)
						continue;

					order.Register(match.Cells.Count);
					if (order.IsComplete)
					{
						bonus += RewardPerOrder;
						CompletedOrderCount++;
					}
				}
			}

			return bonus;
		}
	}
}
