using System;

namespace Client.Framework.Release
{
    public enum ReleasePhase { Idle, Check, Prepare, Select, Trial, ResourceInit, Metadata, Hotfix, LoginReady, Ready, PreparationFailed, RestartRequired, Stopped }
    public sealed class ReleaseDiagnostics
    {
        public ReleasePhase Phase { get; internal set; }
        public string Code { get; internal set; }
        public Exception Error { get; internal set; }
        public ReleaseProgress Progress { get; internal set; }
    }
}