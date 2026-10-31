using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace Client.Tests.PlayMode
{
    [Category("D1")]
    public sealed class NativeStartupFailureTests
    {
        private GameObject view, host, presentationRoot;
        private TextAsset invalidProfile;
        private Text status;
        private Component native;

        [SetUp] public void SetUp()
        {
            view=Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/App/UIUpdate.prefab"));
            var update=view.GetComponent(Type.GetType("Client.Bootstrap.UIUpdate, Client.Bootstrap"));
            status=(Text)update.GetType().GetField("m_Status",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(update);
            host=new GameObject("Native startup failure");host.SetActive(false);
            var bootstrap=(Behaviour)host.AddComponent(Type.GetType("Client.Bootstrap.ClientBootstrap, Client.Bootstrap"));
            bootstrap.GetType().GetField("m_UpdateUI",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(bootstrap,update);
            bootstrap.enabled=false; // The real Player builder defers it until native composition is configured.
            native=host.AddComponent(Type.GetType("Client.Bootstrap.NativeClientHost, Client.Bootstrap"));
        }

        [TearDown] public void TearDown()
        {
            if(host!=null)Object.DestroyImmediate(host);
            if(view!=null)Object.DestroyImmediate(view);
            if(presentationRoot!=null)Object.DestroyImmediate(presentationRoot);
            if(invalidProfile!=null)Object.DestroyImmediate(invalidProfile);
        }

        [UnityTest] public IEnumerator MissingNativeInputsShowFailureInsteadOfInitializing()
        {
            LogAssert.Expect(LogType.Exception,new Regex("Native profile and UI root must be baked"));
            host.SetActive(true);yield return null;yield return null;
            AssertFailureIsVisible();
        }

        [UnityTest] public IEnumerator InvalidNativeProfileShowsGenericFailureWithoutRawInput()
        {
            invalidProfile=new TextAsset("{\"synthetic-secret\":\"not-for-the-page\"}");
            presentationRoot=new GameObject("Unused presentation input");
            native.GetType().GetField("m_PublicProfile",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(native,invalidProfile);
            native.GetType().GetField("m_UIRoot",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(native,presentationRoot);
            LogAssert.Expect(LogType.Exception,new Regex("Invalid native profile schema"));
            host.SetActive(true);yield return null;yield return null;
            AssertFailureIsVisible();
            Assert.That(status.text,Does.Not.Contain("synthetic-secret").And.Not.Contain("not-for-the-page"));
        }

        private void AssertFailureIsVisible()
        {
            Assert.That(status.text,Does.Contain("failed").IgnoreCase,"A terminal setup failure must replace the pending status.");
            Assert.That(status.color,Is.EqualTo(Color.red));
            var buttons=view.GetComponentsInChildren<Button>(true);
            var retry=buttons.Single(button=>button.name=="Retry");
            var exit=buttons.Single(button=>button.name=="Exit");
            Assert.That(retry.IsInteractable(),Is.False,"Unconfigured native composition cannot safely retry in place.");
            Assert.That(exit.gameObject.activeInHierarchy&&exit.IsInteractable(),Is.True);
        }
    }
}
