using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Client.Contracts;
using Client.Framework.Resource;
using Client.Framework.HotUpdate;
using Client.Framework.UI;

namespace Client.Framework.Release
{
    public sealed class LaunchSelection
    {
        public PreparedRelease Prepared { get; }
        public long Epoch { get; }
        public string ReleaseId => Prepared.Release.ReleaseId;
        internal CancellationToken Token { get; }
        internal LaunchSelection(PreparedRelease prepared, long epoch, CancellationToken token) { Prepared = prepared; Epoch = epoch; Token = token; }
    }

    public sealed class ReleaseCoordinator : IDisposable
    {
        private static long nextEpoch;
        private static int nativeLaunchAttempted;
        private static readonly TimeSpan PreparationTimeout = TimeSpan.FromMinutes(10);
        private readonly ReleaseStateStore state;
        private readonly TrialLaunchPolicy policy;
        private readonly object sync;
        private readonly SemaphoreSlim preparing = new SemaphoreSlim(1, 1);
        private readonly SemaphoreSlim rootGate;
        private readonly NativeLaunchConfiguration configuration;
        private readonly ReleaseStore releaseStore;
        private readonly ManifestVerifier verifier;
        private readonly IReleaseTransport updates;
        private readonly IReleaseStreamReader baseline;
        private LaunchSelection current;
        private readonly CancellationTokenSource lifetime = new CancellationTokenSource();
        private readonly CancellationToken lifetimeToken;
        private readonly int threadId;
        private CancellationTokenSource launchLifetime, contextLifetime;
        private CancellationToken launchToken;
        private ResourceSlotBootstrap resources;
        private bool irreversible, resourceInitialized, assemblyAttempted, compositionAttempted;
        private IHotfixEntry entry;
#if UNITY_EDITOR
        // Editor fixtures replace only the unavailable native load boundary. Players compile the direct F6 call below.
        private Func<PreparedRelease, PlayerIdentity, IHotfixEntry> editorEntryLoader = null;
#endif
        private LaunchReadinessContext launchContext;
        private readonly TaskCompletionSource<bool> ready = new TaskCompletionSource<bool>();
        private bool ownsRootGate, disposed;
        private long preparationEpoch;
        private long plannedEpoch;
        private string preparingReleaseId;
        public bool TrialBegun { get; private set; }
        public bool RestartRequired { get; private set; }
        public bool IsReady { get; private set; }
        public ReleaseDiagnostics Diagnostics { get; } = new ReleaseDiagnostics();
        public NativeLaunchConfiguration Configuration => configuration;
        public event Action<ReleaseDiagnostics> Changed;

        public ReleaseCoordinator(ReleaseStateStore state, NativeLaunchConfiguration configuration, ReleaseStore releaseStore = null,
            ManifestVerifier verifier = null, IReleaseTransport updates = null, IReleaseStreamReader baseline = null)
        {
            this.state = state ?? throw new ArgumentNullException(nameof(state));
            this.configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
            this.releaseStore = releaseStore; this.verifier = verifier; this.updates = updates; this.baseline = baseline;
            if (updates != null && (releaseStore == null || verifier == null)) throw new ArgumentException("Updates require the real release store and pinned verifier.");
            if (baseline != null && verifier == null) throw new ArgumentException("A baseline requires the pinned verifier.");
            sync = ReleaseFileSystem.StateLock(state.Root); rootGate = ReleaseFileSystem.PrepareGate(state.Root); policy = new TrialLaunchPolicy(state);
            lifetimeToken = lifetime.Token; threadId = Thread.CurrentThread.ManagedThreadId;
        }

        public async Task<LaunchSelection> PrepareLaunchAsync(CancellationToken token)
        {
            using (var operation = CancellationTokenSource.CreateLinkedTokenSource(token, lifetimeToken))
                return await PrepareCoreAsync(token, operation.Token);
        }

