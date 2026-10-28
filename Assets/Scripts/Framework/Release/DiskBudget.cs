using System;
using System.IO;
using System.Runtime.InteropServices;

namespace Client.Framework.Release
{
    public sealed class NoSpaceException : Exception { public NoSpaceException() : base("Insufficient space to prepare a complete release.") { } }
    public sealed class DiskBudget
    {
        public const long MarginBytes = 64L * 1024 * 1024;
        private readonly Func<string, long> availableBytes;
        public DiskBudget(Func<string, long> availableBytes = null)
        { this.availableBytes = availableBytes ?? QueryAvailableBytes; }
        public void Require(string root, VerifiedRelease release)
        {
            // Full materialization plus all potential temporaries (UGF resume or Android platform stream),
            // signed evidence and safety reserve. Reserving all temporary file lengths bounds both concurrent streams.
            long required = MarginBytes;
            try
            {
                checked
                {
                    required += release.ManifestBytes.LongLength + release.SignatureBytes.LongLength;
                    foreach (ReleaseFile file in release.Files) required += file.Length * 2;
                }
            }
            catch (OverflowException) { throw new NoSpaceException(); }
            if (availableBytes(ExistingTargetAncestor(root)) < required) throw new NoSpaceException();
        }
        private static string ExistingTargetAncestor(string root)
        {
            string path = Path.GetFullPath(root);
            while (!Directory.Exists(path))
            {
                DirectoryInfo parent = Directory.GetParent(path);
                if (parent == null) throw new DirectoryNotFoundException("No existing ancestor for release storage.");
                path = parent.FullName;
            }
            return path;
        }
        private static long QueryAvailableBytes(string existingTargetPath)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            // Android persistent storage can live on a mount other than '/'. StatFs must receive that path.
            using (var stats = new UnityEngine.AndroidJavaObject("android.os.StatFs", existingTargetPath))
                return stats.Call<long>("getAvailableBytes");
#elif UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
            // The full directory also handles Windows volumes mounted below a drive root.
            if (!GetDiskFreeSpaceEx(existingTargetPath, out ulong available, out _, out _))
                throw new IOException("Cannot query release storage capacity.", new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error()));
            return available > long.MaxValue ? long.MaxValue : (long)available;
#else
            throw new PlatformNotSupportedException("Release storage capacity currently supports Android and Windows.");
#endif
        }
#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
        [DllImport("kernel32.dll", EntryPoint = "GetDiskFreeSpaceExW", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetDiskFreeSpaceEx(string directory, out ulong available, out ulong total, out ulong free);
#endif
    }
}
