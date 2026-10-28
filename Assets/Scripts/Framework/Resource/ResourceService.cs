using System;
using System.Threading;
using System.Threading.Tasks;
using Client.Contracts;
using GameFramework.Resource;
using UnityEngine;
using UnityGameFramework.Runtime;

namespace Client.Framework.Resource
{
    public sealed class ResourceService : IResourceService
    {
        private readonly ResourceComponent resource;
        public ResourceService(ResourceComponent resource) => this.resource = resource != null ? resource : throw new ArgumentNullException(nameof(resource));

        public Task<IAssetHandle<T>> LoadAsync<T>(string assetName, CancellationToken token) where T : UnityEngine.Object
        {
            if (string.IsNullOrWhiteSpace(assetName)) throw new ArgumentException("Asset name is required.", nameof(assetName));
            token.ThrowIfCancellationRequested();
            var completion = new TaskCompletionSource<IAssetHandle<T>>();
            var registration = token.Register(() => completion.TrySetCanceled(token));
            try { resource.LoadAsset(assetName, typeof(T), new LoadAssetCallbacks(
                (name, asset, duration, data) =>
                {
                    registration.Dispose();
                    try
                    {
                        if (!(asset is T typed)) { resource.UnloadAsset(asset); throw new InvalidOperationException("Native resource returned the wrong asset type."); }
                        var handle = new AssetHandle<T>(resource, typed);
                        if (!completion.TrySetResult(handle)) handle.Dispose();
                    }
                    catch (Exception exception) { completion.TrySetException(exception); }
                },
                (name, status, message, data) =>
                {
                    registration.Dispose();
                    completion.TrySetException(new InvalidOperationException("Cannot load asset " + name + ": " + status + " " + message));
                })); }
            catch { registration.Dispose(); throw; }
            return completion.Task;
        }

        public async Task<string> LoadTextAsync(string assetName)
        {
            using (var handle = await LoadAsync<TextAsset>(assetName, CancellationToken.None)) return handle.Asset.text;
        }

        private sealed class AssetHandle<T> : IAssetHandle<T> where T : UnityEngine.Object
        {
            private readonly ResourceComponent resource;
            private T asset;
            public AssetHandle(ResourceComponent resource, T asset) { this.resource = resource; this.asset = asset; }
            public T Asset => asset != null ? asset : throw new ObjectDisposedException(nameof(AssetHandle<T>));
            public void Dispose()
            {
                var value = asset;
                if (value == null) return;
                asset = null;
                resource.UnloadAsset(value);
            }
        }
    }
}