        private async Task<LaunchSelection> PrepareCoreAsync(CancellationToken selectionToken, CancellationToken token)
        {
            await preparing.WaitAsync(token);
            try
            {
                long checkingEpoch;
                lock (sync)
                {
                    RequireSelectable(); checkingEpoch = ++preparationEpoch;
                    plannedEpoch = Interlocked.Increment(ref nextEpoch); preparingReleaseId = null;
                    Diagnostics.Selection = null;
                    SetPhase(ReleasePhase.Check);
                }
                await rootGate.WaitAsync(token);
                try { lock (sync) { RequireSelectable(); policy.RecoverUnfinished(); state.PruneUnreferencedSlots(); } }
                finally { rootGate.Release(); }
                token.ThrowIfCancellationRequested();
                bool preparationFailed = false;
                var preparationClock = System.Diagnostics.Stopwatch.StartNew();
                try
                {
                    using (var check = CancellationTokenSource.CreateLinkedTokenSource(token, lifetimeToken))
                    {
                        check.CancelAfter(PreparationTimeout);
                        if (updates != null) await CheckAndPrepareAsync(check.Token, checkingEpoch);
                        else if (baseline != null && Preferred(state.Read()) == null) await InstallBaselineAsync(check.Token);
                    }
                }
                catch (Exception error) when (!(error is OperationCanceledException) || !token.IsCancellationRequested && !lifetimeToken.IsCancellationRequested)
                {
                    preparationFailed = true;
                    RecordFailure("PreparationFailed", error);
                    SetPhase(ReleasePhase.PreparationFailed, "PreparationFailed", error);
                }
                token.ThrowIfCancellationRequested(); lifetimeToken.ThrowIfCancellationRequested();
                var available = state.Read();
                if (preparationFailed && baseline != null && available.KnownGood == null && available.PreviousGood == null)
                {
                    using (var install = CancellationTokenSource.CreateLinkedTokenSource(token, lifetimeToken))
                    {
                        var remaining = PreparationTimeout - preparationClock.Elapsed;
                        if (remaining <= TimeSpan.Zero) throw new TimeoutException("Preparation deadline expired before packaged baseline fallback.");
                        install.CancelAfter(remaining);
                        await InstallBaselineAsync(install.Token);
                    }
                    preparationFailed = false;
                }
                lock (sync)
                {
                    var stored = state.Read();
                    string id = preparationFailed ? stored.KnownGood ?? stored.PreviousGood : Preferred(stored);
                    if (id == null) throw new InvalidOperationException("No verified release is available; native baseline provisioning is required.");
                    var prepared = new PreparedRelease(ReleaseFileSystem.Child(state.Root, "slots/" + id), state.VerifySlot(id));
                    token.ThrowIfCancellationRequested();
                    current = new LaunchSelection(prepared, plannedEpoch, selectionToken);
                    Diagnostics.Selection = new ReleaseSelectionSnapshot(current);
                    SetPhase(ReleasePhase.Select); return current;
                }
            }
            catch (Exception error)
            {
                RecordFailure(error is OperationCanceledException ? "PreparationCancelled" : "LaunchPreparationFailed", error);
                throw;
            }
            finally { preparing.Release(); }
        }

        private static string Preferred(ReleaseState stored) => stored.Candidate ?? stored.KnownGood ?? stored.PreviousGood;

