using System;
using System.Reflection;
using Client.Framework.UI;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace Client.Tests.EditMode
{
    [Category("U3")]
    public sealed class UIBindingTests
    {
        private GameObject root;
        private GameObject external;
        [TearDown] public void Cleanup() { if(root!=null) Object.DestroyImmediate(root); if(external!=null) Object.DestroyImmediate(external); }
        private static Type Required(string name, string assembly = "Client.Framework")
        {
            var type = Type.GetType(name + ", " + assembly); Assert.That(type, Is.Not.Null, "U3 requires " + name); return type;
        }
        private static void Set(object value,string field,object data) => value.GetType().GetField(field).SetValue(value,data);
        private object Bindings()
        {
            root = new GameObject("Component", typeof(RectTransform));
            return root.AddComponent(Required("Client.Framework.UI.UIBindings"));
        }
        private static object Entry(string kind,string id,object value,string type,bool required=true)
        {
            var item = Activator.CreateInstance(Required("Client.Framework.UI.UIBindings").GetNestedType(kind));
            Set(item,"Id",id); Set(item,kind=="Binding"?"Value":"Values",value); Set(item,"ComponentType",type); Set(item,"Required",required); return item;
        }
        private static Array Entries(params object[] items)
        { var values=Array.CreateInstance(items[0].GetType(),items.Length); for(int i=0;i<items.Length;i++) values.SetValue(items[i],i); return values; }
        private static void Configure(object bindings,Array singles,Array arrays) => bindings.GetType().GetMethod("Configure").Invoke(bindings,new object[]{singles,arrays});
        private static string Errors(GameObject target)
        {
            var result = Required("Client.Editor.UI.UIBindingValidator","Client.Editor").GetMethod("Validate").Invoke(null,new object[]{target,null});
            return string.Join("\n",(System.Collections.Generic.IEnumerable<string>)result);
        }

        // Omitted explicit references, duplicate IDs, wrong component types and null array elements must block authoring/builds.
        [TestCase("missing", "required")]
        [TestCase("type", "type")]
        [TestCase("duplicate", "duplicate")]
        [TestCase("array", "required")]
        [TestCase("font", "font")]
        public void ValidatorRejectsBrokenReferencesTypesDuplicatesAndFonts(string broken,string expected)
        {
            var bindings=Bindings(); var button=root.AddComponent<Button>();
            var good=Entry("Binding","Action",button,typeof(Button).AssemblyQualifiedName);
            if(broken=="missing") Set(good,"Value",null);
            if(broken=="type") Set(good,"ComponentType",typeof(InputField).AssemblyQualifiedName);
            var singles=broken=="duplicate"?Entries(good,Entry("Binding","Action",button,typeof(Button).AssemblyQualifiedName)):Entries(good);
            var arrays=broken=="array"?Entries(Entry("ArrayBinding","Items",new Component[]{button,null},typeof(Button).AssemblyQualifiedName)):null;
            Configure(bindings,singles,arrays);
            if(broken=="font") root.AddComponent<Text>().font=null;
            Assert.That(Errors(root).ToLowerInvariant(),Does.Contain(expected));
        }

        [Test]
        public void ExplicitSingleAndArrayBindingsResolveWithoutHierarchySearch()
        {
            var bindings=Bindings(); var button=root.AddComponent<Button>();
            Configure(bindings,Entries(Entry("Binding","Action",button,typeof(Button).AssemblyQualifiedName)),Entries(Entry("ArrayBinding","Items",new Component[]{button},typeof(Button).AssemblyQualifiedName)));
            Assert.That(bindings.GetType().GetMethod("Resolve").Invoke(bindings,new object[]{"Action"}),Is.SameAs(button));
            var many=(System.Collections.Generic.IReadOnlyList<Component>)bindings.GetType().GetMethod("ResolveMany").Invoke(bindings,new object[]{"Items"});
            Assert.That(many.Count,Is.EqualTo(1)); Assert.That(many[0],Is.SameAs(button)); Assert.That(Errors(root),Is.Empty);
        }

        [Test]
        public void MainWindowValidationRequiresSerializedHostReferences()
        {
            Bindings(); root.AddComponent(Required("Client.Framework.UI.UIViewBridge"));
            Assert.That(Errors(root),Does.Contain("RootCanvas").And.Contain("RootCanvasGroup").And.Contain("Bindings"));
        }

        private UIViewBridge MainWindow()
        {
            var bindings=(UIBindings)Bindings(); root.name="UIWindow";
            var node=new GameObject("UINode",typeof(RectTransform),typeof(Canvas),typeof(CanvasGroup),typeof(GraphicRaycaster));
            node.transform.SetParent(root.transform,false);
            var rect=(RectTransform)node.transform; rect.anchorMin=Vector2.zero; rect.anchorMax=Vector2.one; rect.offsetMin=rect.offsetMax=Vector2.zero;
            var safe=new GameObject("SafeArea",typeof(RectTransform),typeof(SafeAreaRoot));safe.transform.SetParent(node.transform,false);
            var action=new GameObject("Action",typeof(RectTransform),typeof(Button)); action.transform.SetParent(safe.transform,false);
            bindings.Configure(new[]{new UIBindings.Binding{Id="Action",Value=action.GetComponent<Button>(),ComponentType=typeof(Button).AssemblyQualifiedName}},null);
            var bridge=root.AddComponent<UIViewBridge>();
            bridge.Configure(node.GetComponent<Canvas>(),node.GetComponent<CanvasGroup>(),bindings,localSafeArea:safe.GetComponent<SafeAreaRoot>());
            return bridge;
        }

        // The authoring/build boundary must admit the stable root + child Canvas template.
        [Test]
        public void CompliantHierarchyPassesValidationAndBuildGate()
        {
            const string path="Assets/Content/UI/Common/U3ValidHierarchyFixture.prefab";
            var bridge=MainWindow();
            Assert.That(root.GetComponent<Canvas>(),Is.Null);
            Assert.That(bridge.RootCanvas.transform.parent,Is.EqualTo(root.transform));
            Assert.That(Errors(root),Is.Empty);
            try
            {
                PrefabUtility.SaveAsPrefabAsset(root,path);
                var validator=Activator.CreateInstance(Required("Client.Editor.UI.UIBindingValidator","Client.Editor"));
                Assert.DoesNotThrow(()=>validator.GetType().GetMethod("OnPreprocessBuild").Invoke(validator,new object[]{null}));
            }
            finally { AssetDatabase.DeleteAsset(path); }
        }

        [TestCase("all-on-root","Canvas")]
        [TestCase("external-canvas","RootCanvas")]
        [TestCase("external-group","RootCanvasGroup")]
        [TestCase("split-group","RootCanvasGroup")]
        [TestCase("misplaced-raycaster","GraphicRaycaster")]
        [TestCase("child-bindings","Bindings")]
        [TestCase("wrong-node-name","UINode")]
        [TestCase("missing-local-safe","LocalSafeArea")]
        [TestCase("external-local-safe","LocalSafeArea")]
        [TestCase("wrong-safe-parent","LocalSafeArea")]
        public void ValidatorRejectsIncorrectMainWindowHierarchy(string broken,string expected)
        {
            var bridge=MainWindow();
            var canvas=bridge.RootCanvas; var group=bridge.RootCanvasGroup; var bindings=bridge.Bindings;var localSafe=bridge.LocalSafeArea;
            switch(broken)
            {
                case "all-on-root":
                    canvas=root.AddComponent<Canvas>(); group=root.AddComponent<CanvasGroup>(); root.AddComponent<GraphicRaycaster>(); break;
                case "external-canvas":
                    external=new GameObject("UINode",typeof(RectTransform),typeof(Canvas)); canvas=external.GetComponent<Canvas>(); break;
                case "external-group":
                    external=new GameObject("ExternalGroup",typeof(CanvasGroup)); group=external.GetComponent<CanvasGroup>(); break;
                case "split-group":
                    var other=new GameObject("OtherGroup",typeof(RectTransform),typeof(CanvasGroup)); other.transform.SetParent(root.transform,false); group=other.GetComponent<CanvasGroup>(); break;
                case "misplaced-raycaster":
                    Object.DestroyImmediate(canvas.GetComponent<GraphicRaycaster>());
                    var raycaster=new GameObject("OtherRaycaster",typeof(RectTransform),typeof(Canvas),typeof(GraphicRaycaster)); raycaster.transform.SetParent(root.transform,false); break;
                case "child-bindings": bindings=canvas.gameObject.AddComponent<UIBindings>(); break;
                case "wrong-node-name": canvas.name="OtherCanvas"; break;
                case "missing-local-safe": localSafe=null;break;
                case "external-local-safe": external=new GameObject("SafeArea",typeof(RectTransform),typeof(SafeAreaRoot));localSafe=external.GetComponent<SafeAreaRoot>();break;
                case "wrong-safe-parent": localSafe.transform.SetParent(root.transform,false);break;
            }
            bridge.Configure(canvas,group,bindings,localSafeArea:localSafe);
            Assert.That(Errors(root),Does.Contain(expected));
        }

        [Test]
        public void BuildGateRejectsPrefabWithMissingTMPFont()
        {
            const string path="Assets/Content/UI/Common/U3InvalidFixture.prefab";
            var bindings=Bindings();
            root.SetActive(false); // Inspect serialized data without TMP's optional importer-window Awake.
            root.AddComponent<TMPro.TextMeshProUGUI>().font=null;
            Configure(bindings,null,null);
            try
            {
                PrefabUtility.SaveAsPrefabAsset(root,path);
                var validator=Activator.CreateInstance(Required("Client.Editor.UI.UIBindingValidator","Client.Editor"));
                var error=Assert.Throws<TargetInvocationException>(()=>validator.GetType().GetMethod("OnPreprocessBuild").Invoke(validator,new object[]{null}));
                Assert.That(error.InnerException,Is.TypeOf<BuildFailedException>());
                Assert.That(error.InnerException.Message,Does.Contain("TMP font"));
            }
            finally { AssetDatabase.DeleteAsset(path); }
        }
    }
}
