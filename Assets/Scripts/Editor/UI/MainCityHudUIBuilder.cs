using System;
using System.Collections.Generic;
using System.IO;
using Client.Framework.UI;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace Client.Editor.UI
{
    /// <summary>Fullscreen MainUI: resources at the top, player information and actions at the bottom.</summary>
    public static class MainCityHudUIBuilder
    {
        const string Art="Assets/Content/UI/MainCity/Art/";
        static readonly Color Ink=new Color32(54,47,39,255),Secondary=new Color32(88,80,65,255);
        static readonly Color Jade=new Color32(41,79,65,255),Gold=new Color32(184,151,87,255),Paper=new Color32(249,244,230,255);

        public static IReadOnlyList<UIBindings.Binding> Attach(GameObject root,Font font)
        {
            PrepareArt();
            var bindings=new List<UIBindings.Binding>();
            var main=Rect("MainUI",root.transform.Find("UINode/SafeArea/Static"));Area(main,0,0,1,1);
            Add("MainUI",main);
            var top=Rect("Top",main);top.anchorMin=new Vector2(0,1);top.anchorMax=Vector2.one;top.pivot=new Vector2(.5f,1);top.sizeDelta=new Vector2(0,CityUIFrameBuilder.HeaderHeight);top.anchoredPosition=Vector2.zero;
            top.gameObject.AddComponent<Image>().color=Color.clear;
            var fade=Rect("CityInkFade",top);fade.anchorMin=Vector2.zero;fade.anchorMax=new Vector2(1,0);
            fade.offsetMin=new Vector2(-160,-64);fade.offsetMax=new Vector2(160,256);
            var wash=fade.gameObject.AddComponent<RawImage>();wash.texture=AssetDatabase.LoadAssetAtPath<Texture2D>(Art+"Refined/MainUITopFade.png");wash.raycastTarget=false;
            var resourceFrame=CityUIFrameBuilder.ResourceFrame(top);Add("MainUIResourceFrame",resourceFrame);
            var bottom=Rect("Bottom",main);Pin(bottom,0,0,0,268);bottom.anchorMax=new Vector2(1,0);bottom.sizeDelta=new Vector2(0,268);
            bottom.gameObject.AddComponent<Image>().color=Color.clear;
            var bottomFade=Rect("CityInkFade",bottom);bottomFade.anchorMin=Vector2.zero;bottomFade.anchorMax=new Vector2(1,0);bottomFade.offsetMin=new Vector2(-160,-160);bottomFade.offsetMax=new Vector2(160,360);
            var bottomWash=bottomFade.gameObject.AddComponent<RawImage>();bottomWash.texture=AssetDatabase.LoadAssetAtPath<Texture2D>(Art+"Refined/MainUIBottomFade.png");bottomWash.raycastTarget=false;
            var playerFrame=Rect("PlayerFrame",bottom);playerFrame.anchorMin=Vector2.zero;playerFrame.anchorMax=Vector2.right;playerFrame.offsetMin=new Vector2(0,24);playerFrame.offsetMax=new Vector2(0,258);
            Rule(playerFrame,0,234,1);Add("MainUIPlayerFrame",playerFrame);
            var profile=Rect("PlayerButton",bottom);Pin(profile,46,64,144,166);
            var profileHit=profile.gameObject.AddComponent<Image>();profileHit.color=Color.clear;
            var profileButton=profile.gameObject.AddComponent<Button>();profileButton.targetGraphic=profileHit;Add("MainUIPlayerButton",profileButton);
            var halo=Rect("PortraitRim",profile);Pin(halo,9,13,126,126);var rim=halo.gameObject.AddComponent<Image>();rim.sprite=Sprite("Refined/CircleSurface");rim.color=Gold;rim.raycastTarget=false;
            var inset=Rect("JadeInset",halo);Area(inset,.018f,.018f,.964f,.964f);var jade=inset.gameObject.AddComponent<Image>();jade.sprite=rim.sprite;jade.color=Jade;jade.raycastTarget=false;
            var portrait=Rect("Portrait",profile);Pin(portrait,4,10,136,158);var face=portrait.gameObject.AddComponent<Image>();face.sprite=Sprite("Refined/PlayerPortrait");face.preserveAspect=true;face.raycastTarget=false;Add("MainUIAvatar",face);
            var level=Label("PlayerLevel",bottom,font,"等级 1",23,Paper,true);Pin(level.rectTransform,216,133,130,38);Add("MainUILevel",level);
            var noble=Label("NobleLevel",bottom,font,"贵族 0",22,new Color32(233,210,157,255));Pin(noble.rectTransform,366,133,150,38);Add("MainUINoble",noble);

            var name=Label("PlayerName",bottom,font,"初入三国",35,Paper,true);Pin(name.rectTransform,214,176,470,56);Add("MainUIName",name);
            var powerCaption=Label("PowerCaption",bottom,font,"战力",22,new Color32(205,210,189,255));Pin(powerCaption.rectTransform,550,133,70,40);
            var power=Label("CombatPower",bottom,font,"0",30,Paper,true);Pin(power.rectTransform,628,124,230,56);Add("MainUIPower",power);
            var experienceTrack=Rect("ExperienceTrack",bottom);experienceTrack.anchorMin=new Vector2(.20f,0);experienceTrack.anchorMax=new Vector2(.955f,0);experienceTrack.offsetMin=new Vector2(0,104);experienceTrack.offsetMax=new Vector2(0,114);
            var track=experienceTrack.gameObject.AddComponent<Image>();track.sprite=Sprite("Refined/RoundedSurface");track.type=Image.Type.Sliced;track.color=new Color(.02f,.04f,.035f,.70f);track.raycastTarget=false;
            var experienceFill=Rect("Fill",experienceTrack);Area(experienceFill,0,0,1,1);var fill=experienceFill.gameObject.AddComponent<Image>();fill.sprite=track.sprite;fill.type=Image.Type.Filled;fill.fillMethod=Image.FillMethod.Horizontal;fill.fillOrigin=0;fill.fillAmount=0;fill.color=new Color32(185,163,98,255);fill.raycastTarget=false;Add("MainUIExperienceFill",fill);
            var experience=Label("Experience",bottom,font,"经验 0 / 100",20,new Color32(219,222,202,255));Pin(experience.rectTransform,216,60,730,34);Add("MainUIExperience",experience);
            var function=ActionButton("FunctionsButton",bottom,font,"功能",new Rect(0,156,152,74));
            var functionRect=(RectTransform)function.transform;functionRect.anchorMin=functionRect.anchorMax=new Vector2(1,0);functionRect.pivot=new Vector2(1,0);functionRect.anchoredPosition=new Vector2(-48,156);Add("MainUIFunctions",function);
            var resources=CityUIFrameBuilder.ResourceStock(top,"Resources");
            string[] icons={"Gold","Silver","Iron","Wood","Food"},values={"120","6,800","12,800","15,600","24,000"};
            for(int i=0;i<icons.Length;i++)
            {
                var row=Rect(icons[i]+"Group",resources);row.anchorMin=new Vector2(.012f+i*.198f,0);row.anchorMax=new Vector2(.202f+i*.198f,1);row.offsetMin=row.offsetMax=Vector2.zero;
                var icon=Rect("Icon",row);Area(icon,.02f,.10f,.25f,.80f);var image=icon.gameObject.AddComponent<Image>();image.sprite=Sprite(icons[i]);image.preserveAspect=true;image.raycastTarget=false;
                var value=Label(icons[i]+"Amount",row,font,values[i],27,Paper,true);Area(value.rectTransform,.32f,0,.66f,1);Add("MainUI"+icons[i],value);
            }

            var menu=Rect("FunctionsMenu",main);menu.anchorMin=new Vector2(.045f,0);menu.anchorMax=new Vector2(.955f,0);menu.offsetMin=new Vector2(0,286);menu.offsetMax=new Vector2(0,488);
            var menuImage=menu.gameObject.AddComponent<Image>();menuImage.color=new Color(.055f,.10f,.085f,.95f);var outline=menu.gameObject.AddComponent<Outline>();outline.effectColor=new Color(.65f,.53f,.30f,.7f);outline.effectDistance=new Vector2(1,-1);
            var menuTitle=Label("Heading",menu,font,"主城功能",29,Paper,true);Pin(menuTitle.rectTransform,28,138,360,46);
            string[] ids={"Heroes","Bag","Alliance","Territory"},captions={"武将","背包","联盟","领地"};
            for(int i=0;i<ids.Length;i++)
            {
                var button=ActionButton(ids[i],menu,font,captions[i],new Rect());var rect=(RectTransform)button.transform;
                Area(rect,.03f+i*.245f,.17f,.225f,.39f);Add("MainUI"+ids[i],button);
            }
            Add("MainUIFunctionsMenu",menu);menu.gameObject.SetActive(false);

            var dialog=Rect("PlayerDialog",main);Area(dialog,0,0,1,1);dialog.gameObject.AddComponent<Image>().color=new Color(.025f,.055f,.045f,.58f);
            var card=Rect("Panel",dialog);Area(card,.07f,.33f,.86f,.34f);var cardImage=card.gameObject.AddComponent<Image>();cardImage.sprite=Sprite("Refined/Panel");cardImage.type=Image.Type.Sliced;
            var title=Label("Title",card,font,"主公信息",42,Ink,true);Area(title.rectTransform,.10f,.75f,.80f,.14f);title.alignment=TextAnchor.MiddleCenter;Add("MainUIDialogTitle",title);
            var body=Label("Body",card,font,"",28,Secondary);Area(body.rectTransform,.12f,.29f,.76f,.40f);body.alignment=TextAnchor.MiddleCenter;Add("MainUIDialogBody",body);
            var dismiss=ActionButton("Dismiss",card,font,"返回主城",new Rect());Area((RectTransform)dismiss.transform,.29f,.09f,.42f,.14f);Add("MainUIDialogClose",dismiss);
            Add("MainUIDialog",dialog);dialog.gameObject.SetActive(false);
            return bindings;
            void Add(string id,Component component)=>bindings.Add(new UIBindings.Binding{Id=id,Value=component,ComponentType=component.GetType().AssemblyQualifiedName});
        }
        static void PrepareArt()
        {
            string path=Art+"Refined/PlayerPortrait.png";AssetDatabase.ImportAsset(path,ImportAssetOptions.ForceSynchronousImport);
            var importer=(TextureImporter)AssetImporter.GetAtPath(path);if(importer==null)throw new InvalidOperationException("Missing player portrait.");
            importer.textureType=TextureImporterType.Sprite;importer.spriteImportMode=SpriteImportMode.Single;importer.mipmapEnabled=false;
            importer.alphaIsTransparency=true;importer.wrapMode=TextureWrapMode.Clamp;importer.maxTextureSize=512;importer.textureCompression=TextureImporterCompression.Uncompressed;importer.SaveAndReimport();
            foreach(bool topFade in new[]{true,false})
            {
                var texture=new Texture2D(4,256,TextureFormat.RGBA32,false);
                for(int y=0;y<256;y++)for(int x=0;x<4;x++)
                {float t=topFade?Mathf.InverseLerp(0,.42f,y/255f):Mathf.InverseLerp(.64f,1,y/255f);float alpha=topFade?Mathf.SmoothStep(0,1,t):1-Mathf.SmoothStep(0,1,t);texture.SetPixel(x,y,new Color(.045f,.085f,.075f,alpha*.90f));}
                texture.Apply();path=Art+"Refined/"+(topFade?"MainUITopFade":"MainUIBottomFade")+".png";File.WriteAllBytes(path,texture.EncodeToPNG());UnityEngine.Object.DestroyImmediate(texture);
                AssetDatabase.ImportAsset(path,ImportAssetOptions.ForceSynchronousImport);importer=(TextureImporter)AssetImporter.GetAtPath(path);
                importer.textureType=TextureImporterType.Default;importer.mipmapEnabled=false;importer.wrapMode=TextureWrapMode.Clamp;importer.alphaIsTransparency=true;
                importer.textureCompression=TextureImporterCompression.Uncompressed;importer.SaveAndReimport();
            }
        }
        static Button ActionButton(string name,Transform parent,Font font,string caption,Rect area)
        {
            var rect=Rect(name,parent);Pin(rect,area.x,area.y,area.width,area.height);
            var border=rect.gameObject.AddComponent<Image>();border.sprite=Sprite("Refined/RoundedSurface");border.type=Image.Type.Sliced;border.color=Gold;
            var face=Rect("JadeFace",rect);Area(face,0,0,1,1);face.offsetMin=new Vector2(2,2);face.offsetMax=new Vector2(-2,-2);var image=face.gameObject.AddComponent<Image>();image.sprite=border.sprite;image.type=Image.Type.Sliced;image.color=Jade;image.raycastTarget=false;
            var button=rect.gameObject.AddComponent<Button>();button.targetGraphic=border;
            var text=Label("Caption",rect,font,caption,28,Paper,true);Area(text.rectTransform,0,0,1,1);text.alignment=TextAnchor.MiddleCenter;
            return button;
        }
        static RectTransform Rect(string name,Transform parent){var go=new GameObject(name,typeof(RectTransform));go.transform.SetParent(parent,false);return (RectTransform)go.transform;}
        static void Area(RectTransform rect,float x,float y,float width,float height){rect.anchorMin=new Vector2(x,y);rect.anchorMax=new Vector2(x+width,y+height);rect.offsetMin=rect.offsetMax=Vector2.zero;}
        static void Pin(RectTransform rect,float x,float y,float width,float height){rect.anchorMin=rect.anchorMax=Vector2.zero;rect.pivot=Vector2.zero;rect.anchoredPosition=new Vector2(x,y);rect.sizeDelta=new Vector2(width,height);}
        static Text Label(string name,Transform parent,Font font,string caption,int size,Color color,bool bold=false)
        {var rect=Rect(name,parent);var text=rect.gameObject.AddComponent<Text>();text.font=font;text.text=caption;text.fontSize=size;text.color=color;text.fontStyle=bold?FontStyle.Bold:FontStyle.Normal;text.raycastTarget=false;text.alignment=TextAnchor.MiddleLeft;text.horizontalOverflow=HorizontalWrapMode.Wrap;text.verticalOverflow=VerticalWrapMode.Truncate;return text;}
        static void Rule(Transform parent,float x,float y,float width){var rect=Rect("GoldRule",parent);rect.anchorMin=new Vector2(x,0);rect.anchorMax=new Vector2(x+width,0);rect.offsetMin=new Vector2(0,y);rect.offsetMax=new Vector2(0,y+1);var image=rect.gameObject.AddComponent<Image>();image.color=new Color(.62f,.51f,.32f,.34f);image.raycastTarget=false;}
        static Sprite Sprite(string name){var sprite=AssetDatabase.LoadAssetAtPath<Sprite>(Art+name+".png");if(sprite==null)throw new InvalidOperationException("Missing MainUI art: "+name);return sprite;}
    }
}
