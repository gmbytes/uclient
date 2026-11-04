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
        {Client.Editor.City.MainCityAssetBuilder.PrepareResources();Build();LoginArtworkBuilder.Apply();}
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
                var tasks=Panel("Tasks",hud,new Rect(.02f,.73f,.48f,.16f));
                var taskText=Label("TaskText",tasks,font,"第一章  ·  初建城池\n\n前往城主府，巡视领地\n点击建筑查看详情",25,Gold);
                Stretch(taskText.rectTransform,18,12);taskText.alignment=TextAnchor.UpperLeft;
                var mode=Label("Mode",hud,font,"本地主城展示",20,new Color(.75f,.82f,.72f));Anchors(mode.rectTransform,new Rect(.68f,.86f,.29f,.035f));
                var back=Button("ReturnButton",hud,font,"返回登录");Anchors((RectTransform)back.transform,new Rect(.72f,.81f,.25f,.045f));
                var hint=Label("Hint",hud,font,"拖拽巡视城池  ·  滚轮或双指缩放  ·  点击建筑",20,new Color(.85f,.88f,.76f));
                Anchors(hint.rectTransform,new Rect(.04f,.10f,.92f,.032f));
                var detail=Panel("BuildingDetails",hud,new Rect(.04f,.145f,.92f,.18f));
                var title=Label("BuildingTitle",detail,font,"城主府  ·  1级",32,Gold);Anchors(title.rectTransform,new Rect(.04f,.73f,.75f,.22f));title.alignment=TextAnchor.MiddleLeft;
                var desc=Label("BuildingDescription",detail,font,"点击建筑查看详情",24,new Color(.89f,.91f,.83f));Anchors(desc.rectTransform,new Rect(.04f,.25f,.9f,.46f));desc.alignment=TextAnchor.UpperLeft;
                var close=Button("CloseDetails",detail,font,"关闭");Anchors((RectTransform)close.transform,new Rect(.78f,.75f,.18f,.19f));
                var focus=Button("FocusBuilding",detail,font,"定位建筑");Anchors((RectTransform)focus.transform,new Rect(.62f,.035f,.34f,.22f));
                detail.gameObject.SetActive(false);
                var bindings=root.GetComponent<UIBindings>();
                bindings.Configure(new[]{Binding("Map",map),Binding("BuildingTitle",title),Binding("BuildingDescription",desc),Binding("BuildingDetails",detail),Binding("ReturnButton",back),Binding("CloseDetails",close),Binding("FocusBuilding",focus)},null);
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
