using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Client.Contracts;
using Client.Framework.HotUpdate;
using Client.Framework.Network;
using Client.Framework.Release;
using Client.Framework.Resource;
using Client.Framework.UI;
using GameFramework.Event;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using UnityGameFramework.Runtime;

namespace Client.Bootstrap
{
    // Public trust material is a native scene-owned asset, never a release/slot/runtime-argument input.
    public sealed class NativeClientHost : MonoBehaviour, ILogService
    {
        [SerializeField] private TextAsset m_PublicProfile;
        [SerializeField] private GameObject m_UIRoot;
        private NativeAcceptanceOptions options;
        private NativeEvidence evidence;
        private ClientBootstrap host;
        private ReleaseCoordinator coordinator;
        private ReleaseStateStore state;
        private GfDownloadTransport transport;
        private ObservedReleaseTransport observedTransport;
        private EventComponent events;
        private CancellationTokenSource launch;
        private string releaseRoot, uiFingerprint;
        private int metadataCount, resourceAttempts, stableFrames;
        private bool completed, observing;
        private static readonly int[] DownloadEvents = { DownloadStartEventArgs.EventId, DownloadUpdateEventArgs.EventId, DownloadSuccessEventArgs.EventId, DownloadFailureEventArgs.EventId };
        private async void Start()
        {
            try
            {
                if(m_PublicProfile==null||m_UIRoot==null)throw new InvalidDataException("Native profile and UI root must be baked into the Player scene.");
                var profile=NativeBuildProfile.Parse(m_PublicProfile.text);
                options=NativeAcceptanceOptions.Parse(profile.Acceptance,Input("clientAcceptance"));
                string evidenceRoot=Path.Combine(Application.persistentDataPath,"Acceptance",options.RunId??"normal",options.CaseId??"startup");
                evidence=new NativeEvidence(evidenceRoot,options);
                Application.logMessageReceived+=OnLog;
                host=GetComponent<ClientBootstrap>();if(host==null)throw new InvalidOperationException("Native host requires ClientBootstrap.");
                var resources=FindObjectOfType<ResourceSlotBootstrap>();
                await Task.Yield();
                var identity=JObject.Parse(await ReadNativeDocument("ClientIdentity.json"));
                var player=new PlayerIdentity((string)identity["playerBuildId"],(string)identity["platform"],(string)identity["architecture"],(string)identity["unityVersion"],(string)identity["contractVersion"],(string)identity["envelopeSha256"],(string)identity["aotMetadataSetId"]);
                evidence.Record("native.runtime",new {applicationId=Application.identifier,profilePackageId=profile.PackageId,platform=Application.platform.ToString(),unityVersion=Application.unityVersion,pointerBits=IntPtr.Size*8});
                RequireRuntimeIdentity(player,profile.PackageId,Application.platform,Application.identifier,Application.unityVersion,IntPtr.Size);
                releaseRoot=Path.Combine(ReleaseStore.PersistentRoot,player.PlayerBuildId);
                evidence.Record("native.identity",new {player,releaseRoot,applicationId=Application.identifier,profilePackageId=profile.PackageId,platform=Application.platform.ToString(),pointerBits=IntPtr.Size*8,processor=SystemInfo.processorType,profileSha256=HotfixLoadPlan.Hash(System.Text.Encoding.UTF8.GetBytes(profile.Json))});
                var verifier=profile.CreateVerifier();state=new ReleaseStateStore(releaseRoot,verifier,player,evidence);
                evidence.Record("state.before",state.Read());
                events=GameEntry.GetComponent<EventComponent>();
                transport=new GfDownloadTransport(GameEntry.GetComponent<DownloadComponent>(),events,profile.Endpoint);observedTransport=new ObservedReleaseTransport(transport);
                foreach(int id in DownloadEvents)events.Subscribe(id,OnDownload);observing=true;
                var store=new ReleaseStore(releaseRoot,new ReleaseDownloader(observedTransport),state,options.LowSpace?new DiskBudget(_=>0):null,evidence);
                var baseline=new PlatformReleaseStreamReader(Application.streamingAssetsPath.TrimEnd('/')+"/Baseline",Application.platform==RuntimePlatform.Android,Path.Combine(releaseRoot,"streams"));
                coordinator=new ReleaseCoordinator(state,new NativeLaunchConfiguration(player,options.Tok?"Client.Game.TokDevelopmentEntry":"Client.Game.Entry","UILogin",TimeSpan.FromSeconds(90)),store,verifier,observedTransport,baseline);
                coordinator.Changed+=OnChanged;
                var session=new SessionClient(new TcpFrameConnection(),new SessionOptions(profile.SessionHost,profile.SessionPort));
                var observedSession=new NativeObservedSession(session,evidence,options.InvalidCredential);
                var composition=new NativeClientComposition(m_UIRoot,"Assets/Content/UI/Common/UIViewDefinitions.asset",this,host.Lifetime,observedSession);
                host.ConfigureLaunchAsync(coordinator,resources,composition.ComposeAsync,false);host.enabled=true;
                launch=CancellationTokenSource.CreateLinkedTokenSource(host.Lifetime);
                if(Application.platform==RuntimePlatform.Android)StartCoroutine(EnableRotationAfterInitialPortrait());
                try{await host.LaunchAsync(launch.Token);}
                catch(Exception error){evidence.Record("launch.failure",new {error=Bounded(error.ToString()),restartRequired=coordinator.RestartRequired});Debug.LogException(error);}
                completed=true;
                evidence.Record("launch.result",new {ready=coordinator.IsReady,restartRequired=coordinator.RestartRequired,selection=coordinator.Diagnostics.Selection,state=state.Read(),metadataCount,resourceAttempts,hotAssemblies=AppDomain.CurrentDomain.GetAssemblies().Where(a=>a.GetName().Name.StartsWith("Client.Game",StringComparison.Ordinal)).Select(a=>a.GetName().Name).ToArray(),failures=coordinator.Diagnostics.Failures.Select(f=>new {f.ReleaseId,f.Epoch,phase=f.Phase.ToString(),f.Code,error=Bounded(f.Exception.ToString())}).ToArray(),network=observedTransport.Records});
                if(options.AutoExit)StartCoroutine(FinishAcceptance());
            }
            catch(Exception error)
            {
                Debug.LogException(error);evidence?.Record("host.failure",new {error=Bounded(error.ToString())});completed=true;
                // 此时 Bootstrap 可能仍被禁用，不能依赖它的 Start/启动任务来切换失败界面。
                (host??GetComponent<ClientBootstrap>())?.ShowNativeSetupFailure();
                if(options?.AutoExit==true)StartCoroutine(FinishAcceptance());
            }
        }
        private static void RequireRuntimeIdentity(PlayerIdentity player,string profilePackageId,RuntimePlatform runtimePlatform,string actualApplicationId,string runtimeUnityVersion,int pointerBytes)
        {
            if(runtimePlatform!=RuntimePlatform.WindowsPlayer&&runtimePlatform!=RuntimePlatform.Android)throw new InvalidDataException("Unsupported native runtime platform.");
            string platform=runtimePlatform==RuntimePlatform.Android?"Android":"StandaloneWindows64";
            string architecture=runtimePlatform==RuntimePlatform.Android?"arm64":"x64";
            if(player.Platform!=platform||player.Architecture!=architecture||player.UnityVersion!=runtimeUnityVersion||pointerBytes!=8)
                throw new InvalidDataException("Native Player identity differs from the actual platform, target architecture, Unity version or pointer width.");
            // Win32 standalone has no Android-style OS package identifier. Native package pins still bind the full Player identity.
            if(runtimePlatform==RuntimePlatform.Android&&profilePackageId!=actualApplicationId)throw new InvalidDataException("Baked profile package differs from this native Android application.");
        }
        private void OnChanged(ReleaseDiagnostics diagnostics)
        {
            if(diagnostics.Phase==ReleasePhase.ResourceInit)resourceAttempts++;
            evidence.Record("release.phase",new {phase=diagnostics.Phase.ToString(),diagnostics.Code,selection=diagnostics.Selection,resourceAttempts,metadataCount,frame=Time.frameCount});
            if(diagnostics.Phase==ReleasePhase.Trial && state.Read().Attempt==1)
                evidence.Checkpoint("trial.persisted",null,new {selection=diagnostics.Selection,state=state.Read()});
        }
        private void OnDownload(object sender,GameEventArgs args)
        {
            object owner;string kind,path,uri;long bytes;int serial;
            if(args is DownloadStartEventArgs start){owner=start.UserData;kind="start";path=start.DownloadPath;uri=start.DownloadUri;bytes=start.CurrentLength;serial=start.SerialId;}
            else if(args is DownloadUpdateEventArgs update){owner=update.UserData;kind="update";path=update.DownloadPath;uri=update.DownloadUri;bytes=update.CurrentLength;serial=update.SerialId;}
            else if(args is DownloadSuccessEventArgs success){owner=success.UserData;kind="success";path=success.DownloadPath;uri=success.DownloadUri;bytes=success.CurrentLength;serial=success.SerialId;}
            else if(args is DownloadFailureEventArgs failure){owner=failure.UserData;kind="failure";path=failure.DownloadPath;uri=failure.DownloadUri;bytes=0;serial=failure.SerialId;}
            else return;
            if(!ReferenceEquals(owner,transport))return;
            evidence.Record("download."+kind,new {path,uri,bytes,serial});
            if(kind=="update"&&evidence.IsIncompleteTransfer(path,bytes))
            {
                // Require an authenticated candidate path and 0 < actual bytes < authenticated total.
                evidence.Checkpoint("download.partial",null,new {path,uri,bytes,expectedBytes=evidence.ExpectedTransferLength(path),serial});
                if(options.CancelOnTransfer)launch?.Cancel();
            }
        }
        public void Write(ClientLogLevel level,string code,string message)
        {
            evidence?.Record("hot.log",new {level=level.ToString(),code,message=Bounded(message)});Debug.Log(code+" "+message);
            if(code=="CLIENT_ENTRY_EXECUTING")evidence?.Checkpoint("hotentry.executing",null,new {selection=coordinator?.Diagnostics.Selection,message});
        }
        private void OnLog(string message,string stack,LogType kind)
        {
            if(message.StartsWith("F6_METADATA ",StringComparison.Ordinal)){metadataCount++;evidence?.Record("metadata",new {message,metadataCount,selection=coordinator?.Diagnostics.Selection});}
            else if(kind==LogType.Error||kind==LogType.Exception||kind==LogType.Assert)evidence?.Record("native.error",new {kind=kind.ToString(),message=Bounded(message),stack=Bounded(stack),selection=coordinator?.Diagnostics.Selection});
        }
        private void LateUpdate()
        {
            if(evidence==null||options?.Enabled!=true)return;
            var input=FindObjectsOfType<InputField>().FirstOrDefault();
            var layout=FindObjectsOfType<ScreenLayoutComponent>().FirstOrDefault(c=>c.Service.Current!=null);
            var bridge=FindObjectsOfType<UIViewBridge>().FirstOrDefault();
            var buttons=FindObjectsOfType<Button>().Select(b=>new {id=b.GetInstanceID(),name=b.name,active=b.gameObject.activeInHierarchy,interactable=b.IsInteractable(),bounds=Bounds(b.transform as RectTransform)}).ToArray();
            var current=layout?.Service.Current;
            var snapshot=new {width=Screen.width,height=Screen.height,safeArea=Rect(Screen.safeArea),orientation=Screen.orientation.ToString(),keyboardVisible=TouchScreenKeyboard.visible,keyboardArea=Rect(TouchScreenKeyboard.area),selected=EventSystem.current?.currentSelectedGameObject?.GetInstanceID()??0,view=bridge==null?0:bridge.gameObject.GetInstanceID(),input=input==null?null:new {id=input.GetInstanceID(),textSha256=HotfixLoadPlan.Hash(System.Text.Encoding.UTF8.GetBytes(input.text??"")),length=input.text?.Length??0,focused=input.isFocused,caret=input.caretPosition,selectionAnchor=input.selectionAnchorPosition,selectionFocus=input.selectionFocusPosition,bounds=Bounds(input.transform as RectTransform)},layout=current==null?null:new {current.Width,current.Height,current.Revision,mode=current.LayoutMode.ToString(),safeArea=Rect(current.ClampedSafeArea)},buttons,scrolls=FindObjectsOfType<ScrollRect>().Select(s=>new {id=s.GetInstanceID(),x=s.normalizedPosition.x,y=s.normalizedPosition.y,bounds=Bounds(s.viewport)}).ToArray()};
            string fingerprint=JsonConvert.SerializeObject(snapshot);
            if(fingerprint!=uiFingerprint){uiFingerprint=fingerprint;stableFrames=0;evidence.Record("ui.sample",new {frame=Time.frameCount,realtime=Time.realtimeSinceStartup,snapshot});}
            else if(++stableFrames==2)evidence.Record("ui.stable",new {frame=Time.frameCount,realtime=Time.realtimeSinceStartup,stableFrames,snapshot});
        }
        private static object Rect(UnityEngine.Rect value)=>new {x=value.x,y=value.y,width=value.width,height=value.height};
        private static object Bounds(RectTransform value)
        {if(value==null)return null;var corners=new Vector3[4];value.GetWorldCorners(corners);var points=corners.Select(p=>RectTransformUtility.WorldToScreenPoint(null,p)).ToArray();return new {left=points.Min(p=>p.x),right=points.Max(p=>p.x),bottom=points.Min(p=>p.y),top=points.Max(p=>p.y)};}
        private void OnApplicationPause(bool paused)=>evidence?.Record("application.pause",new {paused,frame=Time.frameCount});
        private void OnApplicationFocus(bool focused)=>evidence?.Record("application.focus",new {focused,frame=Time.frameCount});
        private IEnumerator EnableRotationAfterInitialPortrait(){yield return null;yield return null;Screen.orientation=ScreenOrientation.AutoRotation;evidence?.Record("orientation.policy",new {initial="Portrait",automatic=true});}
        private IEnumerator FinishAcceptance()
        {
            yield return new WaitForEndOfFrame();
            if(evidence!=null)
            {
                var image=ScreenCapture.CaptureScreenshotAsTexture();try{File.WriteAllBytes(Path.Combine(evidence.DirectoryPath,"screen.png"),image.EncodeToPNG());}finally{Destroy(image);}
                evidence.Record("process.finished",new {completed,ready=coordinator?.IsReady??false,restartRequired=coordinator?.RestartRequired??false});
                File.WriteAllText(Path.Combine(evidence.DirectoryPath,"complete.json"),new JObject{["runId"]=options.RunId,["caseId"]=options.CaseId,["attemptId"]=evidence.AttemptId,["processId"]=System.Diagnostics.Process.GetCurrentProcess().Id,["ready"]=coordinator?.IsReady??false,["restartRequired"]=coordinator?.RestartRequired??false,["releaseRoot"]=releaseRoot}.ToString(Formatting.Indented));
            }
            Application.Quit(coordinator?.IsReady==true?0:1);
        }
        private static async Task<string> ReadNativeDocument(string name)
        {var reader=new PlatformReleaseStreamReader();using(var input=await reader.OpenReadAsync(name,CancellationToken.None))using(var output=new MemoryStream()){var buffer=new byte[4096];int count;while((count=await input.ReadAsync(buffer,0,buffer.Length))>0){if(output.Length+count>256*1024)throw new InvalidDataException("Native identity is oversized.");output.Write(buffer,0,count);}return System.Text.Encoding.UTF8.GetString(output.ToArray());}}
        private static string Input(string name)
        {if(Application.platform==RuntimePlatform.Android){using(var unity=new AndroidJavaClass("com.unity3d.player.UnityPlayer"))using(var activity=unity.GetStatic<AndroidJavaObject>("currentActivity"))using(var intent=activity.Call<AndroidJavaObject>("getIntent"))return intent.Call<string>("getStringExtra",name);}string[] args=Environment.GetCommandLineArgs();int i=Array.IndexOf(args,"-"+name);return i>=0&&i+1<args.Length?args[i+1]:null;}
        private static string Bounded(string value)=>value==null?null:value.Length<=16384?value:value.Substring(0,16384)+" [truncated]";
        private void OnDestroy()
        {Application.logMessageReceived-=OnLog;if(observing&&events!=null)foreach(int id in DownloadEvents)events.Unsubscribe(id,OnDownload);launch?.Cancel();launch?.Dispose();observedTransport?.Dispose();coordinator?.Dispose();evidence?.Dispose();}
    }

