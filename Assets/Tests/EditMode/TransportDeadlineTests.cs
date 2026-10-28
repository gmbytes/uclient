using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using System.IO;
using Client.Framework.Network;
using NUnit.Framework;

namespace Client.Tests.EditMode
{
    [Category("N2")]
    public sealed class TransportDeadlineTests
    {
        [TestCase(0u), TestCase(1048577u), TestCase(uint.MaxValue)]
        public void RejectsLengthBeforeAllocating(uint length) => Run(() => RejectsLengthBeforeAllocatingAsync(length));
        private async Task RejectsLengthBeforeAllocatingAsync(uint length)
        {
            int allocations = 0;
            using (var pair = await Pair.Open(allocate: n => { Interlocked.Increment(ref allocations); return new byte[n]; }))
            {
                await pair.Peer.GetStream().WriteAsync(FrameCodecTests.Prefix(length), 0, 4);
                Assert.That((await Failure(pair.Transport.ReadAsync(CancellationToken.None))).Code, Is.EqualTo(TransportError.InvalidFrame));
                Assert.That(allocations, Is.Zero, "Invalid uint length must never reach the payload allocator.");
            }
        }

        [Test]
        public void DeadlineIncludesWaitingForFirstByte() => Run(DeadlineIncludesWaitingForFirstByteAsync);
        private async Task DeadlineIncludesWaitingForFirstByteAsync()
        {
            var clock = new ManualClock();
            using (var pair = await Pair.Open(clock: clock))
            {
                await clock.WaitForReadDeadline();
                var read = pair.Transport.ReadAsync(CancellationToken.None);
                clock.Advance(15);
                Assert.That((await Failure(read)).Code, Is.EqualTo(TransportError.Timeout));
            }
        }

        [Test]
        public void DripBytesCannotExtendFrameDeadline() => Run(DripBytesCannotExtendFrameDeadlineAsync);
        private async Task DripBytesCannotExtendFrameDeadlineAsync()
        {
            var clock = new ManualClock();
            using (var pair = await Pair.Open(clock: clock))
            {
                await clock.WaitForReadDeadline();
                await pair.Peer.GetStream().WriteAsync(new byte[] { 0 }, 0, 1);
                clock.Advance(14);
                await pair.Peer.GetStream().WriteAsync(new byte[] { 0 }, 0, 1);
                clock.Advance(1);
                Assert.That((await Failure(pair.Transport.ReadAsync(CancellationToken.None))).Code, Is.EqualTo(TransportError.Timeout));
            }
        }

        [Test]
        public void LateBodyCannotReachConsumerBeforeDeadlineValidation() => Run(async () =>
        {
            var clock = new LateCompletionClock();
            var allocated = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            using (var pair = await Pair.Open(clock: clock, allocate: n => { allocated.TrySetResult(true); return new byte[n]; }))
            {
                await pair.Peer.GetStream().WriteAsync(new byte[] { 0,0,0,2 },0,4);
                await WithLimit(allocated.Task); // Reader is waiting for the body, with its deadline already fixed.
                clock.AtFinalValidation = () => pair.Transport.ReadAsync(CancellationToken.None);
                await pair.Peer.GetStream().WriteAsync(new byte[] { 8,3 },0,2);
                await WithLimit(clock.Validating.Task);
                Assert.That((await Failure(clock.ConsumerRead)).Code, Is.EqualTo(TransportError.Timeout),
                    "A consumer at final deadline validation must never observe the late frame.");
                Assert.That((await Failure(pair.Transport.ReadAsync(CancellationToken.None))).Code, Is.EqualTo(TransportError.Timeout));
            }
        });

