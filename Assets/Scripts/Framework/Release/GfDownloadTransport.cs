using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GameFramework.Event;
using UnityGameFramework.Runtime;

namespace Client.Framework.Release
{
    // Construct and call on the Unity main thread. UGF owns resumable .download files and agents.
    public sealed class GfDownloadTransport : IReleaseTransport, IDisposable
    {
        private readonly DownloadComponent downloads;
        private readonly EventComponent events;
        private readonly string baseUri;
        private readonly SynchronizationContext context;
        private readonly Dictionary<int, TaskCompletionSource<bool>> pending = new Dictionary<int, TaskCompletionSource<bool>>();
        private bool disposed;
        public GfDownloadTransport(DownloadComponent downloads, EventComponent events, string baseUri)
        {
            this.downloads = downloads ?? throw new ArgumentNullException(nameof(downloads));
            this.events = events ?? throw new ArgumentNullException(nameof(events));
            if (!Uri.TryCreate(baseUri, UriKind.Absolute, out var uri) || (uri.Scheme != "https" && uri.Scheme != "http")) throw new ArgumentException("Absolute HTTP release base URI required.");
            this.baseUri = baseUri.TrimEnd('/') + "/";
            context = SynchronizationContext.Current ?? throw new InvalidOperationException("Unity synchronization context is required.");
            events.Subscribe(DownloadSuccessEventArgs.EventId, OnSuccess);
            events.Subscribe(DownloadFailureEventArgs.EventId, OnFailure);
        }
        public async Task DownloadAsync(string relativePath, string destination, CancellationToken token)
        {
            if (SynchronizationContext.Current != context) throw new InvalidOperationException("UGF downloads must run on the Unity main thread.");
            if (disposed) throw new ObjectDisposedException(nameof(GfDownloadTransport));
            RelativePathPolicy.RequireCanonical(relativePath); token.ThrowIfCancellationRequested();
            destination = Path.GetFullPath(destination);
            ReleaseFileSystem.RequireDownloadPath(destination);
            string uri = baseUri + string.Join("/", relativePath.Split('/').Select(Uri.EscapeDataString));
            // GF 在遍历模块时派发下载事件；启动续体会创建 UI 模块，必须排回 Unity 主线程后再执行。
            var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            int id = downloads.AddDownload(destination, uri, this);
            pending.Add(id, completion);
            using (token.Register(() => context.Post(_ =>
            {
                if (!pending.Remove(id)) return;
                downloads.RemoveDownload(id);
                completion.TrySetCanceled();
            }, null)))
            {
                try { await completion.Task; token.ThrowIfCancellationRequested(); }
                finally { pending.Remove(id); }
            }
        }
        private void OnSuccess(object sender, GameEventArgs args)
        {
            var value = (DownloadSuccessEventArgs)args;
            if (ReferenceEquals(value.UserData, this) && pending.TryGetValue(value.SerialId, out var completion)) completion.TrySetResult(true);
        }
        private void OnFailure(object sender, GameEventArgs args)
        {
            var value = (DownloadFailureEventArgs)args;
            if (ReferenceEquals(value.UserData, this) && pending.TryGetValue(value.SerialId, out var completion))
                completion.TrySetException(new ReleaseTransportException("UGF transport failure: " + value.ErrorMessage));
        }
        public void Dispose()
        {
            if (disposed) return;
            if (SynchronizationContext.Current != context) throw new InvalidOperationException("UGF download must be disposed on the Unity main thread.");
            disposed = true;
            // Unity 不保证宿主与 GF BaseComponent 的销毁顺序；GF Shutdown 可能已清空事件池。
            if (events != null && events.Check(DownloadSuccessEventArgs.EventId, OnSuccess)) events.Unsubscribe(DownloadSuccessEventArgs.EventId, OnSuccess);
            if (events != null && events.Check(DownloadFailureEventArgs.EventId, OnFailure)) events.Unsubscribe(DownloadFailureEventArgs.EventId, OnFailure);
            var cancelled = pending.ToArray(); pending.Clear();
            foreach (var item in cancelled) { if (downloads != null) downloads.RemoveDownload(item.Key); item.Value.TrySetCanceled(); }
        }
    }
}
