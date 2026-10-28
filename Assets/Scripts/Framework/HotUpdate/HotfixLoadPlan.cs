using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Client.Framework.Release;

namespace Client.Framework.HotUpdate
{
    public sealed class HotfixLoadPlan
    {
        public const string NetstandardFacadePath = "References/netstandard.dll";
        public const string NetstandardFacadeSha256 = "c2abb88d6218d5835be5aa4978f95869e62c188285f2db8dc30e6102b1a45f8d";
        public PlayerIdentity Player { get; }
        public ArchivedMetadataSet Metadata { get; }
        public IReadOnlyList<VerifiedAssembly> Assemblies { get; }
        public IReadOnlyList<string> ReferenceFacades { get; }
        private HotfixLoadPlan(PlayerIdentity player, ArchivedMetadataSet metadata, List<VerifiedAssembly> assemblies, string[] facades)
        { Player = player; Metadata = metadata; Assemblies = assemblies.AsReadOnly(); ReferenceFacades = Array.AsReadOnly(facades); }
        public static HotfixLoadPlan Create(PreparedRelease prepared, PlayerIdentity player)
        {
            if (prepared == null) throw new ArgumentNullException(nameof(prepared));
            var release = prepared.Release;
            ReleaseCompatibility.RequireExact(release.Player, player);
            string[] reserved = { "Client.Game.Protocol", "Client.Game" };
            if (release.Assemblies.Count != 2 || !new HashSet<string>(release.Assemblies.Select(a => a.Name), StringComparer.Ordinal).SetEquals(reserved) ||
                !release.AssemblyOrder.SequenceEqual(reserved)) throw new InvalidDataException("The complete reserved Protocol/Game assembly set and order are required.");
            var metadata = ArchivedMetadataSet.FromPrepared(prepared, player);
            var available = new HashSet<string>(AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetName().Name), StringComparer.Ordinal);
            available.UnionWith(metadata.Names);
            // HybridCLR 9 Image.cpp explicitly forwards netstandard types into loaded BCL images.
            // Bind that native behavior to a signed, native-pinned pure forwarding facade, never AOT metadata.
            available.Remove("netstandard");
            string[] facades = Array.Empty<string>();
            if (release.Assemblies.Any(a => a.Dependencies.Contains("netstandard")))
            {
                var facade = release.Files.SingleOrDefault(f => f.Path == NetstandardFacadePath && f.Kind == ReleaseFileKind.Configuration);
                if (facade == null || facade.Sha256 != NetstandardFacadeSha256 || Hash(ReadChecked(prepared.Root, facade)) != NetstandardFacadeSha256)
                    throw new InvalidDataException("The native netstandard forwarding contract must be explicitly bound to this release.");
                facades = new[] { "netstandard" }; available.Add("netstandard");
            }
            // Even in the Editor, a loaded hot module cannot satisfy a forward/cyclic hot dependency.
            available.ExceptWith(reserved);
            var assemblies = new List<VerifiedAssembly>();
            foreach (string name in release.AssemblyOrder)
            {
                var assembly = release.Assemblies.Single(a => a.Name == name);
                if (assembly.Dependencies.Any(d => !available.Contains(d))) throw new InvalidDataException("Missing, cyclic or out-of-order assembly dependency: " + name);
                var file = release.Files.Single(f => f.Path == assembly.Path && f.Kind == ReleaseFileKind.HotfixDll);
                byte[] bytes = ReadChecked(prepared.Root, file);
                var actual = ManagedAssemblyIdentity.Read(bytes);
                if (actual.Name != name || !new HashSet<string>(actual.References, StringComparer.Ordinal).SetEquals(assembly.Dependencies))
                    throw new InvalidDataException("Actual DLL identity/references differ from the signed descriptor: " + name);
                assemblies.Add(new VerifiedAssembly(name, bytes, file.Sha256, assembly.Dependencies));
                available.Add(name);
            }
            return new HotfixLoadPlan(player, metadata, assemblies, facades);
        }
        internal static byte[] ReadChecked(string root, ReleaseFile file)
        {
            string path = ReleaseFileSystem.Child(ReleaseFileSystem.Root(root), file.Path);
            if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("Release files cannot be symbolic links.");
            long length = new FileInfo(path).Length;
            if (length != file.Length || length > int.MaxValue) throw new InvalidDataException("Release file length changed: " + file.Path);
            byte[] bytes = File.ReadAllBytes(path);
            if (bytes.LongLength != file.Length || Hash(bytes) != file.Sha256) throw new InvalidDataException("Release file hash changed: " + file.Path);
            return bytes;
        }
        public static string Hash(byte[] bytes)
        { using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant(); }
        public static string MetadataSetId(IEnumerable<KeyValuePair<string, byte[]>> files)
        {
            var sorted = files.OrderBy(p => p.Key, StringComparer.Ordinal).ToArray();
            if (sorted.Length == 0 || sorted.Any(p => Path.GetFileName(p.Key) != p.Key || !p.Key.EndsWith(".dll", StringComparison.Ordinal) || p.Value == null) ||
                sorted.Select(p => p.Key).Distinct(StringComparer.OrdinalIgnoreCase).Count() != sorted.Length) throw new InvalidDataException("Complete flat AOT DLL inventory required.");
            return Hash(Encoding.UTF8.GetBytes(string.Concat(sorted.Select(p => p.Key + "\t" + p.Value.LongLength + "\t" + Hash(p.Value) + "\n"))));
        }
    }
}
