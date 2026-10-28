using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Client.Framework.Network;
using NUnit.Framework;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.TestTools;
using UnityGameFramework.Runtime;

namespace Client.Tests
{
    [Category("N2")]
    public sealed class TcpLoopbackTests
    {
        [UnityTest]
        public IEnumerator HostDeliversOnlyCurrentEpochOnMainThreadAndDestroyClosesSocket()
        {
            GameEntry.Shutdown(ShutdownType.None);
            var listener = new TcpListener(IPAddress.Loopback,0); listener.Start();
            var go = new GameObject("N2 network host");
            var host = go.AddComponent<TokNetworkComponent>(); host.enabled = false;
            int mainThread = Thread.CurrentThread.ManagedThreadId;
            var frames = new List<byte[]>();
            host.FrameReceived += frame => { Assert.That(Thread.CurrentThread.ManagedThreadId, Is.EqualTo(mainThread)); frames.Add(frame); };
            TcpClient first = null, second = null;
            try
            {
                var accept = listener.AcceptTcpClientAsync();
                var connect = host.ConnectAsync("127.0.0.1",((IPEndPoint)listener.LocalEndpoint).Port,CancellationToken.None);
                yield return Await(connect); connect.GetAwaiter().GetResult();
                yield return Await(accept); first = accept.Result;
                var sent = first.GetStream().WriteAsync(new byte[]{0,0,0,2,8,1},0,6);
                yield return Await(sent);
                // A disabled host cannot deliver; replacement must discard this epoch even if its pump is delayed.
                yield return null;
                accept = listener.AcceptTcpClientAsync();
                connect = host.ConnectAsync("127.0.0.1",((IPEndPoint)listener.LocalEndpoint).Port,CancellationToken.None);
                yield return Await(connect); connect.GetAwaiter().GetResult();
                yield return Await(accept); second = accept.Result;
                sent = second.GetStream().WriteAsync(new byte[]{0,0,0,2,8,2},0,6);
                yield return Await(sent);
                host.enabled = true;
                float end = Time.realtimeSinceStartup + 4;
                while (frames.Count == 0 && Time.realtimeSinceStartup < end) yield return null;
                Assert.That(frames.Count, Is.EqualTo(1)); Assert.That(frames[0][5], Is.EqualTo(2));
                UnityEngine.Object.Destroy(go); yield return null;
                var eof = second.GetStream().ReadAsync(new byte[1],0,1);
                yield return Await(eof);
                Assert.That(eof.Result, Is.Zero);
            }
            finally { if (go != null) UnityEngine.Object.Destroy(go); first?.Close(); second?.Close(); listener.Stop(); GameEntry.Shutdown(ShutdownType.None); }
        }

        [Test]
        public void PureConnectionSurfacePumpsRealFrames() => Run(async () =>
        {
            var listener = new TcpListener(IPAddress.Loopback,0); listener.Start();
            IFrameConnection connection = new TcpFrameConnection();
            var received = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
            connection.FrameReceived += frame => received.TrySetResult(frame);
            try
            {
                var accept = listener.AcceptTcpClientAsync();
                await connection.ConnectAsync("127.0.0.1",((IPEndPoint)listener.LocalEndpoint).Port,CancellationToken.None);
                using (var peer = await Limit(accept))
                {
                    await peer.GetStream().WriteAsync(new byte[]{0,0,0,2,8,3},0,6);
                    Assert.That(await Limit(received.Task), Is.EqualTo(new byte[]{0,0,0,2,8,3}));
                }
            }
            finally { connection.Close(); listener.Stop(); }
        });

        [Test]
        public void PureConnectionConsumerFailureClosesSocketAndReportsError() => Run(async () =>
        {
            var listener = new TcpListener(IPAddress.Loopback,0); listener.Start();
            var connection = new TcpFrameConnection();
            var failure = new TaskCompletionSource<TransportException>(TaskCreationOptions.RunContinuationsAsynchronously);
            connection.FrameReceived += _ => throw new InvalidOperationException("Consumer rejected delivery.");
            connection.TransportFailed += ex => failure.TrySetResult(ex);
            try
            {
                var accept = listener.AcceptTcpClientAsync();
                await connection.ConnectAsync("127.0.0.1",((IPEndPoint)listener.LocalEndpoint).Port,CancellationToken.None);
                using (var peer = await Limit(accept))
                {
                    await peer.GetStream().WriteAsync(new byte[]{0,0,0,2,8,3},0,6);
                    var error = await Limit(failure.Task);
                    Assert.That(error.Code, Is.EqualTo(TransportError.SocketError));
                    Assert.That(error.InnerException, Is.TypeOf<InvalidOperationException>());
                    Assert.That(await Limit(peer.GetStream().ReadAsync(new byte[1],0,1)), Is.Zero);
                }
            }
            finally { connection.Close(); listener.Stop(); }
        });

        private static IEnumerator Await(Task task)
        {
            float end = Time.realtimeSinceStartup + 4;
            while (!task.IsCompleted && Time.realtimeSinceStartup < end) yield return null;
            Assert.That(task.IsCompleted, Is.True, "Host operation timed out.");
            task.GetAwaiter().GetResult();
        }
        [TestCase(false), TestCase(true)]
        public void ReadsSplitAndCoalescedFrames(bool ipv6) => Run(() => ReadsSplitAndCoalescedFramesAsync(ipv6));
        private async Task ReadsSplitAndCoalescedFramesAsync(bool ipv6)
        {
            using (var pair = await Pair.Open(ipv6))
            {
                var stream = pair.Peer.GetStream();
                await stream.WriteAsync(new byte[] { 0, 0 }, 0, 2);
                var read = pair.Transport.ReadAsync(CancellationToken.None);
                await stream.WriteAsync(new byte[] { 0, 2, 8 }, 0, 3);
                Assert.That(read.IsCompleted, Is.False, "A partial body cannot be delivered.");
                await stream.WriteAsync(new byte[] { 3, 0,0,0,2,8,4 }, 0, 7);
                Assert.That(await Limit(read), Is.EqualTo(new byte[] { 0,0,0,2,8,3 }));
                Assert.That(await Limit(pair.Transport.ReadAsync(CancellationToken.None)), Is.EqualTo(new byte[] { 0,0,0,2,8,4 }));
            }
        }

