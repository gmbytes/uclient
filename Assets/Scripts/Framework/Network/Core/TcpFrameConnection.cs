using System;
using System.Threading;
using System.Threading.Tasks;

namespace Client.Framework.Network
{
    public sealed class TcpFrameConnection : IFrameConnection
    {
        private readonly object gate = new object();
        private readonly IFrameTransport transport;
        private CancellationTokenSource lifetime;
        private long epoch;
        // Managed adapter callbacks run on its pump thread and complete before the next ReadAsync.
        public event Action<byte[]> FrameReceived;
        public event Action<TransportException> TransportFailed;
        public TcpFrameConnection(IFrameTransport transport = null) { this.transport = transport ?? new TcpFrameTransport(); }
        public async Task ConnectAsync(string host, int port, CancellationToken token)
        {
            CancellationTokenSource life; long generation;
            lock (gate)
            {
                Close(); lifetime = life = new CancellationTokenSource(); generation = epoch;
            }
            try
            {
                using (var linked = CancellationTokenSource.CreateLinkedTokenSource(token, life.Token))
                    await transport.ConnectAsync(host, port, linked.Token).ConfigureAwait(false);
                lock (gate)
                {
                    if (generation != epoch) throw new TransportException(TransportError.Closed, "Connection was replaced.");
                    _ = Task.Run(() => Pump(generation, life));
                }
            }
            catch
            {
                lock (gate) { if (ReferenceEquals(lifetime, life)) lifetime = null; }
                life.Dispose(); throw;
            }
        }
        public Task SendAsync(ReadOnlyMemory<byte> frame, CancellationToken token) => transport.SendAsync(frame, token);
        public void Close()
        {
            lock (gate) { epoch++; lifetime?.Cancel(); lifetime = null; transport.Close(); }
        }
        private async Task Pump(long generation, CancellationTokenSource life)
        {
            try
            {
                while (!life.IsCancellationRequested)
                {
                    var frame = await transport.ReadAsync(life.Token).ConfigureAwait(false);
                    lock (gate)
                    {
                        if (generation != epoch || life.IsCancellationRequested) return;
                        FrameReceived?.Invoke(frame);
                    }
                }
            }
            catch (Exception ex)
            {
                lock (gate)
                {
                    if (generation != epoch || life.IsCancellationRequested) return;
                    transport.Close();
                    var error = ex as TransportException ?? new TransportException(TransportError.SocketError, "Connection frame pump failed.", ex);
                    try { TransportFailed?.Invoke(error); }
                    catch (Exception callbackError)
                    {
                        // Observer code cannot leave a live socket or an unobserved pump task.
                        System.Diagnostics.Trace.TraceError("TransportFailed observer threw {0}.", callbackError.GetType().FullName);
                    }
                }
            }
            finally { lock (gate) { if (ReferenceEquals(lifetime, life)) lifetime = null; } life.Dispose(); }
        }
    }
}
