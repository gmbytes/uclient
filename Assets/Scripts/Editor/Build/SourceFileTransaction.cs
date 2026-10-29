using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Client.Editor.Build
{
    // Every path is an explicit mutation allowance. Never recursively remove a directory during restoration.
    public sealed class SourceFileTransaction : IDisposable
    {
        private sealed class Snapshot { public string Path, DirectoryOwner; public byte[] Bytes; public FileAttributes Attributes; }
        private readonly List<Snapshot> files = new List<Snapshot>();
        private readonly HashSet<string> missingDirectories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private bool disposed;

        private SourceFileTransaction(IEnumerable<string> paths)
        {
            var selected = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string raw in paths ?? throw new ArgumentNullException(nameof(paths)))
            {
                string path = System.IO.Path.GetFullPath(raw);
                Add(path, selected); Add(path + ".meta", selected);
                for (string parent = System.IO.Path.GetDirectoryName(path); parent != null && !Directory.Exists(parent); parent = System.IO.Path.GetDirectoryName(parent))
                { missingDirectories.Add(parent); Add(parent + ".meta", selected).DirectoryOwner = parent; }
            }
        }

        public static SourceFileTransaction Capture(IEnumerable<string> paths) => new SourceFileTransaction(paths);
        private Snapshot Add(string path, HashSet<string> selected)
        {
            if (!selected.Add(path)) return files.Single(f => string.Equals(f.Path, path, StringComparison.OrdinalIgnoreCase));
            RejectReparsePoints(path);
            if (Directory.Exists(path)) throw new IOException("A source snapshot must name files: " + path);
            var snapshot = new Snapshot { Path = path, Bytes = File.Exists(path) ? File.ReadAllBytes(path) : null, Attributes = File.Exists(path) ? File.GetAttributes(path) : FileAttributes.Normal };
            files.Add(snapshot);
            return snapshot;
        }

        internal static void RejectReparsePoints(string path)
        {
            for (string current = System.IO.Path.GetFullPath(path); current != null; current = System.IO.Path.GetDirectoryName(current))
                if ((File.Exists(current) || Directory.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                    throw new IOException("Build mutations cannot traverse symbolic links or junctions: " + current);
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            var failures = new List<Exception>();
            foreach (var file in files.Where(f => f.DirectoryOwner == null))
            {
                try { Restore(file); }
                catch (Exception error) { failures.Add(error); }
            }
            foreach (string directory in missingDirectories.OrderByDescending(p => p.Length))
            {
                bool removedOrAbsent = false;
                try
                {
                    RejectReparsePoints(directory);
                    if (Directory.Exists(directory) && !Directory.EnumerateFileSystemEntries(directory).Any()) Directory.Delete(directory);
                    removedOrAbsent = !Directory.Exists(directory);
                }
                catch (Exception error) { failures.Add(error); }
                var meta = files.Single(f => string.Equals(f.DirectoryOwner, directory, StringComparison.OrdinalIgnoreCase));
                // A retained folder owns its newly created GUID. Preexisting orphan metas always restore exactly.
                try { if (meta.Bytes != null || removedOrAbsent) Restore(meta); }
                catch (Exception error) { failures.Add(error); }
            }
            if (failures.Count != 0) throw new AggregateException("Source restoration failed; the build must not succeed.", failures);
        }

        private static void Restore(Snapshot file)
        {
            RejectReparsePoints(file.Path);
            if (File.Exists(file.Path)) File.SetAttributes(file.Path, FileAttributes.Normal);
            if (file.Bytes == null) { if (File.Exists(file.Path)) File.Delete(file.Path); }
            else { Directory.CreateDirectory(System.IO.Path.GetDirectoryName(file.Path)); File.WriteAllBytes(file.Path, file.Bytes); File.SetAttributes(file.Path, file.Attributes); }
        }
    }
}