        [TestCase(2, 100, 3), TestCase(64, 12, 3)]
        public void QueueNeverExceedsBothLimits(int items, int bytes, int attempts)
        {
            var queue = new MainThreadFrameQueue(new TransportOptions(maxQueueItems: items, maxQueueBytes: bytes));
            queue.BeginEpoch(10);
            Assert.That(queue.TryEnqueue(10, new byte[6]), Is.True);
            Assert.That(queue.TryEnqueue(10, new byte[6]), Is.True);
            Assert.That(queue.TryEnqueue(10, new byte[6]), Is.False);
            Assert.That(queue.Count, Is.EqualTo(2)); Assert.That(queue.Bytes, Is.EqualTo(12));
            int delivered = 0;
            Assert.That(queue.Drain(10, _ => delivered++), Is.EqualTo(2));
            Assert.That(delivered, Is.EqualTo(2)); Assert.That(queue.Bytes, Is.Zero);
        }

        [Test]
        public void StaleEpochCannotEnqueueOrDrainCurrentFrames()
        {
            var queue = new MainThreadFrameQueue();
            queue.BeginEpoch(1); Assert.That(queue.TryEnqueue(1, new byte[5]), Is.True);
            queue.BeginEpoch(2); Assert.That(queue.TryEnqueue(1, new byte[5]), Is.False);
            Assert.That(queue.TryEnqueue(2, new byte[6]), Is.True);
            Assert.That(queue.Drain(1, _ => Assert.Fail("Stale consumer")), Is.Zero);
            Assert.That(queue.Count, Is.EqualTo(1));
            queue.Drain(2, _ => { }); Assert.That(queue.Count, Is.Zero);
        }

        [TestCase(1, 100), TestCase(64, 6)]
        public void IncomingOverflowFailsConnectionWithoutWaiting(int items, int bytes) => Run(() => IncomingOverflowAsync(items,bytes));
        private async Task IncomingOverflowAsync(int items, int bytes)
        {
            using (var pair = await Pair.Open(new TransportOptions(maxQueueItems: items, maxQueueBytes: bytes)))
            {
                await pair.Peer.GetStream().WriteAsync(new byte[] { 0,0,0,2,8,3, 0,0,0,2,8,3 }, 0, 12);
                // Wait for the remote FIN produced by overflow; ReadAsync must report the terminal reason.
                var closed = pair.Peer.GetStream().ReadAsync(new byte[1], 0, 1);
                await RemoteClose(closed);
                Assert.That((await Failure(pair.Transport.ReadAsync(CancellationToken.None))).Code, Is.EqualTo(TransportError.Backpressure));
            }
        }

        [Test]
        public void HostHeldFrameStillCountsAgainstReceiveBudget() => Run(async () =>
        {
            using (var pair = await Pair.Open(new TransportOptions(maxQueueItems: 2, maxQueueBytes: 12)))
            {
                await pair.Peer.GetStream().WriteAsync(new byte[] { 0,0,0,2,8,1 }, 0, 6);
                var held = await WithLimit(pair.Transport.ReadAsync(CancellationToken.None));
                var host = new MainThreadFrameQueue(); host.BeginEpoch(1); host.TryEnqueue(1, held);
                await pair.Peer.GetStream().WriteAsync(new byte[] { 0,0,0,2,8,2, 0,0,0,2,8,3 }, 0, 12);
                await RemoteClose(pair.Peer.GetStream().ReadAsync(new byte[1],0,1));
                Assert.That((await Failure(pair.Transport.ReadAsync(CancellationToken.None))).Code, Is.EqualTo(TransportError.Backpressure));
                Assert.That(host.Count, Is.EqualTo(1));
            }
        });

        [Test]
        public void RejectsBudgetOverflowBeforeAllocatingBody() => Run(async () =>
        {
            int allocations = 0;
            using (var pair = await Pair.Open(new TransportOptions(maxQueueBytes: 5), allocate: n => { allocations++; return new byte[n]; }))
            {
                await pair.Peer.GetStream().WriteAsync(new byte[] { 0,0,0,2 }, 0, 4);
                Assert.That((await Failure(pair.Transport.ReadAsync(CancellationToken.None))).Code, Is.EqualTo(TransportError.Backpressure));
                Assert.That(allocations, Is.Zero);
            }
        });

