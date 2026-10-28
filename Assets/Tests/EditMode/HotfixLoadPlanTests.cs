using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using Client.Framework.Release;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using dnlib.DotNet;

namespace Client.Tests.EditMode
{
    [Category("F6")]
    public sealed class HotfixLoadPlanTests
    {
        private string root;
        private RSA key;
        private ManifestVerifier verifier;
        private PlayerIdentity player;
        private Dictionary<string, byte[]> payload;
        private JObject document;
        [SetUp]
        public void Setup()
        {
            root = Path.GetFullPath(Path.Combine("Builds/F6Scratch", Guid.NewGuid().ToString("N")));
            key = new RSACryptoServiceProvider(2048) { PersistKeyInCsp = false };
            verifier = (ManifestVerifier)Activator.CreateInstance(typeof(ManifestVerifier), BindingFlags.Instance | BindingFlags.NonPublic,
                null, new object[] { new Dictionary<string, RSAParameters> { ["test-f6"] = key.ExportParameters(false) } }, null);
            payload = new Dictionary<string, byte[]> {
                ["Resources/index.dat"] = new byte[] { 1 }, ["Hotfix/Client.Game.Protocol.dll"] = Dll("Client.Game.Protocol", "mscorlib"),
                ["Hotfix/Client.Game.dll"] = Dll("Client.Game", "mscorlib", "Client.Game.Protocol"), ["Metadata/mscorlib.dll"] = new byte[] { 4 }
            };
            string metadataId = Hash(Encoding.UTF8.GetBytes("mscorlib.dll\t1\t" + Hash(new byte[] { 4 }) + "\n"));
            player = new PlayerIdentity("player-f6", "StandaloneWindows64", "x64", "2022.3.25f1", "1", new string('a', 64), metadataId);
            document = new JObject { ["schemaVersion"] = 1, ["keyId"] = "test-f6", ["releaseId"] = "f6", ["player"] = new JObject {
                ["playerBuildId"] = player.PlayerBuildId, ["platform"] = player.Platform, ["architecture"] = player.Architecture,
                ["unityVersion"] = player.UnityVersion, ["contractVersion"] = player.ContractVersion,
                ["envelopeSha256"] = player.EnvelopeSha256, ["aotMetadataSetId"] = player.AotMetadataSetId },
                ["protocolSha256"] = new string('b', 64), ["resourceVersionListPath"] = "Resources/index.dat",
                ["files"] = new JArray(payload.Select(p => new JObject { ["path"] = p.Key, ["length"] = p.Value.Length, ["sha256"] = Hash(p.Value),
                    ["kind"] = p.Key.StartsWith("Metadata/") ? "AotMetadata" : p.Key.StartsWith("Hotfix/") ? "HotfixDll" : "NativeResource" })),
                ["assemblies"] = new JArray(Assembly("Client.Game.Protocol", "mscorlib"), Assembly("Client.Game", "mscorlib", "Client.Game.Protocol")),
                ["assemblyOrder"] = new JArray("Client.Game.Protocol", "Client.Game") };
        }
        [TearDown] public void Cleanup() { key.Dispose(); if (Directory.Exists(root)) Directory.Delete(root, true); }
        [Test] public void RejectsMetadataFromAnotherPlatform()
        {
            var prepared = Prepare();
            var other = new PlayerIdentity(player.PlayerBuildId, "Android", "arm64", player.UnityVersion, player.ContractVersion, player.EnvelopeSha256, player.AotMetadataSetId);
            Assert.That(() => Create(prepared, other), Throws.InstanceOf<ReleaseVerificationException>());
        }
        [Test] public void AssemblyOrderIsTopologicalAndComplete()
        {
            object plan = Create(Prepare(), player);
            var values = (System.Collections.IEnumerable)plan.GetType().GetProperty("Assemblies").GetValue(plan);
            Assert.That(values.Cast<object>().Select(a => (string)a.GetType().GetProperty("Name").GetValue(a)), Is.EqualTo(new[] { "Client.Game.Protocol", "Client.Game" }));
        }
        [Test] public void RejectsWrongOrderBeforeAnyLoad() { document["assemblyOrder"] = new JArray("Client.Game", "Client.Game.Protocol"); RejectPlan(); }
        [Test] public void RejectsCycleBeforeAnyLoad() { document["assemblies"][0]["dependencies"] = new JArray("Client.Game"); RejectPlan(); }
        [Test] public void RejectsUnreservedAssemblyBeforeAnyLoad()
        {
            document["assemblies"][1]["name"] = "Client.Other"; document["assemblyOrder"][1] = "Client.Other"; RejectPlan();
        }
        [Test] public void RejectsMissingDllBeforeAnyLoad()
        {
            var prepared = Prepare(); File.Delete(Path.Combine(prepared.Root, "Hotfix/Client.Game.dll"));
            Assert.That(() => Create(prepared, player), Throws.InstanceOf<IOException>());
        }
        [Test] public void RejectsChangedMetadataBeforeAnyLoad()
        {
            var prepared = Prepare(); File.WriteAllBytes(Path.Combine(prepared.Root, "Metadata/mscorlib.dll"), new byte[] { 9 });
            Assert.That(() => Create(prepared, player), Throws.InstanceOf<InvalidDataException>());
        }
        [Test] public void RejectsSignedMetadataSetThatIsNotThePlayerArchive()
        {
            payload["Metadata/mscorlib.dll"] = new byte[] { 8 };
            document["files"][3]["sha256"] = Hash(new byte[] { 8 }); RejectPlan();
        }
        [Test] public void MissingAotApiRequiresNewPlayer()
        {
            Type checker = Type.GetType("Client.Editor.Build.HotfixCompatibilityCheck, Client.Editor");
            Assert.That(checker, Is.Not.Null, "F6 official compatibility gate is missing.");
            Assert.That(checker.GetMethod("Check", new[] { typeof(string), typeof(string) }), Is.Not.Null);
            Assert.That(() => Invoke(checker.GetMethod("Check"), null, root, root), Throws.InstanceOf<IOException>(), "A missing archive must fail closed before a patch can be published.");
        }
        [TestCase("Client.Other", "mscorlib")]
        [TestCase("Client.Game.Protocol", "Client.Game")]
        public void ActualDllIdentityAndReferencesRejectBeforeMetadataOrAssemblyLoad(string actualName, string actualDependency)
        {
            string path = "Hotfix/Client.Game.Protocol.dll";
            byte[] dll = Dll(actualName, actualDependency); payload[path] = dll;
            document["files"][1]["length"] = dll.Length; document["files"][1]["sha256"] = Hash(dll);
            var prepared = Prepare();
            int assemblyLoads = 0, metadataLoads = 0;
            AssemblyLoadEventHandler assembly = (sender, args) => { if (args.LoadedAssembly.GetName().Name.StartsWith("Client.Game") || args.LoadedAssembly.GetName().Name == "Client.Other") assemblyLoads++; };
            UnityEngine.Application.LogCallback metadata = (message, stack, type) => { if (message.StartsWith("F6_METADATA ")) metadataLoads++; };
            var gate = typeof(Client.Framework.HotUpdate.HotfixAssemblyLoader).GetField("attempted", BindingFlags.Static | BindingFlags.NonPublic);
            object before = gate.GetValue(null);
            gate.SetValue(null, 0);
            AppDomain.CurrentDomain.AssemblyLoad += assembly; UnityEngine.Application.logMessageReceived += metadata;
            try { Assert.That(() => new Client.Framework.HotUpdate.HotfixAssemblyLoader("unused.Entry").LoadOnce(prepared, player), Throws.InstanceOf<InvalidDataException>()); }
            finally { AppDomain.CurrentDomain.AssemblyLoad -= assembly; UnityEngine.Application.logMessageReceived -= metadata; gate.SetValue(null, before); }
            Assert.That(assemblyLoads, Is.Zero); Assert.That(metadataLoads, Is.Zero);
        }
        private static byte[] Dll(string name, params string[] references)
        {
            using (var module = new ModuleDefUser(name + ".dll") { Kind = ModuleKind.Dll })
            {
                var assembly = new AssemblyDefUser(name, new Version(1, 0, 0, 0)); assembly.Modules.Add(module);
                for (int i = 0; i < references.Length; i++) module.Types.Add(new TypeDefUser("Fixture", "Type" + i,
                    new TypeRefUser(module, "Fixture", "Base", new AssemblyRefUser(new AssemblyNameInfo(references[i] + ", Version=1.0.0.0")))) { Attributes = dnlib.DotNet.TypeAttributes.Public });
                using (var bytes = new MemoryStream()) { module.Write(bytes); return bytes.ToArray(); }
            }
        }
        [Test]
        public void NetstandardRequiresExplicitNativePinnedForwarderBeforeLoad()
        {
            byte[] dll = Dll("Client.Game.Protocol", "netstandard");
            payload["Hotfix/Client.Game.Protocol.dll"] = dll;
            document["files"][1]["length"] = dll.Length; document["files"][1]["sha256"] = Hash(dll);
            document["assemblies"][0]["dependencies"] = new JArray("netstandard");
            RejectPlan();
        }
        [TestCase(false)] [TestCase(true)]
        public void NativePinnedForwarderBindsOnlyExactSignedBytes(bool changed)
        {
            byte[] dll = Dll("Client.Game.Protocol", "netstandard");
            payload["Hotfix/Client.Game.Protocol.dll"] = dll;
            document["files"][1]["length"] = dll.Length; document["files"][1]["sha256"] = Hash(dll);
            document["assemblies"][0]["dependencies"] = new JArray("netstandard");
            byte[] facade = File.ReadAllBytes("Packages/com.code-philosophy.hybridclr/Data~/NetStandard/netstandard2.1.dll");
            if (changed) facade[facade.Length - 1] ^= 1;
            payload["References/netstandard.dll"] = facade;
            ((JArray)document["files"]).Add(new JObject { ["path"] = "References/netstandard.dll", ["length"] = facade.Length, ["sha256"] = Hash(facade), ["kind"] = "Configuration" });
            if (changed) RejectPlan(); else Assert.That(Create(Prepare(), player), Is.Not.Null);
        }
        [TestCase(0)] [TestCase(1)] [TestCase(63)] [TestCase(128)]
        public void BoundedReaderRejectsTruncatedDll(int length)
        {
            byte[] dll = payload["Hotfix/Client.Game.Protocol.dll"].Take(length).ToArray();
            payload["Hotfix/Client.Game.Protocol.dll"] = dll;
            document["files"][1]["length"] = dll.Length; document["files"][1]["sha256"] = Hash(dll); RejectPlan();
        }
        [Test]
        public void BoundedReaderRejectsOutOfRangeStringsStream()
        {
            byte[] dll = payload["Hotfix/Client.Game.Protocol.dll"];
            byte[] name = Encoding.ASCII.GetBytes("#Strings\0");
            int offset = Enumerable.Range(0, dll.Length - name.Length).First(i => dll.Skip(i).Take(name.Length).SequenceEqual(name));
            for (int i = offset - 4; i < offset; i++) dll[i] = 255;
            document["files"][1]["sha256"] = Hash(dll); RejectPlan();
        }
        [Test]
        public void BoundedReaderAcceptsPe32PlusIdentity()
        {
            using (var module = new ModuleDefUser("Client.Game.Protocol.dll") { Kind = ModuleKind.Dll, Machine = dnlib.PE.Machine.AMD64 })
            {
                new AssemblyDefUser("Client.Game.Protocol", new Version(1, 0, 0, 0)).Modules.Add(module);
                module.Types.Add(new TypeDefUser("Fixture", "Protocol", module.CorLibTypes.Object.TypeDefOrRef) { Attributes = dnlib.DotNet.TypeAttributes.Public });
                using (var output = new MemoryStream()) { module.Write(output); byte[] dll = output.ToArray(); Assert.That(BitConverter.ToUInt16(dll, BitConverter.ToInt32(dll, 0x3c) + 24), Is.EqualTo(0x20b), "Fixture must actually exercise PE32+."); payload["Hotfix/Client.Game.Protocol.dll"] = dll; document["files"][1]["length"] = dll.Length; document["files"][1]["sha256"] = Hash(dll); }
            }
            Assert.That(Create(Prepare(), player), Is.Not.Null);
        }
        private void RejectPlan() { var prepared = Prepare(); Assert.That(() => Create(prepared, player), Throws.InstanceOf<InvalidDataException>()); }
        private object Create(PreparedRelease prepared, PlayerIdentity identity)
        {
            Type type = Type.GetType("Client.Framework.HotUpdate.HotfixLoadPlan, Client.Framework");
            Assert.That(type, Is.Not.Null, "F6 load plan is missing.");
            return Invoke(type.GetMethod("Create"), null, prepared, identity);
        }
        private PreparedRelease Prepare()
        {
            Directory.CreateDirectory(Path.Combine(root, "source"));
            foreach (var p in payload) { string path = Path.Combine(root, "source", p.Key); Directory.CreateDirectory(Path.GetDirectoryName(path)); File.WriteAllBytes(path, p.Value); }
            byte[] bytes = Encoding.UTF8.GetBytes(document.ToString(Formatting.None));
            var release = verifier.Verify(bytes, key.SignData(bytes, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1), player);
            var state = new ReleaseStateStore(Path.Combine(root, "store"), verifier, player);
            var installer = new BaseReleaseInstaller(Path.Combine(root, "store"), new PlatformReleaseStreamReader(Path.Combine(root, "source"), false), state,
                new DiskBudget(_ => long.MaxValue));
            return System.Threading.Tasks.Task.Run(() => installer.InstallAsync(release, null, CancellationToken.None)).GetAwaiter().GetResult();
        }
        private static JObject Assembly(string name, params string[] dependency) => new JObject { ["name"] = name, ["path"] = "Hotfix/" + name + ".dll", ["dependencies"] = new JArray(dependency) };
        private static object Invoke(MethodInfo method, object target, params object[] args)
        { try { return method.Invoke(target, args); } catch (TargetInvocationException e) { System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(e.InnerException).Throw(); throw; } }
        private static string Hash(byte[] bytes) { using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant(); }
    }
}
