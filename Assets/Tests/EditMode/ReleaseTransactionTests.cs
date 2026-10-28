using System;
using System.Collections.Generic;
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

namespace Client.Tests.EditMode
{
    [Category("F5")]
    public sealed class ReleaseTransactionTests
    {
        private string root;
        private RSA key;
        private ManifestVerifier verifier;
        private PlayerIdentity player;
        private Faults faults;
        private ReleaseStateStore state;
        private FilesTransport transport;
        private ReleaseStore store;
        private VerifiedRelease oldRelease;
        private VerifiedRelease nextRelease;

        [OneTimeSetUp]
        public void CreateEphemeralSigner()
        {
            key = new RSACryptoServiceProvider(2048) { PersistKeyInCsp = false };
            verifier = (ManifestVerifier)Activator.CreateInstance(typeof(ManifestVerifier), BindingFlags.Instance | BindingFlags.NonPublic,
                null, new object[] { new Dictionary<string, RSAParameters> { ["test-f5"] = key.ExportParameters(false) } }, null);
            player = new PlayerIdentity("player-1", "Windows", "x64", "2022.3.25f1", "1", new string('a', 64), "aot-1");
        }
        [OneTimeTearDown]
        public void DisposeSigner() => key?.Dispose();
        [SetUp]
        public void Setup()
        {
            root = Path.GetFullPath(Path.Combine("Builds", "F5Scratch", Guid.NewGuid().ToString("N")));
            faults = new Faults();
            state = new ReleaseStateStore(root, verifier, player, faults);
            transport = new FilesTransport();
            oldRelease = Sign("old", new byte[] { 1, 2, 3 });
            nextRelease = Sign("next", new byte[] { 4, 5, 6 });
            store = new ReleaseStore(root, new ReleaseDownloader(transport, (delay, token) => Task.CompletedTask), state, new DiskBudget(_ => long.MaxValue), faults);
        }

        [TearDown]
        public void Cleanup() { if (Directory.Exists(root)) Directory.Delete(root, true); }

        private string InstallGood()
        {
            Put(oldRelease, new byte[] { 1, 2, 3 });
            PreparedRelease prepared = Run(() => store.PrepareAsync(oldRelease, null, CancellationToken.None));
            state.WriteAtomic(new ReleaseState { KnownGood = "old" });
            transport.Requests.Clear();
            Put(nextRelease, new byte[] { 4, 5, 6 });
            return prepared.Root;
        }

        [Test]
        public void ChangedOnlyDownloadStillCreatesFullSnapshot()
        {
            string good = InstallGood();
            PreparedRelease prepared = Run(() => store.PrepareAsync(nextRelease, null, CancellationToken.None));
            Assert.That(transport.Requests, Is.EqualTo(new[] { "Hotfix/Game.dll" }));
            foreach (var file in nextRelease.Files)
            {
                byte[] bytes = File.ReadAllBytes(Path.Combine(prepared.Root, file.Path));
                Assert.That(bytes.LongLength, Is.EqualTo(file.Length));
                Assert.That(Hash(bytes), Is.EqualTo(file.Sha256));
            }
            Assert.That(File.ReadAllBytes(Path.Combine(good, "Hotfix/Game.dll")), Is.EqualTo(new byte[] { 1, 2, 3 }));
            Assert.That(state.Read().KnownGood, Is.EqualTo("old"));
            Assert.That(state.Read().Candidate, Is.EqualTo("next"));
            Assert.That(File.ReadAllBytes(Path.Combine(prepared.Root, "release.manifest.json")), Is.EqualTo(nextRelease.ManifestBytes));
            Assert.That(File.ReadAllBytes(Path.Combine(prepared.Root, "release.manifest.sig")), Is.EqualTo(nextRelease.SignatureBytes));
        }

        [TestCase("copy.before")]
        [TestCase("copy.after")]
        [TestCase("download.before")]
        [TestCase("download.after")]
        [TestCase("hash.before")]
        [TestCase("hash.after")]
        [TestCase("rename.before")]
        [TestCase("rename.after")]
        [TestCase("state.write.before")]
        [TestCase("state.write.after")]
        [TestCase("state.replace.before")]
        [TestCase("state.replace.after")]
        public void InterruptedPreparePreservesKnownGood(string checkpoint)
        {
            string good = InstallGood();
            byte[] before = File.ReadAllBytes(Path.Combine(good, "Hotfix/Game.dll"));
            faults.Checkpoint = checkpoint;
            Throws<IOException>(() => store.PrepareAsync(nextRelease, null, CancellationToken.None));
            Assert.That(faults.Hits, Does.Contain(checkpoint));
            faults.Checkpoint = null;
            Assert.That(state.Read().KnownGood, Is.EqualTo("old"));
            Assert.That(state.Read().Candidate, checkpoint == "state.replace.after" ? Is.EqualTo("next") : Is.Null);
            Assert.That(File.ReadAllBytes(Path.Combine(good, "Hotfix/Game.dll")), Is.EqualTo(before));
        }