        private async Task CheckAndPrepareAsync(CancellationToken token, long checkingEpoch)
        {
            string scratch = ReleaseFileSystem.Child(state.Root, "staging/check-" + Guid.NewGuid().ToString("N") + ".partial");
            Directory.CreateDirectory(scratch);
            try
            {
                byte[] channelBytes = await DownloadMetadataAsync("channel.json", Path.Combine(scratch, "channel.json"), ManifestVerifier.MaximumDocumentBytes, token);
                byte[] channelSignature = await DownloadMetadataAsync("channel.sig", Path.Combine(scratch, "channel.sig"), 256, token);
                var channel = verifier.VerifyChannel(channelBytes, channelSignature);
                preparingReleaseId = channel.ReleaseId;
                if (state.Read().BadReleaseIds.Contains(channel.ReleaseId, StringComparer.Ordinal)) throw new InvalidOperationException("The signed channel offers a quarantined release.");
                byte[] manifest = await DownloadMetadataAsync(channel.ManifestRelativePath, Path.Combine(scratch, "release.json"), ManifestVerifier.MaximumDocumentBytes, token);
                string signaturePath = Path.ChangeExtension(channel.ManifestRelativePath, "sig").Replace('\\', '/');
                byte[] signature = await DownloadMetadataAsync(signaturePath, Path.Combine(scratch, "release.sig"), 256, token);
                var release = verifier.VerifyPinnedRelease(channel, manifest, signature, configuration.Player);
                SetPhase(ReleasePhase.Prepare);
                await releaseStore.PrepareAsync(release, new Progress<ReleaseProgress>(value =>
                {
                    lock (sync)
                    {
                        if (disposed || TrialBegun || preparationEpoch != checkingEpoch || Diagnostics.Phase != ReleasePhase.Prepare) return;
                        Diagnostics.Progress = value; Changed?.Invoke(Diagnostics);
                    }
                }), token);
            }
            finally { Directory.Delete(scratch, true); }
        }

        private async Task<byte[]> DownloadMetadataAsync(string path, string destination, int maximum, CancellationToken token)
        {
            await updates.DownloadAsync(path, destination, token);
            token.ThrowIfCancellationRequested();
            var info = new FileInfo(destination);
            if (!info.Exists || info.Length <= 0 || info.Length > maximum) throw new InvalidDataException("Release metadata length is outside the bounded limit.");
            return File.ReadAllBytes(destination);
        }

        private async Task InstallBaselineAsync(CancellationToken token)
        {
            byte[] manifest = await ReadBaselineMetadataAsync("release.json", ManifestVerifier.MaximumDocumentBytes, token);
            byte[] signature = await ReadBaselineMetadataAsync("release.sig", 256, token);
            var verified = verifier.Verify(manifest, signature, configuration.Player);
            SetPhase(ReleasePhase.Prepare);
            await new BaseReleaseInstaller(state.Root, baseline, state).InstallAsync(verified, null, token);
        }

        private async Task<byte[]> ReadBaselineMetadataAsync(string path, int maximum, CancellationToken token)
        {
            using (var input = await baseline.OpenReadAsync(path, token))
            using (var output = new MemoryStream())
            {
                byte[] buffer = new byte[4096]; int count;
                while ((count = await input.ReadAsync(buffer, 0, buffer.Length, token)) > 0)
                {
                    if (output.Length + count > maximum) throw new InvalidDataException("Packaged metadata exceeds the bounded limit.");
                    output.Write(buffer, 0, count);
                }
                token.ThrowIfCancellationRequested(); return output.ToArray();
            }
        }

        public void BeginTrial(LaunchSelection selection)
        {
            lock (sync)
            {
                RequireCurrent(selection); selection.Token.ThrowIfCancellationRequested();
                if (TrialBegun) throw new InvalidOperationException("This launch already began.");
                if (!rootGate.Wait(0)) throw new InvalidOperationException("Release preparation is still active.");
                ownsRootGate = true;
                try { policy.Begin(selection.ReleaseId); TrialBegun = true; SetPhase(ReleasePhase.Trial); }
                catch (Exception error) { RecordFailure("BeginTrialFailed", error); ownsRootGate = false; rootGate.Release(); throw; }
            }
        }

        public void ReportReady(string releaseId)
        {
            lock (sync)
            {
                if (disposed || RestartRequired || current == null || current.ReleaseId != releaseId || current.Token.IsCancellationRequested || !TrialBegun || IsReady) return;
                // Compatibility shape only: an ID cannot identify a launch epoch or its context.
                // Only LaunchReadinessContext.ReportReady reaches the selection-bound request path.
                Diagnostics.Code = "UnboundReadyRejected";
            }
        }

