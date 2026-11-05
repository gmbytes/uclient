using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.Rendering;

namespace Client.Framework.Presentation
{
    /// <summary>Owns only the selected building and its transparent thumbnail; never changes the city camera.</summary>
    [DisallowMultipleComponent,RequireComponent(typeof(RawImage))]
    public sealed class BuildingPreviewViewport:MonoBehaviour
    {
        const int PreviewLayer=30;
        [SerializeField] GameObject cityPrefab;
        [SerializeField] Shader renderShader,resolveShader;
        [SerializeField] RectTransform details;
        readonly List<Material> materials=new List<Material>();
        Scene scene;
        GameObject world;
        RawImage output;
        RenderTexture raw,resolved;
        Material resolve;
        CommandBuffer draw;
        Bounds bounds;
        bool dirty,hasCrop;
        float visibleAspect;
        public int BuildingId{get;private set;}
        public GameObject ModelRoot{get;private set;}
        public Camera PreviewCamera{get;private set;}
        public bool IsReady=>ModelRoot!=null&&PreviewCamera!=null&&resolved!=null;
        public Texture Texture=>resolved;

        public void Configure(GameObject prefab,Shader renderer,Shader alphaResolve,RectTransform information)
        {cityPrefab=prefab;renderShader=renderer;resolveShader=alphaResolve;details=information;}
        public void Show(int id)
        {
            if(id<1)throw new ArgumentOutOfRangeException(nameof(id));
            if(BuildingId==id&&ModelRoot!=null){Layout();dirty=true;RenderNow();return;}
            Release();BuildingId=id;
            if(!Application.isPlaying||isActiveAndEnabled)Build();
        }
        Transform Template()
        {
            if(cityPrefab==null)return null;
            foreach(var child in cityPrefab.GetComponentsInChildren<Transform>(true))if(child.name=="buildParent"+BuildingId)return child;
            return null;
        }
        void OnEnable(){if(BuildingId>0)Build();}
        void Build()
        {
            if(ModelRoot!=null)return;
            var template=Template();if(template==null)throw new InvalidOperationException("No building preview for ID "+BuildingId);
            if(renderShader==null||resolveShader==null)throw new InvalidOperationException("Building preview shaders must be assigned.");
            try
            {
#if UNITY_EDITOR
                scene=Application.isPlaying?SceneManager.CreateScene("BuildingPreview."+GetInstanceID()):UnityEditor.SceneManagement.EditorSceneManager.NewPreviewScene();
#else
                scene=SceneManager.CreateScene("BuildingPreview."+GetInstanceID());
#endif
                world=new GameObject("BuildingPreview.World"){hideFlags=HideFlags.DontSave};SceneManager.MoveGameObjectToScene(world,scene);
                world.transform.position=new Vector3(0,-30000,0);
                ModelRoot=Instantiate(template.gameObject,world.transform,false);ModelRoot.name="BuildingPreview.Model."+BuildingId;ModelRoot.SetActive(true);
                // Keep the authored BuildingRoot's 180-degree orientation and unit conversion.
                ModelRoot.transform.localPosition=template.position;
                ModelRoot.transform.localRotation=template.rotation;
                ModelRoot.transform.localScale=template.lossyScale;
                foreach(var item in ModelRoot.GetComponentsInChildren<Transform>(true))item.gameObject.layer=PreviewLayer;
                var variants=new Dictionary<Material,Material>();bool found=false;
                foreach(var renderer in ModelRoot.GetComponentsInChildren<Renderer>())
                {
                    if(!renderer.enabled)continue;
                    if(!found){bounds=renderer.bounds;found=true;}else bounds.Encapsulate(renderer.bounds);
                    var source=renderer.sharedMaterials;var replacements=new Material[source.Length];
                    for(int i=0;i<source.Length;i++)
                    {
                        if(source[i]==null)continue;
                        if(!variants.TryGetValue(source[i],out var variant))
                        {
                            variant=new Material(renderShader){name="BuildingPreview.Material",hideFlags=HideFlags.DontSave,renderQueue=source[i].renderQueue};
                            if(source[i].HasProperty("_MainTex")){variant.mainTexture=source[i].mainTexture;variant.mainTextureScale=source[i].mainTextureScale;variant.mainTextureOffset=source[i].mainTextureOffset;}
                            if(source[i].HasProperty("_Color"))variant.color=source[i].color;
                            variants.Add(source[i],variant);materials.Add(variant);
                        }
                        replacements[i]=variant;
                    }
                    renderer.sharedMaterials=replacements;
                }
                if(!found)throw new InvalidOperationException("Selected building has no visible renderer.");
                var cameraObject=new GameObject("BuildingPreview.Camera");cameraObject.transform.SetParent(world.transform,false);
                PreviewCamera=cameraObject.AddComponent<Camera>();PreviewCamera.enabled=false;PreviewCamera.orthographic=true;
#if UNITY_EDITOR
                PreviewCamera.scene=scene;
#endif
                PreviewCamera.cullingMask=0;PreviewCamera.clearFlags=CameraClearFlags.SolidColor;PreviewCamera.backgroundColor=Color.clear;
                PreviewCamera.allowHDR=false;PreviewCamera.allowMSAA=false;PreviewCamera.transform.rotation=Quaternion.Euler(90,0,0);
                float distance=Mathf.Max(100,bounds.size.magnitude*2);PreviewCamera.nearClipPlane=.1f;PreviewCamera.farClipPlane=distance*4;
                PreviewCamera.transform.position=bounds.center+Vector3.up*distance;
                draw=new CommandBuffer{name="Selected building only"};
                var renderers=ModelRoot.GetComponentsInChildren<Renderer>();
                Array.Sort(renderers,(a,b)=>
                {
                    int queueA=a.sharedMaterial!=null?a.sharedMaterial.renderQueue:3000,queueB=b.sharedMaterial!=null?b.sharedMaterial.renderQueue:3000;
                    int queue=queueA.CompareTo(queueB);return queue!=0?queue:a.bounds.center.y.CompareTo(b.bounds.center.y);
                });
                foreach(var renderer in renderers)
                {
                    if(!renderer.enabled)continue;
                    var entries=renderer.sharedMaterials;
                    for(int i=0;i<entries.Length;i++)if(entries[i]!=null)draw.DrawRenderer(renderer,entries[i],i);
                }
                PreviewCamera.AddCommandBuffer(CameraEvent.AfterForwardAlpha,draw);
                resolve=new Material(resolveShader){hideFlags=HideFlags.DontSave};output=GetComponent<RawImage>();output.raycastTarget=false;
                Layout();Resize();dirty=true;RenderNow();
            }
            catch{Release();throw;}
        }
        void Layout()
        {
            var rect=(RectTransform)transform;var parent=(RectTransform)rect.parent;
            float width=parent.rect.width,height=parent.rect.height;if(width<=0||height<=0)return;
            float top=height*(BuildingId==1?.21f:.172f)+24;
            float footer=48;
            float minimumDetails=Mathf.Min(530,height*.34f);
            float maximum=Mathf.Max(120,Mathf.Min(height*.35f,height-top-footer-minimumDetails-24));
            float aspect=hasCrop?visibleAspect:Mathf.Max(.1f,bounds.size.x/Mathf.Max(.01f,bounds.size.z));
            float previewWidth=width*.86f,previewHeight=Mathf.Clamp(previewWidth/aspect,120,maximum);
            previewWidth=Mathf.Min(previewWidth,previewHeight*aspect);
            rect.anchorMin=rect.anchorMax=new Vector2(.5f,1);rect.pivot=new Vector2(.5f,1);
            rect.sizeDelta=new Vector2(previewWidth,previewHeight);rect.anchoredPosition=new Vector2(0,-top);
            if(details!=null)
            {
                float detailsTop=top+previewHeight+24;
                details.anchorMin=new Vector2(0,1);details.anchorMax=Vector2.one;details.pivot=new Vector2(.5f,1);
                details.sizeDelta=new Vector2(0,Mathf.Max(300,height-detailsTop-footer));details.anchoredPosition=new Vector2(0,-detailsTop);
            }
        }
        void Resize()
        {
            var rect=(RectTransform)transform;var canvas=GetComponentInParent<Canvas>();float scale=canvas!=null?canvas.rootCanvas.scaleFactor:1;
            var parent=(RectTransform)rect.parent;
            int w=Mathf.Clamp(Mathf.NextPowerOfTwo(Mathf.RoundToInt(parent.rect.width*.86f*scale)),256,1024),h=w;
            if(raw!=null&&raw.width==w&&raw.height==h)return;
            if(PreviewCamera!=null)PreviewCamera.targetTexture=null;FreeTexture(raw);FreeTexture(resolved);
            raw=new RenderTexture(w,h,24,RenderTextureFormat.ARGB32){name="BuildingPreview.Raw",hideFlags=HideFlags.DontSave};raw.Create();
            resolved=new RenderTexture(w,h,0,RenderTextureFormat.ARGB32){name="BuildingPreview.StraightAlpha",hideFlags=HideFlags.DontSave};resolved.Create();
            PreviewCamera.targetTexture=raw;PreviewCamera.aspect=1;
            PreviewCamera.orthographicSize=Mathf.Max(.01f,bounds.extents.z,bounds.extents.x)*1.04f;
            output.texture=resolved;output.uvRect=new Rect(0,0,1,1);hasCrop=false;dirty=true;
        }
        public void RenderNow()
        {
            if(ModelRoot==null)return;Layout();Resize();PreviewCamera.Render();Graphics.Blit(raw,resolved,resolve);
            if(!hasCrop)CropToVisibleBuilding();Layout();dirty=false;
        }
        void CropToVisibleBuilding()
        {
            var previous=RenderTexture.active;RenderTexture.active=resolved;
            var image=new Texture2D(resolved.width,resolved.height,TextureFormat.RGBA32,false);
            try
            {
                image.ReadPixels(new Rect(0,0,image.width,image.height),0,0);image.Apply();var pixels=image.GetPixels32();
                int minX=image.width,minY=image.height,maxX=-1,maxY=-1;
                for(int y=0;y<image.height;y++)for(int x=0;x<image.width;x++)
                    if(pixels[y*image.width+x].a>20){minX=Mathf.Min(minX,x);maxX=Mathf.Max(maxX,x);minY=Mathf.Min(minY,y);maxY=Mathf.Max(maxY,y);}
                if(maxX<minX)return;
                int padding=Mathf.Max(2,Mathf.RoundToInt(Mathf.Max(maxX-minX,maxY-minY)*.015f));
                minX=Mathf.Max(0,minX-padding);maxX=Mathf.Min(image.width-1,maxX+padding);
                minY=Mathf.Max(0,minY-padding);maxY=Mathf.Min(image.height-1,maxY+padding);
                output.uvRect=new Rect((float)minX/image.width,(float)minY/image.height,(float)(maxX-minX+1)/image.width,(float)(maxY-minY+1)/image.height);
                visibleAspect=(float)(maxX-minX+1)/(maxY-minY+1);hasCrop=true;
            }
            finally{RenderTexture.active=previous;DestroyOwned(image);}
        }
        void LateUpdate(){if(ModelRoot==null)return;Layout();Resize();if(dirty)RenderNow();}
        void OnRectTransformDimensionsChange(){dirty=true;}
        void OnDisable()=>Release();
        void OnDestroy()=>Release();
        void Release()
        {
            if(output!=null)output.texture=null;
            if(PreviewCamera!=null){PreviewCamera.enabled=false;PreviewCamera.targetTexture=null;}
            if(draw!=null){if(PreviewCamera!=null)PreviewCamera.RemoveCommandBuffer(CameraEvent.AfterForwardAlpha,draw);draw.Release();draw=null;}
            FreeTexture(raw);FreeTexture(resolved);raw=resolved=null;
            foreach(var material in materials)DestroyOwned(material);materials.Clear();DestroyOwned(resolve);resolve=null;
            if(world!=null){world.SetActive(false);DestroyOwned(world);}world=null;ModelRoot=null;PreviewCamera=null;
            if(scene.IsValid()&&scene.isLoaded)
            {
#if UNITY_EDITOR
                if(!Application.isPlaying)UnityEditor.SceneManagement.EditorSceneManager.ClosePreviewScene(scene);else SceneManager.UnloadSceneAsync(scene);
#else
                SceneManager.UnloadSceneAsync(scene);
#endif
            }
            scene=default;output=null;dirty=hasCrop=false;visibleAspect=0;
        }
        static void FreeTexture(RenderTexture value){if(value!=null){value.Release();DestroyOwned(value);}}
        static void DestroyOwned(UnityEngine.Object value){if(value==null)return;if(Application.isPlaying)Destroy(value);else DestroyImmediate(value);}
    }
}