        [Test]
        public void NoSpaceAndCancellationNeverActivate()
        {
            InstallGood();
            var noSpace = new ReleaseStore(root, new ReleaseDownloader(transport), state, new DiskBudget(_ => 0));
            Throws<NoSpaceException>(() => noSpace.PrepareAsync(nextRelease, null, CancellationToken.None));
            using (var cancel = new CancellationTokenSource())
            {
                cancel.Cancel();
                Throws<OperationCanceledException>(() => store.PrepareAsync(nextRelease, null, cancel.Token));
            }
            Assert.That(transport.Requests, Is.Empty);
            Assert.That(state.Read().Candidate, Is.Null);
            Assert.That(state.Read().KnownGood, Is.EqualTo("old"));
        }

        [Test]
        public void CancellationDuringTransferNeverPublishesCandidate()
        {
            InstallGood();
            using (var cancel = new CancellationTokenSource())
            {
                transport.OnWrite = () => cancel.Cancel();
                Throws<OperationCanceledException>(() => store.PrepareAsync(nextRelease, null, cancel.Token));
            }
            Assert.That(state.Read().Candidate, Is.Null);
        }

        [Test]
        public void StateRecoveryUsesValidPrimaryOrBackup()
        {
            InstallGood();
            Run(() => store.PrepareAsync(nextRelease, null, CancellationToken.None));
            File.WriteAllText(Path.Combine(root, "state.json"), "{broken");
            Assert.That(state.Read().KnownGood, Is.EqualTo("old"));
            Assert.That(state.Read().Candidate, Is.Null);
            Assert.That(File.Exists(Path.Combine(root, "state.json")), Is.True, "Corrupt evidence must be retained.");
            File.WriteAllText(Path.Combine(root, "state.json.bak"), "{}");
            var error = Assert.Throws<AggregateException>(() => state.Read());
            Assert.That(error.InnerExceptions.Count, Is.EqualTo(2));
        }

        [Test]
        public void StateRecoveryRejectsTamperedOrPartialReferences()
        {
            InstallGood();
            var prepared = Run(() => store.PrepareAsync(nextRelease, null, CancellationToken.None));
            File.WriteAllBytes(Path.Combine(prepared.Root, "Hotfix/Game.dll"), new byte[] { 9 });
            Assert.That(state.Read().Candidate, Is.Null, "Tampered primary must fall back to valid backup.");
            Assert.Throws<InvalidDataException>(() => state.WriteAtomic(new ReleaseState { KnownGood = "next.partial" }));
            Assert.Throws<InvalidDataException>(() => state.WriteAtomic(new ReleaseState { Attempt = 1 }));
        }

        [Test]
        public void PendingTrialCannotBeReplaced()
        {
            InstallGood();
            Run(() => store.PrepareAsync(nextRelease, null, CancellationToken.None));
            state.WriteAtomic(new ReleaseState { KnownGood = "old", Candidate = "next", Attempt = 1 });
            Throws<InvalidOperationException>(() => store.PrepareAsync(oldRelease, null, CancellationToken.None));
            Assert.That(state.Read().Candidate, Is.EqualTo("next"));
            Assert.That(state.Read().Attempt, Is.EqualTo(1));
        }

        [Test]
        public void CorruptKnownGoodFailsClosedAndHashFailureIsNotRetried()
        {
            string good = InstallGood();
            File.WriteAllBytes(Path.Combine(good, "Resources/GameFrameworkVersion.dat"), new byte[] { 9, 9, 9 });
            // State cannot reference a corrupt known-good; recovery must fail closed.
            Throws<AggregateException>(() => store.PrepareAsync(nextRelease, null, CancellationToken.None));
            Assert.That(transport.Requests, Is.Empty);
            transport.Data["Hotfix/Game.dll"] = new byte[] { 0, 0, 0 };
            var downloader = new ReleaseDownloader(transport, (delay, token) => Task.CompletedTask);
            Throws<InvalidDataException>(() => downloader.DownloadAsync(nextRelease.Files.Single(f => f.Path == "Hotfix/Game.dll"), Path.Combine(root, "bad.partial"), CancellationToken.None));
            Assert.That(transport.Requests.Count, Is.EqualTo(1));
        }

