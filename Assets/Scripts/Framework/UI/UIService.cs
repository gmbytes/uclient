using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Client.Contracts;
using GameFramework.Event;
using UnityEngine;
using UnityGameFramework.Runtime;

namespace Client.Framework.UI
{
    /// <summary>Main-thread GF UI adapter. Native UI/Resource helpers uniquely own prefab loads.</summary>
    public sealed class UIService : IUIService, IDisposable
    {
        private readonly UIComponent ui;
        private readonly EventComponent events;
        private readonly AdaptiveCanvas canvas;
        private readonly ScreenLayoutService screen;
        private readonly SynchronizationContext context;
        private readonly int threadId;
        private readonly Dictionary<string, UIViewDefinition> definitions = new Dictionary<string, UIViewDefinition>(StringComparer.Ordinal);
        private readonly HashSet<OpenRequest> pending = new HashSet<OpenRequest>();
        private readonly List<UIViewHandle> active = new List<UIViewHandle>();
        private bool listening, disposed;
        internal event Action<UIViewHandle> FirstFramePresented;

        public UIService(UIComponent ui, EventComponent events, UIViewDefinitions catalog, AdaptiveCanvas canvas, Transform windows)
        {
            this.ui = ui != null ? ui : throw new ArgumentNullException(nameof(ui));
            this.events = events != null ? events : throw new ArgumentNullException(nameof(events));
            this.canvas = canvas != null ? canvas : throw new ArgumentNullException(nameof(canvas));
            if (catalog == null) throw new ArgumentNullException(nameof(catalog));
            if (windows == null || windows.parent != canvas.transform || windows.name != "Windows" || !(windows is RectTransform)) throw new ArgumentException("Windows must be the full-frame direct child of the shared UI root.", nameof(windows));
            var mount=(RectTransform)windows;
            if(mount.anchorMin!=Vector2.zero || mount.anchorMax!=Vector2.one || mount.offsetMin!=Vector2.zero || mount.offsetMax!=Vector2.zero)
                throw new ArgumentException("Windows must be the full-frame direct child with stretch anchors and zero offsets.",nameof(windows));
            context = SynchronizationContext.Current; threadId = Thread.CurrentThread.ManagedThreadId;
            foreach (var definition in catalog.Definitions)
            {
                if (definition == null || string.IsNullOrWhiteSpace(definition.ViewID) || string.IsNullOrWhiteSpace(definition.PrefabAddress))
                    throw new ArgumentException("View ID and prefab address are required.", nameof(catalog));
                UIGroups.Depth(definition.Group);
                if (definitions.ContainsKey(definition.ViewID)) throw new ArgumentException("Duplicate view ID: " + definition.ViewID, nameof(catalog));
                definitions.Add(definition.ViewID, definition);
            }
            foreach (string group in UIGroups.Names)
            {
                if (!ui.HasUIGroup(group)) ui.AddUIGroup(group, UIGroups.Depth(group));
                var nativeGroup = ui.GetUIGroup(group);
                nativeGroup.Depth = UIGroups.Depth(group);
                var helper = (MonoBehaviour)nativeGroup.Helper;
                helper.transform.SetParent(windows, false);
                // Native default groups are Transform-only. A RectTransform mounting layer keeps
                // their full-frame bounds without introducing a per-window scaler/policy.
                var rect = helper.GetComponent<RectTransform>();
                if (rect == null) rect = helper.gameObject.AddComponent<RectTransform>();
                rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.offsetMin = rect.offsetMax = Vector2.zero;
            }
            screen = canvas.GetComponent<ScreenLayoutComponent>().Service;
            screen.Changed += ApplyLayout;
            if (screen.Current != null) canvas.Apply(screen.Current);
        }

        public Task<IViewHandle> OpenAsync(string viewId, CancellationToken token)
        {
            CheckThread();
            if (disposed) throw new ObjectDisposedException(nameof(UIService));
            if (string.IsNullOrWhiteSpace(viewId)) throw new ArgumentException("View ID is required.", nameof(viewId));
            if (!definitions.TryGetValue(viewId, out var definition)) throw new KeyNotFoundException("Unknown view: " + viewId);
            token.ThrowIfCancellationRequested();
            var request = new OpenRequest(this, definition);
            pending.Add(request); SetListening(true);
            try
            {
                // UIForm helper and its ResourceComponent perform the only prefab load/release.
                request.SerialId = ui.OpenUIForm(definition.PrefabAddress, definition.Group, false, request);
                request.Registration = token.Register(() =>
                {
                    request.Completion.TrySetCanceled(token);
                    OnMainThread(() => Cancel(request));
                });
                // Register can invoke synchronously if cancellation raced with the native open.
                if (!pending.Contains(request)) request.Registration.Dispose();
            }
            catch (Exception exception)
            {
                Remove(request); request.Completion.TrySetException(exception);
            }
            return request.Completion.Task;
        }

