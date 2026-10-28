using System;
using UnityEngine;
using UnityEngine.UI;
using UnityGameFramework.Runtime;

namespace Client.Framework.UI
{
    /// <summary>Native GF lifecycle only. Awake has no entry/resource/network dependency.</summary>
    [DisallowMultipleComponent]
    public sealed class UIViewBridge : UIFormLogic
    {
        [SerializeField] private Canvas m_RootCanvas;
        [SerializeField] private CanvasGroup m_RootCanvasGroup;
        [SerializeField] private UIBindings m_Bindings;
        [SerializeField] private AdaptiveLayout m_Layout;
        [SerializeField] private KeyboardVisibilityController m_Keyboard;
        [SerializeField] private Button m_Mask;
        [SerializeField] private SafeAreaRoot m_LocalSafeArea;
        public Canvas RootCanvas => m_RootCanvas;
        public CanvasGroup RootCanvasGroup => m_RootCanvasGroup;
        public UIBindings Bindings => m_Bindings;
        public AdaptiveLayout Layout => m_Layout;
        public KeyboardVisibilityController Keyboard => m_Keyboard;
        public Button Mask => m_Mask;
        public SafeAreaRoot LocalSafeArea => m_LocalSafeArea;
        public event Action<bool> VisibilityChanged;
        public event Action Closed;
        internal event Action OrderChanged;

        public void Configure(Canvas rootCanvas, CanvasGroup rootCanvasGroup, UIBindings bindings,
            AdaptiveLayout layout = null, KeyboardVisibilityController keyboard = null, Button mask = null, SafeAreaRoot localSafeArea = null)
        {
            m_RootCanvas = rootCanvas; m_RootCanvasGroup = rootCanvasGroup; m_Bindings = bindings;
            m_Layout = layout; m_Keyboard = keyboard; m_Mask = mask; m_LocalSafeArea = localSafeArea;
        }

        public Component Resolve(string bindingId) => m_Bindings != null
            ? m_Bindings.Resolve(bindingId) : throw new InvalidOperationException("Window has no UIBindings.");

        /// <summary>Shared runtime/preview ordering: local safe content, layout, then keyboard baseline.</summary>
        public void Apply(ScreenLayoutSnapshot snapshot)
        {
            if (snapshot == null) return;
            if (m_LocalSafeArea != null) m_LocalSafeArea.Apply(snapshot.ClampedSafeArea, new Vector2(snapshot.Width, snapshot.Height));
            if (m_Layout != null) m_Layout.Apply(snapshot);
            if (m_Keyboard != null) m_Keyboard.SetLayoutSnapshot(snapshot);
        }

        internal void SetInputEnabled(bool enabled)
        {
            if (m_RootCanvasGroup == null) return;
            m_RootCanvasGroup.interactable = enabled && Visible;
            m_RootCanvasGroup.blocksRaycasts = enabled && Visible;
        }

        protected override void OnClose(bool isShutdown, object userData)
        {
            base.OnClose(isShutdown, userData);
            try { Closed?.Invoke(); }
            finally { Closed = null; VisibilityChanged = null; OrderChanged = null; }
        }
        protected override void OnDepthChanged(int uiGroupDepth, int depthInUIGroup)
        {
            base.OnDepthChanged(uiGroupDepth, depthInUIGroup);
            if (m_RootCanvas != null)
            {
                m_RootCanvas.overrideSorting = true;
                m_RootCanvas.sortingOrder = uiGroupDepth * 5000 + depthInUIGroup * 2 + 1;
            }
            transform.SetAsLastSibling();
            OrderChanged?.Invoke();
        }
        protected override void InternalSetVisible(bool visible)
        {
            base.InternalSetVisible(visible);
            if (m_RootCanvasGroup != null) m_RootCanvasGroup.alpha = visible ? 1 : 0;
            VisibilityChanged?.Invoke(visible);
            OrderChanged?.Invoke();
        }
    }
}
