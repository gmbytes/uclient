#if UNITY_EDITOR
using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Client.Contracts;
using Client.Framework.Release;
using Client.Framework.Resource;
using Client.Framework.UI;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using UnityGameFramework.Runtime;
using Object=UnityEngine.Object;

namespace Client.Tests
{
    [Category("F7")]
    public sealed class BootstrapLaunchCancellationTests
    {
        private F7LaunchFixture launch;
        private GameObject foundation,template,canvasRoot,hostObject;
        private ResourceSlotBootstrap resources;
        private EventComponent events;
        private DownloadComponent downloads;
        private Component host;
        private OwnedContext source;
        private UIService ui;
        private UIViewDefinitions catalog;
        private PendingEntry entry;
        private TaskCompletionSource<IClientContext> composition;
        private TaskCompletionSource<ResourceSlotBootstrap> compositionEntered;
        private int eventBaseline;
        [UnitySetUp] public IEnumerator SetUp()
        {
            GameEntry.Shutdown(ShutdownType.None);yield return null;ResetGuards();
            bool asynchronous=TestContext.CurrentContext.Test.MethodName.StartsWith("AsyncComposition",StringComparison.Ordinal);
            launch=new F7LaunchFixture(readyTimeout:asynchronous?TimeSpan.FromSeconds(1):TimeSpan.FromSeconds(10));
            foundation=new GameObject("F7 cancellation native foundation");foundation.SetActive(false);foundation.AddComponent<BaseComponent>().EditorResourceMode=false;
            events=foundation.AddComponent<EventComponent>();downloads=foundation.AddComponent<DownloadComponent>();resources=foundation.AddComponent<ResourceSlotBootstrap>();
            template=new GameObject("F7 cancellation native GameRoot");template.SetActive(false);template.AddComponent<ResourceComponent>();template.AddComponent<ObjectPoolComponent>();var nativeUI=template.AddComponent<UIComponent>();
            typeof(UIComponent).GetField("m_UIGroups",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(nativeUI,Array.CreateInstance(typeof(UIComponent).GetNestedType("UIGroup",BindingFlags.NonPublic),0));
            typeof(ResourceSlotBootstrap).GetField("m_GameRootPrefab",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(resources,template);
            foundation.SetActive(true);yield return null;
            var type=Type.GetType("Client.Bootstrap.ClientBootstrap, Client.Bootstrap");hostObject=new GameObject("F7 actual launch cancellation host");host=hostObject.AddComponent(type);
            entry=new PendingEntry();var loader=typeof(ReleaseCoordinator).GetField("editorEntryLoader",BindingFlags.Instance|BindingFlags.NonPublic);
            Assert.That(loader,Is.Not.Null,"Editor fixture needs the guarded native-loader boundary; Players must keep the direct production loader.");
            loader.SetValue(launch.Coordinator,(Func<PreparedRelease,PlayerIdentity,IHotfixEntry>)((prepared,player)=>entry));
            if(asynchronous)
            {
                composition=new TaskCompletionSource<IClientContext>();compositionEntered=new TaskCompletionSource<ResourceSlotBootstrap>();
                var method=type.GetMethod("ConfigureLaunchAsync");Assert.That(method,Is.Not.Null,"Actual Package catalog composition requires an awaited native seam.");
                method.Invoke(host,new object[]{launch.Coordinator,resources,(Func<ResourceSlotBootstrap,CancellationToken,Task<IClientContext>>)((root,token)=>{Assert.That(root.Initialization.IsCompletedSuccessfully,Is.True);compositionEntered.TrySetResult(root);return composition.Task;}),false});
            }
            else type.GetMethod("ConfigureLaunch").Invoke(host,new object[]{launch.Coordinator,resources,(Func<ResourceSlotBootstrap,IClientContext>)Compose,false});
        }
        private IClientContext Compose(ResourceSlotBootstrap root)
        {
            canvasRoot=Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Content/UI/Common/AdaptiveUIRoot.prefab"));
            catalog=ScriptableObject.CreateInstance<UIViewDefinitions>();
            ui=new UIService(root.ResourceComponent.GetComponent<UIComponent>(),events,catalog,canvasRoot.GetComponent<AdaptiveCanvas>(),canvasRoot.transform.Find("Windows"));
            eventBaseline=events.EventHandlerCount;
            var transport=new GfDownloadTransport(downloads,events,"http://127.0.0.1:9");
            // Inject the real owned transport after preparation so this cancellation test performs no network Check.
            typeof(ReleaseCoordinator).GetField("updates",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(launch.Coordinator,transport);
            source=new OwnedContext(root.Service,ui);return source;
        }
        [UnityTearDown] public IEnumerator TearDown()
        {
            if(hostObject!=null)Object.DestroyImmediate(hostObject);
            launch?.Coordinator.Dispose();ui?.Dispose();GameObject clone=resources!=null&&resources.ResourceComponent!=null?resources.ResourceComponent.transform.root.gameObject:null;
            GameEntry.Shutdown(ShutdownType.None);if(clone!=null)Object.Destroy(clone);if(template!=null)Object.Destroy(template);if(foundation!=null)Object.Destroy(foundation);if(canvasRoot!=null)Object.Destroy(canvasRoot);if(catalog!=null)Object.Destroy(catalog);
            yield return null;launch?.Dispose();ResetGuards();
        }
        private static void ResetGuards()
        {typeof(ReleaseCoordinator).GetField("nativeLaunchAttempted",BindingFlags.Static|BindingFlags.NonPublic).SetValue(null,0);typeof(ResourceSlotBootstrap).GetField("s_Activated",BindingFlags.Static|BindingFlags.NonPublic).SetValue(null,false);}
        private static IEnumerator Terminal(Task task)
        {float until=Time.realtimeSinceStartup+10;while(!task.IsCompleted&&Time.realtimeSinceStartup<until)yield return null;Assert.That(task.IsCompleted,Is.True,"Actual host launch cancellation exceeded its guard.");}
        [UnityTest] public IEnumerator CancelledLaunchDisposesScopesAndListenersWithoutExternalShutdown()
        {
            using(var caller=new CancellationTokenSource())
            {
                var task=(Task)host.GetType().GetMethod("LaunchAsync").Invoke(host,new object[]{caller.Token});
                yield return Terminal(entry.Entered.Task);Assert.That(resources.Initialization.IsCompletedSuccessfully,Is.True);
                Assert.That(events.EventHandlerCount,Is.EqualTo(eventBaseline+2));Assert.That(FrameListeners(ui),Is.EqualTo(1));
                caller.Cancel();yield return Terminal(task);Assert.That(task.IsCanceled,Is.True,task.Exception?.ToString());
                // No external ShutdownAsync or destruction before these assertions.
                Assert.That(source.Disposals,Is.EqualTo(1),"Cancelled LaunchAsync itself must dispose its native-owned source scope.");
                Assert.That(events.EventHandlerCount,Is.EqualTo(eventBaseline));Assert.That(FrameListeners(ui),Is.Zero);Assert.That(entry.Shutdowns,Is.EqualTo(1));
                Assert.That(launch.Coordinator.IsReady,Is.False);Assert.That(launch.Coordinator.RestartRequired,Is.True);Assert.That(launch.State.Read().Candidate,Is.EqualTo("C"));Assert.That(launch.State.Read().Attempt,Is.EqualTo(1));Assert.That(launch.State.Read().BadReleaseIds,Is.Empty);
            }
        }
        [UnityTest] public IEnumerator CancelledLaunchRetainsCancellationAndCleanupFailuresWhileReleasingListeners()
        {
            using(var caller=new CancellationTokenSource())
            {
                var task=(Task)host.GetType().GetMethod("LaunchAsync").Invoke(host,new object[]{caller.Token});yield return Terminal(entry.Entered.Task);
                source.FailDispose=true;caller.Cancel();yield return Terminal(task);Assert.That(task.IsFaulted,Is.True);
                var causes=task.Exception.Flatten().InnerExceptions;Assert.That(causes.Any(error=>error is OperationCanceledException),Is.True);Assert.That(causes.Any(error=>error is IOException),Is.True);
                Assert.That(source.Disposals,Is.EqualTo(1));Assert.That(events.EventHandlerCount,Is.EqualTo(eventBaseline));Assert.That(FrameListeners(ui),Is.Zero);
                Assert.That(launch.Coordinator.Diagnostics.Failures.Any(record=>record.Exception is OperationCanceledException),Is.True);
                Assert.That(launch.Coordinator.Diagnostics.Failures.Any(record=>record.Exception is IOException&&record.ReleaseId=="C"),Is.True);
                Assert.That(launch.State.Read().BadReleaseIds,Is.Empty);Assert.That(launch.State.Read().Attempt,Is.EqualTo(1));
            }
        }
        private static int FrameListeners(UIService service)=>(typeof(UIService).GetField("FirstFramePresented",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(service) as Delegate)?.GetInvocationList().Length??0;
        [UnityTest] public IEnumerator AsyncCompositionAwaitsActualResourcesAndTransfersContextOnce()
        {
            using(var caller=new CancellationTokenSource())
            {
                var task=(Task)host.GetType().GetMethod("LaunchAsync").Invoke(host,new object[]{caller.Token});yield return Terminal(compositionEntered.Task);
                var duplicate=launch.Coordinator.InitializeComposedAsync((LaunchSelection)typeof(ReleaseCoordinator).GetField("current",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(launch.Coordinator),(root,token)=>throw new Exception("Duplicate composition must not execute."),CancellationToken.None);
                Assert.Throws<InvalidOperationException>(()=>duplicate.GetAwaiter().GetResult());Assert.That(launch.Coordinator.RestartRequired,Is.False);
                Assert.That(entry.Entered.Task.IsCompleted,Is.False);yield return null;composition.SetResult(Compose(compositionEntered.Task.Result));
                yield return Terminal(entry.Entered.Task);caller.Cancel();yield return Terminal(task);Assert.That(task.IsCanceled,Is.True);Assert.That(source.Disposals,Is.EqualTo(1));
            }
        }
        [UnityTest] public IEnumerator AsyncCompositionCancellationDisposesRacingUnboundContextExactlyOnce()
        {
            using(var caller=new CancellationTokenSource())
            {
                var task=(Task)host.GetType().GetMethod("LaunchAsync").Invoke(host,new object[]{caller.Token});yield return Terminal(compositionEntered.Task);
                var late=new LateContext(resources.Service);caller.Cancel();composition.SetResult(late);yield return Terminal(task);yield return null;
                Assert.That(task.IsCanceled,Is.True);Assert.That(late.Disposals,Is.EqualTo(1));Assert.That(entry.Entered.Task.IsCompleted,Is.False);Assert.That(launch.State.Read().Attempt,Is.EqualTo(1));Assert.That(launch.State.Read().BadReleaseIds,Is.Empty);
            }
        }
        [UnityTest] public IEnumerator AsyncCompositionTimeoutKeepsOriginalDeadlineAndDisposesLateContext()
        {
            var task=(Task)host.GetType().GetMethod("LaunchAsync").Invoke(host,new object[]{CancellationToken.None});yield return Terminal(compositionEntered.Task);
            yield return Terminal(task);Assert.That(task.IsCanceled,Is.True);Assert.That(launch.Coordinator.RestartRequired,Is.True);Assert.That(launch.State.Read().BadReleaseIds,Contains.Item("C"));
            var late=new LateContext(resources.Service);composition.SetResult(late);yield return null;yield return null;
            Assert.That(late.Disposals,Is.EqualTo(1));Assert.That(entry.Entered.Task.IsCompleted,Is.False);
        }
        [UnityTest] public IEnumerator AsyncCompositionPreBindCancellationAndCleanupFailureStillShutDownHost()
        {
            using(var caller=new CancellationTokenSource())
            {
                var task=(Task)host.GetType().GetMethod("LaunchAsync").Invoke(host,new object[]{caller.Token});yield return Terminal(compositionEntered.Task);
                Compose(compositionEntered.Task.Result);source.FailDispose=true;
                Assert.That(events.EventHandlerCount,Is.EqualTo(eventBaseline+2));
                composition.SetResult(new CancellingResourceContext(source,caller));yield return Terminal(task);
                Assert.That(task.IsFaulted,Is.True);var errors=task.Exception.Flatten().InnerExceptions;
                Assert.That(errors.Any(e=>e is OperationCanceledException),Is.True);Assert.That(errors.Any(e=>e is IOException),Is.True);
                Assert.That(source.Disposals,Is.EqualTo(1));Assert.That(events.EventHandlerCount,Is.EqualTo(eventBaseline),"Pre-bind cleanup aggregation must still dispose the real owned transport.");
                Assert.That((CancellationToken)host.GetType().GetProperty("Lifetime").GetValue(host),Has.Property("IsCancellationRequested").True);
                Assert.That(entry.Entered.Task.IsCompleted,Is.False);Assert.That(launch.State.Read().Attempt,Is.EqualTo(1));Assert.That(launch.State.Read().BadReleaseIds,Is.Empty);
            }
        }
        [UnityTest] public IEnumerator DistinctCompositionCallerCancelsPendingWaitAndLateContext()
        {
            var selected=launch.Coordinator.PrepareLaunchAsync(CancellationToken.None);yield return Terminal(selected);launch.Coordinator.BeginTrial(selected.Result);
            yield return Terminal(launch.Coordinator.ActivateResourcesAsync(selected.Result,resources,CancellationToken.None));
            using(var caller=new CancellationTokenSource())
            {
                var pending=new TaskCompletionSource<IClientContext>();CancellationToken received=default;var cleanupError=new OperationCanceledException("late context disposal cause");var late=new LateContext(resources.Service){Failure=cleanupError};
                var task=launch.Coordinator.InitializeComposedAsync(selected.Result,(root,token)=>{received=token;return pending.Task;},caller.Token);
                try
                {
                    caller.Cancel();float until=Time.realtimeSinceStartup+.5f;while(!task.IsCompleted&&Time.realtimeSinceStartup<until)yield return null;
                    Assert.That(task.IsCompleted,Is.True,"The distinct composition caller must cancel without waiting for the 10-second local deadline.");Assert.That(task.IsCanceled,Is.True);Assert.That(received.IsCancellationRequested,Is.True);
                    pending.SetResult(late);yield return null;yield return null;Assert.That(late.Disposals,Is.EqualTo(1));Assert.That(launch.State.Read().Attempt,Is.EqualTo(1));Assert.That(launch.State.Read().BadReleaseIds,Is.Empty);
                    Assert.That(launch.Coordinator.Diagnostics.Failures.Any(f=>ReferenceEquals(f.Exception,cleanupError)),Is.True,"Cancellation thrown by late disposal is cleanup evidence, not cancellation of the pending composer.");
                }
                finally { pending.TrySetResult(late); }
            }
        }
        [UnityTest] public IEnumerator DistinctCompositionWorkerCancellationThenFaultRetainsExactLateFailure()
        {
            var selected=launch.Coordinator.PrepareLaunchAsync(CancellationToken.None);yield return Terminal(selected);launch.Coordinator.BeginTrial(selected.Result);
            yield return Terminal(launch.Coordinator.ActivateResourcesAsync(selected.Result,resources,CancellationToken.None));
            using(var caller=new CancellationTokenSource())
            {
                var pending=new TaskCompletionSource<IClientContext>();var cleanupFailure=new IOException("worker composer cleanup failure");var owned=new LateContext(resources.Service){Failure=cleanupFailure};int calls=0;int unityThread=Thread.CurrentThread.ManagedThreadId;
                var task=launch.Coordinator.InitializeComposedAsync(selected.Result,(root,token)=>{calls++;return pending.Task;},caller.Token);
                var worker=Task.Run(()=>
                {
                    if(Thread.CurrentThread.ManagedThreadId==unityThread)throw new InvalidOperationException("Worker ordering fixture ran on Unity thread.");
                    caller.Cancel();
                    try{owned.Dispose();}catch(Exception error){pending.SetException(error);}
                });
                // Both worker events complete while this Unity continuation is still blocked, then it may resume.
                Assert.That(worker.Wait(2000),Is.True);Assert.That(pending.Task.IsFaulted,Is.True);
                yield return Terminal(task);yield return null;
                Assert.That(task.IsCanceled,Is.True);Assert.That(calls,Is.EqualTo(1));Assert.That(owned.Disposals,Is.EqualTo(1));Assert.That(entry.Entered.Task.IsCompleted,Is.False);
                Assert.That(launch.State.Read().Attempt,Is.EqualTo(1));Assert.That(launch.State.Read().BadReleaseIds,Is.Empty);
                Assert.That(launch.Coordinator.Diagnostics.Failures.Any(f=>ReferenceEquals(f.Exception,cleanupFailure)),Is.True,"A pending task already faulted before Unity resumes must retain its exact late failure.");
            }
        }
        private sealed class CancellingResourceContext:IClientContext,IDisposable
        {
            private readonly OwnedContext inner;private readonly CancellationTokenSource caller;public CancellingResourceContext(OwnedContext inner,CancellationTokenSource caller){this.inner=inner;this.caller=caller;}
            public IResourceService Resources{get{caller.Cancel();return inner.Resources;}}public IUIService UI=>inner.UI;public ISessionClient Session=>null;public ILogService Log=>null;public CancellationToken Lifetime=>CancellationToken.None;public void ReportReady()=>throw new InvalidOperationException();public void Dispose()=>inner.Dispose();
        }
        private sealed class LateContext:IClientContext,IDisposable
        {internal int Disposals;internal Exception Failure;public LateContext(IResourceService resources){Resources=resources;}public IResourceService Resources{get;}public IUIService UI=>null;public ISessionClient Session=>null;public ILogService Log=>null;public CancellationToken Lifetime=>CancellationToken.None;public void ReportReady()=>throw new InvalidOperationException();public void Dispose(){Disposals++;if(Failure!=null)throw Failure;}}
        private sealed class PendingEntry:IHotfixEntry
        {internal readonly TaskCompletionSource<bool> Entered=new TaskCompletionSource<bool>();internal int Shutdowns;public async Task InitializeAsync(IClientContext context,CancellationToken token){Entered.TrySetResult(true);await Task.Delay(Timeout.Infinite,token);}public Task ShutdownAsync(CancellationToken token){Shutdowns++;return Task.CompletedTask;}}
        private sealed class OwnedContext:IClientContext,IDisposable
        {
            internal int Disposals;internal bool FailDispose;internal OwnedContext(IResourceService resources,UIService ui){Resources=resources;UI=ui;}public IResourceService Resources{get;}public IUIService UI{get;}public ISessionClient Session=>throw new NotSupportedException();public ILogService Log=>throw new NotSupportedException();public CancellationToken Lifetime=>CancellationToken.None;public void ReportReady()=>throw new InvalidOperationException();public void Dispose(){Disposals++;((UIService)UI).Dispose();if(FailDispose)throw new IOException("F7 owned context cleanup cause");}
        }
    }
}
#endif
