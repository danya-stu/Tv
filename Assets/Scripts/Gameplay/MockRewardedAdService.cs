using System;
using System.Collections;
using UnityEngine;
using PotionCraft.Core;

namespace PotionCraft.Gameplay
{
	/// <summary>
	/// Placeholder IRewardedAdService that simulates watching a rewarded ad
	/// with a short delay, always granting the reward. Lets us build and
	/// test the entire "watch ad to continue" flow end to end in the Editor
	/// and on-device before a real ad SDK (e.g. Yandex Mobile Ads for
	/// RuStore) is integrated. Swapping to a real SDK later only requires a
	/// new IRewardedAdService implementation; GridView, AdRewardController,
	/// and GameSession never need to change.
	/// </summary>
	public sealed class MockRewardedAdService : IRewardedAdService
	{
		private readonly MonoBehaviour _coroutineRunner;
		private readonly float _simulatedDelaySeconds;

		public bool IsReady => true;

		public MockRewardedAdService(MonoBehaviour coroutineRunner, float simulatedDelaySeconds = 1.5f)
		{
			_coroutineRunner = coroutineRunner;
			_simulatedDelaySeconds = simulatedDelaySeconds;
		}

		public void Show(Action<bool> onComplete)
		{
			_coroutineRunner.StartCoroutine(ShowRoutine(onComplete));
		}

		private IEnumerator ShowRoutine(Action<bool> onComplete)
		{
			yield return new WaitForSeconds(_simulatedDelaySeconds);
			onComplete?.Invoke(true);
		}
	}
}
