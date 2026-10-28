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
                if (target == BuildTarget.Android) ConfigureAndroidExternalTools();
                ConfigureFullPlayerBuild();
                PrebuildCommand.GenerateAll();
                ConfigureFullPlayerBuild();
                Directory.CreateDirectory(output);
                string artifact = Path.Combine(output, platform == "Android" ? "Client.apk" : "Client.exe");
                var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
                {
                    scenes = new[] { "Assets/Scenes/Bootstrap.unity" },
                    locationPathName = artifact,
                    target = target,
                    options = BuildOptions.None
                });
                File.WriteAllText(Path.Combine(output, "build-result.json"), CreateResultJson(platform, artifact,
                    report.summary.result, report.summary.totalErrors, report.summary.totalWarnings, report.summary.totalSize) + "\n");
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

        public static string CreateResultJson(string platform, string artifact, BuildResult buildResult,
            int errors, int warnings, ulong totalBuildBytes)
        {
            var group = platform == "Windows64" ? BuildTargetGroup.Standalone :
                platform == "Android" ? BuildTargetGroup.Android : throw new ArgumentException("Expected Windows64 or Android.");
            var result = new SmokeResult
            {
                platform = platform,
                result = buildResult.ToString(),
                errors = errors,
                warnings = warnings,
                artifact = artifact,
                backend = PlayerSettings.GetScriptingBackend(group).ToString(),
                unityVersion = Application.unityVersion,
                totalBuildBytes = totalBuildBytes,
                artifactBytes = File.Exists(artifact) ? new FileInfo(artifact).Length : 0,
#if UNITY_ANDROID
                androidSdk = UnityEditor.Android.AndroidExternalToolsSettings.sdkRootPath,
                androidNdk = UnityEditor.Android.AndroidExternalToolsSettings.ndkRootPath,
                javaHome = UnityEditor.Android.AndroidExternalToolsSettings.jdkRootPath
#endif
            };
            return JsonUtility.ToJson(result, true);
        }

        private static void ConfigureFullPlayerBuild()
        {
            EditorUserBuildSettings.development = false;
            EditorUserBuildSettings.buildAppBundle = false;
            EditorUserBuildSettings.buildScriptsOnly = false;
            EditorUserBuildSettings.exportAsGoogleAndroidProject = false;
#if UNITY_EDITOR_WIN
            UnityEditor.WindowsStandalone.UserBuildSettings.createSolution = false;
#endif
        }

        private static void ConfigureAndroidExternalTools()
        {
#if UNITY_ANDROID
            string sdk = Environment.GetEnvironmentVariable("ANDROID_SDK_ROOT");
            string ndk = Environment.GetEnvironmentVariable("ANDROID_NDK_ROOT");
            string jdk = Environment.GetEnvironmentVariable("JAVA_HOME");
            foreach (var path in new[] { sdk, ndk, jdk })
                if (!string.IsNullOrEmpty(path) && !Directory.Exists(path))
                    throw new DirectoryNotFoundException("Android toolchain path does not exist: " + path);
            if (!string.IsNullOrEmpty(sdk)) UnityEditor.Android.AndroidExternalToolsSettings.sdkRootPath = Path.GetFullPath(sdk);
            if (!string.IsNullOrEmpty(ndk)) UnityEditor.Android.AndroidExternalToolsSettings.ndkRootPath = Path.GetFullPath(ndk);
            if (!string.IsNullOrEmpty(jdk)) UnityEditor.Android.AndroidExternalToolsSettings.jdkRootPath = Path.GetFullPath(jdk);
#endif
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
            public ulong totalBuildBytes;
            public long artifactBytes;
            public string androidSdk;
            public string androidNdk;
            public string javaHome;
        }
    }
}
