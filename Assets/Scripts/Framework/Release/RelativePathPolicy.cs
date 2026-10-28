using System;
using System.Linq;

namespace Client.Framework.Release
{
    public static class RelativePathPolicy
    {
        public static void RequireCanonical(string path)
        {
            if (string.IsNullOrEmpty(path) || path.Length > 1024 || path.StartsWith("/", StringComparison.Ordinal) ||
                path.Any(c => c < 32 || c > 126 || "\\:%?#<>\"|*".IndexOf(c) >= 0)) Fail();
            foreach (string segment in path.Split('/'))
            {
                if (segment.Length == 0 || segment == "." || segment == ".." ||
                    segment.EndsWith(".", StringComparison.Ordinal) || segment.EndsWith(" ", StringComparison.Ordinal)) Fail();
            }
        }

        internal static void RequireIdentifier(string value)
        {
            if (string.IsNullOrEmpty(value) || value.Length > 128 || value == "." || value == ".." ||
                value.Any(c => !(c >= 'a' && c <= 'z' || c >= 'A' && c <= 'Z' || c >= '0' && c <= '9' || c == '_' || c == '-' || c == '.')))
                throw new ReleaseVerificationException(ReleaseErrorCode.InvalidManifest);
        }

        private static void Fail() => throw new ReleaseVerificationException(ReleaseErrorCode.UnsafePath);
    }
}
