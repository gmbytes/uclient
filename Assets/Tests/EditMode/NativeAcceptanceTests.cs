using System;
using System.IO;
using System.Reflection;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Client.Contracts;
using Client.Framework.Network;
using Client.Framework.Release;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace Client.Tests.EditMode
{
    [Category("D1")]
    public sealed class NativeAcceptanceTests
    {
        private string root;
        [SetUp] public void Setup() { root=Path.GetFullPath("Builds/Tests/D1/Acceptance/"+Guid.NewGuid().ToString("N")); }
        [TearDown] public void Cleanup() { if(Directory.Exists(root))Directory.Delete(root,true); }
        private static Type Required(string name) { var type=Type.GetType("Client.Framework.Release."+name+", Client.Framework");Assert.That(type,Is.Not.Null,"D1 native observation boundary missing: "+name);return type; }
        private static object Options(bool allowed,string json)
        {try{return Required("NativeAcceptanceOptions").GetMethod("Parse").Invoke(null,new object[]{allowed,json});}catch(TargetInvocationException e){System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(e.InnerException).Throw();throw;}}
        [TestCase("pausePoint","rename.before"),TestCase("lowSpace",true),TestCase("invalidCredential",true)]
        public void ProductionProfileCannotEnableAcceptanceControls(string property,object value)
        {
            string json=new JObject{["runId"]="run",["caseId"]="case",[property]=JToken.FromObject(value)}.ToString();
            Assert.Throws<InvalidDataException>(()=>Options(false,json));
        }
        [Test] public void StatePauseRequiresExactWritePurpose()
        {Assert.Throws<InvalidDataException>(()=>Options(true,new JObject{["runId"]="run",["caseId"]="case",["pausePoint"]="state.replace.after"}.ToString()));}
        [Test] public void ActualStateReplacementEmitsPurposeAndDurableProcessCorrelation()
        {
            var options=Options(true,"{\"runId\":\"run\",\"caseId\":\"case\"}");
            using(var evidence=(IDisposable)Activator.CreateInstance(Required("NativeEvidence"),root,options))
            {
                var state=new ReleaseStateStore(Path.Combine(root,"store"),new ManifestVerifier(),new PlayerIdentity("player","StandaloneWindows64","x64","2022.3.25f1","1",new string('a',64),"metadata"),(IFileFaultInjector)evidence);
                state.WriteAtomic(new ReleaseState());
                Assert.That(state.Read().Attempt,Is.EqualTo(0));
                string[] records;using(var input=new FileStream(Path.Combine(root,"events.jsonl"),FileMode.Open,FileAccess.Read,FileShare.ReadWrite))using(var reader=new StreamReader(input))records=reader.ReadToEnd().Split('\n');
                var before=Array.Find(records,line=>line.Contains("state.replace.before"));var after=Array.Find(records,line=>line.Contains("state.replace.after"));
                Assert.That(before,Is.Not.Null);Assert.That(after,Is.Not.Null);
                var parsed=JObject.Parse(after);Assert.That((string)parsed["runId"],Is.EqualTo("run"));Assert.That((string)parsed["caseId"],Is.EqualTo("case"));
                Assert.That((int)parsed["processId"],Is.EqualTo(System.Diagnostics.Process.GetCurrentProcess().Id));Assert.That((string)parsed["data"]["purpose"],Is.EqualTo("write"));
                Assert.That((int)parsed["data"]["state"]["Attempt"],Is.EqualTo(0));
            }
        }
        [Test] public void PartialTransferRequiresAuthenticatedPathAndIncompletePositiveLength()
        {
            var method=typeof(NativeEvidence).GetMethod("IsIncompleteTransfer");Assert.That(method,Is.Not.Null,"A start/update callback alone cannot prove an incomplete transfer.");
            using(var key=new RSACryptoServiceProvider(2048){PersistKeyInCsp=false})
            {
                var verifier=(ManifestVerifier)Activator.CreateInstance(typeof(ManifestVerifier),BindingFlags.Instance|BindingFlags.NonPublic,null,new object[]{new Dictionary<string,RSAParameters>{{"test-f4",key.ExportParameters(false)}}},null);
                byte[] json=File.ReadAllBytes("Tests/Fixtures/Releases/ManifestCases/valid-release.json");
                var release=verifier.Verify(json,key.SignData(json,HashAlgorithmName.SHA256,RSASignaturePadding.Pkcs1),new PlayerIdentity("player-1","Windows","x64","2022.3.25f1","1",new string('a',64),"aot-1"));
                using(var evidence=new NativeEvidence(root,(NativeAcceptanceOptions)Options(true,"{\"runId\":\"run\",\"caseId\":\"transfer\"}")))
                {
                    string store=Path.Combine(root,"store");evidence.Preparation(release,Path.Combine(store,"slots",release.ReleaseId));
                    var fs=typeof(ReleaseStore).Assembly.GetType("Client.Framework.Release.ReleaseFileSystem");string stage=(string)fs.GetMethod("StageKey",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{release.ManifestSha256});
                    string path=Path.Combine(store,"staging",stage+".partial","downloads","0.partial");
                    Assert.That(method.Invoke(evidence,new object[]{path,0L}),Is.False);Assert.That(method.Invoke(evidence,new object[]{path,2L}),Is.True);Assert.That(method.Invoke(evidence,new object[]{path,3L}),Is.False);Assert.That(method.Invoke(evidence,new object[]{path,4L}),Is.False);
                    Assert.That(method.Invoke(evidence,new object[]{Path.Combine(root,"foreign.partial"),2L}),Is.False);
                }
            }
        }
        [Test] public void SessionObservationExposesActualInFlightRequestAndConnectionEpoch()
        {
            var id=typeof(SessionClient).GetProperty("ActiveRequestId");var epoch=typeof(SessionClient).GetProperty("ConnectionEpoch");Assert.That(id,Is.Not.Null);Assert.That(epoch,Is.Not.Null);
            using(var session=new SessionClient(new PendingConnection(),new SessionOptions("localhost",1),new RequestIdSequence(42),context:new ImmediateContext()))
            {
                var pending=session.LoginAsync(new LoginInput("fixture",null),CancellationToken.None);
                Assert.That(id.GetValue(session),Is.EqualTo(42UL));Assert.That(epoch.GetValue(session),Is.EqualTo(1L));
                session.Disconnect();Assert.That(id.GetValue(session),Is.EqualTo(0UL));Assert.That(epoch.GetValue(session),Is.EqualTo(2L));Assert.That(pending.IsCompleted,Is.True);
            }
        }
        private sealed class ImmediateContext:SynchronizationContext {public override void Post(SendOrPostCallback action,object value)=>action(value);}
        private sealed class PendingConnection:IFrameConnection
        {
            private readonly TaskCompletionSource<bool> pending=new TaskCompletionSource<bool>();
            public event Action<byte[]> FrameReceived {add{}remove{}}
            public event Action<TransportException> TransportFailed {add{}remove{}}
            public Task ConnectAsync(string host,int port,CancellationToken token){token.Register(()=>pending.TrySetCanceled());return pending.Task;}
            public Task SendAsync(ReadOnlyMemory<byte> frame,CancellationToken token)=>Task.CompletedTask;
            public void Close()=>pending.TrySetCanceled();
        }
    }
}
