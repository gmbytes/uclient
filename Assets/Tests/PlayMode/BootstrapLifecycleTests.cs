#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Client.Contracts;
using Client.Framework.UI;
using Client.Framework.Release;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Client.Tests
{
    [Category("F7")]
    public sealed class BootstrapLifecycleTests
    {
        private readonly UILifecycleTests fixture = new UILifecycleTests();
        private readonly List<IDisposable> gates = new List<IDisposable>();
        private UIService UI => (UIService)typeof(UILifecycleTests).GetField("service",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(fixture);
        [UnitySetUp] public IEnumerator SetUp()
        {
            yield return fixture.SetUp();
            var screen=(ScreenLayoutComponent)typeof(UILifecycleTests).GetField("screen",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(fixture);
            screen.Service.Evaluate(new ScreenMetrics(Screen.width,Screen.height,new Rect(0,0,Screen.width,Screen.height)));
            screen.Service.FlushPending(Time.frameCount);
        }
        [UnityTearDown] public IEnumerator TearDown()
        { foreach(var gate in gates)gate.Dispose();gates.Clear();yield return fixture.TearDown(); }
        private object Gate(CancellationToken token=default,string expected="Page")
        {
            var type=Type.GetType("Client.Framework.Release.LocalReadinessGate, Client.Framework");
            Assert.That(type,Is.Not.Null,"F7 needs one local readiness gate driven by the actual AOT frame producer.");
            var gate=Activator.CreateInstance(type,BindingFlags.Instance|BindingFlags.NonPublic,null,new object[]{UI,expected,token},null);
            gates.Add((IDisposable)gate);return gate;
        }
        private static object Call(object value,string method,params object[] args)
        {try{return value.GetType().GetMethod(method,BindingFlags.Instance|BindingFlags.NonPublic).Invoke(value,args);}catch(TargetInvocationException error){throw error.InnerException;}}
        private static bool CanCommit(object gate)=>(bool)gate.GetType().GetProperty("CanCommit",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(gate);
        private static IEnumerator Await(Task task)
        {float deadline=Time.realtimeSinceStartup+10;while(!task.IsCompleted&&Time.realtimeSinceStartup<deadline)yield return null;Assert.That(task.IsCompleted,Is.True,"F7 local readiness operation timed out.");Assert.That(task.IsFaulted,Is.False,task.Exception?.ToString());}
        private static void Facts(object gate,int omit=-1)
        {if(omit!=0)Call(gate,"RecordResourceInitialized");if(omit!=1)Call(gate,"RecordHotfixLoaded");if(omit!=2)Call(gate,"VerifyLocalEnvelope");if(omit!=3)Call(gate,"EntrySucceeded");}

        // No missing local fact can be manufactured by Ready; early requests must be discarded, not remembered.
        [UnityTest] public IEnumerator EveryLocalPhaseAndFreshReadyAreRequired()
        {
            for(int omitted=0;omitted<4;omitted++)
            {
                var gate=Gate();Facts(gate,omitted);
                var opening=UI.OpenAsync("Page",CancellationToken.None);yield return Await(opening);var view=opening.Result;
                yield return Await(UIFirstFrame.WaitAsync(view,CancellationToken.None));
                bool accepted=(bool)Call(gate,"ReportReady");
                Assert.That(accepted,Is.EqualTo(omitted==3),"Only entry return may still be pending after eligible Ready.");
                Assert.That(CanCommit(gate),Is.False);
                Call(gate,new[]{"RecordResourceInitialized","RecordHotfixLoaded","VerifyLocalEnvelope","EntrySucceeded"}[omitted]);
                Assert.That(CanCommit(gate),Is.EqualTo(omitted==3),"Previously ineligible requests must not latch.");
                Call(gate,"ReportReady");Assert.That(CanCommit(gate),Is.True);
                ((IDisposable)gate).Dispose();view.Close();yield return null;
            }
        }
        // A future real frame after an old immediate Ready is not a fresh readiness request.
        [UnityTest] public IEnumerator ImmediateReadyIsNotLatchedByLaterActualFrame()
        {
            var gate=Gate();Facts(gate);
            Assert.That(Call(gate,"ReportReady"),Is.False);
            var opening=UI.OpenAsync("Page",CancellationToken.None);yield return Await(opening);
            yield return Await(UIFirstFrame.WaitAsync(opening.Result,CancellationToken.None));
            Assert.That(CanCommit(gate),Is.False);
            Assert.That(Call(gate,"ReportReady"),Is.True);Assert.That(CanCommit(gate),Is.True);
        }
        // Expected ViewID and the live concrete handle both matter; another visible modal cannot satisfy login readiness.
        [UnityTest] public IEnumerator UnrelatedViewFrameCannotBecomeLocalReady()
        {
            var gate=Gate(expected:"UILogin");Facts(gate);
            var opening=UI.OpenAsync("Page",CancellationToken.None);yield return Await(opening);
            yield return Await(UIFirstFrame.WaitAsync(opening.Result,CancellationToken.None));
            Assert.That(Call(gate,"ReportReady"),Is.False);Assert.That(CanCommit(gate),Is.False);
        }
        // Closing a previously rendered exact handle invalidates an eligible pending Ready.
        [UnityTest] public IEnumerator ReadyThenCloseBeforeEntryReturnCannotCommit()
        {
            var gate=Gate();Facts(gate,3);
            var opening=UI.OpenAsync("Page",CancellationToken.None);yield return Await(opening);
            yield return Await(UIFirstFrame.WaitAsync(opening.Result,CancellationToken.None));
            Assert.That(Call(gate,"ReportReady"),Is.True);
            opening.Result.Close();Call(gate,"EntrySucceeded");Assert.That(CanCommit(gate),Is.False);
        }
        // Entry failure or launch cancellation wins even after the eligible Ready request.
        [UnityTest] public IEnumerator ReadyThenFailureOrCancellationCannotCommit()
        {
            using(var cancel=new CancellationTokenSource())
            {
                var gate=Gate(cancel.Token);Facts(gate,3);
                var opening=UI.OpenAsync("Page",CancellationToken.None);yield return Await(opening);
                yield return Await(UIFirstFrame.WaitAsync(opening.Result,CancellationToken.None));
                Assert.That(Call(gate,"ReportReady"),Is.True);
                cancel.Cancel();Call(gate,"EntrySucceeded");Assert.That(CanCommit(gate),Is.False);
            }
            var failed=Gate();Facts(failed,3);
            var second=UI.OpenAsync("Page",CancellationToken.None);yield return Await(second);
            yield return Await(UIFirstFrame.WaitAsync(second.Result,CancellationToken.None));
            Assert.That(Call(failed,"ReportReady"),Is.True);Call(failed,"Abort");Call(failed,"EntrySucceeded");
            Assert.That(CanCommit(failed),Is.False);
        }
        // Unbound production calls must fail before consuming the process-wide F6 load attempt.
        [Test] public void UnboundProductionInitializeCannotAttemptAssemblyLoad()
        {
            var type=Type.GetType("Client.Bootstrap.ClientBootstrap, Client.Bootstrap");Assert.That(type,Is.Not.Null);
            var hostObject=new GameObject("F7 unbound host");var host=hostObject.AddComponent(type);
            try
            {
                var attempted=typeof(Client.Framework.HotUpdate.HotfixAssemblyLoader).GetField("attempted",BindingFlags.Static|BindingFlags.NonPublic);
                int before=(int)attempted.GetValue(null);
                var method=type.GetMethod("InitializeAsync",new[]{typeof(PreparedRelease),typeof(PlayerIdentity),typeof(string),typeof(IClientContext),typeof(CancellationToken)});
                Exception actual=null;
                try{var task=(Task)method.Invoke(host,new object[]{null,new PlayerIdentity("p","Windows","x64","2022.3.25f1","1",new string('a',64),"a"),"Client.Game.Entry",new SourceContext(UI),CancellationToken.None});task.GetAwaiter().GetResult();}
                catch(TargetInvocationException error){actual=error.InnerException;}
                catch(Exception error){actual=error;}
                Assert.That(actual,Is.TypeOf<InvalidOperationException>());
                Assert.That((int)attempted.GetValue(null),Is.EqualTo(before),"No load attempt before native launch binding.");
            }
            finally{Object.DestroyImmediate(hostObject);}
        }
        // Tokens retained by consumers remain readable after host destruction disposes the source.
        [Test] public void DestroyedHostLifetimeRemainsReadableAndCancelled()
        {
            var type=Type.GetType("Client.Bootstrap.ClientBootstrap, Client.Bootstrap");Assert.That(type,Is.Not.Null);
            var hostObject=new GameObject("F7 lifetime host");var host=hostObject.AddComponent(type);
            var lifetime=type.GetProperty("Lifetime");var before=(CancellationToken)lifetime.GetValue(host);
            Object.DestroyImmediate(hostObject);
            Assert.That(before.IsCancellationRequested,Is.True);Assert.DoesNotThrow(()=>lifetime.GetValue(host));
            Assert.That(((CancellationToken)lifetime.GetValue(host)).IsCancellationRequested,Is.True);
        }
        // Initialize return must not dispose the only token link still used by the live page.
        [UnityTest] public IEnumerator ProofHostRetainsLinkedLifetimeUntilShutdown()
        {
            var type=Type.GetType("Client.Bootstrap.ClientBootstrap, Client.Bootstrap");Assert.That(type,Is.Not.Null);
            var hostObject=new GameObject("F7 retained host");var host=hostObject.AddComponent(type);
            using(var cancellation=new CancellationTokenSource())
            {
                try
                {
                    var entry=new LifetimeEntry();
                    var method=type.GetMethod("InitializeAsync",new[]{typeof(IHotfixEntry),typeof(IClientContext),typeof(CancellationToken)});
                    yield return Await((Task)method.Invoke(host,new object[]{entry,new SourceContext(UI),cancellation.Token}));
                    Assert.That(entry.Token.IsCancellationRequested,Is.False);
                    cancellation.Cancel();Assert.That(entry.Token.IsCancellationRequested,Is.True);
                    yield return Await((Task)type.GetMethod("ShutdownAsync").Invoke(host,new object[]{CancellationToken.None}));
                }
                finally{Object.DestroyImmediate(hostObject);}
            }
        }
        private IClientContext BindCoordinator(F7LaunchFixture launch,out LaunchSelection selection,CancellationToken token=default)
        {
            selection=launch.Coordinator.PrepareLaunchAsync(token).GetAwaiter().GetResult();launch.Coordinator.BeginTrial(selection);
            var create=typeof(ReleaseCoordinator).GetMethod("CreateReadinessContext",BindingFlags.Instance|BindingFlags.NonPublic);
            Assert.That(create,Is.Not.Null,"The production host needs a selection-bound readiness context.");
            var context=(IClientContext)create.Invoke(launch.Coordinator,new object[]{selection,new SourceContext(UI),token});
            var gate=context.GetType().GetProperty("Gate",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(context);Facts(gate,3);return context;
        }
        private static Task RunEntry(F7LaunchFixture launch,LaunchSelection selection,IClientContext context,IHotfixEntry entry,CancellationToken token=default)
            => RunEntry(launch.Coordinator,selection,context,entry,token);
        private static Task RunEntry(ReleaseCoordinator coordinator,LaunchSelection selection,IClientContext context,IHotfixEntry entry,CancellationToken token=default)
        {
            var run=typeof(ReleaseCoordinator).GetMethod("CompleteEntryAsync",BindingFlags.Instance|BindingFlags.NonPublic);
            Assert.That(run,Is.Not.Null,"Production entry return must drive the same atomic commit gate.");
            return (Task)run.Invoke(coordinator,new object[]{selection,entry,context,token});
        }
        private static IEnumerator Terminal(Task task)
        {float deadline=Time.realtimeSinceStartup+10;while(!task.IsCompleted&&Time.realtimeSinceStartup<deadline)yield return null;Assert.That(task.IsCompleted,Is.True,"F7 entry completion timed out.");}
        private static async Task<UIViewHandle> Present(IClientContext context)
        {var view=(UIViewHandle)await context.UI.OpenAsync("Page",context.Lifetime);await UIFirstFrame.WaitAsync(view,context.Lifetime);return view;}

        // An eligible request inside the real InitializeAsync call must wait for successful method return.
        [UnityTest] public IEnumerator EligibleReadyCommitsOnceOnlyAfterActualEntryReturn()
        {
            using(var launch=new F7LaunchFixture())
            {
                var context=BindCoordinator(launch,out var selection);var requested=new TaskCompletionSource<bool>();var finish=new TaskCompletionSource<bool>();UIViewHandle view=null;
                var task=RunEntry(launch,selection,context,new DelegateEntry(async ctx=>{view=await Present(ctx);ctx.ReportReady();requested.SetResult(true);await finish.Task;}));
                yield return Await(requested.Task);Assert.That(launch.State.Read().KnownGood,Is.EqualTo("K"));Assert.That(launch.Coordinator.IsReady,Is.False);
                finish.SetResult(true);yield return Await(task);Assert.That(launch.Coordinator.IsReady,Is.True);
                var state=launch.State.Read();Assert.That(state.KnownGood,Is.EqualTo("C"));Assert.That(state.PreviousGood,Is.EqualTo("K"));Assert.That(state.Candidate,Is.Null);Assert.That(state.Attempt,Is.Zero);
                int writes=launch.Fault.Writes;context.ReportReady();context.ReportReady();Assert.That(launch.Fault.Writes,Is.EqualTo(writes));view.Close();
            }
        }
        // A real hot-entry throw after Ready must quarantine C instead of blessing it.
        [UnityTest] public IEnumerator ActualEntryReadyThenThrowCannotCommit()
        {
            using(var launch=new F7LaunchFixture())
            {
                var context=BindCoordinator(launch,out var selection);UIViewHandle view=null;
                var task=RunEntry(launch,selection,context,new DelegateEntry(async ctx=>{view=await Present(ctx);ctx.ReportReady();throw new InvalidOperationException("local entry failed");}));
                yield return Terminal(task);Assert.That(task.IsFaulted,Is.True,task.Exception?.ToString());
                Assert.That(launch.Coordinator.IsReady,Is.False);Assert.That(launch.Coordinator.RestartRequired,Is.True);
                var state=launch.State.Read();Assert.That(state.KnownGood,Is.EqualTo("K"));Assert.That(state.Candidate,Is.Null);Assert.That(state.BadReleaseIds,Is.EqualTo(new[]{"C"}));view.Close();
            }
        }
        // Shutdown cancellation is unfinished trial recovery, not a local release defect or authentication failure.
        [UnityTest] public IEnumerator ShutdownAfterEligibleReadyPreservesUnfinishedTrial()
        {
            using(var launch=new F7LaunchFixture())
            {
                var context=BindCoordinator(launch,out var selection);var requested=new TaskCompletionSource<bool>();var finish=new TaskCompletionSource<bool>();UIViewHandle view=null;
                var task=RunEntry(launch,selection,context,new DelegateEntry(async ctx=>{view=await Present(ctx);ctx.ReportReady();requested.SetResult(true);await finish.Task;ctx.Lifetime.ThrowIfCancellationRequested();}));
                yield return Await(requested.Task);yield return Await(launch.Coordinator.ShutdownAsync(CancellationToken.None));finish.SetResult(true);yield return Terminal(task);
                Assert.That(task.IsCanceled,Is.True);Assert.That(launch.Coordinator.IsReady,Is.False);
                var state=launch.State.Read();Assert.That(state.KnownGood,Is.EqualTo("K"));Assert.That(state.Candidate,Is.EqualTo("C"));Assert.That(state.Attempt,Is.EqualTo(1));Assert.That(state.BadReleaseIds,Is.Empty);view.Close();
            }
        }
        // Cancellation at the durable replace boundary must prevent promotion and keep UI readiness false.
        [UnityTest] public IEnumerator CancellationImmediatelyBeforeReadyReplaceCannotCommit()
        {
            using(var launch=new F7LaunchFixture())using(var cancel=new CancellationTokenSource())
            {
                var context=BindCoordinator(launch,out var selection,cancel.Token);UIViewHandle view=null;
                launch.Fault.At=point=>{if(point=="state.replace.before")cancel.Cancel();};
                var task=RunEntry(launch,selection,context,new DelegateEntry(async ctx=>{view=await Present(ctx);ctx.ReportReady();}),cancel.Token);
                yield return Terminal(task);Assert.That(task.IsCanceled,Is.True);Assert.That(launch.Coordinator.IsReady,Is.False);
                var state=launch.State.Read();Assert.That(state.KnownGood,Is.EqualTo("K"));Assert.That(state.Candidate,Is.EqualTo("C"));Assert.That(state.BadReleaseIds,Is.Empty);view.Close();
            }
        }
        // Both sides of atomic replace must surface persistence failure without publishing Ready.
        [UnityTest] public IEnumerator ReadyPersistenceFaultNeverClaimsReady()
        {
            foreach(string point in new[]{"state.replace.before","state.replace.after"})using(var launch=new F7LaunchFixture())
            {
                var context=BindCoordinator(launch,out var selection);UIViewHandle view=null;launch.Fault.Checkpoint=point;
                var task=RunEntry(launch,selection,context,new DelegateEntry(async ctx=>{view=await Present(ctx);ctx.ReportReady();}));
                yield return Terminal(task);Assert.That(task.IsFaulted,Is.True,task.Exception?.ToString());launch.Fault.Checkpoint=null;
                Assert.That(launch.Coordinator.IsReady,Is.False);Assert.That(launch.Coordinator.RestartRequired,Is.True);
                Assert.That(launch.State.Read().KnownGood,point=="state.replace.after"?Is.EqualTo("C"):Is.EqualTo("K"));view.Close();
            }
        }
        // A refused server login remains a page/session outcome while a sound local release commits.
        [UnityTest] public IEnumerator RefusedAuthenticationDoesNotBlockLocalReadyCommit()
        {
            using(var launch=new F7LaunchFixture())
            {
                var context=BindCoordinator(launch,out var selection);UIViewHandle view=null;LoginResult login=null;
                var task=RunEntry(launch,selection,context,new DelegateEntry(async ctx=>{view=await Present(ctx);login=await ctx.Session.LoginAsync(new LoginInput("fixture",null),ctx.Lifetime);ctx.ReportReady();}));
                yield return Await(task);Assert.That(login.Error,Is.EqualTo(LoginError.Rejected));Assert.That(launch.Coordinator.IsReady,Is.True);
                Assert.That(launch.State.Read().KnownGood,Is.EqualTo("C"));Assert.That(launch.State.Read().BadReleaseIds,Is.Empty);view.Close();
            }
        }
        private sealed class DelegateEntry:IHotfixEntry
        {private readonly Func<IClientContext,Task> initialize;internal DelegateEntry(Func<IClientContext,Task> initialize){this.initialize=initialize;}public Task InitializeAsync(IClientContext context,CancellationToken token)=>initialize(context);public Task ShutdownAsync(CancellationToken token)=>Task.CompletedTask;}
        private static Exception[] FailureExceptions(ReleaseDiagnostics diagnostics)
        {
            var property=typeof(ReleaseDiagnostics).GetProperty("Failures");
            return property==null?(diagnostics.Error==null?Array.Empty<Exception>():new[]{diagnostics.Error}):((IEnumerable)property.GetValue(diagnostics)).Cast<object>().Select(record=>(Exception)record.GetType().GetProperty("Exception").GetValue(record)).ToArray();
        }
        [UnityTest] public IEnumerator FallbackVerificationEvidenceSurvivesEventualReady()
        {
            using(var launch=new F7LaunchFixture(new InvalidChannelTransport()))
            {
                var context=BindCoordinator(launch,out var selection);Assert.That(selection.ReleaseId,Is.EqualTo("K"));UIViewHandle view=null;
                var task=RunEntry(launch,selection,context,new DelegateEntry(async ctx=>{view=await Present(ctx);ctx.ReportReady();}));yield return Await(task);
                Assert.That(launch.Coordinator.IsReady,Is.True);Assert.That(FailureExceptions(launch.Coordinator.Diagnostics).OfType<ReleaseVerificationException>().Any(error=>error.Code==ReleaseErrorCode.InvalidSignature),Is.True,"Ready of the old good release must not erase the update failure.");view.Close();
            }
        }
        [UnityTest] public IEnumerator EntryAndQuarantinePersistenceFailuresBothRemainObservable()
        {
            using(var launch=new F7LaunchFixture())
            {
                var context=BindCoordinator(launch,out var selection);UIViewHandle view=null;var original=new InvalidOperationException("F7 original entry cause");launch.Fault.Checkpoint="state.replace.before";
                var task=RunEntry(launch,selection,context,new DelegateEntry(async ctx=>{view=await Present(ctx);ctx.ReportReady();throw original;}));yield return Terminal(task);
                Assert.That(task.IsFaulted,Is.True);var causes=task.Exception.Flatten().InnerExceptions;
                Assert.That(causes.Any(error=>ReferenceEquals(error,original)),Is.True,"A quarantine write failure must not replace the original hot-entry exception.");
                Assert.That(causes.Any(error=>error is IOException),Is.True);
                var retained=FailureExceptions(launch.Coordinator.Diagnostics);Assert.That(retained.Any(error=>ReferenceEquals(error,original)),Is.True);Assert.That(retained.Any(error=>error is IOException),Is.True);
                launch.Fault.Checkpoint=null;Assert.That(launch.Coordinator.IsReady,Is.False);Assert.That(launch.Coordinator.RestartRequired,Is.True);view.Close();
            }
        }
        private sealed class InvalidChannelTransport:IReleaseTransport
        {public Task DownloadAsync(string path,string destination,CancellationToken token){token.ThrowIfCancellationRequested();File.WriteAllBytes(destination,path.EndsWith(".sig",StringComparison.Ordinal)?new byte[256]:System.Text.Encoding.UTF8.GetBytes("{\"untrusted\":true}"));return Task.CompletedTask;}}
        // A bare ID has no epoch or context capability and must not bless an otherwise eligible new launch.
        [UnityTest] public IEnumerator UnboundReleaseIdReadyCannotCommitEligibleContext()
        {
            using(var launch=new F7LaunchFixture())
            {
                var context=BindCoordinator(launch,out var selection);UIViewHandle view=null;
                var task=RunEntry(launch,selection,context,new DelegateEntry(async ctx=>{view=await Present(ctx);}));
                float deadline=Time.realtimeSinceStartup+10;
                while(launch.Coordinator.Diagnostics.Phase!=ReleasePhase.LoginReady&&Time.realtimeSinceStartup<deadline)yield return null;
                Assert.That(launch.Coordinator.Diagnostics.Phase,Is.EqualTo(ReleasePhase.LoginReady));
                int writes=launch.Fault.Writes;launch.Coordinator.ReportReady(selection.ReleaseId);
                Assert.That(launch.Coordinator.IsReady,Is.False,"Only the selection-bound context request may become eligible Ready.");Assert.That(launch.Fault.Writes,Is.EqualTo(writes));
                context.ReportReady();yield return Await(task);Assert.That(launch.Coordinator.IsReady,Is.True);view.Close();
            }
        }
        [UnityTest] public IEnumerator OldContextForSameReleaseCannotReadyAnotherEpoch()
        {
            using(var launch=new F7LaunchFixture())
            {
                launch.State.WriteAtomic(new ReleaseState{KnownGood="C",PreviousGood="K"});
                var old=BindCoordinator(launch,out var oldSelection);var oldViewTask=Present(old);yield return Await(oldViewTask);
                launch.Coordinator.Dispose();
                using(var next=new ReleaseCoordinator(launch.State,new NativeLaunchConfiguration(launch.Player,"Client.Game.Entry","Page",TimeSpan.FromSeconds(10))))
                {
                    var selected=next.PrepareLaunchAsync(CancellationToken.None);yield return Await(selected);var selection=selected.Result;
                    Assert.That(selection.ReleaseId,Is.EqualTo(oldSelection.ReleaseId));Assert.That(selection.Epoch,Is.GreaterThan(oldSelection.Epoch));next.BeginTrial(selection);
                    var create=typeof(ReleaseCoordinator).GetMethod("CreateReadinessContext",BindingFlags.Instance|BindingFlags.NonPublic);
                    var context=(IClientContext)create.Invoke(next,new object[]{selection,new SourceContext(UI),CancellationToken.None});
                    var gate=context.GetType().GetProperty("Gate",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(context);Facts(gate,3);UIViewHandle view=null;
                    var task=RunEntry(next,selection,context,new DelegateEntry(async ctx=>{view=await Present(ctx);}));
                    float deadline=Time.realtimeSinceStartup+10;while(next.Diagnostics.Phase!=ReleasePhase.LoginReady&&Time.realtimeSinceStartup<deadline)yield return null;
                    Assert.That(next.Diagnostics.Phase,Is.EqualTo(ReleasePhase.LoginReady));int writes=launch.Fault.Writes;
                    old.ReportReady();Assert.That(next.IsReady,Is.False);Assert.That(launch.Fault.Writes,Is.EqualTo(writes));
                    context.ReportReady();yield return Await(task);Assert.That(next.IsReady,Is.True);Assert.That(launch.Fault.Writes,Is.EqualTo(writes));
                    Assert.That(launch.State.Read().PreviousGood,Is.EqualTo("K"));view.Close();
                }
                oldViewTask.Result.Close();
            }
        }
        private sealed class RefusingSession:ISessionClient
        {
            public event Action<SessionState> StateChanged;
            public Task<LoginResult> LoginAsync(LoginInput input,CancellationToken token){StateChanged?.Invoke(SessionState.Disconnected);return Task.FromResult(new LoginResult(false,LoginError.Rejected,401,null,"rejected"));}
            public void Disconnect()=>StateChanged?.Invoke(SessionState.Disconnected);
        }
        private sealed class SourceContext:IClientContext
        {
            public SourceContext(IUIService ui){UI=ui;}
            public IUIService UI{get;}public IResourceService Resources=>throw new NotSupportedException();
            public ISessionClient Session {get;} = new RefusingSession();public ILogService Log=>throw new NotSupportedException();
            public CancellationToken Lifetime=>CancellationToken.None;public void ReportReady(){throw new InvalidOperationException("Source readiness must be gated.");}
        }
        private sealed class LifetimeEntry:IHotfixEntry
        {
            public CancellationToken Token;public Task InitializeAsync(IClientContext context,CancellationToken token){Token=token;return Task.CompletedTask;}
            public Task ShutdownAsync(CancellationToken token)=>Task.CompletedTask;
        }
        [Test] public void HostShutdownDisposesOwnedContextBeforeDestroy()
        {
            using(var launch=new F7LaunchFixture())
            {
                var selection=launch.Coordinator.PrepareLaunchAsync(CancellationToken.None).GetAwaiter().GetResult();launch.Coordinator.BeginTrial(selection);
                var source=new OwnedSourceContext(UI);
                var create=typeof(ReleaseCoordinator).GetMethod("CreateReadinessContext",BindingFlags.Instance|BindingFlags.NonPublic);
                var context=(IClientContext)create.Invoke(launch.Coordinator,new object[]{selection,source,CancellationToken.None});
                var type=Type.GetType("Client.Bootstrap.ClientBootstrap, Client.Bootstrap");var go=new GameObject("F7 shutdown scope host");var host=go.AddComponent(type);
                type.GetField("coordinator",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(host,launch.Coordinator);
                try
                {
                    ((Task)type.GetMethod("ShutdownAsync").Invoke(host,new object[]{CancellationToken.None})).GetAwaiter().GetResult();
                    Assert.That(source.Disposals,Is.EqualTo(1),"Native-owned UI/session scopes and transport listeners must be disposed before GameFramework quit/destruction.");
                    Assert.That(context.Lifetime.IsCancellationRequested,Is.True);context.ReportReady();Assert.That(launch.State.Read().Attempt,Is.EqualTo(1));Assert.That(launch.State.Read().BadReleaseIds,Is.Empty);
                    ((Task)type.GetMethod("ShutdownAsync").Invoke(host,new object[]{CancellationToken.None})).GetAwaiter().GetResult();Assert.That(source.Disposals,Is.EqualTo(1));
                }
                finally{Object.DestroyImmediate(go);}
            }
        }
        private sealed class OwnedSourceContext:IClientContext,IDisposable
        {
            public int Disposals;public OwnedSourceContext(IUIService ui){UI=ui;}public IUIService UI{get;}
            public IResourceService Resources=>throw new NotSupportedException();public ISessionClient Session=>new RefusingSession();
            public ILogService Log=>throw new NotSupportedException();public CancellationToken Lifetime=>CancellationToken.None;public void ReportReady()=>throw new InvalidOperationException();public void Dispose(){Disposals++;}
        }
    }
}
#endif
