using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Client.Contracts;
using Tok.Client.V1;

namespace Client.Framework.Network
{
    /// <summary>One login at a time; no business replay, heartbeat or automatic resume.</summary>
    public sealed class SessionClient : ISessionClient, IDisposable
    {
        private readonly IFrameConnection connection;
        private readonly SessionOptions options;
        private readonly RequestIdSequence ids;
        private readonly ITransportClock clock;
        private readonly SynchronizationContext context;
        private readonly object queueGate = new object();
        private readonly Queue<Action> actions = new Queue<Action>();
        private bool draining, disposed;
        private Attempt current;
        private long epoch;
        private volatile SessionState state;
        private volatile SessionIdentity identity;
        public SessionState State => state;
        public SessionIdentity Identity => identity;
        // Owning-context observations only; these values confer no request/state mutation capability.
        public long ConnectionEpoch => epoch;
        public ulong ActiveRequestId => current?.RequestId ?? 0;
        public event Action<SessionState> StateChanged;
#if UNITY_EDITOR
        // Per-instance scheduling seam for the split mailbox publication regression only.
        // Excluded from normal Player/managed builds; never set by production callers.
        internal Action MailboxOccupiedForTest;
#endif

        public SessionClient(IFrameConnection connection, SessionOptions options, RequestIdSequence ids = null,
            ITransportClock clock = null, SynchronizationContext context = null)
        {
            this.connection = connection ?? throw new ArgumentNullException(nameof(connection));
            this.options = options ?? throw new ArgumentNullException(nameof(options));
            this.ids = ids ?? new RequestIdSequence(); this.clock = clock ?? new TransportClock();
            this.context = context ?? SynchronizationContext.Current;
        }

        public Task<LoginResult> LoginAsync(LoginInput input, CancellationToken token)
        {
            var result = new TaskCompletionSource<LoginResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            Dispatch(() => Begin(input, token, result));
            return result.Task;
        }

        public void Disconnect() => Dispatch(() => End(current, LoginError.Disconnected));
        public void Dispose() => Dispatch(() => { if (disposed) return; disposed = true; End(current, LoginError.Disconnected); });

        private void Begin(LoginInput input, CancellationToken token, TaskCompletionSource<LoginResult> result)
        {
            if (disposed) { result.TrySetResult(Failure(LoginError.Disconnected)); return; }
            if (current != null) { result.TrySetResult(Failure(LoginError.Busy)); return; }
            if (input == null || string.IsNullOrWhiteSpace(input.Credential) == string.IsNullOrWhiteSpace(input.ResumeToken))
            { result.TrySetResult(Failure(LoginError.InvalidInput)); return; }
            if (token.IsCancellationRequested) { result.TrySetResult(Failure(LoginError.Cancelled)); return; }
            ulong requestId;
            try { requestId = ids.Next(); }
            catch (InvalidOperationException) { result.TrySetResult(Failure(LoginError.ProtocolError)); return; }
            var attempt = new Attempt(++epoch, requestId, result);
            current = attempt; identity = null;
            attempt.FrameHandler = bytes => Receive(attempt, bytes);
            attempt.ErrorHandler = error => Dispatch(() =>
            {
                if (IsCurrent(attempt)) End(attempt, MapTransport(error, false));
            });
            connection.FrameReceived += attempt.FrameHandler;
            connection.TransportFailed += attempt.ErrorHandler;
            SetState(SessionState.Connecting);
            attempt.Cancel = token.Register(() => Dispatch(() => End(attempt, LoginError.Cancelled)));
            if (!IsCurrent(attempt)) return;
            StartDeadline(attempt, SessionState.Connecting, options.ConnectTimeout);
            Task connect;
            try { connect = connection.ConnectAsync(options.Host, options.Port, attempt.Lifetime.Token); }
            catch (Exception error) { End(attempt, MapTransport(error, true)); return; }
            CompleteConnect(connect, attempt, input);
        }

