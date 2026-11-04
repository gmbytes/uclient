using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using UnityEditor;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;

namespace Client.Tests.EditMode
{
    internal static class MainCityVerification
    {
        static TestRunnerApi api;
        [InitializeOnLoadMethod]
        static void Initialize()
        {
            api=ScriptableObject.CreateInstance<TestRunnerApi>();
            api.RegisterCallbacks(new Results());
            const string request="Builds/MainCity/verify.request";
            if(!File.Exists(request))return;
            EditorApplication.delayCall+=()=>
            {
                if(EditorApplication.isPlaying)return;
                File.Delete(request);
                api.Execute(new ExecutionSettings(new Filter{testMode=TestMode.PlayMode}));
            };
        }
        sealed class Results:ICallbacks
        {
            public void RunStarted(ITestAdaptor tests){}
            public void TestStarted(ITestAdaptor test){}
            public void TestFinished(ITestResultAdaptor result){}
            public void RunFinished(ITestResultAdaptor result)
            {
                var failures=new List<object>();Collect(result,failures);
                Directory.CreateDirectory("Builds/MainCity");
                File.WriteAllText("Builds/MainCity/verification-result.json",JsonConvert.SerializeObject(new{result=result.ResultState,passed=result.PassCount,failed=result.FailCount,skipped=result.SkipCount,inconclusive=result.InconclusiveCount,failures},Formatting.Indented));
            }
            static void Collect(ITestResultAdaptor result,List<object> failures)
            {
                if(result.HasChildren){foreach(var child in result.Children)Collect(child,failures);}
                else if(result.ResultState.StartsWith("Failed"))failures.Add(new{test=result.FullName,result.Message,result.StackTrace});
            }
        }
    }
}
