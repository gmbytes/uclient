using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Client.Bootstrap;
using Client.Framework.Release;
using Client.Framework.UI;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace Client.Tests.EditMode
{
    [Category("D1")]
    public sealed class NativeWindowIdentityTests
    {
        [Test] public void NativeSampleIncludesActualLoginInputComponent()
        {
            string root=Path.GetFullPath("Builds/Tests/D1/LoginInput/"+Guid.NewGuid().ToString("N"));
            var window=UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Content/UI/Login/UILogin.prefab"));
            var hostObject=new GameObject("D1 input sampler",typeof(NativeClientHost));
            var host=hostObject.GetComponent<NativeClientHost>();
            var options=NativeAcceptanceOptions.Parse(true,"{\"runId\":\"input-identity\",\"caseId\":\"login-prefab\"}");
            try
            {
                var input=window.GetComponentInChildren<InputField>(true);Assert.That(input,Is.Not.Null);
                Assert.That(UnityEngine.Object.FindObjectsOfType<InputField>().Length,Is.EqualTo(1));
                using(var evidence=new NativeEvidence(root,options))
                {
                    typeof(NativeClientHost).GetField("options",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(host,options);
                    typeof(NativeClientHost).GetField("evidence",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(host,evidence);
                    typeof(NativeClientHost).GetMethod("LateUpdate",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(host,null);
                    string text;using(var stream=new FileStream(Path.Combine(evidence.DirectoryPath,"events.jsonl"),FileMode.Open,FileAccess.Read,FileShare.ReadWrite))using(var reader=new StreamReader(stream))text=reader.ReadToEnd();
                    var sample=text.Split('\n').Where(line=>!string.IsNullOrWhiteSpace(line)).Select(JObject.Parse).Single(item=>(string)item["kind"]=="ui.sample");
                    var observed=sample["data"]["snapshot"]["input"];
                    Assert.That(observed.Type,Is.EqualTo(JTokenType.Object),"The shipped Login prefab's real InputField must be observed.");
                    Assert.That((int)observed["id"],Is.EqualTo(input.GetInstanceID()));
                    Assert.That((int)observed["length"],Is.EqualTo(input.text.Length));
                    Assert.That((int)observed["caret"],Is.EqualTo(input.caretPosition));
                    Assert.That((int)observed["selectionAnchor"],Is.EqualTo(input.selectionAnchorPosition));
                    Assert.That((int)observed["selectionFocus"],Is.EqualTo(input.selectionFocusPosition));
                    Assert.That((bool)observed["focused"],Is.EqualTo(input.isFocused));
                    Assert.That((string)observed["textSha256"],Is.EqualTo(Client.Framework.HotUpdate.HotfixLoadPlan.Hash(System.Text.Encoding.UTF8.GetBytes(input.text))));
                    Assert.That(observed["bounds"].Type,Is.EqualTo(JTokenType.Object));
                }
            }
            finally{UnityEngine.Object.DestroyImmediate(hostObject);UnityEngine.Object.DestroyImmediate(window);}
        }
        [Test] public void NativeSampleUsesActualViewRootGameObjectIdentityWithinAttempt()
        {
            string root=Path.GetFullPath("Builds/Tests/D1/ViewIdentity/"+Guid.NewGuid().ToString("N"));
            var window=new GameObject("D1 canonical view root",typeof(RectTransform),typeof(UIViewBridge));
            var hostObject=new GameObject("D1 sampler",typeof(NativeClientHost));
            var host=hostObject.GetComponent<NativeClientHost>();var bridge=window.GetComponent<UIViewBridge>();
            var options=NativeAcceptanceOptions.Parse(true,"{\"runId\":\"view-identity\",\"caseId\":\"same-window\"}");
            try
            {
                Assert.That(UnityEngine.Object.FindObjectsOfType<UIViewBridge>().Length,Is.EqualTo(1),"The observation fixture must own the only active view bridge.");
                Assert.That(window.GetInstanceID(),Is.Not.EqualTo(bridge.GetInstanceID()),"The real GameObject and component must have distinct Unity identities.");
                using(var evidence=new NativeEvidence(root,options))
                {
                    typeof(NativeClientHost).GetField("options",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(host,options);
                    typeof(NativeClientHost).GetField("evidence",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(host,evidence);
                    typeof(NativeClientHost).GetMethod("LateUpdate",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(host,null);
                    string text;using(var input=new FileStream(Path.Combine(evidence.DirectoryPath,"events.jsonl"),FileMode.Open,FileAccess.Read,FileShare.ReadWrite))using(var reader=new StreamReader(input))text=reader.ReadToEnd();
                    var sample=text.Split('\n').Where(line=>!string.IsNullOrWhiteSpace(line)).Select(JObject.Parse).Single(item=>(string)item["kind"]=="ui.sample");
                    Assert.That((string)sample["attemptId"],Is.EqualTo(evidence.AttemptId));Assert.That((int)sample["processId"],Is.EqualTo(System.Diagnostics.Process.GetCurrentProcess().Id));
                    Assert.That((int)sample["data"]["snapshot"]["view"],Is.EqualTo(window.GetInstanceID()),"CLIENT_VIEW.view uses opened.Root, which is Bridge.gameObject.");
                    Assert.That((int)sample["data"]["snapshot"]["view"],Is.Not.EqualTo(bridge.GetInstanceID()));
                }
            }
            finally{UnityEngine.Object.DestroyImmediate(hostObject);UnityEngine.Object.DestroyImmediate(window);}
        }
    }
}
