using System;
using System.Collections.Generic;
using NUnit.Framework;
using PotionCraft.Core;
using PotionCraft.Gameplay;
using UnityEngine;

namespace PotionCraft.Tests.Editor
{
	[TestFixture]
	public sealed class BoardStateTests
	{
		private static ItemColor PatternColor(int x, int y)
		{
			return (ItemColor)(1 + ((x + (2 * y)) % 5));
		}

		private static GridModel BuildMatchFreeGrid(int width, int height)
		{
			var grid = new GridModel(width, height);
			for (int y = 0; y < height; y++)
			{
				for (int x = 0; x < width; x++)
				{
					grid.SetItem(x, y, Item.Create(PatternColor(x, y)));
				}
			}
			return grid;
		}

		private static BoardController CreateTestController()
		{
			try
			{
				var go = new GameObject("BoardControllerTest");
				return go.AddComponent<BoardController>();
			}
			catch
			{
				return (BoardController)System.Runtime.Serialization.FormatterServices.GetUninitializedObject(typeof(BoardController));
			}
		}

		private static void DestroyTestController(BoardController controller)
		{
			if (controller == null)
				return;

			try
			{
				if (controller.gameObject != null)
				{
					UnityEngine.Object.DestroyImmediate(controller.gameObject);
				}
			}
			catch
			{
				// In standalone runner without Unity native runtime
			}
		}

		[Test]
		public void Test_InvalidMove_RevertsGridState()
		{
			const int size = 6;
			GridModel gridModel = BuildMatchFreeGrid(size, size);

			var cellA = new Vector2Int(1, 1);
			var cellB = new Vector2Int(2, 1);
			Item origA = gridModel.GetItem(cellA.x, cellA.y);
			Item origB = gridModel.GetItem(cellB.x, cellB.y);

			var controller = CreateTestController();
			try
			{
				var stateMachine = new BoardStateMachine(BoardState.Idle);
				controller.Initialize(gridModel, stateMachine, initialMoves: 20);

				bool revertedEventFired = false;
				controller.OnSwapReverted += (from, to) =>
				{
					if (from == cellA && to == cellB)
					{
						revertedEventFired = true;
					}
				};

				// Swapping two cells on a match-free board produces zero matches
				controller.OnSwipe(cellA, cellB);

				// State machine must return to Idle
				Assert.AreEqual(BoardState.Idle, controller.StateMachine.CurrentState);
				Assert.IsTrue(revertedEventFired, "OnSwapReverted event should have been fired.");

				// Board items must be reverted back to their exact original values
				Assert.AreEqual(origA, gridModel.GetItem(cellA.x, cellA.y));
				Assert.AreEqual(origB, gridModel.GetItem(cellB.x, cellB.y));
				Assert.AreEqual(20, controller.RemainingMoves, "Remaining moves must not be consumed for an invalid swap.");
			}
			finally
			{
				DestroyTestController(controller);
			}
		}

		[Test]
		public void Test_ValidMove_ModifiesGridState_And_ProducesCascades()
		{
			const int size = 6;
			GridModel gridModel = BuildMatchFreeGrid(size, size);

			// Setup match:
			// y = 1: (0,1)=Red, (1,1)=Red, (2,1)=Blue
			// y = 2: (2,2)=Red
			// Swapping (2,2) and (2,1) creates a 3-match of Red at row y = 1
			gridModel.SetItem(0, 1, Item.Create(ItemColor.Red));
			gridModel.SetItem(1, 1, Item.Create(ItemColor.Red));
			gridModel.SetItem(2, 1, Item.Create(ItemColor.Blue));
			gridModel.SetItem(2, 2, Item.Create(ItemColor.Red));

			var controller = CreateTestController();
			try
			{
				var stateMachine = new BoardStateMachine(BoardState.Idle);
				controller.Initialize(gridModel, stateMachine, initialMoves: 15);

				IReadOnlyList<CascadeStep> receivedCascades = null;
				controller.OnCascadesDispatched += steps =>
				{
					receivedCascades = steps;
				};

				controller.OnSwipe(new Vector2Int(2, 2), new Vector2Int(2, 1));

				// Valid move transitions to ResolvingCascades
				Assert.AreEqual(BoardState.ResolvingCascades, controller.StateMachine.CurrentState);

				// Moves count should decrement
				Assert.AreEqual(14, controller.RemainingMoves);

				// Cascades list should be dispatched and contain at least one cascade step
				Assert.IsNotNull(receivedCascades);
				Assert.IsTrue(receivedCascades.Count > 0);

				CascadeStep firstStep = receivedCascades[0];
				Assert.IsTrue(firstStep.DestroyedTiles.Contains(new Vector2Int(0, 1)));
				Assert.IsTrue(firstStep.DestroyedTiles.Contains(new Vector2Int(1, 1)));
				Assert.IsTrue(firstStep.DestroyedTiles.Contains(new Vector2Int(2, 1)));
			}
			finally
			{
				DestroyTestController(controller);
			}
		}

