using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Client.Framework.UI
{
    [DisallowMultipleComponent, DefaultExecutionOrder(1000)]
    public sealed class KeyboardVisibilityController : MonoBehaviour
    {
        [SerializeField] private ScrollRect m_Scroll;
        [SerializeField] private RectTransform m_PrimaryAction;
        private ScreenLayoutSnapshot snapshot;
        private Vector2 baseMin,baseMax,lastMin,lastMax;
        private bool captured;
        private bool manualFallback;
        private ScrollRect.MovementType baseMovementType;
        private readonly Vector3[] corners=new Vector3[4];
        public Func<Rect> KeyboardAreaSource { get; set; }
        /// <summary>Optional visibility seam. Explicit false overrides stale keyboard geometry.</summary>
        public Func<bool> KeyboardVisibleSource { get; set; }
        public Func<RectTransform> FocusSource { get; set; }

        public void Configure(ScrollRect scroll,RectTransform primaryAction)
        {
            RestoreManualFallback();
            m_Scroll=scroll; m_PrimaryAction=primaryAction; captured=false;
        }

        public void SetLayoutSnapshot(ScreenLayoutSnapshot value)
        { snapshot=value; captured=false; Capture(); }

        private void Capture()
        {
            if(m_Scroll==null || m_Scroll.viewport==null) return;
            var view=m_Scroll.viewport;
            baseMin=lastMin=view.offsetMin; baseMax=lastMax=view.offsetMax; captured=true;
            // AdaptiveLayout may have installed a new policy during a rotation.
            if(!manualFallback || m_Scroll.movementType!=ScrollRect.MovementType.Unrestricted)
                baseMovementType=m_Scroll.movementType;
        }

        private void OnDisable() => RestoreManualFallback();

        private void LateUpdate()
        {
            if(snapshot==null || m_Scroll==null || m_Scroll.content==null) return;
            var eventSystem=EventSystem.current;
            var selectedObject=eventSystem!=null ? eventSystem.currentSelectedGameObject : null;
            // A selected window control can be destroyed before EventSystem clears its reference.
            // Unity's destroyed-object null check must run before accessing that GameObject.
            var selected=FocusSource!=null ? FocusSource() : selectedObject!=null ? selectedObject.GetComponent<RectTransform>() : null;
            if(selected!=null && !selected.IsChildOf(m_Scroll.content)) selected=null;
            Apply(snapshot,KeyboardAreaSource!=null?KeyboardAreaSource():TouchScreenKeyboard.visible?TouchScreenKeyboard.area:Rect.zero,selected);
        }

        /// <summary>
        /// Measured area uses bottom-left client pixels. A positive finite area retains legacy
        /// visibility inference; KeyboardVisibleSource can explicitly distinguish visible/unknown from closed.
        /// </summary>
        public void Apply(ScreenLayoutSnapshot value,Rect keyboardArea,RectTransform focusedTarget)
        {
            snapshot=value;
            if(value==null || m_Scroll==null || m_Scroll.viewport==null || m_Scroll.content==null) return;
            var view=m_Scroll.viewport;
            if(!captured || view.offsetMin!=lastMin || view.offsetMax!=lastMax) Capture();
            view.offsetMin=baseMin; view.offsetMax=baseMax;
            m_Scroll.vertical=true;
            bool visible=KeyboardVisibleSource!=null ? KeyboardVisibleSource()
                : TouchScreenKeyboard.visible || HasArea(keyboardArea);
            Rect pixels=PixelRect(view,new Vector2(value.Width,value.Height));
            bool measured=visible && HasArea(keyboardArea)
                && keyboardArea.yMin>=0 && keyboardArea.yMax<=value.Height
                && keyboardArea.xMin<pixels.xMax && keyboardArea.xMax>pixels.xMin && keyboardArea.yMin<pixels.yMax && keyboardArea.yMax>pixels.yMin;
            if(measured && pixels.height>0)
            {
                RestoreManualFallback();
                float covered=Mathf.Clamp(keyboardArea.yMax-pixels.yMin,0,pixels.height-1);
                view.offsetMin=baseMin+new Vector2(0,covered*view.rect.height/pixels.height);
                var target=focusedTarget!=null && focusedTarget.IsChildOf(m_Scroll.content)?focusedTarget:m_PrimaryAction;
                if(target!=null) Reveal(target);
            }
            else if(visible)
            {
                AllowManualTravel();
            }
            else
            {
                RestoreManualFallback();
            }
            lastMin=view.offsetMin; lastMax=view.offsetMax;
        }

        private void AllowManualTravel()
        {
            if(!manualFallback || m_Scroll.movementType!=ScrollRect.MovementType.Unrestricted)
                baseMovementType=m_Scroll.movementType;
            manualFallback=true;
            // No guessed keyboard rectangle: keep authored geometry and permit bounded manual travel.
            m_Scroll.movementType=ScrollRect.MovementType.Unrestricted;
            float actionHeight=m_PrimaryAction!=null ? BoundsInViewport(m_PrimaryAction).size.y : 0;
            ClampVerticalTravel(Mathf.Max(0,m_Scroll.viewport.rect.height-actionHeight));
        }

        private void RestoreManualFallback()
        {
            if(!manualFallback) return;
            manualFallback=false;
            if(m_Scroll==null) return;
            m_Scroll.movementType=baseMovementType;
            if(m_Scroll.viewport!=null && m_Scroll.content!=null) ClampVerticalTravel(0);
            m_Scroll.StopMovement();
        }

        private void ClampVerticalTravel(float extraTravel)
        {
            var view=m_Scroll.viewport;
            Bounds bounds=BoundsInViewport(m_Scroll.content);
            float minDelta=view.rect.yMax-bounds.max.y;
            float maxDelta=Mathf.Max(minDelta,view.rect.yMin-bounds.min.y+extraTravel);
            float delta=Mathf.Clamp(0,minDelta,maxDelta);
            if(Mathf.Abs(delta)>.001f)
            {
                m_Scroll.content.anchoredPosition+=new Vector2(0,delta);
                m_Scroll.StopMovement();
            }
        }

        private Bounds BoundsInViewport(RectTransform target)
        {
            target.GetWorldCorners(corners);
            var bounds=new Bounds(m_Scroll.viewport.InverseTransformPoint(corners[0]),Vector3.zero);
            for(int i=1;i<4;i++) bounds.Encapsulate(m_Scroll.viewport.InverseTransformPoint(corners[i]));
            return bounds;
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
        private static bool HasArea(Rect value)=>Finite(value.x) && Finite(value.y) && Finite(value.width) && Finite(value.height) && value.width>0 && value.height>0;
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
