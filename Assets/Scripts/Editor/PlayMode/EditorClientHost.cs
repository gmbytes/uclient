using System;
using System.Threading;
using System.Threading.Tasks;
using Client.Bootstrap;
using Client.Contracts;
using Client.Framework.Resource;
using Client.Framework.UI;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityGameFramework.Runtime;
using Object = UnityEngine.Object;

namespace Client.Editor.PlayMode
{
    // Editor assemblies cannot attach MonoBehaviours. This owner uses Editor/scene lifecycle
    // events so all development composition stays outside the Player assemblies.
    public sealed class EditorClientHost : ILogService, IDisposable
    {
        readonly CancellationTokenSource lifetime = new CancellationTokenSource();
        readonly BaseComponent framework;
        readonly GameObject owner;
        readonly int sceneHandle;
        IHotfixEntry entry;
        EditorContext context;
        UIUpdate updateUI;
        GameObject uiRoot;
        Task shutdown;
        bool started, disposed;

        public EditorClientHost(BaseComponent framework, Scene scene)
        {
            this.framework = framework;
            sceneHandle = scene.handle;
            owner = new GameObject("Editor Client");
            SceneManager.MoveGameObjectToScene(owner, scene);
            owner.transform.SetParent(framework.transform, false);
            EditorApplication.update += Tick;
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
            SceneManager.sceneUnloaded += OnSceneUnloaded;
            AssemblyReloadEvents.beforeAssemblyReload += Dispose;
        }

        void Tick()
        {
            if (!Application.isPlaying || owner == null || framework == null) { Dispose(); return; }
            // Editor updates can run before a newly loaded scene's Start callbacks. Wait for
            // actual GF setup instead of guessing how many Editor/game frames have elapsed.
            var resources = framework.GetComponent<EditorResourceComponent>();
            if (!started && !string.IsNullOrEmpty(resources.ReadOnlyPath)
                && framework.GetComponentInChildren<UIFormHelperBase>() != null)
            {
                started = true;
                BeginAsync();
            }
        }

        async void BeginAsync()
        {
            try { await InitializeAsync(); }
            catch (OperationCanceledException) { }
            catch (Exception error)
            {
                if (updateUI != null) updateUI.SetStatus("Editor startup failed. See Console.", true);
                Debug.LogException(error);
                Dispose();
            }
        }

        async Task InitializeAsync()
        {
            var token = lifetime.Token;
            updateUI = Object.FindObjectOfType<UIUpdate>();
            var resource = new ResourceService(framework.GetComponent<ResourceComponent>());
            var catalog = await resource.LoadAsync<UIViewDefinitions>("Assets/Content/UI/Common/UIViewDefinitions.asset", token);
            try
            {
                token.ThrowIfCancellationRequested();
                uiRoot = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Content/UI/Common/AdaptiveUIRoot.prefab"), owner.transform);
                var ui = new UIService(framework.GetComponent<UIComponent>(), framework.GetComponent<EventComponent>(),
                    catalog.Asset, uiRoot.GetComponent<AdaptiveCanvas>(), uiRoot.transform.Find("Windows"));
                context = new EditorContext(resource, ui, catalog, this, token);
                catalog = null;
                var cameraObject = new GameObject("Editor Camera", typeof(Camera));
                cameraObject.transform.SetParent(owner.transform, false);
                var camera = cameraObject.GetComponent<Camera>();
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = new Color(.02f, .025f, .04f);
                camera.cullingMask = 0;
                camera.depth = -100;
                if (Object.FindObjectOfType<AudioListener>() == null) cameraObject.AddComponent<AudioListener>();
                entry = (IHotfixEntry)Activator.CreateInstance(Type.GetType("Client.Game.Entry, Client.Game", true));
                await entry.InitializeAsync(context, token);
                token.ThrowIfCancellationRequested();
                if (!context.Ready) throw new InvalidOperationException("Editor entry returned without a rendered login page.");
                if (updateUI != null) updateUI.gameObject.SetActive(false);
                Debug.Log("EDITOR_CLIENT_READY LocalSimulation");
            }
            catch
            {
                catalog?.Dispose();
                await StopAsync();
                throw;
            }
        }

        Task StopAsync() => shutdown ?? (shutdown = StopOwnedAsync());

        async Task StopOwnedAsync()
        {
            lifetime.Cancel();
            try
            {
                if (entry != null) await entry.ShutdownAsync(CancellationToken.None);
            }
            finally
            {
                entry = null;
                context?.Dispose();
                context = null;
                if (uiRoot != null) Object.Destroy(uiRoot);
                if (owner != null) Object.Destroy(owner);
                lifetime.Dispose();
            }
        }

        void OnPlayModeChanged(PlayModeStateChange state)
        { if (state == PlayModeStateChange.ExitingPlayMode) Dispose(); }

        void OnSceneUnloaded(Scene scene)
        { if (scene.handle == sceneHandle) Dispose(); }

        void ReleaseFrameworkRegistration()
        {
            // GF clears its component registry via Shutdown, not BaseComponent.OnDestroy.
            // Only release our own old registration; never shut down a replacement scene's host.
            if (ReferenceEquals(GameEntry.GetComponent<BaseComponent>(), framework))
                GameEntry.Shutdown(ShutdownType.None);
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            EditorApplication.update -= Tick;
            EditorApplication.playModeStateChanged -= OnPlayModeChanged;
            SceneManager.sceneUnloaded -= OnSceneUnloaded;
            AssemblyReloadEvents.beforeAssemblyReload -= Dispose;
            StopAndReportAsync();
        }

        async void StopAndReportAsync()
        {
            try { await StopAsync(); }
            catch (Exception error) { Debug.LogException(error); }
            // Startup failure also disposes this owner and removes its scene callbacks.
            // Releasing here guarantees that a later reload cannot inherit stale GF components.
            finally { ReleaseFrameworkRegistration(); }
        }

        public void Write(ClientLogLevel level, string code, string message)
        {
            string value = "EDITOR_CLIENT " + code + " " + message;
            if (level == ClientLogLevel.Error) Debug.LogError(value);
            else if (level == ClientLogLevel.Warning) Debug.LogWarning(value);
            else Debug.Log(value);
        }

        sealed class EditorContext : IClientContext, IDisposable
        {
            readonly UIService ui;
            readonly IAssetHandle<UIViewDefinitions> catalog;
            bool disposed;
            public IResourceService Resources { get; }
            public IUIService UI => ui;
            public ISessionClient Session => null;
            public ILogService Log { get; }
            public CancellationToken Lifetime { get; }
            public bool Ready { get; private set; }

            public EditorContext(IResourceService resources, UIService ui, IAssetHandle<UIViewDefinitions> catalog,
                ILogService log, CancellationToken lifetime)
            { Resources = resources; this.ui = ui; this.catalog = catalog; Log = log; Lifetime = lifetime; }

            // Editor readiness only controls the local startup overlay; it never commits release health.
            public void ReportReady() => Ready = true;

            public void Dispose()
            {
                if (disposed) return;
                disposed = true;
                try { ui.Dispose(); }
                finally { catalog.Dispose(); }
            }
        }
    }
}