        public async Task ActivateResourcesAsync(LaunchSelection selection, ResourceSlotBootstrap bootstrap, CancellationToken token)
        {
            lock (sync)
            {
                RequireCurrent(selection);
                if (!TrialBegun || irreversible || bootstrap == null) throw new InvalidOperationException("A durable launch trial and native resource bootstrap are required.");
                token.ThrowIfCancellationRequested(); selection.Token.ThrowIfCancellationRequested();
                if (Interlocked.Exchange(ref nativeLaunchAttempted, 1) != 0) throw new InvalidOperationException("A native launch was already attempted; restart is required.");
                irreversible = true; resources = bootstrap;
                launchLifetime = CancellationTokenSource.CreateLinkedTokenSource(token, selection.Token, lifetimeToken);
                launchLifetime.CancelAfter(configuration.LocalReadyTimeout); launchToken = launchLifetime.Token;
                SetPhase(ReleasePhase.ResourceInit);
            }
            try
            {
                // Invocation is the conservative irreversible boundary, including partial native failures.
                launchToken.ThrowIfCancellationRequested();
                bootstrap.Activate(selection.Prepared.Root);
                await AwaitWithCancellation(bootstrap.Initialization, launchToken);
                lock (sync) { RequireCurrent(selection); launchToken.ThrowIfCancellationRequested(); resourceInitialized = true; }
            }
            catch (OperationCanceledException error) { HandleCancellation(selection, token, CancellationToken.None, error); throw; }
            catch (Exception error) { FailCurrentWithEvidence(ReleaseErrorCode.ResourceInitializationFailed, error); throw; }
        }

        public async Task InitializeComposedAsync(LaunchSelection selection,
            Func<ResourceSlotBootstrap, CancellationToken, Task<IClientContext>> compose, CancellationToken token)
        {
            lock (sync)
            {
                RequireCurrent(selection);
                if (!resourceInitialized || compositionAttempted || assemblyAttempted || compose == null) throw new InvalidOperationException("Native composition requires initialized resources and can begin only once.");
                compositionAttempted = true;
            }
            Task<IClientContext> pending = null; IClientContext source = null; bool transferred = false;
            Exception failure = null;
            try
            {
                launchToken.ThrowIfCancellationRequested(); token.ThrowIfCancellationRequested();
                pending = compose(resources, launchToken) ?? throw new InvalidOperationException("Native composition returned no task.");
                await AwaitWithCancellation(pending, launchToken);
                source = await pending;
                launchToken.ThrowIfCancellationRequested(); token.ThrowIfCancellationRequested();
                Task initialization = InitializeAsync(selection, selection.Prepared, configuration.Player, configuration.EntryTypeName, source, token);
                transferred = true;
                await initialization;
            }
            catch (OperationCanceledException error)
            {
                failure = error;
                if (source == null && pending != null) _ = DisposeAbandonedCompositionAsync(pending);
                HandleCancellation(selection, token, CancellationToken.None, error); throw;
            }
            catch (Exception error) { failure = error; if (!RestartRequired) FailCurrentWithEvidence(ReleaseErrorCode.LocalReadinessFailed, error); throw; }
            finally
            {
                if (!transferred && source != null)
                    try { (source as IDisposable)?.Dispose(); }
                    catch (Exception cleanup) { RecordFailure("CompositionCleanupFailed", cleanup); throw failure == null ? cleanup : new AggregateException("Native composition and cleanup failed.", failure, cleanup); }
            }
        }
        private async Task DisposeAbandonedCompositionAsync(Task<IClientContext> pending)
        {
            try { var abandoned = await pending; (abandoned as IDisposable)?.Dispose(); }
            catch (OperationCanceledException) { }
            catch (Exception error) { RecordFailure("LateCompositionCleanupFailed", error); }
        }

        public Task InitializeAsync(LaunchSelection selection, PreparedRelease prepared, PlayerIdentity nativePlayer, string entryTypeName,
            IClientContext source, CancellationToken token)
        {
            lock (sync)
            {
                RequireCurrent(selection);
                if (!resourceInitialized || resources == null || !resources.Initialization.IsCompletedSuccessfully || !ReferenceEquals(selection.Prepared, prepared) ||
                    assemblyAttempted || source == null || !ReferenceEquals(source.Resources, resources.Service) || entryTypeName != configuration.EntryTypeName)
                    throw new InvalidOperationException("The host requires its actual initialized resource selection and native launch configuration.");
                ReleaseCompatibility.RequireExact(configuration.Player, nativePlayer);
                assemblyAttempted = true;
            }
            return InitializeBoundAsync(selection, source, token);
        }

