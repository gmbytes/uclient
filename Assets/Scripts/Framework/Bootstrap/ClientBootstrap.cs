using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Client.Contracts;
using Client.Framework.Release;
using Client.Framework.Resource;
using UnityEngine;

namespace Client.Bootstrap
{
    public sealed class ClientBootstrap : MonoBehaviour
    {
        [SerializeField] private UIUpdate m_UpdateUI;
        private readonly CancellationTokenSource lifetime = new CancellationTokenSource();
        private CancellationToken lifetimeToken;
        private CancellationTokenSource proofLifetime;
        private CancellationTokenSource launchLifetime;
        private IHotfixEntry proofEntry;
        private ReleaseCoordinator coordinator;
        private ResourceSlotBootstrap resources;
        private Func<ResourceSlotBootstrap, IClientContext> compose;
        private LaunchSelection selection;
        private Task launchTask;
        private Task shutdownTask;
        private bool started, autoStart, destroyed;
        public CancellationToken Lifetime => lifetimeToken;

        private void Awake()
        {
            lifetimeToken = lifetime.Token;
            if (m_UpdateUI != null) m_UpdateUI.ConfigureActions(Retry, Exit);
        }
        private async void Start()
        {
            if (coordinator == null)
            {
                if (m_UpdateUI != null) m_UpdateUI.ShowFailure("Startup setup is incomplete. Please exit.", false);
                return;
            }
            if (autoStart) await ObserveLaunchAsync();
        }

        /// <summary>Native build/composition supplies the original baked identity, pinned verifier and baseline inputs.</summary>
        public void ConfigureLaunch(ReleaseCoordinator coordinator, ResourceSlotBootstrap resources,
            Func<ResourceSlotBootstrap, IClientContext> compose, bool autoStart = true)
        {
            if (destroyed || started || launchTask != null || this.coordinator != null) throw new InvalidOperationException("The native host can only be configured once before launch.");
            this.coordinator = coordinator ?? throw new ArgumentNullException(nameof(coordinator));
            this.resources = resources != null ? resources : throw new ArgumentNullException(nameof(resources));
            this.compose = compose ?? throw new ArgumentNullException(nameof(compose)); this.autoStart = autoStart;
            coordinator.Changed += OnReleaseChanged;
        }
        public Task LaunchAsync(CancellationToken token)
        {
            if (coordinator == null || resources == null || compose == null) throw new InvalidOperationException("Native launch configuration is required before startup.");
            if (destroyed) throw new ObjectDisposedException(nameof(ClientBootstrap));
            if (launchTask != null && (!launchTask.IsCompleted || coordinator.TrialBegun || coordinator.RestartRequired)) return launchTask;
            launchLifetime?.Cancel(); launchLifetime?.Dispose();
            launchLifetime = CancellationTokenSource.CreateLinkedTokenSource(token, Lifetime);
            launchTask = RunLaunchAsync(launchLifetime.Token); return launchTask;
        }
        private async Task RunLaunchAsync(CancellationToken token)
        {
            selection = await coordinator.PrepareLaunchAsync(token);
            token.ThrowIfCancellationRequested();
            coordinator.BeginTrial(selection);
            // Keep the launch token alive after initialization; cancelling it never retries a native root.
            try
            {
                await coordinator.ActivateResourcesAsync(selection, resources, token);
                var source = compose(resources);
                await InitializeAsync(selection.Prepared, coordinator.Configuration.Player, coordinator.Configuration.EntryTypeName, source, token);
            }
            catch (OperationCanceledException cancelled)
            {
                try { await ShutdownAsync(CancellationToken.None); }
                catch (Exception cleanup) { throw new AggregateException("Launch cancellation and owned cleanup failed.", cancelled, cleanup); }
                throw;
            }
            catch (Exception error)
            {
                if (!coordinator.RestartRequired && !coordinator.IsReady)
                    coordinator.FailCurrentWithEvidence(ReleaseErrorCode.LocalReadinessFailed, error);
                throw;
            }
        }

