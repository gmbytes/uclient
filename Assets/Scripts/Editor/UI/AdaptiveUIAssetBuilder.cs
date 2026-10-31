using System.IO;
using Client.Framework.UI;
using UnityEditor;

using UnityEngine;
using UnityEngine.UI;

namespace Client.Editor.UI
{
    public static class AdaptiveUIAssetBuilder
    {
        public const string RootPath="Assets/Content/UI/Common/AdaptiveUIRoot.prefab";
        public const string LayoutPath="Assets/Content/UI/Login/UILogin.Layout.asset";
        [MenuItem("Game/UI/Generate Adaptive Foundation")]
        public static void Generate()
        {
            Directory.CreateDirectory("Assets/Content/UI/Common"); Directory.CreateDirectory("Assets/Content/UI/Login");
            var definition=AssetDatabase.LoadAssetAtPath<AdaptiveLayoutDefinition>(LayoutPath);
            if(definition==null) { definition=ScriptableObject.CreateInstance<AdaptiveLayoutDefinition>(); AssetDatabase.CreateAsset(definition,LayoutPath); }
            var root=new GameObject("AdaptiveUIRoot",typeof(RectTransform),typeof(Canvas),typeof(CanvasScaler),typeof(GraphicRaycaster),typeof(AdaptiveCanvas));
            root.GetComponent<Canvas>().renderMode=RenderMode.ScreenSpaceOverlay;
            var scaler=root.GetComponent<CanvasScaler>(); scaler.uiScaleMode=CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.screenMatchMode=CanvasScaler.ScreenMatchMode.MatchWidthOrHeight; scaler.matchWidthOrHeight=.5f; scaler.referenceResolution=new Vector2(1080,1920);
            var background=Child("Background",root.transform); Stretch(background);
            var image=background.gameObject.AddComponent<Image>(); image.color=new Color(.035f,.055f,.09f); image.raycastTarget=false;
            var safe=Child("SafeArea",root.transform); Stretch(safe); var safeRoot=safe.gameObject.AddComponent<SafeAreaRoot>();
            var windows=Child("Windows",root.transform); Stretch(windows);
            root.GetComponent<AdaptiveCanvas>().Configure(safeRoot,null,null);
            var prefab=PrefabUtility.SaveAsPrefabAsset(root,RootPath); Object.DestroyImmediate(root);
            // The shared AOT canvas exists under the inactive GameRoot and adds no EventSystem.
            string gameRootPath="Assets/Prefabs/App/GameRoot.prefab";
            var gameRoot=PrefabUtility.LoadPrefabContents(gameRootPath);
            if(gameRoot.GetComponentInChildren<AdaptiveCanvas>(true)==null) PrefabUtility.InstantiatePrefab(prefab,gameRoot.transform);
            PrefabUtility.SaveAsPrefabAsset(gameRoot,gameRootPath); PrefabUtility.UnloadPrefabContents(gameRoot);
            string startupPath="Assets/Prefabs/App/UIUpdate.prefab";
            var startup=PrefabUtility.LoadPrefabContents(startupPath);
            EnsureStartupBackground(startup);
            var startupCanvas=startup.GetComponent<AdaptiveCanvas>()??startup.AddComponent<AdaptiveCanvas>();
            var startupSafe=startup.GetComponentInChildren<SafeAreaRoot>(true);
            if(startupSafe==null)
            {
                var status=startup.GetComponentInChildren<Text>(true);
                var safeRect=Child("SafeArea",startup.transform); Stretch(safeRect); startupSafe=safeRect.gameObject.AddComponent<SafeAreaRoot>();
                if(status!=null) status.transform.SetParent(safeRect,false);
            }
            startupCanvas.Configure(startupSafe,null,null);
            PrefabUtility.SaveAsPrefabAsset(startup,startupPath); PrefabUtility.UnloadPrefabContents(startup);
            AssetDatabase.SaveAssets(); AssetDatabase.Refresh();
            Debug.Log("U2_ASSETS_PASS");
        }
        internal static void EnsureStartupBackground(GameObject startup)
        {
            var child=startup.transform.Find("Background");
            var background=child==null?Child("Background",startup.transform):(RectTransform)child;
            Stretch(background);background.SetAsFirstSibling();
            var image=background.GetComponent<Image>()??background.gameObject.AddComponent<Image>();
            // 启动页没有场景相机，必须由不透明背景覆盖上一帧，状态文字更新时才不会残留旧画面。
            image.color=new Color(.035f,.055f,.09f,1);image.raycastTarget=false;
        }
        [MenuItem("Game/UI/Upgrade Startup Background")]
        public static void UpgradeStartupBackground()
        {
            var startup=PrefabUtility.LoadPrefabContents("Assets/Prefabs/App/UIUpdate.prefab");
            try{EnsureStartupBackground(startup);PrefabUtility.SaveAsPrefabAsset(startup,"Assets/Prefabs/App/UIUpdate.prefab");}
            finally{PrefabUtility.UnloadPrefabContents(startup);}
            AssetDatabase.SaveAssets();Debug.Log("STARTUP_BACKGROUND_UPGRADE_PASS");
        }
        /// <summary>One-time in-place migration; keeps the existing Windows object and nested prefab references.</summary>
        public static void UpgradeFullFrameWindows()
        {
            var root=PrefabUtility.LoadPrefabContents(RootPath);
            try
            {
                var windows=root.transform.Find("Windows")??root.transform.Find("SafeArea/Windows");
                if(windows==null)throw new System.InvalidOperationException("Shared Windows mounting layer is missing.");
                windows.SetParent(root.transform,false);Stretch((RectTransform)windows);
                PrefabUtility.SaveAsPrefabAsset(root,RootPath);
            }
            finally{PrefabUtility.UnloadPrefabContents(root);}
            UIWindowCreator.UpgradeLocalSafeAreaTemplate();
            AssetDatabase.Refresh();Debug.Log("U4_FULL_FRAME_UPGRADE_PASS");
        }
        private static RectTransform Child(string name,Transform parent)
        { var go=new GameObject(name,typeof(RectTransform)); go.transform.SetParent(parent,false); return (RectTransform)go.transform; }
        private static void Stretch(RectTransform rect) { rect.anchorMin=Vector2.zero; rect.anchorMax=Vector2.one; rect.offsetMin=rect.offsetMax=Vector2.zero; }
    }
}
