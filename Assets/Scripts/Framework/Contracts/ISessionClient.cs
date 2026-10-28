using System;
using System.Threading;
using System.Threading.Tasks;

namespace Client.Contracts
{
    public interface ISessionClient
    {
        Task<LoginResult> LoginAsync(LoginInput input, CancellationToken token);
        void Disconnect();
        event Action<SessionState> StateChanged;
    }
}
