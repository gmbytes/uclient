using System;
using System.IO;
using System.Linq;
using Client.Bootstrap;
using Client.Framework.HotUpdate;
using Client.Framework.Release;
using HybridCLR;
using HybridCLR.Commands;
using HybridCLR.Installer;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityGameFramework.Runtime;
using UnityBuildResult=UnityEditor.Build.Reporting.BuildResult;

namespace Client.Editor.Build
{
    public static class PlayerBuilder
    {
        internal static void ApplySettings(BuildRequest request,NativeBuildProfile profile)
        {
            ProjectSetup.ConfigurePlayer();EditorUserBuildSettings.development=false;EditorUserBuildSettings.buildScriptsOnly=false;EditorUserBuildSettings.buildAppBundle=false;EditorUserBuildSettings.exportAsGoogleAndroidProject=false;UnityEditor.WindowsStandalone.UserBuildSettings.createSolution=false;
            PlayerSettings.SetApplicationIdentifier(BuildTargetGroup.Android,profile.PackageId);PlayerSettings.SetApplicationIdentifier(BuildTargetGroup.Standalone,profile.PackageId);
            PlayerSettings.productName="Client Framework";PlayerSettings.defaultScreenWidth=720;PlayerSettings.defaultScreenHeight=1280;PlayerSettings.fullScreenMode=FullScreenMode.Windowed;PlayerSettings.resizableWindow=true;
            // Android 必须启用 AutoRotation；仅设置允许方向仍会把原生 Activity 锁为竖屏。
            PlayerSettings.defaultInterfaceOrientation=request.Platform==BuildPlatform.Android?UIOrientation.AutoRotation:UIOrientation.Portrait;PlayerSettings.allowedAutorotateToPortrait=true;PlayerSettings.allowedAutorotateToPortraitUpsideDown=false;PlayerSettings.allowedAutorotateToLandscapeLeft=true;PlayerSettings.allowedAutorotateToLandscapeRight=true;
            PlayerSettings.Android.forceInternetPermission=true;PlayerSettings.insecureHttpOption=profile.Endpoint.StartsWith("http://",StringComparison.Ordinal)?InsecureHttpOption.AlwaysAllowed:InsecureHttpOption.NotAllowed;
#if UNITY_ANDROID
            UnityEditor.Android.AndroidExternalToolsSettings.sdkRootPath=Environment.GetEnvironmentVariable("ANDROID_SDK_ROOT");UnityEditor.Android.AndroidExternalToolsSettings.ndkRootPath=Environment.GetEnvironmentVariable("ANDROID_NDK_ROOT");UnityEditor.Android.AndroidExternalToolsSettings.jdkRootPath=Environment.GetEnvironmentVariable("JAVA_HOME");
#endif
        }
        internal static void Build(BuildRequest request,BuildResult result,BuildInputs inputs,ResourceConfigurationScope scope)
        {
            BuildTarget target=BuildPreflight.Target(request.Platform);
            var installer=new InstallerController();if(!installer.HasInstalledHybridCLR())
            {string source=Environment.GetEnvironmentVariable("SGAME_HYBRIDCLR_SOURCE");if(string.IsNullOrWhiteSpace(source)||!Directory.Exists(source))throw new BuildFailedException("SGAME_HYBRIDCLR_SOURCE must identify the pinned assembled local toolchain.");installer.InstallFromLocal(Path.GetFullPath(source));}
            if(!installer.HasInstalledHybridCLR())throw new BuildFailedException("Pinned HybridCLR installation failed.");
            ProjectSetup.CreateBootstrap();ApplySettings(request,inputs.Profile);
            var gameRoot=PrefabUtility.LoadPrefabContents("Assets/Prefabs/App/GameRoot.prefab");
            try{if(gameRoot.GetComponent<ObjectPoolComponent>()==null)gameRoot.AddComponent<ObjectPoolComponent>();var settings=new SerializedObject(gameRoot.GetComponent<UIComponent>());settings.FindProperty("m_UIGroups").arraySize=0;settings.ApplyModifiedPropertiesWithoutUndo();PrefabUtility.SaveAsPrefabAsset(gameRoot,"Assets/Prefabs/App/GameRoot.prefab");}
            finally{PrefabUtility.UnloadPrefabContents(gameRoot);}
            var scene=EditorSceneManager.OpenScene("Assets/Scenes/Bootstrap.unity");var bootstrap=UnityEngine.Object.FindObjectOfType<ClientBootstrap>();bootstrap.enabled=false;
            var native=bootstrap.gameObject.AddComponent<NativeClientHost>();var nativeSettings=new SerializedObject(native);
            nativeSettings.FindProperty("m_PublicProfile").objectReferenceValue=AssetDatabase.LoadAssetAtPath<TextAsset>(BuildInputs.PublicAsset);
            nativeSettings.FindProperty("m_UIRoot").objectReferenceValue=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Content/UI/Common/AdaptiveUIRoot.prefab");nativeSettings.ApplyModifiedPropertiesWithoutUndo();
            const string scenePath="Assets/Tests/Generated/D1/Bootstrap.unity";Directory.CreateDirectory(Path.GetDirectoryName(scenePath));EditorSceneManager.SaveScene(scene,scenePath);EditorBuildSettings.scenes=new[]{new EditorBuildSettingsScene(scenePath,true)};AssetDatabase.SaveAssets();
            string baseline=Path.GetFullPath("Assets/StreamingAssets/Baseline");
            try
            {
                BuildProtocol.Verify(result.OutputDirectory);
                CompileDllCommand.CompileDll(target,false);
                ResourcePackageBuilder.Build(request,result,scope,baseline);
                GenerateStableInputs(request,result,inputs.Profile);
                string compiled=SettingsUtil.GetHotUpdateDllsOutputDirByTarget(target);HotfixCompatibilityCheck.RecordCompileInputs(compiled);
                string archiveRoot=Path.Combine(result.OutputDirectory,"Archive");var archive=PlayerBaselineArchive.Stage(archiveRoot,target,compiled,baseline);
                BuildToolArchive.Capture(archiveRoot,request.LinkerPolicyPath,archive.buildProfile,request.SigningKeyPath);result.EvidenceFiles.Add(Path.Combine(archiveRoot,"BuildTools/manifest.json"));
                BuildFiles.Put(Path.GetFullPath("Assets/StreamingAssets/ClientIdentity.json"),System.Text.Encoding.UTF8.GetBytes(JObject.FromObject(new{archive.playerBuildId,archive.platform,archive.architecture,archive.unityVersion,archive.contractVersion,archive.envelopeSha256,archive.aotMetadataSetId}).ToString(Formatting.Indented)+"\n"));
                PatchBuilder.SignPayload(request,inputs.Profile,archive,compiled,baseline,false);
                BuildFiles.CopyTree(baseline,Path.Combine(result.OutputDirectory,"Release"));
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport|ImportAssetOptions.ForceUpdate);ApplySettings(request,inputs.Profile);AssetDatabase.SaveAssets();
                string player=Path.Combine(result.OutputDirectory,"Player");Directory.CreateDirectory(player);string artifact=Path.Combine(player,request.Platform==BuildPlatform.Android?"Client.apk":"Client.exe");
                var report=BuildPipeline.BuildPlayer(new BuildPlayerOptions{scenes=new[]{scenePath},target=target,locationPathName=artifact,options=BuildOptions.None});
                File.WriteAllText(Path.Combine(result.OutputDirectory,"native-build.json"),SmokeBuild.CreateResultJson(request.Platform.ToString(),artifact,report.summary.result,report.summary.totalErrors,report.summary.totalWarnings,report.summary.totalSize)+"\n");
                if(report.summary.result!=UnityBuildResult.Succeeded||report.summary.totalErrors!=0||!File.Exists(artifact))throw new BuildFailedException("Actual full native Player build failed.");
                File.Delete(Path.Combine(baseline,"release.json"));File.Delete(Path.Combine(baseline,"release.sig"));PlayerBaselineArchive.Finalize(archiveRoot,baseline,player,target);
                PatchBuilder.Check(archiveRoot,compiled,result);PatchBuilder.SignChannel(request,inputs.Profile,archive,Path.Combine(result.OutputDirectory,"Release"));
                result.PlayerBuildId=archive.playerBuildId;result.ReleaseId=request.ReleaseId;result.EvidenceFiles.Add(Path.Combine(archiveRoot,"archive.json"));result.EvidenceFiles.Add(Path.Combine(result.OutputDirectory,"native-build.json"));
            }
            finally
            {
                BuildInputs.RequireOwnedSource(baseline);SourceFileTransaction.RejectReparsePoints(baseline);if(Directory.Exists(baseline))Directory.Delete(baseline,true);
            }
        }
        private static void GenerateStableInputs(BuildRequest request,BuildResult result,NativeBuildProfile profile)
        {
            var observations=new JArray();string previous=null;BuildTarget target=BuildPreflight.Target(request.Platform);
            for(int pass=1;pass<=3;pass++)
            {
                string evidence=Path.Combine(result.OutputDirectory,"Generation","pass"+pass);ApplySettings(request,profile);PrebuildCommand.GenerateAll();ApplySettings(request,profile);AssetDatabase.SaveAssets();
                var generated=new JArray();foreach(string path in new[]{"Assets/Scripts/Framework/HotUpdate/Generated/link.xml",BuildInputs.GeneratedAot}){generated.Add(new JObject{["path"]=path,["sha256"]=HotfixLoadPlan.Hash(File.ReadAllBytes(path))});BuildFiles.Put(Path.Combine(evidence,"Generated",Path.GetFileName(path)),File.ReadAllBytes(path));}
                foreach(var file in PlayerBaselineArchive.Inventory(SettingsUtil.GeneratedCppDir))generated.Add(JObject.FromObject(file));
                var snapshot=new JObject{["generated"]=generated,["compiled"]=new JArray(PlayerBaselineArchive.Inventory(SettingsUtil.GetHotUpdateDllsOutputDirByTarget(target)).Where(f=>f.path.EndsWith(".dll",StringComparison.Ordinal)).Select(JObject.FromObject)),["metadata"]=new JArray(PlayerBaselineArchive.Inventory(SettingsUtil.GetAssembliesPostIl2CppStripDir(target)).Select(JObject.FromObject))};
                BuildFiles.CopyTree(SettingsUtil.GeneratedCppDir,Path.Combine(evidence,"Bridge"));BuildFiles.CopyTree(SettingsUtil.GetHotUpdateDllsOutputDirByTarget(target),Path.Combine(evidence,"Compiled"));BuildFiles.CopyTree(SettingsUtil.GetAssembliesPostIl2CppStripDir(target),Path.Combine(evidence,"Metadata"));
                string digest=HotfixLoadPlan.Hash(System.Text.Encoding.UTF8.GetBytes(snapshot.ToString(Formatting.None)));observations.Add(new JObject{["pass"]=pass,["sha256"]=digest,["snapshot"]=snapshot});File.WriteAllText(Path.Combine(result.OutputDirectory,"generation-passes.json"),observations.ToString(Formatting.Indented)+"\n");
                if(pass>=2&&digest==previous)return;previous=digest;
            }
            throw new BuildFailedException("Generated/compiled/post-strip inputs did not converge within three official passes.");
        }
    }
}
