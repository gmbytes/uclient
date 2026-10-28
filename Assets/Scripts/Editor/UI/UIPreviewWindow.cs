using System;
using System.IO;
using Client.Framework.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object=UnityEngine.Object;
namespace Client.Editor.UI
{
    /// <summary>Isolated editor scene. Only the production layout/keyboard policies consume preview seams.</summary>
    public sealed class UIPreviewSession : IDisposable
    {
        readonly Scene scene;readonly Camera camera;readonly ScreenLayoutComponent source;readonly SafeAreaRoot safeArea;
        readonly UIViewBridge bridge;int frame;ScreenMetrics metrics;
        bool keyboardVisible;Rect keyboardArea;RectTransform focus;
        public GameObject Instance {get;}
        public AdaptiveCanvas Canvas {get;}
        public RenderTexture Texture {get;private set;}
        public UIPreviewSession(UIViewDefinition definition)
        {
            if(definition==null)throw new ArgumentNullException(nameof(definition));
            var validation=UIAuthoringValidator.Validate(definition);
            if(!validation.IsValid)throw new InvalidOperationException("UI_PREVIEW_VALIDATION: "+validation);
            UIWindowCreator.RequireSavedEditingScenes();
            var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(definition.PrefabAddress);
            if(prefab==null)throw new InvalidOperationException("UI_PREVIEW: prefab missing.");
            if(SceneManager.GetSceneByPath(UIWindowCreator.PreviewScenePath).isLoaded)throw new InvalidOperationException("UI_PREVIEW: UIEdit is already open; close it normally before starting an owned preview session.");
            scene=EditorSceneManager.OpenScene(UIWindowCreator.PreviewScenePath,OpenSceneMode.Additive);
            try
            {
                var global=(GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(AdaptiveUIAssetBuilder.RootPath),scene);
                Canvas=global.GetComponent<AdaptiveCanvas>();source=global.GetComponent<ScreenLayoutComponent>();safeArea=global.GetComponentInChildren<SafeAreaRoot>(true);
                Instance=(GameObject)PrefabUtility.InstantiatePrefab(prefab,scene);Instance.transform.SetParent(global.transform.Find("Windows"),false);
                bridge=Instance.GetComponent<UIViewBridge>();
                Canvas.Configure(safeArea,null,null);source.MetricsSource=()=>metrics;
                if(bridge.Keyboard!=null)
                {
                    bridge.Keyboard.KeyboardVisibleSource=()=>keyboardVisible;bridge.Keyboard.KeyboardAreaSource=()=>keyboardArea;bridge.Keyboard.FocusSource=()=>focus;
                }
                var cameraObject=new GameObject("UI Preview Camera",typeof(Camera));SceneManager.MoveGameObjectToScene(cameraObject,scene);
                camera=cameraObject.GetComponent<Camera>();camera.orthographic=true;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.02f,.025f,.04f);camera.transform.position=new Vector3(0,0,-10);
                var rootCanvas=global.GetComponent<UnityEngine.Canvas>();rootCanvas.renderMode=RenderMode.ScreenSpaceCamera;rootCanvas.worldCamera=camera;rootCanvas.planeDistance=1;
                if(source.Service.Current!=null){Canvas.Apply(source.Service.Current);bridge.Apply(source.Service.Current);}
            }
            catch{EditorSceneManager.CloseScene(scene,true);throw;}
        }
        public void Apply(ScreenMetrics value,bool visible,Rect area,RectTransform focused)
        {
            if(value.Width<=0 || value.Height<=0)throw new ArgumentOutOfRangeException(nameof(value));
            metrics=value;keyboardVisible=visible;keyboardArea=area;focus=focused;
            bool graphics=SystemInfo.graphicsDeviceType!=UnityEngine.Rendering.GraphicsDeviceType.Null;
            bool resized=false;
            if(graphics && (Texture==null || Texture.width!=value.Width || Texture.height!=value.Height))
            {
                if(Texture!=null){camera.targetTexture=null;Texture.Release();Object.DestroyImmediate(Texture);}
                Texture=new RenderTexture(value.Width,value.Height,24){name="UIEdit Preview",hideFlags=HideFlags.HideAndDontSave};Texture.Create();camera.targetTexture=Texture;resized=true;
            }
            // A changed render target updates Canvas pixel dimensions at its render boundary.
            // Settle that Unity-owned geometry before the single per-window layout application.
            if(resized){UnityEngine.Canvas.ForceUpdateCanvases();camera.Render();}
            source.Service.Evaluate(source.MetricsSource());source.Service.FlushPending(++frame);
            // EditMode has no player Update; execute the existing scaler component's update policy.
            typeof(CanvasScaler).GetMethod("Handle",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).Invoke(Canvas.GetComponent<CanvasScaler>(),null);
            UnityEngine.Canvas.ForceUpdateCanvases();
            if(source.Service.Current!=null){Canvas.Apply(source.Service.Current);bridge.Apply(source.Service.Current);}
            if(bridge.Keyboard!=null)bridge.Keyboard.Apply(source.Service.Current,bridge.Keyboard.KeyboardAreaSource(),bridge.Keyboard.FocusSource());
            UnityEngine.Canvas.ForceUpdateCanvases();if(graphics)camera.Render();
        }
        public void Dispose()
        {
            if(camera!=null)camera.targetTexture=null;
            if(Texture!=null){Texture.Release();Object.DestroyImmediate(Texture);Texture=null;}
            if(scene.IsValid())EditorSceneManager.CloseScene(scene,true);
        }
    }
    public sealed class UIPreviewWindow : EditorWindow
    {
        UIPreviewSession session;UIWindowConfig config;int width=720,height=1280;Rect safe=new Rect(0,0,720,1280);bool keyboard;Rect area;RectTransform focus;
        [MenuItem("Game/UI/Preview")]static void Open()=>GetWindow<UIPreviewWindow>("UI Preview");
        void OnDisable(){session?.Dispose();session=null;}
        void OnGUI()
        {
            config=(UIWindowConfig)EditorGUILayout.ObjectField("Window config",config??Selection.activeObject as UIWindowConfig,typeof(UIWindowConfig),false);
            EditorGUILayout.HelpBox("UIEdit uses an isolated preview scene. Save edited scenes and Prefab Stage normally before opening. No boot, resource or network flow runs.",MessageType.Info);
            using(new EditorGUILayout.HorizontalScope())
            {
                if(GUILayout.Button("Portrait")){width=720;height=1280;safe=new Rect(0,40,720,1200);Apply();}
                if(GUILayout.Button("Landscape")){width=1280;height=720;safe=new Rect(40,0,1200,720);Apply();}
            }
            width=EditorGUILayout.IntField("Width",width);height=EditorGUILayout.IntField("Height",height);safe=EditorGUILayout.RectField("Safe area (pixels)",safe);
            keyboard=EditorGUILayout.Toggle("Keyboard visible",keyboard);area=EditorGUILayout.RectField("Measured area (zero=unknown)",area);focus=(RectTransform)EditorGUILayout.ObjectField("Focus",focus,typeof(RectTransform),true);
            if(GUILayout.Button("Open / reload selected UIEdit preview"))
            {
                if(HasDirtyEditingContent()){EditorUtility.DisplayDialog("Save editing content","Save all edited scenes and Prefab Stage through the normal editor save action first.","OK");return;}
                try{session?.Dispose();session=null;session=new UIPreviewSession(config.Resolve());Apply();}catch(Exception e){Debug.LogError(e.Message);}
            }
            if(GUILayout.Button("Apply preview metrics"))Apply();
            if(session?.Texture!=null)
            {
                var rect=GUILayoutUtility.GetRect(100,10000,100,10000);GUI.DrawTexture(rect,session.Texture,ScaleMode.ScaleToFit,false);
            }
        }
        static bool HasDirtyEditingContent()
        {
            for(int i=0;i<SceneManager.sceneCount;i++)if(SceneManager.GetSceneAt(i).isDirty)return true;
            var stage=PrefabStageUtility.GetCurrentPrefabStage();return stage!=null && stage.scene.isDirty;
        }
        void Apply(){if(session!=null && width>0 && height>0){session.Apply(new ScreenMetrics(width,height,safe),keyboard,area,focus);Repaint();}}
    }
}
