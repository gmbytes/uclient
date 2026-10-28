using System;
using System.Threading;
using System.Threading.Tasks;

namespace Client.Contracts
{
    public interface ISessionClient
    {
        // One active login; credential or fast-login token creates a new logical session.
        // Runtime implementations deliver StateChanged on their captured owning context.
        Task<LoginResult> LoginAsync(LoginInput input, CancellationToken token);
        // Explicitly ends the current connection; no automatic recovery or replay.
        void Disconnect();
        event Action<SessionState> StateChanged;
    }
}
