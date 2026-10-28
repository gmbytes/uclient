using System;
using System.IO;
using System.Linq;
using Client.Framework.UI;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using Object=UnityEngine.Object;

namespace Client.Editor.UI
{
    public static class U5LoginAssetBuilder
    {
        const string DirectoryPath="Assets/Content/UI/Login";
        public static void Create()
        {
            var definition=UIWindowCreator.Create("UILogin",DirectoryPath);
            var root=PrefabUtility.LoadPrefabContents(definition.PrefabAddress);
            try
            {
                var font=AssetDatabase.LoadAssetAtPath<Font>("Assets/Content/UI/Common/Fonts/NotoSansCJKsc-Regular.otf");
                if(font==null)throw new InvalidOperationException("U5_FONT: licensed font must be imported first.");
                var safe=root.transform.Find("UINode/SafeArea");var brand=safe.Find("Static");var content=safe.Find("Dynamic/Content");
                var title=Label("Title",brand,font,"框架登录演示",44,150);Stretch(title.rectTransform);
                title.alignment=TextAnchor.MiddleCenter;title.color=new Color(.84f,.9f,1);
                var group=content.gameObject.AddComponent<VerticalLayoutGroup>();group.spacing=22;group.padding=new RectOffset(20,20,20,20);group.childControlHeight=true;group.childControlWidth=true;group.childForceExpandHeight=false;group.childForceExpandWidth=true;
                Label("AccountLabel",content,font,"测试账号",28,50);
                var inputObject=new GameObject("AccountInput",typeof(RectTransform),typeof(Image),typeof(InputField),typeof(LayoutElement));inputObject.transform.SetParent(content,false);inputObject.GetComponent<LayoutElement>().preferredHeight=100;
                inputObject.GetComponent<Image>().color=new Color(.12f,.17f,.24f);
                var input=inputObject.GetComponent<InputField>();input.targetGraphic=inputObject.GetComponent<Image>();input.characterLimit=20;input.contentType=InputField.ContentType.IntegerNumber;input.text="10001";
                var value=Label("Value",input.transform,font,"10001",32,100);Stretch(value.rectTransform);value.rectTransform.offsetMin=new Vector2(24,8);value.rectTransform.offsetMax=new Vector2(-24,-8);value.alignment=TextAnchor.MiddleLeft;input.textComponent=value;
                var login=Button("LoginButton",content,font,"登录",new Color(.16f,.44f,.74f));
                var cancel=Button("CancelButton",content,font,"取消",new Color(.22f,.27f,.35f));
                var status=Label("StatusLabel",content,font,"请输入测试账号",28,130);
                var version=Label("VersionLabel",content,font,"Framework demo v1 · 本地模拟",24,100);version.color=new Color(.62f,.72f,.83f);
                var bindings=root.GetComponent<UIBindings>();bindings.Configure(new[]{Binding("Title",title),Binding("AccountInput",input),Binding("LoginButton",login),Binding("CancelButton",cancel),Binding("StatusLabel",status),Binding("VersionLabel",version)},null);
                var bridge=root.GetComponent<UIViewBridge>();var layout=bridge.Layout;var scroll=content.parent.GetComponent<ScrollRect>();
                layout.Configure(definition.LayoutDefinition,(RectTransform)brand,(RectTransform)content.parent,scroll,(RectTransform)content,(RectTransform)login.transform);
                bridge.Keyboard.Configure(scroll,(RectTransform)login.transform);
                PrefabUtility.SaveAsPrefabAsset(root,definition.PrefabAddress);
                var generated=UIBindingGenerator.Regenerate(bindings,DirectoryPath);if(!generated.Success)throw new InvalidOperationException(generated.Error);
                var texts=AssetDatabase.LoadAssetAtPath<UITextDefinition>(DirectoryPath+"/UILogin.Text.asset");
                texts.Entries=new[]{new UITextDefinition.Entry{Key="Title",Value="框架登录演示"},new UITextDefinition.Entry{Key="LocalSimulation",Value="本地模拟"},new UITextDefinition.Entry{Key="TokDevelopment",Value="Tok 开发登录"}};EditorUtility.SetDirty(texts);
                System.IO.Directory.CreateDirectory("Assets/Settings/Environment");AssetDatabase.Refresh();
                Profile("LocalSimulationProfile",LoginPresentationMode.LocalSimulation,"本地模拟");
                Profile("DevelopmentProfile",LoginPresentationMode.TokDevelopment,"Tok 开发登录");
                AssetDatabase.SaveAssets();
                var report=UIAuthoringValidator.Validate(definition);if(!report.IsValid)throw new InvalidOperationException(report.ToString());
                Debug.Log("U5_LOGIN_ASSETS_CREATED\n"+report);
            }
            finally{PrefabUtility.UnloadPrefabContents(root);}
        }
        public static void UpdatePresentationVersion()
        {
            foreach(string name in new[]{"LocalSimulationProfile","DevelopmentProfile"})
            {var profile=AssetDatabase.LoadAssetAtPath<LoginPresentationProfile>("Assets/Settings/Environment/"+name+".asset");if(profile==null)throw new InvalidOperationException("Missing profile.");profile.Version="Framework demo v1";EditorUtility.SetDirty(profile);}
            string path=DirectoryPath+"/UILogin.prefab";var root=PrefabUtility.LoadPrefabContents(path);
            try{((Text)root.GetComponent<UIBindings>().Resolve("VersionLabel")).text="Framework demo v1 · 本地模拟";PrefabUtility.SaveAsPrefabAsset(root,path);}
            finally{PrefabUtility.UnloadPrefabContents(root);}
            AssetDatabase.SaveAssets();Debug.Log("U5_PRESENTATION_VERSION_UPDATED");
        }
        static void Profile(string name,LoginPresentationMode mode,string label)
        {var profile=ScriptableObject.CreateInstance<LoginPresentationProfile>();profile.Mode=mode;profile.Label=label;AssetDatabase.CreateAsset(profile,"Assets/Settings/Environment/"+name+".asset");}
        static UIBindings.Binding Binding(string id,Component value)=>new UIBindings.Binding{Id=id,Value=value,ComponentType=value.GetType().AssemblyQualifiedName};
        static Text Label(string name,Transform parent,Font font,string value,int size,float height)
        {
            var go=new GameObject(name,typeof(RectTransform),typeof(Text),typeof(LayoutElement));go.transform.SetParent(parent,false);go.GetComponent<LayoutElement>().preferredHeight=height;
            var text=go.GetComponent<Text>();text.font=font;text.text=value;text.fontSize=size;text.color=Color.white;text.raycastTarget=false;text.alignment=TextAnchor.MiddleLeft;text.horizontalOverflow=HorizontalWrapMode.Wrap;text.verticalOverflow=VerticalWrapMode.Truncate;return text;
        }
        static Button Button(string name,Transform parent,Font font,string value,Color color)
        {
            var go=new GameObject(name,typeof(RectTransform),typeof(Image),typeof(Button),typeof(LayoutElement));go.transform.SetParent(parent,false);go.GetComponent<LayoutElement>().preferredHeight=100;go.GetComponent<Image>().color=color;
            var button=go.GetComponent<Button>();button.targetGraphic=go.GetComponent<Image>();var text=Label("Label",go.transform,font,value,32,100);Stretch(text.rectTransform);text.alignment=TextAnchor.MiddleCenter;return button;
        }
        static void Stretch(RectTransform rect){rect.anchorMin=Vector2.zero;rect.anchorMax=Vector2.one;rect.offsetMin=rect.offsetMax=Vector2.zero;}
    }
}
