using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Client.Framework.Release
{
    public sealed class ManifestVerifier
    {
        public const int MaximumDocumentBytes = 256 * 1024;
        private readonly KeyValuePair<string, RSAParameters>[] trustedKeys;
        private static readonly Regex JsonNumber = new Regex(@"^-?(0|[1-9][0-9]*)(\.[0-9]+)?([eE][+-]?[0-9]+)?$", RegexOptions.CultureInvariant);

        // Provision production pins here at Player build time. Empty means no release is trusted.
        public ManifestVerifier() : this(new Dictionary<string, RSAParameters>()) { }

        // Trusted construction seam, never populated from channel/manifest/network JSON.
        internal ManifestVerifier(IDictionary<string, RSAParameters> publicKeys)
        {
            if (publicKeys == null || publicKeys.Count > 4) throw new ArgumentException("At most four pinned public keys are supported.");
            trustedKeys = publicKeys.Select(pair =>
            {
                RelativePathPolicy.RequireIdentifier(pair.Key);
                RSAParameters key = pair.Value;
                if (key.Modulus == null || key.Modulus.Length != 256 || key.Exponent == null || key.Exponent.Length == 0 ||
                    key.D != null || key.P != null || key.Q != null || key.DP != null || key.DQ != null || key.InverseQ != null)
                    throw new ArgumentException("Only RSA-2048 public keys may be pinned.");
                return new KeyValuePair<string, RSAParameters>(pair.Key, new RSAParameters { Modulus = (byte[])key.Modulus.Clone(), Exponent = (byte[])key.Exponent.Clone() });
            }).ToArray();
        }

        public VerifiedRelease Verify(byte[] json, byte[] signature, PlayerIdentity player)
        {
            byte[] raw = Snapshot(json);
            byte[] detached = SnapshotSignature(signature);
            string signingKey = Authenticate(raw, detached);
            JObject root = Parse(raw);
            RequireKey(root, signingKey);
            Fields(root, "schemaVersion", "keyId", "releaseId", "player", "protocolSha256", "resourceVersionListPath", "files", "assemblies", "assemblyOrder");
            Version(root);
            string releaseId = Identifier(root, "releaseId");
            JObject identity = Object(root["player"]);
            Fields(identity, "playerBuildId", "platform", "architecture", "unityVersion", "contractVersion", "envelopeSha256", "aotMetadataSetId");
            var expected = new PlayerIdentity(Identifier(identity, "playerBuildId"), IdentityText(identity, "platform"), IdentityText(identity, "architecture"),
                IdentityText(identity, "unityVersion"), IdentityText(identity, "contractVersion"), Sha(identity, "envelopeSha256"), Identifier(identity, "aotMetadataSetId"));
            ReleaseCompatibility.RequireExact(expected, player);
            string protocolHash = Sha(root, "protocolSha256");
            string resourceIndex = Path(root, "resourceVersionListPath");
            var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var files = new List<ReleaseFile>();
            foreach (JToken item in Array(root, "files"))
            {
                JObject file = Object(item); Fields(file, "path", "length", "sha256", "kind");
                string path = Path(file, "path");
                if (!paths.Add(path)) Fail(ReleaseErrorCode.DuplicatePath);
                if (file["length"].Type != JTokenType.Integer) Invalid();
                long length;
                try { length = (long)file["length"]; } catch (OverflowException) { Invalid(); throw; }
                if (length < 0) Invalid();
                ReleaseFileKind kind;
                string kindName = Text(file, "kind");
                if (!Enum.TryParse(kindName, false, out kind) || kind.ToString() != kindName || !Enum.IsDefined(typeof(ReleaseFileKind), kind)) Invalid();
                files.Add(new ReleaseFile(path, length, Sha(file, "sha256"), kind));
            }
            if (files.Count == 0 || !files.Any(f => f.Path == resourceIndex && f.Kind == ReleaseFileKind.NativeResource)) Invalid();
            var names = new HashSet<string>(StringComparer.Ordinal);
            var assemblyPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var assemblies = new List<ReleaseAssembly>();
            foreach (JToken item in Array(root, "assemblies"))
            {
                JObject assembly = Object(item); Fields(assembly, "name", "path", "dependencies");
                string name = Identifier(assembly, "name"), path = Path(assembly, "path");
                if (name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) || !names.Add(name) || !assemblyPaths.Add(path)) Invalid();
                if (!files.Any(f => f.Path == path && f.Kind == ReleaseFileKind.HotfixDll)) Invalid();
                string[] dependencies = Array(assembly, "dependencies").Select(StringValue).ToArray();
                foreach (string dependency in dependencies) RelativePathPolicy.RequireIdentifier(dependency);
                if (dependencies.Distinct(StringComparer.Ordinal).Count() != dependencies.Length || dependencies.Contains(name)) Invalid();
                assemblies.Add(new ReleaseAssembly(name, path, dependencies));
            }
            string[] order = Array(root, "assemblyOrder").Select(StringValue).ToArray();
            if (order.Length == 0 || order.Length != names.Count || order.Distinct(StringComparer.Ordinal).Count() != order.Length || order.Any(n => !names.Contains(n)) ||
                files.Count(f => f.Kind == ReleaseFileKind.HotfixDll) != assemblies.Count) Invalid();
            return new VerifiedRelease(releaseId, signingKey, expected, protocolHash, resourceIndex, Hash(raw), raw, detached, files, assemblies, order);
        }

        public VerifiedChannelPointer VerifyChannel(byte[] json, byte[] signature)
        {
            byte[] raw = Snapshot(json);
            string signingKey = Authenticate(raw, SnapshotSignature(signature));
            JObject root = Parse(raw); RequireKey(root, signingKey);
            Fields(root, "schemaVersion", "keyId", "releaseId", "manifestRelativePath", "manifestSha256"); Version(root);
            return new VerifiedChannelPointer(Identifier(root, "releaseId"), Path(root, "manifestRelativePath"), Sha(root, "manifestSha256"), signingKey);
        }

        public VerifiedRelease VerifyPinnedRelease(VerifiedChannelPointer channel, byte[] json, byte[] signature, PlayerIdentity player)
        {
            if (channel == null) Invalid();
            byte[] raw = Snapshot(json);
            if (!string.Equals(Hash(raw), channel.ManifestSha256, StringComparison.Ordinal)) Fail(ReleaseErrorCode.ManifestHashMismatch);
            VerifiedRelease release = Verify(raw, signature, player);
            if (release.ReleaseId != channel.ReleaseId) Fail(ReleaseErrorCode.ReleaseIdMismatch);
            return release;
        }

        private static byte[] Snapshot(byte[] json)
        {
            if (json != null && json.Length > MaximumDocumentBytes) Fail(ReleaseErrorCode.DocumentTooLarge);
            if (json == null || json.Length == 0) Fail(ReleaseErrorCode.InvalidJson);
            return (byte[])json.Clone();
        }

        private static byte[] SnapshotSignature(byte[] signature)
        {
            if (signature == null || signature.Length != 256) Fail(ReleaseErrorCode.InvalidSignature);
            return (byte[])signature.Clone();
        }

        private string Authenticate(byte[] raw, byte[] signature)
        {
            foreach (var key in trustedKeys)
            {
                try
                {
                    using (RSA rsa = RSA.Create())
                    {
                        rsa.ImportParameters(key.Value);
                        if (rsa.KeySize == 2048 && rsa.VerifyData(raw, signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1)) return key.Key;
                    }
                }
                catch (CryptographicException) { }
            }
            Fail(ReleaseErrorCode.InvalidSignature); return null;
        }

        private static JObject Parse(byte[] raw)
        {
            try
            {
                string json = new UTF8Encoding(false, true).GetString(raw);
                StrictLexicalJson(json);
                using (var text = new StringReader(json))
                using (var reader = new StrictJsonReader(text) { DateParseHandling = DateParseHandling.None, MaxDepth = 32, FloatParseHandling = FloatParseHandling.Decimal })
                {
                    JObject root = JObject.Load(reader, new JsonLoadSettings { DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error, CommentHandling = CommentHandling.Load });
                    if (reader.Read()) Fail(ReleaseErrorCode.InvalidJson);
                    return root;
                }
            }
            catch (ReleaseVerificationException) { throw; }
            catch (Exception error) when (error is JsonException || error is DecoderFallbackException || error is ArgumentException || error is OverflowException)
            { Fail(ReleaseErrorCode.InvalidJson); return null; }
        }

        private sealed class StrictJsonReader : JsonTextReader
        {
            internal StrictJsonReader(TextReader input) : base(input) { }
            public override bool Read()
            {
                bool read = base.Read();
                if (read && (TokenType == JsonToken.PropertyName || TokenType == JsonToken.String) && QuoteChar != '"')
                    throw new JsonReaderException("JSON strings and property names require double quotes.");
                return read;
            }
        }

        // Json.NET accepts several JavaScript extensions; disallow them before its structural parser.
        private static void StrictLexicalJson(string text)
        {
            char previous = '\0';
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (c == ' ' || c == '\t' || c == '\r' || c == '\n') continue;
                if (c == '"')
                {
                    bool closed = false;
                    for (i++; i < text.Length; i++)
                    {
                        if (text[i] < 32) Fail(ReleaseErrorCode.InvalidJson);
                        if (text[i] == '\\') { i++; continue; }
                        if (text[i] == '"') { closed = true; break; }
                    }
                    if (!closed) Fail(ReleaseErrorCode.InvalidJson);
                    previous = 's'; continue;
                }
                if ("{}[],:".IndexOf(c) >= 0)
                {
                    if ((c == '}' || c == ']') && previous == ',') Fail(ReleaseErrorCode.InvalidJson);
                    previous = c; continue;
                }
                int start = i;
                while (i + 1 < text.Length && " \t\r\n{}[],:".IndexOf(text[i + 1]) < 0) i++;
                string token = text.Substring(start, i - start + 1);
                if (token != "true" && token != "false" && token != "null" && !JsonNumber.IsMatch(token)) Fail(ReleaseErrorCode.InvalidJson);
                previous = 'v';
            }
        }

        private static void RequireKey(JObject root, string key) { if (Text(root, "keyId") != key) Fail(ReleaseErrorCode.UntrustedKey); }
        private static void Version(JObject root) { if (root["schemaVersion"].Type != JTokenType.Integer || root["schemaVersion"].ToString(Formatting.None) != "1") Invalid(); }
        private static void Fields(JObject value, params string[] names) { if (value.Properties().Count() != names.Length || names.Any(n => value.Property(n, StringComparison.Ordinal) == null)) Invalid(); }
        private static JObject Object(JToken value) { if (!(value is JObject)) Invalid(); return (JObject)value; }
        private static JArray Array(JObject value, string name) { if (!(value[name] is JArray)) Invalid(); return (JArray)value[name]; }
        private static string StringValue(JToken value) { if (value == null || value.Type != JTokenType.String || string.IsNullOrWhiteSpace((string)value) || ((string)value).Length > 1024) Invalid(); return (string)value; }
        private static string Text(JObject value, string name) => StringValue(value[name]);
        private static string IdentityText(JObject value, string name) { string text = Text(value, name); if (text.Length > 256) Invalid(); return text; }
        private static string Identifier(JObject value, string name) { string text = Text(value, name); RelativePathPolicy.RequireIdentifier(text); return text; }
        private static string Path(JObject value, string name) { string path = Text(value, name); RelativePathPolicy.RequireCanonical(path); return path; }
        private static string Sha(JObject value, string name) { string hash = Text(value, name); if (hash.Length != 64 || hash.Any(c => !(c >= '0' && c <= '9' || c >= 'a' && c <= 'f'))) Invalid(); return hash; }
        private static string Hash(byte[] bytes) { using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant(); }
        private static void Invalid() => Fail(ReleaseErrorCode.InvalidManifest);
        private static void Fail(ReleaseErrorCode code) => throw new ReleaseVerificationException(code);
    }
}
