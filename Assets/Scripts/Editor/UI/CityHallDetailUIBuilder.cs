using System;
using System.Collections.Generic;
using System.IO;
using Client.Framework.UI;
using Client.Framework.Presentation;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace Client.Editor.UI
{
    /// <summary>White-stone, lacquer and jade UI authored for the palace's 1080x1920 composition.</summary>
    public static class CityHallDetailUIBuilder
    {
        const string Art="Assets/Content/UI/MainCity/Art/";
        static readonly Color Gold=new Color32(209,179,112,255);
        static readonly Color Paper=new Color32(249,244,230,255);
        static readonly Color Ink=new Color32(54,47,39,255);
        static readonly Color Secondary=new Color32(88,80,65,255);
        static readonly Color Jade=new Color32(41,93,73,255);
        static readonly Color Vermilion=new Color32(143,48,35,255);

        public static void Build()=>MainCityUIBuilder.Build();

        public static IReadOnlyList<UIBindings.Binding> Attach(GameObject root,Font font)
        {
            PrepareArt();
            var bindings=new List<UIBindings.Binding>();
            var node=root.transform.Find("UINode");
            var safe=node.Find("SafeArea");
            var backdrop=Rect("CityDetailBackdrop",node.Find("Background"));Stretch(backdrop);
            backdrop.gameObject.AddComponent<Image>().color=Color.clear;
            Shade("SlateBackground",backdrop,"BuildingDetailThemeBorderless",new Rect(0,0,1,1));
            if(backdrop.parent.GetComponent<RectMask2D>()==null)backdrop.parent.gameObject.AddComponent<RectMask2D>();

            var page=Rect("CityDetailPage",safe);Stretch(page);
            page.gameObject.AddComponent<Image>().color=Color.clear;

            var resourcePanel=CityUIFrameBuilder.ResourceFrame(page);
            var stock=CityUIFrameBuilder.ResourceStock(page,"ResourceStock");
            Stock(stock,font,"Gold","120",0);Stock(stock,font,"Silver","6,800",1);
            Stock(stock,font,"Iron","12,800",2);Stock(stock,font,"Wood","15,600",3);Stock(stock,font,"Food","24,000",4);

            var titleText=Label("CityHallName",page,font,"城主府",54,Ink,true);
            Area(titleText.rectTransform,.22f,.868f,.56f,.06f);
            Rule(page,.31f,.86f,.38f,new Color(.90f,.79f,.53f,.8f));
            Diamond(page,new Vector2(.5f,.859f),8,Gold);
            var level=Label("CityHallLevel",page,font,"1级  城池中枢",25,Secondary);
            Area(level.rectTransform,.29f,.828f,.42f,.03f);
            var close=CloseButton(page,font,new Rect(.86f,.866f,.115f,.064f));

            var mine=Counter(page,font,"Mine","野矿","0 / 2",new Rect(.28f,.79f,.21f,.0375f));
            var camp=Counter(page,font,"Camp","营寨","0 / 1",new Rect(.51f,.79f,.21f,.0375f));

            var info=Rect("UpgradeInformation",page);Area(info,0,.078f,1,.275f);
            var previewRect=Rect("SelectedBuilding",page);Area(previewRect,.07f,.43f,.86f,.33f);
            previewRect.gameObject.AddComponent<RawImage>().raycastTarget=false;
            var preview=previewRect.gameObject.AddComponent<BuildingPreviewViewport>();
            preview.Configure(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Content/Prefabs/MainCity/MainCity.prefab"),
                AssetDatabase.LoadAssetAtPath<Shader>("Assets/Content/Shaders/BuildingPreview.shader"),
                AssetDatabase.LoadAssetAtPath<Shader>("Assets/Content/Shaders/BuildingPreviewResolve.shader"),info);
            var alliance=RoundEntry("AllianceButton",info,font,"Refined/Alliance","联盟",new Rect());
            var territory=RoundEntry("TerritoryButton",info,font,"Refined/Territory","领地",new Rect());
            PinAction((RectTransform)alliance.transform,-240);PinAction((RectTransform)territory.transform,-92);
            var cardTitle=Label("UpgradeHeading",info,font,"建筑升级",34,Ink,true);Area(cardTitle.rectTransform,.065f,.82f,.42f,.12f);cardTitle.alignment=TextAnchor.MiddleLeft;
            var badge=Surface("PreviewBadge",info,new Rect(.49f,.85f,.145f,.065f),new Color(.24f,.37f,.29f,.08f),Color.clear);
            var badgeText=Label("PreviewValues",badge,font,"体验数据",19,Secondary);Stretch(badgeText.rectTransform);
            var description=Label("CityHallDescription",info,font,"城池政务与领地管理的中心",25,Secondary);Area(description.rectTransform,.065f,.735f,.60f,.09f);description.alignment=TextAnchor.MiddleLeft;
            var benefitCaption=Label("BenefitsCaption",info,font,"升级收益",24,Secondary);Area(benefitCaption.rectTransform,.065f,.57f,.17f,.12f);benefitCaption.alignment=TextAnchor.MiddleLeft;
            var benefits=Label("UpgradeBenefits",info,font,"野矿上限 2 → 3    营寨上限 1 → 2",27,Jade);Area(benefits.rectTransform,.25f,.57f,.685f,.12f);benefits.alignment=TextAnchor.MiddleLeft;
            Rule(info,.065f,.53f,.87f,new Color(.62f,.51f,.32f,.24f));
            Cost(info,font,"Iron","铁料","1,200",0);
            Cost(info,font,"Wood","木材","1,600",1);
            Cost(info,font,"Food","粮食","2,400",2);
            var timeCaption=Label("UpgradeTimeCaption",info,font,"预计耗时",21,Secondary);Area(timeCaption.rectTransform,.065f,.185f,.32f,.065f);timeCaption.alignment=TextAnchor.MiddleLeft;
            var time=Label("UpgradeTime",info,font,"00:05:00",30,Ink,true);Area(time.rectTransform,.065f,.075f,.32f,.11f);time.alignment=TextAnchor.MiddleLeft;
            var upgrade=PrimaryButton("UpgradeCityHall",info,font,"升级",new Rect(.515f,.07f,.42f,.19f),36);

            var dialog=Rect("CityDetailDialog",page);Stretch(dialog);dialog.gameObject.AddComponent<Image>().color=new Color(.025f,.055f,.045f,.58f);
            var panel=Illustration("Panel",dialog,"Refined/Panel",new Rect(.07f,.33f,.86f,.30f),true);
            var dialogTitle=Label("DialogTitle",panel.transform,font,"联盟",42,Ink,true);Area(dialogTitle.rectTransform,.10f,.74f,.80f,.15f);
            Rule(panel.transform,.16f,.70f,.68f,new Color(.62f,.51f,.32f,.30f));
            var dialogBody=Label("DialogBody",panel.transform,font,"",28,Secondary);Area(dialogBody.rectTransform,.10f,.29f,.80f,.37f);
            var dialogClose=PrimaryButton("DismissCityDialog",panel.transform,font,"返回详情",new Rect(.28f,.08f,.44f,.15f),28);

            dialog.gameObject.SetActive(false);page.gameObject.SetActive(false);backdrop.gameObject.SetActive(false);
            var transition=node.gameObject.AddComponent<BuildingDetailTransition>();
            transition.Configure(node.Find("Background/Map").GetComponent<CityViewport>(),page,backdrop,safe.Find("Static").gameObject);
            Add("CityDetailPage",page);Add("CityDetailBackdrop",backdrop);Add("CityHud",safe.Find("Static").GetComponent<RectTransform>());
            Add("DetailResourceFrame",resourcePanel);
            Add("DetailTransition",transition);Add("DetailName",titleText);Add("DetailLevel",level);Add("DetailDescription",description);Add("DetailBenefits",benefits);
            Add("BuildingPreview",preview);Add("MineCounter",mine);Add("CampCounter",camp);
            foreach(string resource in new[]{"Gold","Silver","Iron","Wood","Food"})
                Add("Detail"+resource+"Stock",stock.Find(resource+"StockGroup/"+resource+"Stock").GetComponent<Text>());
            Add("CloseCityDetails",close);Add("AllianceButton",alliance);Add("TerritoryButton",territory);Add("UpgradeCityHall",upgrade);
            Add("CityDetailDialog",dialog);Add("CityDialogTitle",dialogTitle);Add("CityDialogBody",dialogBody);Add("DismissCityDialog",dialogClose);
            return bindings;
            void Add(string id,Component value)=>bindings.Add(new UIBindings.Binding{Id=id,Value=value,ComponentType=value.GetType().AssemblyQualifiedName});
        }

        static void PrepareArt()
        {
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            foreach(string name in new[]{"CityHallDetail","Iron","Wood","Food","Gold","Silver","Mine","Camp"})
            {
                string path=Art+name+".png";
                var importer=(TextureImporter)AssetImporter.GetAtPath(path);
                if(importer==null)throw new InvalidOperationException("Missing detail artwork: "+path);
                importer.textureType=name=="CityHallDetail"?TextureImporterType.Default:TextureImporterType.Sprite;
                importer.spriteImportMode=SpriteImportMode.Single;importer.mipmapEnabled=false;importer.wrapMode=TextureWrapMode.Clamp;
                importer.maxTextureSize=name=="CityHallDetail"?2048:256;importer.textureCompression=TextureImporterCompression.Uncompressed;importer.SaveAndReimport();
            }
            foreach(string name in new[]{"Panel","Upgrade","Alliance","Territory"})ImportTrimmed(name);
            MakeSurface("RoundedSurface",false);MakeSurface("CircleSurface",true);
            MakeShade("TopShade",true);MakeShade("BottomShadeSoft",false);
            MakeUpgradeWash();
            MakeSlateBackground();
            MakeResourceFade();
            var theme=(TextureImporter)AssetImporter.GetAtPath(Art+"Refined/BuildingDetailThemeBorderless.png");
            if(theme==null)throw new InvalidOperationException("Missing building detail theme background.");
            theme.textureType=TextureImporterType.Default;theme.mipmapEnabled=false;theme.wrapMode=TextureWrapMode.Clamp;
            theme.npotScale=TextureImporterNPOTScale.None;theme.maxTextureSize=2048;
            theme.textureCompression=TextureImporterCompression.Uncompressed;theme.SaveAndReimport();
        }
        static void ImportTrimmed(string name)
        {
            string path=Art+"Refined/"+name+".png";
            var importer=(TextureImporter)AssetImporter.GetAtPath(path);
            if(importer==null)throw new InvalidOperationException("Missing refined UI asset: "+name);
            importer.textureType=TextureImporterType.Sprite;importer.spriteImportMode=SpriteImportMode.Single;
            importer.isReadable=true;importer.mipmapEnabled=false;importer.alphaIsTransparency=true;importer.wrapMode=TextureWrapMode.Clamp;
            importer.maxTextureSize=name=="Panel"?2048:name=="Upgrade"?1024:512;
            importer.textureCompression=TextureImporterCompression.Uncompressed;importer.SaveAndReimport();
            var texture=AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            Rect pixels=ConnectedBounds(texture);
            importer.GetSourceTextureWidthAndHeight(out int sourceWidth,out int sourceHeight);
            float scaleX=(float)sourceWidth/texture.width,scaleY=(float)sourceHeight/texture.height;
            var rect=new Rect(pixels.x*scaleX,pixels.y*scaleY,pixels.width*scaleX,pixels.height*scaleY);
            Vector4 border=Vector4.zero;
            if(name=="Panel")border=new Vector4(rect.width*.06f,rect.height*.12f,rect.width*.06f,rect.height*.12f);
            if(name=="Upgrade")border=new Vector4(rect.width*.045f,rect.height*.14f,rect.width*.045f,rect.height*.14f);
            importer.spriteImportMode=SpriteImportMode.Multiple;
            importer.spritesheet=new[]{new SpriteMetaData{name=name,rect=rect,pivot=new Vector2(.5f,.5f),alignment=(int)SpriteAlignment.Center,border=border}};
            importer.isReadable=false;importer.SaveAndReimport();
        }
        static Rect ConnectedBounds(Texture2D texture)
        {
            var pixels=texture.GetPixels32();int w=texture.width,h=texture.height;
            int start=(h/2)*w+w/2;
            if(pixels[start].a<64)return new Rect(0,0,w,h);
            var seen=new bool[pixels.Length];var queue=new int[pixels.Length];int read=0,write=0;
            queue[write++]=start;seen[start]=true;int minX=w,maxX=0,minY=h,maxY=0;
            while(read<write)
            {
                int i=queue[read++],x=i%w,y=i/w;
                minX=Mathf.Min(minX,x);maxX=Mathf.Max(maxX,x);minY=Mathf.Min(minY,y);maxY=Mathf.Max(maxY,y);
                Visit(x-1,y);Visit(x+1,y);Visit(x,y-1);Visit(x,y+1);
            }
            return new Rect(Mathf.Max(0,minX-2),Mathf.Max(0,minY-2),Mathf.Min(w-1,maxX+2)-Mathf.Max(0,minX-2)+1,Mathf.Min(h-1,maxY+2)-Mathf.Max(0,minY-2)+1);
            void Visit(int x,int y){if(x<0||x>=w||y<0||y>=h)return;int i=y*w+x;if(seen[i]||pixels[i].a<64)return;seen[i]=true;queue[write++]=i;}
        }
        static void MakeSurface(string name,bool circle)
        {
            var t=new Texture2D(128,128,TextureFormat.RGBA32,false);
            for(int y=0;y<128;y++)for(int x=0;x<128;x++)
            {
                float d;
                if(circle)d=Vector2.Distance(new Vector2(x,y),new Vector2(63.5f,63.5f))-62;
                else{var q=new Vector2(Mathf.Abs(x-63.5f)-43.5f,Mathf.Abs(y-63.5f)-43.5f);d=new Vector2(Mathf.Max(q.x,0),Mathf.Max(q.y,0)).magnitude+Mathf.Min(Mathf.Max(q.x,q.y),0)-19;}
                t.SetPixel(x,y,new Color(1,1,1,Mathf.Clamp01(1-d)));
            }
            SaveUtility(t,name,true,circle?Vector4.zero:Vector4.one*24);
        }
        static void MakeShade(string name,bool top)
        {
            var t=new Texture2D(4,256,TextureFormat.RGBA32,false);
            for(int y=0;y<256;y++)for(int x=0;x<4;x++){float a=top?Mathf.Pow(y/255f,1.2f)*.48f:Mathf.Pow(1-y/255f,1.3f)*.24f;t.SetPixel(x,y,new Color(.035f,.07f,.05f,a));}
            SaveUtility(t,name,false,Vector4.zero);
        }
        static void SaveUtility(Texture2D texture,string name,bool sprite,Vector4 border)
        {
            texture.Apply();string path=Art+"Refined/"+name+".png";File.WriteAllBytes(path,texture.EncodeToPNG());UnityEngine.Object.DestroyImmediate(texture);
            AssetDatabase.ImportAsset(path,ImportAssetOptions.ForceSynchronousImport);
            var importer=(TextureImporter)AssetImporter.GetAtPath(path);importer.textureType=sprite?TextureImporterType.Sprite:TextureImporterType.Default;
            importer.spriteImportMode=SpriteImportMode.Single;importer.spriteBorder=border;importer.mipmapEnabled=false;importer.wrapMode=TextureWrapMode.Clamp;importer.alphaIsTransparency=true;
            importer.textureCompression=TextureImporterCompression.Uncompressed;importer.SaveAndReimport();
        }
        static void MakeUpgradeWash()
        {
            var texture=new Texture2D(4,256,TextureFormat.RGBA32,false);
            for(int y=0;y<256;y++)for(int x=0;x<4;x++)
            {
                float alpha=Mathf.Pow(1-y/255f,.30f)*.98f;
                texture.SetPixel(x,y,new Color(1f,.995f,.975f,alpha));
            }
            SaveUtility(texture,"UpgradeWash",false,Vector4.zero);
        }
        static void MakeSlateBackground()
        {
            var texture=new Texture2D(4,256,TextureFormat.RGBA32,false);
            for(int y=0;y<256;y++)for(int x=0;x<4;x++)
                texture.SetPixel(x,y,Color.Lerp(new Color(.95f,.95f,.94f,.99f),new Color(.84f,.85f,.84f,.96f),y/255f));
            SaveUtility(texture,"SlateBackground",false,Vector4.zero);
        }
        static void MakeResourceFade()
        {
            var texture=new Texture2D(4,256,TextureFormat.RGBA32,false);
            for(int y=0;y<256;y++)for(int x=0;x<4;x++)
            {
                float t=Mathf.InverseLerp(0,.42f,y/255f);
                float alpha=Mathf.SmoothStep(0,1,t)*.90f;
                texture.SetPixel(x,y,new Color(.045f,.085f,.075f,alpha));
            }
            SaveUtility(texture,"ResourceTopFade",false,Vector4.zero);
        }
        static void PinAction(RectTransform rect,float x)
        {rect.anchorMin=rect.anchorMax=Vector2.one;rect.pivot=new Vector2(.5f,1);rect.sizeDelta=new Vector2(122,150);rect.anchoredPosition=new Vector2(x,-4);}

        static RectTransform Counter(Transform parent,Font font,string icon,string caption,string value,Rect area)
        {
            var row=Surface(caption,parent,area,new Color(.97f,.95f,.88f,.88f),new Color(.72f,.59f,.36f,.45f));
            var image=Illustration("Icon",row,icon,new Rect(.07f,.20f,.18f,.60f));image.preserveAspect=true;
            var text=Label("Value",row,font,caption+"  "+value,24,Ink);Area(text.rectTransform,.28f,.1f,.67f,.8f);text.alignment=TextAnchor.MiddleLeft;
            return row;
        }
        static Button RoundEntry(string name,Transform parent,Font font,string icon,string caption,Rect area)
        {
            var rect=Rect(name,parent);Area(rect,area.x,area.y,area.width,area.height);
            rect.gameObject.AddComponent<Image>().color=Color.clear;
            var picture=Illustration("Emblem",rect,icon,new Rect(.04f,.22f,.92f,.76f));picture.preserveAspect=true;
            var button=rect.gameObject.AddComponent<Button>();button.targetGraphic=picture;
            var nameplate=Surface("Nameplate",rect,new Rect(.12f,0,.76f,.25f),new Color(.98f,.95f,.85f,.94f),new Color(.72f,.59f,.36f,.35f));
            var label=Label("Caption",nameplate,font,caption,24,Ink);Stretch(label.rectTransform);
            return button;
        }
        static void Cost(Transform parent,Font font,string icon,string caption,string value,int index)
        {
            var row=Rect(icon+"CostGroup",parent);Area(row,.065f+index*.29f,.30f,.27f,.19f);
            var picture=Illustration("Icon",row,icon,new Rect(.0f,.20f,.20f,.55f));picture.preserveAspect=true;
            var label=Label("ResourceName",row,font,caption,20,Secondary);Area(label.rectTransform,.26f,.55f,.70f,.37f);label.alignment=TextAnchor.MiddleLeft;
            var amount=Label(icon+"Cost",row,font,value,29,Ink,true);Area(amount.rectTransform,.26f,.13f,.70f,.48f);amount.alignment=TextAnchor.MiddleLeft;
        }
        static void Stock(Transform parent,Font font,string icon,string value,int index)
        {
            var row=Rect(icon+"StockGroup",parent);Area(row,.012f+index*.198f,0,.19f,1);
            var image=Illustration("Icon",row,icon,new Rect(.02f,.10f,.25f,.80f));image.preserveAspect=true;
            var text=Label(icon+"Stock",row,font,value,27,Ink,true);Area(text.rectTransform,.32f,0,.66f,1);text.alignment=TextAnchor.MiddleLeft;
            var stroke=text.gameObject.AddComponent<Outline>();stroke.effectColor=new Color(1,.98f,.91f,.85f);stroke.effectDistance=new Vector2(1,-1);
        }
        static Button CloseButton(Transform parent,Font font,Rect area)
        {
            var rect=Rect("CloseCityDetails",parent);Area(rect,area.x,area.y,area.width,area.height);
            rect.gameObject.AddComponent<Image>().color=Color.clear;
            var visual=Rect("Visual",rect);Area(visual,.18f,.18f,.64f,.64f);
            var rim=visual.gameObject.AddComponent<Image>();rim.sprite=Sprite("Refined/CircleSurface");rim.color=Gold;rim.raycastTarget=false;
            var inner=Rect("Inset",visual);Stretch(inner,1.5f,1.5f);var face=inner.gameObject.AddComponent<Image>();face.sprite=rim.sprite;face.color=Paper;face.raycastTarget=false;
            var button=rect.gameObject.AddComponent<Button>();button.targetGraphic=face;
            var glyph=Label("CloseGlyph",visual,font,"×",32,Vermilion);Stretch(glyph.rectTransform);
            return button;
        }
        static Button PrimaryButton(string name,Transform parent,Font font,string caption,Rect area,int size)
        {
            var image=Illustration(name,parent,"Refined/Upgrade",area);image.preserveAspect=true;image.raycastTarget=true;
            var shadow=image.gameObject.AddComponent<Shadow>();shadow.effectColor=new Color(.05f,.035f,.015f,.22f);shadow.effectDistance=new Vector2(0,-3);
            var button=image.gameObject.AddComponent<Button>();button.targetGraphic=image;var colors=button.colors;colors.highlightedColor=new Color(1,.96f,.85f);colors.pressedColor=new Color(.78f,.72f,.63f);colors.fadeDuration=.10f;button.colors=colors;
            var label=Label("Caption",image.transform,font,caption,size,Paper,true);Stretch(label.rectTransform,20,5);TextShadow(label,new Color(.32f,.06f,.025f,.65f),new Vector2(0,-1));
            return button;
        }
        static RectTransform Surface(string name,Transform parent,Rect area,Color fill,Color rim)
        {
            var rect=Rect(name,parent);Area(rect,area.x,area.y,area.width,area.height);
            var image=rect.gameObject.AddComponent<Image>();image.sprite=Sprite("Refined/RoundedSurface");image.type=Image.Type.Sliced;image.color=fill;image.raycastTarget=false;
            if(rim.a>0){var line=rect.gameObject.AddComponent<Outline>();line.effectColor=rim;line.effectDistance=new Vector2(1,-1);}
            return rect;
        }
        static void Shade(string name,Transform parent,string texture,Rect area)
        {var rect=Rect(name,parent);Area(rect,area.x,area.y,area.width,area.height);var image=rect.gameObject.AddComponent<RawImage>();image.texture=AssetDatabase.LoadAssetAtPath<Texture2D>(Art+"Refined/"+texture+".png");image.raycastTarget=false;}
        static void Rule(Transform parent,float x,float y,float width,Color color)
        {var rect=Rect("FineRule",parent);Area(rect,x,y,width,.001f);var image=rect.gameObject.AddComponent<Image>();image.color=color;image.raycastTarget=false;}
        static void Diamond(Transform parent,Vector2 point,float size,Color color)
        {var rect=Rect("Crest",parent);rect.anchorMin=rect.anchorMax=point;rect.sizeDelta=Vector2.one*size;rect.localRotation=Quaternion.Euler(0,0,45);var image=rect.gameObject.AddComponent<Image>();image.color=color;image.raycastTarget=false;}
        static void TextShadow(Text text,Color color,Vector2 distance){var shadow=text.gameObject.AddComponent<Shadow>();shadow.effectColor=color;shadow.effectDistance=distance;}
        static RectTransform Rect(string name,Transform parent){var go=new GameObject(name,typeof(RectTransform));go.transform.SetParent(parent,false);return (RectTransform)go.transform;}
        static void Area(RectTransform rect,float x,float y,float w,float h){rect.anchorMin=new Vector2(x,y);rect.anchorMax=new Vector2(x+w,y+h);rect.offsetMin=rect.offsetMax=Vector2.zero;}
        static void Stretch(RectTransform rect,float x=0,float y=0){Area(rect,0,0,1,1);rect.offsetMin=new Vector2(x,y);rect.offsetMax=new Vector2(-x,-y);}
        static Sprite Sprite(string name){var sprite=AssetDatabase.LoadAssetAtPath<Sprite>(Art+name+".png");if(sprite==null)throw new InvalidOperationException("Missing UI sprite: "+name);return sprite;}
        static Image Illustration(string name,Transform parent,string sprite,Rect area,bool sliced=false)
        {
            var rect=Rect(name,parent);Area(rect,area.x,area.y,area.width,area.height);
            var image=rect.gameObject.AddComponent<Image>();image.sprite=Sprite(sprite);image.raycastTarget=false;
            if(sliced){image.type=Image.Type.Sliced;image.pixelsPerUnitMultiplier=1.6f;var shadow=rect.gameObject.AddComponent<Shadow>();shadow.effectColor=new Color(.05f,.035f,.015f,.22f);shadow.effectDistance=new Vector2(0,-5);}
            return image;
        }
        static Text Label(string name,Transform parent,Font font,string value,int size,Color color,bool bold=false)
        {var rect=Rect(name,parent);var text=rect.gameObject.AddComponent<Text>();text.font=font;text.text=value;text.fontSize=size;text.fontStyle=bold?FontStyle.Bold:FontStyle.Normal;text.color=color;text.raycastTarget=false;text.alignment=TextAnchor.MiddleCenter;text.horizontalOverflow=HorizontalWrapMode.Wrap;text.verticalOverflow=VerticalWrapMode.Truncate;return text;}
    }
}
