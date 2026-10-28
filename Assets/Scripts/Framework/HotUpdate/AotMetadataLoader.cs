using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Client.Framework.Release;
using HybridCLR;
using UnityEngine;

namespace Client.Framework.HotUpdate
{
    public sealed class ArchivedMetadataSet
    {
        private readonly KeyValuePair<string, byte[]>[] files;
        public PlayerIdentity Player { get; }
        public IReadOnlyList<string> Names { get; }
        private ArchivedMetadataSet(PlayerIdentity player, KeyValuePair<string, byte[]>[] files)
        { Player = player; this.files = files; Names = Array.AsReadOnly(files.Select(p => Path.GetFileNameWithoutExtension(p.Key)).ToArray()); }
        internal IEnumerable<KeyValuePair<string, byte[]>> Snapshot() => files.Select(p => new KeyValuePair<string, byte[]>(p.Key, (byte[])p.Value.Clone()));
        internal static ArchivedMetadataSet FromPrepared(PreparedRelease prepared, PlayerIdentity player)
        {
            ReleaseCompatibility.RequireExact(prepared.Release.Player, player);
            var files = prepared.Release.Files.Where(f => f.Kind == ReleaseFileKind.AotMetadata).Select(f =>
                new KeyValuePair<string, byte[]>(Path.GetFileName(f.Path), HotfixLoadPlan.ReadChecked(prepared.Root, f))).OrderBy(p => p.Key, StringComparer.Ordinal).ToArray();
            if (HotfixLoadPlan.MetadataSetId(files) != player.AotMetadataSetId) throw new InvalidDataException("Metadata differs from the immutable Player archive.");
            if (files.Any(p => p.Key.StartsWith("Client.Game", StringComparison.Ordinal))) throw new InvalidDataException("Hotfix code cannot be AOT metadata.");
            return new ArchivedMetadataSet(player, files);
        }
    }
    public sealed class AotMetadataLoader
    {
        public void Load(ArchivedMetadataSet set)
        {
            if (set == null) throw new ArgumentNullException(nameof(set));
            string platform = Application.platform == RuntimePlatform.Android ? "Android" : Application.platform == RuntimePlatform.WindowsPlayer ? "StandaloneWindows64" : null;
            if (platform == null || platform != set.Player.Platform) throw new InvalidOperationException("Metadata platform must match the running Player.");
            foreach (var file in set.Snapshot())
            {
                LoadImageErrorCode code = RuntimeApi.LoadMetadataForAOTAssembly(file.Value, HomologousImageMode.SuperSet);
                Debug.Log("F6_METADATA " + set.Player.PlayerBuildId + " " + file.Key + " " + code);
                if (code != LoadImageErrorCode.OK) throw new InvalidOperationException("AOT metadata load failed: " + file.Key + " " + code);
            }
        }
    }
}
