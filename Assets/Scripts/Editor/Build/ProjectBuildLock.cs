using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor.Build;

namespace Client.Editor.Build
{
    public sealed class ProjectBuildLock : IDisposable
    {
        private static readonly HashSet<string> Owners = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly string project;
        private FileStream handle;
        private ProjectBuildLock(string project, FileStream handle) { this.project = project; this.handle = handle; }

        public static ProjectBuildLock Acquire(string projectRoot)
        {
            string project = Path.GetFullPath(projectRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            SourceFileTransaction.RejectReparsePoints(project);
            lock (Owners)
            {
                if (!Owners.Add(project)) throw new BuildFailedException("A build already owns this project; reentrant builds are rejected.");
                try
                {
                    string path = Path.Combine(project, "Library", "ClientBuild.lock");
                    SourceFileTransaction.RejectReparsePoints(path);
                    Directory.CreateDirectory(Path.GetDirectoryName(path));
                    return new ProjectBuildLock(project, new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None));
                }
                catch (Exception error) { Owners.Remove(project); throw new BuildFailedException("Cannot acquire the project build lock: " + error.Message); }
            }
        }

        public void Dispose()
        {
            lock (Owners)
            {
                if (handle == null) return;
                handle.Dispose(); handle = null; Owners.Remove(project);
                // Keep the lock file: unlinking it would allow a second owner to lock a different inode.
            }
        }
    }
}