        private async Task InitializeBoundAsync(LaunchSelection selection, IClientContext source, CancellationToken token)
        {
            bool bound = false; Exception failure = null;
            try
            {
                launchToken.ThrowIfCancellationRequested(); token.ThrowIfCancellationRequested();
                var context = CreateReadinessContext(selection, source, token);
                bound = true;
                var gate = context.Gate;
                gate.RecordResourceInitialized(); SetPhase(ReleasePhase.Metadata);
#if UNITY_EDITOR
                if (editorEntryLoader != null) entry = editorEntryLoader(selection.Prepared, configuration.Player);
                else
#endif
                    entry = new HotfixAssemblyLoader(configuration.EntryTypeName).LoadOnce(selection.Prepared, configuration.Player);
                gate.RecordHotfixLoaded(); gate.VerifyLocalEnvelope(); SetPhase(ReleasePhase.Hotfix);
                await CompleteEntryAsync(selection, entry, context, token);
            }
            catch (OperationCanceledException error) { failure = error; HandleCancellation(selection, token, source.Lifetime, error); throw; }
            catch (Exception error) { failure = error; if (!RestartRequired) FailCurrentWithEvidence(ReleaseErrorCode.HotfixInitializationFailed, error); throw; }
            finally
            {
                if (!bound) try { (source as IDisposable)?.Dispose(); }
                    catch (Exception cleanup) { RecordFailure("UnboundContextCleanupFailed", cleanup); throw failure == null ? cleanup : new AggregateException("Native initialization and unbound cleanup failed.", failure, cleanup); }
            }
        }

        private LaunchReadinessContext CreateReadinessContext(LaunchSelection selection, IClientContext source, CancellationToken token)
        {
            lock (sync)
            {
                RequireCurrent(selection);
                if (!TrialBegun || launchContext != null) throw new InvalidOperationException("A readiness context can only bind once to the current begun trial.");
                if (!(source.UI is UIService ui)) throw new InvalidOperationException("A native UIService is required for local readiness.");
                contextLifetime = CancellationTokenSource.CreateLinkedTokenSource(launchToken, source.Lifetime, token, lifetimeToken);
                var gate = new LocalReadinessGate(ui, configuration.ReadyViewId, contextLifetime.Token);
                launchContext = new LaunchReadinessContext(source, gate, contextLifetime.Token, value => RequestReady(selection, value));
                gate.Invalidated += OnReadyViewInvalidated; return launchContext;
            }
        }

        private async Task CompleteEntryAsync(LaunchSelection selection, IHotfixEntry hotfixEntry, LaunchReadinessContext context, CancellationToken token)
        {
            try
            {
                context.Lifetime.ThrowIfCancellationRequested();
                entry = hotfixEntry;
                await AwaitWithCancellation(hotfixEntry.InitializeAsync(context, context.Lifetime), context.Lifetime);
                lock (sync)
                {
                    RequireCurrent(selection); context.Lifetime.ThrowIfCancellationRequested();
                    context.Gate.EntrySucceeded(); SetPhase(ReleasePhase.LoginReady); TryCommitReady(selection, context);
                }
                await AwaitWithCancellation(ready.Task, context.Lifetime);
                // The ready timeout bounds startup, not the successfully running page lifetime.
                launchLifetime?.CancelAfter(Timeout.InfiniteTimeSpan);
            }
            catch (OperationCanceledException error) { HandleCancellation(selection, token, context.OriginalLifetime, error); throw; }
            catch (Exception error) { if (!RestartRequired) FailCurrentWithEvidence(ReleaseErrorCode.HotfixInitializationFailed, error); throw; }
        }

