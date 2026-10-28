using System.Collections;
using Client.Framework.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.TestTools;

namespace Client.Tests
{
    [Category("U2")]
    public sealed class KeyboardLayoutTests
    {
        private GameObject root;
        private GameObject events;
        private AdaptiveLayoutDefinition definition;
        [TearDown] public void Cleanup() { if(root!=null) Object.DestroyImmediate(root); if(events!=null) Object.DestroyImmediate(events); if(definition!=null) Object.DestroyImmediate(definition); }
        [TestCase(true)] [TestCase(false)]
        public void KeyboardGeometryPreservesScrollAndRevealsTarget(bool validGeometry)
        {
            root=new GameObject("KeyboardRoot",typeof(RectTransform));
            var canvas=(RectTransform)root.transform; canvas.sizeDelta=new Vector2(720,1280);
            var viewport=AdaptiveLayoutTests.Child("Viewport",canvas);
            viewport.anchorMin=Vector2.zero; viewport.anchorMax=Vector2.one; viewport.sizeDelta=Vector2.zero;
            var content=AdaptiveLayoutTests.Child("Content",viewport);
            content.anchorMin=new Vector2(0,1); content.anchorMax=Vector2.one; content.pivot=new Vector2(.5f,1); content.sizeDelta=new Vector2(0,1600);
            var submit=AdaptiveLayoutTests.Child("Submit",content);
            submit.anchorMin=submit.anchorMax=new Vector2(.5f,0); submit.pivot=new Vector2(.5f,0); submit.sizeDelta=new Vector2(300,96);
            var scroll=viewport.gameObject.AddComponent<ScrollRect>(); scroll.viewport=viewport; scroll.content=content;
            var keyboard=root.AddComponent(AdaptiveLayoutTests.Required("KeyboardVisibilityController"));
            AdaptiveLayoutTests.Call(keyboard,"Configure",scroll,submit);
            AdaptiveLayoutTests.Call(keyboard,"Apply",AdaptiveLayoutTests.Snapshot(720,1280,new Rect(0,0,720,1280)),validGeometry?new Rect(0,0,720,500):Rect.zero,submit);
            Assert.That(scroll.vertical,Is.True);
            Assert.That(viewport.rect.height,Is.EqualTo(validGeometry?780:1280).Within(1));
            if(!validGeometry) { scroll.verticalNormalizedPosition=0; }
            var target=new Vector3[4]; var visible=new Vector3[4]; submit.GetWorldCorners(target); viewport.GetWorldCorners(visible);
            Assert.That(target[0].y,Is.GreaterThanOrEqualTo(visible[0].y-1));
            Assert.That(target[2].y,Is.LessThanOrEqualTo(visible[2].y+1));
            AdaptiveLayoutTests.Call(keyboard,"Apply",AdaptiveLayoutTests.Snapshot(720,1280,new Rect(0,0,720,1280)),Rect.zero,null);
            Assert.That(viewport.rect.height,Is.EqualTo(1280).Within(1),"Closing keyboard restores base viewport.");
        }
        [UnityTest]
        public IEnumerator InjectedKeyboardGeometryRevealsFocusedFieldBeforePrimaryAction()
        {
            root=new GameObject("KeyboardHost",typeof(RectTransform));
            ((RectTransform)root.transform).sizeDelta=new Vector2(720,1280);
            var viewport=AdaptiveLayoutTests.Child("Viewport",root.transform);
            viewport.anchorMin=Vector2.zero;viewport.anchorMax=Vector2.one;viewport.sizeDelta=Vector2.zero;
            var content=AdaptiveLayoutTests.Child("Content",viewport);
            content.anchorMin=new Vector2(0,1);content.anchorMax=Vector2.one;content.pivot=new Vector2(.5f,1);content.sizeDelta=new Vector2(0,1600);
            var field=AdaptiveLayoutTests.Child("FocusedField",content);
            field.anchorMin=field.anchorMax=new Vector2(.5f,1);field.pivot=new Vector2(.5f,1);field.sizeDelta=new Vector2(300,100);field.anchoredPosition=new Vector2(0,-950);
            var primary=AdaptiveLayoutTests.Child("Primary",content);
            primary.anchorMin=primary.anchorMax=new Vector2(.5f,0);primary.pivot=new Vector2(.5f,0);primary.sizeDelta=new Vector2(300,96);
            var scroll=viewport.gameObject.AddComponent<ScrollRect>();scroll.viewport=viewport;scroll.content=content;
            var controller=root.AddComponent<KeyboardVisibilityController>();controller.Configure(scroll,primary);
            var keyboard=new Rect(0,0,720,500);
            controller.KeyboardAreaSource=()=>keyboard;controller.FocusSource=()=>field;
            controller.SetLayoutSnapshot(AdaptiveLayoutTests.Snapshot(720,1280,new Rect(0,0,720,1280)));
            yield return null;
            var visible=RectTransformUtility.CalculateRelativeRectTransformBounds(viewport,field);
            Assert.That(visible.min.y,Is.GreaterThanOrEqualTo(viewport.rect.yMin-1));
            Assert.That(visible.max.y,Is.LessThanOrEqualTo(viewport.rect.yMax+1));
            Assert.That(viewport.rect.height,Is.EqualTo(780).Within(1));
            keyboard=Rect.zero;yield return null;
            Assert.That(viewport.rect.height,Is.EqualTo(1280).Within(1));Assert.That(scroll.vertical,Is.True);
        }
        internal static void SetKeyboardVisible(KeyboardVisibilityController controller, System.Func<bool> visible) => controller.KeyboardVisibleSource=visible;

