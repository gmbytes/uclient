using System;
using System.IO;
using System.Security.Cryptography;
using Client.Framework.Release;

namespace Client.Editor.Build
{
    public static class ReleaseManifestWriter
    {
        // Editor-only signer. Callers obtain the signing key outside the repository.
        public static void WriteSigned(string outputDirectory, byte[] json, string keyId, RSA signingKey, PlayerIdentity player)
        {
            if (string.IsNullOrWhiteSpace(keyId) || keyId.StartsWith("test", StringComparison.OrdinalIgnoreCase) || keyId.StartsWith("dev", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Production signing requires a production key ID.");
            if (json == null || json.Length == 0 || json.Length > 256 * 1024 || signingKey == null || signingKey.KeySize != 2048)
                throw new ArgumentException("A bounded document and RSA-2048 signer are required.");
            byte[] raw = (byte[])json.Clone();
            byte[] signature = signingKey.SignData(raw, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            VerifiedRelease verified = new ManifestVerifier().Verify(raw, signature, player);
            if (verified.KeyId != keyId) throw new InvalidOperationException("Signing key ID does not match the document.");
            Directory.CreateDirectory(outputDirectory);
            File.WriteAllBytes(Path.Combine(outputDirectory, "release.json"), raw);
            File.WriteAllBytes(Path.Combine(outputDirectory, "release.sig"), signature);
        }
    }
}
