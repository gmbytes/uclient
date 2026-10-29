using System;
using System.IO;
using System.Security.Cryptography;
using Client.Framework.Release;

namespace Client.Editor.Build
{
    public static class ReleaseManifestWriter
    {
        public static void WriteSignedWithProfile(string outputDirectory,byte[] json,NativeBuildProfile profile,RSA signingKey,PlayerIdentity player)
        {
            if(profile==null)throw new ArgumentNullException(nameof(profile));profile.RequireSigner(signingKey);
            if(profile.KeyId.StartsWith("test",StringComparison.OrdinalIgnoreCase)||profile.KeyId.StartsWith("dev",StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("A non-test/development signing key ID is required.");
            byte[] raw=(byte[])(json??throw new ArgumentNullException(nameof(json))).Clone();
            byte[] signature=signingKey.SignData(raw,HashAlgorithmName.SHA256,RSASignaturePadding.Pkcs1);
            var release=profile.CreateVerifier().Verify(raw,signature,player);
            if(release.KeyId!=profile.KeyId)throw new InvalidOperationException("The document key ID differs from the trusted public profile.");
            Directory.CreateDirectory(outputDirectory);File.WriteAllBytes(Path.Combine(outputDirectory,"release.json"),raw);File.WriteAllBytes(Path.Combine(outputDirectory,"release.sig"),signature);
        }
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
