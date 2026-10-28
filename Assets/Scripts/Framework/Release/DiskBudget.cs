using System;
using System.IO;

namespace Client.Framework.Release
{
    public sealed class NoSpaceException : Exception { public NoSpaceException() : base("Insufficient space to prepare a complete release.") { } }
    public sealed class DiskBudget
    {
        public const long MarginBytes = 64L * 1024 * 1024;
        private readonly Func<string, long> availableBytes;
        public DiskBudget(Func<string, long> availableBytes = null)
        { this.availableBytes = availableBytes ?? (path => new DriveInfo(Path.GetPathRoot(Path.GetFullPath(path))).AvailableFreeSpace); }
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
            if (availableBytes(root) < required) throw new NoSpaceException();
        }
    }
}
