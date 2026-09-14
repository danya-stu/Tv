using System;
using NUnit.Framework;
using PotionCraft.Core;

namespace PotionCraft.Tests.Editor
{
	[TestFixture]
	public class OrderTests
	{
		[Test]
		public void Constructor_ThrowsForNoneColor()
		{
			Assert.Throws<ArgumentException>(() => new Order(ItemColor.None, 10));
		}

		[TestCase(0)]
		[TestCase(-5)]
		public void Constructor_ThrowsForNonPositiveRequiredCount(int requiredCount)
		{
			Assert.Throws<ArgumentOutOfRangeException>(() => new Order(ItemColor.Red, requiredCount));
		}

		[Test]
		public void Constructor_InitializesIncompleteWithZeroProgress()
		{
			var order = new Order(ItemColor.Red, 10);

			Assert.AreEqual(0, order.CollectedCount);
			Assert.IsFalse(order.IsComplete);
		}

		[Test]
		public void Register_AccumulatesProgress()
		{
			var order = new Order(ItemColor.Blue, 10);

			order.Register(4);
			order.Register(3);

			Assert.AreEqual(7, order.CollectedCount);
			Assert.IsFalse(order.IsComplete);
		}

		[Test]
		public void Register_ClampsAtRequiredCount()
		{
			var order = new Order(ItemColor.Green, 5);

			order.Register(9);

			Assert.AreEqual(5, order.CollectedCount);
			Assert.IsTrue(order.IsComplete);
		}

		[Test]
		public void Register_IgnoresZeroOrNegativeCounts()
		{
			var order = new Order(ItemColor.Yellow, 5);

			order.Register(0);
			order.Register(-3);

			Assert.AreEqual(0, order.CollectedCount);
		}

		[Test]
		public void Register_IgnoredOnceComplete()
		{
			var order = new Order(ItemColor.Purple, 5);
			order.Register(5);

			order.Register(3);

			Assert.AreEqual(5, order.CollectedCount);
		}
	}
}
