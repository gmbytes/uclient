#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using Client.Framework.UI;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Object=UnityEngine.Object;

namespace Client.Tests
{
    [Category("UIMatrix")]
    public sealed class AdaptiveUIMatrixTests
    {
        [Serializable] private sealed class MatrixReport { public string profile="Editor render-texture preview"; public bool passed; public List<Case> cases=new List<Case>(); }
        [Serializable] private sealed class Case
        {
            public int width,height; public string safeAreaCase,screenshot; public Rect safeArea,background,interactive;
            public Vector2 referenceResolution; public float scale,formWidth,buttonHeight; public bool submitReachable;
        }
        private GameObject root,cameraRoot;
        private RenderTexture texture;
        [TearDown] public void Cleanup()
        {
            if(root!=null) Object.DestroyImmediate(root); if(cameraRoot!=null) Object.DestroyImmediate(cameraRoot);
            if(texture!=null) { texture.Release(); Object.DestroyImmediate(texture); }
        }
        [UnityTest]
        public IEnumerator EditorMatrixHasSafeInteractiveBoundsAndReachableSubmit()
        {
            string output=Argument("-uiMatrixOutput")??Path.GetFullPath("Builds/Tests/U2Matrix/Regression-"+Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(output);
            var prefab=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Content/UI/Common/AdaptiveUIRoot.prefab");
            Assert.That(prefab,Is.Not.Null,"Generate U2 assets before the matrix.");
            root=Object.Instantiate(prefab); root.GetComponent<ScreenLayoutComponent>().enabled=false;
            var canvas=root.GetComponent<Canvas>(); var adaptive=root.GetComponent<AdaptiveCanvas>();
            cameraRoot=new GameObject("MatrixCamera",typeof(Camera)); var camera=cameraRoot.GetComponent<Camera>(); camera.orthographic=true;
            canvas.renderMode=RenderMode.ScreenSpaceCamera; canvas.worldCamera=camera; canvas.planeDistance=1;
            var safe=root.GetComponentInChildren<SafeAreaRoot>();
            var brand=AdaptiveLayoutTests.Child("Brand",safe.transform); var brandText=Label("BrandLabel",brand,"ADAPTIVE UI\nLayout foundation",48); Stretch(brandText.rectTransform);
            var viewport=AdaptiveLayoutTests.Child("FormViewport",safe.transform); viewport.gameObject.AddComponent<RectMask2D>();
            var scroll=viewport.gameObject.AddComponent<ScrollRect>();
            var content=AdaptiveLayoutTests.Child("Form",viewport); scroll.viewport=viewport; scroll.content=content;
            var header=Label("Header",content,"Form preview",42); Place(header.rectTransform,0,-25,0,90);
            var first=Label("FieldOne",content,"Account input placeholder",32); Place(first.rectTransform,0,-160,0,96); first.color=Color.white;
            var second=Label("FieldTwo",content,"Credential input placeholder",32); Place(second.rectTransform,0,-320,0,96);
            var submit=AdaptiveLayoutTests.Child("PrimaryAction",content); submit.anchorMin=new Vector2(0,0); submit.anchorMax=new Vector2(1,0); submit.pivot=new Vector2(.5f,0); submit.sizeDelta=new Vector2(0,96);
            var image=submit.gameObject.AddComponent<Image>(); image.color=new Color(.13f,.5f,.7f); submit.gameObject.AddComponent<Button>();
            var buttonLabel=Label("Label",submit,"Primary action",32); Stretch(buttonLabel.rectTransform);
            var layout=safe.gameObject.AddComponent<AdaptiveLayout>();
            var definition=AssetDatabase.LoadAssetAtPath<AdaptiveLayoutDefinition>("Assets/Content/UI/Login/UILogin.Layout.asset");
            Assert.That(definition,Is.Not.Null); layout.Configure(definition,brand,viewport,scroll,content,submit); adaptive.Configure(safe,layout,null);
            var report=new MatrixReport();
            int[,] sizes={{720,1280},{1080,1920},{1080,2400},{1536,2048},{1280,720},{1920,1080},{2560,1080},{720,480}};
            string[] edges={"Full","Left","Right","Top","Bottom"};
            for(int i=0;i<sizes.GetLength(0);i++) for(int edge=0;edge<edges.Length;edge++)
            {
                int w=sizes[i,0],h=sizes[i,1];
                if(texture!=null) { camera.targetTexture=null; texture.Release(); Object.DestroyImmediate(texture); }
                texture=new RenderTexture(w,h,24); texture.Create(); camera.targetTexture=texture;
                Rect safePixels=new Rect(0,0,w,h); float inset=Mathf.Round(Mathf.Min(w,h)*.08f);
                if(edge==1) safePixels.xMin=inset; if(edge==2) safePixels.xMax=w-inset;
                if(edge==3) safePixels.yMax=h-inset; if(edge==4) safePixels.yMin=inset;
                var snapshot=AdaptiveLayoutTests.Snapshot(w,h,safePixels);
                adaptive.Apply(snapshot); Canvas.ForceUpdateCanvases(); yield return null;
                // RenderTexture drives the Canvas pixelRect; this checks preview fidelity numerically.
                var rootRect=(RectTransform)root.transform;
                Assert.That(rootRect.rect.width*canvas.scaleFactor,Is.EqualTo(w).Within(1),"preview width");
                Assert.That(rootRect.rect.height*canvas.scaleFactor,Is.EqualTo(h).Within(1),"preview height");
                var actual=Pixels(viewport,rootRect,w,h); var background=Pixels(root.transform.GetChild(0) as RectTransform,rootRect,w,h);
                AssertInside(actual,safePixels,edges[edge]);
                Assert.That(background.xMin,Is.EqualTo(0).Within(1)); Assert.That(background.yMin,Is.EqualTo(0).Within(1));
                Assert.That(background.xMax,Is.EqualTo(w).Within(1)); Assert.That(background.yMax,Is.EqualTo(h).Within(1));
                Assert.That(viewport.rect.width,Is.LessThanOrEqualTo(760.01f)); Assert.That(submit.rect.height,Is.GreaterThanOrEqualTo(96));
                var profile=definition.For(snapshot.LayoutMode); var safeRect=(RectTransform)safe.transform;
                var formRegion=profile.FormAnchors;
                float expectedLeft=safePixels.xMin+(formRegion.xMin*safeRect.rect.width+48)*canvas.scaleFactor;
                float expectedRight=safePixels.xMin+(formRegion.xMax*safeRect.rect.width-48)*canvas.scaleFactor;
                Assert.That(actual.xMin,Is.GreaterThanOrEqualTo(expectedLeft-1)); Assert.That(actual.xMax,Is.LessThanOrEqualTo(expectedRight+1));
                scroll.verticalNormalizedPosition=0; scroll.StopMovement();
                Rect action=Pixels(submit,rootRect,w,h); AssertInside(action,actual,"submit scroll destination");
                scroll.verticalNormalizedPosition=1; Canvas.ForceUpdateCanvases();
                camera.Render(); var previous=RenderTexture.active; RenderTexture.active=texture;
                var png=new Texture2D(w,h,TextureFormat.RGB24,false); png.ReadPixels(new Rect(0,0,w,h),0,0); png.Apply();
                string filename=$"{w}x{h}-{edges[edge]}.png"; File.WriteAllBytes(Path.Combine(output,filename),png.EncodeToPNG()); Object.DestroyImmediate(png); RenderTexture.active=previous;
                report.cases.Add(new Case {width=w,height=h,safeAreaCase=edges[edge],safeArea=safePixels,background=background,interactive=actual,referenceResolution=root.GetComponent<CanvasScaler>().referenceResolution,scale=canvas.scaleFactor,formWidth=viewport.rect.width,buttonHeight=submit.rect.height,submitReachable=true,screenshot=filename});
                File.WriteAllText(Path.Combine(output,"matrix.json"),JsonUtility.ToJson(report,true));
            }
            report.passed=true; File.WriteAllText(Path.Combine(output,"matrix.json"),JsonUtility.ToJson(report,true));
            Assert.That(report.cases.Count,Is.EqualTo(40)); Debug.Log("U2_MATRIX_PASS "+output);
        }
        private static void AssertInside(Rect inner,Rect outer,string context)
        { Assert.That(inner.xMin,Is.GreaterThanOrEqualTo(outer.xMin-1),context); Assert.That(inner.yMin,Is.GreaterThanOrEqualTo(outer.yMin-1),context); Assert.That(inner.xMax,Is.LessThanOrEqualTo(outer.xMax+1),context); Assert.That(inner.yMax,Is.LessThanOrEqualTo(outer.yMax+1),context); }
        private static Rect Pixels(RectTransform target,RectTransform root,int w,int h)
        { var corners=new Vector3[4]; target.GetWorldCorners(corners); Vector2 a=(Vector2)root.InverseTransformPoint(corners[0])-root.rect.min,b=(Vector2)root.InverseTransformPoint(corners[2])-root.rect.min; return Rect.MinMaxRect(a.x/root.rect.width*w,a.y/root.rect.height*h,b.x/root.rect.width*w,b.y/root.rect.height*h); }
        private static Text Label(string name,Transform parent,string value,int size)
        { var rect=AdaptiveLayoutTests.Child(name,parent); var text=rect.gameObject.AddComponent<Text>(); text.font=Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); text.text=value;text.fontSize=size;text.alignment=TextAnchor.MiddleCenter;text.raycastTarget=false;return text; }
        private static void Stretch(RectTransform rect) {rect.anchorMin=Vector2.zero;rect.anchorMax=Vector2.one;rect.offsetMin=rect.offsetMax=Vector2.zero;}
        private static void Place(RectTransform rect,float x,float y,float width,float height) {rect.anchorMin=new Vector2(0,1);rect.anchorMax=Vector2.one;rect.pivot=new Vector2(.5f,1);rect.sizeDelta=new Vector2(width,height);rect.anchoredPosition=new Vector2(x,y);}
        private static string Argument(string key) {var args=Environment.GetCommandLineArgs();int index=Array.IndexOf(args,key);return index>=0&&index+1<args.Length?args[index+1]:null;}
    }
}
#endif
