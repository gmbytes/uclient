using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Client.Framework.HotUpdate;
using dnlib.DotNet;
using HybridCLR.HotUpdate;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Client.Editor.Build
{
    public sealed class CompatibilityReport
    {
        public bool RequiresNewPlayer { get; internal set; }
        public IReadOnlyList<string> MissingAotReferences { get; internal set; }
        public IReadOnlyList<string> AssemblyOrder { get; internal set; }
        public IReadOnlyDictionary<string, IReadOnlyList<string>> Dependencies { get; internal set; }
    }
    public static class HotfixCompatibilityCheck
    {
        private static readonly string[] FixedReferences = { "Client.Contracts.dll", "Client.Framework.dll", "Client.Protocol.Envelope.dll", "HybridCLR.Runtime.dll", "Google.Protobuf.dll", "System.Runtime.CompilerServices.Unsafe.dll", "netstandard.dll" };
        public sealed class CompileInputs
        {
            public int schemaVersion = 1;
            public string compatibilityInputsSha256;
            public PlayerBaselineArchive.FileRecord[] references;
        }
        // Invoke only after the official compiler, with the same fixed references it consumed.
        // Check never fills missing evidence from current project files.
        public static void RecordCompileInputs(string compiledHotfixRoot)
        {
            foreach (string name in new[] { "Google.Protobuf.dll", "System.Runtime.CompilerServices.Unsafe.dll" })
            {
                string source = Path.Combine("Assets/ThirdParty/Protobuf", name), output = Path.Combine(compiledHotfixRoot, name);
                if (File.Exists(output) && HotfixLoadPlan.Hash(File.ReadAllBytes(output)) != HotfixLoadPlan.Hash(File.ReadAllBytes(source)))
                    throw new InvalidDataException("Compiler reference differs from the installed fixed runtime: " + name);
                if (!File.Exists(output)) File.Copy(source, output);
            }
            string compilerReference = Path.Combine(UnityEditor.EditorApplication.applicationContentsPath, "NetStandard/ref/2.1.0/netstandard.dll");
            string originalReference = Path.Combine(compiledHotfixRoot, "netstandard.dll");
            if (File.Exists(originalReference) && HotfixLoadPlan.Hash(File.ReadAllBytes(originalReference)) != HotfixLoadPlan.Hash(File.ReadAllBytes(compilerReference)))
                throw new InvalidDataException("The actual netstandard compiler reference changed.");
            if (!File.Exists(originalReference)) File.Copy(compilerReference, originalReference);
            var records = FixedReferences.Select(name => { string path = Path.Combine(compiledHotfixRoot, name); byte[] bytes = File.ReadAllBytes(path);
                return new PlayerBaselineArchive.FileRecord { path = name, length = bytes.LongLength, sha256 = HotfixLoadPlan.Hash(bytes) }; }).ToArray();
            File.WriteAllText(Path.Combine(compiledHotfixRoot, "compile-inputs.json"), JsonConvert.SerializeObject(new CompileInputs {
                compatibilityInputsSha256 = PlayerBaselineArchive.CompatibilityInputsHash(), references = records }, Formatting.Indented) + "\n");
        }
        public static CompatibilityReport Check(string playerArchive, string compiledHotfixRoot)
        {
            var archive = PlayerBaselineArchive.Open(playerArchive);
            string[] reserved = { "Client.Game.Protocol", "Client.Game" };
            var failures = new List<string>();
            if (archive.buildProfile != PlayerBaselineArchive.CurrentBuildProfile()) failures.Add("The original Player linker/toolchain build profile changed.");
            if (archive.compatibilityInputsSha256 != PlayerBaselineArchive.CompatibilityInputsHash()) failures.Add("Stable contracts, Envelope, runtime dependencies, toolchain or trimming configuration changed.");
            CheckCompileInputs(archive, compiledHotfixRoot, failures);
            string metadata = Path.GetFullPath(Path.Combine("Builds/CompatibilityCheck", Guid.NewGuid().ToString("N")));
            Directory.CreateDirectory(metadata);
            foreach (string file in Directory.GetFiles(Path.Combine(playerArchive, "Metadata"), "*.dll")) File.Copy(file, Path.Combine(metadata, Path.GetFileName(file)));
            // The pure forwarder only resolves names into the actual original AOT modules.
            // The compiler reference stub is never used as implementation/API evidence.
            foreach (string file in Directory.GetFiles(Path.Combine(playerArchive, "Facades"), "*.dll")) File.Copy(file, Path.Combine(metadata, Path.GetFileName(file)));
            var checker = new MissingMetadataChecker(metadata, reserved);
            var aotIdentities = new Dictionary<string, Tuple<Version, PublicKeyToken>>(StringComparer.Ordinal);
            foreach (string aot in Directory.GetFiles(metadata, "*.dll"))
                using (var module = ModuleDefMD.Load(File.ReadAllBytes(aot))) aotIdentities.Add(module.Assembly.Name.String, Tuple.Create(module.Assembly.Version, module.Assembly.PublicKeyToken));
            var dependencies = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
            var complete = new HashSet<string>(Directory.GetFiles(metadata, "*.dll").Select(Path.GetFileNameWithoutExtension), StringComparer.Ordinal);
            foreach (string name in reserved)
            {
                string path = Path.Combine(compiledHotfixRoot, name + ".dll");
                if (!File.Exists(path)) { failures.Add("Missing reserved DLL: " + name); continue; }
                using (var module = ModuleDefMD.Load(File.ReadAllBytes(path)))
                {
                    if (module.Assembly.Name != name) failures.Add("Assembly filename/name mismatch: " + name);
                    string[] refs = module.GetAssemblyRefs().Select(a => a.Name.String).OrderBy(n => n, StringComparer.Ordinal).ToArray();
                    dependencies.Add(name, Array.AsReadOnly(refs));
                    foreach (string dependency in refs.Where(d => !complete.Contains(d))) failures.Add("Missing, cyclic or out-of-order dependency: " + name + " -> " + dependency);
                    foreach (var reference in module.GetAssemblyRefs())
                        if (aotIdentities.TryGetValue(reference.Name.String, out var original) &&
                            (reference.Version != original.Item1 || !PublicKeyBase.TokenEquals(reference.PublicKeyOrToken, original.Item2)))
                            failures.Add("AOT AssemblyRef version/public-key token changed: " + reference.FullName);
                }
                Application.LogCallback capture = (message, stack, type) => { if (type == LogType.Error && message.StartsWith("Missing ", StringComparison.Ordinal)) failures.Add(message); };
                Application.logMessageReceived += capture;
                try { if (!checker.Check(path)) failures.Add("Official HybridCLR metadata check failed: " + name); }
                finally { Application.logMessageReceived -= capture; }
                complete.Add(name);
            }
            foreach (string file in Directory.GetFiles(compiledHotfixRoot, "Client.Game*.dll"))
                if (!reserved.Contains(Path.GetFileNameWithoutExtension(file))) failures.Add("Unreserved hotfix assembly: " + Path.GetFileName(file));
            return new CompatibilityReport { RequiresNewPlayer = failures.Count != 0, MissingAotReferences = failures.AsReadOnly(),
                AssemblyOrder = Array.AsReadOnly(reserved), Dependencies = dependencies };
        }
        private static void CheckCompileInputs(PlayerBaselineArchive.Archive archive, string compiledRoot, List<string> failures)
        {
            string path = Path.Combine(compiledRoot, "compile-inputs.json");
            try
            {
                if (!File.Exists(path) || new FileInfo(path).Length > 256 * 1024) throw new InvalidDataException("Missing or oversized compile-input evidence.");
                var json = JObject.Parse(File.ReadAllText(path), new JsonLoadSettings { DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error });
                var inputs = json.ToObject<CompileInputs>();
                if (inputs == null || inputs.schemaVersion != 1 || inputs.compatibilityInputsSha256 != archive.compatibilityInputsSha256 || inputs.references == null || inputs.references.Any(r => r == null) ||
                    inputs.references.Length != FixedReferences.Length || !new HashSet<string>(inputs.references.Select(r => r.path), StringComparer.Ordinal).SetEquals(FixedReferences))
                    throw new InvalidDataException("Incomplete or incompatible compile-input evidence.");
                foreach (var reference in inputs.references)
                {
                    var original = archive.compiled.SingleOrDefault(f => f.path == reference.path);
                    string file = Path.Combine(compiledRoot, reference.path);
                    if (original == null || original.sha256 != reference.sha256 || original.length != reference.length ||
                        !File.Exists(file) || new FileInfo(file).Length != reference.length || HotfixLoadPlan.Hash(File.ReadAllBytes(file)) != reference.sha256)
                        throw new InvalidDataException("Fixed compiler reference changed or is missing: " + reference.path);
                }
            }
            catch (Exception error) when (error is IOException || error is InvalidDataException || error is JsonException || error is ArgumentException || error is InvalidOperationException)
            { failures.Add("Requires original fixed compiler inputs: " + error.Message); }
        }
    }
}
