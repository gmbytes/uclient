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

        public BuildRequest(BuildKind kind, BuildPlatform platform, string outputRoot, string playerArchive = null)
        { Kind = kind; Platform = platform; OutputRoot = outputRoot; PlayerArchive = playerArchive; }
    }
}
