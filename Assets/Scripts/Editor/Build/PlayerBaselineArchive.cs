using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Client.Framework.HotUpdate;
using Client.Framework.Release;
using HybridCLR;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

namespace Client.Editor.Build
{
    public static class PlayerBaselineArchive
    {
        [Serializable] public sealed class FileRecord { public string path; public long length; public string sha256; }
        [Serializable] public sealed class Archive
        {
            public int schemaVersion = 1;
            public string playerBuildId, platform, architecture, unityVersion, contractVersion, envelopeSha256, aotMetadataSetId;
            public string compatibilityInputsSha256, originalMetadataSource;
            public string buildProfile, linkerArguments;
            public FileRecord[] metadata, baseline, player, compiled, inputs, facades;
            public PlayerIdentity Identity => new PlayerIdentity(playerBuildId, platform, architecture, unityVersion, contractVersion, envelopeSha256, aotMetadataSetId);
        }
        // Called after official GenerateAll; D1 owns orchestration and signing.
        public static Archive Stage(string archiveRoot, BuildTarget target, string compiledHotfixRoot, string baselineRoot)
        {
            if (Directory.Exists(archiveRoot) || File.Exists(archiveRoot)) throw new IOException("Player archives are immutable; choose a new output path.");
            if (target != BuildTarget.StandaloneWindows64 && target != BuildTarget.Android) throw new BuildFailedException("Unsupported initial platform.");
            if (!File.Exists(Path.Combine(baselineRoot, "Resources/GameFrameworkVersion.dat"))) throw new BuildFailedException("A native GF Package baseline is required.");
            if (!File.Exists(Path.Combine(compiledHotfixRoot, "compile-inputs.json"))) throw new BuildFailedException("Record the actual fixed compile inputs before staging a Player baseline.");
            string source = SettingsUtil.GetAssembliesPostIl2CppStripDir(target);
            var bytes = Directory.GetFiles(source, "*.dll").OrderBy(p => p, StringComparer.Ordinal).Select(p => new KeyValuePair<string, byte[]>(Path.GetFileName(p), File.ReadAllBytes(p))).ToArray();
            if (bytes.Any(p => p.Key.StartsWith("Client.Game", StringComparison.Ordinal))) throw new BuildFailedException("Hotfix assemblies must not be AOT.");
            Directory.CreateDirectory(archiveRoot);
            const string facadeSource = "Packages/com.code-philosophy.hybridclr/Data~/NetStandard/netstandard2.1.dll";
            byte[] facade = File.ReadAllBytes(facadeSource);
            if (HotfixLoadPlan.Hash(facade) != HotfixLoadPlan.NetstandardFacadeSha256) throw new BuildFailedException("The locked native forwarding facade changed.");
            Put(Path.Combine(archiveRoot, "Facades/netstandard.dll"), facade);
            Put(Path.Combine(baselineRoot, HotfixLoadPlan.NetstandardFacadePath), facade);
            CopyTree(compiledHotfixRoot, Path.Combine(archiveRoot, "Compiled"));
            foreach (string input in CompatibilityInputFiles()) Put(Path.Combine(archiveRoot, "Inputs", input), File.ReadAllBytes(input));
            foreach (var pair in bytes) { Put(Path.Combine(archiveRoot, "Metadata", pair.Key), pair.Value); Put(Path.Combine(baselineRoot, "Metadata", pair.Key), pair.Value); }
            foreach (string name in new[] { "Client.Game.Protocol", "Client.Game" })
                Put(Path.Combine(baselineRoot, "Hotfix", name + ".dll"), File.ReadAllBytes(Path.Combine(compiledHotfixRoot, name + ".dll")));
            var provenance = JObject.Parse(File.ReadAllText("Assets/Scripts/Framework/Protocol/Envelope/Generated/source-provenance.json"));
            if ((string)provenance["generated_sha256"] != HotfixLoadPlan.Hash(File.ReadAllBytes("Assets/Scripts/Framework/Protocol/Envelope/Generated/Client.cs")))
                throw new BuildFailedException("Generated Envelope differs from its source provenance.");
            var archive = new Archive {
                playerBuildId = Guid.NewGuid().ToString("N"), platform = target.ToString(), architecture = target == BuildTarget.Android ? "arm64" : "x64",
                unityVersion = Application.unityVersion, contractVersion = "1", envelopeSha256 = (string)provenance["envelope_sha256"],
                aotMetadataSetId = HotfixLoadPlan.MetadataSetId(bytes), originalMetadataSource = Path.GetFullPath(source), compatibilityInputsSha256 = CompatibilityInputsHash(),
                buildProfile = CurrentBuildProfile(),
                linkerArguments = ReleaseLinkerSymbolPolicy.Arguments,
                metadata = Inventory(Path.Combine(archiveRoot, "Metadata")), baseline = Inventory(baselineRoot), compiled = Inventory(compiledHotfixRoot), inputs = Inventory(Path.Combine(archiveRoot, "Inputs")), facades = Inventory(Path.Combine(archiveRoot, "Facades")) };
            Write(Path.Combine(archiveRoot, "pending.json"), archive);
            return archive;
        }
        public static void Finalize(string archiveRoot, string baselineRoot, string playerOutputRoot, BuildTarget target)
        {
            if (File.Exists(Path.Combine(archiveRoot, "archive.json"))) throw new IOException("A finalized Player archive cannot be overwritten.");
            var archive = Read(Path.Combine(archiveRoot, "pending.json"));
            if (archive.platform != target.ToString()) throw new BuildFailedException("Player archive platform mismatch.");
            ValidateRecords(Path.Combine(archiveRoot, "Metadata"), archive.metadata);
            ValidateRecords(Path.Combine(archiveRoot, "Compiled"), archive.compiled);
            ValidateRecords(Path.Combine(archiveRoot, "Inputs"), archive.inputs);
            ValidateRecords(Path.Combine(archiveRoot, "Facades"), archive.facades);
            ValidateRecords(baselineRoot, archive.baseline);
            string final = SettingsUtil.GetAssembliesPostIl2CppStripDir(target);
            if (!Same(archive.metadata, Inventory(final))) throw new BuildFailedException("Final post-strip metadata differs from the packaged baseline. Rebuild baseline and Player; publication is forbidden.");
            if (CompatibilityInputsHash() != archive.compatibilityInputsSha256) throw new BuildFailedException("Build compatibility inputs changed during Player generation.");
            archive.player = Inventory(playerOutputRoot);
            if (archive.player.Length == 0) throw new BuildFailedException("Native Player output is empty.");
            // Keep the actual producer bytes and immutable hashes, rather than only paths to a mutable Library directory.
            CopyTree(playerOutputRoot, Path.Combine(archiveRoot, "Player"));
            CopyTree(baselineRoot, Path.Combine(archiveRoot, "Baseline"));
            Write(Path.Combine(archiveRoot, "archive.json"), archive);
            File.Delete(Path.Combine(archiveRoot, "pending.json"));
        }
        public static Archive Open(string archiveRoot)
        {
            var archive = Read(Path.Combine(archiveRoot, "archive.json"));
            if (archive.schemaVersion != 1 || archive.player == null || archive.player.Length == 0) throw new InvalidDataException("Incomplete Player archive.");
            ValidateRecords(Path.Combine(archiveRoot, "Metadata"), archive.metadata);
            ValidateRecords(Path.Combine(archiveRoot, "Baseline"), archive.baseline);
            ValidateRecords(Path.Combine(archiveRoot, "Player"), archive.player);
            ValidateRecords(Path.Combine(archiveRoot, "Compiled"), archive.compiled);
            ValidateRecords(Path.Combine(archiveRoot, "Inputs"), archive.inputs);
            ValidateRecords(Path.Combine(archiveRoot, "Facades"), archive.facades);
            if (archive.facades.Length != 1 || archive.facades[0].path != "netstandard.dll" || archive.facades[0].sha256 != HotfixLoadPlan.NetstandardFacadeSha256)
                throw new InvalidDataException("The original native forwarding facade is missing or incompatible.");
            if (InputsHash(archive.inputs) != archive.compatibilityInputsSha256) throw new InvalidDataException("Archived dependency locks/trimming inputs differ.");
            var bytes = Directory.GetFiles(Path.Combine(archiveRoot, "Metadata"), "*.dll").Select(p => new KeyValuePair<string, byte[]>(Path.GetFileName(p), File.ReadAllBytes(p)));
            if (HotfixLoadPlan.MetadataSetId(bytes) != archive.aotMetadataSetId) throw new InvalidDataException("Player metadata inventory identity differs.");
            var identity = archive.Identity;
            return archive;
        }
        public static string CompatibilityInputsHash()
            => InputsHash(CompatibilityInputFiles().Select(p => new FileRecord { path = p.Replace('\\', '/'), sha256 = HotfixLoadPlan.Hash(File.ReadAllBytes(p)) }));
        private static string InputsHash(System.Collections.Generic.IEnumerable<FileRecord> inputs)
            => HotfixLoadPlan.Hash(System.Text.Encoding.UTF8.GetBytes(string.Concat(inputs.OrderBy(p => p.path, StringComparer.Ordinal).Select(p => p.path + "\t" + p.sha256 + "\n"))));
        public static string[] CompatibilityInputFiles()
        {
            var paths = new List<string> { "Packages/manifest.json", "Packages/packages-lock.json", "docs/dependencies.lock.json", "ProjectSettings/ProjectVersion.txt", "ProjectSettings/HybridCLRSettings.asset", "ProjectSettings/ProjectSettings.asset", "Assets/ThirdParty/Protobuf/Google.Protobuf.dll", "Assets/ThirdParty/Protobuf/System.Runtime.CompilerServices.Unsafe.dll", "Assets/ThirdParty/Protobuf/dependency-provenance.json", "Assets/Scripts/Framework/Protocol/Envelope/Generated/source-provenance.json" };
            paths.AddRange(Directory.GetFiles("Assets/Scripts/Framework/Contracts", "*", SearchOption.AllDirectories).Where(p => p.EndsWith(".cs") || p.EndsWith(".asmdef")));
            paths.AddRange(Directory.GetFiles("Assets/Scripts/Framework/Protocol/Envelope", "*.cs", SearchOption.AllDirectories));
            paths.AddRange(Directory.GetFiles("Assets/Scripts", "link.xml", SearchOption.AllDirectories));
            paths.Add("Packages/com.code-philosophy.hybridclr/Data~/NetStandard/netstandard2.1.dll");
            paths.Add("Packages/com.code-philosophy.hybridclr/LICENSE");
            paths.Add("docs/licenses/HybridCLR-netstandard-facade-NOTICE.txt");
            return paths.ToArray();
        }
        public static string CurrentBuildProfile()
        {
            if (Application.unityVersion != "2022.3.25f1") throw new BuildFailedException("The verified linker policy is pinned to Unity 2022.3.25f1.");
            string deploy = Path.Combine(SettingsUtil.LocalIl2CppDir, "build/deploy");
            if (!File.Exists(Path.Combine(deploy, "UnityLinker.dll"))) deploy = Path.Combine(EditorApplication.applicationContentsPath, "il2cpp/build/deploy");
            return JsonConvert.SerializeObject(new { unityVersion = Application.unityVersion,
                unityLinkerSha256 = HotfixLoadPlan.Hash(File.ReadAllBytes(Path.Combine(deploy, "UnityLinker.dll"))),
                monoLinkerSha256 = HotfixLoadPlan.Hash(File.ReadAllBytes(Path.Combine(deploy, "monolinker.dll"))),
                linkSymbols = ReleaseLinkerSymbolPolicy.Fingerprint == null, deterministicMvid = true, symbolPolicy = ReleaseLinkerSymbolPolicy.Fingerprint,
                additionalArguments = ReleaseLinkerSymbolPolicy.Fingerprint == null ? ReleaseLinkerSymbolPolicy.Arguments : null,
                development = EditorUserBuildSettings.development });
        }
        public static FileRecord[] Inventory(string root) => Directory.GetFiles(Path.GetFullPath(root), "*", SearchOption.AllDirectories).Where(p => !p.EndsWith(".meta", StringComparison.OrdinalIgnoreCase)).OrderBy(p => p, StringComparer.Ordinal).Select(p => new FileRecord {
            path = p.Substring(Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar).Length + 1).Replace('\\', '/'), length = new FileInfo(p).Length, sha256 = HotfixLoadPlan.Hash(File.ReadAllBytes(p)) }).ToArray();
        private static bool Same(FileRecord[] a, FileRecord[] b) => JsonConvert.SerializeObject(a) == JsonConvert.SerializeObject(b);
        public static void ValidateRecords(string root, FileRecord[] records)
        { if (records == null || !Same(records, Inventory(Path.GetFullPath(root)))) throw new InvalidDataException("Immutable archive content differs: " + root); }
        private static Archive Read(string path) => JsonConvert.DeserializeObject<Archive>(File.ReadAllText(path)) ?? throw new InvalidDataException("Invalid Player archive.");
        private static void Write(string path, Archive archive) => File.WriteAllText(path, JsonConvert.SerializeObject(archive, Formatting.Indented) + "\n");
        private static void Put(string path, byte[] bytes) { Directory.CreateDirectory(Path.GetDirectoryName(path)); File.WriteAllBytes(path, bytes); }
        private static void CopyTree(string source, string target) { foreach (string path in Directory.GetFiles(Path.GetFullPath(source), "*", SearchOption.AllDirectories).Where(p => !p.EndsWith(".meta", StringComparison.OrdinalIgnoreCase))) Put(Path.Combine(target, path.Substring(Path.GetFullPath(source).TrimEnd(Path.DirectorySeparatorChar).Length + 1)), File.ReadAllBytes(path)); }
    }
}
