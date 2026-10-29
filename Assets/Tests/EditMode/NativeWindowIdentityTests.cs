using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Client.Bootstrap;
using Client.Framework.Release;
using Client.Framework.UI;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;

namespace Client.Tests.EditMode
{
    [Category("D1")]
    public sealed class NativeWindowIdentityTests
    {
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
