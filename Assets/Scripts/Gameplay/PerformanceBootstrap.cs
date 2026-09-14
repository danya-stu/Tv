using UnityEngine;

namespace PotionCraft.Gameplay
{
	/// <summary>
	/// Mobile platforms default Application.targetFrameRate to a conservative
	/// value (commonly 30, to save battery) unless told otherwise, and a
	/// non-zero QualitySettings.vSyncCount can silently halve the effective
	/// frame rate on top of that. This runs once before any scene loads so
	/// every build (Editor, Android, iOS) starts at a consistent, higher
	/// frame rate without needing a GameObject placed in every scene.
	///
	/// A low frame rate does not just look choppy: at 30fps each frame is
	/// about 33ms, so a fast tap/swipe can start and end entirely between two
	/// frames and never register as a press. Raising the frame rate directly
	/// improves input reliability, not just visual smoothness.
	/// </summary>
	public static class PerformanceBootstrap
	{
		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
		private static void Configure()
		{
			// vSyncCount overrides targetFrameRate when non-zero, so it must be
			// explicitly disabled first for targetFrameRate to take effect.
			QualitySettings.vSyncCount = 0;
			Application.targetFrameRate = 60;
		}
	}
}
