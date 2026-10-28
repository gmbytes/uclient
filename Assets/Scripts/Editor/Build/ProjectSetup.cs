using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityGameFramework.Runtime;
using HybridCLR.Installer;
using HybridCLR.Settings;
using Client.Framework.Resource;
using Client.Bootstrap;
using UnityEngine.UI;
using UnityEngine.EventSystems;

namespace Client.Editor.Build
{
    public static class ProjectSetup
    {
        public static void CreateBootstrap()
        {
            Directory.CreateDirectory("Assets/Scenes");
            Directory.CreateDirectory("Assets/Prefabs/App");
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var host = new GameObject("GameFramework");
            host.AddComponent<BaseComponent>().EditorResourceMode = false;
            host.AddComponent<EventComponent>();
            host.AddComponent<DownloadComponent>();
            var gameRoot = new GameObject("GameRoot");
            gameRoot.SetActive(false);
            gameRoot.AddComponent<ResourceComponent>();
            gameRoot.AddComponent<UIComponent>();
            var gameRootPrefab = PrefabUtility.SaveAsPrefabAsset(gameRoot, "Assets/Prefabs/App/GameRoot.prefab");
            Object.DestroyImmediate(gameRoot);
            var bootstrap = host.AddComponent<ResourceSlotBootstrap>();
            var bootstrapSettings = new SerializedObject(bootstrap);
            bootstrapSettings.FindProperty("m_GameRootPrefab").objectReferenceValue = gameRootPrefab;
            bootstrapSettings.ApplyModifiedPropertiesWithoutUndo();
            PrefabUtility.SaveAsPrefabAssetAndConnect(host, "Assets/Prefabs/App/GameFramework.prefab", InteractionMode.AutomatedAction);
            var startup = new GameObject("ClientBootstrap").AddComponent<ClientBootstrap>();
            var updateRoot = new GameObject("UIUpdate", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
            updateRoot.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = updateRoot.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080, 1920);
            scaler.matchWidthOrHeight = 0.5f;
            var labelRoot = new GameObject("StatusLabel", typeof(RectTransform), typeof(Text));
            labelRoot.transform.SetParent(updateRoot.transform, false);
            var label = labelRoot.GetComponent<Text>();
            label.text = "Initializing";
            label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            label.fontSize = 32;
            label.alignment = TextAnchor.MiddleCenter;
            label.raycastTarget = false;
            var rect = labelRoot.GetComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            var updateUI = updateRoot.AddComponent<UIUpdate>();
            var updateSettings = new SerializedObject(updateUI);
            updateSettings.FindProperty("m_Status").objectReferenceValue = label;
            updateSettings.ApplyModifiedPropertiesWithoutUndo();
            PrefabUtility.SaveAsPrefabAssetAndConnect(updateRoot, "Assets/Prefabs/App/UIUpdate.prefab", InteractionMode.AutomatedAction);
            var startupSettings = new SerializedObject(startup);
            startupSettings.FindProperty("m_UpdateUI").objectReferenceValue = updateUI;
            startupSettings.ApplyModifiedPropertiesWithoutUndo();
            new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
            EditorSceneManager.SaveScene(scene, "Assets/Scenes/Bootstrap.unity");
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene("Assets/Scenes/Bootstrap.unity", true) };
            ConfigurePlayer();
            AssetDatabase.SaveAssets();
        }

        public static void ConfigurePlayer()
        {
            PlayerSettings.companyName = "sgame";
            PlayerSettings.productName = "Client";
            PlayerSettings.bundleVersion = "0.1.0";
            PlayerSettings.SetApplicationIdentifier(BuildTargetGroup.Standalone, "com.sgame.client");
            PlayerSettings.SetApplicationIdentifier(BuildTargetGroup.Android, "com.sgame.client");
            foreach (var group in new[] { BuildTargetGroup.Standalone, BuildTargetGroup.Android })
            {
                PlayerSettings.SetScriptingBackend(group, ScriptingImplementation.IL2CPP);
                PlayerSettings.SetApiCompatibilityLevel(group, ApiCompatibilityLevel.NET_Standard);
            }
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel23;
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.Portrait;
            EditorSettings.serializationMode = SerializationMode.ForceText;
            var settings = HybridCLRSettings.instance;
            settings.enable = true;
            settings.useGlobalIl2cpp = false;
            settings.outputLinkFile = "Scripts/Framework/HotUpdate/Generated/link.xml";
            settings.outputAOTGenericReferenceFile = "Scripts/Framework/HotUpdate/Generated/AOTGenericReferences.cs";
            // Protocol is reserved by its asmdef; N1/F6 adds it once a real DLL exists.
            settings.hotUpdateAssemblyDefinitions = new[] { AssetDatabase.LoadAssetAtPath<UnityEditorInternal.AssemblyDefinitionAsset>("Assets/Scripts/Game/Client.Game.asmdef") };
            settings.hotUpdateAssemblies = System.Array.Empty<string>();
            HybridCLRSettings.Save();
        }

        public static void InstallHybridCLR()
        {
            var arguments = System.Environment.GetCommandLineArgs();
            var index = System.Array.IndexOf(arguments, "-hybridclrSource");
            if (index < 0 || index + 1 >= arguments.Length) throw new System.ArgumentException("-hybridclrSource is required.");
            var installer = new InstallerController();
            installer.InstallFromLocal(Path.GetFullPath(arguments[index + 1]));
            if (!installer.HasInstalledHybridCLR() || installer.InstalledLibil2cppVersion != "9.0.0")
                throw new System.InvalidOperationException("HybridCLR 9.0.0 installation was not verified.");
            Debug.Log("HYBRIDCLR_INSTALL_PASS 9.0.0");
        }
    }
}
