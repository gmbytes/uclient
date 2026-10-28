#if UNITY_EDITOR
using System;
using System.Collections;
using System.Reflection;
using Client.Framework.UI;
using Client.Framework.Release;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Object=UnityEngine.Object;

namespace Client.Tests
{
    // Geometry/action tests only; these do not claim a displayed login frame.
    [Category("F7")]
    public sealed class BootstrapStartupPresentationTests
    {
        private GameObject root;
        private Component status;
        private int retries,exits;
        [SetUp] public void SetUp()
        {
            root=Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/App/UIUpdate.prefab"));
            status=root.GetComponent(Type.GetType("Client.Bootstrap.UIUpdate, Client.Bootstrap"));
            status.GetType().GetMethod("ConfigureActions").Invoke(status,new object[]{(Action)(()=>retries++),(Action)(()=>exits++)});
        }
        [TearDown] public void TearDown(){if(root!=null)Object.DestroyImmediate(root);retries=exits=0;}
        [UnityTest] public IEnumerator StartupStatusUsesSharedSafeAreaAndDoesNotObscureWholePage()
        {
            var screen=root.GetComponent<ScreenLayoutComponent>();Assert.That(screen,Is.Not.Null,"AOT startup must use the shared adaptive root policy.");screen.enabled=false;
            screen.Service.Evaluate(new ScreenMetrics(720,1280,new Rect(40,90,650,1100)));screen.Service.FlushPending(Time.frameCount);Canvas.ForceUpdateCanvases();
            var label=Array.Find(root.GetComponentsInChildren<Text>(true),value=>value.name=="StatusLabel");var safe=(RectTransform)label.transform.parent;
            Assert.That(safe.anchorMin.x,Is.EqualTo(40f/720).Within(.0001f));Assert.That(safe.anchorMin.y,Is.EqualTo(90f/1280).Within(.0001f));
            Assert.That(safe.anchorMax.x,Is.EqualTo(690f/720).Within(.0001f));Assert.That(safe.anchorMax.y,Is.EqualTo(1190f/1280).Within(.0001f));
            foreach(var button in root.GetComponentsInChildren<Button>(true))Assert.That(button.transform.parent,Is.SameAs(safe),"Startup actions must consume the same safe-area policy as status text.");
            Assert.That(label.rectTransform.anchorMin.y,Is.EqualTo(1));Assert.That(label.rectTransform.rect.height,Is.LessThanOrEqualTo(100));Assert.That(label.raycastTarget,Is.False);
            Assert.That(root.GetComponent<Image>(),Is.Null,"No opaque full-screen startup overlay may cover the local ready view.");yield return null;
        }
        [Test] public void StartupErrorHasBoundedRetryAndExitActions()
        {
            status.GetType().GetMethod("ConfigureActions").Invoke(status,new object[]{(Action)(()=>retries++),(Action)(()=>exits++)});
            status.GetType().GetMethod("ShowFailure").Invoke(status,new object[]{"Restart required",false});
            var buttons=root.GetComponentsInChildren<Button>(true);Assert.That(buttons.Length,Is.EqualTo(2));
            var retry=Array.Find(buttons,button=>button.name=="Retry");var exit=Array.Find(buttons,button=>button.name=="Exit");
            Assert.That(retry.gameObject.activeSelf,Is.True);Assert.That(retry.interactable,Is.False);Assert.That(exit.gameObject.activeSelf,Is.True);
            status.GetType().GetMethod("ShowFailure").Invoke(status,new object[]{"Prepare failed",true});retry.onClick.Invoke();exit.onClick.Invoke();
            Assert.That(retries,Is.EqualTo(1));Assert.That(exits,Is.EqualTo(1));
        }
        [Test] public void LegacyBuiltStartupAlsoUsesSharedAdaptivePolicy()
        {
            Object.DestroyImmediate(root);
            root=new GameObject("Legacy built startup",typeof(RectTransform),typeof(Canvas),typeof(CanvasScaler));
            var labelObject=new GameObject("StatusLabel",typeof(RectTransform),typeof(Text));labelObject.transform.SetParent(root.transform,false);
            var type=Type.GetType("Client.Bootstrap.UIUpdate, Client.Bootstrap");status=root.AddComponent(type);
            type.GetField("m_Status",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(status,labelObject.GetComponent<Text>());
            type.GetMethod("ConfigureActions").Invoke(status,new object[]{(Action)(()=>retries++),(Action)(()=>exits++)});
            Assert.That(root.GetComponent<AdaptiveCanvas>(),Is.Not.Null,"Build-owned legacy startup scaffolds must still use shared root policy.");
            Assert.That(labelObject.GetComponentInParent<SafeAreaRoot>(),Is.Not.Null);
        }
        [Test] public void PreparationCanExitWithoutOfferingRetry()
        {
            var phase=new ReleaseDiagnostics();typeof(ReleaseDiagnostics).GetProperty("Phase").SetValue(phase,ReleasePhase.Prepare);
            status.GetType().GetMethod("SetPhase").Invoke(status,new object[]{phase,true});
            var buttons=root.GetComponentsInChildren<Button>(true);var retry=Array.Find(buttons,button=>button.name=="Retry");var exit=Array.Find(buttons,button=>button.name=="Exit");
            Assert.That(retry.gameObject.activeSelf,Is.False);Assert.That(exit.gameObject.activeSelf,Is.True,"Preparing an update must remain cancellable through Exit.");exit.onClick.Invoke();Assert.That(exits,Is.EqualTo(1));
        }
    }
}
#endif
