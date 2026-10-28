using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Client.Framework.HotUpdate;
using HybridCLR;
using HybridCLR.Commands;
using HybridCLR.Installer;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityGameFramework.Editor.ResourceTools;

namespace Client.Editor.Build
{
    // An isolated native acceptance harness. D1 owns the production build orchestrator.
    public static class HotfixAotProbeBuilder
    {
        private static string root;
        private static ReleaseLinkerSymbolPolicy linkerPolicy;
        [ResourceCollectionConfigPath] private static string CollectionPath => root == null ? null : Path.Combine(root, "ResourceCollection.xml");
        [ResourceBuilderConfigPath] private static string BuilderPath => root == null ? null : Path.Combine(root, "ResourceBuilder.xml");
        public static void Run()
        {
            try
            {
                root = Path.GetFullPath(Arg("-f6Output"));
                if (Directory.Exists(root)) throw new IOException("A new F6 output directory is required.");
                Directory.CreateDirectory(root);
                if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.StandaloneWindows64) throw new BuildFailedException("F6 native probe requires Win64.");
                var installer = new InstallerController();
                if (!installer.HasInstalledHybridCLR()) installer.InstallFromLocal(Path.GetFullPath(Arg("-hybridclrSource")));
                if (!installer.HasInstalledHybridCLR()) throw new BuildFailedException("Actual HybridCLR installation failed.");
                linkerPolicy = ReleaseLinkerSymbolPolicy.Apply(Arg("-f6LinkerPolicy"));
                File.Copy(Arg("-f6LinkerPolicy"), Path.Combine(root, "linker-policy.json"));
                ProjectSetup.CreateBootstrap();
                const string scenePath = "Assets/Tests/Generated/F6AotProbe.unity";
                Directory.CreateDirectory(Path.GetDirectoryName(scenePath));
                var scene = EditorSceneManager.OpenScene("Assets/Scenes/Bootstrap.unity");
                var type = Type.GetType("Client.Framework.F6Probe.RuntimeProbe, Client.Bootstrap");
                if (type == null) throw new BuildFailedException("Isolated F6 runtime harness is missing.");
                new GameObject("F6 Release IL2CPP Probe").AddComponent(type);
                EditorSceneManager.SaveScene(scene, scenePath);
                EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(scenePath, true) };
                string baseline = Path.GetFullPath("Assets/StreamingAssets/F6Baseline");
                BuildNativePackage(baseline);
                GenerateStableInputs();
                string compiled = SettingsUtil.GetHotUpdateDllsOutputDirByTarget(BuildTarget.StandaloneWindows64);
                HotfixCompatibilityCheck.RecordCompileInputs(compiled);
                string archiveRoot = Path.Combine(root, "Archive");
                var archive = PlayerBaselineArchive.Stage(archiveRoot, BuildTarget.StandaloneWindows64, compiled, baseline);
                File.WriteAllText("Assets/StreamingAssets/F6PlayerIdentity.json", JsonConvert.SerializeObject(archive, Formatting.Indented) + "\n");
                WriteSignedBaseline(baseline, archive);
                AssetDatabase.Refresh();
                string player = Path.Combine(root, "Player");
                string exe = Path.Combine(player, "Client.exe");
                var build = BuildPipeline.BuildPlayer(new BuildPlayerOptions { scenes = new[] { scenePath }, target = BuildTarget.StandaloneWindows64, locationPathName = exe, options = BuildOptions.None });
                File.WriteAllText(Path.Combine(root, "build-result.json"), SmokeBuild.CreateResultJson("Windows64", exe, build.summary.result, build.summary.totalErrors, build.summary.totalWarnings, build.summary.totalSize) + "\n");
                if (build.summary.result != BuildResult.Succeeded || build.summary.totalErrors != 0) throw new BuildFailedException("F6 Release IL2CPP Player build failed.");
                // Signatures are outside payload inventory; compare the payload exactly as packaged.
                File.Delete(Path.Combine(baseline, "release.json")); File.Delete(Path.Combine(baseline, "release.sig"));
                PlayerBaselineArchive.Finalize(archiveRoot, baseline, player, BuildTarget.StandaloneWindows64);
                var check = HotfixCompatibilityCheck.Check(archiveRoot, compiled);
                File.WriteAllText(Path.Combine(root, "compatibility.json"), JsonConvert.SerializeObject(check, Formatting.Indented) + "\n");
                if (check.RequiresNewPlayer) throw new BuildFailedException("The native probe's actual hotfix failed official metadata checks.");
                Debug.Log("F6_NATIVE_BUILD_PASS " + archive.playerBuildId);
            }
            catch (Exception error) { Debug.LogException(error); EditorApplication.Exit(1); }
            finally
            {
                if (linkerPolicy != null) { linkerPolicy.Dispose(); linkerPolicy = null; }
                root = null;
            }
        }
        private static void FullReleaseFlags() { EditorUserBuildSettings.development = false; EditorUserBuildSettings.buildScriptsOnly = false; EditorUserBuildSettings.exportAsGoogleAndroidProject = false; UnityEditor.WindowsStandalone.UserBuildSettings.createSolution = false; }
        private static void GenerateStableInputs()
        {
            var observations = new JArray();
            string previous = null;
            for (int pass = 1; pass <= 3; pass++)
            {
                string evidence = Path.Combine(root, "Generation", "pass" + pass);
                foreach (string file in new[] { "Assets/Scripts/Framework/HotUpdate/Generated/link.xml", "Assets/Scripts/Framework/HotUpdate/Generated/AOTGenericReferences.cs" })
                    SaveGenerationFile(Path.Combine(evidence, "Before", Path.GetFileName(file)), file);
                FullReleaseFlags();
                PrebuildCommand.GenerateAll();
                FullReleaseFlags();
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
                var generated = new JArray();
                foreach (string file in new[] { "Assets/Scripts/Framework/HotUpdate/Generated/link.xml", "Assets/Scripts/Framework/HotUpdate/Generated/AOTGenericReferences.cs" })
                    generated.Add(new JObject { ["path"] = file, ["sha256"] = HotfixLoadPlan.Hash(File.ReadAllBytes(file)) });
                foreach (var file in PlayerBaselineArchive.Inventory(SettingsUtil.GeneratedCppDir)) generated.Add(JObject.FromObject(file));
                var compiled = new JArray(PlayerBaselineArchive.Inventory(SettingsUtil.GetHotUpdateDllsOutputDirByTarget(BuildTarget.StandaloneWindows64)).Where(f => f.path.EndsWith(".dll", StringComparison.Ordinal)).Select(JObject.FromObject));
                var metadata = new JArray(PlayerBaselineArchive.Inventory(SettingsUtil.GetAssembliesPostIl2CppStripDir(BuildTarget.StandaloneWindows64)).Select(JObject.FromObject));
                var snapshot = new JObject { ["generated"] = generated, ["compiled"] = compiled, ["metadata"] = metadata };
                foreach (string file in new[] { "Assets/Scripts/Framework/HotUpdate/Generated/link.xml", "Assets/Scripts/Framework/HotUpdate/Generated/AOTGenericReferences.cs" })
                    SaveGenerationFile(Path.Combine(evidence, "After", Path.GetFileName(file)), file);
                foreach (string file in Directory.GetFiles(SettingsUtil.GeneratedCppDir)) SaveGenerationFile(Path.Combine(evidence, "Bridge", Path.GetFileName(file)), file);
                foreach (string file in Directory.GetFiles(SettingsUtil.GetHotUpdateDllsOutputDirByTarget(BuildTarget.StandaloneWindows64), "*.dll")) SaveGenerationFile(Path.Combine(evidence, "Compiled", Path.GetFileName(file)), file);
                foreach (string file in Directory.GetFiles(SettingsUtil.GetAssembliesPostIl2CppStripDir(BuildTarget.StandaloneWindows64), "*.dll")) SaveGenerationFile(Path.Combine(evidence, "Metadata", Path.GetFileName(file)), file);
                string digest = HotfixLoadPlan.Hash(System.Text.Encoding.UTF8.GetBytes(snapshot.ToString(Formatting.None)));
                observations.Add(new JObject { ["pass"] = pass, ["sha256"] = digest, ["snapshot"] = snapshot });
                File.WriteAllText(Path.Combine(root, "generation-passes.json"), observations.ToString(Formatting.Indented) + "\n");
                if (pass >= 2 && digest == previous) return;
                previous = digest;
            }
            throw new BuildFailedException("Official generation/compiled inputs/post-strip bytes did not stabilize within three passes; no baseline or final Player may be published.");
        }
        private static void SaveGenerationFile(string target, string source) { Directory.CreateDirectory(Path.GetDirectoryName(target)); File.Copy(source, target); }
        private static void BuildNativePackage(string baseline)
        {
            const string asset = "Assets/Tests/Generated/F6Package/marker.txt";
            Directory.CreateDirectory(Path.GetDirectoryName(asset)); File.WriteAllText(asset, "F6 native GF Package\n"); AssetDatabase.ImportAsset(asset, ImportAssetOptions.ForceUpdate);
            var collection = new ResourceCollection();
            if (!collection.AddResource("f6/marker", null, null, LoadType.LoadFromFile, true) || !collection.AssignAsset(AssetDatabase.AssetPathToGUID(asset), "f6/marker", null) || !collection.Save()) throw new BuildFailedException("Cannot create native GF collection.");
            var builder = new ResourceBuilderController { Platforms = Platform.Windows64, InternalResourceVersion = 1, OutputDirectory = Path.Combine(root, "NativePackage"), OutputPackageSelected = true, OutputFullSelected = false, OutputPackedSelected = false, ForceRebuildAssetBundleSelected = true };
            Directory.CreateDirectory(builder.OutputDirectory);
            if (!builder.BuildResources()) throw new BuildFailedException("Native GF Package build failed.");
            string source = Path.GetFullPath(Path.Combine(builder.OutputPackagePath, "Windows64"));
            foreach (string file in Directory.GetFiles(source, "*", SearchOption.AllDirectories)) { string output = Path.Combine(baseline, "Resources", file.Substring(source.Length + 1)); Directory.CreateDirectory(Path.GetDirectoryName(output)); File.Copy(file, output); }
        }
        private static void WriteSignedBaseline(string baseline, PlayerBaselineArchive.Archive archive)
        {
            string compiled = SettingsUtil.GetHotUpdateDllsOutputDirByTarget(BuildTarget.StandaloneWindows64);
            string[] names = { "Client.Game.Protocol", "Client.Game" };
            var manifest = new JObject { ["schemaVersion"] = 1, ["keyId"] = "test-f6-native", ["releaseId"] = "f6-baseline", ["player"] = JObject.FromObject(new {
                playerBuildId = archive.playerBuildId, platform = archive.platform, architecture = archive.architecture, unityVersion = archive.unityVersion, contractVersion = archive.contractVersion, envelopeSha256 = archive.envelopeSha256, aotMetadataSetId = archive.aotMetadataSetId }),
                ["protocolSha256"] = (string)JObject.Parse(File.ReadAllText("Tests/Fixtures/HotfixAotProbe/Generated/Pristine/protocol-manifest.json"))["schema_sha256"], ["resourceVersionListPath"] = "Resources/GameFrameworkVersion.dat",
                ["files"] = new JArray(archive.baseline.Select(f => new JObject { ["path"] = f.path, ["length"] = f.length, ["sha256"] = f.sha256, ["kind"] = f.path.StartsWith("Metadata/") ? "AotMetadata" : f.path.StartsWith("Hotfix/") ? "HotfixDll" : f.path.StartsWith("References/") ? "Configuration" : "NativeResource" })),
                ["assemblies"] = new JArray(names.Select(name => { using (var module = dnlib.DotNet.ModuleDefMD.Load(File.ReadAllBytes(Path.Combine(compiled, name + ".dll")))) return new JObject { ["name"] = name, ["path"] = "Hotfix/" + name + ".dll", ["dependencies"] = new JArray(module.GetAssemblyRefs().Select(a => a.Name.String)) }; })), ["assemblyOrder"] = new JArray(names) };
            byte[] bytes = System.Text.Encoding.UTF8.GetBytes(manifest.ToString(Formatting.None));
            using (RSA key = RSA.Create()) { key.FromXmlString(File.ReadAllText(Arg("-f6Key"))); File.WriteAllBytes(Path.Combine(baseline, "release.json"), bytes); File.WriteAllBytes(Path.Combine(baseline, "release.sig"), key.SignData(bytes, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1)); }
        }
        private static string Arg(string name) { string[] args = Environment.GetCommandLineArgs(); int i = Array.IndexOf(args, name); if (i < 0 || i + 1 >= args.Length) throw new ArgumentException(name); return args[i + 1]; }
    }
}
