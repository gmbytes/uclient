using System;
using System.Threading;
using System.Threading.Tasks;
using Client.Contracts;
using Client.Framework.UI;
using Client.Game.Login;

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
                var opened=await context.UI.OpenAsync("UILogin",launchToken);
                view=opened;
                launchToken.ThrowIfCancellationRequested();
                presenter=new UILogin();presenter.ConfigurePresentation(label,version);presenter.Bind(opened,session);
                context.Log?.Write(ClientLogLevel.Info,"CLIENT_VIEW",Newtonsoft.Json.JsonConvert.SerializeObject(new {view=opened.Root.GetInstanceID(),presenter=System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(presenter),version,mode=mode.ToString()}));
                await UIFirstFrame.WaitAsync(opened,launchToken);
                launchToken.ThrowIfCancellationRequested();
                ((UIViewHandle)opened).Lifetime.ThrowIfCancellationRequested();
                if(!ReferenceEquals(view,opened))throw new OperationCanceledException(launchToken);
                DeliveryVersion.Run(context.Log);
                context.ReportReady();
            }
            catch
            {
                Cleanup();throw;
            }
        }

        public Task ShutdownAsync(CancellationToken token)
        {Cleanup();return Task.CompletedTask;}

        void OnCancelled()
        {
            if(ownerContext!=null && !ReferenceEquals(SynchronizationContext.Current,ownerContext))ownerContext.Post(_=>Cleanup(),null);
            else Cleanup();
        }

        void Cleanup()
        {
            var registration=cancelled;cancelled=default;
            var source=lifetime;lifetime=null;
            var login=presenter;presenter=null;
            var window=view;view=null;
            var simulation=localSession;localSession=null;
            registration.Dispose();
            try{source?.Cancel();}
            finally
            {
                try{login?.Unbind();}
                finally
                {
                    try{window?.Close();}
                    finally{simulation?.Dispose();source?.Dispose();}
                }
            }
        }
    }
}
