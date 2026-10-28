namespace Client.Framework.Release
{
    public sealed class VerifiedChannelPointer
    {
        public string ReleaseId { get; }
        public string ManifestRelativePath { get; }
        public string ManifestSha256 { get; }
        public string KeyId { get; }
        internal VerifiedChannelPointer(string releaseId, string path, string hash, string keyId)
        { ReleaseId = releaseId; ManifestRelativePath = path; ManifestSha256 = hash; KeyId = keyId; }
    }
}