        private sealed class ManualFixture
        {
            public RectTransform Viewport,Content,Primary;
            public ScrollRect Scroll;
            public KeyboardVisibilityController Controller;
        }

        private ManualFixture CreateManualFixture(int width,int height,float contentHeight)
        {
            root=new GameObject("ManualKeyboardRoot",typeof(RectTransform));
            ((RectTransform)root.transform).sizeDelta=new Vector2(width,height);
            var viewport=AdaptiveLayoutTests.Child("Viewport",root.transform);
            viewport.anchorMin=Vector2.zero; viewport.anchorMax=Vector2.one; viewport.sizeDelta=Vector2.zero;
            var content=AdaptiveLayoutTests.Child("Content",viewport);
            content.anchorMin=new Vector2(0,1); content.anchorMax=Vector2.one; content.pivot=new Vector2(.5f,1);
            content.sizeDelta=new Vector2(0,contentHeight);
            var primary=AdaptiveLayoutTests.Child("Primary",content);
            primary.anchorMin=primary.anchorMax=new Vector2(.5f,0); primary.pivot=new Vector2(.5f,0); primary.sizeDelta=new Vector2(300,96);
            var scroll=viewport.gameObject.AddComponent<ScrollRect>(); scroll.viewport=viewport; scroll.content=content;
            scroll.movementType=ScrollRect.MovementType.Clamped; scroll.inertia=false; scroll.scrollSensitivity=100;
            var controller=root.AddComponent<KeyboardVisibilityController>(); controller.Configure(scroll,primary);
            return new ManualFixture {Viewport=viewport,Content=content,Primary=primary,Scroll=scroll,Controller=controller};
        }

        internal static void ScrollUp(ScrollRect scroll)
        {
            scroll.OnScroll(new UnityEngine.EventSystems.PointerEventData(null) {scrollDelta=new Vector2(0,-100)});
        }

        private static void AssertActionInUpperViewport(ManualFixture fixture)
        {
            var bounds=RectTransformUtility.CalculateRelativeRectTransformBounds(fixture.Viewport,fixture.Primary);
            Assert.That(bounds.min.y,Is.GreaterThanOrEqualTo(fixture.Viewport.rect.center.y-1),"Manual scrolling must lift the bottom action above a lower-half occlusion.");
            Assert.That(bounds.max.y,Is.LessThanOrEqualTo(fixture.Viewport.rect.yMax+1),"The complete action must remain reachable.");
        }