        private void RequestReady(LaunchSelection selection, LaunchReadinessContext context)
        {
            if (Thread.CurrentThread.ManagedThreadId != threadId) return;
            lock (sync)
            {
                if (!CurrentAndLive(selection, context) || !context.Gate.ReportReady()) { Diagnostics.Code = "IneligibleReadyRejected"; return; }
                TryCommitReady(selection, context);
            }
        }
        private bool CurrentAndLive(LaunchSelection selection, LaunchReadinessContext context) => !disposed && !RestartRequired && !IsReady &&
            TrialBegun && ReferenceEquals(selection, current) && current.Epoch == selection.Epoch && ReferenceEquals(context, launchContext) &&
            !selection.Token.IsCancellationRequested && !context.Lifetime.IsCancellationRequested;
        private void TryCommitReady(LaunchSelection selection, LaunchReadinessContext context)
        {
            if (!CurrentAndLive(selection, context) || !context.Gate.CanCommit) return;
            try
            {
                // Read/modify/write shares the same per-root lock; token is checked again before atomic replace.
                policy.Promote(selection.ReleaseId, context.Lifetime);
                IsReady = true; SetPhase(ReleasePhase.Ready); ready.TrySetResult(true);
            }
            catch (OperationCanceledException error)
            {
                RecordFailure("LaunchCancelled", error);
                RestartRequired = true; contextLifetime?.Cancel(); context.Gate.Abort(); SetPhase(ReleasePhase.RestartRequired, "LaunchCancelled");
                ready.TrySetCanceled(); throw;
            }
            catch (Exception error)
            {
                RecordFailure("ReadyStatePersistenceFailed", error);
                RestartRequired = true; contextLifetime?.Cancel(); context.Gate.Abort(); SetPhase(ReleasePhase.RestartRequired, "ReadyStatePersistenceFailed", error);
                ready.TrySetException(error); throw;
            }
        }
        private void OnReadyViewInvalidated()
        {
            if (IsReady || RestartRequired || disposed) return;
            var original = new InvalidOperationException("The expected ready view closed before commit.");
            try { FailCurrentWithEvidence(ReleaseErrorCode.LocalReadinessFailed, original); ready.TrySetException(original); }
            catch (Exception error) { ready.TrySetException(error); }
        }
        private void HandleCancellation(LaunchSelection selection, CancellationToken caller, CancellationToken source, OperationCanceledException error)
        {
            lock (sync)
            {
                if (IsReady || RestartRequired || disposed || !ReferenceEquals(current, selection)) return;
                if (caller.IsCancellationRequested || source.IsCancellationRequested || selection.Token.IsCancellationRequested || lifetimeToken.IsCancellationRequested)
                {
                    RecordFailure("LaunchCancelled", error);
                    RestartRequired = true; launchContext?.Gate.Abort(); SetPhase(ReleasePhase.RestartRequired, "LaunchCancelled");
                    // Preserve the begun trial for next-process unfinished-trial recovery.
                }
                else FailCurrentWithEvidence(ReleaseErrorCode.LocalReadyTimeout, error);
            }
        }
        private static async Task AwaitWithCancellation(Task operation, CancellationToken token)
        {
            var cancelled = new TaskCompletionSource<bool>();
            using (token.Register(() => cancelled.TrySetCanceled()))
            {
                Task winner = await Task.WhenAny(operation, cancelled.Task);
                if (!ReferenceEquals(winner, operation))
                    _ = operation.ContinueWith(completed => { var observed = completed.Exception; }, CancellationToken.None,
                        TaskContinuationOptions.ExecuteSynchronously | TaskContinuationOptions.OnlyOnFaulted, TaskScheduler.Default);
                await winner; token.ThrowIfCancellationRequested();
            }
        }
        public async Task ShutdownAsync(CancellationToken token)
        {
            var errors = new List<Exception>();
            IHotfixEntry currentEntry;
            lock (sync)
            {
                if (TrialBegun && !IsReady && !RestartRequired) { RestartRequired = true; SetPhase(ReleasePhase.RestartRequired, "LaunchCancelled"); }
                currentEntry = entry; entry = null;
                try { if (!lifetimeToken.IsCancellationRequested) lifetime.Cancel(); }
                catch (Exception error) { RecordFailure("LifetimeCancellationFailed", error); errors.Add(error); }
            }
            launchContext?.Gate.Abort();
            if (currentEntry != null)
            {
                using (var shutdown = CancellationTokenSource.CreateLinkedTokenSource(token))
                {
                    shutdown.CancelAfter(TimeSpan.FromSeconds(5));
                    try { await AwaitWithCancellation(currentEntry.ShutdownAsync(shutdown.Token), shutdown.Token); }
                    catch (Exception error) { RecordFailure("EntryShutdownFailed", error); errors.Add(error); }
                }
            }
            if (errors.Count == 1) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(errors[0]).Throw();
            if (errors.Count > 1) throw new AggregateException("Native launch shutdown failed.", errors);
        }