        [Test]
        public void OutgoingOverflowIsFiniteAndDoesNotWrite() => Run(async () =>
        {
            using (var pair = await Pair.Open(new TransportOptions(maxQueueBytes: 5)))
            {
                Assert.That((await Failure(pair.Transport.SendAsync(new byte[] { 0,0,0,2,8,3 },CancellationToken.None))).Code,
                    Is.EqualTo(TransportError.Backpressure));
                Assert.That(await WithLimit(pair.Peer.GetStream().ReadAsync(new byte[1],0,1)), Is.Zero);
            }
        });

        [Test]
        public void BlockedWriteUsesOneTotalDeadline() => Run(async () =>
        {
            var clock = new ManualClock();
            using (var pair = await Pair.Open(new TransportOptions(writeFrameTimeout: TimeSpan.FromSeconds(7)), clock, receiveBuffer: 1024))
            {
                var frame = new byte[1048580]; frame[1] = 16; // Exactly 1 MiB body; peer deliberately never reads.
                var write = await SendUntilBlocked(pair.Transport, frame, CancellationToken.None);
                await clock.WaitForWriteDeadline();
                Assert.That(write.IsCompleted, Is.False, "The loopback fixture must actually exert socket backpressure.");
                clock.Advance(7);
                Assert.That((await Failure(write)).Code, Is.EqualTo(TransportError.Timeout));
            }
        });

        [TestCase(false), TestCase(true)]
        public void InFlightWriteCountsTowardsItemLimitAndCanBeCancelled(bool cancelWrite) => Run(async () =>
        {
            var clock = new ManualClock();
            using (var pair = await Pair.Open(new TransportOptions(maxQueueItems: 1, writeFrameTimeout: TimeSpan.FromSeconds(7)), clock, receiveBuffer: 1024))
            using (var cancellation = new CancellationTokenSource())
            {
                var frame = new byte[1048580]; frame[1] = 16;
                var first = await SendUntilBlocked(pair.Transport, frame, cancellation.Token);
                await clock.WaitForWriteDeadline();
                Assert.That(first.IsCompleted, Is.False, "Fixture must block a real socket write.");
                if (cancelWrite)
                {
                    cancellation.Cancel();
                    Assert.That((await Failure(first)).Code, Is.EqualTo(TransportError.Cancelled));
                }
                else
                {
                    Assert.That((await Failure(pair.Transport.SendAsync(frame,CancellationToken.None))).Code, Is.EqualTo(TransportError.Backpressure));
                    Assert.That((await Failure(first)).Code, Is.EqualTo(TransportError.Backpressure));
                }
            }
        });

        [Test]
        public void ExpiredConnectDeadlineClosesSocket() => Run(async () =>
        {
            var listener = new TcpListener(IPAddress.Loopback,0); listener.Start();
            var transport = new TcpFrameTransport(clock: new ExpiringConnectClock());
            try
            {
                Assert.That((await Failure(transport.ConnectAsync("127.0.0.1",((IPEndPoint)listener.LocalEndpoint).Port,CancellationToken.None))).Code,
                    Is.EqualTo(TransportError.Timeout));
                Assert.That((await Failure(transport.ReadAsync(CancellationToken.None))).Code, Is.EqualTo(TransportError.Timeout));
            }
            finally { transport.Close(); listener.Stop(); }
        });

        private static void Run(Func<Task> test) => Task.Run(test).GetAwaiter().GetResult();
        private static async Task<Task> SendUntilBlocked(TcpFrameTransport transport, byte[] frame, CancellationToken token)
        {
            // OS send buffers vary; fill a finite amount until a real write remains pending.
            for (int attempt = 0; attempt < 16; attempt++)
            {
                var send = transport.SendAsync(frame, token);
                if (await Task.WhenAny(send, Task.Delay(50)) != send) return send;
                await send;
            }
            Assert.Fail("Loopback socket accepted 16 MiB without backpressure; fixture cannot exercise a blocked write.");
            return null;
        }
        private static async Task RemoteClose(Task<int> read)
        {
            try { Assert.That(await WithLimit(read), Is.Zero); }
            catch (IOException) { /* Windows can send RST when closing with unread bytes. */ }
        }
        internal static async Task<T> WithLimit<T>(Task<T> task)
        {
            if (await Task.WhenAny(task, Task.Delay(4000)) != task) Assert.Fail("Operation did not finish in four seconds.");
            return await task;
        }
        internal static async Task<TransportException> Failure(Task task)
        {
            if (await Task.WhenAny(task, Task.Delay(4000)) != task) Assert.Fail("Transport error did not complete in four seconds.");
            try { await task; } catch (TransportException ex) { return ex; }
            Assert.Fail("Expected a finite transport error."); return null;
        }

