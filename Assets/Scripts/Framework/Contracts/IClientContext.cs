using System.Threading;

namespace Client.Contracts
{
    public interface IClientContext
    {
        IResourceService Resources { get; }
        IUIService UI { get; }
        ISessionClient Session { get; }
        ILogService Log { get; }
        CancellationToken Lifetime { get; }
        void ReportReady();
    }
}
