using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Client.Contracts;
using Client.Framework.Resource;
using Client.Framework.UI;
using UnityEngine;
using UnityGameFramework.Runtime;

namespace Client.Framework.Release
{
    /// <summary>Normal native UI/service composition, shared by build profiles. No release selection or trust decisions.</summary>
    public sealed class NativeClientComposition
    {
        private readonly GameObject rootPrefab;
        private readonly string catalogAddress;
        private readonly ILogService log;
        private readonly ISessionClient session;
        private readonly CancellationToken lifetime;
        private bool composed;
        public NativeClientComposition(GameObject rootPrefab, string catalogAddress, ILogService log, CancellationToken lifetime, ISessionClient session = null)
        { this.rootPrefab = rootPrefab != null ? rootPrefab : throw new ArgumentNullException(nameof(rootPrefab)); this.catalogAddress = !string.IsNullOrWhiteSpace(catalogAddress) ? catalogAddress : throw new ArgumentException("A packaged catalog address is required.",nameof(catalogAddress)); this.log = log ?? throw new ArgumentNullException(nameof(log)); this.lifetime = lifetime; this.session = session; }
        public async Task<IClientContext> ComposeAsync(ResourceSlotBootstrap resources, CancellationToken token)
        {
            if (composed || resources == null || !resources.Initialization.IsCompletedSuccessfully) throw new InvalidOperationException("Native resources must initialize exactly once before context composition.");
            // Acceptance transfers the supplied session to this attempt, including its failure path.
            composed = true;
            IAssetHandle<UIViewDefinitions> catalog = null; GameObject root = null; UIService ui = null;
            try
            {
                catalog = await resources.Service.LoadAsync<UIViewDefinitions>(catalogAddress, token);
                token.ThrowIfCancellationRequested(); root = UnityEngine.Object.Instantiate(rootPrefab);
                ui = new UIService(resources.ResourceComponent.GetComponent<UIComponent>(), GameEntry.GetComponent<EventComponent>(), catalog.Asset,
                    root.GetComponent<AdaptiveCanvas>(), root.transform.Find("Windows"));
                return new Context(resources.Service, ui, root, catalog, session, log, lifetime);
            }
            catch (Exception original)
            {
                var errors = new List<Exception> { original };
                foreach (var cleanup in new Action[] { () => ui?.Dispose(), () => catalog?.Dispose(), () => (session as IDisposable)?.Dispose(), () => { if (root != null) UnityEngine.Object.Destroy(root); } })
                    try { cleanup(); } catch (Exception error) { errors.Add(error); }
                if (errors.Count > 1) throw new AggregateException("Native composition and owned cleanup failed.", errors);
                throw;
            }
        }
        private sealed class Context : IClientContext, IDisposable
        {
            private readonly UIService ui; private readonly GameObject root; private bool disposed;
            private readonly IAssetHandle<UIViewDefinitions> catalog;
            public IResourceService Resources { get; } public IUIService UI => ui; public ISessionClient Session { get; }
            public ILogService Log { get; } public CancellationToken Lifetime { get; }
            public Context(IResourceService resources, UIService ui, GameObject root, IAssetHandle<UIViewDefinitions> catalog, ISessionClient session, ILogService log, CancellationToken lifetime)
            { Resources = resources; this.ui = ui; this.root = root; this.catalog = catalog; Session = session; Log = log; Lifetime = lifetime; }
            public void ReportReady() { throw new InvalidOperationException("Only the coordinator's captured readiness context may report Ready."); }
            public void Dispose()
            {
                if (disposed) return; disposed = true; var errors = new List<Exception>();
                foreach (var cleanup in new Action[] { ui.Dispose, catalog.Dispose, () => (Session as IDisposable)?.Dispose(), () => UnityEngine.Object.Destroy(root) })
                    try { cleanup(); } catch (Exception error) { errors.Add(error); }
                if (errors.Count != 0) throw new AggregateException("Native context cleanup failed.", errors);
            }
        }
    }
}
