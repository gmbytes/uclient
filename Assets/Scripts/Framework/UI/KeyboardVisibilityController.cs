using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Client.Framework.UI
{
    [DisallowMultipleComponent]
    public sealed class KeyboardVisibilityController : MonoBehaviour
    {
        [SerializeField] private ScrollRect m_Scroll;
        [SerializeField] private RectTransform m_PrimaryAction;
        private ScreenLayoutSnapshot snapshot;
        private Vector2 baseMin,baseMax,lastMin,lastMax;
        private bool captured;
        public Func<Rect> KeyboardAreaSource { get; set; }
        public Func<RectTransform> FocusSource { get; set; }
        public void Configure(ScrollRect scroll,RectTransform primaryAction)
        { m_Scroll=scroll; m_PrimaryAction=primaryAction; captured=false; }
        public void SetLayoutSnapshot(ScreenLayoutSnapshot value)
        { snapshot=value; captured=false; Capture(); }
        private void Capture()
        {
            if(m_Scroll==null || m_Scroll.viewport==null) return;
            var view=m_Scroll.viewport;
            baseMin=lastMin=view.offsetMin; baseMax=lastMax=view.offsetMax; captured=true;
        }
        private void LateUpdate()
        {
            if(snapshot==null || m_Scroll==null || m_Scroll.content==null) return;
            var selected=FocusSource!=null ? FocusSource() : EventSystem.current!=null ? EventSystem.current.currentSelectedGameObject?.GetComponent<RectTransform>() : null;
            if(selected!=null && !selected.IsChildOf(m_Scroll.content)) selected=null;
            Apply(snapshot,KeyboardAreaSource!=null?KeyboardAreaSource():TouchScreenKeyboard.visible?TouchScreenKeyboard.area:Rect.zero,selected);
        }
        /// <summary>Measured keyboard rect is bottom-left client pixels. Invalid/absent geometry keeps manual scrolling.</summary>
        public void Apply(ScreenLayoutSnapshot value,Rect keyboardArea,RectTransform focusedTarget)
        {
            snapshot=value;
            if(value==null || m_Scroll==null || m_Scroll.viewport==null || m_Scroll.content==null) return;
            var view=m_Scroll.viewport;
            if(!captured || view.offsetMin!=lastMin || view.offsetMax!=lastMax) Capture();
            view.offsetMin=baseMin; view.offsetMax=baseMax;
            m_Scroll.vertical=true;
            Rect pixels=PixelRect(view,new Vector2(value.Width,value.Height));
            bool measured=Finite(keyboardArea.x) && Finite(keyboardArea.y) && Finite(keyboardArea.width) && Finite(keyboardArea.height)
                && keyboardArea.width>0 && keyboardArea.height>0 && keyboardArea.yMin>=0 && keyboardArea.yMax<=value.Height
                && keyboardArea.xMin<pixels.xMax && keyboardArea.xMax>pixels.xMin && keyboardArea.yMin<pixels.yMax && keyboardArea.yMax>pixels.yMin;
            if(measured && pixels.height>0)
            {
                float covered=Mathf.Clamp(keyboardArea.yMax-pixels.yMin,0,pixels.height-1);
                view.offsetMin=baseMin+new Vector2(0,covered*view.rect.height/pixels.height);
                var target=focusedTarget!=null && focusedTarget.IsChildOf(m_Scroll.content)?focusedTarget:m_PrimaryAction;
                if(target!=null) Reveal(target);
            }
            lastMin=view.offsetMin; lastMax=view.offsetMax;
        }
        public void Reveal(RectTransform target)
        {
            if(m_Scroll==null || m_Scroll.viewport==null || m_Scroll.content==null || target==null) return;
            var view=m_Scroll.viewport;
            Bounds bounds=RectTransformUtility.CalculateRelativeRectTransformBounds(view,target);
            float delta=bounds.min.y<view.rect.yMin ? view.rect.yMin-bounds.min.y : bounds.max.y>view.rect.yMax ? view.rect.yMax-bounds.max.y : 0;
            m_Scroll.StopMovement(); m_Scroll.content.anchoredPosition+=new Vector2(0,delta);
            m_Scroll.verticalNormalizedPosition=Mathf.Clamp01(m_Scroll.verticalNormalizedPosition);
        }
        private static bool Finite(float value)=>!float.IsNaN(value)&&!float.IsInfinity(value);
        private static Rect PixelRect(RectTransform target,Vector2 size)
        {
            var canvas=target.GetComponentInParent<Canvas>();
            var root=canvas!=null?(RectTransform)canvas.rootCanvas.transform:(RectTransform)target.root;
            var corners=new Vector3[4]; target.GetWorldCorners(corners);
            var min=(Vector2)root.InverseTransformPoint(corners[0])-root.rect.min;
            var max=(Vector2)root.InverseTransformPoint(corners[2])-root.rect.min;
            return Rect.MinMaxRect(min.x/root.rect.width*size.x,min.y/root.rect.height*size.y,max.x/root.rect.width*size.x,max.y/root.rect.height*size.y);
        }
    }
}
