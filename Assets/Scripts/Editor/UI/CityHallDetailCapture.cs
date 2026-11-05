using System;
using System.IO;
using System.Reflection;
using Client.Contracts;
using Client.Framework.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Client.Editor.UI
{
    public static class CityHallDetailCapture
    {
        [InitializeOnLoadMethod]
        static void WatchRequests()
        {
            EditorApplication.update-=Poll;
            EditorApplication.update+=Poll;
        }
        static void Poll()
        {
            const string request="Builds/CityHallDetails/capture.request";
            if(!File.Exists(request)||EditorApplication.isCompiling||EditorApplication.isUpdating||EditorApplication.isPlayingOrWillChangePlaymode)return;
            File.Delete(request);
            try{BuildAndCapture();File.WriteAllText("Builds/CityHallDetails/capture-result.txt","OK");}
            catch(Exception error){File.WriteAllText("Builds/CityHallDetails/capture-result.txt",error.ToString());Debug.LogException(error);}
        }
        sealed class View:IViewHandle
        {public GameObject Root{get;set;}public void Close(){}}
        [MenuItem("Game/MainCity/Build and Capture City Hall Details")]
        public static void BuildAndCapture(){MainCityUIBuilder.Build();Capture();}
        public static void Capture()
        {
            var definition=AssetDatabase.LoadAssetAtPath<UIWindowConfig>("Assets/Content/UI/MainCity/UIMainCity.Config.asset").Resolve();
            Directory.CreateDirectory("Builds/CityHallDetails");
            var type=Type.GetType("Client.Game.MainCity.UIMainCity, Client.Game")??throw new InvalidOperationException("Missing game presenter.");
            foreach(var size in new[]{new Vector2Int(1080,1920),new Vector2Int(720,1280),new Vector2Int(1080,2400),new Vector2Int(1080,2340)})
            {
                using(var preview=new OwnedPreview(definition))
                {
                    object presenter=Activator.CreateInstance(type);
                    try
                    {
                        type.GetMethod("Bind").Invoke(presenter,new object[]{new View{Root=preview.Instance},(Action)(()=>{})});
                        var safe=size.y==2340?new Rect(0,96,size.x,size.y-240):size.y==2400?new Rect(0,72,size.x,size.y-132):new Rect(0,0,size.x,size.y);
                        preview.Apply(new ScreenMetrics(size.x,size.y,safe),false,Rect.zero,null);
                        preview.BuildMap();preview.RenderMap();
                        preview.Apply(new ScreenMetrics(size.x,size.y,safe),false,Rect.zero,null);
                        foreach(var text in preview.Instance.GetComponentsInChildren<Text>())
                            if(text.gameObject.activeInHierarchy&&!string.IsNullOrWhiteSpace(text.text)&&text.cachedTextGenerator.characterCountVisible==0)
                                throw new InvalidOperationException("Visible MainUI text has no rendered characters: "+text.name);
                        Save(preview.Texture,$"Builds/CityHallDetails/main-city-{size.x}x{size.y}.png");
                        if(size.x==1080&&size.y==1920)
                        {
                            var hudBindings=preview.Instance.GetComponent<UIBindings>();
                            ((Button)hudBindings.Resolve("MainUIFunctions")).onClick.Invoke();
                            preview.Apply(new ScreenMetrics(size.x,size.y,safe),false,Rect.zero,null);
                            Save(preview.Texture,"Builds/CityHallDetails/main-ui-functions-1080x1920.png");
                            ((Button)hudBindings.Resolve("MainUIPlayerButton")).onClick.Invoke();
                            preview.Apply(new ScreenMetrics(size.x,size.y,safe),false,Rect.zero,null);
                            Save(preview.Texture,"Builds/CityHallDetails/main-ui-player-1080x1920.png");
                            ((Button)hudBindings.Resolve("MainUIDialogClose")).onClick.Invoke();
                        }
                        type.GetMethod("Selected",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(presenter,new object[]{1,"城主府"});
                        preview.Apply(new ScreenMetrics(size.x,size.y,safe),false,Rect.zero,null);
                        foreach(var text in preview.Instance.GetComponentsInChildren<Text>())
                            if(text.gameObject.activeInHierarchy&&!string.IsNullOrWhiteSpace(text.text)&&text.cachedTextGenerator.characterCountVisible==0)
                                throw new InvalidOperationException("Visible city-detail text has no rendered characters: "+text.name);
                        Save(preview.Texture,$"Builds/CityHallDetails/details-{size.x}x{size.y}.png");
                        if(size.x==1080&&size.y==1920)
                        {
                            var bindings=preview.Instance.GetComponent<UIBindings>();
                            ((Button)bindings.Resolve("AllianceButton")).onClick.Invoke();
                            preview.Apply(new ScreenMetrics(size.x,size.y,safe),false,Rect.zero,null);
                            Save(preview.Texture,"Builds/CityHallDetails/alliance-1080x1920.png");
                            ((Button)bindings.Resolve("DismissCityDialog")).onClick.Invoke();
                            ((Button)bindings.Resolve("TerritoryButton")).onClick.Invoke();
                            preview.Apply(new ScreenMetrics(size.x,size.y,safe),false,Rect.zero,null);
                            Save(preview.Texture,"Builds/CityHallDetails/territory-1080x1920.png");
                            ((Button)bindings.Resolve("DismissCityDialog")).onClick.Invoke();
                            ((Button)bindings.Resolve("CloseCityDetails")).onClick.Invoke();
                            type.GetMethod("Selected",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(presenter,new object[]{2,"仓库"});
                            preview.Apply(new ScreenMetrics(size.x,size.y,safe),false,Rect.zero,null);
                            Save(preview.Texture,"Builds/CityHallDetails/warehouse-1080x1920.png");
                            ((Button)bindings.Resolve("CloseCityDetails")).onClick.Invoke();
                            type.GetMethod("Selected",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(presenter,new object[]{3,"兵营"});
                            preview.Apply(new ScreenMetrics(size.x,size.y,safe),false,Rect.zero,null);
                            Save(preview.Texture,"Builds/CityHallDetails/barracks-1080x1920.png");
                        }
                    }
                    finally{type.GetMethod("Unbind").Invoke(presenter,null);}
                }
            }
            Debug.Log("CITY_HALL_DETAIL_CAPTURED");
        }
        sealed class OwnedPreview:IDisposable
        {
            readonly Scene scene;
            readonly Camera camera;
            readonly AdaptiveCanvas canvas;
            readonly ScreenLayoutComponent source;
            readonly UIViewBridge bridge;
            int frame;
            Scene mapScene;
            GameObject mapRoot;
            Camera mapCamera;
            RenderTexture mapTexture;
            Bounds mapBounds;
            public GameObject Instance{get;}
            public RenderTexture Texture{get;private set;}
            public OwnedPreview(UIViewDefinition definition)
            {
                var validation=UIAuthoringValidator.Validate(definition);
                if(!validation.IsValid)throw new InvalidOperationException(validation.ToString());
                scene=EditorSceneManager.NewPreviewScene();
                try
                {
                    var global=(GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(AdaptiveUIAssetBuilder.RootPath),scene);
                    canvas=global.GetComponent<AdaptiveCanvas>();source=global.GetComponent<ScreenLayoutComponent>();source.enabled=false;
                    canvas.Configure(global.GetComponentInChildren<SafeAreaRoot>(true),null,null);
                    Instance=(GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(definition.PrefabAddress),scene);
                    Instance.transform.SetParent(global.transform.Find("Windows"),false);bridge=Instance.GetComponent<UIViewBridge>();
                    var obj=new GameObject("City Detail Preview Camera",typeof(Camera));SceneManager.MoveGameObjectToScene(obj,scene);
                    camera=obj.GetComponent<Camera>();camera.orthographic=true;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=Color.black;camera.transform.position=new Vector3(0,0,-10);camera.scene=scene;
                    var c=global.GetComponent<Canvas>();c.renderMode=RenderMode.ScreenSpaceCamera;c.worldCamera=camera;c.planeDistance=1;
                }
                catch{EditorSceneManager.ClosePreviewScene(scene);throw;}
            }
            public void Apply(ScreenMetrics metrics,bool keyboard,Rect area,RectTransform focus)
            {
                if(Texture==null)
                {Texture=new RenderTexture(metrics.Width,metrics.Height,24);Texture.Create();camera.targetTexture=Texture;}
                Canvas.ForceUpdateCanvases();camera.Render();
                source.Service.Evaluate(metrics);source.Service.FlushPending(++frame);
                typeof(CanvasScaler).GetMethod("Handle",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(canvas.GetComponent<CanvasScaler>(),null);
                canvas.Apply(source.Service.Current);bridge.Apply(source.Service.Current);Canvas.ForceUpdateCanvases();
                foreach(var building in Instance.GetComponentsInChildren<Client.Framework.Presentation.BuildingPreviewViewport>())building.RenderNow();
                Canvas.ForceUpdateCanvases();RenderMap();camera.Render();
            }
            public void BuildMap()
            {
                if(mapCamera!=null)return;
                mapScene=EditorSceneManager.NewPreviewScene();
                mapRoot=(GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Content/Prefabs/MainCity/MainCity.prefab"),mapScene);
                foreach(var child in mapRoot.GetComponentsInChildren<Transform>(true))child.gameObject.layer=31;
                var framing=mapRoot.transform.Find("BuildingRoot")??mapRoot.transform;
                mapBounds=BoundsOf(framing);
                var obj=new GameObject("Owned City Preview Camera",typeof(Camera));SceneManager.MoveGameObjectToScene(obj,mapScene);
                mapCamera=obj.GetComponent<Camera>();mapCamera.scene=mapScene;mapCamera.orthographic=true;mapCamera.cullingMask=1<<31;
                mapCamera.transform.rotation=Quaternion.Euler(90,0,0);mapCamera.clearFlags=CameraClearFlags.SolidColor;mapCamera.backgroundColor=new Color(.07f,.11f,.12f);
                float distance=Mathf.Max(mapBounds.size.magnitude*2,100);mapCamera.nearClipPlane=.1f;mapCamera.farClipPlane=distance*4;
                mapTexture=new RenderTexture(Texture.width,Texture.height,24);mapTexture.Create();mapCamera.targetTexture=mapTexture;mapCamera.aspect=(float)Texture.width/Texture.height;
                float size=Mathf.Min(mapBounds.extents.z,mapBounds.extents.x/mapCamera.aspect)*.96f*.9f;
                mapCamera.orthographicSize=size;mapCamera.transform.position=mapBounds.center+Vector3.up*distance;
                ((Client.Framework.Presentation.CityViewport)Instance.GetComponent<UIBindings>().Resolve("Map")).GetComponent<RawImage>().texture=mapTexture;
            }
            public void RenderMap()
            {if(mapCamera!=null)mapCamera.Render();}
            static Bounds BoundsOf(Transform transform)
            {bool found=false;var bounds=new Bounds(transform.position,Vector3.one);foreach(var r in transform.GetComponentsInChildren<Renderer>()){if(!r.enabled)continue;if(!found){bounds=r.bounds;found=true;}else bounds.Encapsulate(r.bounds);}return bounds;}
            public void Dispose()
            {if(camera!=null)camera.targetTexture=null;if(mapCamera!=null)mapCamera.targetTexture=null;if(Texture!=null){Texture.Release();UnityEngine.Object.DestroyImmediate(Texture);}if(mapTexture!=null){mapTexture.Release();UnityEngine.Object.DestroyImmediate(mapTexture);}if(scene.IsValid())EditorSceneManager.ClosePreviewScene(scene);if(mapScene.IsValid())EditorSceneManager.ClosePreviewScene(mapScene);}
        }
        static void Save(RenderTexture texture,string path)
        {
            if(texture==null)throw new InvalidOperationException("Graphics rendering is required for city detail capture.");
            var previous=RenderTexture.active;RenderTexture.active=texture;
            var image=new Texture2D(texture.width,texture.height,TextureFormat.RGB24,false);
            try{image.ReadPixels(new Rect(0,0,texture.width,texture.height),0,0);image.Apply();File.WriteAllBytes(path,image.EncodeToPNG());}
            finally{RenderTexture.active=previous;UnityEngine.Object.DestroyImmediate(image);}
        }
    }
}
