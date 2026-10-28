using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityGameFramework.Runtime;
using HybridCLR.Installer;
using HybridCLR.Settings;

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
            PrefabUtility.SaveAsPrefabAssetAndConnect(host, "Assets/Prefabs/App/GameFramework.prefab", InteractionMode.AutomatedAction);
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
