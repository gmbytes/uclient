using System;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;

namespace Client.Framework.Network
{
    public sealed class TcpFrameTransport : IFrameTransport
    {
        private readonly object gate = new object();
        private readonly TransportOptions options;
        private readonly ITransportClock clock;
        private readonly Func<int, byte[]> allocate;
        private Connection current;

        public TcpFrameTransport(TransportOptions options = null, ITransportClock clock = null, Func<int, byte[]> allocateBuffer = null)
        {
            this.options = options ?? new TransportOptions();
            this.clock = clock ?? new TransportClock();
            allocate = allocateBuffer ?? (n => new byte[n]);
        }

        public async Task ConnectAsync(string host, int port, CancellationToken token)
        {
            if (string.IsNullOrWhiteSpace(host)) throw new ArgumentException("A host is required.", nameof(host));
            if (port < 1 || port > 65535) throw new ArgumentOutOfRangeException(nameof(port));
            var client = IPAddress.TryParse(host, out var ip) ? new TcpClient(ip.AddressFamily) : new TcpClient();
            var connection = new Connection(client);
            lock (gate)
            {
                current?.Stop(Error(TransportError.Closed, "Connection replaced."));
                current = connection;
            }
            using (token.Register(() => connection.Stop(Error(TransportError.Cancelled, "Connect cancelled."))))
            {
                try
                {
                    await Deadline(connection, () => client.ConnectAsync(host, port), options.ConnectTimeout).ConfigureAwait(false);
                    lock (connection.Gate)
                    {
                        connection.ThrowIfStopped();
                        connection.Stream = client.GetStream();
                        connection.Connected = true;
                    }
                    // Each loop catches and records its own terminal error. No unobserved fire-and-forget faults.
                    _ = ReadLoop(connection);
                    _ = WriteLoop(connection);
                }
                catch (Exception ex)
                {
                    connection.Stop(Map(connection, ex));
                    throw connection.Error;
                }
            }
        }

        public Task SendAsync(ReadOnlyMemory<byte> frame, CancellationToken token)
        {
            // Reject malformed framing before copying memory or admitting it into a queue.
            try
            {
                if (frame.Length < 4 || FrameCodec.ReadLength(frame.Span.Slice(0, 4)) != frame.Length - 4)
                    return Task.FromException(Error(TransportError.InvalidFrame, "Send requires exactly one framed body."));
            }
            catch (TransportException ex) { return Task.FromException(ex); }
            Connection connection;
            try { connection = GetConnection(); }
            catch (TransportException ex) { return Task.FromException(ex); }
            if (token.IsCancellationRequested)
            {
                connection.Stop(Error(TransportError.Cancelled, "Send cancelled."));
                return Task.FromException(connection.Error);
            }
            WriteRequest request;
            lock (connection.Gate)
            {
                if (connection.Error != null) return Task.FromException(connection.Error);
                if (connection.OutgoingItems >= options.MaxQueueItems || frame.Length > options.MaxQueueBytes - connection.OutgoingBytes)
                {
                    connection.Stop(Error(TransportError.Backpressure, "Outgoing queue limit exceeded."));
                    return Task.FromException(connection.Error);
                }
                request = new WriteRequest(frame.ToArray(), clock.Elapsed + options.WriteFrameTimeout);
                connection.Outgoing.Enqueue(request);
                connection.OutgoingItems++; connection.OutgoingBytes += frame.Length;
                connection.Writable.Release();
            }
            return CompleteSend(connection, request, token);
        }

        private static async Task CompleteSend(Connection connection, WriteRequest request, CancellationToken token)
        {
            using (token.Register(() => connection.Stop(Error(TransportError.Cancelled, "Send cancelled."))))
                await request.Completion.Task.ConfigureAwait(false);
        }