        private void OnOpened(object sender, GameEventArgs args)
        {
            var opened = (OpenUIFormSuccessEventArgs)args;
            if (!(opened.UserData is OpenRequest request) || request.Owner != this) return;
            var form = opened.UIForm;
            if (!pending.Contains(request) || disposed || request.Completion.Task.IsCanceled)
            {
                // A queued success may refer to an object already closed and reused by GF.
                if (form != null && form.SerialId == request.SerialId && ui.HasUIForm(request.SerialId)) ui.CloseUIForm(request.SerialId);
                Remove(request); return;
            }
            Remove(request);
            try
            {
                var bridge = form.Logic as UIViewBridge;
                if (bridge == null || bridge.RootCanvas == null || bridge.RootCanvasGroup == null || bridge.Bindings == null || bridge.LocalSafeArea == null)
                    throw new InvalidOperationException("Window requires UIViewBridge RootCanvas, RootCanvasGroup, Bindings and LocalSafeArea.");
                if (bridge.GetComponentInChildren<UnityEngine.UI.CanvasScaler>(true) != null)
                    throw new InvalidOperationException("Window must use the shared root CanvasScaler.");
                if (request.Definition.Modal && bridge.Mask == null) throw new InvalidOperationException("Modal window requires an explicit mask Button.");
                if (request.Definition.LayoutDefinition != null && (bridge.Layout == null || bridge.Layout.Definition != request.Definition.LayoutDefinition))
                    throw new InvalidOperationException("Window layout must match its view definition.");
                var rect = bridge.transform as RectTransform;
                if (rect != null) { rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.offsetMin = rect.offsetMax = Vector2.zero; rect.localScale = Vector3.one; }
                var handle = new UIViewHandle(this, bridge, request.Definition, request.SerialId);
                active.Add(handle); bridge.OrderChanged += RefreshInput;
                if (bridge.Mask != null)
                {
                    bridge.Mask.gameObject.SetActive(request.Definition.Modal);
                    if (request.Definition.CloseOnMask) handle.Listen(bridge.Mask, handle.Close);
                }
                bridge.Apply(screen.Current); RefreshInput();
                if (!request.Completion.TrySetResult(handle)) handle.Close();
            }
            catch (Exception exception)
            {
                if (ui.HasUIForm(request.SerialId)) ui.CloseUIForm(request.SerialId);
                request.Completion.TrySetException(exception);
            }
        }

        private void OnFailed(object sender, GameEventArgs args)
        {
            var failed = (OpenUIFormFailureEventArgs)args;
            if (!(failed.UserData is OpenRequest request) || request.Owner != this) return;
            Remove(request);
            request.Completion.TrySetException(new InvalidOperationException("Cannot open UI " + request.Definition.ViewID + ": " + failed.ErrorMessage));
        }

        private void Cancel(OpenRequest request)
        {
            if (!pending.Contains(request)) return;
            if (ui.IsLoadingUIForm(request.SerialId) || ui.HasUIForm(request.SerialId)) ui.CloseUIForm(request.SerialId);
            Remove(request);
        }

        private void Remove(OpenRequest request)
        {
            pending.Remove(request); request.Registration.Dispose();
            if (pending.Count == 0) SetListening(false);
        }

        private void SetListening(bool value)
        {
            if (listening == value) return;
            listening = value;
            if (value) { events.Subscribe(OpenUIFormSuccessEventArgs.EventId, OnOpened); events.Subscribe(OpenUIFormFailureEventArgs.EventId, OnFailed); }
            else { events.Unsubscribe(OpenUIFormSuccessEventArgs.EventId, OnOpened); events.Unsubscribe(OpenUIFormFailureEventArgs.EventId, OnFailed); }
        }

        internal void Close(UIViewHandle handle)
        {
            CheckThread();
            if (handle.IsClosed) return;
            if (ui.HasUIForm(handle.SerialId)) ui.CloseUIForm(handle.SerialId);
            handle.Invalidate();
        }

        internal void Forget(UIViewHandle handle)
        {
            handle.Bridge.OrderChanged -= RefreshInput;
            active.Remove(handle); RefreshInput();
        }

