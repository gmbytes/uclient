using System;
using System.Collections;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;
using UnityGameFramework.Runtime;
using Client.Framework.Release;

namespace Client.Framework.Resource
{
    public sealed class ResourceSlotBootstrap : MonoBehaviour
    {
        [SerializeField] private GameObject m_GameRootPrefab;
        private static bool s_Activated;
        private readonly TaskCompletionSource<bool> initialization = new TaskCompletionSource<bool>();

        public ResourceComponent ResourceComponent { get; private set; }
        public ResourceService Service { get; private set; }
        public Task Initialization => initialization.Task;

        public void Activate(string slotRoot)
        {
            if (s_Activated) throw new InvalidOperationException("A resource slot has already been activated in this process.");
            if (string.IsNullOrWhiteSpace(slotRoot)) throw new ArgumentException("Slot root is required.", nameof(slotRoot));
            string root = Path.GetFullPath(slotRoot);
            string readOnly = Path.Combine(root, "Resources");
            string scratch = Path.Combine(root, "Scratch");
            ReleaseFileSystem.RequireSupportedPath(Path.Combine(readOnly, "GameFrameworkVersion.dat"));
            ReleaseFileSystem.RequireSupportedPath(scratch);
            if (!File.Exists(Path.Combine(readOnly, "GameFrameworkVersion.dat")))
                throw new FileNotFoundException("Complete native Package version table is required.", readOnly);
            if (m_GameRootPrefab == null || m_GameRootPrefab.activeSelf)
                throw new InvalidOperationException("An inactive GameRoot prefab is required.");
            if (GameEntry.GetComponent<ResourceComponent>() != null)
                throw new InvalidOperationException("Resource component already exists.");

            Directory.CreateDirectory(scratch);
            s_Activated = true;
            var gameRoot = Instantiate(m_GameRootPrefab);
            gameRoot.name = "GameRoot";
            ResourceComponent = gameRoot.GetComponent<ResourceComponent>();
            if (ResourceComponent == null) throw new InvalidOperationException("GameRoot requires ResourceComponent.");
            ResourceComponent.SetCustomResourcePaths(readOnly, scratch);
            Service = new ResourceService(ResourceComponent);
            gameRoot.SetActive(true);
            StartCoroutine(InitializeAfterComponentStart());
        }

        private IEnumerator InitializeAfterComponentStart()
        {
            yield return null;
            try
            {
                if (ResourceComponent.ResourceMode != GameFramework.Resource.ResourceMode.Package || ResourceComponent.LoadTotalAgentCount == 0)
                    throw new InvalidOperationException("Package resource agents were not initialized.");
                ResourceComponent.InitResources(() => initialization.TrySetResult(true));
            }
            catch (Exception exception) { initialization.TrySetException(exception); }
        }
    }
}
