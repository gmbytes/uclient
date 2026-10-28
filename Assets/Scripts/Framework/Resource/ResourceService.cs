using System;
using System.Threading.Tasks;
using GameFramework.Resource;
using UnityEngine;
using UnityGameFramework.Runtime;

namespace Client.Framework.Resource
{
    // F2's narrow consumer boundary. F3 will introduce the shared resource contract.
    public sealed class ResourceService
    {
        private readonly ResourceComponent resource;
        public ResourceService(ResourceComponent resource) => this.resource = resource != null ? resource : throw new ArgumentNullException(nameof(resource));

        public Task<string> LoadTextAsync(string assetName)
        {
            if (string.IsNullOrWhiteSpace(assetName)) throw new ArgumentException("Asset name is required.", nameof(assetName));
            var completion = new TaskCompletionSource<string>();
            resource.LoadAsset(assetName, typeof(TextAsset), new LoadAssetCallbacks(
                (name, asset, duration, data) =>
                {
                    try
                    {
                        string text;
                        try { text = ((TextAsset)asset).text; }
                        finally { resource.UnloadAsset(asset); }
                        completion.TrySetResult(text);
                    }
                    catch (Exception exception) { completion.TrySetException(exception); }
                },
                (name, status, message, data) => completion.TrySetException(new InvalidOperationException("Cannot load text asset " + name + ": " + status + " " + message))));
            return completion.Task;
        }
    }
}
