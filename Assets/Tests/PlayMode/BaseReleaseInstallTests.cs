using System;
using System.Collections;
using System.IO;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Client.Framework.Release;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityGameFramework.Runtime;
using Object = UnityEngine.Object;

namespace Client.Tests.PlayMode
{
    [Category("F5")]
    public sealed class BaseReleaseInstallTests
    {
        [UnityTest] public IEnumerator StreamScratchBudgetRejectsBeforeCreatingDirectory()
        {
            string root=Path.GetFullPath(Path.Combine("Builds","F5StreamPathBudget",Guid.NewGuid().ToString("N")));
            string cache=Path.Combine(root,new string('x',210-root.Length-1));
            try
            {
                var reader=new PlatformReleaseStreamReader(new Uri(root+Path.DirectorySeparatorChar).AbsoluteUri,true,cache);
                var task=reader.OpenReadAsync("baseline.bin",CancellationToken.None);while(!task.IsCompleted)yield return null;
                Assert.That(task.IsFaulted,Is.True);Assert.That(task.Exception.InnerException,Is.TypeOf<PathTooLongException>());Assert.That(Directory.Exists(root),Is.False,"Scratch directory must not precede validation of its actual GUID filename.");
            }
            finally{if(Directory.Exists(root))Directory.Delete(root,true);}
        }
        [UnityTest] public IEnumerator NativeDownloadTempSuffixBudgetRejectsBeforeQueuingTransfer()
        {
            GameEntry.Shutdown(ShutdownType.None);yield return null;
            var host=new GameObject("F5 download path budget host");host.SetActive(false);host.AddComponent<BaseComponent>().EditorResourceMode=false;
            var events=host.AddComponent<EventComponent>();var downloads=host.AddComponent<DownloadComponent>();host.SetActive(true);yield return null;yield return null;
            string root=Path.GetFullPath(Path.Combine("Builds","F5DownloadPathBudget",Guid.NewGuid().ToString("N")));Directory.CreateDirectory(root);
            var transport=new GfDownloadTransport(downloads,events,"http://127.0.0.1:9");
            try
            {
                string destination=Path.Combine(root,new string('d',235-root.Length-1));int waiting=downloads.WaitingTaskCount,working=downloads.WorkingAgentCount;
                var task=transport.DownloadAsync("channel.json",destination,CancellationToken.None);Assert.That(task.IsCompleted,Is.True,"The real .download suffix must be checked before native queuing.");
                Assert.Throws<PathTooLongException>(()=>task.GetAwaiter().GetResult());Assert.That(downloads.WaitingTaskCount,Is.EqualTo(waiting));Assert.That(downloads.WorkingAgentCount,Is.EqualTo(working));Assert.That(Directory.GetFileSystemEntries(root),Is.Empty);
            }
            finally{transport.Dispose();GameEntry.Shutdown(ShutdownType.None);Object.Destroy(host);Directory.Delete(root,true);}
            yield return null;
        }
        [UnityTest]
        public IEnumerator AndroidBaselineUsesStreamReader()
        {
            string root = Path.GetFullPath(Path.Combine("Builds", "F5AndroidReader", Guid.NewGuid().ToString("N")));
            Directory.CreateDirectory(root);
            File.WriteAllBytes(Path.Combine(root, "baseline.bin"), new byte[] { 11, 22, 33 });
            try
            {
                // Executes the real Android URI reader in Editor against a file URI. Device jar IO is F8/D2.
                string cache = Path.Combine(root, "cache");
                var reader = new PlatformReleaseStreamReader(new Uri(root + Path.DirectorySeparatorChar).AbsoluteUri, true, cache);
                var task = reader.OpenReadAsync("baseline.bin", CancellationToken.None);
                while (!task.IsCompleted) yield return null;
                Assert.That(task.IsFaulted, Is.False, task.Exception?.ToString());
                using (Stream stream = task.GetAwaiter().GetResult())
                {
                    Assert.That(stream, Is.TypeOf<FileStream>(), "Baseline bundles must be streamed from scratch rather than buffered in RAM.");
                    Assert.That(Directory.GetFiles(cache).Length, Is.EqualTo(1));
                    var bytes = new byte[3]; Assert.That(stream.Read(bytes, 0, 3), Is.EqualTo(3));
                    Assert.That(bytes, Is.EqualTo(new byte[] { 11, 22, 33 }));
                }
                Assert.That(Directory.GetFiles(cache), Is.Empty, "Closing the source stream must remove its temporary file.");
            }
            finally { Directory.Delete(root, true); }
        }