        public async Task<byte[]> ReadAsync(CancellationToken token)
        {
            var connection = GetConnection();
            bool ownsRead = false;
            using (token.Register(() => connection.Stop(Error(TransportError.Cancelled, "Read cancelled."))))
            {
                try
                {
                    lock (connection.Gate)
                    {
                        connection.ThrowIfStopped();
                        if (connection.ReadPending) throw new InvalidOperationException("Only one ReadAsync consumer is supported.");
                        connection.ReadPending = ownsRead = true;
                        if (connection.DeliveredLength > 0)
                        {
                            connection.IncomingItems--; connection.IncomingBytes -= connection.DeliveredLength;
                            connection.DeliveredLength = 0;
                        }
                    }
                    await connection.Readable.WaitAsync(connection.Lifetime.Token).ConfigureAwait(false);
                    lock (connection.Gate)
                    {
                        connection.ThrowIfStopped();
                        var result = connection.Incoming.Dequeue();
                        connection.DeliveredLength = result.Length;
                        return result;
                    }
                }
                catch (Exception ex) { throw Map(connection, ex); }
                finally { if (ownsRead) lock (connection.Gate) connection.ReadPending = false; }
            }
        }

        public void Close()
        {
            lock (gate) current?.Stop(Error(TransportError.Closed, "Transport closed."));
        }

        private Connection GetConnection()
        {
            lock (gate)
            {
                if (current == null) throw Error(TransportError.NotConnected, "Transport has no connection.");
                lock (current.Gate)
                {
                    current.ThrowIfStopped();
                    if (!current.Connected) throw Error(TransportError.NotConnected, "Connect has not completed.");
                    return current;
                }
            }
        }

        private async Task ReadLoop(Connection connection)
        {
            try
            {
                while (true)
                {
                    await Deadline(connection, async () =>
                    {
                        var prefix = new byte[4];
                        await ReadExactly(connection, prefix, 0, 4, true).ConfigureAwait(false);
                        int length = FrameCodec.ReadLength(prefix);
                        lock (connection.Gate)
                        {
                            connection.ThrowIfStopped();
                            if (connection.IncomingItems >= options.MaxQueueItems || length + 4 > options.MaxQueueBytes - connection.IncomingBytes)
                                throw Error(TransportError.Backpressure, "Incoming queue limit exceeded.");
                            // Reserve before allocation, including the body currently being read.
                            connection.IncomingItems++; connection.IncomingBytes += length + 4;
                        }
                        var frame = allocate(length + 4);
                        if (frame == null || frame.Length != length + 4) throw new InvalidOperationException("Frame allocator returned the wrong length.");
                        Buffer.BlockCopy(prefix, 0, frame, 0, 4);
                        await ReadExactly(connection, frame, 4, length, false).ConfigureAwait(false);
                        lock (connection.Gate)
                        {
                            connection.ThrowIfStopped();
                            connection.Incoming.Enqueue(frame); connection.Readable.Release();
                        }
                    }, options.ReadFrameTimeout).ConfigureAwait(false);
                }
            }
            catch (Exception ex) { connection.Stop(Map(connection, ex)); }
        }

        private static async Task ReadExactly(Connection connection, byte[] buffer, int offset, int count, bool prefix)
        {
            int read = 0;
            while (read < count)
            {
                int n = await connection.Stream.ReadAsync(buffer, offset + read, count - read).ConfigureAwait(false);
                if (n == 0) throw Error(prefix && read == 0 ? TransportError.EndOfStream : TransportError.TruncatedFrame,
                    prefix && read == 0 ? "Peer closed between frames." : "Peer closed inside a frame.");
                read += n;
            }
        }

