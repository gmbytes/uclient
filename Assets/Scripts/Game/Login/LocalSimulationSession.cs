using System;
using System.Threading;
using System.Threading.Tasks;
using Client.Contracts;

namespace Client.Game.Login
{
    // This local interaction demo never creates a server identity or authentication token.
    public sealed class LocalSimulationSession : ISessionClient,IDisposable
    {
        long epoch;bool busy,disposed;
        public event Action<SessionState> StateChanged;
        public async Task<LoginResult> LoginAsync(LoginInput input,CancellationToken token)
        {
            if(disposed)return Failure(LoginError.Disconnected);
            if(busy)return Failure(LoginError.Busy);
            if(input==null)return Failure(LoginError.InvalidInput);
            if(token.IsCancellationRequested)return Failure(LoginError.Cancelled);
            long attempt=++epoch;busy=true;StateChanged?.Invoke(SessionState.Connecting);
            await Task.Yield();
            if(attempt!=epoch)return Failure(LoginError.Cancelled);
            if(token.IsCancellationRequested){busy=false;StateChanged?.Invoke(SessionState.Idle);return Failure(LoginError.Cancelled);}
            busy=false;StateChanged?.Invoke(SessionState.Idle);
            return new LoginResult(true,LoginError.None,0,null,"本地模拟，未进行服务器认证");
        }
        public void Disconnect(){epoch++;busy=false;if(!disposed)StateChanged?.Invoke(SessionState.Disconnected);}
        public void Dispose(){if(disposed)return;Disconnect();disposed=true;StateChanged=null;}
        static LoginResult Failure(LoginError error)=>new LoginResult(false,error,0,null,"本地模拟");
    }
}