        [UnityTest]
        public IEnumerator BaselineInstallerMaterializesSignedCompleteSlotThroughPlatformReader()
        {
            string root = Path.GetFullPath(Path.Combine("Builds", "F5Baseline", Guid.NewGuid().ToString("N")));
            string source = Path.Combine(root, "package"); string storage = Path.Combine(root, "persistent");
            Directory.CreateDirectory(Path.Combine(source, "Resources")); Directory.CreateDirectory(Path.Combine(source, "Hotfix"));
            byte[] resource = { 7, 8, 9 }, dll = { 1, 2, 3 };
            File.WriteAllBytes(Path.Combine(source, "Resources/GameFrameworkVersion.dat"), resource);
            File.WriteAllBytes(Path.Combine(source, "Hotfix/Game.dll"), dll);
            using (RSA key = new RSACryptoServiceProvider(2048) { PersistKeyInCsp = false })
            {
                var verifier = (ManifestVerifier)Activator.CreateInstance(typeof(ManifestVerifier), BindingFlags.Instance | BindingFlags.NonPublic,
                    null, new object[] { new Dictionary<string, RSAParameters> { ["test-f5"] = key.ExportParameters(false) } }, null);
                var player = new PlayerIdentity("base-player", "Android", "ARM64", "2022.3.25f1", "1", new string('a', 64), "aot-1");
                byte[] raw = Encoding.UTF8.GetBytes("{\"schemaVersion\":1,\"keyId\":\"test-f5\",\"releaseId\":\"baseline\",\"player\":{\"playerBuildId\":\"base-player\",\"platform\":\"Android\",\"architecture\":\"ARM64\",\"unityVersion\":\"2022.3.25f1\",\"contractVersion\":\"1\",\"envelopeSha256\":\"" + new string('a', 64) + "\",\"aotMetadataSetId\":\"aot-1\"},\"protocolSha256\":\"" + new string('b', 64) + "\",\"resourceVersionListPath\":\"Resources/GameFrameworkVersion.dat\",\"files\":[{\"path\":\"Resources/GameFrameworkVersion.dat\",\"length\":3,\"sha256\":\"" + Hash(resource) + "\",\"kind\":\"NativeResource\"},{\"path\":\"Hotfix/Game.dll\",\"length\":3,\"sha256\":\"" + Hash(dll) + "\",\"kind\":\"HotfixDll\"}],\"assemblies\":[{\"name\":\"Game\",\"path\":\"Hotfix/Game.dll\",\"dependencies\":[\"Client.Contracts\"]}],\"assemblyOrder\":[\"Game\"]}");
                var release = verifier.Verify(raw, key.SignData(raw, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1), player);
                var state = new ReleaseStateStore(storage, verifier, player);
                var reader = new PlatformReleaseStreamReader(new Uri(source + Path.DirectorySeparatorChar).AbsoluteUri, true, Path.Combine(root, "cache"));
                var installer = new BaseReleaseInstaller(storage, reader, state, new DiskBudget(_ => long.MaxValue));
                try
                {
                    var task = installer.InstallAsync(release, null, CancellationToken.None);
                    while (!task.IsCompleted) yield return null;
                    Assert.That(task.IsFaulted, Is.False, task.Exception?.ToString());
                    PreparedRelease prepared = task.GetAwaiter().GetResult();
                    Assert.That(File.ReadAllBytes(Path.Combine(prepared.Root, "Resources/GameFrameworkVersion.dat")), Is.EqualTo(resource));
                    Assert.That(File.ReadAllBytes(Path.Combine(prepared.Root, "Hotfix/Game.dll")), Is.EqualTo(dll));
                    Assert.That(state.Read().Candidate, Is.EqualTo("baseline"));
                    Assert.That(File.ReadAllBytes(Path.Combine(prepared.Root, "release.manifest.json")), Is.EqualTo(raw));
                }
                finally { Directory.Delete(root, true); }
            }
        }

        [UnityTest]
        public IEnumerator AndroidReaderErrorRemovesOnlyItsOwnedScratchFile()
        {
            string root = Path.GetFullPath(Path.Combine("Builds", "F5ReaderError", Guid.NewGuid().ToString("N")));
            Directory.CreateDirectory(root);
            File.WriteAllText(Path.Combine(root, "unrelated.partial"), "retain");
            try
            {
                var reader = new PlatformReleaseStreamReader(new Uri(root + Path.DirectorySeparatorChar).AbsoluteUri, true, root);
                var task = reader.OpenReadAsync("missing.bin", CancellationToken.None);
                while (!task.IsCompleted) yield return null;
                Assert.That(task.IsFaulted, Is.True);
                Assert.That(task.Exception.InnerException, Is.InstanceOf<IOException>());
                Assert.That(Directory.GetFiles(root), Is.EqualTo(new[] { Path.Combine(root, "unrelated.partial") }));
            }
            finally { Directory.Delete(root, true); }
        }

