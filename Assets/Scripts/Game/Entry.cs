using System;
using System.Threading;
using System.Threading.Tasks;
using Client.Contracts;
using Client.Framework.UI;
using Client.Game.Login;
using Client.Game.MainCity;
using Client.Game.CityOutskirts;

namespace Client.Game
{
    public sealed class Entry : IHotfixEntry
    {
        public const string LocalSimulationProfilePath="Assets/Settings/Environment/LocalSimulationProfile.asset";
        public const string TokDevelopmentProfilePath="Assets/Settings/Environment/DevelopmentProfile.asset";
        readonly string profilePath;
        CancellationTokenSource lifetime;
        CancellationTokenRegistration cancelled;
        SynchronizationContext ownerContext;
        IViewHandle view;
        UILogin presenter;
        LocalSimulationSession localSession;
        bool started;
        IClientContext context;
        IViewHandle cityView;
        UIMainCity cityPresenter;
        ISessionClient activeSession;
        bool cityOpening;
        long navigationEpoch;
        CancellationTokenSource outskirtsLoad;
        IViewHandle outskirtsView;
        UICityOutskirts outskirtsPresenter;

        public Entry():this(LocalSimulationProfilePath) { }
        public Entry(string profilePath)
        {
            if(string.IsNullOrWhiteSpace(profilePath))throw new ArgumentException("A presentation profile path is required.",nameof(profilePath));
            this.profilePath=profilePath;
        }

        public async Task InitializeAsync(IClientContext context, CancellationToken token)
        {
            if(context==null)throw new ArgumentNullException(nameof(context));
            if(started)throw new InvalidOperationException("An entry instance initializes only once.");
            started=true;ownerContext=SynchronizationContext.Current;
            this.context=context;
            context.Log?.Write(ClientLogLevel.Info,"CLIENT_ENTRY_EXECUTING","Client.Game.Entry");
            lifetime=CancellationTokenSource.CreateLinkedTokenSource(context.Lifetime,token);
            var launchToken=lifetime.Token;
            try
            {
                launchToken.ThrowIfCancellationRequested();
                cancelled=launchToken.Register(OnCancelled);
                LoginPresentationMode mode;string label,version;
                using(var profile=await context.Resources.LoadAsync<LoginPresentationProfile>(profilePath,launchToken))
                {
                    launchToken.ThrowIfCancellationRequested();
                    var data=profile.Asset;
                    if(!Enum.IsDefined(typeof(LoginPresentationMode),data.Mode))throw new InvalidOperationException("Unsupported login presentation mode.");
                    mode=data.Mode;label=data.Label;version=data.Version;
                }
                ISessionClient session;
                if(mode==LoginPresentationMode.LocalSimulation)session=localSession=new LocalSimulationSession();
                else session=context.Session??throw new InvalidOperationException("Tok development requires the host session.");
                activeSession=session;
                activeSession.StateChanged+=OnSessionState;
                var opened=await context.UI.OpenAsync("UILogin",launchToken);
                view=opened;
                launchToken.ThrowIfCancellationRequested();
                presenter=new UILogin();presenter.ConfigurePresentation(label,version);presenter.Bind(opened,session);
                presenter.EnterGameRequested+=EnterCity;
                context.Log?.Write(ClientLogLevel.Info,"CLIENT_VIEW",Newtonsoft.Json.JsonConvert.SerializeObject(new {view=opened.Root.GetInstanceID(),presenter=System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(presenter),version,mode=mode.ToString()}));
                await UIFirstFrame.WaitAsync(opened,launchToken);
                launchToken.ThrowIfCancellationRequested();
                ((UIViewHandle)opened).Lifetime.ThrowIfCancellationRequested();
                if(!ReferenceEquals(view,opened))throw new OperationCanceledException(launchToken);
                DeliveryVersion.Run(context.Log);
                // 这里只请求“本地页面可用”，不代表服务器认证成功；宿主仍需等待本入口成功返回才晋升版本。
                context.ReportReady();
            }
            catch
            {
                Cleanup();throw;
            }
        }

        public Task ShutdownAsync(CancellationToken token)
        {Cleanup();return Task.CompletedTask;}

