using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Versioning;
using NUnit.Framework;
using UnityEditor.Compilation;
using Assembly=System.Reflection.Assembly;
using System.Security.Cryptography;
using Newtonsoft.Json.Linq;

namespace Client.Tests.EditMode
{
    [Category("N1")]
    public sealed class ProtocolIntegrationTests
    {
        [Test]
        public void UnityConsumesNetstandardProtobufAndOnlyRequiredCompanion()
        {
            const string root="Assets/ThirdParty/Protobuf";
            Assert.That(File.Exists(root+"/Google.Protobuf.dll"),Is.True,"Install the pinned runtime through shared restore");
            var protobuf=Assembly.Load(File.ReadAllBytes(root+"/Google.Protobuf.dll"));
            Assert.That(protobuf.GetCustomAttribute<TargetFrameworkAttribute>().FrameworkName,Is.EqualTo(".NETStandard,Version=v2.0"));
            Assert.That(protobuf.GetName().Version,Is.EqualTo(new Version(3,36,2,0)));
            var companion=Assembly.Load(File.ReadAllBytes(root+"/System.Runtime.CompilerServices.Unsafe.dll"));
            Assert.That(companion.GetName().Version,Is.EqualTo(new Version(4,0,4,1)));
            Assert.That(Directory.GetFiles(root,"*.dll").Select(Path.GetFileName),Is.EquivalentTo(new[]{"Google.Protobuf.dll","System.Runtime.CompilerServices.Unsafe.dll"}));
        }

        [Test]
        public void CanonicalEnvelopeParserPreservesUnsigned64BitAndOneof()
        {
            var frame=Type.GetType("Tok.Client.V1.Frame, Client.Protocol.Envelope");
            Assert.That(frame,Is.Not.Null,"The fixed envelope must compile into its AOT assembly");
            // Frame v3 -> BusinessRequest(request_id=uint64.max, message_id=101, body=[8,1]).
            var golden=new byte[]{8,3,34,17,8,255,255,255,255,255,255,255,255,255,1,16,101,26,2,8,1};
            var parser=frame.GetProperty("Parser").GetValue(null);
            var decoded=parser.GetType().GetMethod("ParseFrom",new[]{typeof(byte[])}).Invoke(parser,new object[]{golden});
            Assert.That(frame.GetProperty("Version").GetValue(decoded),Is.EqualTo(3u));
            Assert.That(frame.GetProperty("ContentCase").GetValue(decoded).ToString(),Is.EqualTo("Request"));
            var request=frame.GetProperty("Request").GetValue(decoded);
            Assert.That(request.GetType().GetProperty("RequestId").GetValue(request),Is.EqualTo(ulong.MaxValue));
            Assert.That(request.GetType().GetProperty("MessageId").GetValue(request),Is.EqualTo(101u));
        }

        [Test]
        public void RealEmptyRegistryImplementsTheAotInterface()
        {
            var contract=Type.GetType("Client.Framework.Protocol.IMessageRegistry, Client.Framework");
            var implementation=Type.GetType("Client.Game.Protocol.GameMessageRegistry, Client.Game.Protocol");
            Assert.That(contract,Is.Not.Null);Assert.That(implementation,Is.Not.Null);
            Assert.That(contract.IsAssignableFrom(implementation),Is.True);
            var registry=Activator.CreateInstance(implementation);
            var parserArguments=new object[]{101u,null};
            Assert.That(implementation.GetMethod("TryGetParser").Invoke(registry,parserArguments),Is.False);
            Assert.That(parserArguments[1],Is.Null);
            var responseArguments=new object[]{101u,0u};
            Assert.That(implementation.GetMethod("TryGetResponseId").Invoke(registry,responseArguments),Is.False);
            Assert.That(responseArguments[1],Is.EqualTo(0u));
        }

