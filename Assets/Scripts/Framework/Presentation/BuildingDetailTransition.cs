using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace Client.Framework.Presentation
{
    /// <summary>One owned transition for every building: immediate 1.3x fade-in and collapse, without moving the map.</summary>
    [DisallowMultipleComponent]
    public sealed class BuildingDetailTransition:MonoBehaviour
    {
        [SerializeField] CityViewport viewport;
        [SerializeField] RectTransform page,backdrop;
        [SerializeField] CanvasGroup pageGroup,backdropGroup;
        [SerializeField] GameObject hud;
        Coroutine operation;
        Vector2 pageTarget,backdropTarget;
        bool originalRaycast;
        public bool IsOpen{get;private set;}
        public bool IsTransitioning{get;private set;}
        public const float OpenDuration=.30f,CloseDuration=.30f;

        public void Configure(CityViewport map,RectTransform content,RectTransform background,GameObject cityHud)
        {
            viewport=map;page=content;backdrop=background;hud=cityHud;
            pageGroup=page.GetComponent<CanvasGroup>();
            if(pageGroup==null)pageGroup=page.gameObject.AddComponent<CanvasGroup>();
            backdropGroup=backdrop.GetComponent<CanvasGroup>();
            if(backdropGroup==null)backdropGroup=backdrop.gameObject.AddComponent<CanvasGroup>();
        }
        public void Open(int buildingId)
        {
            if(IsOpen||IsTransitioning)return;
            pageTarget=backdropTarget=Vector2.zero;
            if(viewport!=null&&viewport.IsReady)
            {
                if(viewport.TryGetBuildingCenter(buildingId,out var center))CacheCollapsePoint(center);
            }
            var image=viewport!=null?viewport.GetComponent<RawImage>():null;
            originalRaycast=image!=null&&image.raycastTarget;if(image!=null)image.raycastTarget=false;
            if(hud!=null)hud.SetActive(false);
            IsOpen=true;page.gameObject.SetActive(true);backdrop.gameObject.SetActive(true);
            pageGroup.blocksRaycasts=backdropGroup.blocksRaycasts=true;
            pageGroup.interactable=false;backdropGroup.interactable=false;
            page.anchoredPosition=backdrop.anchoredPosition=Vector2.zero;
            if(!Application.isPlaying||!isActiveAndEnabled){FinishOpen();return;}
            pageGroup.alpha=backdropGroup.alpha=0;IsTransitioning=true;
            operation=StartCoroutine(Opening());
        }
        IEnumerator Opening()
        {
            yield return Tween(OpenDuration,t=>
            {
                page.localScale=backdrop.localScale=Vector3.one*Mathf.Lerp(1.3f,1,t);
                pageGroup.alpha=backdropGroup.alpha=Mathf.Lerp(50/255f,1,t);
            });
            FinishOpen();
        }
        void FinishOpen()
        {
            page.localScale=backdrop.localScale=Vector3.one;
            pageGroup.alpha=backdropGroup.alpha=1;pageGroup.interactable=true;
            IsTransitioning=false;operation=null;
        }
        public void Close()
        {
            if(!IsOpen)return;
            if(operation!=null)StopCoroutine(operation);
            if(!Application.isPlaying||!isActiveAndEnabled){ResetImmediately();return;}
            IsTransitioning=true;pageGroup.interactable=false;
            operation=StartCoroutine(Closing());
        }
        IEnumerator Closing()
        {
            var scale=page.localScale;float alpha=pageGroup.alpha;
            yield return Tween(CloseDuration,t=>
            {
                page.localScale=backdrop.localScale=Vector3.Lerp(scale,Vector3.one*.02f,t);
                pageGroup.alpha=backdropGroup.alpha=Mathf.Lerp(alpha,0,t);
                page.anchoredPosition=Vector2.Lerp(Vector2.zero,pageTarget,t);
                backdrop.anchoredPosition=Vector2.Lerp(Vector2.zero,backdropTarget,t);
            });
            operation=null;ResetImmediately();
        }
        public void ResetImmediately()
        {
            if(operation!=null){StopCoroutine(operation);operation=null;}
            bool hadState=IsOpen||IsTransitioning;
            if(page!=null){page.gameObject.SetActive(false);page.localScale=Vector3.one;page.anchoredPosition=Vector2.zero;}
            if(backdrop!=null){backdrop.gameObject.SetActive(false);backdrop.localScale=Vector3.one;backdrop.anchoredPosition=Vector2.zero;}
            if(hud!=null)hud.SetActive(true);
            if(viewport!=null&&hadState)
            {
                var image=viewport.GetComponent<RawImage>();if(image!=null)image.raycastTarget=originalRaycast;
            }
            IsOpen=IsTransitioning=false;
        }
        void CacheCollapsePoint(Vector3 center)
        {
            var uv=viewport.ViewCamera.WorldToViewportPoint(center);
            var region=(RectTransform)viewport.transform;
            var world=region.TransformPoint(new Vector3(Mathf.Lerp(region.rect.xMin,region.rect.xMax,uv.x),Mathf.Lerp(region.rect.yMin,region.rect.yMax,uv.y),0));
            var canvas=region.GetComponentInParent<Canvas>();var camera=canvas!=null&&canvas.renderMode!=RenderMode.ScreenSpaceOverlay?canvas.worldCamera:null;
            var screen=RectTransformUtility.WorldToScreenPoint(camera,world);
            RectTransformUtility.ScreenPointToLocalPointInRectangle((RectTransform)page.parent,screen,camera,out pageTarget);
            RectTransformUtility.ScreenPointToLocalPointInRectangle((RectTransform)backdrop.parent,screen,camera,out backdropTarget);
        }
        static IEnumerator Tween(float seconds,System.Action<float> apply)
        {
            float start=Time.unscaledTime;
            while(Time.unscaledTime-start<seconds)
            {float t=Mathf.Clamp01((Time.unscaledTime-start)/seconds);apply(t*t*(3-2*t));yield return null;}
            apply(1);
        }
        void OnDisable()=>ResetImmediately();
        void OnDestroy()=>ResetImmediately();
    }
}