        private async void CompleteConnect(Task connect, Attempt attempt, LoginInput input)
        {
            Exception failure = null;
            try { await connect.ConfigureAwait(false); } catch (Exception error) { failure = error; }
            Dispatch(() =>
            {
                if (!IsCurrent(attempt)) return;
                if (failure != null) { End(attempt, MapTransport(failure, true)); return; }
                if (Expired(attempt)) { End(attempt, LoginError.Timeout); return; }
                SetState(SessionState.Authenticating);
                StartDeadline(attempt, SessionState.Authenticating, options.LoginTimeout);
                bool credentialLogin = !string.IsNullOrWhiteSpace(input.Credential);
                var request = new Frame { Version = 3, Login = new LoginRequest
                { RequestId = attempt.RequestId, Credential = credentialLogin ? input.Credential : "", ResumeToken = credentialLogin ? "" : input.ResumeToken } };
                try { CompleteSend(connection.SendAsync(FrameCodec.Encode(request), attempt.Lifetime.Token), attempt); }
                catch (Exception error) { End(attempt, MapTransport(error, false)); }
            });
        }

        private async void CompleteSend(Task send, Attempt attempt)
        {
            try { await send.ConfigureAwait(false); }
            catch (Exception error) { Dispatch(() => { if (IsCurrent(attempt)) End(attempt, MapTransport(error, false)); }); }
        }

        private void Receive(Attempt attempt, byte[] bytes)
        {
            // Only one parsed frame can await the owner context. A burst cannot create an
            // unbounded second receive queue outside the transport's budget.
            lock (attempt.MailboxGate)
            {
                if (attempt.FramePending)
                {
#if UNITY_EDITOR
                    MailboxOccupiedForTest?.Invoke();
#endif
                    attempt.FrameOverflow = true;
                    return;
                }
                attempt.FramePending = true;
            }
            Frame frame = null;
            try { frame = FrameCodec.Decode(bytes); } catch (TransportException) { }
            Dispatch(() =>
            {
                bool overflow;
                lock (attempt.MailboxGate)
                {
                    overflow = attempt.FrameOverflow;
                    attempt.FrameOverflow = false;
                    attempt.FramePending = false;
                }
                if (!IsCurrent(attempt)) return;
                if (overflow || frame == null || frame.Version != 3 || state != SessionState.Authenticating)
                { End(attempt, LoginError.ProtocolError); return; }
                if (Expired(attempt)) { End(attempt, LoginError.Timeout); return; }
                if (frame.ContentCase == Frame.ContentOneofCase.Error)
                {
                    if (frame.Error.RequestId != attempt.RequestId || frame.Error.ErrorCode == 0) End(attempt, LoginError.ProtocolError);
                    else End(attempt, MapServer(frame.Error.ErrorCode), frame.Error.ErrorCode);
                    return;
                }
                var response = frame.LoggedIn;
                if (response == null || response.RequestId != attempt.RequestId) { End(attempt, LoginError.ProtocolError); return; }
                if (response.ErrorCode != 0) { End(attempt, MapServer(response.ErrorCode), response.ErrorCode); return; }
                if (string.IsNullOrWhiteSpace(response.PlayerId) || string.IsNullOrWhiteSpace(response.SessionId) || response.Epoch == 0 ||
                    string.IsNullOrWhiteSpace(response.ResumeToken) || response.ResumeTokenTtlMs == 0)
                { End(attempt, LoginError.ProtocolError); return; }
                StopDeadline(attempt); attempt.Cancel.Dispose();
                identity = new SessionIdentity(response.PlayerId, response.SessionId, response.Epoch, response.ResumeToken, response.ResumeTokenTtlMs);
                SetState(SessionState.Authenticated);
                attempt.Result.TrySetResult(new LoginResult(true, LoginError.None, 0, identity, "Login succeeded."));
            });
        }

