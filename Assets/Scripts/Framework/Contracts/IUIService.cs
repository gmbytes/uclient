using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace Client.Contracts
{
    public interface IUIService
    {
        Task<IViewHandle> OpenAsync(string viewId, CancellationToken token);
    }

    public interface IViewHandle
    {
        GameObject Root { get; }
        void Close();
    }
}
