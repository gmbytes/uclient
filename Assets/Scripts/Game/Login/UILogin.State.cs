using Client.Contracts;

namespace Client.Game.Login
{
    public sealed partial class UILogin
    {
        void OnStateChanged(SessionState state)
        {
            if(view==null)return;
            switch(state)
            {
                case SessionState.Connecting:StatusLabel.text=Prefix("连接中…");break;
                case SessionState.Authenticating:StatusLabel.text=Prefix("验证中…");break;
                case SessionState.Authenticated:authenticated=!simulation;SetBusy(busy);StatusLabel.text=Prefix(simulation?"模拟完成（未进行服务器认证）":"已登录（框架演示）");break;
                case SessionState.Disconnected:
                    disconnectEpoch++;authenticated=false;SetBusy(false);
                    StatusLabel.text=Prefix("连接已断开，请重试");
                    break;
            }
        }
        static string ErrorText(LoginError error)
        {
            switch(error)
            {
                case LoginError.Cancelled:return "已取消";
                case LoginError.Rejected:return "登录被拒绝，请检查测试账号";
                case LoginError.Timeout:return "连接超时，请重试";
                case LoginError.Disconnected:return "连接已断开，请重试";
                case LoginError.InvalidInput:return "请输入有效的数字测试账号";
                case LoginError.ProtocolError:return "协议错误，请重试";
                default:return "连接失败，请重试";
            }
        }
    }
}
