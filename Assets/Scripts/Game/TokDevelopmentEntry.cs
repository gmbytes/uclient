using System.Threading;
using System.Threading.Tasks;
using Client.Contracts;

namespace Client.Game
{
    // Selected through the host's existing entry type name; shares its N3 session.
    public sealed class TokDevelopmentEntry : IHotfixEntry
    {
        readonly Entry entry=new Entry(Entry.TokDevelopmentProfilePath);
        public Task InitializeAsync(IClientContext context,CancellationToken token)=>entry.InitializeAsync(context,token);
        public Task ShutdownAsync(CancellationToken token)=>entry.ShutdownAsync(token);
    }
}
