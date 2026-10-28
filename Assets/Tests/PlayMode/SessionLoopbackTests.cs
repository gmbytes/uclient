using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Client.Contracts;
using Client.Framework.Network;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityGameFramework.Runtime;

namespace Client.Tests
{
    [Category("N3")]
    public sealed class SessionLoopbackTests
    {
        [TestCase("success", LoginError.None)]
        [TestCase("reject", LoginError.Rejected)]
        [TestCase("timeout", LoginError.Timeout)]
        [TestCase("wrong-version", LoginError.ProtocolError)]
        [TestCase("wrong-id", LoginError.ProtocolError)]
        [TestCase("unknown-code", LoginError.ProtocolError)]
        [TestCase("business", LoginError.ProtocolError)]
        [TestCase("half-frame", LoginError.ProtocolError)]
        [TestCase("close", LoginError.Disconnected)]
        public void RealLoginFixtureReturnsFiniteResult(string scenario, LoginError expected)
        {
            string root = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            Task.Run(async () =>
            {
                using (var fixture = await Fixture.Start(root, scenario))
                {
                    var connection = new TcpFrameConnection();
                    using (var session = new SessionClient(connection, new SessionOptions("127.0.0.1", fixture.Port), new RequestIdSequence(9007199254740993UL)))
                    {
                        var result = await Limit(session.LoginAsync(new LoginInput("demo:1", null), CancellationToken.None), 8000);
                        Assert.That(result.Error, Is.EqualTo(expected)); Assert.That(result.Succeeded, Is.EqualTo(expected == LoginError.None));
                        if (expected == LoginError.None)
                        {
                            Assert.That(result.Identity.Epoch, Is.EqualTo(9007199254740993UL));
                            Assert.That(result.Identity.ResumeTokenTtlMs, Is.EqualTo(9007199254740995UL));
                            session.Disconnect();
                        }
                        else
                        {
                            Assert.That(session.State, Is.EqualTo(SessionState.Disconnected)); Assert.That(result.Identity, Is.Null);
                            if (scenario == "unknown-code") Assert.That(result.ServerErrorCode, Is.EqualTo(123));
                        }
                        await fixture.Finish();
                    }
                }
            }).GetAwaiter().GetResult();
        }
        [Test] public void RealLoginCancellationClosesOnlyOwnedSocketAndProcess()
        {
            string root = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            Task.Run(async () =>
            {
                using (var fixture = await Fixture.Start(root, "cancel"))
                using (var cancel = new CancellationTokenSource())
                using (var session = new SessionClient(new TcpFrameConnection(), new SessionOptions("127.0.0.1", fixture.Port)))
                {
                    var task = session.LoginAsync(new LoginInput("demo:1", null), cancel.Token);
                    await fixture.Received(); cancel.Cancel();
                    Assert.That((await Limit(task)).Error, Is.EqualTo(LoginError.Cancelled));
                    await fixture.Finish();
                }
            }).GetAwaiter().GetResult();
        }
        [Test] public void ServerIdleCloseRestoresDisconnectedState()
        {
            string root = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            Task.Run(async () =>
            {
                using (var fixture = await Fixture.Start(root, "idle-disconnect"))
                using (var session = new SessionClient(new TcpFrameConnection(), new SessionOptions("127.0.0.1", fixture.Port)))
                {
                    var disconnected = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                    session.StateChanged += state => { if (state == SessionState.Disconnected) disconnected.TrySetResult(true); };
                    Assert.That((await Limit(session.LoginAsync(new LoginInput(null, "memory-token"), CancellationToken.None))).Succeeded, Is.True);
                    await Limit(disconnected.Task); Assert.That(session.Identity, Is.Null); await fixture.Finish();
                }
            }).GetAwaiter().GetResult();
        }
        [UnityTest] public IEnumerator RuntimeHostSessionCallbacksStayOnMainThread()
        {
            GameEntry.Shutdown(ShutdownType.None);
            string root = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            var start = Fixture.Start(root, "success"); yield return Await(start);
            using (var fixture = start.Result)
            {
                var go = new GameObject("N3 session network host"); var host = go.AddComponent<TokNetworkComponent>();
                using (var session = new SessionClient(host, new SessionOptions("127.0.0.1", fixture.Port)))
                {
                    int owner = Thread.CurrentThread.ManagedThreadId; var threads = new List<int>(); var states = new List<SessionState>();
                    session.StateChanged += state => { threads.Add(Thread.CurrentThread.ManagedThreadId); states.Add(state); };
                    try
                    {
                        var login = session.LoginAsync(new LoginInput("demo:1", null), CancellationToken.None); yield return Await(login);
                        Assert.That(login.Result.Succeeded, Is.True);
                        var disconnect = Task.Run(() => session.Disconnect()); yield return Await(disconnect);
                        float until = Time.realtimeSinceStartup + 4;
                        while (session.State != SessionState.Disconnected && Time.realtimeSinceStartup < until) yield return null;
                        Assert.That(states, Is.EqualTo(new[] { SessionState.Connecting, SessionState.Authenticating, SessionState.Authenticated, SessionState.Disconnected }));
                        Assert.That(threads, Has.All.EqualTo(owner));
                        var exit = fixture.Finish(); yield return Await(exit);
                    }
                    finally { session.Disconnect(); UnityEngine.Object.Destroy(go); GameEntry.Shutdown(ShutdownType.None); }
                }
            }
        }
        private static IEnumerator Await(Task task)
        {
            float until = Time.realtimeSinceStartup + 8;
            while (!task.IsCompleted && Time.realtimeSinceStartup < until) yield return null;
            Assert.That(task.IsCompleted, Is.True, "N3 host operation exceeded guard."); task.GetAwaiter().GetResult();
        }
        private static async Task<T> Limit<T>(Task<T> task, int timeout = 4000)
        {
            if (await Task.WhenAny(task, Task.Delay(timeout)).ConfigureAwait(false) != task) Assert.Fail("N3 fixture operation exceeded guard.");
            return await task.ConfigureAwait(false);
        }
        private sealed class Fixture : IDisposable
        {
            private Process process;
            public int Port { get; private set; }
            public static async Task<Fixture> Start(string root, string scenario)
            {
                var fixture = new Fixture();
                var reservation = new TcpListener(IPAddress.Loopback, 0); reservation.Start();
                fixture.Port = ((IPEndPoint)reservation.LocalEndpoint).Port; reservation.Stop();
                string executable = Path.Combine(root, "Builds/Fixtures/LoginServer/LoginServer.dll");
                Assert.That(File.Exists(executable), Is.True, "Build LoginServer.csproj before N3 PlayMode.");
                var info = new ProcessStartInfo("dotnet", "\"" + executable + "\" --port " + fixture.Port + " --case " + scenario)
                { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true, WorkingDirectory = root };
                try
                {
                    fixture.process = Process.Start(info);
                    string ready = await Limit(fixture.process.StandardOutput.ReadLineAsync()).ConfigureAwait(false);
                    Assert.That(ready, Is.EqualTo("READY " + fixture.Port)); return fixture;
                }
                catch { fixture.Dispose(); throw; }
            }
            public async Task Received() => Assert.That(await Limit(process.StandardOutput.ReadLineAsync()).ConfigureAwait(false), Is.EqualTo("RECEIVED"));
            public async Task Finish()
            {
                bool exited = await Task.Run(() => process.WaitForExit(4000)).ConfigureAwait(false);
                Assert.That(exited, Is.True, "Owned fixture failed to exit after socket cleanup.");
                Assert.That(process.ExitCode, Is.Zero, "Fixture reported extra traffic or protocol failure.");
                Assert.That(await process.StandardError.ReadToEndAsync().ConfigureAwait(false), Is.Empty);
            }
            public void Dispose()
            {
                if (process == null) return;
                try { if (!process.HasExited && !process.WaitForExit(1000)) { process.Kill(); process.WaitForExit(4000); } }
                finally { process.Dispose(); process = null; }
            }
        }
    }
}
