using System;

namespace PotionCraft.Core
{
	/// <summary>
	/// Bridges a rewarded ad view to the pure GameSession "continue" reward,
	/// so the ad SDK, the UI button, and the scoring rules never need to
	/// know about each other directly.
	/// </summary>
	public static class AdRewardController
	{
		/// <summary>
		/// Requests a rewarded-ad continue for a Lost session. If the
		/// session cannot currently accept a continue (not Lost, or the
		/// continue budget is already used up), the ad is never shown and
		/// onResult(false) fires immediately. Otherwise the ad is shown;
		/// onResult reports whether the continue was actually granted.
		/// </summary>
		public static void RequestContinue(
			GameSession session,
			IRewardedAdService adService,
			int bonusMoves,
			Action<bool> onResult)
		{
			if (session == null || adService == null)
			{
				onResult?.Invoke(false);
				return;
			}

			if (!session.CanContinueWithAd)
			{
				onResult?.Invoke(false);
				return;
			}

			adService.Show(watched =>
			{
				bool granted = watched && session.TryContinueWithBonusMoves(bonusMoves);
				onResult?.Invoke(granted);
			});
		}
	}
}