        [Test]
        public void TransportRetriesThreeTimesWithOneAndTwoSecondBackoff()
        {
            Directory.CreateDirectory(root); Put(nextRelease, new byte[] { 4, 5, 6 });
            var delays = new List<double>(); transport.Failures = 2;
            var downloader = new ReleaseDownloader(transport, (delay, token) => { delays.Add(delay.TotalSeconds); return Task.CompletedTask; });
            string path = Path.Combine(root, "download.partial");
            Run(() => downloader.DownloadAsync(nextRelease.Files[0], path, CancellationToken.None));
            Assert.That(File.ReadAllBytes(path), Is.EqualTo(new byte[] { 7, 8, 9 }));
            Assert.That(transport.Requests.Count, Is.EqualTo(3));
            Assert.That(delays, Is.EqualTo(new[] { 1d, 2d }));
            transport.Failures = 9; transport.Requests.Clear();
            Throws<ReleaseTransportException>(() => downloader.DownloadAsync(nextRelease.Files[0], path, CancellationToken.None));
            Assert.That(transport.Requests.Count, Is.EqualTo(3));
        }

        [Test]
        public void MaterializationRunsAtMostTwoTransfers()
        {
            Put(nextRelease, new byte[] { 4, 5, 6 }); transport.Delay = true;
            Run(() => store.PrepareAsync(nextRelease, null, CancellationToken.None));
            Assert.That(transport.MaximumActive, Is.EqualTo(2));
        }

        [Test]
        public void DiskBudgetIncludesFullSnapshotTemporaryBytesAndMargin()
        {
            long required = 64L * 1024 * 1024 + 12 + nextRelease.ManifestBytes.Length + nextRelease.SignatureBytes.Length;
            Assert.Throws<NoSpaceException>(() => new DiskBudget(_ => required - 1).Require(root, nextRelease));
            Assert.DoesNotThrow(() => new DiskBudget(_ => required).Require(root, nextRelease));
        }

        [Test]
        public void DiskBudgetQueriesNearestExistingTargetAncestorWithoutCreatingDirectories()
        {
            string mount = Path.Combine(root, "target-mount");
            string requested = Path.Combine(mount, "new-release", "storage");
            Directory.CreateDirectory(mount);
            string queried = null;
            var budget = new DiskBudget(path => { queried = path; return path == mount ? long.MaxValue : 0; });
            Assert.DoesNotThrow(() => budget.Require(requested, nextRelease));
            Assert.That(queried, Is.EqualTo(mount), "Capacity must come from the target mount, not '/' or an absent descendant.");
            Assert.That(Directory.Exists(Path.Combine(mount, "new-release")), Is.False);
        }

        [Test]
        public void DiskBudgetUsesExistingTargetPathProviderResultRatherThanVolumeRoot()
        {
            Directory.CreateDirectory(root);
            string queried = null;
            var budget = new DiskBudget(path => { queried = path; return path == root ? 0 : long.MaxValue; });
            Assert.Throws<NoSpaceException>(() => budget.Require(root, nextRelease));
            Assert.That(queried, Is.EqualTo(root));
            Assert.That(Directory.GetFileSystemEntries(root), Is.Empty);
        }

        [Test]
        public void DefaultWindowsCapacityProviderQueriesActualStorageWithoutCreatingTarget()
        {
            if (Environment.OSVersion.Platform != PlatformID.Win32NT) Assert.Ignore("Windows native capacity adapter.");
            if (new DriveInfo(Path.GetPathRoot(root)).AvailableFreeSpace < 1024L * 1024 * 1024)
                Assert.Ignore("Native capacity smoke requires at least 1 GiB of free test storage.");
            Assert.DoesNotThrow(() => new DiskBudget().Require(root, nextRelease));
            Assert.That(Directory.Exists(root), Is.False);
        }

        [Test]
        public void MaximumSizedStateRoundTripsPrimaryAndBackupAndReaderRejectsOneExtraByte()
        {
            const int maximumBytes = 256 * 1024;
            ReleaseState maximum = SizedState(maximumBytes);
            state.WriteAtomic(maximum);
            Assert.That(new FileInfo(Path.Combine(root, "state.json")).Length, Is.EqualTo(maximumBytes));
            Assert.That(state.Read().BadReleaseIds, Is.EqualTo(maximum.BadReleaseIds));
            state.WriteAtomic(maximum);
            Assert.That(new FileInfo(Path.Combine(root, "state.json.bak")).Length, Is.EqualTo(maximumBytes));
            File.AppendAllText(Path.Combine(root, "state.json"), " ", new UTF8Encoding(false));
            Assert.That(state.Read().BadReleaseIds, Is.EqualTo(maximum.BadReleaseIds));
            Assert.That(state.RecoveryDiagnostics.Count, Is.EqualTo(1), "The cap must reject primary +1 byte, then accept the cap-sized backup.");
        }

