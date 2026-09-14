using System.Collections.Generic;
using NUnit.Framework;
using PotionCraft.Core;

namespace PotionCraft.Tests.Editor
{
	[TestFixture]
	public class OrderBookTests
	{
		private static MatchGroup MakeMatch(ItemColor color, int cellCount, MatchShape shape = MatchShape.Line3)
		{
			var cells = new List<(int X, int Y)>();
			for (int i = 0; i < cellCount; i++)
				cells.Add((i, 0));

			return new MatchGroup(color, cells, shape);
		}

		[Test]
		public void RegisterMatches_AddsProgressOnlyToMatchingColorOrders()
		{
			var redOrder = new Order(ItemColor.Red, 10);
			var blueOrder = new Order(ItemColor.Blue, 10);
			var book = new OrderBook(new List<Order> { redOrder, blueOrder });

			var matches = new List<MatchGroup> { MakeMatch(ItemColor.Red, 3) };

			book.RegisterMatches(matches);

			Assert.AreEqual(3, redOrder.CollectedCount);
			Assert.AreEqual(0, blueOrder.CollectedCount);
		}

		[Test]
		public void RegisterMatches_ReturnsRewardWhenOrderCompletes()
		{
			var order = new Order(ItemColor.Green, 5);
			var book = new OrderBook(new List<Order> { order });

			var matches = new List<MatchGroup> { MakeMatch(ItemColor.Green, 5) };

			int bonus = book.RegisterMatches(matches);

			Assert.AreEqual(OrderBook.RewardPerOrder, bonus);
			Assert.AreEqual(1, book.CompletedOrderCount);
			Assert.IsTrue(order.IsComplete);
		}

		[Test]
		public void RegisterMatches_DoesNotDoubleRewardAnAlreadyCompleteOrder()
		{
			var order = new Order(ItemColor.Purple, 3);
			var book = new OrderBook(new List<Order> { order });

			book.RegisterMatches(new List<MatchGroup> { MakeMatch(ItemColor.Purple, 3) });
			int secondBonus = book.RegisterMatches(new List<MatchGroup> { MakeMatch(ItemColor.Purple, 3) });

			Assert.AreEqual(0, secondBonus);
			Assert.AreEqual(1, book.CompletedOrderCount);
		}

		[Test]
		public void RegisterMatches_IgnoresColorsWithNoMatchingOrder()
		{
			var order = new Order(ItemColor.Yellow, 5);
			var book = new OrderBook(new List<Order> { order });

			int bonus = book.RegisterMatches(new List<MatchGroup> { MakeMatch(ItemColor.Red, 4) });

			Assert.AreEqual(0, bonus);
			Assert.AreEqual(0, order.CollectedCount);
		}

		[Test]
		public void AllOrdersComplete_TrueOnlyWhenEveryOrderIsComplete()
		{
			var orderA = new Order(ItemColor.Red, 3);
			var orderB = new Order(ItemColor.Blue, 3);
			var book = new OrderBook(new List<Order> { orderA, orderB });

			Assert.IsFalse(book.AllOrdersComplete);

			book.RegisterMatches(new List<MatchGroup> { MakeMatch(ItemColor.Red, 3) });
			Assert.IsFalse(book.AllOrdersComplete);

			book.RegisterMatches(new List<MatchGroup> { MakeMatch(ItemColor.Blue, 3) });
			Assert.IsTrue(book.AllOrdersComplete);
		}

		[Test]
		public void AllOrdersComplete_FalseWhenNoOrders()
		{
			var book = new OrderBook(new List<Order>());

			Assert.IsFalse(book.AllOrdersComplete);
		}
	}
}