        async void EnterCity()
        {
            if(cityOpening||cityView!=null||lifetime==null||lifetime.IsCancellationRequested)return;
            var cityToken=lifetime.Token;
            long expectedEpoch=navigationEpoch;
            cityOpening=true;IViewHandle opened=null;
            try
            {
                opened=await context.UI.OpenAsync("UIMainCity",cityToken);
                cityToken.ThrowIfCancellationRequested();
                if(expectedEpoch!=navigationEpoch)throw new OperationCanceledException(cityToken);
                var city=new UIMainCity();city.Bind(opened,ReturnToLogin);
                city.OutskirtsRequested += EnterOutskirts;
                cityPresenter=city;cityView=opened;opened=null;
            }
            catch(OperationCanceledException){opened?.Close();}
            catch(Exception error)
            {
                opened?.Close();
                context?.Log?.Write(ClientLogLevel.Error,"CITY_OPEN_FAILED",error.Message);
                if(presenter?.StatusLabel!=null)presenter.StatusLabel.text="主城加载失败，请重试";
            }
            finally{cityOpening=false;}
        }
        async void EnterOutskirts()
        {
            var city = cityPresenter;
            if (city == null || outskirtsLoad != null || outskirtsView != null || lifetime == null) return;
            var load = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
            outskirtsLoad = load;
            var token = load.Token;
            IViewHandle opened = null;
            bool suspended = false;
            try
            {
                IOutskirtsDataSource source = new LocalOutskirtsDataSource(context.Resources);
                var snapshot = await source.LoadAsync(token);
                token.ThrowIfCancellationRequested();
                opened = await context.UI.OpenAsync("UICityOutskirts", token);
                token.ThrowIfCancellationRequested();
                // Keep the previous full-screen page covering login until the new page has rendered.
                await UIFirstFrame.WaitAsync(opened, token);
                token.ThrowIfCancellationRequested();
                if (!ReferenceEquals(city, cityPresenter)) throw new OperationCanceledException(token);
                city.SuspendForOutskirts();
                suspended = true;
                outskirtsPresenter = new UICityOutskirts(opened, snapshot, ReturnFromOutskirts);
                outskirtsView = opened;
                opened = null;
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                opened?.Close();
            }
            catch (Exception error)
            {
                opened?.Close();
                context?.Log?.Write(ClientLogLevel.Error, "OUTSKIRTS_OPEN_FAILED", error.Message);
                if (!token.IsCancellationRequested && ReferenceEquals(city, cityPresenter))
                {
                    if (suspended) city.ResumeFromOutskirts();
                    city.SetOutskirtsRetry(true);
                }
            }
            finally
            {
                outskirtsLoad = null;
                load.Dispose();
            }
        }

        void ReturnFromOutskirts()
        {
            cityPresenter?.ResumeFromOutskirts();
            CloseOutskirts();
        }

        void CloseOutskirts()
        {
            outskirtsLoad?.Cancel();
            outskirtsPresenter?.Dispose();
            outskirtsPresenter = null;
            var window = outskirtsView;
            outskirtsView = null;
            window?.Close();
        }

        void ReturnToLogin()
        {
            CloseOutskirts();
            var city=cityPresenter;cityPresenter=null;
            var window=cityView;cityView=null;
            city?.Unbind();window?.Close();activeSession?.Disconnect();
        }
        void OnSessionState(SessionState state)
        {
            if(state!=SessionState.Disconnected)return;
            navigationEpoch++;
            if(cityView!=null)ReturnToLogin();
        }

        void OnCancelled()
        {
            if(ownerContext!=null && !ReferenceEquals(SynchronizationContext.Current,ownerContext))ownerContext.Post(_=>Cleanup(),null);
            else Cleanup();
        }

        void Cleanup()
        {
            CloseOutskirts();
            var registration=cancelled;cancelled=default;
            navigationEpoch++;
            if(activeSession!=null)activeSession.StateChanged-=OnSessionState;
            activeSession=null;
            var source=lifetime;lifetime=null;
            var login=presenter;presenter=null;
            if(login!=null)login.EnterGameRequested-=EnterCity;
            var city=cityPresenter;cityPresenter=null;
            var cityWindow=cityView;cityView=null;
            var window=view;view=null;
            var simulation=localSession;localSession=null;
            registration.Dispose();
            try{source?.Cancel();}
            finally
            {
                try{city?.Unbind();cityWindow?.Close();login?.Unbind();}
                finally
                {
                    try{window?.Close();}
                    finally{simulation?.Dispose();source?.Dispose();}
                }
            }
        }
    }
}
