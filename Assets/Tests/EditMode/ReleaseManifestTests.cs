using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace Client.Tests.EditMode
{
    [Category("F4")]
    public sealed class ReleaseManifestTests
    {
        private RSA key;
        private RSA otherKey;
        private object verifier;
        private static readonly string HashA = new string('a', 64);
        private static readonly string HashB = new string('b', 64);
        private static readonly string[] IdentityFields = { "playerBuildId", "platform", "architecture", "unityVersion", "contractVersion", "envelopeSha256", "aotMetadataSetId" };

        [OneTimeSetUp]
        public void GenerateEphemeralTestKeys()
        {
            key = new RSACryptoServiceProvider(2048) { PersistKeyInCsp = false };
            otherKey = new RSACryptoServiceProvider(2048) { PersistKeyInCsp = false };
            string root = Path.Combine("Builds", "TestKeys", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            System.IO.File.WriteAllText(Path.Combine(root, "test-f4.xml"), key.ToXmlString(true));
            System.IO.File.WriteAllText(Path.Combine(root, "test-rotation.xml"), otherKey.ToXmlString(true));
        }

        [OneTimeTearDown]
        public void DisposeKeys() { key?.Dispose(); otherKey?.Dispose(); }

        [SetUp]
        public void CreateVerifier()
        {
            Type type = RequiredType("ManifestVerifier");
            verifier = Activator.CreateInstance(type, BindingFlags.Instance | BindingFlags.NonPublic, null,
                new object[] { new Dictionary<string, RSAParameters> { ["test-f4"] = key.ExportParameters(false), ["test-rotation"] = otherKey.ExportParameters(false) } }, null);
        }

        [Test]
        public void ValidReleaseExposesCompleteImmutableSnapshot()
        {
            byte[] json = Bytes(Manifest());
            object release = Verify(json, Sign(json), Identity());
            Assert.That(Get(release, "ReleaseId"), Is.EqualTo("release-1"));
            Assert.That(Get(release, "ManifestSha256"), Is.EqualTo(Hash(json)));
            Assert.That(Get(release, "ProtocolSha256"), Is.EqualTo(HashB));
            Assert.That(Get(release, "ResourceVersionListPath"), Is.EqualTo("Resources/GameFrameworkVersion.dat"));
            var files = (IList)Get(release, "Files");
            Assert.That(files.Count, Is.EqualTo(4));
            Assert.That(Get(files[0], "Path"), Is.EqualTo("Resources/GameFrameworkVersion.dat"));
            Assert.That(((IEnumerable)Get(release, "AssemblyOrder")).Cast<string>(), Is.EqualTo(new[] { "Client.Game.Protocol", "Client.Game" }));
            Assert.Throws<NotSupportedException>(() => files.Clear());
            var order = (IList)Get(release, "AssemblyOrder");
            Assert.Throws<NotSupportedException>(() => order[0] = "Changed");
            Assert.That(release.GetType().GetProperties().All(p => p.SetMethod == null || !p.SetMethod.IsPublic), Is.True);
            Array.Clear(json, 0, json.Length);
            Assert.That(Get(files[0], "Sha256"), Is.EqualTo(HashA));
        }

        [Test]
        public void VerifiedReleaseRetainsImmutableSignedEvidenceForRestart()
        {
            byte[] json = Bytes(Manifest()), original = (byte[])json.Clone();
            byte[] signature = Sign(json), detached = (byte[])signature.Clone();
            object release = Verify(json, signature, Identity());
            Array.Clear(json, 0, json.Length); Array.Clear(signature, 0, signature.Length);
            Assert.That(Get(release, "ManifestBytes"), Is.EqualTo(original));
            Assert.That(Get(release, "SignatureBytes"), Is.EqualTo(detached));
            var exposed = (byte[])Get(release, "ManifestBytes"); exposed[0] ^= 1;
            exposed = (byte[])Get(release, "SignatureBytes"); exposed[0] ^= 1;
            Assert.That(Get(release, "ManifestBytes"), Is.EqualTo(original));
            Assert.That(Get(release, "SignatureBytes"), Is.EqualTo(detached));
        }

        [TestCaseSource(nameof(IdentityFields))]
        public void WrongIdentityLoadsZeroDlls(string field)
        {
            var manifest = Manifest();
            manifest["player"][field] = field == "envelopeSha256" ? HashB : "wrong-value";
            byte[] json = Bytes(manifest);
            int loads = 0;
            AssemblyLoadEventHandler loaded = (sender, args) => loads++;
            AppDomain.CurrentDomain.AssemblyLoad += loaded;
            try { Reject("IncompatiblePlayer", () => Verify(json, Sign(json), Identity())); }
            finally { AppDomain.CurrentDomain.AssemblyLoad -= loaded; }
            Assert.That(loads, Is.Zero, "Identity rejection must precede any assembly load.");
        }

        [TestCase(false)]
        [TestCase(true)]
        public void SignatureCoversOriginalBytes(bool channel)
        {
            byte[] json = Bytes(channel ? Channel() : Manifest());
            byte[] signature = Sign(json);
            Action<byte[]> verify = value => { if (channel) Call("VerifyChannel", value, signature); else Verify(value, signature, Identity()); };
            verify(json);
            // A legal whitespace-only change must invalidate the detached signature too.
            Reject("InvalidSignature", () => verify(json.Concat(new byte[] { 32 }).ToArray()));
            json[10] ^= 1;
            Reject("InvalidSignature", () => verify(json));
        }

        [TestCase("/etc/passwd")]
        [TestCase("C:/secret")]
        [TestCase("C:secret")]
        [TestCase("../secret")]
        [TestCase("Resources/../secret")]
        [TestCase("Resources\\secret")]
        [TestCase("Resources//secret")]
        [TestCase("Resources/./secret")]
        [TestCase("Resources/secret/")]
        [TestCase("Resources/%2e%2e/secret")]
        [TestCase("Resources/secret:stream")]
        [TestCase("Resources/secret.")]
        public void ManifestRejectsUnsafePaths(string path)
        {
            var value = Manifest(); value["files"][0]["path"] = path;
            RejectSigned(value, "UnsafePath");
            var channel = Channel(); channel["manifestRelativePath"] = path;
            byte[] bytes = Bytes(channel);
            Reject("UnsafePath", () => Call("VerifyChannel", bytes, Sign(bytes)));
        }

        [TestCase("Resources/GameFrameworkVersion.dat")]
        [TestCase("resources/gameframeworkversion.dat")]
        public void ManifestRejectsDuplicatePathsAcrossPlatforms(string path)
        {
            var value = Manifest(); value["files"][1]["path"] = path;
            RejectSigned(value, "DuplicatePath");
        }

        [TestCase("{\"keyId\":\"test-f4\",\"keyId\":\"test-f4\"}")]
        [TestCase("{\"keyId\":\"test-f4\",\"nested\":{\"x\":1,\"x\":2}}")]
        [TestCase("{/*comment*/\"keyId\":\"test-f4\"}")]
        [TestCase("{\"keyId\":'test-f4'}")]
        [TestCase("{\"keyId\":\"test-f4\",}")]
        [TestCase("{} {}")]
        [TestCase("{true:1,\"keyId\":\"test-f4\"}")]
        public void StrictJsonRejectsAmbiguousInputs(string input)
        {
            byte[] json = Encoding.UTF8.GetBytes(input);
            Reject("InvalidJson", () => Verify(json, Sign(json), Identity()));
        }

        [Test]
        public void StrictUtf8AndSizeAreBoundedBeforeParsing()
        {
            byte[] invalid = new byte[] { 0x7b, 0xff, 0x7d };
            Reject("InvalidJson", () => Verify(invalid, Sign(invalid), Identity()));
            byte[] oversized = new byte[256 * 1024 + 1];
            Reject("DocumentTooLarge", () => Verify(oversized, Sign(oversized), Identity()));
            Reject("DocumentTooLarge", () => Call("VerifyChannel", oversized, Sign(oversized)));
            byte[] original = Bytes(Manifest());
            byte[] atLimit = original.Concat(Enumerable.Repeat((byte)32, 256 * 1024 - original.Length)).ToArray();
            Assert.That(Verify(atLimit, Sign(atLimit), Identity()), Is.Not.Null);
        }

        [Test]
        public void InvalidSignatureWinsBeforeJsonParsing()
        {
            byte[] malformed = Encoding.UTF8.GetBytes("private-outside-path-secret");
            Reject("InvalidSignature", () => Verify(malformed, new byte[256], Identity()));
        }

        [TestCase(0)]
        [TestCase(255)]
        [TestCase(257)]
        public void SignatureMustBeRawRsa2048Length(int length)
        {
            Reject("InvalidSignature", () => Verify(Bytes(Manifest()), new byte[length], Identity()));
        }

        [Test]
        public void RotationUsesPinnedKeyThenChecksClaimedKeyId()
        {
            var value = Manifest(); value["keyId"] = "test-rotation";
            byte[] json = Bytes(value);
            Assert.That(Verify(json, Sign(json, otherKey), Identity()), Is.Not.Null);
            Reject("UntrustedKey", () => Verify(json, Sign(json), Identity()));
            value["keyId"] = "remote-key"; value["publicKey"] = key.ToXmlString(false);
            RejectSigned(value, "UntrustedKey");
        }

        [Test]
        public void ProductionEmptyKeyringFailsClosed()
        {
            object production = Activator.CreateInstance(RequiredType("ManifestVerifier"));
            byte[] json = Bytes(Manifest());
            Reject("InvalidSignature", () => Invoke(production, "Verify", json, Sign(json), Identity()));
        }

        [Test]
        public void ChannelPinsManifestHashAndReleaseIdentity()
        {
            byte[] json = Bytes(Manifest());
            var channel = Channel(); channel["manifestSha256"] = Hash(json);
            byte[] pointerJson = Bytes(channel);
            object pointer = Call("VerifyChannel", pointerJson, Sign(pointerJson));
            Assert.That(Call("VerifyPinnedRelease", pointer, json, Sign(json), Identity()), Is.Not.Null);
            var altered = Manifest(); altered["releaseId"] = "release-2";
            byte[] other = Bytes(altered);
            Reject("ManifestHashMismatch", () => Call("VerifyPinnedRelease", pointer, other, Sign(other), Identity()));
            channel["manifestSha256"] = Hash(other);
            pointerJson = Bytes(channel); pointer = Call("VerifyChannel", pointerJson, Sign(pointerJson));
            Reject("ReleaseIdMismatch", () => Call("VerifyPinnedRelease", pointer, other, Sign(other), Identity()));
        }

        [TestCase("missing-field")]
        [TestCase("unknown-field")]
        [TestCase("negative-length")]
        [TestCase("float-length")]
        [TestCase("bad-hash")]
        [TestCase("unknown-kind")]
        [TestCase("missing-dll")]
        [TestCase("missing-order")]
        [TestCase("duplicate-order")]
        [TestCase("duplicate-assembly")]
        [TestCase("resource-index-not-listed")]
        public void InvalidManifestSchemaCannotProduceVerifiedRelease(string mutation)
        {
            var value = Manifest();
            switch (mutation)
            {
                case "missing-field": ((JObject)value["player"]).Remove("envelopeSha256"); break;
                case "unknown-field": value["unexpected"] = true; break;
                case "negative-length": value["files"][0]["length"] = -1; break;
                case "float-length": value["files"][0]["length"] = 1.5; break;
                case "bad-hash": value["files"][0]["sha256"] = "no-hash"; break;
                case "unknown-kind": value["files"][0]["kind"] = "Executable"; break;
                case "missing-dll": ((JArray)value["files"]).RemoveAt(2); break;
                case "missing-order": ((JArray)value["assemblyOrder"]).RemoveAt(1); break;
                case "duplicate-order": value["assemblyOrder"][1] = "Client.Game.Protocol"; break;
                case "duplicate-assembly": ((JArray)value["assemblies"]).Add(value["assemblies"][0].DeepClone()); break;
                case "resource-index-not-listed": value["resourceVersionListPath"] = "Resources/missing.dat"; break;
            }
            RejectSigned(value, "InvalidManifest");
        }

        [Test]
        public void ProductionWriterRejectsTestKeyIdBeforeSigningOrWriting()
        {
            Type writer = Type.GetType("Client.Editor.Build.ReleaseManifestWriter, Client.Editor");
            Assert.That(writer, Is.Not.Null);
            string path = Path.Combine("Builds", "WriterReject-" + Guid.NewGuid().ToString("N"));
            byte[] json = Bytes(Manifest());
            var error = Assert.Throws<TargetInvocationException>(() => writer.GetMethod("WriteSigned").Invoke(null, new object[] { path, json, "test-f4", key, Identity() }));
            Assert.That(error.InnerException, Is.TypeOf<InvalidOperationException>());
            Assert.That(Directory.Exists(path), Is.False);
        }

        [Test]
        public void ProductionWriterRejectsAnUnpinnedProductionLookingKey()
        {
            Type writer = Type.GetType("Client.Editor.Build.ReleaseManifestWriter, Client.Editor");
            string path = Path.Combine("Builds", "WriterUnpinned-" + Guid.NewGuid().ToString("N"));
            var document = Manifest(); document["keyId"] = "production-looking";
            var error = Assert.Throws<TargetInvocationException>(() => writer.GetMethod("WriteSigned").Invoke(null,
                new object[] { path, Bytes(document), "production-looking", key, Identity() }));
            Assert.That(error.InnerException.GetType().Name, Is.EqualTo("ReleaseVerificationException"));
            Assert.That(Directory.Exists(path), Is.False);
        }

        [TestCase("schema-overflow")]
        [TestCase("identity-too-long")]
        public void MalformedSchemaAlwaysUsesFiniteErrorCodes(string mutation)
        {
            var document = Manifest();
            if (mutation == "schema-overflow") document["schemaVersion"] = JToken.Parse("999999999999999999999999999999999");
            else document["player"]["platform"] = new string('x', 300);
            RejectSigned(document, "InvalidManifest");
        }

        [Test]
        public void ArchivedUnsafePathVectorsAreRejectedByBothDocumentTypes()
        {
            var cases = JArray.Parse(System.IO.File.ReadAllText("Tests/Fixtures/Releases/ManifestCases/unsafe-paths.json"));
            foreach (string path in cases.Values<string>()) ManifestRejectsUnsafePaths(path);
        }

        [Test]
        public void KeyringOwnsPublicKeySnapshotAndRejectsPrivateMaterial()
        {
            Type type = RequiredType("ManifestVerifier");
            var parameters = key.ExportParameters(false);
            var dictionary = new Dictionary<string, RSAParameters> { ["test-f4"] = parameters };
            object snapshot = Activator.CreateInstance(type, BindingFlags.Instance | BindingFlags.NonPublic, null, new object[] { dictionary }, null);
            parameters.Modulus[0] ^= 1;
            dictionary.Clear();
            byte[] json = Bytes(Manifest());
            Assert.That(Invoke(snapshot, "Verify", json, Sign(json), Identity()), Is.Not.Null);
            dictionary["test-private"] = key.ExportParameters(true);
            var error = Assert.Throws<TargetInvocationException>(() => Activator.CreateInstance(type, BindingFlags.Instance | BindingFlags.NonPublic, null, new object[] { dictionary }, null));
            Assert.That(error.InnerException, Is.TypeOf<ArgumentException>());
        }

        [Test]
        public void SignatureRejectsWrongDigestAndUnknownSigner()
        {
            byte[] json = Bytes(Manifest());
            Reject("InvalidSignature", () => Verify(json, key.SignData(json, HashAlgorithmName.SHA1, RSASignaturePadding.Pkcs1), Identity()));
            using (var unknown = new RSACryptoServiceProvider(2048) { PersistKeyInCsp = false })
                Reject("InvalidSignature", () => Verify(json, Sign(json, unknown), Identity()));
        }

        private static JObject Player() => new JObject { ["playerBuildId"] = "player-1", ["platform"] = "Windows", ["architecture"] = "x64", ["unityVersion"] = "2022.3.25f1", ["contractVersion"] = "1", ["envelopeSha256"] = HashA, ["aotMetadataSetId"] = "aot-1" };
        private static JObject Manifest() => JObject.Parse(System.IO.File.ReadAllText("Tests/Fixtures/Releases/ManifestCases/valid-release.json"));
        private static JObject Channel() => JObject.Parse(System.IO.File.ReadAllText("Tests/Fixtures/Releases/ManifestCases/valid-channel.json"));
        private static byte[] Bytes(JObject value) => Encoding.UTF8.GetBytes(value.ToString(Formatting.None));
        private byte[] Sign(byte[] bytes, RSA signer = null) => (signer ?? key).SignData(bytes, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        private static string Hash(byte[] bytes) { using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant(); }
        private static Type RequiredType(string name) { Type type = Type.GetType("Client.Framework.Release." + name + ", Client.Framework"); Assert.That(type, Is.Not.Null, name + " implementation is required."); return type; }
        private static object Identity() => Activator.CreateInstance(RequiredType("PlayerIdentity"), IdentityFields.Select(f => (object)(string)Player()[f]).ToArray());
        private object Verify(byte[] json, byte[] signature, object player) => Call("Verify", json, signature, player);
        private object Call(string name, params object[] args) => Invoke(verifier, name, args);
        private static object Invoke(object target, string name, params object[] args)
        {
            try { return target.GetType().GetMethod(name).Invoke(target, args); }
            catch (TargetInvocationException error) { throw error.InnerException; }
        }
        private static object Get(object value, string name)
        {
            PropertyInfo property = value.GetType().GetProperty(name);
            Assert.That(property, Is.Not.Null, name + " is required on the verified object.");
            return property.GetValue(value);
        }
        private void RejectSigned(JObject value, string code) { byte[] json = Bytes(value); Reject(code, () => Verify(json, Sign(json), Identity())); }
        private static void Reject(string code, TestDelegate action)
        {
            Exception error = Assert.Catch(action);
            Assert.That(error.GetType().FullName, Is.EqualTo("Client.Framework.Release.ReleaseVerificationException"));
            Assert.That(Get(error, "Code").ToString(), Is.EqualTo(code));
            Assert.That(error.ToString(), Does.Not.Contain("private-outside-path-secret").And.Not.Contain("/etc/passwd"));
        }
    }
}
