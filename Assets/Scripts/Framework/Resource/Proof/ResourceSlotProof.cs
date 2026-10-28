#if UNITY_EDITOR || RESOURCE_SLOT_PROOF
using System;
using System.IO;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Client.Framework.Resource;
using UnityEngine;

namespace Client.ResourceSlot.Proof
{
    public sealed class ResourceSlotProof : MonoBehaviour
    {
        private async void Start()
        {
            var result = new ProofResult();
            string output = null;
            try
            {
                output = Argument("-slotResult");
                result.slotRoot = Path.GetFullPath(Argument("-slotRoot"));
                result.expected = Argument("-slotExpected");
                result.processId = System.Diagnostics.Process.GetCurrentProcess().Id;
                var bootstrap = FindObjectOfType<ResourceSlotBootstrap>();
                bootstrap.Activate(result.slotRoot);
                await Task.WhenAny(bootstrap.Initialization, Task.Delay(30000));
                if (!bootstrap.Initialization.IsCompleted) throw new TimeoutException("Native Package initialization timed out.");
                bootstrap.Initialization.GetAwaiter().GetResult();
                result.readOnlyPath = bootstrap.ResourceComponent.ReadOnlyPath;
                result.readWritePath = bootstrap.ResourceComponent.ReadWritePath;
                result.mode = bootstrap.ResourceComponent.ResourceMode.ToString();
                result.agentCount = bootstrap.ResourceComponent.LoadTotalAgentCount;
                result.versionTableHash = Hash(Path.Combine(result.readOnlyPath, "GameFrameworkVersion.dat"));
                var load = bootstrap.Service.LoadTextAsync("Assets/Tests/Generated/ResourceSlot/marker.txt");
                await Task.WhenAny(load, Task.Delay(30000));
                if (!load.IsCompleted) throw new TimeoutException("Native marker load timed out.");
                result.marker = load.GetAwaiter().GetResult().Trim();
                if (result.marker != result.expected) throw new InvalidOperationException("Unexpected marker: " + result.marker);
                bool rejected = false;
                try { bootstrap.ResourceComponent.SetCustomResourcePaths(result.slotRoot + "-other", result.slotRoot + "-other"); }
                catch (InvalidOperationException) { rejected = true; }
                if (!rejected) throw new InvalidOperationException("Late root mutation was accepted.");
                if (bootstrap.ResourceComponent.ReadOnlyPath != result.readOnlyPath) throw new InvalidOperationException("Root changed.");
                result.lateMutationRejected = rejected;
                bool secondActivationRejected = false;
                try { bootstrap.Activate(result.slotRoot); }
                catch (InvalidOperationException) { secondActivationRejected = true; }
                if (!secondActivationRejected) throw new InvalidOperationException("Second activation was accepted.");
                result.secondActivationRejected = true;
                result.passed = true;
                Debug.Log("RESOURCE_SLOT_LOADED " + result.marker + " " + result.readOnlyPath);
            }
            catch (Exception exception) { result.error = exception.ToString(); Debug.LogException(exception); }
            if (output != null) File.WriteAllText(output, JsonUtility.ToJson(result, true) + "\n");
            Application.Quit(result.passed ? 0 : 1);
        }

        private static string Argument(string name)
        {
            var args = Environment.GetCommandLineArgs();
            int index = Array.IndexOf(args, name);
            if (index < 0 || index + 1 >= args.Length) throw new ArgumentException(name + " is required.");
            return args[index + 1];
        }

        private static string Hash(string file)
        {
            using (var sha = SHA256.Create()) using (var stream = File.OpenRead(file))
                return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
        }

        [Serializable]
        private sealed class ProofResult
        {
            public bool passed;
            public bool lateMutationRejected;
            public bool secondActivationRejected;
            public int processId;
            public string slotRoot;
            public string expected;
            public string marker;
            public string readOnlyPath;
            public string readWritePath;
            public string mode;
            public int agentCount;
            public string versionTableHash;
            public string error;
        }
    }
}
#endif
