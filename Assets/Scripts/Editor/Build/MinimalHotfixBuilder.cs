using System;
using System.IO;
using System.Linq;
using HybridCLR;
using HybridCLR.Commands;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Player;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Client.Editor.Build
{
    public static class MinimalHotfixBuilder
    {
        public static void Run()
        {
            try
            {
                string output = Path.GetFullPath(Argument("-hotfixProofOutput"));
                if (Directory.Exists(output)) throw new IOException("Proof output must be a new directory.");
                if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.StandaloneWindows64) throw new BuildFailedException("Launch with -buildTarget Win64.");
                Directory.CreateDirectory(output);
                ProjectSetup.CreateBootstrap();
                const string scenePath = "Assets/Tests/Generated/MinimalHotfixProof.unity";
                Directory.CreateDirectory(Path.GetDirectoryName(scenePath));
                var scene = EditorSceneManager.OpenScene("Assets/Scenes/Bootstrap.unity");
                var proofType = Type.GetType("Client.Bootstrap.Proof.HotfixProof, Client.Bootstrap.Proof");
                if (proofType == null) throw new BuildFailedException("Proof assembly is unavailable.");
                new GameObject("Minimal Hotfix Proof").AddComponent(proofType);
                EditorSceneManager.SaveScene(scene, scenePath);
                EditorUserBuildSettings.development = false;
                EditorUserBuildSettings.buildScriptsOnly = false;
                UnityEditor.WindowsStandalone.UserBuildSettings.createSolution = false;
                PrebuildCommand.GenerateAll();
                string artifact = Path.Combine(output, "Player", "Client.exe");
                var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
                {
                    scenes = new[] { scenePath }, target = BuildTarget.StandaloneWindows64,
                    locationPathName = artifact, options = BuildOptions.None,
                    extraScriptingDefines = new[] { "MINIMAL_HOTFIX_PROOF" }
                });
                File.WriteAllText(Path.Combine(output, "build-result.json"), SmokeBuild.CreateResultJson("Windows64", artifact,
                    report.summary.result, report.summary.totalErrors, report.summary.totalWarnings, report.summary.totalSize) + "\n");
                if (report.summary.result != BuildResult.Succeeded || report.summary.totalErrors != 0) throw new BuildFailedException("Minimal hotfix Player build failed.");

                // Read after this actual Player build: HybridCLR's postprocess replaces this directory.
                string metadata = Path.Combine(output, "Metadata");
                Directory.CreateDirectory(metadata);
                string finalStripped = SettingsUtil.GetAssembliesPostIl2CppStripDir(BuildTarget.StandaloneWindows64);
                var aotFiles = Directory.GetFiles(finalStripped, "*.dll");
                if (aotFiles.Length == 0 || aotFiles.Any(f => Path.GetFileNameWithoutExtension(f).StartsWith("Client.Game", StringComparison.Ordinal)))
                    throw new BuildFailedException("Final stripped AOT archive is missing or includes hotfix code.");
                foreach (string file in aotFiles) File.Copy(file, Path.Combine(metadata, Path.GetFileName(file)));
                string buildId = Guid.NewGuid().ToString("N");
                File.WriteAllText(Path.Combine(metadata, "player-build-id.txt"), buildId + "\n");
                File.WriteAllText(Path.Combine(metadata, "metadata-source.json"), JsonUtility.ToJson(new MetadataSource {
                    playerBuildId = buildId, platform = "StandaloneWindows64", sourceDirectory = Path.GetFullPath(finalStripped),
                    buildResult = Path.Combine(output, "build-result.json") }, true) + "\n");
                CompileFixture(output, "v1", "HOTFIX_PROOF_V1");
                CompileFixture(output, "v2", "HOTFIX_PROOF_V2");
                Debug.Log("MINIMAL_HOTFIX_BUILD_PASS");
            }
            catch (Exception exception) { Debug.LogException(exception); EditorApplication.Exit(1); }
        }

        private static void CompileFixture(string output, string version, string symbol)
        {
            string compileRoot = Path.Combine(output, "Compiled", version);
            var settings = new ScriptCompilationSettings
            {
                group = BuildTargetGroup.Standalone, target = BuildTarget.StandaloneWindows64,
                options = ScriptCompilationOptions.None, extraScriptingDefines = new[] { symbol }
            };
            Directory.CreateDirectory(compileRoot);
            var result = PlayerBuildInterface.CompilePlayerScripts(settings, compileRoot);
            string dll = Path.Combine(compileRoot, "Client.Game.dll");
            if (result.assemblies == null || !result.assemblies.Any(a => Path.GetFileName(a) == "Client.Game.dll") || !File.Exists(dll))
                throw new BuildFailedException("Actual Client.Game assembly compilation failed: " + version);
            string target = Path.Combine(output, "Dlls", version);
            Directory.CreateDirectory(target);
            string protocol = Path.Combine(compileRoot, "Client.Game.Protocol.dll");
            if (!result.assemblies.Any(a => Path.GetFileName(a) == "Client.Game.Protocol.dll") || !File.Exists(protocol))
                throw new BuildFailedException("The real generated protocol module did not compile: " + version);
            File.Copy(protocol, Path.Combine(target, "Client.Game.Protocol.dll"));
            File.Copy(dll, Path.Combine(target, "Client.Game.dll"));
        }

        private static string Argument(string name)
        {
            string[] args = Environment.GetCommandLineArgs();
            int index = Array.IndexOf(args, name);
            if (index < 0 || index + 1 >= args.Length) throw new ArgumentException(name + " is required.");
            return args[index + 1];
        }

        [Serializable]
        private sealed class MetadataSource
        {
            public string playerBuildId;
            public string platform;
            public string sourceDirectory;
            public string buildResult;
        }
    }
}
