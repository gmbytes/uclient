using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Client.Contracts;
using Client.Framework.Network;
using NUnit.Framework;
using Tok.Client.V1;
namespace Client.Tests
{
    [Category("N3")]
    public sealed class SessionClientTests
    {
        private static LoginInput Input => new LoginInput("demo:1", null);
        [Test] public void LoginCredentialAndTokenAreExclusive()
        {
            Assert.Throws<ArgumentException>(() => new LoginInput("demo:1", "private-token"));
            Assert.Throws<ArgumentException>(() => new LoginInput(null, null));
            foreach (var input in new[] { Input, new LoginInput(null, "private-token") })
            {
                var wire = new Connection(); using (var session = New(wire))
                {
                    var login = session.LoginAsync(input, CancellationToken.None);
                    var request = FrameCodec.Decode(wire.Sent);
                    Assert.That(request.Version, Is.EqualTo(3)); Assert.That(request.ContentCase, Is.EqualTo(Frame.ContentOneofCase.Login));
                    Assert.That(request.Login.Credential, Is.EqualTo(input.Credential ?? ""));
                    Assert.That(request.Login.ResumeToken, Is.EqualTo(input.ResumeToken ?? ""));
                    wire.Deliver(Success(request.Login.RequestId)); Assert.That(login.Result.Succeeded, Is.True);
                }
            }
        }
        [Test] public void ConcurrentLoginReturnsBusy()
        {
            var wire = new Connection(); using (var session = New(wire))
            {
                var first = session.LoginAsync(Input, CancellationToken.None);
                Assert.That(session.LoginAsync(Input, CancellationToken.None).Result.Error, Is.EqualTo(LoginError.Busy));
                wire.Deliver(Success(1)); Assert.That(first.Result.Succeeded, Is.True);
            }
        }
        [TestCase("demo:1", " \t", "demo:1", "")]
        [TestCase(" \t", "private-token", "", "private-token")]
        [TestCase(" demo:1 ", "\t", " demo:1 ", "")]
        [TestCase("\t", " private-token ", "", " private-token ")]
        public void LoginWireClearsWhitespaceInUnselectedCredential(string credential, string token, string expectedCredential, string expectedToken)
        {
            var wire = new Connection(); using (var session = New(wire))
            {
                session.LoginAsync(new LoginInput(credential, token), CancellationToken.None);
                var sent = FrameCodec.Decode(wire.Sent).Login;
                Assert.That(sent.Credential, Is.EqualTo(expectedCredential));
                Assert.That(sent.ResumeToken, Is.EqualTo(expectedToken));
            }
        }
        [TestCase(3u, LoginError.Rejected), TestCase(7u, LoginError.Timeout), TestCase(123u, LoginError.ProtocolError)]
        public void LoginFailureNeverBecomesAuthenticated(uint code, LoginError expected)
        {
            var wire = new Connection(); using (var session = New(wire))
            {
                var states = new List<SessionState>(); session.StateChanged += states.Add;
                var task = session.LoginAsync(Input, CancellationToken.None); var reply = Success(1); reply.LoggedIn.ErrorCode = code; wire.Deliver(reply);
                Assert.That(task.Result.Error, Is.EqualTo(expected)); Assert.That(task.Result.ServerErrorCode, Is.EqualTo(code));
                Assert.That(task.Result.Identity, Is.Null); Assert.That(states, Is.Not.Contains(SessionState.Authenticated)); Assert.That(wire.Closed, Is.True);
            }
        }
        [Test] public void CancelledOldResponseCannotChangeNewSession()
        {
            var wire = new Connection(); using (var session = New(wire)) using (var cancel = new CancellationTokenSource())
            {
                var old = session.LoginAsync(Input, cancel.Token); var stale = wire.FrameReceivedSnapshot;
                cancel.Cancel(); Assert.That(old.Result.Error, Is.EqualTo(LoginError.Cancelled));
                var current = session.LoginAsync(Input, CancellationToken.None);
                stale(FrameCodec.Encode(Success(1))); Assert.That(current.IsCompleted, Is.False);
                wire.Deliver(Success(2)); Assert.That(current.Result.Succeeded, Is.True);
                Assert.That(session.State, Is.EqualTo(SessionState.Authenticated));
            }
        }
        [Test] public void LateConnectCompletionAndOldCancellationCannotCloseReplacement() => Run(async () =>
        {
            var oldConnect = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var context = new Context(); var wire = new Connection { ConnectGate = oldConnect };
            using (var session = new SessionClient(wire, new SessionOptions("localhost", 1), context: context))
            using (var cancel = new CancellationTokenSource())
            {
                var old = session.LoginAsync(Input, cancel.Token); context.Drain(); cancel.Cancel(); context.Drain();
                Assert.That((await Limit(old)).Error, Is.EqualTo(LoginError.Cancelled));
                wire.ConnectGate = null;
                var replacement = session.LoginAsync(Input, CancellationToken.None); context.Drain();
                wire.Deliver(Success(2)); context.Drain();
                Assert.That((await Limit(replacement)).Succeeded, Is.True);
                var staleQueued = context.ExpectPost();
                oldConnect.SetException(new InvalidOperationException("stale failure"));
                Assert.That(await Limit(staleQueued), Is.True, "Stale connect completion did not reach the owner queue.");
                context.Drain(); cancel.Cancel(); context.Drain();
                Assert.That(session.State, Is.EqualTo(SessionState.Authenticated)); Assert.That(wire.Closed, Is.False);
            }
            context.Drain();
        });
        [Test] public void CancellationAfterSuccessDoesNotEndAuthenticatedSession()
        {
            var wire = new Connection(); using (var session = New(wire)) using (var cancel = new CancellationTokenSource())
            {
                var task = session.LoginAsync(Input, cancel.Token); wire.Deliver(Success(1));
                Assert.That(task.Result.Succeeded, Is.True); cancel.Cancel();
                Assert.That(session.State, Is.EqualTo(SessionState.Authenticated)); Assert.That(wire.Closed, Is.False);
            }
        }
        [Test] public void QueuedFrameBurstTerminatesWithoutAuthenticating()
        {
            var context = new Context(); var wire = new Connection();
            using (var session = new SessionClient(wire, new SessionOptions("localhost", 1), context: context))
            {
                var task = session.LoginAsync(Input, CancellationToken.None); context.Drain();
                wire.Deliver(Success(1));
                wire.Deliver(new Frame { Version = 3, Notify = new Notify { MessageId = 1 } });
                Assert.That(task.IsCompleted, Is.False); context.Drain();
                Assert.That(task.Result.Error, Is.EqualTo(LoginError.ProtocolError)); Assert.That(session.Identity, Is.Null);
            }
            context.Drain();
        }
        [Test] public void ConcurrentMailboxTransitionsCannotLoseUnsupportedFrame()
        {
            var context = new Context(); var wire = new Connection();
            using (var session = new SessionClient(wire, new SessionOptions("localhost", 1), context: context))
            {
                session.LoginAsync(Input, CancellationToken.None); context.Drain(); wire.Deliver(Success(1));
                var attempt = typeof(SessionClient).GetField("current", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).GetValue(session);
                var mailboxGate = attempt.GetType().GetField("MailboxGate").GetValue(attempt);
                var observer = typeof(SessionClient).GetField("MailboxOccupiedForTest", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                Exception producerError = null, consumerError = null;
                bool occupiedTransitionIsAtomic = false;
                using (var occupied = new ManualResetEventSlim())
                using (var releaseProducer = new ManualResetEventSlim())
                using (var consumerStarted = new ManualResetEventSlim())
                using (var consumerFinished = new ManualResetEventSlim())
                {
                    observer.SetValue(session, (Action)(() =>
                    {
                        // Select a legal schedule only: the fixed atomic transition must
                        // finish publishing before its blocked consumer can run.
                        occupiedTransitionIsAtomic = Monitor.IsEntered(mailboxGate);
                        occupied.Set();
                        if (!releaseProducer.Wait(4000)) throw new TimeoutException("Mailbox producer barrier exceeded guard.");
                    }));
                    var producer = new Thread(() =>
                    {
                        try { wire.Deliver(new Frame { Version = 3, Notify = new Notify { MessageId = 1 } }); }
                        catch (Exception error) { producerError = error; }
                    }) { IsBackground = true };
                    var consumer = new Thread(() =>
                    {
                        consumerStarted.Set();
                        try { context.Drain(); } catch (Exception error) { consumerError = error; }
                        finally { consumerFinished.Set(); }
                    }) { IsBackground = true };
                    try
                    {
                        producer.Start();
                        Assert.That(occupied.Wait(4000), Is.True, "Producer did not observe the occupied mailbox.");
                        consumer.Start(); Assert.That(consumerStarted.Wait(4000), Is.True, "Owner consumer did not start.");
                        if (!occupiedTransitionIsAtomic)
                            Assert.That(consumerFinished.Wait(4000), Is.True, "Unprotected owner did not consume before late publication.");
                        releaseProducer.Set();
                    }
                    finally
                    {
                        releaseProducer.Set();
                        bool producerStopped = (producer.ThreadState & ThreadState.Unstarted) != 0 || producer.Join(4000);
                        bool consumerStopped = (consumer.ThreadState & ThreadState.Unstarted) != 0 || consumer.Join(4000);
                        observer.SetValue(session, null);
                        Assert.That(producerStopped && consumerStopped, Is.True, "Mailbox participants did not finish after release.");
                    }
                }
                context.Drain();
                Assert.That(producerError, Is.Null); Assert.That(consumerError, Is.Null);
                Assert.That(session.State, Is.EqualTo(SessionState.Disconnected)); Assert.That(session.Identity, Is.Null);
                Assert.That(wire.Closed, Is.True); Assert.That(wire.SubscriberCount, Is.Zero); Assert.That(wire.SendCount, Is.EqualTo(1));
            }
            context.Drain();
        }
        [Test] public void NullInputAndAlreadyCancelledLoginDoNotConnect()
        {
            var wire = new Connection(); using (var session = New(wire))
            {
                Assert.That(session.LoginAsync(null, CancellationToken.None).Result.Error, Is.EqualTo(LoginError.InvalidInput));
                Assert.That(session.LoginAsync(Input, new CancellationToken(true)).Result.Error, Is.EqualTo(LoginError.Cancelled));
                Assert.That(session.State, Is.EqualTo(SessionState.Idle)); Assert.That(wire.Sent, Is.Null); Assert.That(wire.SubscriberCount, Is.Zero);
            }
        }
        [Test] public void DisconnectRestoresUIState()
        {
            var wire = new Connection(); using (var session = New(wire))
            {
                var states = new List<SessionState>(); session.StateChanged += states.Add;
                var task = session.LoginAsync(Input, CancellationToken.None); wire.Deliver(Success(1));
                Assert.That(task.Result.Identity.Epoch, Is.EqualTo(9007199254740993UL));
                session.Disconnect(); session.Disconnect(); Assert.That(session.Identity, Is.Null);
                Assert.That(states, Is.EqualTo(new[] { SessionState.Connecting, SessionState.Authenticating, SessionState.Authenticated, SessionState.Disconnected }));
                Assert.That(wire.SubscriberCount, Is.Zero);
            }
        }
        [TestCase("version"), TestCase("id"), TestCase("identity"), TestCase("business"), TestCase("malformed")]
        public void InvalidResponseTerminatesSession(string kind)
        {
            var wire = new Connection(); using (var session = New(wire))
            {
                var task = session.LoginAsync(Input, CancellationToken.None); var reply = Success(1);
                if (kind == "version") reply.Version = 2;
                if (kind == "id") reply.LoggedIn.RequestId = 2;
                if (kind == "identity") reply.LoggedIn.SessionId = "";
                if (kind == "business") reply = new Frame { Version = 3, Notify = new Notify { MessageId = 7 } };
                if (kind == "malformed") wire.FrameReceivedSnapshot(new byte[] { 0,0,0,1,255 }); else wire.Deliver(reply);
                Assert.That(task.Result.Error, Is.EqualTo(LoginError.ProtocolError)); Assert.That(session.State, Is.EqualTo(SessionState.Disconnected));
            }
        }
        [Test] public void ControlErrorMapsFiniteCodeAndNeverEchoesSecrets()
        {
            var wire = new Connection(); using (var session = New(wire))
            {
                var task = session.LoginAsync(Input, CancellationToken.None);
                wire.Deliver(new Frame { Version = 3, Error = new ControlError { RequestId = 1, ErrorCode = 3 } });
                Assert.That(task.Result.Error, Is.EqualTo(LoginError.Rejected)); Assert.That(task.Result.SafeMessage, Does.Not.Contain("demo:1"));
            }
        }
        [Test] public void ConnectAndLoginHaveIndependentDeadlines() => Run(async () =>
        {
            var clock = new Clock(); var wire = new Connection { ConnectGate = new TaskCompletionSource<bool>() };
            using (var session = New(wire, clock))
            {
                var task = session.LoginAsync(Input, CancellationToken.None);
                clock.Advance(4); wire.ConnectGate.SetResult(true); await Until(() => wire.Sent != null);
                clock.Advance(4); Assert.That(task.IsCompleted, Is.False);
                clock.Advance(1); Assert.That((await Limit(task)).Error, Is.EqualTo(LoginError.Timeout));
            }
        });
        [Test] public void ConnectTimeoutAndFailureAreFinite() => Run(async () =>
        {
            var clock = new Clock(); var wire = new Connection { ConnectGate = new TaskCompletionSource<bool>() };
            using (var session = New(wire, clock))
            { var task = session.LoginAsync(Input, CancellationToken.None); clock.Advance(5); Assert.That((await Limit(task)).Error, Is.EqualTo(LoginError.Timeout)); }
            wire = new Connection { ConnectError = new InvalidOperationException("unsafe server detail") };
            using (var session = New(wire)) Assert.That((await session.LoginAsync(Input, CancellationToken.None)).Error, Is.EqualTo(LoginError.ConnectFailed));
        });
        [Test] public void CapturedContextOwnsCancellationAndResponseState()
        {
            var context = new Context(); var wire = new Connection();
            using (var session = new SessionClient(wire, new SessionOptions("localhost", 1), context: context))
            using (var cancel = new CancellationTokenSource())
            {
                int owner = Thread.CurrentThread.ManagedThreadId; var threads = new List<int>(); session.StateChanged += _ => threads.Add(Thread.CurrentThread.ManagedThreadId);
                var task = session.LoginAsync(Input, cancel.Token); context.Drain();
                Task.Run(() => cancel.Cancel()).GetAwaiter().GetResult(); Assert.That(task.IsCompleted, Is.False);
                context.Drain(); Assert.That(task.Result.Error, Is.EqualTo(LoginError.Cancelled));
                Assert.That(threads, Has.All.EqualTo(Thread.CurrentThread.ManagedThreadId));
            }
        }
        [Test] public void RequestIdExhaustionRequiresFreshSession()
        {
            var wire = new Connection(); using (var session = new SessionClient(wire, new SessionOptions("localhost", 1), new RequestIdSequence(ulong.MaxValue), context: new ImmediateContext()))
            {
                var task = session.LoginAsync(Input, CancellationToken.None); wire.Deliver(Success(ulong.MaxValue)); Assert.That(task.Result.Succeeded, Is.True);
                session.Disconnect(); Assert.That(session.LoginAsync(Input, CancellationToken.None).Result.Error, Is.EqualTo(LoginError.ProtocolError));
            }
        }
        [Test] public void UnsupportedDownstreamAfterLoginDisconnectsWithoutSendingAck()
        {
            var wire = new Connection(); using (var session = New(wire))
            {
                session.LoginAsync(Input, CancellationToken.None); wire.Deliver(Success(1));
                wire.Deliver(new Frame { Version = 3, Resumed = new ResumeResponse() });
                Assert.That(session.State, Is.EqualTo(SessionState.Disconnected)); Assert.That(wire.SendCount, Is.EqualTo(1));
            }
        }
        [TestCase("0"), TestCase("-1"), TestCase("1.0"), TestCase("1e3"), TestCase("18446744073709551616"), TestCase(" ")]
        public void DevelopmentCredentialRejectsNonPositiveInteger(string account)
        {
            var method = Type.GetType("Client.Game.Login.DevelopmentCredentialProvider, Client.Game").GetMethod("Create");
            var error = Assert.Throws<System.Reflection.TargetInvocationException>(() => method.Invoke(null, new object[] { account }));
            Assert.That(error.InnerException, Is.TypeOf<ArgumentException>());
        }
        [Test] public void DevelopmentCredentialPreservesExactInteger()
        {
            var method = Type.GetType("Client.Game.Login.DevelopmentCredentialProvider, Client.Game").GetMethod("Create");
            var input = (LoginInput)method.Invoke(null, new object[] { "9007199254740993" });
            Assert.That(input.Credential, Is.EqualTo("demo:9007199254740993")); Assert.That(input.ResumeToken, Is.Null.Or.Empty);
        }
        private static SessionClient New(Connection c, ITransportClock clock = null) => new SessionClient(c, new SessionOptions("localhost", 1), clock: clock, context: new ImmediateContext());
        internal static Frame Success(ulong id) => new Frame { Version = 3, LoggedIn = new LoginResponse { RequestId = id, PlayerId = "1", SessionId = "session", Epoch = 9007199254740993UL, ResumeToken = "private-token", ResumeTokenTtlMs = 9007199254740995UL } };
        private static void Run(Func<Task> action) => Task.Run(action).GetAwaiter().GetResult();
        private static async Task<T> Limit<T>(Task<T> task) { if (await Task.WhenAny(task, Task.Delay(4000)) != task) Assert.Fail("Session deadline guard elapsed."); return await task; }
        private static async Task Until(Func<bool> condition) { var end = DateTime.UtcNow.AddSeconds(4); while (!condition() && DateTime.UtcNow < end) await Task.Delay(1); Assert.That(condition(), Is.True); }
        private sealed class Connection : IFrameConnection
        {
            public event Action<byte[]> FrameReceived; public event Action<TransportException> TransportFailed;
            public Action<byte[]> FrameReceivedSnapshot => FrameReceived;
            public int SubscriberCount => (FrameReceived?.GetInvocationList().Length ?? 0) + (TransportFailed?.GetInvocationList().Length ?? 0);
            public byte[] Sent; public int SendCount; public bool Closed; public TaskCompletionSource<bool> ConnectGate; public Exception ConnectError;
            public Task ConnectAsync(string host, int port, CancellationToken token) { Closed = false; if (ConnectError != null) throw ConnectError; return ConnectGate?.Task ?? Task.CompletedTask; }
            public Task SendAsync(ReadOnlyMemory<byte> bytes, CancellationToken token) { Sent = bytes.ToArray(); SendCount++; return Task.CompletedTask; }
            public void Close() { Closed = true; }
            public void Deliver(Frame frame) => FrameReceived?.Invoke(FrameCodec.Encode(frame));
        }
        private sealed class ImmediateContext : SynchronizationContext
        { public override void Post(SendOrPostCallback callback, object state) => callback(state); }
        private sealed class Context : SynchronizationContext
        {
            private readonly Queue<Action> actions = new Queue<Action>();
            private TaskCompletionSource<bool> nextPost;
            public Task<bool> ExpectPost()
            {
                lock (actions) { nextPost = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously); return nextPost.Task; }
            }
            public override void Post(SendOrPostCallback callback, object state)
            {
                lock (actions) { actions.Enqueue(() => callback(state)); nextPost?.TrySetResult(true); nextPost = null; }
            }
            public void Drain() { while (true) { Action next; lock (actions) { if (actions.Count == 0) return; next = actions.Dequeue(); } next(); } }
        }
        private sealed class Clock : ITransportClock
        {
            private readonly object gate = new object(); private TimeSpan now;
            private readonly List<Tuple<TimeSpan, TaskCompletionSource<bool>>> waits = new List<Tuple<TimeSpan, TaskCompletionSource<bool>>>();
            public TimeSpan Elapsed { get { lock (gate) return now; } }
            public Task Delay(TimeSpan duration, CancellationToken token)
            {
                var done = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                lock (gate) waits.Add(Tuple.Create(now + duration, done)); token.Register(() => done.TrySetCanceled()); return done.Task;
            }
            public void Advance(int seconds) { lock (gate) { now += TimeSpan.FromSeconds(seconds); foreach (var wait in waits) if (wait.Item1 <= now) wait.Item2.TrySetResult(true); } }
        }
    }
}
