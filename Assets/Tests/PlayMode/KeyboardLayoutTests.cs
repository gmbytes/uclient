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
        [TearDown] public void Cleanup() { if(root!=null) Object.DestroyImmediate(root); }
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
    }
}
