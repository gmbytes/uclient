using System.Threading;
using System.Threading.Tasks;

namespace Client.Contracts
{
    public interface IHotfixEntry
    {
        Task InitializeAsync(IClientContext context, CancellationToken token);
        Task ShutdownAsync(CancellationToken token);
    }
}
