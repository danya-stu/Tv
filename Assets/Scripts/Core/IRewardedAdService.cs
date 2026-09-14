using System;

namespace PotionCraft.Core
{
	/// <summary>
	/// Engine-agnostic contract for showing a rewarded video ad. Real
	/// implementations wrap a platform SDK (e.g. Yandex Mobile Ads for
	/// RuStore, or AdMob); AdRewardController and GameSession never depend
	/// on any SDK type directly, so the ad provider can be swapped later
	/// without touching gameplay or scoring logic.
	/// </summary>
	public interface IRewardedAdService
	{
		/// <summary>True once an ad has finished loading and can be shown immediately.</summary>
		bool IsReady { get; }

		/// <summary>
		/// Shows the rewarded ad. Calls onComplete(true) only if the user
		/// watched the ad through to completion and should receive the
		/// reward; calls onComplete(false) if the ad was skipped, failed to
		/// show, or wasn't ready.
		/// </summary>
		void Show(Action<bool> onComplete);
	}
}
