using System;
using System.Threading;
using System.Threading.Tasks;

namespace Client.Contracts
{
    public interface IResourceService
    {
        Task<IAssetHandle<T>> LoadAsync<T>(string assetName, CancellationToken token) where T : UnityEngine.Object;
    }

    public interface IAssetHandle<out T> : IDisposable where T : UnityEngine.Object
    {
        T Asset { get; }
    }
}