    internal sealed class NativeObservedSession : ISessionClient, IDisposable
    {
        private readonly SessionClient session;private readonly NativeEvidence evidence;private readonly bool invalidCredential;private long requests;
        public NativeObservedSession(SessionClient session,NativeEvidence evidence,bool invalidCredential){this.session=session;this.evidence=evidence;this.invalidCredential=invalidCredential;session.StateChanged+=Changed;}
        public event Action<SessionState> StateChanged;
        private void Changed(SessionState value){evidence.Record("session.state",new {state=value.ToString(),requests,connectionEpoch=session.ConnectionEpoch,requestId=session.ActiveRequestId,serverEpoch=session.Identity?.Epoch});StateChanged?.Invoke(value);}
        public async Task<LoginResult> LoginAsync(LoginInput input,CancellationToken token)
        {long request=++requests;evidence.Record("session.request",new {request});var result=await session.LoginAsync(invalidCredential?new LoginInput("invalid-d2-credential",null):input,token);evidence.Record("session.result",new {request,result.Succeeded,error=result.Error.ToString(),result.ServerErrorCode,epoch=result.Identity?.Epoch});return result;}
        public void Disconnect()=>session.Disconnect();
        public void Dispose(){session.StateChanged-=Changed;session.Dispose();}
    }
}
