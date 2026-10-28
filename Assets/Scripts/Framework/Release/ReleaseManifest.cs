using System;
using System.Collections.Generic;
using System.Linq;

namespace Client.Framework.Release
{
    public enum ReleaseFileKind { NativeResource, HotfixDll, AotMetadata, Configuration }

    public enum ReleaseErrorCode
    {
        InvalidSignature, UntrustedKey, DocumentTooLarge, InvalidJson, InvalidManifest,
        UnsafePath, DuplicatePath, IncompatiblePlayer, ManifestHashMismatch, ReleaseIdMismatch
    }

    public sealed class ReleaseVerificationException : Exception
    {
        public ReleaseErrorCode Code { get; }
        public ReleaseVerificationException(ReleaseErrorCode code) : base("Release verification failed: " + code + ".") { Code = code; }
    }

    public sealed class PlayerIdentity
    {
        public string PlayerBuildId { get; }
        public string Platform { get; }
        public string Architecture { get; }
        public string UnityVersion { get; }
        public string ContractVersion { get; }
        public string EnvelopeSha256 { get; }
        public string AotMetadataSetId { get; }

        public PlayerIdentity(string playerBuildId, string platform, string architecture, string unityVersion,
            string contractVersion, string envelopeSha256, string aotMetadataSetId)
        {
            if (new[] { playerBuildId, platform, architecture, unityVersion, contractVersion, envelopeSha256, aotMetadataSetId }
                .Any(v => string.IsNullOrWhiteSpace(v) || v.Length > 256))
                throw new ArgumentException("Complete Player identity is required.");
            PlayerBuildId = playerBuildId; Platform = platform; Architecture = architecture;
            UnityVersion = unityVersion; ContractVersion = contractVersion;
            EnvelopeSha256 = envelopeSha256; AotMetadataSetId = aotMetadataSetId;
        }
    }

    public sealed class ReleaseFile
    {
        public string Path { get; }
        public long Length { get; }
        public string Sha256 { get; }
        public ReleaseFileKind Kind { get; }
        internal ReleaseFile(string path, long length, string sha256, ReleaseFileKind kind)
        { Path = path; Length = length; Sha256 = sha256; Kind = kind; }
    }

    public sealed class ReleaseAssembly
    {
        public string Name { get; }
        public string Path { get; }
        public IReadOnlyList<string> Dependencies { get; }
        internal ReleaseAssembly(string name, string path, IEnumerable<string> dependencies)
        { Name = name; Path = path; Dependencies = Array.AsReadOnly(dependencies.ToArray()); }
    }

    // Only the verifier can construct this authenticity/compatibility capability.
    public sealed class VerifiedRelease
    {
        private readonly byte[] manifestBytes;
        private readonly byte[] signatureBytes;
        public string ReleaseId { get; }
        public string KeyId { get; }
        public PlayerIdentity Player { get; }
        public string ProtocolSha256 { get; }
        public string ResourceVersionListPath { get; }
        public string ManifestSha256 { get; }
        public byte[] ManifestBytes => (byte[])manifestBytes.Clone();
        public byte[] SignatureBytes => (byte[])signatureBytes.Clone();
        public IReadOnlyList<ReleaseFile> Files { get; }
        public IReadOnlyList<ReleaseAssembly> Assemblies { get; }
        public IReadOnlyList<string> AssemblyOrder { get; }

        internal VerifiedRelease(string releaseId, string keyId, PlayerIdentity player, string protocolSha256,
            string resourceVersionListPath, string manifestSha256, byte[] manifestBytes, byte[] signatureBytes, IEnumerable<ReleaseFile> files,
            IEnumerable<ReleaseAssembly> assemblies, IEnumerable<string> assemblyOrder)
        {
            ReleaseId = releaseId; KeyId = keyId; Player = player; ProtocolSha256 = protocolSha256;
            ResourceVersionListPath = resourceVersionListPath; ManifestSha256 = manifestSha256;
            this.manifestBytes = (byte[])manifestBytes.Clone(); this.signatureBytes = (byte[])signatureBytes.Clone();
            Files = Array.AsReadOnly(files.ToArray()); Assemblies = Array.AsReadOnly(assemblies.ToArray());
            AssemblyOrder = Array.AsReadOnly(assemblyOrder.ToArray());
        }
    }
}
