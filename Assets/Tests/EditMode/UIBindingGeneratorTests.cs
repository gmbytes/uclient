using System;
using System.IO;
using System.Collections;
using System.Linq;
using System.Reflection;
using UnityEditor.Compilation;
using UnityEngine.TestTools;
using Client.Framework.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;
using Object=UnityEngine.Object;
namespace Client.Tests.EditMode
{
    [Category("U4")]
    public sealed class UIBindingGeneratorTests
    {
        GameObject root; UnityEngine.SceneManagement.Scene fixtureScene; string folder,output,hand;
        [SetUp] public void Setup()
        {
            UnityEditor.AssetDatabase.DisallowAutoRefresh();
            fixtureScene=UnityEditor.SceneManagement.EditorSceneManager.NewPreviewScene();
            root=new GameObject("UIGeneratorFixture",typeof(RectTransform),typeof(UIBindings),typeof(Button));
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(root,fixtureScene);
            root.GetComponent<UIBindings>().Configure(new[]{new UIBindings.Binding{Id="Action",Value=root.GetComponent<Button>(),ComponentType=typeof(Button).AssemblyQualifiedName}},new[]{new UIBindings.ArrayBinding{Id="Items",Values=new Component[]{root.transform},ComponentType=typeof(RectTransform).AssemblyQualifiedName}});
            folder="Assets/Scripts/Game/GeneratorFixture"+Guid.NewGuid().ToString("N"); Directory.CreateDirectory(folder+"/Generated");
            output=folder+"/Generated/UIGeneratorFixture.G.Bindings.cs"; hand=folder+"/UIGeneratorFixture.cs";
            File.WriteAllText(hand,"namespace Client.Game { public sealed partial class UIGeneratorFixture { public string Keep => \"manual\"; } }\n");
        }
        [TearDown] public void Cleanup(){ UnityEditor.SceneManagement.EditorSceneManager.ClosePreviewScene(fixtureScene); if(!Path.GetFullPath(folder).StartsWith(Path.GetFullPath("Assets/Scripts/Game/GeneratorFixture"),StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("Refuse cleanup outside the owned fixture prefix."); UnityEditor.AssetDatabase.DeleteAsset(folder); if(Directory.Exists(folder))Directory.Delete(folder,true); UnityEditor.AssetDatabase.AllowAutoRefresh(); }
        object Generate(string path=null,string className="UIGeneratorFixture") => UIAuthoringTests.Tool("UIBindingGenerator").GetMethod("Generate").Invoke(null,new object[]{root.GetComponent<UIBindings>(),"Client.Game",className,path??output});
        static bool Success(object result)=>(bool)result.GetType().GetProperty("Success").GetValue(result);
        [Test] public void GeneratorPreservesHandwrittenPartial()
        { string before=File.ReadAllText(hand); Assert.That(Success(Generate()),Is.True); Assert.That(File.ReadAllText(output).Replace("\r\n","\n"),Is.EqualTo(File.ReadAllText("Assets/Tests/Fixtures/UIBindings/Expected/UIGeneratorFixture.G.Bindings.cs.txt").Replace("\r\n","\n"))); Assert.That(File.ReadAllText(hand),Is.EqualTo(before)); Assert.That(File.ReadAllText(output),Does.Contain("sealed partial class UIGeneratorFixture").And.Contain("BindComponents").And.Contain("ClearBindings")); }
        [UnityTest] public IEnumerator GeneratedAndHandwrittenCompileAsOnePartialType()
        {
            Assert.That(Success(Generate()),Is.True);
            string artifact="Builds/Tests/U4/compile/"+Guid.NewGuid().ToString("N");Directory.CreateDirectory(artifact);
            string dll=Path.GetFullPath(artifact+"/GeneratedFixture.dll");
            CompilerMessage[] messages=null;
            var builder=new AssemblyBuilder(dll,new[]{Path.GetFullPath(hand),Path.GetFullPath(output)})
            {
                flags=AssemblyBuilderFlags.EditorAssembly,
                referencesOptions=ReferencesOptions.UseEngineModules,
                additionalReferences=new[]{typeof(UIBindings).Assembly.Location,typeof(Button).Assembly.Location}
            };
            builder.buildFinished+=(path,result)=>messages=result;
            Assert.That(builder.Build(),Is.True);
            while(builder.status!=AssemblyBuilderStatus.Finished)yield return null;
            Assert.That(messages.Where(m=>m.type==CompilerMessageType.Error),Is.Empty,string.Join("\n",messages.Select(m=>m.message)));
            var type=System.Reflection.Assembly.LoadFile(dll).GetType("Client.Game.UIGeneratorFixture",true);
            var instance=Activator.CreateInstance(type);
            type.GetMethod("BindComponents").Invoke(instance,new object[]{root.GetComponent<UIBindings>()});
            Assert.That(type.GetProperty("Action").GetValue(instance),Is.SameAs(root.GetComponent<Button>()));
            Assert.That(((RectTransform[])type.GetProperty("Items").GetValue(instance))[0],Is.SameAs(root.transform));
            Assert.That(type.GetProperty("Keep").GetValue(instance),Is.EqualTo("manual"));
            type.GetMethod("ClearBindings").Invoke(instance,null);
            Assert.That(type.GetProperty("Action").GetValue(instance),Is.Null);
            File.Copy(hand,artifact+"/UIGeneratorFixture.cs");File.Copy(output,artifact+"/UIGeneratorFixture.G.Bindings.cs");
        }
        [Test] public void SecondGenerationHasNoDiff()
        { Assert.That(Success(Generate()),Is.True); string before=File.ReadAllText(output); var time=File.GetLastWriteTimeUtc(output); Assert.That(Success(Generate()),Is.True); Assert.That(File.ReadAllText(output),Is.EqualTo(before));Assert.That(File.GetLastWriteTimeUtc(output),Is.EqualTo(time)); }
        [TestCase("keyword")][TestCase("missing")][TestCase("suffix")][TestCase("name")][TestCase("case")][TestCase("outside")][TestCase("invalid-name")]
        public void FailedGenerationKeepsPreviousBytes(string broken)
        {
            File.WriteAllText(output,"previous bytes\n"); var binding=root.GetComponent<UIBindings>().Singles[0];
            if(broken=="keyword")binding.Id="class"; if(broken=="missing")binding.Value=null;
            if(broken=="case")binding.Id="items";
            string path=broken=="suffix"?folder+"/UIGeneratorFixture.cs":broken=="outside"?"Assets/Bad/Generated/UIGeneratorFixture.G.Bindings.cs":output;
            Assert.That(Success(Generate(path,broken=="invalid-name"?"UiWrong":broken=="name"?"UIWrong":"UIGeneratorFixture")),Is.False);
            Assert.That(File.ReadAllText(output),Is.EqualTo("previous bytes\n"));
        }
    }
}
