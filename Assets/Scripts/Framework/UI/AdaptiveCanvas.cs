using System;
using UnityEngine;
using UnityEngine.UI;

namespace Client.Framework.UI
{
    /// <summary>Shared root policy for GameRoot, the AOT startup canvas, and Editor previews.</summary>
    [DisallowMultipleComponent, RequireComponent(typeof(Canvas),typeof(CanvasScaler),typeof(ScreenLayoutComponent))]
    public sealed class AdaptiveCanvas : MonoBehaviour
    {
        [SerializeField] private SafeAreaRoot m_SafeArea;
        [SerializeField] private AdaptiveLayout m_Layout;
        [SerializeField] private KeyboardVisibilityController m_Keyboard;
        private ScreenLayoutComponent source;
        public void Configure(SafeAreaRoot safeArea, AdaptiveLayout layout, KeyboardVisibilityController keyboard)
        { m_SafeArea=safeArea; m_Layout=layout; m_Keyboard=keyboard; }
        private void OnEnable()
        {
            source=GetComponent<ScreenLayoutComponent>(); source.Service.Changed+=Apply;
            if(source.Service.Current!=null) Apply(source.Service.Current);
        }
        private void OnDisable() { if(source!=null) source.Service.Changed-=Apply; }
        public void Apply(ScreenLayoutSnapshot snapshot)
        {
            if(snapshot==null) throw new ArgumentNullException(nameof(snapshot));
            var scaler=GetComponent<CanvasScaler>();
            scaler.uiScaleMode=CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.screenMatchMode=CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight=.5f;
            scaler.referenceResolution=m_Layout!=null && m_Layout.Definition!=null
                ? m_Layout.Definition.For(snapshot.LayoutMode).ReferenceResolution
                : AdaptiveLayoutDefinition.ReferenceFor(snapshot.LayoutMode);
            // Resolve the new scaler before using reference-pixel form constraints.
            Canvas.ForceUpdateCanvases();
            if(m_SafeArea!=null) m_SafeArea.Apply(snapshot.ClampedSafeArea,new Vector2(snapshot.Width,snapshot.Height));
            if(m_Layout!=null) m_Layout.Apply(snapshot);
            if(m_Keyboard!=null) m_Keyboard.SetLayoutSnapshot(snapshot);
        }
    }
}
