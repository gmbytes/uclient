using System;
using System.IO;
using System.Linq;
using Client.Framework.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Object=UnityEngine.Object;
namespace Client.Editor.UI
{
    public sealed class UIWindowCreator : EditorWindow
    {
        public const string TemplatePath="Assets/Content/UI/Common/UIWindowTemplate.prefab";
        public const string PreviewScenePath="Assets/Scenes/UIEdit.unity";
        string windowName="UILogin",directory="Assets/Content/UI/Login";
        [MenuItem("Game/UI/Create Window")] static void Open()=>GetWindow<UIWindowCreator>("Create UI Window");
        void OnGUI()
        {
            windowName=EditorGUILayout.TextField("UI Name",windowName);directory=EditorGUILayout.TextField("Content directory",directory);
            if(GUILayout.Button("Create and register"))try{var d=Create(windowName,directory);Selection.activeObject=AssetDatabase.LoadAssetAtPath<GameObject>(d.PrefabAddress);}catch(Exception e){Debug.LogError(e.Message);}
        }
        public static UIViewDefinition Create(string windowName,string contentDirectory)
        {
            if(!UIBindingGenerator.ValidName(windowName))throw new ArgumentException("UI_NAME: expected UI{Name}, e.g. UILogin.");
            string dir=(contentDirectory??"").Replace('\\','/').TrimEnd('/');
            string full=Path.GetFullPath(dir), assets=Path.GetFullPath("Assets")+Path.DirectorySeparatorChar;
            if(!full.StartsWith(assets,StringComparison.OrdinalIgnoreCase) || Path.GetFileName(dir)!=windowName.Substring(2))throw new ArgumentException("UI_DIRECTORY: content feature directory must match UI name.");
            string codeDir=UIBindingGenerator.CodeDirectory(dir),hand=codeDir+"/"+windowName+".cs",generated=UIBindingGenerator.OutputFor(dir,windowName);
            string[] targets={dir+"/"+windowName+".prefab",dir+"/"+windowName+".Config.asset",dir+"/"+windowName+".Layout.asset",dir+"/"+windowName+".Text.asset",hand,generated};
            var reusedLayout=AssetDatabase.LoadAssetAtPath<AdaptiveLayoutDefinition>(targets[2]);
            if(targets.Where((p,i)=>i!=2).Any(File.Exists) || (File.Exists(targets[2]) && reusedLayout==null))throw new IOException("UI_EXISTS: a target already exists; nothing was overwritten.");
            var catalog=AssetDatabase.LoadAssetAtPath<UIViewDefinitions>(UIBindingValidator.CatalogPath);
            if(catalog==null)throw new InvalidOperationException("UI_CATALOG: create the formal catalog first.");
            if(catalog.Definitions.Any(d=>d!=null && d.ViewID==windowName))throw new InvalidOperationException("UI_DUPLICATE_VIEW: "+windowName);
            if(!File.Exists(TemplatePath))throw new InvalidOperationException("UI_TEMPLATE: use Game/UI/Create Authoring Foundation first.");
            var old=catalog.Definitions.ToArray(); GameObject root=null;
            var workingScene=EditorSceneManager.NewPreviewScene();
            try
            {
                Directory.CreateDirectory(dir);Directory.CreateDirectory(codeDir+"/Generated");AssetDatabase.Refresh();
                var layout=reusedLayout;
                if(layout==null){layout=ScriptableObject.CreateInstance<AdaptiveLayoutDefinition>();AssetDatabase.CreateAsset(layout,targets[2]);}
                AssetDatabase.CreateAsset(ScriptableObject.CreateInstance<UITextDefinition>(),targets[3]);
                root=(GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(TemplatePath),workingScene);
                // Each window owns its template structure; nested public widgets added by authors retain their links.
                PrefabUtility.UnpackPrefabInstance(root,PrefabUnpackMode.OutermostRoot,InteractionMode.AutomatedAction);
                root.name=windowName;ConfigureLayout(root,layout);
                PrefabUtility.SaveAsPrefabAsset(root,targets[0]);
                var definition=new UIViewDefinition{ViewID=windowName,PrefabAddress=targets[0],Group=UIGroups.Page,LayoutDefinition=layout};
                Undo.RecordObject(catalog,"Register UI window");catalog.Configure(old.Concat(new[]{definition}).ToArray());EditorUtility.SetDirty(catalog);
                var config=ScriptableObject.CreateInstance<UIWindowConfig>();config.Catalog=catalog;config.ViewID=windowName;AssetDatabase.CreateAsset(config,targets[1]);
                File.WriteAllText(hand,"namespace Client.Game\n{\n    public sealed partial class "+windowName+"\n    {\n    }\n}\n",new System.Text.UTF8Encoding(false));
                var generation=UIBindingGenerator.Generate(root.GetComponent<UIBindings>(),"Client.Game",windowName,generated);
                if(!generation.Success)throw new InvalidOperationException(generation.Error);
                AssetDatabase.SaveAssetIfDirty(catalog);AssetDatabase.Refresh();return definition;
            }
            catch
            {
                catalog.Configure(old);EditorUtility.SetDirty(catalog);AssetDatabase.SaveAssetIfDirty(catalog);
                foreach(string path in targets){if(path==targets[2] && reusedLayout!=null)continue;AssetDatabase.DeleteAsset(path);if(File.Exists(path))File.Delete(path);}
                throw;
            }
            finally{EditorSceneManager.ClosePreviewScene(workingScene);}
        }
        static RectTransform Child(string name,Transform parent)
        { var go=new GameObject(name,typeof(RectTransform));go.transform.SetParent(parent,false);var r=(RectTransform)go.transform;Stretch(r);return r; }
        static void Stretch(RectTransform rect){rect.anchorMin=Vector2.zero;rect.anchorMax=Vector2.one;rect.offsetMin=rect.offsetMax=Vector2.zero;}
        static void ConfigureLayout(GameObject root,AdaptiveLayoutDefinition definition)
        {
            var node=root.transform.Find("UINode");var safe=(RectTransform)node.Find("SafeArea");
            var viewport=(RectTransform)safe.Find("Dynamic");var content=(RectTransform)viewport.Find("Content");var scroll=viewport.GetComponent<ScrollRect>();
            var layout=safe.GetComponent<AdaptiveLayout>();layout.Configure(definition,(RectTransform)safe.Find("Static"),viewport,scroll,content,null);
            var keyboard=safe.GetComponent<KeyboardVisibilityController>();keyboard.Configure(scroll,null);
            root.GetComponent<UIViewBridge>().Configure(node.GetComponent<Canvas>(),node.GetComponent<CanvasGroup>(),root.GetComponent<UIBindings>(),layout,keyboard,null,safe.GetComponent<SafeAreaRoot>());
        }
        internal static void UpgradeLocalSafeAreaTemplate()
        {
            var root=PrefabUtility.LoadPrefabContents(TemplatePath);
            try{ConfigureLayout(root,root.GetComponent<UIViewBridge>().Layout.Definition);PrefabUtility.SaveAsPrefabAsset(root,TemplatePath);}
            finally{PrefabUtility.UnloadPrefabContents(root);}
        }
        public static void RequireSavedEditingScenes()
        {
            for(int i=0;i<UnityEngine.SceneManagement.SceneManager.sceneCount;i++)
            {
                var scene=UnityEngine.SceneManagement.SceneManager.GetSceneAt(i);
                if(scene.isDirty || string.IsNullOrEmpty(scene.path))throw new InvalidOperationException("UI_SAVE_REQUIRED: save edited scenes normally before authoring UIEdit.");
            }
            var stage=PrefabStageUtility.GetCurrentPrefabStage();
            if(stage!=null && stage.scene.isDirty)throw new InvalidOperationException("UI_SAVE_REQUIRED: save Prefab Stage normally before preview.");
        }
        public static void CreateFoundationBatch()
        {
            if(!Application.isBatchMode)throw new InvalidOperationException("Batch-only foundation entry.");
            var initial=UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            if(UnityEngine.SceneManagement.SceneManager.sceneCount==1 && string.IsNullOrEmpty(initial.path) && !initial.isDirty && initial.rootCount==0)
                EditorSceneManager.OpenScene("Assets/Scenes/Bootstrap.unity",OpenSceneMode.Single);
            CreateFoundation();
        }
        [MenuItem("Game/UI/Create Authoring Foundation")]
        public static void CreateFoundation()
        {
            RequireSavedEditingScenes();
            Directory.CreateDirectory(Path.GetDirectoryName(TemplatePath));Directory.CreateDirectory("Assets/Scenes");
            if(!File.Exists(TemplatePath))
            {
                var root=new GameObject("UIWindowTemplate",typeof(RectTransform),typeof(UIViewBridge),typeof(UIBindings));Stretch((RectTransform)root.transform);
                try
                {
                    var node=Child("UINode",root.transform);node.gameObject.AddComponent<Canvas>();node.gameObject.AddComponent<CanvasGroup>();node.gameObject.AddComponent<GraphicRaycaster>();
                    var background=Child("Background",node).gameObject.AddComponent<Image>();background.color=new Color(.035f,.055f,.09f);background.raycastTarget=false;
                    var safe=Child("SafeArea",node);safe.gameObject.AddComponent<SafeAreaRoot>();safe.gameObject.AddComponent<AdaptiveLayout>();safe.gameObject.AddComponent<KeyboardVisibilityController>();
                    Child("Static",safe);var dynamic=Child("Dynamic",safe);var scroll=dynamic.gameObject.AddComponent<ScrollRect>();dynamic.gameObject.AddComponent<RectMask2D>();var content=Child("Content",dynamic);scroll.viewport=dynamic;scroll.content=content;scroll.horizontal=false;
                    Child("PoolNode",root.transform).gameObject.SetActive(false);ConfigureLayout(root,null);PrefabUtility.SaveAsPrefabAsset(root,TemplatePath);
                }
                finally{Object.DestroyImmediate(root);}
            }
            if(!File.Exists(PreviewScenePath))
            {
                var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Additive);
                try
                {
                    var events=new GameObject("EventSystem",typeof(EventSystem),typeof(StandaloneInputModule));UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(events,scene);
                    EditorSceneManager.SaveScene(scene,PreviewScenePath);
                }
                finally{EditorSceneManager.CloseScene(scene,true);}
            }
            AssetDatabase.Refresh();
        }
    }
}
