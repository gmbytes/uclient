using System;
using System.IO;
using HybridCLR.Commands;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Client.Editor.Build
{
    public static class SmokeBuild
    {
        public static void Run()
        {
            try
            {
                string platform = Argument("-smokePlatform");
                string output = Path.GetFullPath(Argument("-smokeOutput"));
                BuildTarget target = platform == "Windows64" ? BuildTarget.StandaloneWindows64 :
                    platform == "Android" ? BuildTarget.Android : throw new ArgumentException("Expected Windows64 or Android.");
                if (Directory.Exists(output) || File.Exists(output)) throw new IOException("Smoke output must be a new directory.");
                if (!BuildPipeline.IsBuildTargetSupported(BuildPipeline.GetBuildTargetGroup(target), target))
                    throw new BuildFailedException("Required platform module is unavailable: " + target);
                if (EditorUserBuildSettings.activeBuildTarget != target)
                    throw new BuildFailedException("Launch Unity with -buildTarget for the requested platform.");
                ProjectSetup.ConfigurePlayer();
                EditorUserBuildSettings.development = false;
                EditorUserBuildSettings.buildAppBundle = false;
                PrebuildCommand.GenerateAll();
                Directory.CreateDirectory(output);
                string artifact = Path.Combine(output, platform == "Android" ? "Client.apk" : "Client.exe");
                var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
                {
                    scenes = new[] { "Assets/Scenes/Bootstrap.unity" },
                    locationPathName = artifact,
                    target = target,
                    options = BuildOptions.None
                });
                var result = new SmokeResult
                {
                    platform = platform,
                    result = report.summary.result.ToString(),
                    errors = report.summary.totalErrors,
                    warnings = report.summary.totalWarnings,
                    artifact = artifact,
                    backend = PlayerSettings.GetScriptingBackend(BuildPipeline.GetBuildTargetGroup(target)).ToString(),
                    unityVersion = Application.unityVersion,
                    bytes = report.summary.totalSize
                };
                File.WriteAllText(Path.Combine(output, "build-result.json"), JsonUtility.ToJson(result, true) + "\n");
                if (report.summary.result != BuildResult.Succeeded || report.summary.totalErrors != 0 || !File.Exists(artifact))
                    throw new BuildFailedException("Smoke build failed; see build-result.json and Editor.log.");
                Debug.Log("SMOKE_BUILD_PASS " + platform);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                EditorApplication.Exit(1);
            }
        }

        private static string Argument(string name)
        {
            string[] args = Environment.GetCommandLineArgs();
            int index = Array.IndexOf(args, name);
            if (index < 0 || index + 1 >= args.Length || args[index + 1].StartsWith("-"))
                throw new ArgumentException("Required argument missing: " + name);
            return args[index + 1];
        }

        [Serializable]
        private sealed class SmokeResult
        {
            public string platform;
            public string result;
            public int errors;
            public int warnings;
            public string artifact;
            public string backend;
            public string unityVersion;
            public ulong bytes;
        }
    }
}
