using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using Client.Editor.Build;
using Client.Framework.HotUpdate;
using Client.Framework.Release;
using dnlib.DotNet;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Build.Player;
using UnityEngine;

namespace Client.Tests.EditMode
{
    [Category("D1")]
    public sealed class BuildEntryIntegrationTests
    {
        [Test] public void AllEntriesReturnSameResolvedOutput()
        {
            string root=Path.GetFullPath("Builds/Tests/D1/MenuReceipt/"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
            string request=Path.Combine(root,"request.json"),receipt=Path.Combine(root,"receipt.json");
            File.WriteAllText(request,new JObject{["Kind"]="Player",["Platform"]="Windows64",["OutputRoot"]=root,["ReleaseId"]="v1"}.ToString());
            bool existed=EditorPrefs.HasKey(ClientBuild.MenuRequestPreference);string previous=EditorPrefs.GetString(ClientBuild.MenuRequestPreference);
            try
            {
                EditorPrefs.SetString(ClientBuild.MenuRequestPreference,request);
                Assert.That(EditorApplication.ExecuteMenuItem("Game/Build/Execute Selected Request"),Is.True);
                var result=ClientBuild.LastMenuResult;Assert.That(result,Is.Not.Null);Assert.That(result.Status,Is.EqualTo(BuildStatus.Failed),"Missing signer/profile must not build a fake fixture.");
                typeof(ClientBuild).GetMethod("WriteReceipt",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{receipt,result});
                var returned=JObject.Parse(File.ReadAllText(receipt));var saved=JObject.Parse(File.ReadAllText(result.ResultFile));
                Assert.That((string)returned["OutputDirectory"],Is.EqualTo(result.OutputDirectory));Assert.That((string)returned["ResultFile"],Is.EqualTo(result.ResultFile));
                Assert.That((string)saved["OutputDirectory"],Is.EqualTo(result.OutputDirectory));Assert.That((string)saved["ResultFile"],Is.EqualTo(result.ResultFile));
                Assert.That(result.OutputDirectory,Is.Not.EqualTo(root));
            }
            finally{if(existed)EditorPrefs.SetString(ClientBuild.MenuRequestPreference,previous);else EditorPrefs.DeleteKey(ClientBuild.MenuRequestPreference);}
        }
        [Test] public void FinalPlayerSettingsApplyAndRestoreRequiredNativeWindowPolicy()
        {
            byte[] before=File.ReadAllBytes("ProjectSettings/ProjectSettings.asset");
            using(var key=new RSACryptoServiceProvider(2048){PersistKeyInCsp=false})
            {
                var parameters=key.ExportParameters(false);
                var profile=NativeBuildProfile.Parse(new JObject{["schemaVersion"]=1,["keyId"]="acceptance-release",["modulus"]=Convert.ToBase64String(parameters.Modulus),["exponent"]=Convert.ToBase64String(parameters.Exponent),["endpoint"]="http://127.0.0.1:18765/",["acceptance"]=true,["packageId"]="com.sgame.client.acceptance",["sessionHost"]="127.0.0.1",["sessionPort"]=7000}.ToString());
                using(BuildSourceTransaction.Begin(Array.Empty<string>()))
                {
                    typeof(PlayerBuilder).GetMethod("ApplySettings",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{new BuildRequest(BuildKind.Player,BuildPlatform.Windows64,"Builds"),profile});
                    Assert.That(PlayerSettings.defaultScreenWidth,Is.EqualTo(720));Assert.That(PlayerSettings.defaultScreenHeight,Is.EqualTo(1280));Assert.That(PlayerSettings.resizableWindow,Is.True);Assert.That(PlayerSettings.fullScreenMode,Is.EqualTo(FullScreenMode.Windowed));
                    Assert.That(PlayerSettings.defaultInterfaceOrientation,Is.EqualTo(UIOrientation.Portrait));Assert.That(PlayerSettings.allowedAutorotateToPortrait&&PlayerSettings.allowedAutorotateToLandscapeLeft&&PlayerSettings.allowedAutorotateToLandscapeRight,Is.True);Assert.That(PlayerSettings.allowedAutorotateToPortraitUpsideDown,Is.False);Assert.That(PlayerSettings.Android.forceInternetPermission,Is.True);
                    Assert.That(EditorUserBuildSettings.development||EditorUserBuildSettings.buildScriptsOnly||EditorUserBuildSettings.buildAppBundle||EditorUserBuildSettings.exportAsGoogleAndroidProject,Is.False);
                    AssetDatabase.SaveAssets();
                }
            }
            Assert.That(File.ReadAllBytes("ProjectSettings/ProjectSettings.asset"),Is.EqualTo(before));
        }
        [Test] public void OfficialCompilerIncludesNewV2ParserOnlyInV2AndPreservesFixedCompiledReferences()
        {
            string fixture=Path.GetFullPath("Tests/Fixtures/Delivery"),output=Path.GetFullPath("Builds/Tests/D1/OverlayCompiler/"+Guid.NewGuid().ToString("N"));
            string[] versions={"V1","V2"};string[] paths=versions.SelectMany(v=>Directory.GetFiles(Path.Combine(fixture,v),"*.cs",SearchOption.AllDirectories).Select(p=>p.Substring(Path.Combine(fixture,v).Length+1).Replace('\\','/'))).Distinct().ToArray();
            EditorApplication.LockReloadAssemblies();AssetDatabase.DisallowAutoRefresh();
            try
            {
                using(SourceFileTransaction.Capture(paths))
                {
                    foreach(string version in versions)
                    {
                        string sourceRoot=Path.Combine(fixture,version);
                        AssetDatabase.StartAssetEditing();
                        try
                        {
                            if(version=="V2")AssetDatabase.DeleteAsset("Assets/Scripts/Game/Protocol/Delivery/Baseline.cs");
                            foreach(string file in Directory.GetFiles(sourceRoot,"*.cs",SearchOption.AllDirectories))
                            {string target=file.Substring(sourceRoot.Length+1).Replace('\\','/');Directory.CreateDirectory(Path.GetDirectoryName(target));File.Copy(file,target,true);AssetDatabase.ImportAsset(target,ImportAssetOptions.ForceSynchronousImport|ImportAssetOptions.ForceUpdate);}
                        }
                        finally{AssetDatabase.StopAssetEditing();}
                        string compiled=Path.Combine(output,version);Directory.CreateDirectory(compiled);
                        var settings=new ScriptCompilationSettings{group=BuildPipeline.GetBuildTargetGroup(EditorUserBuildSettings.activeBuildTarget),target=EditorUserBuildSettings.activeBuildTarget,options=ScriptCompilationOptions.None};
                        Assert.That(PlayerBuildInterface.CompilePlayerScripts(settings,compiled).assemblies,Is.Not.Empty);
                        Assert.That(File.Exists(Path.Combine(compiled,"Client.Game.Protocol.dll"))&&File.Exists(Path.Combine(compiled,"Client.Game.dll")),Is.True);
                        string snapshot=Path.Combine(output,"Snapshots",version);Directory.CreateDirectory(snapshot);
                        foreach(string file in Directory.GetFiles(compiled,"*.dll"))File.Copy(file,Path.Combine(snapshot,Path.GetFileName(file)));
                    }
                    // Unity's compiler removes its previous output files when its output directory changes.
                    using(var v1=ModuleDefMD.Load(File.ReadAllBytes(Path.Combine(output,"Snapshots/V1/Client.Game.Protocol.dll"))))using(var v2=ModuleDefMD.Load(File.ReadAllBytes(Path.Combine(output,"Snapshots/V2/Client.Game.Protocol.dll"))))
                    {Assert.That(v1.Find("Client.Game.Protocol.AcceptanceV2.AddedMessage",false),Is.Null);Assert.That(v2.Find("Client.Game.Protocol.AcceptanceV2.AddedMessage",false)?.Properties.Any(p=>p.Name=="Parser"),Is.True);}
                    foreach(string assembly in new[]{"Client.Contracts.dll","Client.Framework.dll","Client.Protocol.Envelope.dll","HybridCLR.Runtime.dll"})
                        Assert.That(HotfixLoadPlan.Hash(File.ReadAllBytes(Path.Combine(output,"Snapshots/V1",assembly))),Is.EqualTo(HotfixLoadPlan.Hash(File.ReadAllBytes(Path.Combine(output,"Snapshots/V2",assembly)))),assembly);
                    Assert.That(Type.GetType("Client.Game.Protocol.AcceptanceV2.AddedMessage, Client.Game.Protocol"),Is.Null,"New hot target input must not be mistaken for an already-loaded Editor type.");
                }
            }
            finally{AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport|ImportAssetOptions.ForceUpdate);AssetDatabase.AllowAutoRefresh();EditorApplication.UnlockReloadAssemblies();}
        }
    }
}
