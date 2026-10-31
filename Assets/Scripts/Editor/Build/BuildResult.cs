using System.Collections.Generic;
using System.IO;

namespace Client.Editor.Build
{
    public enum BuildStatus { Pending, Succeeded, Failed }

    public sealed class BuildResult
    {
        public BuildStatus Status { get; internal set; } = BuildStatus.Pending;
        public string OutputDirectory { get; }
        public string ResultFile { get; }
        public string PlayerBuildId { get; internal set; }
        public string ReleaseId { get; internal set; }
        public List<string> Errors { get; } = new List<string>();
        public List<string> EvidenceFiles { get; } = new List<string>();

        internal BuildResult(string outputDirectory)
        { OutputDirectory = outputDirectory; ResultFile = Path.Combine(outputDirectory, "build-result.json"); }
    }
}