        public Task InitializeAsync(PreparedRelease prepared, PlayerIdentity player, string entryTypeName, IClientContext context, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (coordinator == null || selection == null) throw new InvalidOperationException("A bound native launch selection is required before hot initialization.");
            if (started) throw new InvalidOperationException("The hotfix host has already started.");
            started = true;
            return coordinator.InitializeAsync(selection, prepared, player, entryTypeName, context, token);
        }
#if UNITY_EDITOR || MINIMAL_HOTFIX_PROOF
        // F3-only guarded harness. It is not a production release/readiness entry path.
        public async Task InitializeAsync(IHotfixEntry hotfixEntry, IClientContext context, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (started) throw new InvalidOperationException("The hotfix host has already started.");
            if (hotfixEntry == null) throw new ArgumentNullException(nameof(hotfixEntry));
            if (context == null) throw new ArgumentNullException(nameof(context));
            started = true; proofEntry = hotfixEntry;
            proofLifetime = CancellationTokenSource.CreateLinkedTokenSource(token, Lifetime, context.Lifetime);
            await hotfixEntry.InitializeAsync(context, proofLifetime.Token);
        }
#endif
        public Task ShutdownAsync(CancellationToken token)
        {
            if (shutdownTask != null) return shutdownTask;
            // Publish the shared completion before cancellation callbacks can re-enter shutdown.
            var completion = new TaskCompletionSource<bool>(); shutdownTask = completion.Task;
            _ = CompleteShutdownAsync(token, completion); return shutdownTask;
        }
        private async Task CompleteShutdownAsync(CancellationToken token, TaskCompletionSource<bool> completion)
        {
            var errors = new List<Exception>();
            Task stopping = null;
            try { stopping = coordinator?.ShutdownAsync(token); } catch (Exception error) { errors.Add(error); }
            try { if (!lifetimeToken.IsCancellationRequested) lifetime.Cancel(); } catch (Exception error) { errors.Add(error); }
            try
            {
                if (stopping != null) await stopping;
                var current = proofEntry; proofEntry = null;
                if (current != null) await current.ShutdownAsync(token);
            }
            catch (Exception error) { errors.Add(error); }
            finally
            {
                foreach (var cleanup in new Action[] { () => coordinator?.Dispose(), () => proofLifetime?.Dispose(), () => launchLifetime?.Dispose() })
                    try { cleanup(); } catch (Exception error) { errors.Add(error); }
                proofLifetime = launchLifetime = null;
            }
            if (errors.Count == 0) completion.TrySetResult(true);
            else if (errors.Count == 1 && errors[0] is OperationCanceledException) completion.TrySetCanceled();
            else completion.TrySetException(errors.Count == 1 ? errors[0] : new AggregateException("Owned shutdown operations failed.", errors));
        }
        public void HideUpdateUI()
        {
            if (coordinator == null || !coordinator.IsReady) return;
            if (m_UpdateUI != null) m_UpdateUI.gameObject.SetActive(false);
        }
        private void OnReleaseChanged(ReleaseDiagnostics value)
        {
            if (destroyed || m_UpdateUI == null) return;
            if (value.Phase == ReleasePhase.Ready) { HideUpdateUI(); return; }
            m_UpdateUI.SetPhase(value, !coordinator.TrialBegun && !coordinator.RestartRequired);
        }
        private async Task ObserveLaunchAsync()
        {
            try { await LaunchAsync(Lifetime); }
            catch (Exception error)
            {
                if (destroyed || m_UpdateUI == null) return;
                bool retry = coordinator != null && !coordinator.TrialBegun && !coordinator.RestartRequired;
                m_UpdateUI.ShowFailure(retry ? "Startup preparation failed. Retry or exit." : "Startup stopped. Restart the application.", retry);
                // Diagnostics retain the detailed local error; the page never displays credentials or raw transport data.
                if (coordinator != null && coordinator.Diagnostics.Error == null) Debug.LogWarning("CLIENT_STARTUP " + error.GetType().Name);
            }
        }
        private async void Retry() { if (!destroyed && coordinator != null && !coordinator.TrialBegun && !coordinator.RestartRequired) await ObserveLaunchAsync(); }
        private async void Exit() { try { await ShutdownAsync(CancellationToken.None); } finally { Application.Quit(); } }
        private void OnDestroy()
        {
            if (destroyed) return; destroyed = true;
            if (coordinator != null) { coordinator.Changed -= OnReleaseChanged; coordinator.Dispose(); }
            lifetime.Cancel(); proofLifetime?.Dispose(); proofLifetime = null;
            launchLifetime?.Dispose(); launchLifetime = null;
            lifetime.Dispose();
        }
    }
}