        internal Task WaitForFirstFrameAsync(UIViewHandle handle, CancellationToken token)
        {
            CheckThread(); token.ThrowIfCancellationRequested();
            if (disposed || handle.IsClosed || !active.Contains(handle)) throw new ObjectDisposedException(nameof(UIViewHandle));
            return new FirstFrameObservation(this, handle, token).Start();
        }

        internal bool CanPresent(UIViewHandle handle)
        {
            if (disposed || handle.IsClosed || handle.Lifetime.IsCancellationRequested || !active.Contains(handle) ||
                !ui.HasUIForm(handle.SerialId) || ui.GetUIForm(handle.SerialId).Logic != handle.Bridge) return false;
            var bridge = handle.Bridge;
            var snapshot = bridge.AppliedSnapshot;
            var windowCanvas = bridge.RootCanvas;
            if (snapshot == null || !ReferenceEquals(snapshot, screen.Current) || snapshot.Width <= 0 || snapshot.Height <= 0 ||
                !bridge.Visible || !bridge.isActiveAndEnabled || !bridge.gameObject.activeInHierarchy ||
                windowCanvas == null || !windowCanvas.isActiveAndEnabled ||
                SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null) return false;
            var rootCanvas = windowCanvas.rootCanvas;
            if (rootCanvas == null || !rootCanvas.isActiveAndEnabled || rootCanvas.pixelRect.width <= 0 || rootCanvas.pixelRect.height <= 0) return false;
            if (rootCanvas.renderMode == RenderMode.ScreenSpaceCamera &&
                (rootCanvas.worldCamera == null || !rootCanvas.worldCamera.isActiveAndEnabled)) return false;
            if (!(windowCanvas.transform is RectTransform rect) || rect.rect.width <= 0 || rect.rect.height <= 0) return false;
            float opacity = 1;
            for (Transform current = windowCanvas.transform; current != null; current = current.parent)
            {
                foreach (var currentCanvas in current.GetComponents<Canvas>()) if (!currentCanvas.isActiveAndEnabled) return false;
                foreach (var group in current.GetComponents<CanvasGroup>())
                    if (group.isActiveAndEnabled) opacity *= Mathf.Clamp01(group.alpha);
                if (opacity <= .001f) return false;
            }
            return true;
        }

        internal bool ConfirmFirstFrame(UIViewHandle handle)
        {
            CheckThread();
            if (!CanPresent(handle)) return false;
            if (!handle.HasPresentedFrame)
            {
                handle.MarkPresented();
                FirstFramePresented?.Invoke(handle);
            }
            return CanPresent(handle);
        }

        private void RefreshInput()
        {
            int modalOrder = int.MinValue;
            foreach (var handle in active)
                if (!handle.IsClosed && handle.Definition.Modal && handle.Bridge.Visible)
                    modalOrder = Math.Max(modalOrder, handle.Bridge.RootCanvas.sortingOrder);
            foreach (var handle in active)
                handle.Bridge.SetInputEnabled(handle.Bridge.RootCanvas.sortingOrder >= modalOrder);
        }

        private void ApplyLayout(ScreenLayoutSnapshot snapshot)
        {
            // The host may be constructed while GameRoot is inactive, before AdaptiveCanvas.OnEnable.
            // Apply root policy explicitly: subscription order must never select window geometry.
            // The shared root has no page layout; its own subscription can safely repeat root policy.
            canvas.Apply(snapshot);
            foreach (var handle in active) handle.Bridge.Apply(snapshot);
        }

        public void Dispose()
        {
            CheckThread(); if (disposed) return; disposed = true;
            screen.Changed -= ApplyLayout;
            foreach (var request in new List<OpenRequest>(pending))
            { request.Completion.TrySetCanceled(); Cancel(request); }
            foreach (var handle in active.ToArray()) handle.Close();
            SetListening(false);
            FirstFramePresented = null;
        }

        private void CheckThread()
        {
            if (Thread.CurrentThread.ManagedThreadId != threadId) throw new InvalidOperationException("UI operations require the Unity main thread.");
        }
        internal void OnMainThread(Action action)
        {
            if (Thread.CurrentThread.ManagedThreadId == threadId) action();
            else if (context != null) context.Post(_ => action(), null);
            else throw new InvalidOperationException("A Unity synchronization context is required for worker cancellation.");
        }

        private sealed class OpenRequest
        {
            public readonly UIService Owner;
            public readonly UIViewDefinition Definition;
            public readonly TaskCompletionSource<IViewHandle> Completion = new TaskCompletionSource<IViewHandle>();
            public int SerialId;
            public CancellationTokenRegistration Registration;
            public OpenRequest(UIService owner, UIViewDefinition definition) { Owner = owner; Definition = definition; }
        }
    }
}
