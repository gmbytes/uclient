using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Client.Framework.HotUpdate;
using Client.Framework.Release;
using Client.Framework.UI;
using HybridCLR;
using HybridCLR.Commands;
using HybridCLR.Installer;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityGameFramework.Editor.ResourceTools;
using UnityGameFramework.Runtime;

namespace Client.Editor.Build
{
    public static class HotUpdateProofBuild
    {
        private static string root;
        private static BuildTarget target;
        [ResourceCollectionConfigPath] private static string CollectionPath=>root==null?null:Path.Combine(root,"ResourceCollection.xml");
        [ResourceBuilderConfigPath] private static string BuilderPath=>root==null?null:Path.Combine(root,"ResourceBuilder.xml");
        public static void Run()
        {
            try
            {
                root=Path.GetFullPath(Arg("-f8Output"));Directory.CreateDirectory(root);
                target=Arg("-f8Platform")=="Android"?BuildTarget.Android:BuildTarget.StandaloneWindows64;
                if(EditorUserBuildSettings.activeBuildTarget!=target)throw new BuildFailedException("Launch the Editor with the exact target.");
                var installer=new InstallerController();if(!installer.HasInstalledHybridCLR())installer.InstallFromLocal(Path.GetFullPath(Arg("-hybridclrSource")));
                if(!installer.HasInstalledHybridCLR())throw new BuildFailedException("HybridCLR installation unavailable.");
                using(ReleaseLinkerSymbolPolicy.Apply(Arg("-f8LinkerPolicy")))
                { if(Arg("-f8Mode")=="Native")BuildNative();else BuildPatch(); }
            }
            catch(Exception error){Debug.LogException(error);EditorApplication.Exit(1);}
            finally{root=null;}
        }
        private static void FullRelease()
        {EditorUserBuildSettings.development=false;EditorUserBuildSettings.buildScriptsOnly=false;EditorUserBuildSettings.buildAppBundle=false;EditorUserBuildSettings.exportAsGoogleAndroidProject=false;UnityEditor.WindowsStandalone.UserBuildSettings.createSolution=false;}
        private static void Configure()
        {
            ProjectSetup.CreateBootstrap();FullRelease();
            PlayerSettings.SetApplicationIdentifier(BuildTargetGroup.Android,"com.sgame.client.hotupdateproof");
            PlayerSettings.SetApplicationIdentifier(BuildTargetGroup.Standalone,"com.sgame.client.hotupdateproof");
            PlayerSettings.productName="Client Hot Update Proof";PlayerSettings.Android.targetArchitectures=AndroidArchitecture.ARM64;
            PlayerSettings.insecureHttpOption=InsecureHttpOption.AlwaysAllowed;
            PlayerSettings.defaultScreenWidth=720;PlayerSettings.defaultScreenHeight=960;PlayerSettings.fullScreenMode=FullScreenMode.Windowed;
#if UNITY_ANDROID
            UnityEditor.Android.AndroidExternalToolsSettings.sdkRootPath=Environment.GetEnvironmentVariable("ANDROID_SDK_ROOT");
            UnityEditor.Android.AndroidExternalToolsSettings.ndkRootPath=Environment.GetEnvironmentVariable("ANDROID_NDK_ROOT");
            UnityEditor.Android.AndroidExternalToolsSettings.jdkRootPath=Environment.GetEnvironmentVariable("JAVA_HOME");
#endif
            var prefab=PrefabUtility.LoadPrefabContents("Assets/Prefabs/App/GameRoot.prefab");
            try
            {
                if(prefab.GetComponent<ObjectPoolComponent>()==null)prefab.AddComponent<ObjectPoolComponent>();
                var settings=new SerializedObject(prefab.GetComponent<UIComponent>());settings.FindProperty("m_UIGroups").arraySize=0;settings.ApplyModifiedPropertiesWithoutUndo();
                PrefabUtility.SaveAsPrefabAsset(prefab,"Assets/Prefabs/App/GameRoot.prefab");
            }
            finally{PrefabUtility.UnloadPrefabContents(prefab);}
            var scene=EditorSceneManager.OpenScene("Assets/Scenes/Bootstrap.unity");
            UnityEngine.Object.FindObjectOfType<Client.Bootstrap.ClientBootstrap>().enabled=false;
            Type type=Type.GetType("Client.Bootstrap.HotUpdateProof.Runtime, Client.Bootstrap");if(type==null)throw new BuildFailedException("F8 native runtime profile missing.");
            var runtime=new GameObject("Native Hot Update Proof").AddComponent(type);
            type.GetField("uiRoot").SetValue(runtime,AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Content/UI/Common/AdaptiveUIRoot.prefab"));
            Directory.CreateDirectory("Assets/Tests/Generated");EditorSceneManager.SaveScene(scene,"Assets/Tests/Generated/F8.unity");
            EditorBuildSettings.scenes=new[]{new EditorBuildSettingsScene("Assets/Tests/Generated/F8.unity",true)};AssetDatabase.SaveAssets();
        }
        private static void BuildNative()
        {
            if(Directory.Exists(Path.Combine(root,"Archive")))throw new IOException("A new immutable native archive is required.");
            Configure();string baseline=Path.GetFullPath("Assets/StreamingAssets/F8Baseline");BuildPackage(baseline,"v1");
            GenerateStableInputs();string compiled=SettingsUtil.GetHotUpdateDllsOutputDirByTarget(target);HotfixCompatibilityCheck.RecordCompileInputs(compiled);
            string archivePath=Path.Combine(root,"Archive");var archive=PlayerBaselineArchive.Stage(archivePath,target,compiled,baseline);
            File.WriteAllText("Assets/StreamingAssets/F8PlayerIdentity.json",JObject.FromObject(new {playerBuildId=archive.playerBuildId,platform=archive.platform,architecture=archive.architecture,unityVersion=archive.unityVersion,contractVersion=archive.contractVersion,envelopeSha256=archive.envelopeSha256,aotMetadataSetId=archive.aotMetadataSetId}).ToString(Formatting.Indented)+"\n");
            Sign(baseline,archive,"v1",compiled);CopyTree(baseline,Path.Combine(root,"Releases/v1"));AssetDatabase.Refresh();FullRelease();
            string player=Path.Combine(root,"Player");Directory.CreateDirectory(player);string artifact=Path.Combine(player,target==BuildTarget.Android?"Client.apk":"Client.exe");
            var report=BuildPipeline.BuildPlayer(new BuildPlayerOptions{scenes=new[]{"Assets/Tests/Generated/F8.unity"},target=target,locationPathName=artifact,options=BuildOptions.None});
            File.WriteAllText(Path.Combine(root,"build-result.json"),SmokeBuild.CreateResultJson(target==BuildTarget.Android?"Android":"Windows64",artifact,report.summary.result,report.summary.totalErrors,report.summary.totalWarnings,report.summary.totalSize)+"\n");
            if(report.summary.result!=BuildResult.Succeeded||report.summary.totalErrors!=0)throw new BuildFailedException("Fresh native Player build failed.");
            File.Delete(Path.Combine(baseline,"release.json"));File.Delete(Path.Combine(baseline,"release.sig"));File.Delete(Path.Combine(baseline,"channel.json"));File.Delete(Path.Combine(baseline,"channel.sig"));
            PlayerBaselineArchive.Finalize(archivePath,baseline,player,target);var verified=PlayerBaselineArchive.Open(archivePath);
            Check(archivePath,compiled,Path.Combine(root,"compatibility-v1.json"));Debug.Log("F8_NATIVE_BUILD_COMPLETE "+verified.playerBuildId);
        }
        private static void BuildPatch()
        {
            FullRelease();string archivePath=Path.Combine(root,"Archive");var archive=PlayerBaselineArchive.Open(archivePath);
            if(archive.platform!=target.ToString())throw new BuildFailedException("Patch target differs from original Player.");
            string version=Arg("-f8Version");if(version!="v2"&&version!="throw")throw new ArgumentException("Unsupported patch fixture.");
            CompileDllCommand.CompileDll(target,false);string compiled=SettingsUtil.GetHotUpdateDllsOutputDirByTarget(target);HotfixCompatibilityCheck.RecordCompileInputs(compiled);
            Check(archivePath,compiled,Path.Combine(root,"compatibility-"+version+".json"));
            string release=Path.Combine(root,"Releases",version);if(Directory.Exists(release))throw new IOException("Release snapshots cannot be overwritten.");
            BuildPackage(release,version);CopyTree(Path.Combine(archivePath,"Metadata"),Path.Combine(release,"Metadata"));CopyTree(Path.Combine(archivePath,"Facades"),Path.Combine(release,"References"));
            foreach(string name in new[]{"Client.Game.Protocol","Client.Game"})Put(Path.Combine(release,"Hotfix",name+".dll"),File.ReadAllBytes(Path.Combine(compiled,name+".dll")));
            Sign(release,archive,version,compiled);CopyTree(compiled,Path.Combine(root,"PatchCompiled",version));Debug.Log("F8_PATCH_COMPLETE "+version);
        }
        private static void Check(string archive,string compiled,string output)
        {var check=HotfixCompatibilityCheck.Check(archive,compiled);File.WriteAllText(output,JsonConvert.SerializeObject(check,Formatting.Indented)+"\n");if(check.RequiresNewPlayer)throw new BuildFailedException("Official compatibility requires a new Player: "+string.Join("; ",check.MissingAotReferences));}
        private static void BuildPackage(string destination,string version)
        {
            var profile=AssetDatabase.LoadAssetAtPath<LoginPresentationProfile>("Assets/Settings/Environment/LocalSimulationProfile.asset");
            profile.Version=version=="v1"?"F8 resource v1":"F8 resource v2";EditorUtility.SetDirty(profile);AssetDatabase.SaveAssets();
            var collection=new ResourceCollection();
            string[] assets={"Assets/Content/UI/Login/UILogin.prefab","Assets/Content/UI/Common/UIViewDefinitions.asset","Assets/Settings/Environment/LocalSimulationProfile.asset"};
            if(!collection.AddResource("f8/ui",null,null,LoadType.LoadFromFile,true)||!collection.AddResource("f8/profile",null,null,LoadType.LoadFromFile,true))throw new BuildFailedException("Native resources failed.");
            for(int i=0;i<assets.Length;i++){string name=i<2?"f8/ui":"f8/profile";if(!collection.AssignAsset(AssetDatabase.AssetPathToGUID(assets[i]),name,null))throw new BuildFailedException("Native collection failed.");}
            if(!collection.Save())throw new BuildFailedException("Native collection save failed.");
            var builder=new ResourceBuilderController{Platforms=target==BuildTarget.Android?Platform.Android:Platform.Windows64,InternalResourceVersion=version=="v1"?1:2,OutputDirectory=Path.Combine(root,"NativePackage",version),OutputPackageSelected=true,OutputFullSelected=false,OutputPackedSelected=false,ForceRebuildAssetBundleSelected=true};
            Directory.CreateDirectory(builder.OutputDirectory);if(!builder.BuildResources())throw new BuildFailedException("Real GF Package build failed.");
            CopyTree(Path.GetFullPath(Path.Combine(builder.OutputPackagePath,target==BuildTarget.Android?"Android":"Windows64")),Path.Combine(destination,"Resources"));
        }
        private static void GenerateStableInputs()
        {
            var observations=new JArray();string previous=null;
            for(int pass=1;pass<=3;pass++)
            {
                string evidence=Path.Combine(root,"Generation","pass"+pass);FullRelease();PrebuildCommand.GenerateAll();FullRelease();AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport|ImportAssetOptions.ForceUpdate);
                var generated=new JArray();foreach(string path in new[]{"Assets/Scripts/Framework/HotUpdate/Generated/link.xml","Assets/Scripts/Framework/HotUpdate/Generated/AOTGenericReferences.cs"}){generated.Add(new JObject{["path"]=path,["sha256"]=HotfixLoadPlan.Hash(File.ReadAllBytes(path))});Put(Path.Combine(evidence,"Generated",Path.GetFileName(path)),File.ReadAllBytes(path));}
                foreach(var file in PlayerBaselineArchive.Inventory(SettingsUtil.GeneratedCppDir))generated.Add(JObject.FromObject(file));
                var snapshot=new JObject{["generated"]=generated,["compiled"]=new JArray(PlayerBaselineArchive.Inventory(SettingsUtil.GetHotUpdateDllsOutputDirByTarget(target)).Where(f=>f.path.EndsWith(".dll",StringComparison.Ordinal)).Select(JObject.FromObject)),["metadata"]=new JArray(PlayerBaselineArchive.Inventory(SettingsUtil.GetAssembliesPostIl2CppStripDir(target)).Select(JObject.FromObject))};
                CopyTree(SettingsUtil.GeneratedCppDir,Path.Combine(evidence,"Bridge"));CopyTree(SettingsUtil.GetHotUpdateDllsOutputDirByTarget(target),Path.Combine(evidence,"Compiled"));CopyTree(SettingsUtil.GetAssembliesPostIl2CppStripDir(target),Path.Combine(evidence,"Metadata"));
                string digest=HotfixLoadPlan.Hash(System.Text.Encoding.UTF8.GetBytes(snapshot.ToString(Formatting.None)));observations.Add(new JObject{["pass"]=pass,["sha256"]=digest,["snapshot"]=snapshot});File.WriteAllText(Path.Combine(root,"generation-passes.json"),observations.ToString(Formatting.Indented)+"\n");
                if(pass>=2&&digest==previous)return;previous=digest;
            }
            throw new BuildFailedException("Official generated/compiled/post-strip bytes failed exact bounded fixedpoint; no native archive may publish.");
        }
        private static void Sign(string directory,PlayerBaselineArchive.Archive archive,string version,string compiled)
        {
            var files=PlayerBaselineArchive.Inventory(directory);string[] names={"Client.Game.Protocol","Client.Game"};
            string protocol=(string)JObject.Parse(File.ReadAllText("Tests/Fixtures/HotfixAotProbe/Generated/Pristine/protocol-manifest.json"))["schema_sha256"];
            var manifest=new JObject{["schemaVersion"]=1,["keyId"]="test-f8-native",["releaseId"]=version,["player"]=JObject.FromObject(new{playerBuildId=archive.playerBuildId,platform=archive.platform,architecture=archive.architecture,unityVersion=archive.unityVersion,contractVersion=archive.contractVersion,envelopeSha256=archive.envelopeSha256,aotMetadataSetId=archive.aotMetadataSetId}),["protocolSha256"]=protocol,["resourceVersionListPath"]="Resources/GameFrameworkVersion.dat",["files"]=new JArray(files.Select(f=>new JObject{["path"]=f.path,["length"]=f.length,["sha256"]=f.sha256,["kind"]=f.path.StartsWith("Metadata/")?"AotMetadata":f.path.StartsWith("Hotfix/")?"HotfixDll":f.path.StartsWith("References/")?"Configuration":"NativeResource"})),["assemblies"]=new JArray(names.Select(name=>{using(var module=dnlib.DotNet.ModuleDefMD.Load(File.ReadAllBytes(Path.Combine(compiled,name+".dll"))))return new JObject{["name"]=name,["path"]="Hotfix/"+name+".dll",["dependencies"]=new JArray(module.GetAssemblyRefs().Select(a=>a.Name.String))};})),["assemblyOrder"]=new JArray(names)};
            byte[] bytes=System.Text.Encoding.UTF8.GetBytes(manifest.ToString(Formatting.None));
            var verifier=(ManifestVerifier)Type.GetType("Client.Framework.HotUpdateProof.ProofProfile, Client.Framework").GetMethod("CreateVerifier").Invoke(null,null);
            using(var key=RSA.Create())
            {
                key.FromXmlString(File.ReadAllText(Arg("-f8Key")));byte[] signature=key.SignData(bytes,HashAlgorithmName.SHA256,RSASignaturePadding.Pkcs1);verifier.Verify(bytes,signature,archive.Identity);Put(Path.Combine(directory,"release.json"),bytes);Put(Path.Combine(directory,"release.sig"),signature);
                byte[] channel=System.Text.Encoding.UTF8.GetBytes(new JObject{["schemaVersion"]=1,["keyId"]="test-f8-native",["releaseId"]=version,["manifestRelativePath"]="release.json",["manifestSha256"]=HotfixLoadPlan.Hash(bytes)}.ToString(Formatting.None));byte[] signed=key.SignData(channel,HashAlgorithmName.SHA256,RSASignaturePadding.Pkcs1);verifier.VerifyChannel(channel,signed);Put(Path.Combine(directory,"channel.json"),channel);Put(Path.Combine(directory,"channel.sig"),signed);
            }
        }
        private static void Put(string path,byte[] bytes){Directory.CreateDirectory(Path.GetDirectoryName(path));File.WriteAllBytes(path,bytes);}
        private static void CopyTree(string source,string destination){string full=Path.GetFullPath(source);foreach(string path in Directory.GetFiles(full,"*",SearchOption.AllDirectories).Where(p=>!p.EndsWith(".meta",StringComparison.OrdinalIgnoreCase)))Put(Path.Combine(destination,path.Substring(full.TrimEnd(Path.DirectorySeparatorChar).Length+1)),File.ReadAllBytes(path));}
        private static string Arg(string name){string[] args=Environment.GetCommandLineArgs();int i=Array.IndexOf(args,name);if(i<0||i+1>=args.Length)throw new ArgumentException(name);return args[i+1];}
    }
}
