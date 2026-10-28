using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;

namespace Client.Framework.HotUpdate
{
    // Integrity is checked here. Release authenticity and compatibility are F4/F6 responsibilities.
    public sealed class VerifiedAssembly
    {
        private readonly byte[] bytes;
        public string Name { get; }
        public byte[] Bytes => (byte[])bytes.Clone();
        public string Sha256 { get; }
        public IReadOnlyList<string> Dependencies { get; }

        public VerifiedAssembly(string name, byte[] bytes, string sha256, IReadOnlyList<string> dependencies)
        {
            if (string.IsNullOrWhiteSpace(name) || name.Contains("/") || name.Contains("\\") || name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("An assembly simple name is required.", nameof(name));
            if (bytes == null || bytes.Length == 0) throw new ArgumentException("Assembly bytes are required.", nameof(bytes));
            this.bytes = (byte[])bytes.Clone();
            string actual = Hash(this.bytes);
            if (!string.Equals(actual, sha256, StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Assembly hash mismatch.", nameof(sha256));
            if (dependencies == null || dependencies.Any(string.IsNullOrWhiteSpace) || dependencies.Distinct(StringComparer.Ordinal).Count() != dependencies.Count)
                throw new ArgumentException("Unique dependency names are required.", nameof(dependencies));
            Name = name;
            Sha256 = actual;
            Dependencies = Array.AsReadOnly(dependencies.ToArray());
        }

        private static string Hash(byte[] value)
        {
            using (var hash = SHA256.Create())
                return BitConverter.ToString(hash.ComputeHash(value)).Replace("-", "").ToLowerInvariant();
        }
    }
}
