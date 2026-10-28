using System;
using System.Collections;
using Client.Framework.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace Client.Tests
{
    [Category("U2")]
    public sealed class AdaptiveLayoutTests
    {
        private GameObject root;
        private GameObject events;
        private RectTransform safe, brand, viewport, content, submit;
        private ScrollRect scroll;
        private Component layout;
        private ScriptableObject definition;

        private void Create(int width, int height)
        {
            root = new GameObject("AdaptiveRoot", typeof(RectTransform));
            var rect = (RectTransform)root.transform;
            rect.sizeDelta = new Vector2(width, height);
            safe = Child("SafeArea", rect);
            safe.anchorMin = Vector2.zero; safe.anchorMax = Vector2.one; safe.sizeDelta = Vector2.zero;
            brand = Child("Brand", safe);
            viewport = Child("Viewport", safe);
            scroll = viewport.gameObject.AddComponent<ScrollRect>();
            content = Child("Form", viewport);
            submit = Child("Submit", content);
            submit.anchorMin = submit.anchorMax = new Vector2(.5f, 0);
            submit.pivot = new Vector2(.5f, 0); submit.sizeDelta = new Vector2(300, 20);
            scroll.viewport = viewport; scroll.content = content;
            definition = ScriptableObject.CreateInstance(Required("AdaptiveLayoutDefinition"));
            layout = safe.gameObject.AddComponent(Required("AdaptiveLayout"));
            Call(layout, "Configure", definition, brand, viewport, scroll, content, submit);
        }

        internal static Type Required(string name)
        {
            var type = typeof(ScreenLayoutService).Assembly.GetType("Client.Framework.UI." + name);
            Assert.That(type, Is.Not.Null, "U2 runtime component required: " + name);
            return type;
        }
        internal static object Call(object target, string method, params object[] args) => target.GetType().GetMethod(method).Invoke(target, args);
        internal static RectTransform Child(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform)); go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }
        internal static ScreenLayoutSnapshot Snapshot(int w, int h, Rect area)
        {
            var service = new ScreenLayoutService(); service.Evaluate(new ScreenMetrics(w,h,area)); service.FlushPending(1); return service.Current;
        }
        private void Apply(int w, int h, Rect area)
        {
            var snapshot = Snapshot(w,h,area);
            var safeArea = safe.GetComponent(Required("SafeAreaRoot")) ?? safe.gameObject.AddComponent(Required("SafeAreaRoot"));
            Call(safeArea,"Apply",snapshot.ClampedSafeArea,new Vector2(w,h));
            Call(layout,"Apply",snapshot);
        }
        [TearDown] public void Cleanup()
        {
            if (root != null) UnityEngine.Object.DestroyImmediate(root);
            if (events != null) UnityEngine.Object.DestroyImmediate(events);
            if (definition != null) UnityEngine.Object.DestroyImmediate(definition);
        }
        [TestCase(720,1280,40,80,630,1120)]
        [TestCase(1280,720,80,40,1120,630)]
        public void InteractiveRectInsideSafeArea(int w,int h,int x,int y,int sw,int sh)
        {
            Create(w,h); Apply(w,h,new Rect(x,y,sw,sh));
            var corners = new Vector3[4]; viewport.GetWorldCorners(corners);
            Vector3 origin = ((RectTransform)root.transform).TransformPoint(new Vector3(-w/2f,-h/2f));
            Assert.That(corners[0].x-origin.x,Is.GreaterThanOrEqualTo(x-1));
            Assert.That(corners[0].y-origin.y,Is.GreaterThanOrEqualTo(y-1));
            Assert.That(corners[2].x-origin.x,Is.LessThanOrEqualTo(x+sw+1));
            Assert.That(corners[2].y-origin.y,Is.LessThanOrEqualTo(y+sh+1));
            Assert.That(viewport.rect.width,Is.LessThanOrEqualTo(760));
            Assert.That(submit.rect.height,Is.GreaterThanOrEqualTo(96));
        }
        [Test]
        public void LayoutUsesCorrectReferenceResolution()
        {
            root = new GameObject("Canvas",typeof(RectTransform),typeof(Canvas),typeof(CanvasScaler));
            var adaptive = root.AddComponent(Required("AdaptiveCanvas"));
            var scaler = root.GetComponent<CanvasScaler>();
            Call(adaptive,"Apply",Snapshot(720,1280,new Rect(0,0,720,1280)));
            Assert.That(scaler.referenceResolution,Is.EqualTo(new Vector2(1080,1920)));
            Assert.That(scaler.uiScaleMode,Is.EqualTo(CanvasScaler.ScaleMode.ScaleWithScreenSize));
            Assert.That(scaler.screenMatchMode,Is.EqualTo(CanvasScaler.ScreenMatchMode.MatchWidthOrHeight));
            Assert.That(scaler.matchWidthOrHeight,Is.EqualTo(.5f));
            Call(adaptive,"Apply",Snapshot(1280,720,new Rect(0,0,1280,720)));
            Assert.That(scaler.referenceResolution,Is.EqualTo(new Vector2(1920,1080)));
        }
        [UnityTest]
        public IEnumerator RotatingPreservesViewAndInputInstance()
        {
            Create(720,1280);
            events = new GameObject("EventSystem",typeof(EventSystem),typeof(StandaloneInputModule));
            var inputRect = Child("Input",content); inputRect.sizeDelta = new Vector2(300,100);
            var textRect = Child("Text",inputRect); var text = textRect.gameObject.AddComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            var input = inputRect.gameObject.AddComponent<InputField>(); input.textComponent=text; input.text="retained";
            input.Select(); input.ActivateInputField(); yield return null;
            Assert.That(input.isFocused,Is.True);
            int rootId=root.GetInstanceID(), inputId=input.GetInstanceID();
            Apply(720,1280,new Rect(0,0,720,1280));
            ((RectTransform)root.transform).sizeDelta=new Vector2(1280,720);
            Apply(1280,720,new Rect(0,0,1280,720));
            yield return null;
            Assert.That(root.GetInstanceID(),Is.EqualTo(rootId)); Assert.That(input.GetInstanceID(),Is.EqualTo(inputId));
            Assert.That(input.text,Is.EqualTo("retained")); Assert.That(input.isFocused,Is.True);
            Assert.That(EventSystem.current.currentSelectedGameObject,Is.EqualTo(input.gameObject));
        }
        [Test]
        public void OneSnapshotSchedulesOneLayoutRebuild()
        {
            Create(720,1280);
            root.AddComponent<Canvas>().renderMode=RenderMode.ScreenSpaceOverlay;
            root.AddComponent<CanvasScaler>();
            var adaptive=root.AddComponent<AdaptiveCanvas>();
            root.GetComponent<ScreenLayoutComponent>().enabled=false;
            var safeRoot=safe.gameObject.AddComponent<SafeAreaRoot>();
            adaptive.Configure(safeRoot,(AdaptiveLayout)layout,null);
            var counter=content.gameObject.AddComponent<LayoutBuildCounter>();
            adaptive.Apply(Snapshot(720,1280,new Rect(0,0,720,1280))); Canvas.ForceUpdateCanvases();
            counter.VerticalBuilds=0;
            adaptive.Apply(Snapshot(1280,720,new Rect(0,0,1280,720))); Canvas.ForceUpdateCanvases();
            Assert.That(counter.VerticalBuilds,Is.EqualTo(1),"A scaler change and adaptive layout must share one scheduled rebuild.");
        }
        [Test]
        public void ShortWindowCanScrollToSubmit()
        {
            Create(720,480); Apply(720,480,new Rect(0,0,720,480));
            Assert.That(scroll.vertical,Is.True); Assert.That(content.rect.height,Is.GreaterThan(viewport.rect.height));
            scroll.verticalNormalizedPosition=0; scroll.StopMovement();
            var buttonCorners=new Vector3[4]; var viewCorners=new Vector3[4];
            submit.GetWorldCorners(buttonCorners); viewport.GetWorldCorners(viewCorners);
            Assert.That(buttonCorners[0].y,Is.GreaterThanOrEqualTo(viewCorners[0].y-1));
            Assert.That(buttonCorners[2].y,Is.LessThanOrEqualTo(viewCorners[2].y+1));
        }
    }
    public sealed class LayoutBuildCounter : UnityEngine.EventSystems.UIBehaviour, ILayoutGroup
    {
        public int VerticalBuilds;
        public void SetLayoutHorizontal() { }
        public void SetLayoutVertical() { VerticalBuilds++; }
        protected override void OnRectTransformDimensionsChange() => LayoutRebuilder.MarkLayoutForRebuild((RectTransform)transform);
    }
}
