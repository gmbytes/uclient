namespace Client.Editor.Build
{
    public enum BuildKind { Player, Patch }
    public enum BuildPlatform { Android, Windows64 }

    public sealed class BuildRequest
    {
        public BuildKind Kind { get; }
        public BuildPlatform Platform { get; }
        public string OutputRoot { get; }
        public string PlayerArchive { get; }
        public string PublicProfilePath { get; }
        public string SigningKeyPath { get; }
        public string LinkerPolicyPath { get; }
        public string ReleaseId { get; }
        public string HotfixOverlay { get; }

        public BuildRequest(BuildKind kind, BuildPlatform platform, string outputRoot, string playerArchive = null)
            : this(kind,platform,outputRoot,playerArchive,null,null,null,kind==BuildKind.Player?"v1":"v2",null) { }
        public BuildRequest(BuildKind kind, BuildPlatform platform, string outputRoot, string playerArchive, string publicProfilePath, string signingKeyPath, string linkerPolicyPath, string releaseId, string hotfixOverlay)
        { Kind = kind; Platform = platform; OutputRoot = outputRoot; PlayerArchive = playerArchive; PublicProfilePath=publicProfilePath; SigningKeyPath=signingKeyPath; LinkerPolicyPath=linkerPolicyPath; ReleaseId=releaseId; HotfixOverlay=hotfixOverlay; }
    }
}
