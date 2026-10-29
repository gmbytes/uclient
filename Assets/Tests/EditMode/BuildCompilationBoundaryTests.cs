using System;
using System.IO;
using Client.Editor.Build;
using dnlib.DotNet;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Build.Player;

namespace Client.Tests.EditMode
{
    [Category("D1")]
    public sealed class BuildCompilationBoundaryTests
    {
        [Test] public void OfficialTargetCompilationConsumesChangedSourceBeforeEditorDomainReload()
        {
            const string source="Assets/Scripts/Game/Entry.cs";
            string original=File.ReadAllText(source), output=Path.GetFullPath("Builds/Tests/D1/ChangedCompilerInput/"+Guid.NewGuid().ToString("N"));
            const string changed="Assets/Settings/Environment/ChangedInputProof.asset";
            var loaded=Type.GetType("Client.Game.Entry, Client.Game"); Assert.That(loaded,Is.Not.Null);
            string before=(string)loaded.GetField("LocalSimulationProfilePath").GetRawConstantValue();
            EditorApplication.LockReloadAssemblies();AssetDatabase.DisallowAutoRefresh();
            try
            {
                using(SourceFileTransaction.Capture(new[]{source}))
                {
                    File.WriteAllText(source,original.Replace(before,changed));Directory.CreateDirectory(output);
                    var settings=new ScriptCompilationSettings{group=BuildPipeline.GetBuildTargetGroup(EditorUserBuildSettings.activeBuildTarget),target=EditorUserBuildSettings.activeBuildTarget,options=ScriptCompilationOptions.None};
                    var compiled=PlayerBuildInterface.CompilePlayerScripts(settings,output);
                    Assert.That(compiled.assemblies,Is.Not.Empty);
                    using(var module=ModuleDefMD.Load(File.ReadAllBytes(Path.Combine(output,"Client.Game.dll"))))
                        Assert.That(module.Find("Client.Game.Entry",false).FindField("LocalSimulationProfilePath").Constant.Value,Is.EqualTo(changed));
                    Assert.That((string)loaded.GetField("LocalSimulationProfilePath").GetRawConstantValue(),Is.EqualTo(before),"The assertion deliberately proves target bytes differ from the still-loaded Editor type.");
                }
            }
            finally{AssetDatabase.AllowAutoRefresh();EditorApplication.UnlockReloadAssemblies();}
            Assert.That(File.ReadAllText(source),Is.EqualTo(original));
        }
        [Test] public void UnifiedExecuteFailsClosedAndReturnsItsOwnActualResultFile()
        {
            var type=Type.GetType("Client.Editor.Build.ClientBuild, Client.Editor");Assert.That(type,Is.Not.Null,"D1 unified Execute is required.");
            string root=Path.GetFullPath("Builds/Tests/D1/EntryFailure/"+Guid.NewGuid().ToString("N"));
            var result=(Client.Editor.Build.BuildResult)type.GetMethod("Execute").Invoke(null,new object[]{new BuildRequest(BuildKind.Player,BuildPlatform.Windows64,root)});
            Assert.That(result.Status,Is.EqualTo(BuildStatus.Failed));Assert.That(result.Errors,Is.Not.Empty);
            Assert.That(result.ResultFile,Is.EqualTo(Path.Combine(result.OutputDirectory,"build-result.json")));
            Assert.That(File.Exists(result.ResultFile),Is.True);
            Assert.That(File.Exists(Path.Combine(result.OutputDirectory,"Release/channel.json")),Is.False);
        }
    }
}
