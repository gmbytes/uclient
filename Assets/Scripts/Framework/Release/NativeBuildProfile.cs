using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Client.Framework.Release
{
    // Trusted only when supplied by the native package bootstrap or an explicit Editor build input.
    // Never construct trust from a downloaded release, a command-line key, or persistent slot data.
    public sealed class NativeBuildProfile
    {
        private readonly RSAParameters publicKey;
        public string KeyId { get; }
        public string Endpoint { get; }
        public string PackageId { get; }
        public string SessionHost { get; }
        public int SessionPort { get; }
        public bool Acceptance { get; }
        public string Json { get; }
        private NativeBuildProfile(JObject data)
        {
            KeyId = (string)data["keyId"]; RelativePathPolicy.RequireIdentifier(KeyId);
            publicKey = new RSAParameters { Modulus = Convert.FromBase64String((string)data["modulus"]), Exponent = Convert.FromBase64String((string)data["exponent"]) };
            Endpoint = (string)data["endpoint"]; PackageId = (string)data["packageId"]; SessionHost = (string)data["sessionHost"]; SessionPort = (int)data["sessionPort"]; Acceptance = (bool)data["acceptance"];
            if (!Uri.TryCreate(Endpoint, UriKind.Absolute, out var endpoint) || (endpoint.Scheme != "https" && endpoint.Scheme != "http") || !string.IsNullOrEmpty(endpoint.UserInfo) || !string.IsNullOrEmpty(endpoint.Query) || !string.IsNullOrEmpty(endpoint.Fragment) || !Endpoint.EndsWith("/", StringComparison.Ordinal))
                throw new InvalidDataException("A fixed HTTP(S) release endpoint without credentials/query is required.");
            if (string.IsNullOrWhiteSpace(PackageId) || PackageId.Length > 80 || !PackageId.All(c => char.IsLetterOrDigit(c) || c == '.') || !PackageId.Contains(".")) throw new InvalidDataException("Invalid native package identifier.");
            if (string.IsNullOrWhiteSpace(SessionHost) || SessionHost.Length > 253 || SessionHost.Any(char.IsWhiteSpace) || SessionPort < 1 || SessionPort > 65535) throw new InvalidDataException("Invalid fixed development session endpoint.");
            CreateVerifier(); Json = data.ToString(Formatting.None);
        }
        public static NativeBuildProfile Parse(string json)
        {
            try
            {
                if (json == null || Encoding.UTF8.GetByteCount(json) > 16 * 1024) throw new InvalidDataException("Native profile is absent or oversized.");
                var data = JObject.Parse(json, new JsonLoadSettings { DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error });
                string[] fields = { "schemaVersion", "keyId", "modulus", "exponent", "endpoint", "acceptance", "packageId", "sessionHost", "sessionPort" };
                if (!new HashSet<string>(data.Properties().Select(p => p.Name), StringComparer.Ordinal).SetEquals(fields) || data["schemaVersion"].Type != JTokenType.Integer || (int)data["schemaVersion"] != 1 || data["acceptance"].Type != JTokenType.Boolean || data["sessionPort"].Type != JTokenType.Integer)
                    throw new InvalidDataException("Invalid native profile schema.");
                foreach (string field in fields.Where(f => f != "schemaVersion" && f != "acceptance" && f != "sessionPort")) if (data[field].Type != JTokenType.String) throw new InvalidDataException("Invalid profile text field.");
                return new NativeBuildProfile(data);
            }
            catch (InvalidDataException) { throw; }
            catch (Exception error) when (error is JsonException || error is ArgumentException || error is FormatException || error is OverflowException)
            { throw new InvalidDataException("Invalid native public profile.", error); }
        }
        internal ManifestVerifier CreateVerifier() => new ManifestVerifier(new Dictionary<string, RSAParameters> { [KeyId] = publicKey });
        public void RequireSigner(RSA signer)
        {
            if (signer == null || signer.KeySize != 2048) throw new InvalidOperationException("The independently trusted RSA-2048 signer is required.");
            RSAParameters actual = signer.ExportParameters(false);
            if (!actual.Modulus.SequenceEqual(publicKey.Modulus) || !actual.Exponent.SequenceEqual(publicKey.Exponent)) throw new InvalidOperationException("The signing key does not match the trusted native public profile.");
        }
    }
}