        [Test]
        public void OversizedStateRejectsBeforeMutatingPrimaryBackupOrTemporaryFiles()
        {
            InstallGood();
            state.WriteAtomic(new ReleaseState { KnownGood = "old", BadReleaseIds = new[] { "existing-quarantine" } });
            string primary = Path.Combine(root, "state.json"), backup = Path.Combine(root, "state.json.bak");
            byte[] priorPrimary = File.ReadAllBytes(primary), priorBackup = File.ReadAllBytes(backup);
            string[] priorTemporaries = Directory.GetFiles(root, "state.*.partial");
            faults.Hits.Clear();
            Assert.Throws<InvalidDataException>(() => state.WriteAtomic(SizedState(256 * 1024 + 1)));
            Assert.That(File.ReadAllBytes(primary), Is.EqualTo(priorPrimary));
            Assert.That(File.ReadAllBytes(backup), Is.EqualTo(priorBackup));
            Assert.That(Directory.GetFiles(root, "state.*.partial"), Is.EqualTo(priorTemporaries));
            Assert.That(faults.Hits, Is.Empty, "Oversize rejection must precede every write boundary.");
            Assert.That(state.Read().KnownGood, Is.EqualTo("old"));
            Assert.That(state.Read().BadReleaseIds, Is.EqualTo(new[] { "existing-quarantine" }));
        }

        [Test]
        public void StructurallyAllowedLargeQuarantineNeverCreatesUnreadableInitialState()
        {
            var oversized = new ReleaseState { BadReleaseIds = Enumerable.Range(0, 4096).Select(index => index.ToString("D4") + new string('q', 124)).ToArray() };
            Assert.Throws<InvalidDataException>(() => state.WriteAtomic(oversized));
            Assert.That(Directory.Exists(root), Is.False, "An oversized initial state must be rejected before creating storage.");
            Assert.That(state.Read().BadReleaseIds, Is.Empty);
        }

        private static ReleaseState SizedState(int exactBytes)
        {
            // Independent schema-1 byte accounting: identifiers are ASCII and need no JSON escaping.
            const string empty = "{\"schemaVersion\":1,\"knownGood\":null,\"previousGood\":null,\"candidate\":null,\"attempt\":0,\"badReleaseIds\":[]}";
            const int count = 2048;
            int totalIdentifierBytes = exactBytes - Encoding.UTF8.GetByteCount(empty) - (3 * count - 1);
            int length = totalIdentifierBytes / count, longer = totalIdentifierBytes % count;
            Assert.That(length, Is.InRange(5, 127), "Boundary fixture must fit the unchanged identifier/count limits.");
            return new ReleaseState { BadReleaseIds = Enumerable.Range(0, count).Select(index =>
                index.ToString("D4") + "-" + new string('q', length - 5 + (index < longer ? 1 : 0))).ToArray() };
        }

        [TestCase("Resources/CON.dat")]
        [TestCase("Resources/GameFrameworkVersion.dat/child")]
        [TestCase("release.manifest.json")]
        public void FilesystemUnsafeNamesNeverWriteSlots(string unsafePath)
        {
            var release = Sign("unsafe", new byte[] { 4, 5, 6 }, unsafePath);
            Throws<InvalidDataException>(() => store.PrepareAsync(release, null, CancellationToken.None));
            Assert.That(Directory.Exists(Path.Combine(root, "slots", "unsafe")), Is.False);
        }

        [Test]
        public void ExistingCompleteSlotIsNeverOverwritten()
        {
            string good = InstallGood();
            var differentSameId = Sign("old", new byte[] { 9, 9, 9 });
            Put(differentSameId, new byte[] { 9, 9, 9 });
            Throws<InvalidDataException>(() => store.PrepareAsync(differentSameId, null, CancellationToken.None));
            Assert.That(File.ReadAllBytes(Path.Combine(good, "Hotfix/Game.dll")), Is.EqualTo(new byte[] { 1, 2, 3 }));
        }

