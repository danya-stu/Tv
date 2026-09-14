using System.Collections;
using UnityEngine;
using PotionCraft.Core;

namespace PotionCraft.Gameplay
{
	/// <summary>
	/// Visual for a single grid cell. Renders a tinted, procedurally-baked
	/// faceted gem icon plus a small drop shadow (see GemSpriteFactory) on a
	/// child "Visual" transform, and exposes the move/shrink animations
	/// GridView needs while swaps and cascades resolve. The root transform
	/// carries grid position (MoveTo) and the fixed board-scale GridView
	/// sets once; the child Visual transform layers a gentle idle
	/// "breathing" pulse on top so tiles feel alive without fighting the
	/// root's position/shrink animations. Catalyst tiles (see SetItem) pulse
	/// noticeably more so they read as special at a glance. Swap this out
	/// for real imported sprites later without touching GridView's public
	/// API: only SetColor/SetItem and the sprite creation need to change.
	/// </summary>
	public sealed class TileView : MonoBehaviour
	{
		private const float BasePulseAmplitude = 0.035f;
		private const float CatalystPulseAmplitude = 0.075f;
		private const float PulseSpeed = 1.6f;

		private Transform _visual;
		private SpriteRenderer _spriteRenderer;
		private SpriteRenderer _shadowRenderer;
		private float _phaseOffset;
		private float _pulseAmplitude = BasePulseAmplitude;
		private bool _isAnimatingScale;

		public int GridX { get; private set; }
		public int GridY { get; private set; }

		private void Awake()
		{
			var visualGo = new GameObject("Visual");
			visualGo.transform.SetParent(transform, false);
			_visual = visualGo.transform;

			// Small drop shadow, offset down-right and slightly inset, so the
			// gem visually lifts off the board's slot background instead of
			// looking pasted flat onto it.
			var shadowGo = new GameObject("Shadow");
			shadowGo.transform.SetParent(_visual, false);
			shadowGo.transform.localPosition = new Vector3(0.08f, -0.08f, 0.01f);
			shadowGo.transform.localScale = Vector3.one * 0.94f;
			_shadowRenderer = shadowGo.AddComponent<SpriteRenderer>();
			_shadowRenderer.sprite = GemSpriteFactory.GetGemShadow(64);
			_shadowRenderer.sortingOrder = -1;

			_spriteRenderer = visualGo.AddComponent<SpriteRenderer>();
			_spriteRenderer.sprite = GemSpriteFactory.GetGem(64);
			_spriteRenderer.sortingOrder = 0;

			// Randomized per-tile so a full board of gems doesn't breathe in
			// perfect unison, which would look robotic instead of alive.
			_phaseOffset = Random.Range(0f, Mathf.PI * 2f);
		}

		private void Update()
		{
			// Never fight ShrinkAndDisable's own scale animation on the root;
			// once that starts, the child's leftover pulse scale is harmless
			// since it is about to shrink to zero anyway.
			if (_isAnimatingScale)
				return;

			float pulse = 1f + Mathf.Sin(Time.time * PulseSpeed + _phaseOffset) * _pulseAmplitude;
			_visual.localScale = Vector3.one * pulse;
		}

		public void SetGridPosition(int x, int y)
		{
			GridX = x;
			GridY = y;
		}

		public void SetColor(ItemColor color)
		{
			_spriteRenderer.color = ColorForItemColor(color);
		}

		/// <summary>
		/// Sets the tile's visual from a full Item, including a brightened
		/// "glow" tint and a noticeably larger idle pulse when the item
		/// carries a Reaction Catalyst effect, so catalyst tiles are
		/// distinguishable at a glance even before dedicated catalyst
		/// art/icons exist.
		/// </summary>
		public void SetItem(Item item)
		{
			SetColor(item.Color);

			bool isCatalyst = item.Catalyst != CatalystType.None;
			if (isCatalyst)
				_spriteRenderer.color = Color.Lerp(_spriteRenderer.color, Color.white, 0.45f);

			_pulseAmplitude = isCatalyst ? CatalystPulseAmplitude : BasePulseAmplitude;
		}

		public static Color ColorForItemColor(ItemColor color)
		{
			switch (color)
			{
				case ItemColor.Red:
					return new Color(0.85f, 0.2f, 0.2f);
				case ItemColor.Blue:
					return new Color(0.2f, 0.45f, 0.85f);
				case ItemColor.Green:
					return new Color(0.25f, 0.7f, 0.3f);
				case ItemColor.Yellow:
					return new Color(0.9f, 0.8f, 0.2f);
				case ItemColor.Purple:
					return new Color(0.6f, 0.3f, 0.75f);
				default:
					return Color.clear;
			}
		}

		public IEnumerator MoveTo(Vector3 targetLocalPosition, float duration)
		{
			Vector3 start = transform.localPosition;

			if (duration <= 0f)
			{
				transform.localPosition = targetLocalPosition;
				yield break;
			}

			float elapsed = 0f;
			while (elapsed < duration)
			{
				elapsed += Time.deltaTime;
				float t = Mathf.Clamp01(elapsed / duration);
				transform.localPosition = Vector3.Lerp(start, targetLocalPosition, t);
				yield return null;
			}

			transform.localPosition = targetLocalPosition;
		}

		public IEnumerator ShrinkAndDisable(float duration)
		{
			_isAnimatingScale = true;
			Vector3 startScale = transform.localScale;

			if (duration <= 0f)
			{
				transform.localScale = Vector3.zero;
				gameObject.SetActive(false);
				yield break;
			}

			float elapsed = 0f;
			while (elapsed < duration)
			{
				elapsed += Time.deltaTime;
				float t = Mathf.Clamp01(elapsed / duration);
				transform.localScale = Vector3.Lerp(startScale, Vector3.zero, t);
				yield return null;
			}

			transform.localScale = Vector3.zero;
			gameObject.SetActive(false);
		}
	}
}