        private void StartDeadline(Attempt attempt, SessionState stage, TimeSpan duration)
        {
            StopDeadline(attempt);
            attempt.Deadline = clock.Elapsed + duration;
            var alarm = attempt.Alarm = new CancellationTokenSource();
            WatchDeadline(attempt, stage, alarm);
        }
        private async void WatchDeadline(Attempt attempt, SessionState stage, CancellationTokenSource alarm)
        {
            try
            {
                var token = alarm.Token;
                while (true)
                {
                    TimeSpan remaining = attempt.Deadline - clock.Elapsed;
                    if (remaining <= TimeSpan.Zero) break;
                    await clock.Delay(remaining, token).ConfigureAwait(false);
                    token.ThrowIfCancellationRequested();
                }
                Dispatch(() => { if (IsCurrent(attempt) && state == stage && ReferenceEquals(attempt.Alarm, alarm)) End(attempt, LoginError.Timeout); });
            }
            catch (OperationCanceledException) { }
        }
        private bool Expired(Attempt attempt) => clock.Elapsed >= attempt.Deadline;
        private static void StopDeadline(Attempt attempt)
        { var alarm = attempt.Alarm; attempt.Alarm = null; if (alarm != null) { alarm.Cancel(); alarm.Dispose(); } }

        private bool IsCurrent(Attempt attempt) => attempt != null && ReferenceEquals(current, attempt) && attempt.Epoch == epoch;
        private void End(Attempt attempt, LoginError error, uint serverCode = 0)
        {
            if (!IsCurrent(attempt)) return;
            current = null; epoch++; identity = null;
            connection.FrameReceived -= attempt.FrameHandler;
            connection.TransportFailed -= attempt.ErrorHandler;
            StopDeadline(attempt); attempt.Cancel.Dispose();
            attempt.Lifetime.Cancel(); connection.Close(); attempt.Lifetime.Dispose();
            SetState(SessionState.Disconnected);
            attempt.Result.TrySetResult(Failure(error, serverCode));
        }

        private static LoginError MapServer(uint code) => code == 3 ? LoginError.Rejected : code == 7 ? LoginError.Timeout : LoginError.ProtocolError;
        private static LoginError MapTransport(Exception error, bool connecting)
        {
            if (error is OperationCanceledException) return LoginError.Cancelled;
            if (error is TransportException transport)
            {
                if (transport.Code == TransportError.Timeout) return LoginError.Timeout;
                if (transport.Code == TransportError.Cancelled) return LoginError.Cancelled;
                if (transport.Code == TransportError.InvalidFrame || transport.Code == TransportError.TruncatedFrame) return LoginError.ProtocolError;
            }
            return connecting ? LoginError.ConnectFailed : LoginError.Disconnected;
        }
        private static LoginResult Failure(LoginError error, uint serverCode = 0) => new LoginResult(false, error, serverCode, null, "Login ended: " + error + ".");
        private void SetState(SessionState value)
        {
            if (state == value) return;
            state = value;
            var handlers = StateChanged;
            if (handlers == null) return;
            foreach (Action<SessionState> handler in handlers.GetInvocationList())
            {
                try { handler(value); }
                catch (Exception error) { System.Diagnostics.Trace.TraceError("Session observer threw {0}.", error.GetType().FullName); }
            }
        }

        // No lifecycle monitor is held while invoking the connection or observers. This also
        // avoids lock inversion with adapters that invoke their events under a lifecycle gate.
        private void Dispatch(Action action)
        {
            bool schedule;
            lock (queueGate) { actions.Enqueue(action); schedule = !draining; if (schedule) draining = true; }
            if (!schedule) return;
            if (context != null && !ReferenceEquals(SynchronizationContext.Current, context)) context.Post(_ => Drain(), null);
            else Drain();
        }
        private void Drain()
        {
            while (true)
            {
                Action action;
                lock (queueGate) { if (actions.Count == 0) { draining = false; return; } action = actions.Dequeue(); }
                action();
            }
        }
        private sealed class Attempt
        {
            public readonly long Epoch;
            public readonly ulong RequestId;
            public readonly TaskCompletionSource<LoginResult> Result;
            public readonly CancellationTokenSource Lifetime = new CancellationTokenSource();
            public CancellationTokenSource Alarm;
            public CancellationTokenRegistration Cancel;
            public TimeSpan Deadline;
            public Action<byte[]> FrameHandler;
            public Action<TransportException> ErrorHandler;
            public readonly object MailboxGate = new object();
            public bool FramePending, FrameOverflow;
            public Attempt(long epoch, ulong requestId, TaskCompletionSource<LoginResult> result) { Epoch = epoch; RequestId = requestId; Result = result; }
        }
    }
}
