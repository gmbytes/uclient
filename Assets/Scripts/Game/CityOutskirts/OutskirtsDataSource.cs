using System;
using System.Threading;
using System.Threading.Tasks;
using Client.Contracts;
using Client.Framework.Presentation;
using UnityEngine;

namespace Client.Game.CityOutskirts
{
    internal interface IOutskirtsDataSource
    {
        Task<OutskirtsSnapshot> LoadAsync(CancellationToken token);
    }

    [Serializable]
    internal sealed class OutskirtsSnapshot
    {
        public string layoutId;
        public int layoutVersion;
        public int home;
        public OutskirtsCell[] cells;
        public string AreaId { get; internal set; }

        public static OutskirtsSnapshot Parse(string json)
        {
            var snapshot = JsonUtility.FromJson<OutskirtsSnapshot>(json);
            if (snapshot == null || string.IsNullOrEmpty(snapshot.layoutId) || snapshot.layoutVersion < 1 ||
                snapshot.home < 1 || snapshot.home > OutskirtsHex.Count || snapshot.cells?.Length != OutskirtsHex.Count)
                throw new InvalidOperationException("Invalid outskirts layout.");
            for (int i = 0; i < snapshot.cells.Length; i++)
            {
                var cell = snapshot.cells[i];
                if (cell == null || cell.pos != i + 1 || cell.level < 0 || cell.level > 8 ||
                    (int)cell.resource < 0 || (int)cell.resource > 4 ||
                    (cell.kind == 0 && cell.level > 0 && cell.resource == OutskirtsResourceKind.None) ||
                    (cell.kind != 0 && cell.resource != OutskirtsResourceKind.None) ||
                    (cell.kind != 0 && cell.kind != 5 && cell.kind != 15 && cell.kind != 16))
                    throw new InvalidOperationException("Invalid outskirts cell.");
            }
            if (snapshot.cells[snapshot.home - 1].kind != 5)
                throw new InvalidOperationException("Invalid outskirts home.");
            return snapshot;
        }
    }

    [Serializable]
    internal sealed class OutskirtsCell
    {
        public int pos;
        public int kind;
        public int level;
        public OutskirtsResourceKind resource;
    }

    internal sealed class LocalOutskirtsDataSource : IOutskirtsDataSource
    {
        public const string LayoutPath = "Assets/Content/Maps/CityOutskirts/Beginner.json";
        private readonly IResourceService resources;

        public LocalOutskirtsDataSource(IResourceService resources) => this.resources = resources;

        public async Task<OutskirtsSnapshot> LoadAsync(CancellationToken token)
        {
            using (var handle = await resources.LoadAsync<TextAsset>(LayoutPath, token))
            {
                token.ThrowIfCancellationRequested();
                var snapshot = OutskirtsSnapshot.Parse(handle.Asset.text);
                snapshot.AreaId = "local-beginner";
                return snapshot;
            }
        }
    }
}
