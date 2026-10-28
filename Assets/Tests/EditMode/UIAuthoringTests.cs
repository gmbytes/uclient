using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Collections;
using UnityEditor.Compilation;
using UnityEngine.TestTools;
using UnityEngine.SceneManagement;
using Client.Framework.UI;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace Client.Tests.EditMode
{
    [Category("U4")]
    public sealed class UIAuthoringTests
    {
        string directory, codeDirectory, name;
        UIViewDefinitions catalog;
        UIViewDefinition[] original;
        [SetUp] public void Setup()
        {
            AssetDatabase.DisallowAutoRefresh();
            name="UIAuthoringFixture"+Guid.NewGuid().ToString("N");
            directory="Assets/Tests/Generated/"+name+"/"+name.Substring(2);
            codeDirectory="Assets/Scripts/Game/"+name.Substring(2);
            catalog=AssetDatabase.LoadAssetAtPath<UIViewDefinitions>("Assets/Content/UI/Common/UIViewDefinitions.asset");
            original=catalog.Definitions.ToArray();
        }
        [TearDown] public void Cleanup()
        {
            catalog.Configure(original); EditorUtility.SetDirty(catalog); AssetDatabase.SaveAssetIfDirty(catalog);
            Assert.That(Path.GetFullPath("Assets/Tests/Generated/"+name).StartsWith(Path.GetFullPath("Assets/Tests/Generated")+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase),Is.True);
            Assert.That(Path.GetFullPath(codeDirectory).StartsWith(Path.GetFullPath("Assets/Scripts/Game/AuthoringFixture"),StringComparison.OrdinalIgnoreCase),Is.True);
            AssetDatabase.DeleteAsset("Assets/Tests/Generated/"+name);
            AssetDatabase.DeleteAsset(codeDirectory);
            AssetDatabase.AllowAutoRefresh();
        }
        internal static Type Tool(string name)
        {
            var type=Type.GetType("Client.Editor.UI."+name+", Client.Editor");
            Assert.That(type,Is.Not.Null,"U4 requires "+name); return type;
        }
        UIViewDefinition Create() => (UIViewDefinition)Tool("UIWindowCreator").GetMethod("Create").Invoke(null,new object[]{name,directory});
        [Test, Category("U5")] public void CreatorAndRegeneratorKeepMatchingFeatureNamespaceAndHandBytes()
        {
            var definition=Create();
            string hand=codeDirectory+"/"+name+".cs",output=codeDirectory+"/Generated/"+name+".G.Bindings.cs";
            byte[] originalHand=File.ReadAllBytes(hand),originalOutput=File.ReadAllBytes(output);
            Assert.That(File.ReadAllText(hand),Does.Contain("namespace Client.Game."+name.Substring(2)+"\n"));
            var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(definition.PrefabAddress);
            var result=Tool("UIBindingGenerator").GetMethod("Regenerate").Invoke(null,new object[]{prefab.GetComponent<UIBindings>(),directory});
            Assert.That((bool)result.GetType().GetProperty("Success").GetValue(result),Is.True);
            Assert.That(File.ReadAllBytes(hand),Is.EqualTo(originalHand));Assert.That(File.ReadAllBytes(output),Is.EqualTo(originalOutput));
            Assert.That(Validate(definition),Does.Not.Contain("Error"));
        }
        string Validate(UIViewDefinition definition)
        {
            var report=Tool("UIAuthoringValidator").GetMethod("Validate").Invoke(null,new object[]{definition});
            return report.ToString();
        }
        [Test] public void CreatesWindowWithSeparateCanvasAndDefinition()
        {
            var definition=Create(); var root=AssetDatabase.LoadAssetAtPath<GameObject>(definition.PrefabAddress);
            Assert.That(root.name,Is.EqualTo(name)); Assert.That(root.GetComponent<Canvas>(),Is.Null);
            var bridge=root.GetComponent<UIViewBridge>(); Assert.That(bridge.RootCanvas.transform.parent,Is.SameAs(root.transform));
            Assert.That(bridge.RootCanvas.name,Is.EqualTo("UINode"));
            Assert.That(root.GetComponentsInChildren<CanvasScaler>(true),Is.Empty);
            Assert.That(root.transform.Find("UINode/Background").GetComponent<Image>().raycastTarget,Is.False);
            Assert.That(root.transform.Find("UINode/SafeArea/Static"),Is.Not.Null);
            Assert.That(root.transform.Find("UINode/SafeArea/Dynamic"),Is.Not.Null);
            Assert.That(root.transform.Find("PoolNode").gameObject.activeSelf,Is.False);
            Assert.That(catalog.Definitions.Any(d=>d.ViewID==name),Is.True);
            Assert.That(AssetDatabase.LoadMainAssetAtPath(directory+"/"+name+".Config.asset"),Is.Not.Null);
            Assert.That(AssetDatabase.LoadMainAssetAtPath(directory+"/"+name+".Text.asset"),Is.Not.Null);
        }
        [Test] public void RejectsWindowOwnedEventSystem()
        {
            var definition=Create(); Edit(definition,r=>r.AddComponent<EventSystem>());
            Assert.That(Validate(definition),Does.Contain("UI_EVENT_SYSTEM"));
        }
        [Test] public void RejectsDuplicateViewAndBindingIds()
        {
            var definition=Create(); catalog.Configure(original.Concat(new[]{definition,definition}).ToArray());
            Edit(definition,r=>{ var b=r.GetComponent<UIBindings>(); b.Configure(new[]{Entry("Action",r.transform),Entry("action",r.transform)},null); });
            Assert.That(Validate(definition),Does.Contain("UI_DUPLICATE_VIEW").And.Contain("UI_BINDING_ID"));
        }
        [TestCase("outside")][TestCase("type")]
        public void RejectsOutOfRootOrWrongTypeReference(string mode)
        {
            var definition=Create(); var externalScene=UnityEditor.SceneManagement.EditorSceneManager.NewPreviewScene(); var other=new GameObject("External");SceneManager.MoveGameObjectToScene(other,externalScene);
            try { Edit(definition,r=>r.GetComponent<UIBindings>().Configure(new[]{new UIBindings.Binding{Id="Action",Value=mode=="outside"?other.transform:r.transform,ComponentType=(mode=="type"?typeof(Button):typeof(Transform)).AssemblyQualifiedName}},null));
                Assert.That(Validate(definition),Does.Contain("UI_BINDING")); }
            finally { UnityEditor.SceneManagement.EditorSceneManager.ClosePreviewScene(externalScene); }
        }
        [Test] public void NestedWidgetHasNoWindowBridge()
        {
            var definition=Create(); Edit(definition,r=>{var child=new GameObject("Widget",typeof(RectTransform),typeof(UIBindings));child.transform.SetParent(r.transform,false);});
            Assert.That(Validate(definition),Does.Not.Contain("Error"));
            Edit(definition,r=>r.transform.Find("Widget").gameObject.AddComponent<UIViewBridge>());
            Assert.That(Validate(definition),Does.Contain("UI_BINDING"));
        }
        [Test] public void SameUINameAcrossCodePrefabAndConfig()
        {
            var definition=Create(); Assert.That(definition.ViewID,Is.EqualTo(name));
            Assert.That(File.ReadAllText(codeDirectory+"/"+name+".cs"),Does.Contain("sealed partial class "+name));
            definition.ViewID="UIDrift"; Assert.That(Validate(definition),Does.Contain("UI_NAME"));
        }
        [Test] public void ExistingWindowIsNeverOverwritten()
        {
            var definition=Create(); string hash=File.ReadAllText(definition.PrefabAddress);
            Assert.Throws<TargetInvocationException>(()=>Create());
            Assert.That(File.ReadAllText(definition.PrefabAddress),Is.EqualTo(hash));
        }
        [Test] public void CreationRejectsInvalidUIName()
        { Assert.Throws<TargetInvocationException>(()=>Tool("UIWindowCreator").GetMethod("Create").Invoke(null,new object[]{"UiWrong",directory})); }
        [Test] public void GeneratedDriftAndMissingTMPFontBlockValidation()
        {
            var definition=Create();
            File.AppendAllText(codeDirectory+"/Generated/"+name+".G.Bindings.cs","// drift\n");
            Edit(definition,r=>{var text=new GameObject("Label",typeof(RectTransform));text.SetActive(false);text.AddComponent<TMPro.TextMeshProUGUI>();text.transform.SetParent(r.transform.Find("UINode/SafeArea/Static"),false);text.GetComponent<TMPro.TextMeshProUGUI>().font=null;});
            Assert.That(Validate(definition),Does.Contain("UI_GENERATED_DRIFT").And.Contain("UI_TMP_FONT"));
            PreparePreviewScene();
            var exception=Assert.Throws<TargetInvocationException>(()=>Activator.CreateInstance(Tool("UIPreviewSession"),new object[]{definition}));
            Assert.That(exception.InnerException.Message,Does.Contain("UI_PREVIEW_VALIDATION"));
            Assert.That(SceneManager.GetSceneByPath("Assets/Scenes/UIEdit.unity").isLoaded,Is.False);
        }
        [Test] public void CreateReusesExistingLayoutWithoutChangingBytesOrGuid()
        {
            Directory.CreateDirectory(directory);AssetDatabase.Refresh();
            string path=directory+"/"+name+".Layout.asset";
            var layout=ScriptableObject.CreateInstance<AdaptiveLayoutDefinition>();layout.Portrait.MaximumFormWidth=611;AssetDatabase.CreateAsset(layout,path);
            string before=File.ReadAllText(path),guid=AssetDatabase.AssetPathToGUID(path);
            var definition=Create();Assert.That(definition.LayoutDefinition,Is.SameAs(layout));
            Assert.That(File.ReadAllText(path),Is.EqualTo(before));Assert.That(AssetDatabase.AssetPathToGUID(path),Is.EqualTo(guid));
        }
        [UnityTest] public IEnumerator CreationRegistrationDragBindingGenerationCompileValidationAndDualPreviewWorkflow()
        {
            var definition=Create();string hand=codeDirectory+"/"+name+".cs",output=codeDirectory+"/Generated/"+name+".G.Bindings.cs";
            string beforeHand=File.ReadAllText(hand),beforeGenerated=File.ReadAllText(output),guid=AssetDatabase.AssetPathToGUID(definition.PrefabAddress);
            Edit(definition,r=>
            {
                var action=new GameObject("ActionButton",typeof(RectTransform),typeof(Image),typeof(Button));action.transform.SetParent(r.transform.Find("UINode/SafeArea/Dynamic/Content"),false);
                Tool("UIBindingsEditor").GetMethod("AddReference").Invoke(null,new object[]{r.GetComponent<UIBindings>(),action.GetComponent<Button>(),false});
                Tool("UIBindingsEditor").GetMethod("AddReference").Invoke(null,new object[]{r.GetComponent<UIBindings>(),action.GetComponent<Button>(),true});
            });
            var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(definition.PrefabAddress);
            string namespaceName="Client.Game."+name.Substring(2);
            var result=Tool("UIBindingGenerator").GetMethod("Generate").Invoke(null,new object[]{prefab.GetComponent<UIBindings>(),namespaceName,name,output});
            Assert.That((bool)result.GetType().GetProperty("Success").GetValue(result),Is.True);
            Assert.That(File.ReadAllText(hand),Is.EqualTo(beforeHand));Assert.That(File.ReadAllText(output),Is.Not.EqualTo(beforeGenerated));
            string after=File.ReadAllText(output);Tool("UIBindingGenerator").GetMethod("Generate").Invoke(null,new object[]{prefab.GetComponent<UIBindings>(),namespaceName,name,output});
            Assert.That(File.ReadAllText(output),Is.EqualTo(after));Assert.That(AssetDatabase.AssetPathToGUID(definition.PrefabAddress),Is.EqualTo(guid));
            string evidence="Builds/Tests/U4/workflow/"+name;Directory.CreateDirectory(evidence);
            string dll=Path.GetFullPath(evidence+"/"+name+".dll");CompilerMessage[] messages=null;
            var builder=new AssemblyBuilder(dll,new[]{Path.GetFullPath(hand),Path.GetFullPath(output)}){flags=AssemblyBuilderFlags.EditorAssembly,referencesOptions=ReferencesOptions.UseEngineModules,additionalReferences=new[]{typeof(UIBindings).Assembly.Location,typeof(Button).Assembly.Location}};
            builder.buildFinished+=(path,compiled)=>messages=compiled;Assert.That(builder.Build(),Is.True);while(builder.status!=AssemblyBuilderStatus.Finished)yield return null;
            Assert.That(messages.Where(m=>m.type==CompilerMessageType.Error),Is.Empty,string.Join("\n",messages.Select(m=>m.message)));
            var type=System.Reflection.Assembly.LoadFile(dll).GetType(namespaceName+"."+name,true);var view=Activator.CreateInstance(type);
            type.GetMethod("BindComponents").Invoke(view,new object[]{prefab.GetComponent<UIBindings>()});Assert.That(type.GetProperty("ActionButton").GetValue(view),Is.SameAs(prefab.GetComponent<UIBindings>().Resolve("ActionButton")));
            Assert.That(Validate(definition),Does.Not.Contain("Error"));
            PreparePreviewScene();
            var session=Activator.CreateInstance(Tool("UIPreviewSession"),new object[]{definition});
            try
            {
                foreach(var size in new[]{new Vector2Int(720,1280),new Vector2Int(1280,720)})
                {
                    session.GetType().GetMethod("Apply").Invoke(session,new object[]{new ScreenMetrics(size.x,size.y,new Rect(0,0,size.x,size.y)),false,Rect.zero,null});
                    var texture=(RenderTexture)session.GetType().GetProperty("Texture").GetValue(session);
                    if(SystemInfo.graphicsDeviceType!=UnityEngine.Rendering.GraphicsDeviceType.Null)
                    {
                        var previous=RenderTexture.active;RenderTexture.active=texture;var image=new Texture2D(size.x,size.y,TextureFormat.RGB24,false);
                        image.ReadPixels(new Rect(0,0,size.x,size.y),0,0);image.Apply();File.WriteAllBytes(evidence+"/"+size.x+"x"+size.y+".png",image.EncodeToPNG());Object.DestroyImmediate(image);RenderTexture.active=previous;
                    }
                }
                File.Copy(hand,evidence+"/"+name+".cs");File.Copy(output,evidence+"/"+name+".G.Bindings.cs");
                File.Copy(definition.PrefabAddress,evidence+"/"+name+".prefab");File.Copy(definition.PrefabAddress+".meta",evidence+"/"+name+".prefab.meta");
                foreach(string suffix in new[]{".Config.asset",".Layout.asset",".Text.asset"}){string asset=directory+"/"+name+suffix;File.Copy(asset,evidence+"/"+name+suffix);File.Copy(asset+".meta",evidence+"/"+name+suffix+".meta");}
                File.Copy(AssetDatabase.GetAssetPath(catalog),evidence+"/UIViewDefinitions.asset");
                File.WriteAllText(evidence+"/before.cs",beforeGenerated);
                File.WriteAllText(evidence+"/workflow.txt","Prefab="+definition.PrefabAddress+"\nGUID="+guid+"\nHand preserved=True\nGenerated second pass identical=True\nValidation="+Validate(definition)+"\n");
            }
            finally{((IDisposable)session).Dispose();}
        }
        [Test] public void PreviewRefusesDirtyEditingSceneWithoutDiscardingIt()
        {
            var definition=Create();PreparePreviewScene();var scene=SceneManager.GetActiveScene();
            string path=scene.path;var ids=scene.GetRootGameObjects().Select(o=>o.GetInstanceID()).ToArray();
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(scene);
            var exception=Assert.Throws<TargetInvocationException>(()=>Activator.CreateInstance(Tool("UIPreviewSession"),new object[]{definition}));
            Assert.That(exception.InnerException.Message,Does.Contain("UI_SAVE_REQUIRED"));
            Assert.That(scene.isDirty,Is.True);Assert.That(scene.path,Is.EqualTo(path));Assert.That(scene.GetRootGameObjects().Select(o=>o.GetInstanceID()),Is.EqualTo(ids));
            Assert.That(UnityEditor.SceneManagement.EditorSceneManager.SaveScene(scene,path),Is.True);
        }
        [Test] public void PreviewDoesNotDirtySavedEditingSceneAndCanReopen()
        {
            var definition=Create();PreparePreviewScene();var editing=SceneManager.GetActiveScene();
            var session=Activator.CreateInstance(Tool("UIPreviewSession"),new object[]{definition});
            try{Assert.That(editing.isDirty,Is.False,"Preview must create every transient object in its owned scene.");}
            finally{((IDisposable)session).Dispose();}
            var second=Activator.CreateInstance(Tool("UIPreviewSession"),new object[]{definition});((IDisposable)second).Dispose();
            Assert.That(editing.isDirty,Is.False);
        }
        [Test] public void PreviewUsesSameInstanceAndSharedPoliciesAcrossBothLayouts()
        {
            var definition=Create(); PreparePreviewScene(); var session=Activator.CreateInstance(Tool("UIPreviewSession"),new object[]{definition});
            try
            {
                var apply=session.GetType().GetMethod("Apply");
                apply.Invoke(session,new object[]{new ScreenMetrics(720,1280,new Rect(0,40,720,1200)),false,Rect.zero,null});
                var instance=(GameObject)session.GetType().GetProperty("Instance").GetValue(session); int id=instance.GetInstanceID();
                var canvas=(AdaptiveCanvas)session.GetType().GetProperty("Canvas").GetValue(session);
                Assert.That(canvas.GetComponent<CanvasScaler>().referenceResolution,Is.EqualTo(new Vector2(1080,1920)));
                Assert.That(canvas.GetComponent<ScreenLayoutComponent>().Service.Current.Width,Is.EqualTo(720));
                var brand=(RectTransform)instance.transform.Find("UINode/SafeArea/Static");Assert.That(brand.anchorMin,Is.EqualTo(new Vector2(0,.75f)));
                apply.Invoke(session,new object[]{new ScreenMetrics(1280,720,new Rect(40,0,1200,720)),true,Rect.zero,null});
                Assert.That(instance.GetInstanceID(),Is.EqualTo(id));
                Assert.That(canvas.GetComponent<CanvasScaler>().referenceResolution,Is.EqualTo(new Vector2(1920,1080)));
                Assert.That(brand.anchorMax,Is.EqualTo(new Vector2(.35f,1)));
                Assert.That(SceneManager.GetSceneByPath("Assets/Scenes/UIEdit.unity").GetRootGameObjects().SelectMany(r=>r.GetComponentsInChildren<EventSystem>(true)).Count(),Is.EqualTo(1));
                Assert.That(instance.GetComponent<UIViewBridge>().Keyboard,Is.Not.Null);
                Assert.That(instance.GetComponentInChildren<ScrollRect>().movementType,Is.EqualTo(ScrollRect.MovementType.Unrestricted));
            }
            finally { ((IDisposable)session).Dispose(); }
        }
        [Test] public void PreviewWindowImageryFillsInsetsAndLocalInteractionsAreSafeOnce()
        {
            var definition=Create();definition.Modal=true;
            Edit(definition,r=>
            {
                r.transform.Find("UINode/Background").GetComponent<Image>().color=Color.red;
                var mask=new GameObject("Mask",typeof(RectTransform),typeof(Image),typeof(Button));mask.GetComponent<Image>().color=Color.clear;mask.transform.SetParent(r.transform.Find("UINode"),false);mask.transform.SetSiblingIndex(1);
                var rect=(RectTransform)mask.transform;rect.anchorMin=Vector2.zero;rect.anchorMax=Vector2.one;rect.offsetMin=rect.offsetMax=Vector2.zero;
                var input=new GameObject("Input",typeof(RectTransform),typeof(InputField));input.transform.SetParent(r.transform.Find("UINode/SafeArea/Dynamic/Content"),false);input.GetComponent<InputField>().text="preserved";
                var bridge=new SerializedObject(r.GetComponent<UIViewBridge>());bridge.FindProperty("m_Mask").objectReferenceValue=mask.GetComponent<Button>();bridge.ApplyModifiedPropertiesWithoutUndo();
            });
            PreparePreviewScene();var session=Activator.CreateInstance(Tool("UIPreviewSession"),new object[]{definition});
            try
            {
                var global=(AdaptiveCanvas)session.GetType().GetProperty("Canvas").GetValue(session);global.transform.Find("Background").GetComponent<Image>().color=Color.green;
                var instance=(GameObject)session.GetType().GetProperty("Instance").GetValue(session);int identity=instance.GetInstanceID();
                var input=(RectTransform)instance.transform.Find("UINode/SafeArea/Dynamic/Content/Input");var eventSystem=SceneManager.GetSceneByPath("Assets/Scenes/UIEdit.unity").GetRootGameObjects().SelectMany(r=>r.GetComponentsInChildren<EventSystem>()).Single();eventSystem.SetSelectedGameObject(input.gameObject);
                var cases=new[]{new ScreenMetrics(720,1280,new Rect(40,90,650,1100)),new ScreenMetrics(1280,720,new Rect(80,30,1120,650)),new ScreenMetrics(1280,720,new Rect(0,0,1180,720)),new ScreenMetrics(720,1280,new Rect(60,0,660,1280))};
                foreach(var metrics in cases)
                {
                    session.GetType().GetMethod("Apply").Invoke(session,new object[]{metrics,false,Rect.zero,input});var rootRect=(RectTransform)global.transform;var full=new Rect(0,0,metrics.Width,metrics.Height);
                    AssertPreviewRect(PreviewPixels((RectTransform)instance.transform,rootRect,metrics),full,"window root full frame");
                    AssertPreviewRect(PreviewPixels((RectTransform)instance.transform.Find("UINode"),rootRect,metrics),full,"UINode full frame");
                    AssertPreviewRect(PreviewPixels((RectTransform)instance.transform.Find("UINode/Background"),rootRect,metrics),full,"contrasting window background full frame");
                    AssertPreviewRect(PreviewPixels((RectTransform)instance.transform.Find("UINode/Mask"),rootRect,metrics),full,"mask full frame");
                    AssertPreviewRect(PreviewPixels((RectTransform)instance.transform.Find("UINode/SafeArea"),rootRect,metrics),metrics.SafeArea,"local safe bounds exactly once");
                    var viewport=PreviewPixels((RectTransform)instance.transform.Find("UINode/SafeArea/Dynamic"),rootRect,metrics);Assert.That(viewport.xMin,Is.GreaterThanOrEqualTo(metrics.SafeArea.xMin-.1f));Assert.That(viewport.xMax,Is.LessThanOrEqualTo(metrics.SafeArea.xMax+.1f));Assert.That(viewport.yMin,Is.GreaterThanOrEqualTo(metrics.SafeArea.yMin-.1f));Assert.That(viewport.yMax,Is.LessThanOrEqualTo(metrics.SafeArea.yMax+.1f));
                    Assert.That(instance.GetInstanceID(),Is.EqualTo(identity));Assert.That(instance.GetComponentsInChildren<CanvasScaler>(true),Is.Empty);Assert.That(eventSystem.currentSelectedGameObject,Is.SameAs(input.gameObject));Assert.That(input.GetComponent<InputField>().text,Is.EqualTo("preserved"));
                    if(SystemInfo.graphicsDeviceType!=UnityEngine.Rendering.GraphicsDeviceType.Null)
                    {
                        var texture=(RenderTexture)session.GetType().GetProperty("Texture").GetValue(session);var previous=RenderTexture.active;RenderTexture.active=texture;var image=new Texture2D(metrics.Width,metrics.Height,TextureFormat.RGB24,false);image.ReadPixels(full,0,0);image.Apply();
                        Assert.That(image.GetPixel(2,2).r,Is.GreaterThan(.9f));Assert.That(image.GetPixel(2,2).g,Is.LessThan(.1f));Assert.That(image.GetPixel(metrics.Width-3,metrics.Height-3).r,Is.GreaterThan(.9f));
                        Directory.CreateDirectory("Builds/Tests/U4/fix1-preview");File.WriteAllBytes("Builds/Tests/U4/fix1-preview/"+metrics.Width+"x"+metrics.Height+"-"+metrics.SafeArea.xMin+"-"+metrics.SafeArea.width+".png",image.EncodeToPNG());Object.DestroyImmediate(image);RenderTexture.active=previous;
                    }
                }
            }
            finally{((IDisposable)session).Dispose();}
        }
        static Rect PreviewPixels(RectTransform target,RectTransform rootRect,ScreenMetrics value)
        {var points=new Vector3[4];target.GetWorldCorners(points);var min=(Vector2)rootRect.InverseTransformPoint(points[0])-rootRect.rect.min;var max=(Vector2)rootRect.InverseTransformPoint(points[2])-rootRect.rect.min;return Rect.MinMaxRect(min.x/rootRect.rect.width*value.Width,min.y/rootRect.rect.height*value.Height,max.x/rootRect.rect.width*value.Width,max.y/rootRect.rect.height*value.Height);}
        static void AssertPreviewRect(Rect actual,Rect expected,string reason)
        {Assert.That(actual.xMin,Is.EqualTo(expected.xMin).Within(.1f),reason);Assert.That(actual.yMin,Is.EqualTo(expected.yMin).Within(.1f),reason);Assert.That(actual.xMax,Is.EqualTo(expected.xMax).Within(.1f),reason);Assert.That(actual.yMax,Is.EqualTo(expected.yMax).Within(.1f),reason);}
        void PreparePreviewScene()
        {
            var scene=SceneManager.GetActiveScene();
            File.WriteAllText("Builds/Tests/U4/preview-scene-state.txt",string.Join("\n",Enumerable.Range(0,SceneManager.sceneCount).Select(i=>{var s=SceneManager.GetSceneAt(i);return s.name+" | "+s.path+" | dirty="+s.isDirty+" | roots="+string.Join(",",s.GetRootGameObjects().Select(r=>r.name+":"+r.hideFlags));})));
            if(Application.isBatchMode && (string.IsNullOrEmpty(scene.path) || scene.path.StartsWith("Assets/Tests/Generated/UIAuthoringFixture",StringComparison.Ordinal)) && scene.GetRootGameObjects().All(r=>r.name=="Main Camera" && r.GetComponent<Camera>()!=null || r.name=="Directional Light" && r.GetComponent<Light>()!=null))
            {
                string path="Assets/Tests/Generated/"+name+"/Editing.unity";Directory.CreateDirectory(Path.GetDirectoryName(path));
                Assert.That(UnityEditor.SceneManagement.EditorSceneManager.SaveScene(scene,path),Is.True);
            }
        }
        static UIBindings.Binding Entry(string id,Component value)=>new UIBindings.Binding{Id=id,Value=value,ComponentType=typeof(Transform).AssemblyQualifiedName};
        static void Edit(UIViewDefinition definition,Action<GameObject> edit)
        { var root=PrefabUtility.LoadPrefabContents(definition.PrefabAddress); try{edit(root);PrefabUtility.SaveAsPrefabAsset(root,definition.PrefabAddress);}finally{PrefabUtility.UnloadPrefabContents(root);} }
    }
}
