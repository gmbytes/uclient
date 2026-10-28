using System;
using System.Threading;
using System.Threading.Tasks;
using UnityGameFramework.Runtime;

namespace Client.Framework.Network
{
    public sealed class TokNetworkComponent : GameFrameworkComponent, IFrameConnection
    {
        private readonly object gate = new object();
        private readonly MainThreadFrameQueue queue = new MainThreadFrameQueue();
        private IFrameTransport transport;
        private CancellationTokenSource lifetime;
        private TaskCompletionSource<bool> delivery;
        private TransportException pendingError;
        private long epoch;
        private int mainThread;
        public event Action<byte[]> FrameReceived;
        public event Action<TransportException> TransportFailed;

        protected override void Awake() { base.Awake(); mainThread = Thread.CurrentThread.ManagedThreadId; }
        public Task ConnectAsync(string host, int port, CancellationToken token) => ConnectAsync(new TcpFrameTransport(), host, port, token);
        public async Task ConnectAsync(IFrameTransport suppliedTransport, string host, int port, CancellationToken token)
        {
            RequireMainThread();
            if (suppliedTransport == null) throw new ArgumentNullException(nameof(suppliedTransport));
            Close();
            CancellationTokenSource life; long generation;
            lock (gate) { transport = suppliedTransport; lifetime = life = new CancellationTokenSource(); generation = epoch; }
            try
            {
                using (var linked = CancellationTokenSource.CreateLinkedTokenSource(token, life.Token))
                    await suppliedTransport.ConnectAsync(host, port, linked.Token).ConfigureAwait(false);
                lock (gate)
                {
                    if (generation != epoch) throw new TransportException(TransportError.Closed, "Host connection replaced.");
                }
                _ = Pump(suppliedTransport, generation, life);
            }
            catch
            {
                lock (gate) { if (generation == epoch) suppliedTransport.Close(); if (ReferenceEquals(lifetime, life)) lifetime = null; }
                life.Dispose(); throw;
            }
        }
        public Task SendAsync(ReadOnlyMemory<byte> frame, CancellationToken token)
        {
            lock (gate) return transport?.SendAsync(frame, token) ?? Task.FromException(new TransportException(TransportError.NotConnected, "Host has no connection."));
        }
        public void Close()
        {
            RequireMainThread();
            lock (gate)
            {
                epoch++; queue.BeginEpoch(epoch); pendingError = null;
                lifetime?.Cancel(); lifetime = null; delivery = null;
                transport?.Close(); transport = null;
            }
        }
        private async Task Pump(IFrameTransport captured, long generation, CancellationTokenSource life)
        {
            try
            {
                while (!life.IsCancellationRequested)
                {
                    var frame = await captured.ReadAsync(life.Token).ConfigureAwait(false);
                    var delivered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                    lock (gate)
                    {
                        if (generation != epoch || life.IsCancellationRequested) return;
                        if (!queue.TryEnqueue(generation, frame)) throw new TransportException(TransportError.Backpressure, "Main-thread frame queue limit exceeded.");
                        delivery = delivered;
                    }
                    // Do not request another frame until callbacks return. The transport retains this
                    // frame's receive reservation, sharing its 64 frame / 4 MiB budget with the host.
                    using (life.Token.Register(() => delivered.TrySetCanceled()))
                        await delivered.Task.ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) when (life.IsCancellationRequested) { }
            catch (TransportException ex)
            {
                lock (gate)
                {
                    if (generation != epoch || life.IsCancellationRequested) return;
                    captured.Close(); pendingError = ex;
                }
            }
            finally { lock (gate) { if (ReferenceEquals(lifetime, life)) lifetime = null; } life.Dispose(); }
        }
        private void Update()
        {
            long generation; lock (gate) generation = epoch;
            queue.Drain(generation, frame =>
            {
                TaskCompletionSource<bool> completed;
                lock (gate) { if (generation != epoch) return; completed = delivery; delivery = null; }
                try { FrameReceived?.Invoke(frame); }
                finally { completed?.TrySetResult(true); }
            });
            TransportException error;
            lock (gate) { error = pendingError; pendingError = null; }
            if (error != null) TransportFailed?.Invoke(error);
        }
        private void OnApplicationQuit() => Close();
        private void OnDestroy() => Close();
        private void RequireMainThread()
        {
            if (mainThread != 0 && Thread.CurrentThread.ManagedThreadId != mainThread)
                throw new InvalidOperationException("Network host lifecycle must run on its owning main thread.");
        }
    }
}
