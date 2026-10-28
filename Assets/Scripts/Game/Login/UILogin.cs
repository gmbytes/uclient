using System;
using System.Threading;
using Client.Contracts;
using Client.Framework.UI;

namespace Client.Game.Login
{
    public sealed partial class UILogin
    {
        IViewHandle view;
        UIViewHandle nativeView;
        ISessionClient session;
        CancellationTokenSource request;
        long pageEpoch, requestEpoch;
        bool busy, simulation, authenticated;
        string presentationLabel, version="Framework demo v1";

        public void ConfigurePresentation(string label,string versionLabel)
        {
            if(view!=null)throw new InvalidOperationException("Configure presentation before binding.");
            presentationLabel=label;version=versionLabel??"Framework demo v1";
        }

        public void Bind(IViewHandle view,ISessionClient session)
        {
            if(view==null)throw new ArgumentNullException(nameof(view));
            if(session==null)throw new ArgumentNullException(nameof(session));
            Unbind();
            var root=view.Root;
            var resolvedView=view as UIViewHandle;
            if(resolvedView==null)throw new InvalidOperationException("UILogin requires a native UIService view handle.");
            BindComponents(root.GetComponent<UIBindings>());
            if(Title==null || AccountInput==null || LoginButton==null || CancelButton==null || StatusLabel==null || VersionLabel==null)
            {ClearBindings();throw new InvalidOperationException("UILogin requires all six explicit bindings.");}
            this.view=view;this.session=session;nativeView=resolvedView;simulation=session is LocalSimulationSession;authenticated=false;pageEpoch++;
            nativeView.Closed+=Unbind;session.StateChanged+=OnStateChanged;
            LoginButton.onClick.AddListener(Login);CancelButton.onClick.AddListener(Cancel);
            Title.text="框架登录演示";
            VersionLabel.text=version+" · "+(simulation?"本地模拟":presentationLabel??"Tok 开发登录");
            StatusLabel.text=simulation?"本地模拟 · 请输入测试账号":"请输入测试账号";
            SetBusy(false);
        }

        public void Unbind()
        {
            pageEpoch++;requestEpoch++;
            var pending=request;request=null;
            if(nativeView!=null)nativeView.Closed-=Unbind;
            if(session!=null)session.StateChanged-=OnStateChanged;
            if(LoginButton!=null)LoginButton.onClick.RemoveListener(Login);
            if(CancelButton!=null)CancelButton.onClick.RemoveListener(Cancel);
            view=null;nativeView=null;
            var previous=session;session=null;busy=false;authenticated=false;
            pending?.Cancel();previous?.Disconnect();
            ClearBindings();
        }

        async void Login()
        {
            if(view==null || busy || authenticated)return;
            LoginInput input;
            try{input=DevelopmentCredentialProvider.Create(AccountInput.text);}
            catch(ArgumentException){StatusLabel.text=Prefix("请输入有效的数字测试账号");return;}
            long page=pageEpoch,attempt=++requestEpoch;
            var source=new CancellationTokenSource();request=source;SetBusy(true);
            StatusLabel.text=Prefix("连接中…");
            try
            {
                var result=await session.LoginAsync(input,source.Token);
                if(!IsCurrent(page,attempt))return;
                authenticated=result.Succeeded && !simulation;
                StatusLabel.text=Prefix(result.Succeeded?(simulation?"模拟完成（未进行服务器认证）":"已登录（框架演示）"):ErrorText(result.Error));
            }
            catch(OperationCanceledException){if(IsCurrent(page,attempt))StatusLabel.text=Prefix("已取消");}
            catch(Exception){if(IsCurrent(page,attempt))StatusLabel.text=Prefix("连接失败，请重试");}
            finally
            {
                if(IsCurrent(page,attempt)){request=null;SetBusy(false);}
                source.Dispose();
            }
        }

        void Cancel()
        {
            if(view==null || !busy)return;
            requestEpoch++;
            var source=request;request=null;source?.Cancel();session.Disconnect();
            SetBusy(false);StatusLabel.text=Prefix("已取消");
        }
        bool IsCurrent(long page,long attempt)=>view!=null && pageEpoch==page && requestEpoch==attempt;
        string Prefix(string text)=>simulation?"本地模拟 · "+text:text;
        void SetBusy(bool value)
        {
            busy=value;LoginButton.interactable=!value&&!authenticated;CancelButton.interactable=value;
            // Keep the same input alive and selected during rotation and an in-flight request.
        }
    }
}
