using System;
using System.IO;
using System.Collections;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using PotionCraft.Gameplay;

namespace PotionCraft.Tests.Editor
{
	[InitializeOnLoad]
	public static class EditorPlayHelper
	{
		private const string CommandPath = "Temp/editor_cmd.txt";
		private const string ResultPath = "Temp/editor_result.txt";
		private const string ScreenshotPath = "d:/tv/game_view_pc.png";

		static EditorPlayHelper()
		{
			EditorApplication.update += OnEditorUpdate;
		}

		private static void OnEditorUpdate()
		{
			if (!File.Exists(CommandPath)) return;

			string cmd = "";
			try
			{
				cmd = File.ReadAllText(CommandPath).Trim();
			}
			catch
			{
				return;
			}

			if (string.IsNullOrEmpty(cmd)) return;

			// Handle commands
			if (cmd == "play_and_capture")
			{
				File.Delete(CommandPath);
				ExecutePlayAndCapture();
			}
			else if (cmd == "capture")
			{
				File.Delete(CommandPath);
				CaptureFrame();
			}
			else if (cmd.StartsWith("swipe:"))
			{
				File.Delete(CommandPath);
				ExecuteSwipe(cmd);
			}
			else if (cmd == "stop_play")
			{
				File.Delete(CommandPath);
				EditorApplication.isPlaying = false;
				File.WriteAllText(ResultPath, "STOPPED");
			}
			else if (cmd == "build_android")
			{
				File.Delete(CommandPath);
				BuildAndroid();
			}
		}

		[MenuItem("Tools/Build Android APK")]
		public static void BuildAndroid()
		{
			string[] scenes = new string[] { "Assets/Scenes/Match3.unity" };
			string buildPath = "d:/tv/build.apk";
			BuildPlayerOptions opts = new BuildPlayerOptions
			{
				scenes = scenes,
				locationPathName = buildPath,
				target = BuildTarget.Android,
				options = BuildOptions.None
			};

			var report = BuildPipeline.BuildPlayer(opts);
			string res = report.summary.result.ToString();
			File.WriteAllText(ResultPath, "BUILD_" + res + ": " + buildPath);
			Debug.Log($"[EditorPlayHelper] Android build finished with status: {res}, Errors: {report.summary.totalErrors}");
		}

		[MenuItem("Tools/Capture Game View")]
		public static void CaptureMenu()
		{
			CaptureFrame();
		}

		[MenuItem("Tools/Run Play Mode & Capture")]
		public static void PlayAndCaptureMenu()
		{
			ExecutePlayAndCapture();
		}

		private static void ExecutePlayAndCapture()
		{
			if (!EditorApplication.isPlaying)
			{
				if (EditorSceneManager.GetActiveScene().name != "Match3")
				{
					EditorSceneManager.OpenScene("Assets/Scenes/Match3.unity");
				}
				EditorApplication.isPlaying = true;
			}

			// Add a runner GameObject into the scene if playing
			EditorApplication.delayCall += () =>
			{
				var runner = new GameObject("EditorAutomationRunner");
				runner.AddComponent<AutomationRunner>();
			};
		}

		private static void ExecuteSwipe(string cmd)
		{
			// Format swipe:x1,y1,x2,y2
			string[] parts = cmd.Substring("swipe:".Length).Split(',');
			if (parts.Length == 4 &&
			    int.TryParse(parts[0], out int x1) &&
			    int.TryParse(parts[1], out int y1) &&
			    int.TryParse(parts[2], out int x2) &&
			    int.TryParse(parts[3], out int y2))
			{
				var runner = new GameObject("EditorSwipeRunner");
				var comp = runner.AddComponent<AutomationRunner>();
				comp.TargetSwipe = new Vector4(x1, y1, x2, y2);
				comp.PerformSwipe();
			}
		}

		public static void CaptureFrame()
		{
			Camera cam = Camera.main;
			if (cam == null) cam = GameObject.FindObjectOfType<Camera>();

			if (cam != null)
			{
				int width = 1080;
				int height = 1920;
				RenderTexture rt = new RenderTexture(width, height, 24);
				RenderTexture prev = cam.targetTexture;
				cam.targetTexture = rt;
				cam.Render();

				RenderTexture.active = rt;
				Texture2D tex = new Texture2D(width, height, TextureFormat.RGB24, false);
				tex.ReadPixels(new Rect(0, 0, width, height), 0, 0);
				tex.Apply();

				cam.targetTexture = prev;
				RenderTexture.active = null;
				GameObject.DestroyImmediate(rt);

				byte[] bytes = tex.EncodeToPNG();
				GameObject.DestroyImmediate(tex);

				File.WriteAllBytes(ScreenshotPath, bytes);
				File.WriteAllText(ResultPath, "SUCCESS: " + ScreenshotPath);
				Debug.Log("[EditorPlayHelper] Captured screenshot to " + ScreenshotPath);
			}
			else
			{
				File.WriteAllText(ResultPath, "ERROR: No Camera found");
			}
		}

		private sealed class AutomationRunner : MonoBehaviour
		{
			public Vector4? TargetSwipe;

			private void Start()
			{
				StartCoroutine(Routine());
			}

			public void PerformSwipe()
			{
				StartCoroutine(SwipeRoutine());
			}

			private IEnumerator Routine()
			{
				// Wait for initialization
				yield return new WaitForSeconds(0.6f);

				CaptureFrame();
				File.WriteAllText(ResultPath, "PLAY_READY: " + ScreenshotPath);
				Destroy(gameObject);
			}

			private IEnumerator SwipeRoutine()
			{
				yield return new WaitForSeconds(0.2f);

				var controller = GameObject.FindObjectOfType<BoardController>();
				if (controller != null && TargetSwipe.HasValue)
				{
					Vector4 s = TargetSwipe.Value;
					Vector2Int from = new Vector2Int((int)s.x, (int)s.y);
					Vector2Int to = new Vector2Int((int)s.z, (int)s.w);

					var method = typeof(BoardController).GetMethod("TryExecuteMove", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public);
					if (method != null)
					{
						method.Invoke(controller, new object[] { from, to });
					}
				}

				// Wait for cascade animation
				yield return new WaitForSeconds(0.8f);

				CaptureFrame();
				File.WriteAllText(ResultPath, "SWIPE_DONE: " + ScreenshotPath);
				Destroy(gameObject);
			}
		}
	}
}
