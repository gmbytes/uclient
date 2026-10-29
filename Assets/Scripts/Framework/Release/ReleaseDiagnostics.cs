using System;
using System.Collections.Generic;
using System.Linq;

namespace Client.Framework.Release
{
    public enum ReleasePhase { Idle, Check, Prepare, Select, Trial, ResourceInit, Metadata, Hotfix, LoginReady, Ready, PreparationFailed, RestartRequired, Stopped }
    public sealed class ReleaseDiagnostics
    {
        private readonly object sync = new object();
        private readonly List<ReleaseFailureRecord> failures = new List<ReleaseFailureRecord>();
        public IReadOnlyList<ReleaseFailureRecord> Failures { get { lock (sync) return Array.AsReadOnly(failures.ToArray()); } }
        public ReleasePhase Phase { get; internal set; }
        public string Code { get; internal set; }
        public Exception Error { get; internal set; }
        public ReleaseProgress Progress { get; internal set; }
        public ReleaseSelectionSnapshot Selection { get; internal set; }
        internal void RecordFailure(string releaseId, long epoch, ReleasePhase phase, string code, Exception exception)
        {
            if (exception == null) return;
            lock (sync)
            {
                if (failures.Any(record => ReferenceEquals(record.Exception, exception) && record.Epoch == epoch && record.Phase == phase && record.Code == code)) return;
                failures.Add(new ReleaseFailureRecord(releaseId, epoch, phase, code, exception));
            }
        }
    }
    /// <summary>Immutable observation of the real authenticated selection, never a launch capability.</summary>
    public sealed class ReleaseSelectionSnapshot
    {
        public string ReleaseId { get; }
        public long Epoch { get; }
        public string ManifestSha256 { get; }
        public string Root { get; }
        internal ReleaseSelectionSnapshot(LaunchSelection selection)
        { ReleaseId = selection.ReleaseId; Epoch = selection.Epoch; ManifestSha256 = selection.Prepared.Release.ManifestSha256; Root = selection.Prepared.Root; }
    }
    /// <summary>Immutable local evidence, independent of the current UI phase. Never displayed as raw UI text.</summary>
    public sealed class ReleaseFailureRecord
    {
        public string ReleaseId { get; }
        public long Epoch { get; }
        public ReleasePhase Phase { get; }
        public string Code { get; }
        public Exception Exception { get; }
        public DateTime ObservedAtUtc { get; }
        internal ReleaseFailureRecord(string releaseId, long epoch, ReleasePhase phase, string code, Exception exception)
        { ReleaseId = releaseId; Epoch = epoch; Phase = phase; Code = code; Exception = exception; ObservedAtUtc = DateTime.UtcNow; }
    }
}
