using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace CricketVR.Editor
{
    /// <summary>
    /// Builds the Quest APK from the enabled build scenes.
    /// Menu for the Editor; static Build() for batch mode:
    ///   Unity -batchmode -projectPath . -executeMethod CricketVR.Editor.QuestBuild.Build -quit
    /// Add -cricketvrGround for a GroundMode build (plan.md §15); GameConfig.environment is restored afterwards.
    /// </summary>
    public static class QuestBuild
    {
        public const string OutputPath = "Builds/CricketVR.apk";

        [MenuItem("CricketVR/Build/Quest APK (Development)")]
        public static void BuildDevelopmentMenu() => Run(development: true, autoRun: false);

        [MenuItem("CricketVR/Build/Quest APK (Development) and Run on Headset")]
        public static void BuildAndRunMenu() => Run(development: true, autoRun: true);

        [MenuItem("CricketVR/Build/Quest APK (Development, Ground mode)")]
        public static void BuildGroundMenu() => Run(development: true, autoRun: false, EnvironmentMode.GroundMode);

        [MenuItem("CricketVR/Build/Quest APK (Ground mode, foveation A-B measurement)")]
        public static void BuildGroundAbMenu() => Run(development: true, autoRun: false, EnvironmentMode.GroundMode, foveationAbSeconds: 30f);

        /// <summary>Batch-mode entry. Exits with a non-zero code on failure.</summary>
        public static void Build()
        {
            var args = Environment.GetCommandLineArgs();
            bool development = !args.Contains("-cricketvrRelease");
            var mode = args.Contains("-cricketvrGround") ? EnvironmentMode.GroundMode : EnvironmentMode.NetMode;
            var report = Run(development, autoRun: false, mode, args.Contains("-cricketvrFoveationAB") ? 30f : 0f);
            EditorApplication.Exit(report != null && report.summary.result == BuildResult.Succeeded ? 0 : 1);
        }

        static BuildReport Run(bool development, bool autoRun, EnvironmentMode mode = EnvironmentMode.NetMode, float foveationAbSeconds = 0f)
        {
            var config = AssetDatabase.LoadAssetAtPath<GameConfig>(BattingBuilder.ConfigPath);
            if (config == null)
            {
                Debug.LogError("[CricketVR] GameConfig missing. Run CricketVR > Setup > 6. Build or Repair Main Scene first.");
                return null;
            }
            var previous = config.environment;
            float previousAb = config.foveationAbSeconds;
            config.environment = mode;
            config.foveationAbSeconds = foveationAbSeconds;
            EditorUtility.SetDirty(config);
            AssetDatabase.SaveAssets();
            try
            {
                Debug.Log($"[CricketVR] Building in {mode}" + (foveationAbSeconds > 0f ? $", foveation A/B every {foveationAbSeconds} s" : ""));
                return RunBuild(development, autoRun);
            }
            finally
            {
                // The build unloads and reloads assets, so the GameConfig object loaded above may be destroyed: reload it.
                var reloaded = AssetDatabase.LoadAssetAtPath<GameConfig>(BattingBuilder.ConfigPath);
                if (reloaded != null && (reloaded.environment != previous || reloaded.foveationAbSeconds != previousAb))
                {
                    reloaded.environment = previous;
                    reloaded.foveationAbSeconds = previousAb;
                    EditorUtility.SetDirty(reloaded);
                    AssetDatabase.SaveAssets();
                }
            }
        }

        static BuildReport RunBuild(bool development, bool autoRun)
        {
            if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.Android)
            {
                Debug.LogError("[CricketVR] Active build target is not Android. Run CricketVR > Setup > 3. Apply Quest Player Settings first.");
                return null;
            }

            var scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray();
            if (scenes.Length == 0)
            {
                Debug.LogError("[CricketVR] No enabled build scenes. Run CricketVR > Setup > 6. Build or Repair Main Scene first.");
                return null;
            }

            if (XrProjectConfigurator.ReportValidation(fixAutomatic: false) > 0)
                Debug.LogWarning("[CricketVR] Building with OpenXR validation errors; the app may not start in VR.");

            Directory.CreateDirectory(Path.GetDirectoryName(OutputPath));
            var options = BuildOptions.None;
            if (development) options |= BuildOptions.Development;
            if (autoRun) options |= BuildOptions.AutoRunPlayer;

            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = OutputPath,
                target = BuildTarget.Android,
                options = options,
            });

            var s = report.summary;
            var line = $"[CricketVR] Build {s.result}: {s.outputPath}, {s.totalSize / (1024f * 1024f):F1} MB, " +
                       $"{s.totalErrors} error(s), {s.totalWarnings} warning(s), {s.totalTime.TotalSeconds:F0} s.";
            if (s.result == BuildResult.Succeeded) Debug.Log(line); else Debug.LogError(line);
            return report;
        }
    }
}