		[Test]
		public void Test_FiveInARow_WithIntersection_Prioritizes_ColorBomb()
		{
			const int size = 7;
			Item[,] grid = new Item[size, size];
			for (int y = 0; y < size; y++)
			{
				for (int x = 0; x < size; x++)
				{
					grid[x, y] = Item.Empty;
				}
			}

			// Horizontal line of 5 Purple items: (1,3) to (5,3)
			grid[1, 3] = Item.Create(ItemColor.Purple);
			grid[2, 3] = Item.Create(ItemColor.Purple);
			grid[3, 3] = Item.Create(ItemColor.Purple);
			grid[4, 3] = Item.Create(ItemColor.Purple);
			grid[5, 3] = Item.Create(ItemColor.Purple);

			// Intersecting vertical line of 3 Purple items: (3,1), (3,2), (3,3)
			grid[3, 1] = Item.Create(ItemColor.Purple);
			grid[3, 2] = Item.Create(ItemColor.Purple);

			var report = new MatchReport();
			bool hasMatches = MatchEngine.FindMatches(grid, size, size, report);

			Assert.IsTrue(hasMatches);
			Assert.AreEqual(1, report.CreatedSpecials.Count);

			// Supervisor Decision: Length >= 5 ALWAYS creates ColorBomb even in an intersecting cluster
			SpecialCreationInfo special = report.CreatedSpecials[0];
			Assert.AreEqual(SpecialType.ColorBomb, special.Type, "A match containing a line of 5 must prioritize ColorBomb over BombArea.");
			Assert.AreEqual(ItemColor.Purple, special.Color);
			Assert.AreEqual(new Vector2Int(3, 3), special.Position);
		}

		[Test]
		public void Test_SwipeInputReader_ClampsOrthogonally()
		{
			// X dominant -> Right
			Assert.AreEqual(Vector2Int.right, SwipeInputReader.ClampToOrthogonalDirection(new Vector2(100f, 20f)));
			// X dominant negative -> Left
			Assert.AreEqual(Vector2Int.left, SwipeInputReader.ClampToOrthogonalDirection(new Vector2(-100f, 30f)));
			// Y dominant positive -> Up
			Assert.AreEqual(Vector2Int.up, SwipeInputReader.ClampToOrthogonalDirection(new Vector2(10f, 80f)));
			// Y dominant negative -> Down
			Assert.AreEqual(Vector2Int.down, SwipeInputReader.ClampToOrthogonalDirection(new Vector2(-15f, -90f)));
		}

		[Test]
		public void Test_StateMachine_Disallows_InvalidTransitions()
		{
			var sm = new BoardStateMachine(BoardState.Idle);

			// From Idle cannot jump directly to Validating
			Assert.IsFalse(sm.CanTransitionTo(BoardState.Validating));
			Assert.Throws<InvalidOperationException>(() => sm.TransitionTo(BoardState.Validating));

			// Valid transition: Idle -> Swapping
			Assert.IsTrue(sm.TryTransitionTo(BoardState.Swapping));
			Assert.AreEqual(BoardState.Swapping, sm.CurrentState);

			// From Swapping cannot jump to ResolvingCascades
			Assert.IsFalse(sm.CanTransitionTo(BoardState.ResolvingCascades));
		}
	}
}
