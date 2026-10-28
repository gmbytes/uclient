#if UNITY_EDITOR
using System;
using System.Collections;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Client.Framework.Release;
using Client.Framework.Resource;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityGameFramework.Runtime;
using Object=UnityEngine.Object;

namespace Client.Tests
{
    // Native Package initialization has no render requirement; these are not first-frame/Ready proofs.
    [Category("F7")]
    public sealed class BootstrapNativeResourceTests
    {
        private F7LaunchFixture launch;
        private GameObject host,template,clone;
        private ResourceSlotBootstrap resources;
        [UnitySetUp] public IEnumerator SetUp()
        {
            GameEntry.Shutdown(ShutdownType.None);yield return null;ResetProcessGuards();launch=new F7LaunchFixture();
            host=new GameObject("F7 native resource host");host.SetActive(false);host.AddComponent<BaseComponent>().EditorResourceMode=false;host.AddComponent<EventComponent>();
            resources=host.AddComponent<ResourceSlotBootstrap>();
            template=new GameObject("F7 inactive GameRoot");template.SetActive(false);template.AddComponent<ResourceComponent>();template.AddComponent<ObjectPoolComponent>();var ui=template.AddComponent<UIComponent>();
            typeof(UIComponent).GetField("m_UIGroups",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(ui,Array.CreateInstance(typeof(UIComponent).GetNestedType("UIGroup",BindingFlags.NonPublic),0));
            typeof(ResourceSlotBootstrap).GetField("m_GameRootPrefab",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(resources,template);
            host.SetActive(true);yield return null;
        }
        [UnityTearDown] public IEnumerator TearDown()
        {
            launch?.Coordinator.Dispose();GameEntry.Shutdown(ShutdownType.None);
            if(resources!=null&&resources.ResourceComponent!=null)clone=resources.ResourceComponent.transform.root.gameObject;
            if(clone!=null)Object.Destroy(clone);if(host!=null)Object.Destroy(host);if(template!=null)Object.Destroy(template);
            yield return null;launch?.Dispose();launch=null;ResetProcessGuards();
        }
        private static void ResetProcessGuards()
        {
            typeof(ReleaseCoordinator).GetField("nativeLaunchAttempted",BindingFlags.Static|BindingFlags.NonPublic).SetValue(null,0);
            typeof(ResourceSlotBootstrap).GetField("s_Activated",BindingFlags.Static|BindingFlags.NonPublic).SetValue(null,false);
        }
        private static IEnumerator Terminal(Task task)
        {float deadline=Time.realtimeSinceStartup+10;while(!task.IsCompleted&&Time.realtimeSinceStartup<deadline)yield return null;Assert.That(task.IsCompleted,Is.True,"Actual native Package initialization timed out.");}
        // Real GF InitResources callback must complete before the coordinator records resource success.
        [UnityTest] public IEnumerator ActualPackageInitializationUsesPersistedTrialAndOneRoot()
        {
            var select=launch.Coordinator.PrepareLaunchAsync(CancellationToken.None);yield return Terminal(select);var selection=select.Result;
            launch.Coordinator.BeginTrial(selection);Assert.That(launch.State.Read().Attempt,Is.EqualTo(1));
            var task=launch.Coordinator.ActivateResourcesAsync(selection,resources,CancellationToken.None);yield return Terminal(task);
            Assert.That(task.IsCompletedSuccessfully,Is.True,task.Exception?.ToString());Assert.That(resources.Initialization.IsCompletedSuccessfully,Is.True);
            Assert.That(resources.ResourceComponent.ReadOnlyPath,Is.EqualTo(Path.Combine(selection.Prepared.Root,"Resources")));
            Assert.That(resources.ResourceComponent.LoadTotalAgentCount,Is.EqualTo(3));
            Assert.That(typeof(ReleaseCoordinator).GetField("resourceInitialized",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(launch.Coordinator),Is.True);
            var again=launch.Coordinator.ActivateResourcesAsync(selection,resources,CancellationToken.None);yield return Terminal(again);Assert.That(again.IsFaulted,Is.True,again.Exception?.ToString());
            Assert.That(Object.FindObjectsOfType<ResourceComponent>().Length,Is.EqualTo(1));
            yield return Terminal(launch.Coordinator.ShutdownAsync(CancellationToken.None));
            Assert.That(launch.State.Read().Candidate,Is.EqualTo("C"));Assert.That(launch.State.Read().BadReleaseIds,Is.Empty);
        }
        // No durable BeginTrial means no invocation of the irreversible native resource boundary.
        [UnityTest] public IEnumerator BeginTrialDiskFaultPreventsResourceActivation()
        {
            var select=launch.Coordinator.PrepareLaunchAsync(CancellationToken.None);yield return Terminal(select);
            launch.Fault.Checkpoint="state.replace.before";Assert.Throws<IOException>(()=>launch.Coordinator.BeginTrial(select.Result));launch.Fault.Checkpoint=null;
            var task=launch.Coordinator.ActivateResourcesAsync(select.Result,resources,CancellationToken.None);yield return Terminal(task);
            Assert.That(task.IsFaulted,Is.True,task.Exception?.ToString());Assert.That(resources.ResourceComponent,Is.Null);
            Assert.That((bool)typeof(ResourceSlotBootstrap).GetField("s_Activated",BindingFlags.Static|BindingFlags.NonPublic).GetValue(null),Is.False);
            Assert.That(launch.State.Read().Attempt,Is.Zero);
        }
        // Even partial failure during Activate forbids another root or another in-process prepare.
        [UnityTest] public IEnumerator NativeActivationFailureRequiresRestartWithoutSameProcessRetry()
        {
            var select=launch.Coordinator.PrepareLaunchAsync(CancellationToken.None);yield return Terminal(select);launch.Coordinator.BeginTrial(select.Result);
            template.SetActive(true);yield return null;
            var task=launch.Coordinator.ActivateResourcesAsync(select.Result,resources,CancellationToken.None);yield return Terminal(task);
            Assert.That(task.IsFaulted,Is.True,task.Exception?.ToString());Assert.That(launch.Coordinator.RestartRequired,Is.True);
            Assert.That(launch.State.Read().KnownGood,Is.EqualTo("K"));Assert.That(launch.State.Read().BadReleaseIds,Is.EqualTo(new[]{"C"}));
            var retry=launch.Coordinator.PrepareLaunchAsync(CancellationToken.None);yield return Terminal(retry);Assert.That(retry.IsFaulted,Is.True,retry.Exception?.ToString());
        }
    }
}
#endif