        public void FailCurrent(ReleaseErrorCode code) => FailCurrentWithEvidence(code, null);
        public void FailCurrentWithEvidence(ReleaseErrorCode code, Exception original)
        {
            lock (sync)
            {
                if (disposed || current == null || RestartRequired || IsReady) return;
                RecordFailure(code.ToString(), original);
                RestartRequired = true;
                var secondary = new List<Exception>();
                foreach (var cancel in new Action[] { () => contextLifetime?.Cancel(), () => launchLifetime?.Cancel() })
                    try { cancel(); } catch (Exception error) { RecordFailure("CancellationCleanupFailed", error); secondary.Add(error); }
                try { policy.Quarantine(current.ReleaseId); }
                catch (Exception error) { RecordFailure("FailureStatePersistenceFailed", error); secondary.Add(error); }
                Exception retained = original;
                if (secondary.Count > 0)
                {
                    retained = original == null && secondary.Count == 1 ? secondary[0]
                        : new AggregateException("Local startup and failure-state/cleanup operations failed.", (original == null ? secondary : new[] { original }.Concat(secondary)).ToArray());
                    SetPhase(ReleasePhase.RestartRequired, "FailureStatePersistenceFailed", retained);
                    System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(retained).Throw();
                }
                SetPhase(ReleasePhase.RestartRequired, code.ToString(), retained);
            }
        }
        private void RecordFailure(string code, Exception error)
        {
            bool selected = current != null && current.Epoch == plannedEpoch;
            Diagnostics.RecordFailure(selected ? current.ReleaseId : preparingReleaseId, selected ? current.Epoch : plannedEpoch,
                Diagnostics.Phase, code, error);
        }
        private void RequireSelectable()
        {
            if (disposed) throw new ObjectDisposedException(nameof(ReleaseCoordinator));
            if (TrialBegun || RestartRequired || Volatile.Read(ref nativeLaunchAttempted) != 0) throw new InvalidOperationException("This process already selected a launch; restart is required.");
        }
        private void RequireCurrent(LaunchSelection selection)
        {
            if (disposed || selection == null || !ReferenceEquals(selection, current) || selection.Epoch != current.Epoch || RestartRequired)
                throw new InvalidOperationException("The launch selection is stale or stopped.");
        }
        private void SetPhase(ReleasePhase phase, string code = null, Exception error = null)
        { Diagnostics.Phase = phase; Diagnostics.Code = code; Diagnostics.Error = error; Changed?.Invoke(Diagnostics); }
        public void Dispose()
        {
            lock (sync)
            {
                if (disposed) return; disposed = true; Changed = null;
                var errors = new List<Exception>();
                foreach (var cleanup in new Action[] { lifetime.Cancel, () => launchContext?.Dispose(), () => contextLifetime?.Dispose(),
                    () => launchLifetime?.Dispose(), lifetime.Dispose, () => (updates as IDisposable)?.Dispose() })
                    try { cleanup(); } catch (Exception error) { RecordFailure("OwnedCleanupFailed", error); errors.Add(error); }
                if (ownsRootGate && !irreversible) { ownsRootGate = false; rootGate.Release(); }
                if (errors.Count > 0) throw new AggregateException("Native launch scope cleanup failed.", errors);
            }
        }
    }
}
