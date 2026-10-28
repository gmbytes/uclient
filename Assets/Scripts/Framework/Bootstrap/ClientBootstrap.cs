using System;
using System.Threading;
using System.Threading.Tasks;
using Client.Contracts;
using UnityEngine;

namespace Client.Bootstrap
{
    public sealed class ClientBootstrap : MonoBehaviour
    {
        [SerializeField] private UIUpdate m_UpdateUI;
        private readonly CancellationTokenSource lifetime = new CancellationTokenSource();
        private IHotfixEntry entry;
        private bool started;
        public CancellationToken Lifetime => lifetime.Token;

        // The production release coordinator supplies its verified, once-loaded entry in F6.
        public async Task InitializeAsync(IHotfixEntry hotfixEntry, IClientContext context, CancellationToken token)
        {
            if (started) throw new InvalidOperationException("The hotfix host has already started.");
            if (hotfixEntry == null) throw new ArgumentNullException(nameof(hotfixEntry));
            if (context == null) throw new ArgumentNullException(nameof(context));
            started = true;
            entry = hotfixEntry;
            if (m_UpdateUI != null) m_UpdateUI.SetStatus("Starting", false);
            using (var linked = CancellationTokenSource.CreateLinkedTokenSource(token, Lifetime))
                await entry.InitializeAsync(context, linked.Token);
        }

        public async Task ShutdownAsync(CancellationToken token)
        {
            lifetime.Cancel();
            var current = entry;
            entry = null;
            if (current != null) await current.ShutdownAsync(token);
        }

        public void HideUpdateUI()
        {
            if (m_UpdateUI != null) m_UpdateUI.gameObject.SetActive(false);
        }

        private void OnDestroy() { lifetime.Cancel(); lifetime.Dispose(); }
    }
}
