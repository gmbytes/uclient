using System.Threading;
using System.Threading.Tasks;
using Client.Contracts;

namespace Client.Game
{
    public sealed class Entry : IHotfixEntry
    {
        public Task InitializeAsync(IClientContext context, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            context.ReportReady();
            return Task.CompletedTask;
        }

        public Task ShutdownAsync(CancellationToken token) => Task.CompletedTask;
    }
}
