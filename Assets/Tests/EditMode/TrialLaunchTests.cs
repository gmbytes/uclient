using System;
using System.Collections.Generic;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Client.Framework.Release;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace Client.Tests.EditMode
{
    [Category("F7")]
    public sealed class TrialLaunchTests
    {
        private string root;
        private RSA key;
        private ManifestVerifier verifier;
        private PlayerIdentity player;
        private ReleaseStateStore state;
        private Faults faults;
        private readonly Dictionary<string, byte[]> files = new Dictionary<string, byte[]>();
        private readonly List<object> coordinators = new List<object>();
        [OneTimeSetUp] public void Signer()
        {
            key = new RSACryptoServiceProvider(2048) { PersistKeyInCsp = false };
            verifier = (ManifestVerifier)Activator.CreateInstance(typeof(ManifestVerifier), BindingFlags.Instance | BindingFlags.NonPublic,
                null, new object[] { new Dictionary<string, RSAParameters> { ["f7-test"] = key.ExportParameters(false) } }, null);
            player = new PlayerIdentity("player-1", "Windows", "x64", "2022.3.25f1", "1", new string('a',64), "aot-1");
        }
        [OneTimeTearDown] public void DisposeSigner() => key.Dispose();
        [SetUp] public void SetUp()
        {
            root=Path.GetFullPath(Path.Combine("Builds","F7Scratch",Guid.NewGuid().ToString("N")));
            faults=new Faults(); state=new ReleaseStateStore(root,verifier,player,faults);
            foreach (string id in new[] {"P","K","C"}) Install(id);
            state.WriteAtomic(new ReleaseState {KnownGood="K",PreviousGood="P",Candidate="C"});
        }
        [TearDown] public void TearDown()
        {
            foreach (var coordinator in coordinators) (coordinator as IDisposable)?.Dispose();
            coordinators.Clear(); files.Clear(); if(Directory.Exists(root)) Directory.Delete(root,true);
        }
        private object Coordinator(IReleaseTransport updates = null, IReleaseStreamReader baseline = null, TimeSpan? readyTimeout = null)
        {
            var configType=Type.GetType("Client.Framework.Release.NativeLaunchConfiguration, Client.Framework");
            Assert.That(configType,Is.Not.Null,"Native baked launch configuration is required.");
            var config=Activator.CreateInstance(configType,player,"Client.Game.Entry","UILogin",readyTimeout??TimeSpan.FromSeconds(10));
            var type=Type.GetType("Client.Framework.Release.ReleaseCoordinator, Client.Framework");
            Assert.That(type,Is.Not.Null,"F7 release coordinator is required.");
            var store=updates==null?null:new ReleaseStore(root,new ReleaseDownloader(updates),state,new DiskBudget(_=>long.MaxValue));
            var value=Activator.CreateInstance(type,new[] {(object)state,config,store,updates==null&&baseline==null?null:verifier,updates,baseline});
            coordinators.Add(value); return value;
        }
        private static object Call(object target,string method,params object[] args)
        {
            try{return target.GetType().GetMethod(method).Invoke(target,args);}
            catch(TargetInvocationException error){throw error.InnerException;}
        }
        private static object Select(object coordinator)
        {
            var task=(Task)Call(coordinator,"PrepareLaunchAsync",CancellationToken.None);
            task.GetAwaiter().GetResult();return task.GetType().GetProperty("Result").GetValue(task);
        }
        private static string Id(object selection)=>(string)selection.GetType().GetProperty("ReleaseId").GetValue(selection);

        // A crash after durable BeginTrial must exclude C and choose surviving K, not skip to P.
        [Test] public void UnfinishedTrialRollsBackOnNextProcess()
        {
            var first=Coordinator(); var selected=Select(first); Assert.That(Id(selected),Is.EqualTo("C"));
            Call(first,"BeginTrial",selected); Assert.That(state.Read().Attempt,Is.GreaterThan(0));
            ((IDisposable)first).Dispose();
            var second=Coordinator(); Assert.That(Id(Select(second)),Is.EqualTo("K"));
            var recovered=state.Read();Assert.That(recovered.Candidate,Is.Null);Assert.That(recovered.Attempt,Is.Zero);
            Assert.That(recovered.KnownGood,Is.EqualTo("K"));Assert.That(recovered.PreviousGood,Is.EqualTo("P"));
            Assert.That(recovered.BadReleaseIds,Is.EqualTo(new[]{"C"}));
        }
        // Starting a known-good slot is not a new candidate trial; normal process exit must preserve it.
        [Test] public void KnownGoodLaunchIsNotQuarantinedOnOrdinaryShutdown()
        {
            state.WriteAtomic(new ReleaseState{KnownGood="K",PreviousGood="P"});
            var first=Coordinator();var selected=Select(first);Call(first,"BeginTrial",selected);((IDisposable)first).Dispose();
            Assert.That(Id(Select(Coordinator())),Is.EqualTo("K"));
            Assert.That(state.Read().BadReleaseIds,Is.Empty);Assert.That(state.Read().Attempt,Is.Zero);
        }
        // Failure state must remove every failed active reference in the same validated atomic write.
        [Test] public void FailedKnownGoodPromotesSurvivingPreviousAtomically()
        {
            state.WriteAtomic(new ReleaseState{KnownGood="K",PreviousGood="P",Candidate="K"});
            var coordinator=Coordinator();var selected=Select(coordinator);Call(coordinator,"BeginTrial",selected);
            Call(coordinator,"FailCurrent",ReleaseErrorCode.InvalidManifest);
            var failed=state.Read();Assert.That(failed.KnownGood,Is.EqualTo("P"));Assert.That(failed.PreviousGood,Is.Null);
            Assert.That(failed.Candidate,Is.Null);Assert.That(failed.Attempt,Is.Zero);Assert.That(failed.BadReleaseIds,Is.EqualTo(new[]{"K"}));
            Assert.That(coordinator.GetType().GetProperty("RestartRequired").GetValue(coordinator),Is.True);
        }
        // A failed durable trial write must be observable and cannot arm launch activation.
        [Test] public void BeginTrialPersistenceFailureCannotStartLaunch()
        {
            var coordinator=Coordinator();var selected=Select(coordinator);faults.Checkpoint="state.replace.before";
            Assert.Throws<IOException>(()=>Call(coordinator,"BeginTrial",selected));faults.Checkpoint=null;
            Assert.That(state.Read().Attempt,Is.Zero);
            Assert.That(coordinator.GetType().GetProperty("TrialBegun").GetValue(coordinator),Is.False);
        }
        // Bare ready without resource/load/selfcheck/frame facts is rejected without a state write.
        [Test] public void StaleReadyCannotCommitAnotherRelease()
        {
            var coordinator=Coordinator();var selected=Select(coordinator);Call(coordinator,"BeginTrial",selected);
            int writes=faults.Writes;Call(coordinator,"ReportReady","K");Call(coordinator,"ReportReady","C");
            Assert.That(faults.Writes,Is.EqualTo(writes));Assert.That(state.Read().KnownGood,Is.EqualTo("K"));
            Assert.That(state.Read().Candidate,Is.EqualTo("C"));
        }
        // Release state never subscribes to server authentication; an early ready cannot bless local startup.
        [Test] public void ServerLoginFailureDoesNotBlacklistRelease()
        {
            state.WriteAtomic(new ReleaseState{KnownGood="K",PreviousGood="P"});
            var coordinator=Coordinator();var selected=Select(coordinator);Call(coordinator,"BeginTrial",selected);
            Call(coordinator,"ReportReady","K");
            Assert.That(state.Read().KnownGood,Is.EqualTo("K"));Assert.That(state.Read().BadReleaseIds,Is.Empty);
            Assert.That(state.Read().Attempt,Is.Zero);
        }

        // The production Check path must authenticate the channel before reading any manifest path from it.
        [Test] public void InvalidChannelCannotTriggerManifestFetchOrChangeKnownGood()
        {
            state.WriteAtomic(new ReleaseState{KnownGood="K",PreviousGood="P"});
            var transport=Metadata("C");transport.Data["channel.sig"]=new byte[256];
            Assert.That(Id(Select(Coordinator(transport))),Is.EqualTo("K"));
            Assert.That(transport.Requests,Is.EqualTo(new[]{"channel.json","channel.sig"}));
            Assert.That(state.Read().Candidate,Is.Null);Assert.That(state.Read().BadReleaseIds,Is.Empty);
        }
        [Test] public void FallbackRetainsOriginalVerificationFailureAfterSelection()
        {
            state.WriteAtomic(new ReleaseState{KnownGood="K",PreviousGood="P"});var transport=Metadata("C");transport.Data["channel.sig"]=new byte[256];
            var coordinator=Coordinator(transport);var selection=Select(coordinator);Assert.That(Id(selection),Is.EqualTo("K"));
            var diagnostics=(ReleaseDiagnostics)coordinator.GetType().GetProperty("Diagnostics").GetValue(coordinator);
            var property=typeof(ReleaseDiagnostics).GetProperty("Failures");
            var errors=property==null?(diagnostics.Error==null?Array.Empty<Exception>():new[]{diagnostics.Error}):((IEnumerable)property.GetValue(diagnostics)).Cast<object>().Select(record=>(Exception)record.GetType().GetProperty("Exception").GetValue(record)).ToArray();
            Assert.That(errors.OfType<ReleaseVerificationException>().Any(error=>error.Code==ReleaseErrorCode.InvalidSignature),Is.True,"Fallback must retain its real verification failure after presentation advances to Select.");
            var record=((IEnumerable)property.GetValue(diagnostics)).Cast<object>().Single();
            Assert.That(record.GetType().GetProperty("Phase").GetValue(record),Is.EqualTo(ReleasePhase.Check));
            Assert.That(record.GetType().GetProperty("Epoch").GetValue(record),Is.EqualTo(selection.GetType().GetProperty("Epoch").GetValue(selection)));
        }
        // A signed pointer with the wrong hash cannot prepare or select the incoming slot.
        [Test] public void PinnedManifestHashMismatchKeepsExistingGood()
        {
            state.WriteAtomic(new ReleaseState{KnownGood="K",PreviousGood="P"});
            var transport=Metadata("C",new string('f',64));
            Assert.That(Id(Select(Coordinator(transport))),Is.EqualTo("K"));
            Assert.That(state.Read().Candidate,Is.Null);Assert.That(state.Read().KnownGood,Is.EqualTo("K"));
        }
        // Real F4 capabilities drive the actual F5 store; all metadata transport is exercised.
        [Test] public void AuthenticatedChannelPreparesItsExactPinnedRelease()
        {
            state.WriteAtomic(new ReleaseState{KnownGood="K",PreviousGood="P"});
            var transport=Metadata("C");
            Assert.That(Id(Select(Coordinator(transport))),Is.EqualTo("C"));
            Assert.That(transport.Requests,Is.EqualTo(new[]{"channel.json","channel.sig","releases/C/release.json","releases/C/release.sig"}));
            Assert.That(state.Read().Candidate,Is.EqualTo("C"));Assert.That(state.Read().KnownGood,Is.EqualTo("K"));
        }
        // Recovery must precede network Check, including when a stale signed channel offers the bad candidate again.
        [Test] public void UnfinishedTrialIsRecoveredBeforeAnyTransportOrNewPrepare()
        {
            state.WriteAtomic(new ReleaseState{KnownGood="K",PreviousGood="P",Candidate="C",Attempt=1});
            bool observedRecovered=false;
            var transport=Metadata("C");transport.OnDownload=()=>
            {var recovered=state.Read();observedRecovered=recovered.Candidate==null&&recovered.BadReleaseIds.Contains("C");};
            Assert.That(Id(Select(Coordinator(transport))),Is.EqualTo("K"));
            Assert.That(observedRecovered,Is.True);
            Assert.That(state.Read().Candidate,Is.Null);Assert.That(state.Read().BadReleaseIds,Is.EqualTo(new[]{"C"}));
        }
        // A full quarantine set must fail visibly, never truncate IDs or claim a successful failure-state write.
        [Test] public void QuarantineOverflowIsObservableAndStillRequiresRestart()
        {
            var ids=Enumerable.Range(0,4096).Select(i=>"bad"+i).ToArray();
            state.WriteAtomic(new ReleaseState{KnownGood="K",PreviousGood="P",Candidate="C",BadReleaseIds=ids});
            var coordinator=Coordinator();Call(coordinator,"BeginTrial",Select(coordinator));
            Assert.Throws<InvalidDataException>(()=>Call(coordinator,"FailCurrent",ReleaseErrorCode.InvalidManifest));
            Assert.That(coordinator.GetType().GetProperty("RestartRequired").GetValue(coordinator),Is.True);
            Assert.That(state.Read().BadReleaseIds,Is.EqualTo(ids));Assert.That(state.Read().Candidate,Is.EqualTo("C"));
        }
        // A simulated crash after replace has a valid complete quarantined state, while still reporting the injected failure.
        [Test] public void FailureAfterStateReplaceRetainsAtomicQuarantine()
        {
            var coordinator=Coordinator();Call(coordinator,"BeginTrial",Select(coordinator));faults.Checkpoint="state.replace.after";
            Assert.Throws<IOException>(()=>Call(coordinator,"FailCurrent",ReleaseErrorCode.InvalidManifest));faults.Checkpoint=null;
            var failed=state.Read();Assert.That(failed.Candidate,Is.Null);Assert.That(failed.Attempt,Is.Zero);
            Assert.That(failed.KnownGood,Is.EqualTo("K"));Assert.That(failed.BadReleaseIds,Is.EqualTo(new[]{"C"}));
        }
        // First offline launch still authenticates and installs the packaged baseline through real F4/F5.
        [UnityTest] public IEnumerator OfflineFirstLaunchUsesOnlyVerifiedPackagedBaseline()
        {
            var baseline=new Baseline(Path.Combine(root,"slots","C"));
            state.WriteAtomic(new ReleaseState());
            var offline=new MetadataTransport();
            var selectionTask=(Task)Call(Coordinator(offline,baseline),"PrepareLaunchAsync",CancellationToken.None);
            float deadline=UnityEngine.Time.realtimeSinceStartup+10;
            while(!selectionTask.IsCompleted&&UnityEngine.Time.realtimeSinceStartup<deadline)yield return null;
            Assert.That(selectionTask.IsCompleted,Is.True,"Verified baseline preparation timed out.");
            selectionTask.GetAwaiter().GetResult();
            Assert.That(Id(selectionTask.GetType().GetProperty("Result").GetValue(selectionTask)),Is.EqualTo("C"));
            Assert.That(baseline.Reads,Is.EqualTo(new[]{"release.json","release.sig"}));
            Assert.That(state.Read().Candidate,Is.EqualTo("C"));
        }
        // Disposing a service must drain a prepare blocked behind another current launch's root gate.
        [UnityTest] public IEnumerator DisposeCancelsPendingRootGateWait()
        {
            var owner=Coordinator();Call(owner,"BeginTrial",Select(owner));var waiting=Coordinator();
            var pending=(Task)Call(waiting,"PrepareLaunchAsync",CancellationToken.None);
            var observe=pending.ContinueWith(task=>{var ignored=task.Exception;},TaskContinuationOptions.OnlyOnFaulted);
            try
            {
                Assert.That(pending.IsCompleted,Is.False);((IDisposable)waiting).Dispose();
                float deadline=UnityEngine.Time.realtimeSinceStartup+2;
                while(!pending.IsCompleted&&UnityEngine.Time.realtimeSinceStartup<deadline)yield return null;
                Assert.That(pending.IsCompleted,Is.True,"Disposed prepare must not wait forever for the root lock.");
                Assert.That(pending.IsCanceled,Is.True);
            }
            finally{((IDisposable)owner).Dispose();}
        }
        // A fresh capability is required even when two preparations select the identical release ID.
        [Test] public void SameReleaseIdCannotReuseStaleSelectionEpoch()
        {
            var coordinator=Coordinator();var old=Select(coordinator);var current=Select(coordinator);
            Assert.That(Id(old),Is.EqualTo(Id(current)));
            Assert.Throws<InvalidOperationException>(()=>Call(coordinator,"BeginTrial",old));
            Call(coordinator,"BeginTrial",current);Assert.That(state.Read().Attempt,Is.EqualTo(1));
        }
        private object Policy()
        {var type=Type.GetType("Client.Framework.Release.TrialLaunchPolicy, Client.Framework");return Activator.CreateInstance(type,BindingFlags.Instance|BindingFlags.NonPublic,null,new object[]{state},null);}
        private static void Promote(object policy,string id,CancellationToken token)
        {try{policy.GetType().GetMethod("Promote",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(policy,new object[]{id,token});}catch(TargetInvocationException error){throw error.InnerException;}}
        [Test] public void PromotionRetainsLatestGoodAndDuplicateWritesNothing()
        {
            state.WriteAtomic(new ReleaseState{KnownGood="K",PreviousGood="P",Candidate="C",Attempt=1});var policy=Policy();Promote(policy,"C",CancellationToken.None);
            var promoted=state.Read();Assert.That(promoted.KnownGood,Is.EqualTo("C"));Assert.That(promoted.PreviousGood,Is.EqualTo("K"));Assert.That(promoted.Candidate,Is.Null);Assert.That(promoted.Attempt,Is.Zero);
            int writes=faults.Writes;Promote(policy,"C",CancellationToken.None);Assert.That(faults.Writes,Is.EqualTo(writes));
        }
        [Test] public void AlreadyKnownGoodPromotionPreservesActualPreviousSlot()
        {
            state.WriteAtomic(new ReleaseState{KnownGood="K",PreviousGood="P"});int writes=faults.Writes;
            Promote(Policy(),"K",CancellationToken.None);Assert.That(faults.Writes,Is.EqualTo(writes));Assert.That(state.Read().PreviousGood,Is.EqualTo("P"));
        }
        [Test] public void ReadyCancellationAtAtomicReplaceKeepsBegunTrial()
        {
            state.WriteAtomic(new ReleaseState{KnownGood="K",PreviousGood="P",Candidate="C",Attempt=1});
            using(var cancel=new CancellationTokenSource())
            {
                faults.At=point=>{if(point=="state.replace.before")cancel.Cancel();};
                Assert.Throws<OperationCanceledException>(()=>Promote(Policy(),"C",cancel.Token));faults.At=null;
                Assert.That(state.Read().KnownGood,Is.EqualTo("K"));Assert.That(state.Read().Candidate,Is.EqualTo("C"));Assert.That(state.Read().Attempt,Is.EqualTo(1));
            }
        }
        // Both primary and backup references must survive; only an unreferenced complete slot may be removed.
        [Test] public void PruningRetainsBackupReferencesBeforePreparation()
        {
            Install("Z");state.WriteAtomic(new ReleaseState{KnownGood="K",PreviousGood="P"});
            state.WriteAtomic(new ReleaseState{KnownGood="K",PreviousGood="C"});
            Assert.That(Id(Select(Coordinator())),Is.EqualTo("K"));
            Assert.That(Directory.Exists(Path.Combine(root,"slots","Z")),Is.False,"Unreferenced complete slots should be pruned before downloads need disk space.");
            foreach(string id in new[]{"K","C","P"})Assert.That(Directory.Exists(Path.Combine(root,"slots",id)),Is.True,"Keep primary and backup referenced slots.");
        }
        [Test] public void UncertainPrimaryStateConservativelySkipsPruning()
        {
            Install("Z");state.WriteAtomic(new ReleaseState{KnownGood="K",PreviousGood="P"});
            state.WriteAtomic(new ReleaseState{KnownGood="K",PreviousGood="C"});File.WriteAllText(Path.Combine(root,"state.json"),"{broken");
            Assert.That(Id(Select(Coordinator())),Is.EqualTo("K"));
            foreach(string id in new[]{"K","C","P","Z"})Assert.That(Directory.Exists(Path.Combine(root,"slots",id)),Is.True,"Unknown primary references must not be destroyed.");
        }
        // The short local-ready clock starts at resource activation, not while signed metadata is being fetched.
        [UnityTest] public IEnumerator SlowPreparationDoesNotConsumeLocalReadyTimeout()
        {
            state.WriteAtomic(new ReleaseState{KnownGood="K",PreviousGood="P"});var transport=Metadata("C");transport.DelayMilliseconds=1500;
            var task=(Task)Call(Coordinator(transport,readyTimeout:TimeSpan.FromSeconds(1)),"PrepareLaunchAsync",CancellationToken.None);
            float deadline=UnityEngine.Time.realtimeSinceStartup+15;
            while(!task.IsCompleted&&UnityEngine.Time.realtimeSinceStartup<deadline)yield return null;
            Assert.That(task.IsCompleted,Is.True,"Slow signed metadata preparation must remain bounded.");task.GetAwaiter().GetResult();
            Assert.That(Id(task.GetType().GetProperty("Result").GetValue(task)),Is.EqualTo("C"));
            Assert.That(transport.Requests.Count,Is.EqualTo(4));Assert.That(state.Read().Candidate,Is.EqualTo("C"));Assert.That(state.Read().Attempt,Is.Zero);
        }
        private sealed class Baseline:IReleaseStreamReader
        {
            private readonly string directory;public readonly List<string> Reads=new List<string>();public Baseline(string directory){this.directory=directory;}
            public Task<Stream> OpenReadAsync(string path,CancellationToken token)
            {Reads.Add(path);token.ThrowIfCancellationRequested();string stored=path=="release.json"?"release.manifest.json":path=="release.sig"?"release.manifest.sig":path;return Task.FromResult<Stream>(File.OpenRead(Path.Combine(directory,stored)));}
        }
        private MetadataTransport Metadata(string id,string hash=null)
        {
            string slot=Path.Combine(root,"slots",id);byte[] manifest=File.ReadAllBytes(Path.Combine(slot,"release.manifest.json"));
            if(hash==null)using(var sha=SHA256.Create())hash=BitConverter.ToString(sha.ComputeHash(manifest)).Replace("-","").ToLowerInvariant();
            byte[] channel=Encoding.UTF8.GetBytes(new JObject{["schemaVersion"]=1,["keyId"]="f7-test",["releaseId"]=id,["manifestRelativePath"]="releases/"+id+"/release.json",["manifestSha256"]=hash}.ToString(Formatting.None));
            var transport=new MetadataTransport();transport.Data["channel.json"]=channel;transport.Data["channel.sig"]=key.SignData(channel,HashAlgorithmName.SHA256,RSASignaturePadding.Pkcs1);
            transport.Data["releases/"+id+"/release.json"]=manifest;transport.Data["releases/"+id+"/release.sig"]=File.ReadAllBytes(Path.Combine(slot,"release.manifest.sig"));return transport;
        }
        private sealed class MetadataTransport:IReleaseTransport
        {
            public readonly Dictionary<string,byte[]> Data=new Dictionary<string,byte[]>();public readonly List<string> Requests=new List<string>();public Action OnDownload;public int DelayMilliseconds;
            public async Task DownloadAsync(string path,string destination,CancellationToken token){token.ThrowIfCancellationRequested();Requests.Add(path);OnDownload?.Invoke();if(DelayMilliseconds>0)await Task.Delay(DelayMilliseconds,token);File.WriteAllBytes(destination,Data[path]);}
        }
        private void Install(string id)
        {
            byte[] native={7,8,9};byte[] dll=Encoding.UTF8.GetBytes(id);
            var doc=JObject.Parse(File.ReadAllText("Tests/Fixtures/Releases/ManifestCases/valid-release.json"));
            doc["keyId"]="f7-test";doc["releaseId"]=id;
            doc["files"]=new JArray(FileEntry("Resources/GameFrameworkVersion.dat",native,"NativeResource"),FileEntry("Hotfix/Game.dll",dll,"HotfixDll"));
            doc["assemblies"]=new JArray(new JObject{["name"]="Game",["path"]="Hotfix/Game.dll",["dependencies"]=new JArray("Client.Contracts")});doc["assemblyOrder"]=new JArray("Game");
            byte[] raw=Encoding.UTF8.GetBytes(doc.ToString(Formatting.None));var release=verifier.Verify(raw,key.SignData(raw,HashAlgorithmName.SHA256,RSASignaturePadding.Pkcs1),player);
            files["Resources/GameFrameworkVersion.dat"]=native;files["Hotfix/Game.dll"]=dll;
            var store=new ReleaseStore(root,new ReleaseDownloader(new MemoryTransport(files)),state,new DiskBudget(_=>long.MaxValue));
            store.PrepareAsync(release,null,CancellationToken.None).GetAwaiter().GetResult();
        }
        private static JObject FileEntry(string path,byte[] bytes,string kind)
        {using(var hash=SHA256.Create())return new JObject{["path"]=path,["length"]=bytes.Length,["sha256"]=BitConverter.ToString(hash.ComputeHash(bytes)).Replace("-","").ToLowerInvariant(),["kind"]=kind};}
        private sealed class MemoryTransport:IReleaseTransport
        {private readonly Dictionary<string,byte[]> data;public MemoryTransport(Dictionary<string,byte[]> data){this.data=data;}public Task DownloadAsync(string path,string destination,CancellationToken token){token.ThrowIfCancellationRequested();File.WriteAllBytes(destination,data[path]);return Task.CompletedTask;}}
        private sealed class Faults:IFileFaultInjector
        {public string Checkpoint;public int Writes;public Action<string> At;public void Hit(string point){if(point=="state.write.before")Writes++;At?.Invoke(point);if(point==Checkpoint)throw new IOException("Injected "+point);}}
    }
}
