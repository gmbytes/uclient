using System;
using System.Threading;
using Client.Contracts;
using Client.Framework.UI;
using UnityEngine;
using UnityEngine.UI;

namespace Client.Game.Login
{
    public sealed partial class UILogin
    {
        IViewHandle view;
        UIViewHandle nativeView;
        ISessionClient session;
        CancellationTokenSource request;
        long pageEpoch, requestEpoch, disconnectEpoch;
        bool busy, simulation, authenticated, accountLoggedIn;
        string presentationLabel, version="Framework demo v1";
        Toggle agreementToggle;
        InputField passwordInput;
        RectTransform accountRow, passwordRow;
        Button serverButton;
        Text loginLabel;
        public bool IsAccountLoggedIn=>accountLoggedIn;
        public event Action EnterGameRequested;

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
            var table=root.GetComponent<UIBindings>();
            BindComponents(table);
            if(Title==null || AccountInput==null || LoginButton==null || CancelButton==null || StatusLabel==null || VersionLabel==null)
            {ClearBindings();throw new InvalidOperationException("UILogin requires all six explicit bindings.");}
            this.view=view;this.session=session;nativeView=resolvedView;simulation=session is LocalSimulationSession;authenticated=false;accountLoggedIn=false;pageEpoch++;
            nativeView.Closed+=Unbind;session.StateChanged+=OnStateChanged;
            LoginButton.onClick.AddListener(Login);CancelButton.onClick.AddListener(Cancel);
            agreementToggle=Optional<Toggle>(table,"AgreementToggle");
            var serverLabel=Optional<Text>(table,"ServerLabel");
            accountRow=Optional<RectTransform>(table,"AccountRow");
            passwordRow=Optional<RectTransform>(table,"PasswordRow");
            passwordInput=Optional<InputField>(table,"PasswordInput");
            serverButton=Optional<Button>(table,"ServerButton");
            loginLabel=LoginButton.GetComponentInChildren<Text>(true);
            if(passwordInput!=null){passwordInput.contentType=InputField.ContentType.Password;passwordInput.text="";}
            if(serverLabel!=null)serverLabel.text=simulation?"本地体验  ·  默认区服":"默认区服";
            if(agreementToggle!=null)agreementToggle.onValueChanged.AddListener(AgreementChanged);
            Title.text="";
            VersionLabel.text=version+" · "+(simulation?"本地模拟":presentationLabel??"Tok 开发登录");
            StatusLabel.text=Prefix("开发演示，无需真实密码 · 请输入数字测试账号");
            SetAccountStage(false);
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
            if(agreementToggle!=null)agreementToggle.onValueChanged.RemoveListener(AgreementChanged);
            agreementToggle=null;
            if(passwordInput!=null)passwordInput.text="";
            passwordInput=null;accountRow=null;passwordRow=null;serverButton=null;loginLabel=null;
            view=null;nativeView=null;
            var previous=session;session=null;busy=false;authenticated=false;accountLoggedIn=false;
            pending?.Cancel();previous?.Disconnect();
            ClearBindings();
        }

        async void Login()
        {
            if(view==null || busy)return;
            if(accountLoggedIn)
            {
                if(simulation || authenticated)EnterGameRequested?.Invoke();
                return;
            }
            if(authenticated)return;
            if(agreementToggle!=null && !agreementToggle.isOn)
            {StatusLabel.text=Prefix("请先阅读并同意用户协议与隐私政策");return;}
            LoginInput input;
            // Password is a presentation-only field in this development flow. Never read or log it.
            try{input=DevelopmentCredentialProvider.Create(AccountInput.text);}
            catch(ArgumentException){StatusLabel.text=Prefix("请输入有效的数字测试账号");return;}
            long page=pageEpoch,attempt=++requestEpoch,disconnected=disconnectEpoch;
            var source=new CancellationTokenSource();request=source;SetBusy(true);
            StatusLabel.text=Prefix("连接中…");
            try
            {
                var result=await session.LoginAsync(input,source.Token);
                if(!IsCurrent(page,attempt))return;
                // N3 completes asynchronously: a newer disconnect can precede this continuation.
                if(result.Succeeded && disconnectEpoch!=disconnected)return;
                authenticated=result.Succeeded && !simulation;
                StatusLabel.text=Prefix(result.Succeeded?(simulation?"模拟完成（未进行服务器认证）":"已登录（框架演示）"):ErrorText(result.Error));
                if(result.Succeeded)SetAccountStage(true);
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
        void SetAccountStage(bool loggedIn)
        {
            accountLoggedIn=loggedIn;
            if(accountRow!=null)accountRow.gameObject.SetActive(!loggedIn);
            if(passwordRow!=null)passwordRow.gameObject.SetActive(!loggedIn);
            if(serverButton!=null)serverButton.gameObject.SetActive(loggedIn);
            if(agreementToggle!=null)agreementToggle.gameObject.SetActive(!loggedIn);
            if(loginLabel!=null)loginLabel.text=loggedIn?"进 入 游 戏":"登 录";
            if(loggedIn)AccountInput.DeactivateInputField();
            if(passwordInput!=null)passwordInput.text="";
        }
        void SetBusy(bool value)
        {
            busy=value;LoginButton.interactable=!value&&(!authenticated||EnterGameRequested!=null)&&(accountLoggedIn||agreementToggle==null||agreementToggle.isOn);CancelButton.interactable=value;
            CancelButton.gameObject.SetActive(value);
            // Keep the same input alive and selected during rotation and an in-flight request.
        }
        void AgreementChanged(bool accepted)
        {
            if(view==null)return;
            SetBusy(busy);
            if(!busy&&!accountLoggedIn&&!authenticated)StatusLabel.text=Prefix(accepted?"开发演示，无需真实密码 · 请输入数字测试账号":"请先阅读并同意用户协议与隐私政策");
        }
        static T Optional<T>(UIBindings table,string id) where T:Component
        {
            foreach(var binding in table.Singles)if(binding!=null&&binding.Id==id)return binding.Value as T;
            return null;
        }
    }
}
