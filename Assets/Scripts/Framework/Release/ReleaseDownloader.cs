using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Client.Framework.Release
{
    public interface IReleaseTransport { Task DownloadAsync(string relativePath, string destination, CancellationToken token); }
    public sealed class ReleaseTransportException : IOException
    { public ReleaseTransportException(string message) : base(message) { } }
    public sealed class ReleaseDownloader
    {
        private readonly IReleaseTransport transport;
        private readonly Func<TimeSpan, CancellationToken, Task> delay;
        private readonly SemaphoreSlim transfers = new SemaphoreSlim(2, 2);
        public ReleaseDownloader(IReleaseTransport transport, Func<TimeSpan, CancellationToken, Task> delay = null)
        { this.transport = transport ?? throw new ArgumentNullException(nameof(transport)); this.delay = delay ?? Task.Delay; }
        public async Task DownloadAsync(ReleaseFile file, string destination, CancellationToken token)
        {
            await transfers.WaitAsync(token);
            try
            {
                for (int attempt = 0; ; attempt++)
                {
                    token.ThrowIfCancellationRequested();
                    try { await transport.DownloadAsync(file.Path, destination, token); break; }
                    catch (ReleaseTransportException) when (attempt < 2)
                    { await delay(TimeSpan.FromSeconds(attempt + 1), token); }
                }
                token.ThrowIfCancellationRequested();
                ReleaseFileSystem.VerifyFile(destination, file, token);
            }
            finally { transfers.Release(); }
        }
    }
}