        private async Task WriteLoop(Connection connection)
        {
            try
            {
                while (true)
                {
                    await connection.Writable.WaitAsync(connection.Lifetime.Token).ConfigureAwait(false);
                    WriteRequest request;
                    lock (connection.Gate)
                    {
                        connection.ThrowIfStopped();
                        request = connection.Outgoing.Dequeue(); connection.ActiveWrite = request;
                    }
                    await Deadline(connection, () => connection.Stream.WriteAsync(request.Frame, 0, request.Frame.Length),
                        request.Deadline - clock.Elapsed).ConfigureAwait(false);
                    lock (connection.Gate)
                    {
                        connection.ThrowIfStopped();
                        connection.ActiveWrite = null;
                        connection.OutgoingItems--; connection.OutgoingBytes -= request.Frame.Length;
                        request.Completion.TrySetResult(true);
                    }
                }
            }
            catch (Exception ex) { connection.Stop(Map(connection, ex)); }
        }

        private async Task Deadline(Connection connection, Func<Task> operation, TimeSpan duration)
        {
            connection.ThrowIfStopped();
            if (duration <= TimeSpan.Zero)
            {
                connection.Stop(Error(TransportError.Timeout, "Frame operation deadline expired."));
                throw connection.Error;
            }
            var end = clock.Elapsed + duration;
            using (var alarm = CancellationTokenSource.CreateLinkedTokenSource(connection.Lifetime.Token))
            {
                try
                {
                    var expired = clock.Delay(duration, alarm.Token);
                    Task io = operation();
                    Task winner = await Task.WhenAny(io, expired).ConfigureAwait(false);
                    if (winner != io || clock.Elapsed >= end)
                    {
                        connection.Stop(connection.Error ?? Error(TransportError.Timeout, "Frame operation deadline expired."));
                        // Unity's Mono can leave ConnectAsync incomplete after Close. Never await
                        // aborted I/O here; observe a later fault without delaying the finite result.
                        _ = io.ContinueWith(t => { var observed = t.Exception; }, CancellationToken.None,
                            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
                        throw connection.Error;
                    }
                    await io.ConfigureAwait(false);
                    connection.ThrowIfStopped();
                }
                finally { alarm.Cancel(); }
            }
        }

        private static TransportException Error(TransportError code, string message) => new TransportException(code, message);
        private static TransportException Map(Connection connection, Exception ex) =>
            connection.Error ?? ex as TransportException ?? new TransportException(TransportError.SocketError, "TCP operation failed.", ex);

        private sealed class WriteRequest
        {
            public readonly byte[] Frame;
            public readonly TimeSpan Deadline;
            public readonly TaskCompletionSource<bool> Completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            public WriteRequest(byte[] frame, TimeSpan deadline) { Frame = frame; Deadline = deadline; }
        }

        private sealed class Connection
        {
            public readonly object Gate = new object();
            public readonly TcpClient Client;
            public readonly CancellationTokenSource Lifetime = new CancellationTokenSource();
            public readonly SemaphoreSlim Readable = new SemaphoreSlim(0);
            public readonly SemaphoreSlim Writable = new SemaphoreSlim(0);
            public readonly Queue<byte[]> Incoming = new Queue<byte[]>();
            public readonly Queue<WriteRequest> Outgoing = new Queue<WriteRequest>();
            public NetworkStream Stream;
            public bool Connected;
            public int IncomingItems, IncomingBytes, OutgoingItems, OutgoingBytes;
            public int DeliveredLength;
            public bool ReadPending;
            public WriteRequest ActiveWrite;
            private volatile TransportException error;
            public TransportException Error => error;
            public Connection(TcpClient client) { Client = client; }
            public void ThrowIfStopped() { if (Error != null) throw Error; }
            public void Stop(TransportException error)
            {
                lock (Gate)
                {
                    if (Error != null) return;
                    this.error = error; Connected = false;
                    Incoming.Clear(); IncomingItems = IncomingBytes = DeliveredLength = 0;
                    ActiveWrite?.Completion.TrySetException(error);
                    while (Outgoing.Count > 0) Outgoing.Dequeue().Completion.TrySetException(error);
                    OutgoingItems = OutgoingBytes = 0;
                    try { Client.Close(); } finally { Lifetime.Cancel(); }
                }
            }
        }
    }
}
