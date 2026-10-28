#if UNITY_EDITOR
using System;
using System.Collections;
using System.Reflection;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Client.Contracts;
using Client.Framework.UI;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.TestTools;
using UnityEngine.UI;
using UnityGameFramework.Runtime;
using Object=UnityEngine.Object;
using SessionState=Client.Contracts.SessionState;

namespace Client.Tests
{
    [Category("U5")]
    public sealed class UILoginTests
    {
        GameObject host,root,events; UIService service; IViewHandle view; object presenter;UIComponent ui;
        ControlledSession session;
        IHotfixEntry entry;
        static Type PresenterType
        {
            get {var type=Type.GetType("Client.Game.Login.UILogin, Client.Game");Assert.That(type,Is.Not.Null,"U5 requires the plain login presenter.");return type;}
        }
        static object Call(object target,string method,params object[] args)=>target.GetType().GetMethod(method).Invoke(target,args);
        T Control<T>(string name) where T:Component=>(T)view.Root.GetComponent<UIBindings>().Resolve(name);
        static void Set(object target,string name,object value)=>target.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic).SetValue(target,value);
        static int CollectionCount(object target,string name){var value=target.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic).GetValue(target);return (int)value.GetType().GetProperty("Count").GetValue(value);}
        [UnitySetUp] public IEnumerator Setup()
        {
            var required=PresenterType;
            GameEntry.Shutdown(ShutdownType.None);yield return null;
            root=Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Content/UI/Common/AdaptiveUIRoot.prefab"));
            root.GetComponent<ScreenLayoutComponent>().enabled=false;
            var screen=root.GetComponent<ScreenLayoutComponent>().Service;screen.Evaluate(new ScreenMetrics(720,1280,new Rect(0,0,720,1280)));screen.FlushPending(Time.frameCount);
            events=new GameObject("U5 EventSystem",typeof(EventSystem),typeof(StandaloneInputModule));
            host=new GameObject("U5 native GF host");host.SetActive(false);
            host.AddComponent<BaseComponent>();var eventComponent=host.AddComponent<EventComponent>();
            var resources=host.AddComponent<EditorResourceComponent>();Set(resources,"m_MinLoadAssetRandomDelaySeconds",0f);Set(resources,"m_MaxLoadAssetRandomDelaySeconds",0f);
            host.AddComponent<ResourceComponent>();host.AddComponent<ObjectPoolComponent>();ui=host.AddComponent<UIComponent>();
            Set(ui,"m_UIFormHelperTypeName",typeof(U3ObservedUIFormHelper).FullName);U3ObservedUIFormHelper.Instances=U3ObservedUIFormHelper.Releases=0;
            Set(ui,"m_UIGroups",Array.CreateInstance(typeof(UIComponent).GetNestedType("UIGroup",BindingFlags.NonPublic),0));
            host.SetActive(true);yield return null;
            service=new UIService(ui,eventComponent,AssetDatabase.LoadAssetAtPath<UIViewDefinitions>("Assets/Content/UI/Common/UIViewDefinitions.asset"),root.GetComponent<AdaptiveCanvas>(),root.transform.Find("Windows"));
            session=new ControlledSession();
            var opening=service.OpenAsync("UILogin",CancellationToken.None);yield return Await(opening);view=opening.Result;
            presenter=Activator.CreateInstance(required);Call(presenter,"Bind",view,session);
        }
        [UnityTearDown] public IEnumerator Cleanup()
        {
            if(entry!=null){var shutdown=entry.ShutdownAsync(CancellationToken.None);yield return Await(shutdown);entry=null;}
            if(presenter!=null)Call(presenter,"Unbind");view?.Close();service?.Dispose();GameEntry.Shutdown(ShutdownType.None);
            if(host!=null)Object.Destroy(host);if(root!=null)Object.Destroy(root);if(events!=null)Object.Destroy(events);yield return null;
        }
        static IEnumerator Await(Task task)
        {float end=Time.realtimeSinceStartup+10;while(!task.IsCompleted&&Time.realtimeSinceStartup<end)yield return null;Assert.That(task.IsCompleted,Is.True,"Operation timed out.");Assert.That(task.IsFaulted,Is.False,task.Exception?.ToString());}
        [Test] public void OneClickCreatesOneLogin()
        {
            Control<InputField>("AccountInput").text="10001";Control<Button>("LoginButton").onClick.Invoke();Control<Button>("LoginButton").onClick.Invoke();
            Assert.That(session.Calls,Is.EqualTo(1));Assert.That(session.Input.Credential,Is.EqualTo("demo:10001"));
            Assert.That(Control<Button>("LoginButton").interactable,Is.False);Assert.That(Control<Button>("CancelButton").interactable,Is.True);
        }
        [UnityTest] public IEnumerator ButtonStatesFollowSessionState()
        {
            Control<InputField>("AccountInput").text="10001";Control<Button>("LoginButton").onClick.Invoke();session.Emit(SessionState.Authenticating);
            Assert.That(Control<Button>("LoginButton").interactable,Is.False);
            session.Complete(new LoginResult(false,LoginError.Rejected,403,null,"unsafe remote account token"));yield return null;
            Assert.That(Control<Button>("LoginButton").interactable,Is.True);Assert.That(Control<Button>("CancelButton").interactable,Is.False);
            Assert.That(Control<Text>("StatusLabel").text,Does.Not.Contain("unsafe"));
            Control<Button>("LoginButton").onClick.Invoke();session.Complete(new LoginResult(true,LoginError.None,0,new SessionIdentity("p","s",1,"secret",1),null));yield return null;
            Assert.That(Control<Button>("LoginButton").interactable,Is.False,"An authenticated session stays on the page without offering another active login.");Assert.That(Control<Button>("CancelButton").interactable,Is.False);
            session.Emit(SessionState.Disconnected);Assert.That(Control<Button>("LoginButton").interactable,Is.True);
        }
        [UnityTest] public IEnumerator RotateDuringLoginRetainsTextFocusAndRequest()
        {
            var input=Control<InputField>("AccountInput");input.text="10001";EventSystem.current.SetSelectedGameObject(input.gameObject);
            Control<Button>("LoginButton").onClick.Invoke();var identity=view.Root.GetInstanceID();
            foreach(var size in new[]{new Vector2Int(720,1280),new Vector2Int(1280,720),new Vector2Int(320,240)})
            {
                var screen=root.GetComponent<ScreenLayoutComponent>().Service;screen.Evaluate(new ScreenMetrics(size.x,size.y,new Rect(0,0,size.x,size.y)));screen.FlushPending(Time.frameCount);
                yield return null;
                Assert.That(view.Root.GetInstanceID(),Is.EqualTo(identity));Assert.That(input.text,Is.EqualTo("10001"));Assert.That(EventSystem.current.currentSelectedGameObject,Is.SameAs(input.gameObject));Assert.That(session.Calls,Is.EqualTo(1));Assert.That(session.Token.IsCancellationRequested,Is.False);
            }
        }
        [UnityTest] public IEnumerator CloseThenLateSuccessDoesNotTouchView()
        {
            var status=Control<Text>("StatusLabel");Control<InputField>("AccountInput").text="10001";Control<Button>("LoginButton").onClick.Invoke();
            view.Close();string closed=status.text;session.Complete(new LoginResult(true,LoginError.None,0,new SessionIdentity("p","s",1,"secret",1),null));yield return null;
            Assert.That(status.text,Is.EqualTo(closed));Assert.That(session.Token.IsCancellationRequested,Is.True);Assert.That(session.Subscribers,Is.Zero);
        }
        [UnityTest] public IEnumerator SimulationNeverClaimsServerAuthentication()
        {
            Call(presenter,"Unbind");var type=Type.GetType("Client.Game.Login.LocalSimulationSession, Client.Game");Assert.That(type,Is.Not.Null);
            var local=(ISessionClient)Activator.CreateInstance(type);Call(presenter,"Bind",view,local);
            Control<InputField>("AccountInput").text="10001";Control<Button>("LoginButton").onClick.Invoke();yield return null;yield return null;
            Assert.That(Control<Text>("VersionLabel").text,Does.Contain("本地模拟"));Assert.That(Control<Text>("StatusLabel").text,Does.Contain("本地模拟").And.Not.Contain("已登录（框架演示）"));
        }
        [UnityTest] public IEnumerator CancelledSimulationCanStartAgainWithoutServerIdentity()
        {
            var local=(ISessionClient)Activator.CreateInstance(Type.GetType("Client.Game.Login.LocalSimulationSession, Client.Game",true));
            bool authenticated=false;local.StateChanged+=state=>authenticated|=state==SessionState.Authenticated;
            using(var cancel=new CancellationTokenSource())
            {
                var first=local.LoginAsync(new LoginInput("demo:10001",null),cancel.Token);cancel.Cancel();yield return Await(first);
                Assert.That(first.Result.Error,Is.EqualTo(LoginError.Cancelled));
                var next=local.LoginAsync(new LoginInput("demo:10001",null),CancellationToken.None);yield return Await(next);
                Assert.That(next.Result.Succeeded,Is.True);Assert.That(next.Result.Identity,Is.Null);Assert.That(authenticated,Is.False);
            }
            ((IDisposable)local).Dispose();
        }
        [UnityTest] public IEnumerator CancelThenNewRequestIgnoresOldCompletion()
        {
            Control<InputField>("AccountInput").text="10001";Control<Button>("LoginButton").onClick.Invoke();var old=session.Pending;var token=session.Token;
            Control<Button>("CancelButton").onClick.Invoke();Assert.That(token.IsCancellationRequested,Is.True);Assert.That(Control<Button>("LoginButton").interactable,Is.True);
            Control<Button>("LoginButton").onClick.Invoke();Assert.That(session.Calls,Is.EqualTo(2));
            old.SetResult(new LoginResult(true,LoginError.None,0,new SessionIdentity("p","old",1,"secret",1),null));yield return null;
            Assert.That(Control<Button>("LoginButton").interactable,Is.False);Assert.That(Control<Text>("StatusLabel").text,Does.Not.Contain("已登录"));
            session.Complete(new LoginResult(true,LoginError.None,0,new SessionIdentity("p","new",2,"secret",1),null));yield return null;
            Assert.That(Control<Text>("StatusLabel").text,Is.EqualTo("已登录（框架演示）"));
        }
        [UnityTest] public IEnumerator DisconnectAfterCompletedSuccessBeforeContinuationKeepsRetryEnabled()
        {
            Control<InputField>("AccountInput").text="10001";Control<Button>("LoginButton").onClick.Invoke();
            session.Emit(SessionState.Authenticated);
            session.Complete(new LoginResult(true,LoginError.None,0,new SessionIdentity("p","s",1,"secret",1),null));
            session.Emit(SessionState.Disconnected);
            Assert.That(Control<Text>("StatusLabel").text,Is.EqualTo("连接已断开，请重试"));
            Assert.That(Control<Button>("LoginButton").interactable,Is.True);Assert.That(Control<Button>("CancelButton").interactable,Is.False);
            yield return null;
            Assert.That(Control<Text>("StatusLabel").text,Is.EqualTo("连接已断开，请重试"));
            Assert.That(Control<Button>("LoginButton").interactable,Is.True);Assert.That(Control<Button>("CancelButton").interactable,Is.False);
            Control<Button>("LoginButton").onClick.Invoke();Assert.That(session.Calls,Is.EqualTo(2));
            session.Complete(new LoginResult(false,LoginError.Rejected,3,null,null));yield return null;
            Assert.That(Control<Button>("LoginButton").interactable,Is.True);Assert.That(Control<Text>("StatusLabel").text,Does.Contain("拒绝"));
        }
        [UnityTest] public IEnumerator TwentyOpenCloseCyclesReturnEventsAndNativeHandlesToBaseline()
        {
            Call(presenter,"Unbind");view.Close();
            for(int cycle=0;cycle<20;cycle++)
            {
                var opening=service.OpenAsync("UILogin",CancellationToken.None);yield return Await(opening);view=opening.Result;
                presenter=Activator.CreateInstance(PresenterType);Call(presenter,"Bind",view,session);
                Control<InputField>("AccountInput").text="10001";Control<Button>("LoginButton").onClick.Invoke();
                int calls=session.Calls;view.Close();var late=session.Pending;late.SetResult(new LoginResult(true,LoginError.None,0,null,null));yield return null;
                Assert.That(session.Subscribers,Is.Zero);Assert.That(ui.GetAllLoadedUIForms(),Is.Empty);Assert.That(ui.GetAllLoadingUIFormSerialIds(),Is.Empty);
                Assert.That(CollectionCount(service,"active"),Is.Zero);
                Assert.That(CollectionCount(service,"pending"),Is.Zero);
                Assert.That(session.Calls,Is.EqualTo(calls));
            }
            service.Dispose();service=null;GameEntry.Shutdown(ShutdownType.None);yield return null;
            Assert.That(U3ObservedUIFormHelper.Releases,Is.EqualTo(U3ObservedUIFormHelper.Instances),"Native pooled prefab ownership must return to baseline on shutdown.");
        }
        [UnityTest] public IEnumerator AuthoredLoginHasFontsFullFrameBackgroundAndReachableShortWindowActions()
        {
            var bridge=view.Root.GetComponent<UIViewBridge>();var input=Control<InputField>("AccountInput");
            Assert.That(view.Root.GetComponent<Canvas>(),Is.Null);Assert.That(bridge.RootCanvas.name,Is.EqualTo("UINode"));Assert.That(bridge.LocalSafeArea.transform.parent,Is.SameAs(bridge.RootCanvas.transform));
            Assert.That(view.Root.GetComponentsInChildren<CanvasScaler>(true),Is.Empty);
            foreach(var text in view.Root.GetComponentsInChildren<Text>(true))
            {Assert.That(text.font,Is.Not.Null);Assert.That(AssetDatabase.GetAssetPath(text.font),Is.EqualTo("Assets/Content/UI/Common/Fonts/NotoSansCJKsc-Regular.otf"));}
            bool graphics=SystemInfo.graphicsDeviceType!=UnityEngine.Rendering.GraphicsDeviceType.Null;
            var cameraObject=new GameObject("U5 login capture",typeof(Camera));var camera=cameraObject.GetComponent<Camera>();camera.transform.position=new Vector3(0,0,-10);camera.orthographic=true;
            var canvas=root.GetComponent<Canvas>();canvas.renderMode=RenderMode.ScreenSpaceCamera;canvas.worldCamera=camera;canvas.planeDistance=1;
            RenderTexture texture=null;
            try
            {
                var cases=new[]{new ScreenMetrics(720,1280,new Rect(24,50,672,1180)),new ScreenMetrics(1280,720,new Rect(50,24,1180,672)),new ScreenMetrics(320,240,new Rect(0,0,320,240))};
                foreach(var metrics in cases)
                {
                    if(graphics){if(texture!=null){camera.targetTexture=null;texture.Release();Object.DestroyImmediate(texture);}texture=new RenderTexture(metrics.Width,metrics.Height,24);texture.Create();camera.targetTexture=texture;yield return null;Canvas.ForceUpdateCanvases();camera.Render();}
                    var screen=root.GetComponent<ScreenLayoutComponent>().Service;screen.Evaluate(metrics);screen.FlushPending(Time.frameCount);Canvas.ForceUpdateCanvases();yield return null;
                    var rootRect=(RectTransform)root.transform;var background=(RectTransform)view.Root.transform.Find("UINode/Background");
                    var bg=RectInRoot(background,rootRect,metrics);Assert.That(bg.xMin,Is.EqualTo(0).Within(.2f));Assert.That(bg.yMin,Is.EqualTo(0).Within(.2f));Assert.That(bg.width,Is.EqualTo(metrics.Width).Within(.2f));Assert.That(bg.height,Is.EqualTo(metrics.Height).Within(.2f));
                    var safe=RectInRoot((RectTransform)bridge.LocalSafeArea.transform,rootRect,metrics);Assert.That(safe.xMin,Is.EqualTo(metrics.SafeArea.xMin).Within(.2f));Assert.That(safe.yMin,Is.EqualTo(metrics.SafeArea.yMin).Within(.2f));
                    if(metrics.Width==320)
                    {
                        var scroll=bridge.Layout.GetComponentInChildren<ScrollRect>();bridge.Keyboard.KeyboardVisibleSource=()=>true;bridge.Keyboard.Apply(screen.Current,Rect.zero,(RectTransform)input.transform);
                        Assert.That(scroll.movementType,Is.EqualTo(ScrollRect.MovementType.Unrestricted));scroll.verticalNormalizedPosition=0;Canvas.ForceUpdateCanvases();
                        bridge.Keyboard.Reveal((RectTransform)Control<Button>("LoginButton").transform);Canvas.ForceUpdateCanvases();
                        var action=RectTransformUtility.CalculateRelativeRectTransformBounds(scroll.viewport,(RectTransform)Control<Button>("LoginButton").transform);
                        Assert.That(action.min.y,Is.GreaterThanOrEqualTo(scroll.viewport.rect.yMin-.2f));Assert.That(action.max.y,Is.LessThanOrEqualTo(scroll.viewport.rect.yMax+.2f));
                    }
                    if(graphics)
                    {
                        camera.Render();var previous=RenderTexture.active;RenderTexture.active=texture;var image=new Texture2D(metrics.Width,metrics.Height,TextureFormat.RGB24,false);image.ReadPixels(new Rect(0,0,metrics.Width,metrics.Height),0,0);image.Apply();
                        Directory.CreateDirectory("Builds/Tests/U5/screenshots");File.WriteAllBytes("Builds/Tests/U5/screenshots/"+metrics.Width+"x"+metrics.Height+".png",image.EncodeToPNG());Object.DestroyImmediate(image);RenderTexture.active=previous;
                    }
                }
            }
            finally{camera.targetTexture=null;if(texture!=null){texture.Release();Object.DestroyImmediate(texture);}Object.Destroy(cameraObject);}
        }
        static Rect RectInRoot(RectTransform target,RectTransform rootRect,ScreenMetrics metrics)
        {var corners=new Vector3[4];target.GetWorldCorners(corners);var min=(Vector2)rootRect.InverseTransformPoint(corners[0])-rootRect.rect.min;var max=(Vector2)rootRect.InverseTransformPoint(corners[2])-rootRect.rect.min;return Rect.MinMaxRect(min.x/rootRect.rect.width*metrics.Width,min.y/rootRect.rect.height*metrics.Height,max.x/rootRect.rect.width*metrics.Width,max.y/rootRect.rect.height*metrics.Height);}
        void ClosePresenterForEntry()
        {
            Call(presenter,"Unbind");view.Close();presenter=null;view=null;
            var screen=root.GetComponent<ScreenLayoutComponent>().Service;
            screen.Evaluate(new ScreenMetrics(Screen.width,Screen.height,Screen.safeArea));screen.FlushPending(Time.frameCount);
        }
        [UnityTest] public IEnumerator EntryWaitsForActualLoginFrameAndReportsReadyOnceWithoutServerLogin()
        {
            Assert.That(Application.isBatchMode,Is.False,"Entry presentation proof requires the real -Graphics runner.");
            ClosePresenterForEntry();var context=new EntryContext(service,session);
            entry=(IHotfixEntry)Activator.CreateInstance(Type.GetType("Client.Game.Entry, Client.Game",true));
            int armedFrame=Time.frameCount;
            var init=entry.InitializeAsync(context,CancellationToken.None);
            Assert.That(context.Ready,Is.Zero,"Entry cannot report ready before loading/binding/rendering the login page.");
            yield return Await(init);
            Assert.That(Time.frameCount,Is.GreaterThan(armedFrame));
            Assert.That(context.Ready,Is.EqualTo(1));Assert.That(session.Calls,Is.Zero);
            Assert.That(context.ResourcesImpl.Live,Is.Zero);Assert.That(context.ResourcesImpl.LastPath,Is.EqualTo("Assets/Settings/Environment/LocalSimulationProfile.asset"));
            var form=ui.GetAllLoadedUIForms().Single();var table=form.GetComponent<UIBindings>();Assert.That(((Text)table.Resolve("VersionLabel")).text,Does.Contain("本地模拟"));
            yield return new WaitForEndOfFrame();
            var pixels=ScreenCapture.CaptureScreenshotAsTexture();
            try
            {
                int blue=pixels.GetPixels32().Count(pixel=>pixel.b>pixel.r+20 && pixel.b>pixel.g+20);
                Assert.That(blue,Is.GreaterThan(500),"Actual Game-view pixels must contain the bound blue login action.");
                Directory.CreateDirectory("Builds/Tests/U5/entry-frame");File.WriteAllBytes("Builds/Tests/U5/entry-frame/local-ready.png",pixels.EncodeToPNG());
            }
            finally{Object.Destroy(pixels);}
            yield return null;yield return null;Assert.That(context.Ready,Is.EqualTo(1));
        }
        [UnityTest] public IEnumerator TokDevelopmentEntryUsesExistingContextSessionAndReleasesProfile()
        {
            ClosePresenterForEntry();var context=new EntryContext(service,session);
            var type=Type.GetType("Client.Game.TokDevelopmentEntry, Client.Game");Assert.That(type,Is.Not.Null);
            entry=(IHotfixEntry)Activator.CreateInstance(type);yield return Await(entry.InitializeAsync(context,CancellationToken.None));
            Assert.That(context.Ready,Is.EqualTo(1));Assert.That(context.ResourcesImpl.Live,Is.Zero);Assert.That(context.ResourcesImpl.LastPath,Is.EqualTo("Assets/Settings/Environment/DevelopmentProfile.asset"));
            var table=ui.GetAllLoadedUIForms().Single().GetComponent<UIBindings>();((InputField)table.Resolve("AccountInput")).text="10001";((Button)table.Resolve("LoginButton")).onClick.Invoke();
            Assert.That(session.Calls,Is.EqualTo(1));Assert.That(((Text)table.Resolve("VersionLabel")).text,Does.Contain("Tok 开发登录").And.Not.Contain("本地模拟"));
            session.Complete(new LoginResult(false,LoginError.ConnectFailed,0,null,null));yield return null;Assert.That(context.Ready,Is.EqualTo(1));
        }
        [UnityTest] public IEnumerator EntryLifetimeCancellationBeforePresentationDoesNotReportReady()
        {
            ClosePresenterForEntry();using(var lifetime=new CancellationTokenSource())
            {
                var context=new EntryContext(service,session,lifetime.Token);entry=(IHotfixEntry)Activator.CreateInstance(Type.GetType("Client.Game.Entry, Client.Game",true));
                var init=entry.InitializeAsync(context,CancellationToken.None);lifetime.Cancel();
                float end=Time.realtimeSinceStartup+10;while(!init.IsCompleted&&Time.realtimeSinceStartup<end)yield return null;
                Assert.That(init.IsCompleted,Is.True);Assert.That(context.Ready,Is.Zero);Assert.That(init.IsCanceled,Is.True);Assert.That(context.ResourcesImpl.Live,Is.Zero);Assert.That(ui.GetAllLoadedUIForms(),Is.Empty);
            }
        }
        [UnityTest] public IEnumerator EntryLifetimeAfterReadyClosesPageAndCancelsPendingLogin()
        {
            ClosePresenterForEntry();using(var lifetime=new CancellationTokenSource())
            {
                var context=new EntryContext(service,session,lifetime.Token);entry=(IHotfixEntry)Activator.CreateInstance(Type.GetType("Client.Game.TokDevelopmentEntry, Client.Game",true));
                yield return Await(entry.InitializeAsync(context,CancellationToken.None));
                var table=ui.GetAllLoadedUIForms().Single().GetComponent<UIBindings>();((Button)table.Resolve("LoginButton")).onClick.Invoke();var token=session.Token;var status=(Text)table.Resolve("StatusLabel");
                lifetime.Cancel();yield return null;string closed=status.text;
                Assert.That(token.IsCancellationRequested,Is.True);Assert.That(session.Subscribers,Is.Zero);Assert.That(ui.GetAllLoadedUIForms(),Is.Empty);Assert.That(context.ResourcesImpl.Live,Is.Zero);
                session.Complete(new LoginResult(true,LoginError.None,0,new SessionIdentity("p","s",1,"secret",1),null));yield return null;
                Assert.That(status.text,Is.EqualTo(closed));Assert.That(context.Ready,Is.EqualTo(1));
            }
        }
        [UnityTest] public IEnumerator EntryCannotInitializeTwiceOrReportReadyForAnotherContext()
        {
            ClosePresenterForEntry();var first=new EntryContext(service,session);entry=(IHotfixEntry)Activator.CreateInstance(Type.GetType("Client.Game.Entry, Client.Game",true));
            yield return Await(entry.InitializeAsync(first,CancellationToken.None));
            var second=new EntryContext(service,session);var retry=entry.InitializeAsync(second,CancellationToken.None);yield return null;
            Assert.That(retry.IsFaulted,Is.True);Assert.That(retry.Exception.InnerException,Is.TypeOf<InvalidOperationException>());
            Assert.That(first.Ready,Is.EqualTo(1));Assert.That(second.Ready,Is.Zero);Assert.That(second.ResourcesImpl.LastPath,Is.Null);Assert.That(ui.GetAllLoadedUIForms().Length,Is.EqualTo(1));
        }
        [UnityTest] public IEnumerator InvalidProfileModeFailsBeforeOpeningAndReleasesItsHandle()
        {
            ClosePresenterForEntry();var profile=ScriptableObject.CreateInstance<LoginPresentationProfile>();profile.Mode=(LoginPresentationMode)99;
            try
            {
                var context=new EntryContext(service,session);context.ResourcesImpl.Override=profile;entry=(IHotfixEntry)Activator.CreateInstance(Type.GetType("Client.Game.Entry, Client.Game",true));
                var init=entry.InitializeAsync(context,CancellationToken.None);yield return null;
                Assert.That(init.IsFaulted,Is.True);Assert.That(init.Exception.InnerException,Is.TypeOf<InvalidOperationException>());Assert.That(context.Ready,Is.Zero);Assert.That(context.ResourcesImpl.Live,Is.Zero);Assert.That(ui.GetAllLoadedUIForms(),Is.Empty);
            }
            finally{Object.Destroy(profile);}
        }
        sealed class EntryContext:IClientContext
        {
            public readonly ProfileResources ResourcesImpl=new ProfileResources();public int Ready;
            public EntryContext(IUIService ui,ISessionClient session,CancellationToken token=default){UI=ui;Session=session;Lifetime=token;}
            public IResourceService Resources=>ResourcesImpl;public IUIService UI{get;}public ISessionClient Session{get;}public ILogService Log=>null;public CancellationToken Lifetime{get;}
            public void ReportReady()=>Ready++;
        }
        sealed class ProfileResources:IResourceService
        {
            public int Live;public string LastPath;public Object Override;
            public Task<IAssetHandle<T>> LoadAsync<T>(string path,CancellationToken token)where T:Object
            {token.ThrowIfCancellationRequested();LastPath=path;var asset=Override as T??AssetDatabase.LoadAssetAtPath<T>(path);if(asset==null)throw new InvalidOperationException("Profile asset missing.");Live++;return Task.FromResult<IAssetHandle<T>>(new ProfileHandle<T>(this,asset));}
            sealed class ProfileHandle<T>:IAssetHandle<T>where T:Object
            {ProfileResources owner;public T Asset{get;}public ProfileHandle(ProfileResources owner,T asset){this.owner=owner;Asset=asset;}public void Dispose(){if(owner==null)return;owner.Live--;owner=null;}}
        }
        sealed class ControlledSession:ISessionClient
        {
            Action<SessionState> changed;TaskCompletionSource<LoginResult> pending;
            public TaskCompletionSource<LoginResult> Pending=>pending;
            public int Calls,Subscribers;public LoginInput Input;public CancellationToken Token;
            public event Action<SessionState> StateChanged{add{changed+=value;Subscribers++;}remove{changed-=value;Subscribers--;}}
            public Task<LoginResult> LoginAsync(LoginInput input,CancellationToken token){Calls++;Input=input;Token=token;pending=new TaskCompletionSource<LoginResult>(TaskCreationOptions.RunContinuationsAsynchronously);Emit(SessionState.Connecting);return pending.Task;}
            public void Disconnect()=>Emit(SessionState.Disconnected);
            public void Emit(SessionState state)=>changed?.Invoke(state);
            public void Complete(LoginResult result)=>pending.TrySetResult(result);
        }
    }
}
#endif