        [TestCase(1280,1280,0)] [TestCase(1280,1280,1)] [TestCase(1280,1280,2)]
        [TestCase(480,960,0)] [TestCase(480,960,1)] [TestCase(480,960,2)]
        public void VisibleUnmeasurableKeyboardAllowsRealManualScroll(int height,int contentHeight,int invalidArea)
        {
            var fixture=CreateManualFixture(720,height,contentHeight);
            SetKeyboardVisible(fixture.Controller,()=>true);
            Rect area=invalidArea==0?Rect.zero:invalidArea==1?new Rect(float.NaN,0,720,400):new Rect(0,0,720,height+1);
            var snapshot=AdaptiveLayoutTests.Snapshot(720,height,new Rect(0,0,720,height));
            fixture.Controller.Apply(snapshot,area,null);
            Assert.That(fixture.Viewport.rect.height,Is.EqualTo(height),"Fallback must not pretend to have measured the keyboard viewport.");
            Assert.That(fixture.Content.rect.height,Is.EqualTo(contentHeight));
            ScrollUp(fixture.Scroll);
            fixture.Controller.Apply(snapshot,area,null);
            AssertActionInUpperViewport(fixture);
            float position=fixture.Content.anchoredPosition.y;
            for(int frame=0;frame<20;frame++) fixture.Controller.Apply(snapshot,area,null);
            Assert.That(fixture.Content.anchoredPosition.y,Is.EqualTo(position).Within(.01f),"Fallback allowance must not accumulate each frame.");
            Assert.That(fixture.Content.rect.height,Is.EqualTo(contentHeight));
        }

        [Test]
        public void ClosingUnmeasurableKeyboardRestoresOriginalScrollPolicyAndBounds()
        {
            var fixture=CreateManualFixture(720,1280,1280);
            bool visible=true; SetKeyboardVisible(fixture.Controller,()=>visible);
            var snapshot=AdaptiveLayoutTests.Snapshot(720,1280,new Rect(0,0,720,1280));
            fixture.Controller.Apply(snapshot,Rect.zero,null); ScrollUp(fixture.Scroll); fixture.Controller.Apply(snapshot,Rect.zero,null);
            AssertActionInUpperViewport(fixture);
            visible=false; fixture.Controller.Apply(snapshot,Rect.zero,null);
            Assert.That(fixture.Scroll.movementType,Is.EqualTo(ScrollRect.MovementType.Clamped));
            Assert.That(fixture.Viewport.rect.height,Is.EqualTo(1280)); Assert.That(fixture.Content.rect.height,Is.EqualTo(1280));
            Assert.That(fixture.Content.anchoredPosition.y,Is.EqualTo(0).Within(.01f));
            ScrollUp(fixture.Scroll);
            Assert.That(fixture.Content.anchoredPosition.y,Is.EqualTo(0).Within(.01f),"Closed keyboard must remove temporary travel.");
        }

        [Test]
        public void MeasuredAreaAfterFallbackRestoresClampingAndRevealsAction()
        {
            var fixture=CreateManualFixture(720,1280,1600);
            bool visible=true; SetKeyboardVisible(fixture.Controller,()=>visible);
            var snapshot=AdaptiveLayoutTests.Snapshot(720,1280,new Rect(0,0,720,1280));
            fixture.Controller.Apply(snapshot,Rect.zero,null); ScrollUp(fixture.Scroll); fixture.Controller.Apply(snapshot,Rect.zero,null);
            AssertActionInUpperViewport(fixture);
            fixture.Controller.Apply(snapshot,new Rect(0,0,720,500),fixture.Primary);
            Assert.That(fixture.Scroll.movementType,Is.EqualTo(ScrollRect.MovementType.Clamped));
            Assert.That(fixture.Viewport.rect.height,Is.EqualTo(780).Within(1));
            var bounds=RectTransformUtility.CalculateRelativeRectTransformBounds(fixture.Viewport,fixture.Primary);
            Assert.That(bounds.min.y,Is.GreaterThanOrEqualTo(fixture.Viewport.rect.yMin-1));
            Assert.That(bounds.max.y,Is.LessThanOrEqualTo(fixture.Viewport.rect.yMax+1));
            visible=false; fixture.Controller.Apply(snapshot,new Rect(0,0,720,500),null);
            Assert.That(fixture.Viewport.rect.height,Is.EqualTo(1280),"Explicit closed visibility must win over stale area.");
            Assert.That(fixture.Content.rect.height,Is.EqualTo(1600));
        }

