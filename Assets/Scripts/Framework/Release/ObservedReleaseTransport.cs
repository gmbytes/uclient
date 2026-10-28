using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Client.Framework.Release
{
    public sealed class ReleaseTransferObservation
    {
        public string Path { get; } public DateTime StartedUtc { get; } public DateTime FinishedUtc { get; }
        public bool Success { get; } public long Bytes { get; } public string Error { get; }
        internal ReleaseTransferObservation(string path, DateTime start, bool success, long bytes, Exception error)
        { Path = path; StartedUtc = start; FinishedUtc = DateTime.UtcNow; Success = success; Bytes = bytes; Error = error?.ToString(); }
    }
    /// <summary>Records actual transport completion without altering requests, exceptions, cancellation or ownership.</summary>
    public sealed class ObservedReleaseTransport : IReleaseTransport, IDisposable
    {
        private readonly IReleaseTransport inner;
        private readonly List<ReleaseTransferObservation> records = new List<ReleaseTransferObservation>();
        public IReadOnlyList<ReleaseTransferObservation> Records => records.AsReadOnly();
        public ObservedReleaseTransport(IReleaseTransport inner) { this.inner = inner ?? throw new ArgumentNullException(nameof(inner)); }
        public async Task DownloadAsync(string path, string destination, CancellationToken token)
        {
            DateTime start = DateTime.UtcNow; Exception failure = null; bool success = false;
            try { await inner.DownloadAsync(path, destination, token); success = true; }
            catch (Exception error) { failure = error; throw; }
            finally { records.Add(new ReleaseTransferObservation(path, start, success, File.Exists(destination) ? new FileInfo(destination).Length : 0, failure)); }
        }
        public void Dispose() { (inner as IDisposable)?.Dispose(); }
    }
}
