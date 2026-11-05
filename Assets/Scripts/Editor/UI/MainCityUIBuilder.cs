using System;
using System.IO;
using System.Linq;
using Client.Framework.UI;
using Client.Framework.Presentation;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using Object=UnityEngine.Object;

namespace Client.Editor.UI
{
    public static class MainCityUIBuilder
    {
        const string DirectoryPath="Assets/Content/UI/MainCity";
        static readonly Color Gold=new Color(.88f,.75f,.43f);
        static readonly Color Ink=new Color(.035f,.095f,.085f,.95f);
        public static void PrepareAndBuild()
        {Client.Editor.City.MainCityAssetBuilder.PrepareResources();Build();}
        [InitializeOnLoadMethod]
        static void PrepareRequestedResources()
        {
            const string request="Builds/MainCity/rebuild.request";
            if(!File.Exists(request))return;
            EditorApplication.delayCall+=()=>
            {
                if(EditorApplication.isPlaying)return;
                File.Delete(request);
                try{PrepareAndBuild();File.WriteAllText("Builds/MainCity/rebuild-result.txt","OK");}
                catch(Exception error){File.WriteAllText("Builds/MainCity/rebuild-result.txt",error.ToString());Debug.LogException(error);}
            };
        }
        [MenuItem("Game/MainCity/Build Main City UI")]
        public static void Build()
        {
            var model=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Content/Prefabs/MainCity/MainCity.prefab");
            if(model==null)throw new InvalidOperationException("Prepare main-city resources first.");
            Directory.CreateDirectory(DirectoryPath);AssetDatabase.Refresh();
            var font=AssetDatabase.LoadAssetAtPath<Font>("Assets/Content/UI/Common/Fonts/NotoSansCJKsc-Regular.otf");
            var catalog=AssetDatabase.LoadAssetAtPath<UIViewDefinitions>("Assets/Content/UI/Common/UIViewDefinitions.asset");
            var definition=catalog.Definitions.FirstOrDefault(d=>d.ViewID=="UIMainCity");
            if(definition==null)
            {
                var layout=ScriptableObject.CreateInstance<AdaptiveLayoutDefinition>();
                AssetDatabase.CreateAsset(layout,DirectoryPath+"/UIMainCity.Layout.asset");
                AssetDatabase.CreateAsset(ScriptableObject.CreateInstance<UITextDefinition>(),DirectoryPath+"/UIMainCity.Text.asset");
                definition=new UIViewDefinition{ViewID="UIMainCity",PrefabAddress=DirectoryPath+"/UIMainCity.prefab",Group=UIGroups.Page,LayoutDefinition=layout};
                catalog.Configure(catalog.Definitions.Concat(new[]{definition}).ToArray());EditorUtility.SetDirty(catalog);
                var config=ScriptableObject.CreateInstance<UIWindowConfig>();config.Catalog=catalog;config.ViewID="UIMainCity";
                AssetDatabase.CreateAsset(config,DirectoryPath+"/UIMainCity.Config.asset");
            }
            var template=AssetDatabase.LoadAssetAtPath<GameObject>(UIWindowCreator.TemplatePath);
            var root=Object.Instantiate(template);root.name="UIMainCity";
            try
            {
                var node=root.transform.Find("UINode");
                var safe=(RectTransform)node.Find("SafeArea");
                var background=node.Find("Background");background.GetComponent<Image>().color=Ink;
                var mapRect=Rect("Map",background);Stretch(mapRect);mapRect.gameObject.AddComponent<RawImage>().color=Color.white;
                var map=mapRect.gameObject.AddComponent<CityViewport>();map.Configure(model,mapRect);
                map.ConfigureLabels(new[]{"","城主府","仓库","兵营","钱庄","将军府","建筑六","农田","伐木场","冶炼厂","城墙","铁匠铺","军机处","鸿胪寺","瞭望塔","祭台","医馆"});
                map.ConfigureFillViewport(true);
                var hud=(RectTransform)safe.Find("Static");Stretch(hud);
                safe.Find("Dynamic").gameObject.SetActive(false);
                var cityDetailBindings=CityHallDetailUIBuilder.Attach(root,font);
                var mainUIBindings=MainCityHudUIBuilder.Attach(root,font);
                var bindings=root.GetComponent<UIBindings>();
                bindings.Configure(new[]{Binding("Map",map)}.Concat(cityDetailBindings).Concat(mainUIBindings).ToArray(),null);
                var staticLayout=safe.GetComponent<AdaptiveLayout>();
                staticLayout.Configure(definition.LayoutDefinition,null,null,null,null,null);
                root.GetComponent<UIViewBridge>().Configure(node.GetComponent<Canvas>(),node.GetComponent<CanvasGroup>(),bindings,staticLayout,null,null,safe.GetComponent<SafeAreaRoot>());
                PrefabUtility.SaveAsPrefabAsset(root,definition.PrefabAddress);
                Directory.CreateDirectory("Assets/Scripts/Game/MainCity/Generated");
                var generated=UIBindingGenerator.Regenerate(bindings,DirectoryPath);if(!generated.Success)throw new InvalidOperationException(generated.Error);
                AssetDatabase.SaveAssets();AssetDatabase.Refresh();Debug.Log("MAIN_CITY_UI_BUILT");
            }
            finally{Object.DestroyImmediate(root);}
        }
        static RectTransform Rect(string name,Transform parent){var go=new GameObject(name,typeof(RectTransform));go.transform.SetParent(parent,false);return (RectTransform)go.transform;}
        static void Anchors(RectTransform rect,Rect area){rect.anchorMin=area.min;rect.anchorMax=area.max;rect.offsetMin=rect.offsetMax=Vector2.zero;}
        static void Stretch(RectTransform rect,float x=0,float y=0){Anchors(rect,new Rect(0,0,1,1));rect.offsetMin=new Vector2(x,y);rect.offsetMax=new Vector2(-x,-y);}
        static RectTransform Panel(string name,Transform parent,Rect area){var rect=Rect(name,parent);Anchors(rect,area);var image=rect.gameObject.AddComponent<Image>();image.color=Ink;image.raycastTarget=false;var outline=rect.gameObject.AddComponent<Outline>();outline.effectColor=new Color(.6f,.5f,.25f,.7f);outline.effectDistance=new Vector2(1,-1);return rect;}
        static Text Label(string name,Transform parent,Font font,string value,int size,Color color){var rect=Rect(name,parent);var text=rect.gameObject.AddComponent<Text>();text.font=font;text.fontSize=size;text.color=color;text.text=value;text.alignment=TextAnchor.MiddleCenter;text.raycastTarget=false;return text;}
        static Button Button(string name,Transform parent,Font font,string value){var rect=Panel(name,parent,new Rect());var image=rect.GetComponent<Image>();image.raycastTarget=true;image.color=new Color(.13f,.18f,.12f,.98f);var button=rect.gameObject.AddComponent<Button>();button.targetGraphic=image;var label=Label("Label",rect,font,value,24,Gold);Stretch(label.rectTransform,5,2);return button;}
        static UIBindings.Binding Binding(string id,Component value)=>new UIBindings.Binding{Id=id,Value=value,ComponentType=value.GetType().AssemblyQualifiedName};
    }
}