        [Test]
        public void CompiledProtocolGraphAndActualHybridClrLoadSetRespectHotBoundary()
        {
            var assemblies=CompilationPipeline.GetAssemblies(AssembliesType.Player);
            foreach(var name in new[]{"Client.Protocol.Envelope","Client.Game.Protocol"})
                Assert.That(assemblies.Any(a=>a.name==name),Is.True,"Missing real compiled module: "+name);
            var framework=assemblies.Single(a=>a.name=="Client.Framework");
            Assert.That(framework.assemblyReferences.Any(a=>a.name=="Client.Protocol.Envelope"),Is.True);
            var game=assemblies.Single(a=>a.name=="Client.Game");
            Assert.That(game.assemblyReferences.Select(a=>a.name),Does.Contain("Client.Game.Protocol").And.Contain("Client.Framework"));
            foreach(var name in new[]{"Client.Contracts","Client.Framework","Client.Protocol.Envelope"})
            {
                var compiled=Assembly.Load(File.ReadAllBytes(assemblies.Single(a=>a.name==name).outputPath));
                Assert.That(compiled.GetReferencedAssemblies().Any(a=>a.Name.StartsWith("Client.Game",StringComparison.Ordinal)),Is.False,name);
            }
            var settings=Type.GetType("HybridCLR.SettingsUtil, HybridCLR.Editor");
            Assert.That(settings,Is.Not.Null);
            var hot=(IEnumerable)settings.GetProperty("HotUpdateAssemblyNamesExcludePreserved").GetValue(null);
            Assert.That(hot.Cast<string>(),Is.EquivalentTo(new[]{"Client.Game.Protocol","Client.Game"}));
        }

        [Test]
        public void ProtocolSourcesAndInstalledDependenciesMatchTheirInventory()
        {
            const string envelope="Assets/Scripts/Framework/Protocol/Envelope/Generated/";
            var provenance=JObject.Parse(File.ReadAllText(envelope+"source-provenance.json"));
            Assert.That((string)provenance["generated_sha256"],Is.EqualTo(Hash(envelope+"Client.cs")));
            Assert.That((string)provenance["envelope_sha256"],Is.EqualTo(Hash("../comm/proto/tok/client/v1/client.proto")));
            Assert.That((string)provenance["protocol_lock_sha256"],Is.EqualTo(Hash("../comm/proto/protocol.lock.json")));
            const string tokLicense="docs/licenses/Tok-Apache-2.0.txt";
            Assert.That(File.Exists(tokLicense),Is.True,"The generated fixed envelope must retain its input source license");
            var sourceLock=JObject.Parse(File.ReadAllText("../comm/proto/protocol.lock.json"));
            Assert.That(Hash(tokLicense),Is.EqualTo((string)sourceLock["License"]["Sha256"]));
            const string inventoryPath="Assets/ThirdParty/Protobuf/dependency-provenance.json";
            Assert.That(File.Exists(inventoryPath),Is.True,"Shared restore must record installed file hashes and licenses");
            var inventory=JObject.Parse(File.ReadAllText(inventoryPath));
            Assert.That((string)inventory["dependencies_lock_sha256"],Is.EqualTo(Hash("../comm/tools/Dependencies/packages.lock.json")));
            foreach(var library in inventory["libraries"])
            {
                Assert.That((string)library["sha256"],Is.EqualTo(Hash((string)library["path"])));
                Assert.That((string)library["license_sha256"],Is.EqualTo(Hash((string)library["license"])));
                Assert.That((string)library["target_framework"],Is.EqualTo("netstandard2.0"));
            }
            var dependencyLock=JObject.Parse(File.ReadAllText("docs/dependencies.lock.json"));
            Assert.That(dependencyLock["protobuf"].Type,Is.EqualTo(JTokenType.Object));
            Assert.That((string)dependencyLock["protobuf"]["runtime_version"],Is.EqualTo("3.36.2"));
            Assert.That((string)dependencyLock["protobuf"]["inventory_sha256"],Is.EqualTo(Hash(inventoryPath)));
        }

        private static string Hash(string path)
        {
            using(var sha=SHA256.Create())return string.Concat(sha.ComputeHash(File.ReadAllBytes(path)).Select(value=>value.ToString("x2")));
        }
    }
}