        [UnityTest]
        public IEnumerator RotationWhileUnmeasurableRestoresLayoutBoundsAndPreservesInputFocus()
        {
            var fixture=CreateManualFixture(720,1280,1280);
            definition=ScriptableObject.CreateInstance<AdaptiveLayoutDefinition>();
            var brand=AdaptiveLayoutTests.Child("Brand",root.transform);
            var layout=root.AddComponent<AdaptiveLayout>();
            layout.Configure(definition,brand,fixture.Viewport,fixture.Scroll,fixture.Content,fixture.Primary);
            var inputRect=AdaptiveLayoutTests.Child("Input",fixture.Content);
            inputRect.anchorMin=inputRect.anchorMax=new Vector2(.5f,1); inputRect.sizeDelta=new Vector2(300,96);inputRect.anchoredPosition=new Vector2(0,-120);
            var text=AdaptiveLayoutTests.Child("Text",inputRect).gameObject.AddComponent<Text>();text.font=Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            var input=inputRect.gameObject.AddComponent<InputField>();input.textComponent=text;input.text="retained through fallback";
            events=new GameObject("KeyboardEvents",typeof(UnityEngine.EventSystems.EventSystem),typeof(UnityEngine.EventSystems.StandaloneInputModule));
            input.Select();input.ActivateInputField();yield return null;Assert.That(input.isFocused,Is.True);
            int inputId=input.GetInstanceID();bool visible=true;SetKeyboardVisible(fixture.Controller,()=>visible);
            var portrait=AdaptiveLayoutTests.Snapshot(720,1280,new Rect(0,0,720,1280));
            layout.Apply(portrait);fixture.Controller.SetLayoutSnapshot(portrait);fixture.Controller.Apply(portrait,Rect.zero,inputRect);
            ScrollUp(fixture.Scroll);fixture.Controller.Apply(portrait,Rect.zero,inputRect);AssertActionInUpperViewport(fixture);
            ((RectTransform)root.transform).sizeDelta=new Vector2(1280,720);
            var landscape=AdaptiveLayoutTests.Snapshot(1280,720,new Rect(0,0,1280,720));
            layout.Apply(landscape);float height=fixture.Content.rect.height;float viewHeight=fixture.Viewport.rect.height;
            fixture.Controller.SetLayoutSnapshot(landscape);fixture.Controller.Apply(landscape,Rect.zero,inputRect);
            ScrollUp(fixture.Scroll);fixture.Controller.Apply(landscape,Rect.zero,inputRect);AssertActionInUpperViewport(fixture);
            for(int frame=0;frame<20;frame++) fixture.Controller.Apply(landscape,Rect.zero,inputRect);
            Assert.That(fixture.Content.rect.height,Is.EqualTo(height));Assert.That(fixture.Viewport.rect.height,Is.EqualTo(viewHeight));
            visible=false;fixture.Controller.Apply(landscape,Rect.zero,inputRect);yield return null;
            Assert.That(fixture.Scroll.movementType,Is.EqualTo(ScrollRect.MovementType.Clamped));
            Assert.That(fixture.Content.rect.height,Is.EqualTo(height));Assert.That(input.GetInstanceID(),Is.EqualTo(inputId));
            Assert.That(input.text,Is.EqualTo("retained through fallback"));Assert.That(input.isFocused,Is.True);
            var contentBounds=RectTransformUtility.CalculateRelativeRectTransformBounds(fixture.Viewport,fixture.Content);
            Assert.That(contentBounds.max.y,Is.GreaterThanOrEqualTo(fixture.Viewport.rect.yMax-1));
            Assert.That(contentBounds.min.y,Is.LessThanOrEqualTo(fixture.Viewport.rect.yMin+1));
        }
    }
}
