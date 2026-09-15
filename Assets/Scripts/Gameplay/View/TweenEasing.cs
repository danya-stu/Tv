using UnityEngine;

namespace PotionCraft.Gameplay.View
{
	/// <summary>
	/// Pure math easing functions for the procedural tween layer. No third-party
	/// tween plugin (DOTween and friends) is used anywhere in the project: every
	/// animation is a coroutine driving a normalized time value through one of
	/// these curves. All functions take and return normalized time in [0, 1]
	/// (EaseOutBack intentionally overshoots above 1 before settling).
	/// </summary>
	public static class TweenEasing
	{
		/// <summary>Smooth acceleration then deceleration. The default for swaps.</summary>
		public static float EaseInOutQuad(float t)
		{
			t = Mathf.Clamp01(t);
			return t < 0.5f
				? 2f * t * t
				: 1f - (Mathf.Pow((-2f * t) + 2f, 2f) * 0.5f);
		}

		/// <summary>Slow start, fast finish. Used for shrink-to-zero destruction.</summary>
		public static float EaseInQuad(float t)
		{
			t = Mathf.Clamp01(t);
			return t * t;
		}

		/// <summary>Gravity-like landing with three decaying bounces. Used for falls.</summary>
		public static float EaseOutBounce(float t)
		{
			t = Mathf.Clamp01(t);

			const float n1 = 7.5625f;
			const float d1 = 2.75f;

			if (t < 1f / d1)
			{
				return n1 * t * t;
			}

			if (t < 2f / d1)
			{
				t -= 1.5f / d1;
				return (n1 * t * t) + 0.75f;
			}

			if (t < 2.5f / d1)
			{
				t -= 2.25f / d1;
				return (n1 * t * t) + 0.9375f;
			}

			t -= 2.625f / d1;
			return (n1 * t * t) + 0.984375f;
		}

		/// <summary>
		/// Overshoots the target and settles back, which reads as a "pop" when a
		/// bonus tile appears. Overshoot defaults to a mobile-friendly 1.2.
		/// </summary>
		public static float EaseOutBack(float t, float overshoot = 1.2f)
		{
			t = Mathf.Clamp01(t);
			float c1 = overshoot;
			float c3 = c1 + 1f;
			float p = t - 1f;

			return 1f + (c3 * p * p * p) + (c1 * p * p);
		}

		/// <summary>Linear passthrough, handy as an explicit "no easing" argument.</summary>
		public static float Linear(float t)
		{
			return Mathf.Clamp01(t);
		}

		/// <summary>EaseOutBack with the default overshoot, usable as a Func<float, float>.</summary>
		public static float EaseOutBackDefault(float t)
		{
			return EaseOutBack(t);
		}
	}
}
