using System;
using UnityEngine;
using UnityEngine.UI;

namespace Client.Framework.UI
{
    [DisallowMultipleComponent, RequireComponent(typeof(RectTransform))]
    public sealed class AdaptiveLayout : MonoBehaviour
    {
        [SerializeField] private AdaptiveLayoutDefinition m_Definition;
        [SerializeField] private RectTransform m_Brand;
        [SerializeField] private RectTransform m_Viewport;
        [SerializeField] private ScrollRect m_Scroll;
        [SerializeField] private RectTransform m_Content;
        [SerializeField] private RectTransform m_Submit;
        public AdaptiveLayoutDefinition Definition => m_Definition;

        public void Configure(AdaptiveLayoutDefinition definition, RectTransform brand, RectTransform viewport,
            ScrollRect scroll, RectTransform content, RectTransform submit)
        {
            m_Definition=definition; m_Brand=brand; m_Viewport=viewport; m_Scroll=scroll; m_Content=content; m_Submit=submit;
        }

        public void Apply(ScreenLayoutSnapshot snapshot)
        {
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
            if (m_Definition == null || m_Viewport == null || m_Content == null || m_Scroll == null) return;
            var profile=m_Definition.For(snapshot.LayoutMode);
            Rect bounds=((RectTransform)transform).rect;
            var margin=profile.Margins;
            if(m_Brand != null)
            {
                m_Brand.anchorMin=profile.BrandAnchors.min; m_Brand.anchorMax=profile.BrandAnchors.max;
                m_Brand.offsetMin=new Vector2(margin.x,margin.y); m_Brand.offsetMax=new Vector2(-margin.z,-margin.w);
            }
            var area=profile.FormAnchors;
            float width=Mathf.Max(1,Mathf.Min(profile.MaximumFormWidth,bounds.width*area.width-margin.x-margin.z));
            float height=Mathf.Max(1,bounds.height*area.height-margin.y-margin.w);
            float position=m_Scroll.verticalNormalizedPosition;
            m_Viewport.anchorMin=m_Viewport.anchorMax=area.center;
            m_Viewport.pivot=new Vector2(.5f,.5f);
            m_Viewport.sizeDelta=new Vector2(width,height);
            m_Viewport.anchoredPosition=new Vector2((margin.x-margin.z)*.5f,(margin.y-margin.w)*.5f);
            m_Content.anchorMin=new Vector2(0,1); m_Content.anchorMax=Vector2.one; m_Content.pivot=new Vector2(.5f,1);
            m_Content.sizeDelta=new Vector2(0,Mathf.Max(height,profile.MinimumContentHeight));
            m_Content.anchoredPosition=Vector2.zero;
            if(m_Submit != null) m_Submit.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical,Mathf.Max(96,profile.MinimumButtonHeight));
            m_Scroll.horizontal=false; m_Scroll.vertical=true; m_Scroll.movementType=ScrollRect.MovementType.Clamped;
            m_Scroll.StopMovement();
            m_Scroll.verticalNormalizedPosition=Mathf.Clamp01(position);
            // A single scheduled rebuild; controls are never disabled or reconstructed.
            LayoutRebuilder.MarkLayoutForRebuild(m_Content);
        }
    }
}