        private sealed class Pair : IDisposable
        {
            public TcpFrameTransport Transport; public TcpClient Peer; private TcpListener listener;
            public static async Task<Pair> Open(TransportOptions options = null, ITransportClock clock = null, Func<int,byte[]> allocate = null, int receiveBuffer = 0)
            {
                var p = new Pair { listener = new TcpListener(IPAddress.Loopback, 0), Transport = new TcpFrameTransport(options, clock, allocate) };
                if (receiveBuffer > 0) p.listener.Server.ReceiveBufferSize = receiveBuffer;
                p.listener.Start();
                var accepted = p.listener.AcceptTcpClientAsync();
                await p.Transport.ConnectAsync("127.0.0.1", ((IPEndPoint)p.listener.LocalEndpoint).Port, CancellationToken.None);
                p.Peer = await WithLimit(accepted); if (receiveBuffer > 0) p.Peer.ReceiveBufferSize = receiveBuffer; return p;
            }
            public void Dispose() { Transport.Close(); Peer?.Close(); listener.Stop(); }
        }

        private sealed class ManualClock : ITransportClock
        {
            private readonly object gate = new object();
            private readonly List<(TimeSpan at, TaskCompletionSource<bool> done)> alarms = new List<(TimeSpan,TaskCompletionSource<bool>)>();
            private readonly TaskCompletionSource<bool> readDeadline = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            private readonly TaskCompletionSource<bool> writeDeadline = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            public TimeSpan Elapsed { get; private set; }
            public Task Delay(TimeSpan duration, CancellationToken token)
            {
                var done = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                lock (gate)
                {
                    alarms.Add((Elapsed + duration, done));
                    if (duration == TimeSpan.FromSeconds(15)) readDeadline.TrySetResult(true);
                    if (duration == TimeSpan.FromSeconds(7)) writeDeadline.TrySetResult(true);
                }
                token.Register(() => done.TrySetCanceled()); return done.Task;
            }
            public Task WaitForReadDeadline() => WithLimit(readDeadline.Task);
            public Task WaitForWriteDeadline() => WithLimit(writeDeadline.Task);
            public void Advance(int seconds)
            {
                lock (gate) { Elapsed += TimeSpan.FromSeconds(seconds); foreach (var alarm in alarms) if (alarm.at <= Elapsed) alarm.done.TrySetResult(true); }
            }
        }
        private sealed class ExpiringConnectClock : ITransportClock
        {
            public TimeSpan Elapsed { get; private set; }
            public Task Delay(TimeSpan duration, CancellationToken token) { Elapsed += duration; return Task.CompletedTask; }
        }
        private sealed class LateCompletionClock : ITransportClock
        {
            public Func<Task<byte[]>> AtFinalValidation;
            public Task<byte[]> ConsumerRead;
            public readonly TaskCompletionSource<bool> Validating = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            private TimeSpan elapsed;
            public TimeSpan Elapsed
            {
                get
                {
                    var capture = Interlocked.Exchange(ref AtFinalValidation, null);
                    if (capture != null)
                    {
                        elapsed = TimeSpan.FromSeconds(16);
                        // Execute at the exact completion/validation boundary. If publication already
                        // happened, ReadAsync consumes its semaphore synchronously and irreversibly.
                        ConsumerRead = capture();
                        Validating.TrySetResult(true);
                    }
                    return elapsed;
                }
            }
            public Task Delay(TimeSpan duration, CancellationToken token) => Task.Delay(Timeout.Infinite, token);
        }
    }
}
