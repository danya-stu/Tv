using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using PotionCraft.Core;

namespace PotionCraft.Gameplay
{
	/// <summary>
	/// Runtime-built Canvas UI for score, moves, the Order Book, and the
	/// win/lose/continue-with-ad flow. Replaces the earlier OnGUI HUD with a
	/// real uGUI hierarchy (proper anchoring/scaling and a clickable Button
	/// instead of per-frame IMGUI draws) while still requiring zero prefabs
	/// or imported art assets: panels, swatches, and the button all reuse
	/// UISpriteFactory's procedural rounded-rect sprite. Swap in
	/// TextMeshPro/real art later without touching GridView's call sites.
	/// </summary>
	public sealed class GameHud : MonoBehaviour
	{
		public event Action OnWatchAdClicked;

		private Text _scoreText;
		private Text _movesText;
		private GameObject _ordersContainer;
		private readonly List<(Image swatch, Text label)> _orderRows = new List<(Image, Text)>();
		private Text _endStateText;
		private Text _adWatchingText;
		private Button _adButton;
		private Text _adButtonLabel;

		/// <summary>
		/// Builds the full HUD hierarchy under a Screen Space Overlay Canvas
		/// parented to <paramref name="parent"/>. Parenting under a
		/// world-space transform is safe: Overlay canvases ignore their
		/// transform's position/rotation/scale entirely.
		/// </summary>
		public static GameHud Create(Transform parent)
		{
			var canvasGo = new GameObject("GameHud", typeof(RectTransform));
			canvasGo.transform.SetParent(parent, false);

			var canvas = canvasGo.AddComponent<Canvas>();
			canvas.renderMode = RenderMode.ScreenSpaceOverlay;
			canvas.sortingOrder = 10;

			var scaler = canvasGo.AddComponent<CanvasScaler>();
			scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
			scaler.referenceResolution = new Vector2(1080f, 1920f);
			scaler.matchWidthOrHeight = 0.5f;

			canvasGo.AddComponent<GraphicRaycaster>();
			EnsureEventSystem();

			var hud = canvasGo.AddComponent<GameHud>();
			hud.Build(canvasGo.transform);
			return hud;
		}

		private static void EnsureEventSystem()
		{
			if (FindFirstObjectByType<EventSystem>(FindObjectsInactive.Include) != null)
				return;

			new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
		}

		private void Build(Transform canvasTransform)
		{
			Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

			// --- Top-left: score, moves, order book ---
			var topPanel = CreatePanel("TopPanel", canvasTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(24f, -24f));

			var topLayout = topPanel.AddComponent<VerticalLayoutGroup>();
			topLayout.padding = new RectOffset(24, 24, 20, 20);
			topLayout.spacing = 10f;
			topLayout.childAlignment = TextAnchor.UpperLeft;
			topLayout.childControlWidth = true;
			topLayout.childControlHeight = true;
			topLayout.childForceExpandWidth = false;
			topLayout.childForceExpandHeight = false;

			var topFitter = topPanel.AddComponent<ContentSizeFitter>();
			topFitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
			topFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

			_scoreText = CreateText("ScoreText", topPanel.transform, font, 40, Color.white, FontStyle.Bold);
			_movesText = CreateText("MovesText", topPanel.transform, font, 40, Color.white, FontStyle.Bold);

			var ordersTitle = CreateText("OrdersTitle", topPanel.transform, font, 32, new Color(1f, 0.85f, 0.4f), FontStyle.Bold);
			ordersTitle.text = "Книга заказов:";

			_ordersContainer = new GameObject("OrdersContainer", typeof(RectTransform));
			_ordersContainer.transform.SetParent(topPanel.transform, false);

			var ordersLayout = _ordersContainer.AddComponent<VerticalLayoutGroup>();
			ordersLayout.spacing = 6f;
			ordersLayout.childControlWidth = true;
			ordersLayout.childControlHeight = true;
			ordersLayout.childForceExpandWidth = false;
			ordersLayout.childForceExpandHeight = false;

			var ordersFitter = _ordersContainer.AddComponent<ContentSizeFitter>();
			ordersFitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
			ordersFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

			// --- Center: win/lose banner + watch-ad-to-continue ---
			var centerPanel = new GameObject("CenterPanel", typeof(RectTransform));
			centerPanel.transform.SetParent(canvasTransform, false);

			var centerRect = centerPanel.GetComponent<RectTransform>();
			centerRect.anchorMin = new Vector2(0.5f, 0.45f);
			centerRect.anchorMax = new Vector2(0.5f, 0.45f);
			centerRect.pivot = new Vector2(0.5f, 0.5f);
			centerRect.anchoredPosition = Vector2.zero;

			var centerLayout = centerPanel.AddComponent<VerticalLayoutGroup>();
			centerLayout.spacing = 20f;
			centerLayout.childAlignment = TextAnchor.MiddleCenter;
			centerLayout.childControlWidth = true;
			centerLayout.childControlHeight = true;
			centerLayout.childForceExpandWidth = false;
			centerLayout.childForceExpandHeight = false;

			var centerFitter = centerPanel.AddComponent<ContentSizeFitter>();
			centerFitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
			centerFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

			_endStateText = CreateText("EndStateText", centerPanel.transform, font, 64, Color.white, FontStyle.Bold, TextAnchor.MiddleCenter);
			_endStateText.gameObject.SetActive(false);

			_adWatchingText = CreateText("AdWatchingText", centerPanel.transform, font, 34, new Color(0.85f, 0.85f, 0.85f), FontStyle.Italic, TextAnchor.MiddleCenter);
			_adWatchingText.text = "Смотрите рекламу...";
			_adWatchingText.gameObject.SetActive(false);

			var buttonGo = new GameObject("WatchAdButton", typeof(RectTransform));
			buttonGo.transform.SetParent(centerPanel.transform, false);

			var buttonRect = buttonGo.GetComponent<RectTransform>();
			buttonRect.sizeDelta = new Vector2(560f, 100f);

			var buttonImage = buttonGo.AddComponent<Image>();
			buttonImage.sprite = UISpriteFactory.GetRoundedSquare(64, 16, 0);
			buttonImage.type = Image.Type.Sliced;
			buttonImage.color = new Color(0.95f, 0.75f, 0.2f);

			_adButton = buttonGo.AddComponent<Button>();
			var colors = _adButton.colors;
			colors.highlightedColor = new Color(1f, 0.85f, 0.35f);
			colors.pressedColor = new Color(0.8f, 0.6f, 0.1f);
			_adButton.colors = colors;
			_adButton.onClick.AddListener(() => OnWatchAdClicked?.Invoke());

			_adButtonLabel = CreateText("Label", buttonGo.transform, font, 34, new Color(0.15f, 0.1f, 0f), FontStyle.Bold, TextAnchor.MiddleCenter);
			var labelRect = _adButtonLabel.rectTransform;
			labelRect.anchorMin = Vector2.zero;
			labelRect.anchorMax = Vector2.one;
			labelRect.offsetMin = Vector2.zero;
			labelRect.offsetMax = Vector2.zero;

			buttonGo.SetActive(false);
		}