        [Test]
        public void RealFileDirectoryCollisionPreservesStableSlot()
        {
            string good = InstallGood();
            File.WriteAllText(Path.Combine(root, "slots", "next"), "occupied");
            Throws<IOException>(() => store.PrepareAsync(nextRelease, null, CancellationToken.None));
            Assert.That(state.Read().KnownGood, Is.EqualTo("old"));
            Assert.That(File.ReadAllBytes(Path.Combine(good, "Hotfix/Game.dll")), Is.EqualTo(new byte[] { 1, 2, 3 }));
        }

        [Test]
        public void RealWindowsJunctionCannotRedirectSlotWrites()
        {
            if (Path.DirectorySeparatorChar != '\\') Assert.Ignore("Windows junction behavior.");
            string target = Path.Combine(root, "outside-slots");
            string link = Path.Combine(root, "slots");
            Directory.CreateDirectory(target);
            using (var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "cmd.exe", Arguments = "/c mklink /J \"" + link + "\" \"" + target + "\"",
                UseShellExecute = false, CreateNoWindow = true, WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden,
                RedirectStandardOutput = true, RedirectStandardError = true
            }))
            {
                process.WaitForExit(); Assert.That(process.ExitCode, Is.EqualTo(0), process.StandardError.ReadToEnd());
            }
            try
            {
                Put(nextRelease, new byte[] { 4, 5, 6 });
                Throws<InvalidDataException>(() => store.PrepareAsync(nextRelease, null, CancellationToken.None));
                Assert.That(Directory.GetFileSystemEntries(target), Is.Empty);
                Assert.That(transport.Requests, Is.Empty);
            }
            finally { Directory.Delete(link); }
        }

        private VerifiedRelease Sign(string id, byte[] dll, string path = "Hotfix/Game.dll")
        {
            var doc = JObject.Parse(File.ReadAllText("Tests/Fixtures/Releases/ManifestCases/valid-release.json"));
            doc["keyId"] = "test-f5"; doc["releaseId"] = id;
            doc["files"] = new JArray(Entry("Resources/GameFrameworkVersion.dat", new byte[] { 7, 8, 9 }, "NativeResource"), Entry(path, dll, "HotfixDll"));
            doc["assemblies"] = new JArray(new JObject { ["name"] = "Game", ["path"] = path, ["dependencies"] = new JArray("Client.Contracts") });
            doc["assemblyOrder"] = new JArray("Game");
            byte[] raw = Encoding.UTF8.GetBytes(doc.ToString(Formatting.None));
            return verifier.Verify(raw, key.SignData(raw, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1), player);
        }
        private static JObject Entry(string path, byte[] bytes, string kind) => new JObject { ["path"] = path, ["length"] = bytes.Length, ["sha256"] = Hash(bytes), ["kind"] = kind };
        private void Put(VerifiedRelease release, byte[] dll)
        { transport.Data["Resources/GameFrameworkVersion.dat"] = new byte[] { 7, 8, 9 }; transport.Data[release.Files[1].Path] = dll; }
        private static T Run<T>(Func<Task<T>> action) => Task.Run(action).GetAwaiter().GetResult();
        private static void Run(Func<Task> action) => Task.Run(action).GetAwaiter().GetResult();
        private static T Throws<T>(Func<Task> action) where T : Exception => Assert.Catch<T>(() => Run(action));
        private static string Hash(byte[] bytes) { using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant(); }
        private sealed class Faults : IFileFaultInjector
        {
            public string Checkpoint; public readonly List<string> Hits = new List<string>();
            public void Hit(string checkpoint) { Hits.Add(checkpoint); if (Checkpoint == checkpoint) throw new IOException("Injected: " + checkpoint); }
        }
        private sealed class FilesTransport : IReleaseTransport
        {
            public readonly Dictionary<string, byte[]> Data = new Dictionary<string, byte[]>();
            public readonly List<string> Requests = new List<string>();
            public int Failures, MaximumActive; private int active; public bool Delay; public Action OnWrite;
            public async Task DownloadAsync(string relativePath, string destination, CancellationToken token)
            {
                lock (Requests) { Requests.Add(relativePath); active++; MaximumActive = Math.Max(MaximumActive, active); }
                try
                {
                    if (Delay) await Task.Delay(20, token);
                    if (Failures-- > 0) throw new ReleaseTransportException("Transport unavailable.");
                    token.ThrowIfCancellationRequested(); File.WriteAllBytes(destination, Data[relativePath]); OnWrite?.Invoke();
                }
                finally { lock (Requests) active--; }
            }
        }
    }
}
