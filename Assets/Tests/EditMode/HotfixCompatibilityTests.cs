using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Client.Editor.Build;
using Client.Framework.HotUpdate;
using dnlib.DotNet;
using dnlib.DotNet.Emit;
using Newtonsoft.Json;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace Client.Tests.EditMode
{
    [Category("F6")]
    public sealed class HotfixCompatibilityTests
    {
        private string root;
        [OneTimeSetUp]
        public void BuildCurrentEditorLinkerPolicyPrerequisite()
        {
            string project = Path.GetFullPath(".");
            string editor = UnityEditor.EditorApplication.applicationPath;
            string script = Path.Combine(project, "Tool/Client/Build-LinkerPolicy.ps1");
            string evidence = Path.Combine(project, "Builds/Tests/F6Prerequisite", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(evidence);
            string policy = Path.Combine(project, "Builds/LinkerPolicy/policy.json");
            bool existed = File.Exists(policy);
            string arguments = "-NoLogo -NoProfile -NonInteractive -File " + QuoteProcessArgument(script) + " -UnityEditor " + QuoteProcessArgument(editor);
            var start = new System.Diagnostics.ProcessStartInfo("pwsh", arguments) {
                WorkingDirectory = project, UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true, RedirectStandardError = true
            };
            using (var process = System.Diagnostics.Process.Start(start))
            {
                var output = process.StandardOutput.ReadToEndAsync();
                var error = process.StandardError.ReadToEndAsync();
                bool completed = process.WaitForExit(60000);
                if (!completed) { process.Kill(); Assert.That(process.WaitForExit(5000), Is.True, "Timed-out linker prerequisite did not terminate."); }
                bool drained = System.Threading.Tasks.Task.WaitAll(new System.Threading.Tasks.Task[] { output, error }, 5000);
                string stdout = output.IsCompleted ? output.GetAwaiter().GetResult() : "<output drain timed out>";
                string stderr = error.IsCompleted ? error.GetAwaiter().GetResult() : "<error drain timed out>";
                File.WriteAllText(Path.Combine(evidence, "stdout.log"), stdout);
                File.WriteAllText(Path.Combine(evidence, "stderr.log"), stderr);
                File.WriteAllText(Path.Combine(evidence, "result.json"), JsonConvert.SerializeObject(new {
                    editor, executable = start.FileName, arguments, timeoutMilliseconds = 60000,
                    policyExistedBefore = existed, completed, drained, exitCode = process.ExitCode,
                    policyExistsAfter = File.Exists(policy)
                }, Formatting.Indented) + "\n");
                TestContext.Progress.WriteLine("F6_LINKER_PREREQUISITE " + evidence);
                Assert.That(completed && drained, Is.True, "Linker prerequisite timed out. " + stderr);
                Assert.That(process.ExitCode, Is.Zero, stdout + "\n" + stderr);
                Assert.That(stdout, Does.Contain("LINKER_POLICY_BUILT "));
                Assert.That(File.Exists(policy), Is.True, "The actual producer did not create its policy evidence.");
            }
        }
        private static string QuoteProcessArgument(string value)
        {
            if (value.IndexOfAny(new[] { '"', '\r', '\n' }) >= 0) throw new ArgumentException("Invalid prerequisite process path.");
            return "\"" + value + "\"";
        }
        [SetUp] public void Setup() { root = Path.GetFullPath(Path.Combine("Builds/F6Compatibility", Guid.NewGuid().ToString("N"))); Directory.CreateDirectory(root); }
        [TearDown] public void Cleanup() { Directory.Delete(root, true); }
        [Test]
        public void LinkerSymbolPolicyRestoresProcessLocalArguments()
        {
            string before = ReleaseLinkerSymbolPolicy.Arguments;
            using (ReleaseLinkerSymbolPolicy.Apply("Builds/LinkerPolicy/policy.json"))
            {
                Assert.That(ReleaseLinkerSymbolPolicy.Arguments, Does.Contain("--custom-step=-UnityOutputStep:Client.Build.Linker.DisableSymbolsStep,"));
                Assert.That(ReleaseLinkerSymbolPolicy.Fingerprint, Is.Not.Null);
            }
            Assert.That(ReleaseLinkerSymbolPolicy.Arguments, Is.EqualTo(before));
            Assert.That(ReleaseLinkerSymbolPolicy.Fingerprint, Is.Null);
        }
        [Test]
        public void ProbeRegistryAdaptsOnlyNamespaceAndPreservesGeneratedMessageBytes()
        {
            const string generated = "Tests/Fixtures/HotfixAotProbe/Generated/";
            var provenance = Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText(generated + "probe-provenance.json"));
            var manifest = Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText(generated + "Pristine/protocol-manifest.json"));
            string pristine = File.ReadAllText(generated + "Pristine/GameMessageRegistry.cs");
            string oldName = (string)provenance["oldNamespace"], newName = (string)provenance["newNamespace"];
            Assert.That(pristine.Split(new[] { oldName }, StringSplitOptions.None).Length - 1, Is.EqualTo(1));
            Assert.That(File.ReadAllText(generated + "AdaptedRegistry.cs.txt"), Is.EqualTo(pristine.Replace(oldName, newName)));
            Assert.That(HotfixLoadPlan.Hash(File.ReadAllBytes(generated + "Pristine/AotProbe.cs")), Is.EqualTo((string)manifest["generated_sha256"]["AotProbe.cs"]));
            Assert.That(HotfixLoadPlan.Hash(File.ReadAllBytes(generated + "AdaptedRegistry.cs.txt")), Is.EqualTo((string)provenance["adaptedRegistrySha256"]));
            Assert.That(HotfixLoadPlan.Hash(File.ReadAllBytes(generated + "Pristine/GameMessageRegistry.cs")), Is.EqualTo((string)provenance["pristineRegistrySha256"]));
            Assert.That(HotfixLoadPlan.Hash(File.ReadAllBytes(generated + "Pristine/protocol-manifest.json")), Is.EqualTo((string)provenance["pristineManifestSha256"]));
        }
        [Test]
        public void InventoryExcludesUnityImporterSidecars()
        {
            File.WriteAllText(Path.Combine(root, "native.bin"), "payload");
            File.WriteAllText(Path.Combine(root, "native.bin.meta"), "Unity importer sidecar");
            Assert.That(PlayerBaselineArchive.Inventory(root).Select(f => f.path), Is.EqualTo(new[] { "native.bin" }));
        }
        [TestCase(false, "")] [TestCase(true, "")] [TestCase(false, "version")] [TestCase(false, "library")] [TestCase(false, "missing")] [TestCase(false, "forwarder")]
        public void MissingAotApiRequiresNewPlayer(bool missing, string mutation)
        {
            string archive = Path.Combine(root, "Archive"), metadata = Path.Combine(archive, "Metadata"), hot = Path.Combine(root, "Hotfix");
            Directory.CreateDirectory(metadata); Directory.CreateDirectory(hot);
            File.Copy(typeof(object).Assembly.Location, Path.Combine(metadata, "mscorlib.dll"));
            using (var aot = Module("Probe.Aot"))
            {
                var type = new TypeDefUser("Probe", "Api", aot.CorLibTypes.Object.TypeDefOrRef) { Attributes = TypeAttributes.Public };
                aot.Types.Add(type);
                var method = new MethodDefUser("Preserved", MethodSig.CreateStatic(aot.CorLibTypes.Void), MethodImplAttributes.IL, MethodAttributes.Public | MethodAttributes.Static) { Body = new CilBody() };
                method.Body.Instructions.Add(OpCodes.Ret.ToInstruction()); type.Methods.Add(method); aot.Write(Path.Combine(metadata, "Probe.Aot.dll"));
            }
            using (var protocol = Module("Client.Game.Protocol"))
            {
                var type = new TypeDefUser("Probe", "Protocol", protocol.CorLibTypes.Object.TypeDefOrRef) { Attributes = TypeAttributes.Public };
                protocol.Types.Add(type);
                var api = new TypeRefUser(protocol, "Probe", "Api", new AssemblyRefUser(new AssemblyNameInfo("Probe.Aot, Version=" + (mutation == "version" ? "2.0.0.0" : "1.0.0.0"))));
                var member = new MemberRefUser(protocol, missing ? "NewUnreservedApi" : "Preserved", MethodSig.CreateStatic(protocol.CorLibTypes.Void), api);
                var call = new MethodDefUser("Run", MethodSig.CreateStatic(protocol.CorLibTypes.Void), MethodImplAttributes.IL, MethodAttributes.Public | MethodAttributes.Static) { Body = new CilBody() };
                if (mutation == "forwarder") member = new MemberRefUser(protocol, "F6MissingBclMethod", MethodSig.CreateStatic(protocol.CorLibTypes.Void),
                    new TypeRefUser(protocol, "System", "GC", new AssemblyRefUser(new AssemblyNameInfo("netstandard, Version=2.1.0.0, Culture=neutral, PublicKeyToken=cc7b13ffcd2ddd51"))));
                call.Body.Instructions.Add(OpCodes.Call.ToInstruction(member)); call.Body.Instructions.Add(OpCodes.Ret.ToInstruction()); type.Methods.Add(call);
                protocol.Write(Path.Combine(hot, "Client.Game.Protocol.dll"));
            }
            using (var game = Module("Client.Game")) game.Write(Path.Combine(hot, "Client.Game.dll"));
            foreach (string name in new[] { "Client.Contracts", "Client.Framework", "Client.Protocol.Envelope", "HybridCLR.Runtime" })
                File.Copy("Library/ScriptAssemblies/" + name + ".dll", Path.Combine(hot, name + ".dll"));
            foreach (string name in new[] { "Google.Protobuf", "System.Runtime.CompilerServices.Unsafe" })
                File.Copy("Assets/ThirdParty/Protobuf/" + name + ".dll", Path.Combine(hot, name + ".dll"));
            File.Copy(Path.Combine(UnityEditor.EditorApplication.applicationContentsPath, "NetStandard/ref/2.1.0/netstandard.dll"), Path.Combine(hot, "netstandard.dll"));
            Directory.CreateDirectory(Path.Combine(archive, "Facades"));
            File.Copy("Packages/com.code-philosophy.hybridclr/Data~/NetStandard/netstandard2.1.dll", Path.Combine(archive, "Facades/netstandard.dll"));
            var evidence = new { schemaVersion = 1, compatibilityInputsSha256 = PlayerBaselineArchive.CompatibilityInputsHash(), references = PlayerBaselineArchive.Inventory(hot).Where(f => f.path != "Client.Game.Protocol.dll" && f.path != "Client.Game.dll").ToArray() };
            File.WriteAllText(Path.Combine(hot, "compile-inputs.json"), JsonConvert.SerializeObject(evidence));
            Directory.CreateDirectory(Path.Combine(archive, "Compiled"));
            foreach (string file in Directory.GetFiles(hot)) File.Copy(file, Path.Combine(archive, "Compiled", Path.GetFileName(file)));
            foreach (string input in PlayerBaselineArchive.CompatibilityInputFiles())
            { string target = Path.Combine(archive, "Inputs", input); Directory.CreateDirectory(Path.GetDirectoryName(target)); File.Copy(input, target); }
            Directory.CreateDirectory(Path.Combine(archive, "Baseline")); Directory.CreateDirectory(Path.Combine(archive, "Player"));
            File.WriteAllText(Path.Combine(archive, "Baseline/index.dat"), "fixture"); File.WriteAllText(Path.Combine(archive, "Player/native.exe"), "fixture");
            var record = new PlayerBaselineArchive.Archive { playerBuildId = "checker-fixture", platform = "StandaloneWindows64", architecture = "x64", unityVersion = "2022.3.25f1", contractVersion = "1", envelopeSha256 = new string('a', 64),
                aotMetadataSetId = HotfixLoadPlan.MetadataSetId(Directory.GetFiles(metadata).Select(p => new KeyValuePair<string, byte[]>(Path.GetFileName(p), File.ReadAllBytes(p)))),
                compatibilityInputsSha256 = PlayerBaselineArchive.CompatibilityInputsHash(), buildProfile = PlayerBaselineArchive.CurrentBuildProfile(), metadata = PlayerBaselineArchive.Inventory(metadata),
                baseline = PlayerBaselineArchive.Inventory(Path.Combine(archive, "Baseline")), player = PlayerBaselineArchive.Inventory(Path.Combine(archive, "Player")),
                inputs = PlayerBaselineArchive.Inventory(Path.Combine(archive, "Inputs")), compiled = PlayerBaselineArchive.Inventory(Path.Combine(archive, "Compiled")), facades = PlayerBaselineArchive.Inventory(Path.Combine(archive, "Facades")) };
            File.WriteAllText(Path.Combine(archive, "archive.json"), JsonConvert.SerializeObject(record));
            if (mutation == "library") File.AppendAllText(Path.Combine(hot, "Google.Protobuf.dll"), "changed same-version compile input");
            if (mutation == "missing") File.Delete(Path.Combine(hot, "compile-inputs.json"));
            LogAssert.ignoreFailingMessages = true;
            CompatibilityReport result;
            try { result = HotfixCompatibilityCheck.Check(archive, hot); }
            finally { LogAssert.ignoreFailingMessages = false; }
            Assert.That(result.RequiresNewPlayer, Is.EqualTo(missing || mutation != ""), string.Join("\n", result.MissingAotReferences));
            if (mutation == "forwarder") Assert.That(result.MissingAotReferences.Any(s => s.Contains("F6MissingBclMethod")), Is.True, "A reference stub cannot prove a BCL method exists; resolve through the pure forwarder into original AOT.");
            if (missing) Assert.That(result.MissingAotReferences.Any(s => s.Contains("NewUnreservedApi")), Is.True, "The official MissingMetadataChecker must identify the missing real method reference.");
        }
        private static ModuleDefUser Module(string name)
        { var assembly = new AssemblyDefUser(name, new Version(1, 0, 0, 0)); var module = new ModuleDefUser(name + ".dll") { Kind = ModuleKind.Dll }; module.CorLibTypes.AssemblyRef.Version = new Version(4, 0, 0, 0); assembly.Modules.Add(module); return module; }
    }
}
