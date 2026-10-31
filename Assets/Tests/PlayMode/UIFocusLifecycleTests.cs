using System.Collections;
using System.Reflection;
using Client.Framework.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace Client.Tests
{
    [Category("U2")]
    public sealed class UIFocusLifecycleTests
    {
        GameObject root, eventSystem, otherRoot;
        Component lifecycle;
        InputField input;

        void Create()
        {
            eventSystem=new GameObject("EventSystem",typeof(EventSystem),typeof(StandaloneInputModule));
            root=new GameObject("VisibleView",typeof(RectTransform),typeof(Canvas),typeof(GraphicRaycaster));
            root.GetComponent<Canvas>().renderMode=RenderMode.ScreenSpaceOverlay;
            input=Field(root.transform,"Account");
            lifecycle=root.AddComponent(AdaptiveLayoutTests.Required("UIFocusLifecycle"));
        }
        static InputField Field(Transform parent,string name)
        {
            var node=AdaptiveLayoutTests.Child(name,parent);node.sizeDelta=new Vector2(400,100);node.gameObject.AddComponent<Image>();
            var textNode=AdaptiveLayoutTests.Child("Text",node);var text=textNode.gameObject.AddComponent<Text>();
            text.font=Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            var field=node.gameObject.AddComponent<InputField>();field.textComponent=text;field.text="retained input";return field;
        }
        void Pause(bool value)=>lifecycle.GetType().GetMethod("OnApplicationPause",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(lifecycle,new object[]{value});
        IEnumerator Focus()
        {
            input.Select();input.ActivateInputField();yield return null;
            input.caretPosition=2;input.selectionAnchorPosition=5;input.selectionFocusPosition=1;
            yield return null;Assert.That(input.isFocused,Is.True);
        }
        [TearDown] public void Cleanup()
        {
            if(root!=null)Object.DestroyImmediate(root);if(otherRoot!=null)Object.DestroyImmediate(otherRoot);if(eventSystem!=null)Object.DestroyImmediate(eventSystem);
        }
        [UnityTest] public IEnumerator ResumeRestoresOriginalInputAndReverseSelectionAfterPlatformDeactivation()
        {
            Create();yield return Focus();int id=input.GetInstanceID();string text=input.text;
            int caret=input.caretPosition,anchor=input.selectionAnchorPosition,focus=input.selectionFocusPosition;
            Pause(true);input.DeactivateInputField();Pause(false);
            yield return null;yield return null;yield return null;
            Assert.That(input.GetInstanceID(),Is.EqualTo(id));Assert.That(input.text,Is.EqualTo(text));Assert.That(input.isFocused,Is.True);
            Assert.That(EventSystem.current.currentSelectedGameObject,Is.EqualTo(input.gameObject));
            Assert.That(input.caretPosition,Is.EqualTo(caret));Assert.That(input.selectionAnchorPosition,Is.EqualTo(anchor));Assert.That(input.selectionFocusPosition,Is.EqualTo(focus));
        }
        [UnityTest] public IEnumerator ClosingAndReusingViewDiscardsPendingFocusRestoration()
        {
            Create();yield return Focus();Pause(true);input.DeactivateInputField();root.SetActive(false);root.SetActive(true);Pause(false);
            yield return null;yield return null;yield return null;Assert.That(input.isFocused,Is.False);
        }
        [UnityTest] public IEnumerator LatePlatformDeactivationAfterResumeStillRestoresEditing()
        {
            Create();yield return Focus();int anchor=input.selectionAnchorPosition,focus=input.selectionFocusPosition;
            Pause(true);Pause(false);yield return null;
            input.DeactivateInputField();yield return null;yield return null;yield return null;
            Assert.That(input.isFocused,Is.True,"Android may deactivate the old keyboard after the resume callback.");
            Assert.That(input.selectionAnchorPosition,Is.EqualTo(anchor));Assert.That(input.selectionFocusPosition,Is.EqualTo(focus));
        }
        [UnityTest] public IEnumerator ResumeDoesNotStealSelectionFromAnotherView()
        {
            Create();yield return Focus();Pause(true);input.DeactivateInputField();
            otherRoot=new GameObject("OtherView",typeof(RectTransform));var other=Field(otherRoot.transform,"OtherInput");other.Select();other.ActivateInputField();yield return null;
            Pause(false);yield return null;yield return null;yield return null;
            Assert.That(EventSystem.current.currentSelectedGameObject,Is.EqualTo(other.gameObject));Assert.That(other.isFocused,Is.True);Assert.That(input.isFocused,Is.False);
        }
        [UnityTest] public IEnumerator UnfocusedViewDoesNotOpenInputOnResume()
        {
            Create();yield return null;Pause(true);Pause(false);yield return null;yield return null;
            Assert.That(input.isFocused,Is.False);
        }
    }
}
