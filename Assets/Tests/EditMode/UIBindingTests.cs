using System;
using System.Reflection;
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
        [TearDown] public void Cleanup() { if(root!=null) Object.DestroyImmediate(root); }
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
