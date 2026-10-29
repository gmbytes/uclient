using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

namespace Client.Framework.Release
{
    public interface IReleaseStreamReader { Task<Stream> OpenReadAsync(string relativePath, CancellationToken token); }
    public sealed class PlatformReleaseStreamReader : IReleaseStreamReader
    {
        private readonly string root;
        private readonly string temporaryRoot;
        private readonly bool android;
        public PlatformReleaseStreamReader() : this(Application.streamingAssetsPath, Application.platform == RuntimePlatform.Android) { }
        public PlatformReleaseStreamReader(string streamingAssetsRoot, bool android, string temporaryRoot = null)
        {
            root = streamingAssetsRoot ?? throw new ArgumentNullException(nameof(streamingAssetsRoot)); this.android = android;
            this.temporaryRoot = ReleaseFileSystem.Root(temporaryRoot ?? Path.Combine(ReleaseStore.PersistentRoot, "staging", "platform-streams"));
        }
        public async Task<Stream> OpenReadAsync(string relativePath, CancellationToken token)
        {
            RelativePathPolicy.RequireCanonical(relativePath); token.ThrowIfCancellationRequested();
            if (!android) return new FileStream(ReleaseFileSystem.Child(ReleaseFileSystem.Root(root), relativePath), FileMode.Open, FileAccess.Read, FileShare.Read);
            // Android's APK jar URI is read through Unity's platform transport into bounded-memory scratch.
            string temporary = ReleaseFileSystem.Child(temporaryRoot, Guid.NewGuid().ToString("N") + ".partial");
            Directory.CreateDirectory(temporaryRoot);
            try
            {
                string uri = root.TrimEnd('/') + "/" + string.Join("/", relativePath.Split('/').Select(Uri.EscapeDataString));
                using (var request = UnityWebRequest.Get(uri))
                {
                    request.downloadHandler = new DownloadHandlerFile(temporary) { removeFileOnAbort = true };
                    UnityWebRequestAsyncOperation operation = request.SendWebRequest();
                    while (!operation.isDone)
                    {
                        if (token.IsCancellationRequested) { request.Abort(); token.ThrowIfCancellationRequested(); }
                        await Task.Yield();
                    }
                    token.ThrowIfCancellationRequested();
                    if (request.result != UnityWebRequest.Result.Success) throw new IOException("Baseline platform stream failed: " + request.error);
                }
                token.ThrowIfCancellationRequested();
                return new FileStream(temporary, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, FileOptions.DeleteOnClose);
            }
            catch (Exception error)
            {
                try { if (File.Exists(temporary)) File.Delete(temporary); }
                catch (Exception cleanup) when (cleanup is IOException || cleanup is UnauthorizedAccessException)
                { throw new AggregateException("Baseline stream and owned scratch cleanup failed.", error, cleanup); }
                throw;
            }
        }
    }
    public sealed class BaseReleaseInstaller
    {
        private readonly ReleaseStore store;
        public BaseReleaseInstaller(string root, ReleaseStateStore state, DiskBudget budget = null, IFileFaultInjector faults = null)
            : this(root, new PlatformReleaseStreamReader(Application.streamingAssetsPath, Application.platform == RuntimePlatform.Android,
                Path.Combine(root, "staging", "platform-streams")), state, budget, faults) { }
        public BaseReleaseInstaller(string root, IReleaseStreamReader reader, ReleaseStateStore state, DiskBudget budget = null, IFileFaultInjector faults = null)
        { store = new ReleaseStore(root, new ReleaseDownloader(new BaselineTransport(reader)), state, budget, faults); }
        public Task<PreparedRelease> InstallAsync(VerifiedRelease release, IProgress<ReleaseProgress> progress, CancellationToken token)
            => store.PrepareAsync(release, progress, token);
        private sealed class BaselineTransport : IReleaseTransport
        {
            private readonly IReleaseStreamReader reader;
            internal BaselineTransport(IReleaseStreamReader reader) { this.reader = reader ?? throw new ArgumentNullException(nameof(reader)); }
            public async Task DownloadAsync(string relativePath, string destination, CancellationToken token)
            {
                using (Stream source = await reader.OpenReadAsync(relativePath, token))
                using (var target = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    var buffer = new byte[64 * 1024];
                    int count;
                    while ((count = await source.ReadAsync(buffer, 0, buffer.Length, token)) > 0)
                    { token.ThrowIfCancellationRequested(); await target.WriteAsync(buffer, 0, count, token); }
                    target.Flush(true);
                }
            }
        }
    }
}