        [TestCase(0, TransportError.EndOfStream), TestCase(2, TransportError.TruncatedFrame), TestCase(5, TransportError.TruncatedFrame)]
        public void EofInFrameIsError(int sent, TransportError expected) => Run(() => EofInFrameAsync(sent, expected));
        private async Task EofInFrameAsync(int sent, TransportError expected)
        {
            using (var pair = await Pair.Open())
            {
                if (sent > 0) await pair.Peer.GetStream().WriteAsync(new byte[] { 0,0,0,2,8 }, 0, sent);
                pair.Peer.Close();
                Assert.That((await Failure(pair.Transport.ReadAsync(CancellationToken.None))).Code, Is.EqualTo(expected));
            }
        }

        [Test]
        public void CancellingBlockedReadClosesOnlyCapturedGeneration() => Run(CancellingBlockedReadAsync);
        private async Task CancellingBlockedReadAsync()
        {
            using (var pair = await Pair.Open())
            using (var cancel = new CancellationTokenSource())
            {
                var read = pair.Transport.ReadAsync(cancel.Token);
                cancel.Cancel();
                Assert.That((await Failure(read)).Code, Is.EqualTo(TransportError.Cancelled));
                var accept = pair.Listener.AcceptTcpClientAsync();
                await pair.Transport.ConnectAsync("127.0.0.1", pair.Port, CancellationToken.None);
                using (var nextPeer = await Limit(accept))
                {
                    cancel.Cancel(); // Old token/lifetime cannot close this socket.
                    await nextPeer.GetStream().WriteAsync(new byte[] { 0,0,0,2,8,3 }, 0, 6);
                    Assert.That(await Limit(pair.Transport.ReadAsync(CancellationToken.None)), Has.Length.EqualTo(6));
                }
            }
        }

        [Test]
        public void SerialWriterPreservesFramesAndCopiesAcceptedMemory() => Run(SerialWriterAsync);
        private async Task SerialWriterAsync()
        {
            using (var pair = await Pair.Open())
            {
                var first = new byte[] { 0,0,0,2,8,3 };
                var a = pair.Transport.SendAsync(first, CancellationToken.None);
                first[5] = 99;
                var b = pair.Transport.SendAsync(new byte[] { 0,0,0,2,8,4 }, CancellationToken.None);
                var received = new byte[12]; int offset = 0;
                while (offset < received.Length) offset += await Limit(pair.Peer.GetStream().ReadAsync(received, offset, received.Length-offset));
                await Limit(Task.WhenAll(a,b));
                Assert.That(received, Is.EqualTo(new byte[] { 0,0,0,2,8,3, 0,0,0,2,8,4 }));
            }
        }

        [Test]
        public void OversizeOutgoingFrameFailsBeforeWritingAndCloseTerminatesRead() => Run(OversizeOutgoingAsync);
        private async Task OversizeOutgoingAsync()
        {
            using (var pair = await Pair.Open())
            {
                Assert.That((await Failure(pair.Transport.SendAsync(new byte[] { 255,255,255,255 }, CancellationToken.None))).Code,
                    Is.EqualTo(TransportError.InvalidFrame));
                var read = pair.Transport.ReadAsync(CancellationToken.None);
                pair.Transport.Close();
                Assert.That((await Failure(read)).Code, Is.EqualTo(TransportError.Closed));
            }
        }

        private static void Run(Func<Task> test) => Task.Run(test).GetAwaiter().GetResult();
        private static async Task<T> Limit<T>(Task<T> task)
        {
            if (await Task.WhenAny(task, Task.Delay(4000)) != task) Assert.Fail("Loopback did not complete in four seconds.");
            return await task;
        }
        private static async Task Limit(Task task)
        {
            if (await Task.WhenAny(task, Task.Delay(4000)) != task) Assert.Fail("Loopback did not complete in four seconds.");
            await task;
        }
        private static async Task<TransportException> Failure(Task task)
        {
            try { await Limit(task); } catch (TransportException ex) { return ex; }
            Assert.Fail("Expected transport failure."); return null;
        }
        private sealed class Pair : IDisposable
        {
            public TcpFrameTransport Transport = new TcpFrameTransport();
            public TcpClient Peer; public TcpListener Listener;
            public int Port => ((IPEndPoint)Listener.LocalEndpoint).Port;
            public static async Task<Pair> Open(bool ipv6 = false)
            {
                if (ipv6 && !Socket.OSSupportsIPv6) Assert.Ignore("Socket.OSSupportsIPv6 is false on this platform.");
                var pair = new Pair();
                try { pair.Listener = new TcpListener(ipv6 ? IPAddress.IPv6Loopback : IPAddress.Loopback,0); pair.Listener.Start(); }
                catch (SocketException ex) when (ipv6) { Assert.Ignore("IPv6 loopback bind unavailable: " + ex.SocketErrorCode); }
                var accepted = pair.Listener.AcceptTcpClientAsync();
                await pair.Transport.ConnectAsync(ipv6 ? "::1" : "127.0.0.1",pair.Port,CancellationToken.None);
                pair.Peer = await Limit(accepted); return pair;
            }
            public void Dispose() { Transport.Close(); Peer?.Close(); Listener?.Stop(); }
        }
    }
}
