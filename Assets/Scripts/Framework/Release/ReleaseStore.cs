using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace Client.Framework.Release
{
    public interface IFileFaultInjector { void Hit(string checkpoint); }
    public sealed class NoFileFaults : IFileFaultInjector { public void Hit(string checkpoint) { } }
    public sealed class ReleaseProgress
    {
        public string Path { get; }
        public long CompletedBytes { get; }
        public long TotalBytes { get; }
        public ReleaseProgress(string path, long completedBytes, long totalBytes)
        { Path = path; CompletedBytes = completedBytes; TotalBytes = totalBytes; }
    }
    public sealed class PreparedRelease
    {
        public string Root { get; }
        public VerifiedRelease Release { get; }
        internal PreparedRelease(string root, VerifiedRelease release) { Root = root; Release = release; }
    }
    public sealed class ReleaseStore
    {
        public static string PersistentRoot => Path.Combine(UnityEngine.Application.persistentDataPath, "Releases");
        private readonly string root;
        private readonly ReleaseDownloader downloader;
        private readonly ReleaseStateStore state;
        private readonly DiskBudget budget;
        private readonly IFileFaultInjector faults;
        public ReleaseStore(string root, ReleaseDownloader downloader, ReleaseStateStore state, DiskBudget budget = null, IFileFaultInjector faults = null)
        {
            this.root = ReleaseFileSystem.Root(root);
            this.downloader = downloader ?? throw new ArgumentNullException(nameof(downloader));
            this.state = state ?? throw new ArgumentNullException(nameof(state));
            if (!string.Equals(this.root, state.Root, StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Release and state roots must match.");
            this.budget = budget ?? new DiskBudget(); this.faults = faults ?? new NoFileFaults();
        }

        public async Task<PreparedRelease> PrepareAsync(VerifiedRelease release, IProgress<ReleaseProgress> progress, CancellationToken token)
        {
            if (release == null) throw new ArgumentNullException(nameof(release));
            token.ThrowIfCancellationRequested();
            ReleaseFileSystem.ValidateInventory(release);
            var gate = ReleaseFileSystem.PrepareGate(root);
            await gate.WaitAsync(token);
            try
            {
                ReleaseState before = state.Read();
                if (before.Attempt > 0) throw new InvalidOperationException("An unfinished candidate trial must be recovered before preparation.");
                string destination = ReleaseFileSystem.Child(root, "slots/" + release.ReleaseId);
                if (Directory.Exists(destination))
                {
                    VerifiedRelease existing = state.VerifySlot(release.ReleaseId);
                    if (existing.ManifestSha256 != release.ManifestSha256) throw new InvalidDataException("A complete release ID cannot be overwritten.");
                    token.ThrowIfCancellationRequested(); state.PublishCandidate(release.ReleaseId, token);
                    return new PreparedRelease(destination, existing);
                }
                if (File.Exists(destination)) throw new IOException("Release slot path is occupied by a file.");
                budget.Require(root, release);
                string stage = ReleaseFileSystem.Child(root, "staging/" + release.ReleaseId + "." + release.ManifestSha256 + ".partial");
                string filesRoot = ReleaseFileSystem.Child(stage, "files");
                string downloadsRoot = ReleaseFileSystem.Child(stage, "downloads");
                Directory.CreateDirectory(filesRoot); Directory.CreateDirectory(downloadsRoot);
                string reuseRoot = before.KnownGood == null ? null : ReleaseFileSystem.Child(root, "slots/" + before.KnownGood);
                long completed = 0, total = release.Files.Sum(file => file.Length);
                var files = release.Files.Select(async (file, index) =>
                {
                    token.ThrowIfCancellationRequested();
                    string output = ReleaseFileSystem.Child(filesRoot, file.Path);
                    Directory.CreateDirectory(Path.GetDirectoryName(output));
                    string source = reuseRoot == null ? null : ReleaseFileSystem.Child(reuseRoot, file.Path);
                    bool reusable = source != null && ReleaseFileSystem.Matches(source, file, token);
                    if (reusable)
                    {
                        faults.Hit("copy.before");
                        File.Copy(source, output, true);
                        faults.Hit("copy.after");
                    }
                    else
                    {
                        string part = ReleaseFileSystem.Child(downloadsRoot, index + ".partial");
                        faults.Hit("download.before");
                        await downloader.DownloadAsync(file, part, token);
                        faults.Hit("download.after");
                        token.ThrowIfCancellationRequested();
                        if (File.Exists(output)) File.Delete(output);
                        File.Move(part, output);
                    }
                    faults.Hit("hash.before");
                    ReleaseFileSystem.VerifyFile(output, file, token);
                    faults.Hit("hash.after");
                    progress?.Report(new ReleaseProgress(file.Path, Interlocked.Add(ref completed, file.Length), total));
                }).ToArray();
                await Task.WhenAll(files);
                token.ThrowIfCancellationRequested();
                ReleaseFileSystem.WriteDurable(ReleaseFileSystem.Child(filesRoot, "release.manifest.json"), release.ManifestBytes);
                ReleaseFileSystem.WriteDurable(ReleaseFileSystem.Child(filesRoot, "release.manifest.sig"), release.SignatureBytes);
                state.VerifyDirectory(filesRoot, release.ReleaseId);
                token.ThrowIfCancellationRequested();
                Directory.CreateDirectory(ReleaseFileSystem.Child(root, "slots"));
                faults.Hit("rename.before");
                Directory.Move(filesRoot, destination);
                faults.Hit("rename.after");
                token.ThrowIfCancellationRequested();
                state.PublishCandidate(release.ReleaseId, token);
                return new PreparedRelease(destination, release);
            }
            finally { gate.Release(); }
        }
    }

    internal static class ReleaseFileSystem
    {
        private static readonly Dictionary<string, SemaphoreSlim> Gates = new Dictionary<string, SemaphoreSlim>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, object> StateLocks = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        internal static SemaphoreSlim PrepareGate(string root)
        { lock (Gates) { if (!Gates.TryGetValue(root, out var value)) Gates[root] = value = new SemaphoreSlim(1, 1); return value; } }
        internal static object StateLock(string root)
        { lock (StateLocks) { if (!StateLocks.TryGetValue(root, out var value)) StateLocks[root] = value = new object(); return value; } }
        internal static string Root(string root)
        {
            if (string.IsNullOrWhiteSpace(root)) throw new ArgumentException("Release root is required.");
            string full = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            NoReparse(full); return full;
        }
        internal static string Child(string root, string relative)
        {
            string full = Path.GetFullPath(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar)));
            if (!full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Release path escapes its root.");
            NoReparse(full); return full;
        }
        internal static void NoReparse(string path)
        {
            for (string current = path; !string.IsNullOrEmpty(current); current = Path.GetDirectoryName(current))
                if ((Directory.Exists(current) || File.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidDataException("Release paths cannot traverse reparse points.");
        }
        internal static void ValidateId(string id)
        {
            try { RelativePathPolicy.RequireIdentifier(id); RelativePathPolicy.RequireCanonical(id); }
            catch (ReleaseVerificationException error) { throw new InvalidDataException("Invalid release reference.", error); }
            if (id.EndsWith(".partial", StringComparison.OrdinalIgnoreCase) || Reserved(id)) throw new InvalidDataException("Invalid release reference.");
        }
        internal static void ValidateInventory(VerifiedRelease release)
        {
            ValidateId(release.ReleaseId);
            var names = new HashSet<string>(release.Files.Select(file => file.Path), StringComparer.OrdinalIgnoreCase);
            foreach (ReleaseFile file in release.Files)
            {
                string[] parts = file.Path.Split('/');
                if (parts.Any(Reserved) || parts[0].Equals("Scratch", StringComparison.OrdinalIgnoreCase) ||
                    parts[0].Equals("release.manifest.json", StringComparison.OrdinalIgnoreCase) || parts[0].Equals("release.manifest.sig", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Reserved release path.");
                for (int count = 1; count < parts.Length; count++)
                    if (names.Contains(string.Join("/", parts.Take(count)))) throw new InvalidDataException("Release file/directory collision.");
            }
        }
        private static bool Reserved(string segment)
        {
            string name = segment.Split('.')[0].ToUpperInvariant();
            return name == "CON" || name == "PRN" || name == "AUX" || name == "NUL" ||
                name.Length == 4 && (name.StartsWith("COM", StringComparison.Ordinal) || name.StartsWith("LPT", StringComparison.Ordinal)) && name[3] >= '1' && name[3] <= '9';
        }
        internal static bool Matches(string path, ReleaseFile file, CancellationToken token)
        {
            if (!File.Exists(path)) return false;
            try { VerifyFile(path, file, token); return true; }
            catch (InvalidDataException) { return false; }
        }
        internal static void VerifyFile(string path, ReleaseFile file, CancellationToken token)
        {
            NoReparse(path); token.ThrowIfCancellationRequested();
            using (var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (var hash = SHA256.Create())
            {
                if (input.Length != file.Length) throw new InvalidDataException("Release file length mismatch: " + file.Path);
                var buffer = new byte[64 * 1024]; int count;
                while ((count = input.Read(buffer, 0, buffer.Length)) > 0)
                { token.ThrowIfCancellationRequested(); hash.TransformBlock(buffer, 0, count, buffer, 0); }
                hash.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
                string actual = BitConverter.ToString(hash.Hash).Replace("-", "").ToLowerInvariant();
                if (actual != file.Sha256) throw new InvalidDataException("Release file hash mismatch: " + file.Path);
            }
        }
        internal static void WriteDurable(string path, byte[] bytes)
        {
            NoReparse(path);
            using (var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None))
            { stream.Write(bytes, 0, bytes.Length); stream.Flush(true); }
        }
    }
}