		private static GameObject CreatePanel(string name, Transform parent, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 anchoredPosition)
		{
			var go = new GameObject(name, typeof(RectTransform));
			go.transform.SetParent(parent, false);

			var rect = go.GetComponent<RectTransform>();
			rect.anchorMin = anchorMin;
			rect.anchorMax = anchorMax;
			rect.pivot = pivot;
			rect.anchoredPosition = anchoredPosition;

			var image = go.AddComponent<Image>();
			image.sprite = UISpriteFactory.GetRoundedSquare(64, 18, 0);
			image.type = Image.Type.Sliced;
			image.color = new Color(0f, 0f, 0f, 0.55f);

			return go;
		}

		private static Text CreateText(string name, Transform parent, Font font, int fontSize, Color color, FontStyle style, TextAnchor alignment = TextAnchor.UpperLeft)
		{
			var go = new GameObject(name, typeof(RectTransform));
			go.transform.SetParent(parent, false);

			var text = go.AddComponent<Text>();
			text.font = font;
			text.fontSize = fontSize;
			text.color = color;
			text.fontStyle = style;
			text.alignment = alignment;
			text.horizontalOverflow = HorizontalWrapMode.Overflow;
			text.verticalOverflow = VerticalWrapMode.Overflow;

			return text;
		}

		/// <summary>
		/// Builds one row (color swatch + progress label) per order. Call
		/// once after the level's OrderBook is known; row count never
		/// changes for the lifetime of one level attempt.
		/// </summary>
		public void BuildOrderRows(IReadOnlyList<Order> orders)
		{
			Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
			_orderRows.Clear();

			for (int i = 0; i < orders.Count; i++)
			{
				var row = new GameObject($"OrderRow_{i}", typeof(RectTransform));
				row.transform.SetParent(_ordersContainer.transform, false);

				var rowLayout = row.AddComponent<HorizontalLayoutGroup>();
				rowLayout.spacing = 12f;
				rowLayout.childAlignment = TextAnchor.MiddleLeft;
				rowLayout.childControlWidth = true;
				rowLayout.childControlHeight = true;
				rowLayout.childForceExpandWidth = false;
				rowLayout.childForceExpandHeight = false;

				var rowFitter = row.AddComponent<ContentSizeFitter>();
				rowFitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
				rowFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

				var swatchGo = new GameObject("Swatch", typeof(RectTransform));
				swatchGo.transform.SetParent(row.transform, false);

				var swatchRect = swatchGo.GetComponent<RectTransform>();
				swatchRect.sizeDelta = new Vector2(36f, 36f);

				var swatch = swatchGo.AddComponent<Image>();
				swatch.sprite = UISpriteFactory.GetRoundedSquare(32, 8, 0);
				swatch.type = Image.Type.Sliced;

				var label = CreateText("Label", row.transform, font, 30, Color.white, FontStyle.Normal, TextAnchor.MiddleLeft);

				_orderRows.Add((swatch, label));
			}
		}

		public void SetScore(int score, int target)
		{
			_scoreText.text = $"Счёт: {score} / {target}";
		}

		public void SetMoves(int moves)
		{
			_movesText.text = $"Ходы: {moves}";
		}

		public void UpdateOrder(int index, Order order)
		{
			if (index < 0 || index >= _orderRows.Count)
				return;

			(Image swatch, Text label) = _orderRows[index];
			swatch.color = TileView.ColorForItemColor(order.Color);
			label.text = order.IsComplete
				? $"{order.Color}: ✓ готово"
				: $"{order.Color}: {order.CollectedCount}/{order.RequiredCount}";
		}

		public void SetEndState(string text)
		{
			bool visible = !string.IsNullOrEmpty(text);
			_endStateText.gameObject.SetActive(visible);
			_endStateText.text = text ?? string.Empty;
		}

		public void SetAdWatching(bool watching)
		{
			_adWatchingText.gameObject.SetActive(watching);
		}

		public void SetAdButton(bool visible, string label)
		{
			_adButton.gameObject.SetActive(visible);
			if (visible)
				_adButtonLabel.text = label;
		}
	}
}
