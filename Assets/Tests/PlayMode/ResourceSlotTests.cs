using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using GameFramework.Resource;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityGameFramework.Runtime;
using Object = UnityEngine.Object;

namespace Client.Tests
{
    public sealed class ResourceSlotTests
    {
        private GameObject host;
        private string slot;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            GameEntry.Shutdown(ShutdownType.None);
            yield return null;
            slot = Path.Combine(Application.temporaryCachePath, "F2-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(slot, "Resources"));
            Directory.CreateDirectory(Path.Combine(slot, "Scratch"));
            var serializer = new PackageVersionListSerializer();
            serializer.RegisterSerializeCallback(2, BuiltinVersionListSerializer.PackageVersionListSerializeCallback_V2);
            using (var stream = File.Create(Path.Combine(slot, "Resources", "GameFrameworkVersion.dat")))
                Assert.That(serializer.Serialize(stream, new PackageVersionList("0.1.0", 1,
                    new PackageVersionList.Asset[0], new PackageVersionList.Resource[0],
                    new PackageVersionList.FileSystem[0], new PackageVersionList.ResourceGroup[0]), 2), Is.True);
            host = new GameObject("F2 native host");
            host.SetActive(false);
            host.AddComponent<BaseComponent>().EditorResourceMode = false;
            host.AddComponent<EventComponent>();
            host.AddComponent<ResourceComponent>();
            ResourceSlotObservation.Reset(slot);
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            GameEntry.Shutdown(ShutdownType.None);
            if (host != null) Object.Destroy(host);
            yield return null;
            if (Directory.Exists(slot)) Directory.Delete(slot, true);
        }

        [UnityTest, Category("F2")]
        public IEnumerator ResourceRootIsInjectedBeforeInitialization()
        {
            var component = host.GetComponent<ResourceComponent>();
            SetPaths(component, Path.Combine(slot, "Resources"), Path.Combine(slot, "Scratch"));
            SetField(component, "m_ResourceHelperTypeName", typeof(ObservedResourceHelper).FullName);
            SetField(component, "m_LoadResourceAgentHelperTypeName", typeof(ObservedLoadAgentHelper).FullName);
            host.SetActive(true);
            yield return null;
            Assert.That(component.ReadOnlyPath, Is.EqualTo(Path.Combine(slot, "Resources")));
            Assert.That(component.ReadWritePath, Is.EqualTo(Path.Combine(slot, "Scratch")));
            Assert.That(component.ResourceMode, Is.EqualTo(ResourceMode.Package));
            Assert.That(component.LoadTotalAgentCount, Is.EqualTo(3));
            bool initialized = false;
            component.InitResources(() => initialized = true);
            float deadline = Time.realtimeSinceStartup + 10;
            while (!initialized && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.That(initialized, Is.True, "Native Package version table must initialize from the selected root.");
            Assert.That(ResourceSlotObservation.Stages, Is.EqualTo(new[] { "PathsAndPackageObservedBeforeAgents", "AddAgents", "InitResources" }));
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest, Category("F2")]
        public IEnumerator OfficialPathsRemainDefaultsWithoutOverride()
        {
            var component = host.GetComponent<ResourceComponent>();
            host.SetActive(true);
            yield return null;
            Assert.That(component.ReadOnlyPath, Is.EqualTo(Application.streamingAssetsPath));
            Assert.That(component.ReadWritePath, Is.EqualTo(Application.persistentDataPath));
            Assert.That(component.ResourceMode, Is.EqualTo(ResourceMode.Package));
            Assert.That(component.LoadTotalAgentCount, Is.EqualTo(3));
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest, Category("F2")]
        public IEnumerator LatePathMutationIsRejected()
        {
            var component = host.GetComponent<ResourceComponent>();
            SetPaths(component, Path.Combine(slot, "Resources"), Path.Combine(slot, "Scratch"));
            host.SetActive(true);
            yield return null;
            string original = component.ReadOnlyPath;
            var exception = Assert.Throws<TargetInvocationException>(() => SetPaths(component, slot + "-other", slot + "-other-scratch"));
            Assert.That(exception.InnerException, Is.TypeOf<InvalidOperationException>());
            Assert.That(component.ReadOnlyPath, Is.EqualTo(original));
            LogAssert.NoUnexpectedReceived();
        }

        private static void SetPaths(ResourceComponent component, string readOnly, string readWrite)
        {
            var method = typeof(ResourceComponent).GetMethod("SetCustomResourcePaths", new[] { typeof(string), typeof(string) });
            Assert.That(method, Is.Not.Null, "ResourceComponent must support pre-Start custom resource paths.");
            method.Invoke(component, new object[] { readOnly, readWrite });
        }

        private static void SetField(object target, string name, object value) =>
            target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(target, value);
    }

    public static class ResourceSlotObservation
    {
        public static readonly List<string> Stages = new List<string>();
        private static string root;
        public static void Reset(string slot) { root = slot; Stages.Clear(); }
        public static void ObserveConfiguredManager()
        {
            var component = GameEntry.GetComponent<ResourceComponent>();
            Assert.That(component.ReadOnlyPath, Is.EqualTo(Path.Combine(root, "Resources")), "Paths must be applied before helper creation.");
            Assert.That(component.ReadWritePath, Is.EqualTo(Path.Combine(root, "Scratch")));
            Assert.That(component.ResourceMode, Is.EqualTo(ResourceMode.Package));
            Assert.That(component.LoadTotalAgentCount, Is.Zero);
            Stages.Add("PathsAndPackageObservedBeforeAgents");
        }
    }

    public sealed class ObservedResourceHelper : DefaultResourceHelper
    {
        private void Awake() => ResourceSlotObservation.ObserveConfiguredManager();
        public override void LoadBytes(string fileUri, LoadBytesCallbacks callbacks, object userData)
        {
            Assert.That(GameEntry.GetComponent<ResourceComponent>().LoadTotalAgentCount, Is.EqualTo(3));
            Assert.That(fileUri.Replace('\\', '/'), Does.Contain("/Resources/GameFrameworkVersion.dat"));
            ResourceSlotObservation.Stages.Add("InitResources");
            base.LoadBytes(fileUri, callbacks, userData);
        }
    }

    public sealed class ObservedLoadAgentHelper : DefaultLoadResourceAgentHelper
    {
        private void Awake()
        {
            var component = GameEntry.GetComponent<ResourceComponent>();
            Assert.That(component.ResourceMode, Is.EqualTo(ResourceMode.Package));
            if (component.LoadTotalAgentCount == 0) ResourceSlotObservation.Stages.Add("AddAgents");
        }
    }
}