        [UnityTest]
        public IEnumerator AndroidReaderCancellationClosesRequestAndRemovesOwnedScratchFile()
        {
            string root = Path.GetFullPath(Path.Combine("Builds", "F5ReaderCancel", Guid.NewGuid().ToString("N")));
            Directory.CreateDirectory(root);
            File.WriteAllText(Path.Combine(root, "unrelated.partial"), "retain");
            var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            Task<TcpClient> accepting = listener.AcceptTcpClientAsync(); TcpClient client = null;
            using (var cancel = new CancellationTokenSource())
            {
                try
                {
                    var reader = new PlatformReleaseStreamReader("http://127.0.0.1:" + port, true, root);
                    var task = reader.OpenReadAsync("slow.bin", cancel.Token);
                    float deadline = Time.realtimeSinceStartup + 10;
                    while (!accepting.IsCompleted && Time.realtimeSinceStartup < deadline) yield return null;
                    Assert.That(accepting.IsCompleted, Is.True);
                    client = accepting.GetAwaiter().GetResult();
                    Assert.That(Directory.GetFiles(root).Length, Is.EqualTo(2), "A platform response must stream into an owned scratch file.");
                    cancel.Cancel();
                    while (!task.IsCompleted && Time.realtimeSinceStartup < deadline) yield return null;
                    Assert.That(task.IsCanceled, Is.True, task.Exception?.ToString());
                    Assert.That(Directory.GetFiles(root), Is.EqualTo(new[] { Path.Combine(root, "unrelated.partial") }));
                }
                finally { client?.Dispose(); listener.Stop(); Directory.Delete(root, true); }
            }
        }

        [UnityTest]
        public IEnumerator NativeGfDownloadTransportWritesActualHttpResponse()
        {
            GameEntry.Shutdown(ShutdownType.None); yield return null;
            var host = new GameObject("F5 native download host"); host.SetActive(false);
            host.AddComponent<BaseComponent>().EditorResourceMode = false;
            var events = host.AddComponent<EventComponent>(); var downloads = host.AddComponent<DownloadComponent>();
            host.SetActive(true); yield return null; yield return null;
            string root = Path.GetFullPath(Path.Combine("Builds", "F5NativeTransport", Guid.NewGuid().ToString("N")));
            Directory.CreateDirectory(root);
            var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            byte[] payload = { 3, 1, 4, 1, 5 };
            Task server = Task.Run(async () =>
            {
                using (TcpClient client = await listener.AcceptTcpClientAsync())
                using (NetworkStream stream = client.GetStream())
                {
                    var buffer = new byte[4096]; int count = await stream.ReadAsync(buffer, 0, buffer.Length);
                    if (count == 0) throw new IOException("No HTTP request received.");
                    byte[] header = Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\nContent-Length: 5\r\nConnection: close\r\n\r\n");
                    await stream.WriteAsync(header, 0, header.Length); await stream.WriteAsync(payload, 0, payload.Length);
                }
            });
            try
            {
                using (var transport = new GfDownloadTransport(downloads, events, "http://127.0.0.1:" + port + "/"))
                {
                    string path = Path.Combine(root, "response.partial");
                    string aliased = Path.Combine(root, string.Concat(System.Linq.Enumerable.Repeat("./", 120)), Path.GetFileName(path));
                    var task = transport.DownloadAsync("payload.bin", aliased, CancellationToken.None);
                    float deadline = Time.realtimeSinceStartup + 15;
                    while (!task.IsCompleted && Time.realtimeSinceStartup < deadline) yield return null;
                    Assert.That(task.IsCompleted, Is.True, "Native UGF transport timed out.");
                    Assert.That(task.IsFaulted, Is.False, task.Exception?.ToString());
                    task.GetAwaiter().GetResult();
                    Assert.That(File.ReadAllBytes(path), Is.EqualTo(payload));
                    Assert.That(server.IsFaulted, Is.False, server.Exception?.ToString());
                }
            }
            finally { listener.Stop(); GameEntry.Shutdown(ShutdownType.None); Object.Destroy(host); Directory.Delete(root, true); }
            yield return null;
            LogAssert.NoUnexpectedReceived();
        }

        private static string Hash(byte[] bytes) { using (var hash = SHA256.Create()) return BitConverter.ToString(hash.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant(); }
    }
}
