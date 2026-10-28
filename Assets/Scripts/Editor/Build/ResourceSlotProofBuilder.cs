using System;
using System.IO;
using HybridCLR.Commands;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityBuildResult = UnityEditor.Build.Reporting.BuildResult;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityGameFramework.Editor.ResourceTools;

namespace Client.Editor.Build
{
    public static class ResourceSlotProofBuilder
    {
        private static string proofRoot;
        private static ResourceConfigurationScope configuration;

        public static void Run()
        {
            try
            {
                proofRoot = Path.GetFullPath(Argument("-resourceProofOutput"));
                if (Directory.Exists(proofRoot)) throw new IOException("Proof output must be a new directory.");
                if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.StandaloneWindows64)
                    throw new BuildFailedException("Launch with -buildTarget Win64.");
                Directory.CreateDirectory(proofRoot);
                configuration = ResourceConfigurationScope.Enter(proofRoot);
                ProjectSetup.CreateBootstrap();
                BuildPackage("A", 1);
                BuildPackage("B", 2);
                const string proofScene = "Assets/Tests/Generated/ResourceSlotProof.unity";
                var scene = EditorSceneManager.OpenScene("Assets/Scenes/Bootstrap.unity");
                var proofType = System.Type.GetType("Client.ResourceSlot.Proof.ResourceSlotProof, Client.ResourceSlot.Proof");
                if (proofType == null) throw new BuildFailedException("Editor proof assembly is unavailable.");
                new GameObject("Resource Slot Proof").AddComponent(proofType);
                EditorSceneManager.SaveScene(scene, proofScene);
                ProjectSetup.ConfigurePlayer();
                EditorUserBuildSettings.development = false;
                EditorUserBuildSettings.buildScriptsOnly = false;
                UnityEditor.WindowsStandalone.UserBuildSettings.createSolution = false;
                PrebuildCommand.GenerateAll();
                EditorUserBuildSettings.buildScriptsOnly = false;
                string artifact = Path.Combine(proofRoot, "Player", "Client.exe");
                var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
                {
                    scenes = new[] { proofScene }, target = BuildTarget.StandaloneWindows64,
                    locationPathName = artifact, options = BuildOptions.None,
                    extraScriptingDefines = new[] { "RESOURCE_SLOT_PROOF" }
                });
                File.WriteAllText(Path.Combine(proofRoot, "build-result.json"), SmokeBuild.CreateResultJson("Windows64", artifact,
                    report.summary.result, report.summary.totalErrors, report.summary.totalWarnings, report.summary.totalSize) + "\n");
                if (report.summary.result != UnityBuildResult.Succeeded || report.summary.totalErrors != 0 || !File.Exists(artifact))
                    throw new BuildFailedException("Resource proof Player build failed.");
                Debug.Log("RESOURCE_SLOT_BUILD_PASS");
            }
            catch (Exception exception) { Debug.LogException(exception); EditorApplication.Exit(1); }
            finally { try { configuration?.Dispose(); } finally { configuration = null; proofRoot = null; } }
        }

        private static void BuildPackage(string marker, int version)
        {
            const string asset = "Assets/Tests/Generated/ResourceSlot/marker.txt";
            Directory.CreateDirectory(Path.GetDirectoryName(asset));
            File.WriteAllText(asset, marker + "\n");
            AssetDatabase.ImportAsset(asset, ImportAssetOptions.ForceUpdate);
            var collection = configuration.CreateCollection();
            if (!collection.AddResource("proof/marker", null, null, LoadType.LoadFromFile, true) ||
                !collection.AssignAsset(AssetDatabase.AssetPathToGUID(asset), "proof/marker", null) || !collection.Save())
                throw new BuildFailedException("Cannot create native proof collection.");
            string output = Path.Combine(proofRoot, "NativeBuild", marker);
            Directory.CreateDirectory(output);
            var builder = configuration.CreateBuilder();
            builder.Platforms = Platform.Windows64;
            ConfigureBuilder(builder, version, output);
            if (!builder.BuildResources()) throw new BuildFailedException("Native GF Package build failed: " + marker);
            string destination = Path.Combine(proofRoot, "Slots", marker, "Resources");
            CopyDirectory(Path.Combine(builder.OutputPackagePath, "Windows64"), destination);
            Directory.CreateDirectory(Path.Combine(proofRoot, "Slots", marker, "Scratch"));
        }

        private static void ConfigureBuilder(ResourceBuilderController builder, int version, string output)
        {
            builder.InternalResourceVersion = version; builder.OutputDirectory = output;
            builder.OutputPackageSelected = true; builder.OutputFullSelected = false; builder.OutputPackedSelected = false;
            builder.ForceRebuildAssetBundleSelected = true;
        }

        private static void CopyDirectory(string source, string destination)
        {
            Directory.CreateDirectory(destination);
            foreach (string file in Directory.GetFiles(source)) File.Copy(file, Path.Combine(destination, Path.GetFileName(file)));
            foreach (string directory in Directory.GetDirectories(source)) CopyDirectory(directory, Path.Combine(destination, Path.GetFileName(directory)));
        }

        private static string Argument(string name)
        {
            var args = Environment.GetCommandLineArgs();
            int index = Array.IndexOf(args, name);
            if (index < 0 || index + 1 >= args.Length) throw new ArgumentException(name + " is required.");
            return args[index + 1];
        }
    }
}
