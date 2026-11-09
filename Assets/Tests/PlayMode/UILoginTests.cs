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
        private sealed class OutskirtsTestClock : Client.Framework.Presentation.IOutskirtsClock
        {
            public DateTimeOffset GameTime { get; set; }
        }
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
        [UnityTest, Category("L1")] public IEnumerator AgreementChoiceGatesTheNativeLoginAction()
        {
            var binding=view.Root.GetComponent<UIBindings>().Singles.FirstOrDefault(item=>item.Id=="AgreementToggle");
            Assert.That(binding,Is.Not.Null,"The illustrated login must expose an agreement choice.");
            var agreement=(Toggle)binding.Value;
            agreement.isOn=false;
            Control<Button>("LoginButton").onClick.Invoke();
            Assert.That(session.Calls,Is.Zero,"A declined agreement must not start a session request.");
            Assert.That(Control<Button>("LoginButton").interactable,Is.False);
            agreement.isOn=true;
            Control<Button>("LoginButton").onClick.Invoke();
            Assert.That(session.Calls,Is.EqualTo(1));
            session.Complete(new LoginResult(false,LoginError.Rejected,403,null,null));
            yield return null;
            view.Close();
            agreement.isOn=false;
            Assert.That(session.Subscribers,Is.Zero,"Closing the window must remove UI and session subscriptions.");
        }
        [Test] public void OneClickCreatesOneLogin()
        {
            Control<InputField>("AccountInput").text="10001";Control<Button>("LoginButton").onClick.Invoke();Control<Button>("LoginButton").onClick.Invoke();
            Assert.That(session.Calls,Is.EqualTo(1));Assert.That(session.Input.Credential,Is.EqualTo("demo:10001"));
            Assert.That(Control<Button>("LoginButton").interactable,Is.False);Assert.That(Control<Button>("CancelButton").interactable,Is.True);
        }
        [Test] public void AccountIsDirectlyEditableWithoutServerExpansion()
        {
            var table=view.Root.GetComponent<UIBindings>();
            var row=(RectTransform)table.Resolve("AccountRow");
            Assert.That(row.gameObject.activeInHierarchy,Is.True,"The account input must be visible immediately.");
            var input=Control<InputField>("AccountInput");
            var action=(RectTransform)Control<Button>("LoginButton").transform;
            Canvas.ForceUpdateCanvases();var before=action.position;
            ((Button)table.Resolve("ServerButton")).onClick.Invoke();
            Canvas.ForceUpdateCanvases();
            Assert.That(row.gameObject.activeInHierarchy,Is.True);
            Assert.That(action.position,Is.EqualTo(before),"The server row must not expand or collapse account controls.");
            input.text="98765";Control<Button>("LoginButton").onClick.Invoke();
            Assert.That(session.Input.Credential,Is.EqualTo("demo:98765"));
        }
        [UnityTest,Category("C1")] public IEnumerator EnterGameOpensTheMigratedCityAndReleasesItOnShutdown()
        {
            ClosePresenterForEntry();
            var context=new EntryContext(service,session);
            entry=(IHotfixEntry)Activator.CreateInstance(Type.GetType("Client.Game.Entry, Client.Game",true));
            yield return Await(entry.InitializeAsync(context,CancellationToken.None));
            var login=ui.GetAllLoadedUIForms().Single(form=>form.name.StartsWith("UILogin",StringComparison.Ordinal)).GetComponent<UIBindings>();
            Assert.That(((RectTransform)login.Resolve("PasswordRow")).gameObject.activeInHierarchy,Is.True);
            Assert.That(((Button)login.Resolve("ServerButton")).gameObject.activeInHierarchy,Is.False);
            ((Button)login.Resolve("LoginButton")).onClick.Invoke();
            float loginDeadline=Time.realtimeSinceStartup+5;
            while(!((Button)login.Resolve("ServerButton")).gameObject.activeInHierarchy&&Time.realtimeSinceStartup<loginDeadline)yield return null;
            Assert.That(((Button)login.Resolve("ServerButton")).gameObject.activeInHierarchy,Is.True);
            Assert.That(((RectTransform)login.Resolve("AccountRow")).gameObject.activeInHierarchy,Is.False);
            Assert.That(Object.FindObjectOfType<Client.Framework.Presentation.CityViewport>(),Is.Null,"Account login must not automatically enter the city.");
            yield return new WaitForEndOfFrame();
            var postLogin=ScreenCapture.CaptureScreenshotAsTexture();
            try{Directory.CreateDirectory("Builds/MainCity");File.WriteAllBytes("Builds/MainCity/login-ready-to-enter.png",postLogin.EncodeToPNG());}
            finally{Object.Destroy(postLogin);}
            ((Button)login.Resolve("LoginButton")).onClick.Invoke();
            float deadline=Time.realtimeSinceStartup+8;
            Client.Framework.Presentation.CityViewport city=null;
            while(city==null&&Time.realtimeSinceStartup<deadline)
            {
                city=Object.FindObjectOfType<Client.Framework.Presentation.CityViewport>();
                yield return null;
            }
            Assert.That(city,Is.Not.Null,"Entering after a successful local login must open the real main-city window.");
            Assert.That(city.IsReady,Is.True);
            Assert.That(city.WorldRoot.GetComponentsInChildren<MeshRenderer>().Length,Is.GreaterThan(10));
            Assert.That(city.ViewCamera.orthographic,Is.True);
            yield return new WaitForEndOfFrame();yield return new WaitForEndOfFrame();
            var screenshot=ScreenCapture.CaptureScreenshotAsTexture();
            try{Directory.CreateDirectory("Builds/MainCity");File.WriteAllBytes("Builds/MainCity/maincity-1080x1920.png",screenshot.EncodeToPNG());}
            finally{Object.Destroy(screenshot);}
            city.FocusBuilding(1);
            int clicks=0;city.BuildingSelected+=(id,name)=>{if(id==1)clicks++;};
            var mapRect=(RectTransform)city.transform;
            var palace=city.WorldRoot.transform.Find("MainCity/BuildingRoot/buildParent1");
            Assert.That(palace,Is.Not.Null);
            var palaceCenter=palace.GetComponentInChildren<MeshRenderer>().bounds.center;
            var palaceUv=city.ViewCamera.WorldToViewportPoint(palaceCenter);
            var point=RectTransformUtility.WorldToScreenPoint(null,mapRect.TransformPoint(new Vector3(mapRect.rect.xMin+palaceUv.x*mapRect.rect.width,mapRect.rect.yMin+palaceUv.y*mapRect.rect.height)));
            Physics.SyncTransforms();
            var click=new PointerEventData(EventSystem.current){pointerId=-1,position=point,button=PointerEventData.InputButton.Left};
            ExecuteEvents.Execute(city.gameObject,click,ExecuteEvents.pointerDownHandler);
            ExecuteEvents.Execute(city.gameObject,click,ExecuteEvents.pointerUpHandler);
            Assert.That(clicks,Is.EqualTo(1),"A pointer click must hit the migrated palace geometry.");
            var cityBindings=city.GetComponentInParent<UIViewBridge>().Bindings;
            Assert.That(((Text)cityBindings.Resolve("DetailName")).text,Does.Contain("城主府"));
            yield return new WaitForEndOfFrame();
            var selectedFrame=ScreenCapture.CaptureScreenshotAsTexture();
            try{File.WriteAllBytes("Builds/MainCity/maincity-building-selected.png",selectedFrame.EncodeToPNG());}
            finally{Object.Destroy(selectedFrame);}
            var world=city.WorldRoot;var target=city.ViewCamera.targetTexture;
            yield return Await(entry.ShutdownAsync(CancellationToken.None));entry=null;
            yield return null;yield return null;
            Assert.That(world==null,Is.True);Assert.That(target==null,Is.True);
        }
        [UnityTest, Category("CityOutskirts")]
        public IEnumerator OutskirtsUsesTheBeginnerMapAndRestoresTheMainCity()
        {
            ClosePresenterForEntry();
            var transitionUI = new DelayedOutskirtsUI(service);
            var context = new EntryContext(transitionUI, session);
            entry = (IHotfixEntry)Activator.CreateInstance(Type.GetType("Client.Game.Entry, Client.Game", true));
            yield return Await(entry.InitializeAsync(context, CancellationToken.None));
            var login = ui.GetAllLoadedUIForms().Single().GetComponent<UIBindings>();
            var action = (Button)login.Resolve("LoginButton");
            action.onClick.Invoke();
            float deadline = Time.realtimeSinceStartup + 5;
            while (!((Button)login.Resolve("ServerButton")).gameObject.activeInHierarchy && Time.realtimeSinceStartup < deadline)
                yield return null;
            Assert.That(((Button)login.Resolve("ServerButton")).gameObject.activeInHierarchy, Is.True);
            action.onClick.Invoke();
            GameObject cityRoot = null;
            deadline = Time.realtimeSinceStartup + 5;
            while (cityRoot == null && Time.realtimeSinceStartup < deadline)
            {
                cityRoot = ui.GetAllLoadedUIForms().FirstOrDefault(form => form.name.StartsWith("UIMainCity", StringComparison.Ordinal))?.gameObject;
                yield return null;
            }
            Assert.That(cityRoot, Is.Not.Null);
            var cityMap = (Client.Framework.Presentation.CityViewport)cityRoot.GetComponent<UIBindings>().Resolve("Map");
            var saved = cityMap.CaptureView();
            cityRoot.transform.Find("UINode/SafeArea/Static/OutskirtsButton").GetComponent<Button>().onClick.Invoke();
            Assert.That(transitionUI.OpenRequested, Is.True);
            for (int frame = 0; frame < 6; frame++)
            {
                Assert.That(cityRoot.transform.Find("UINode").gameObject.activeInHierarchy, Is.True,
                    "The main city must cover login while outskirts is still loading.");
                Assert.That(cityMap.IsReady, Is.True);
                yield return null;
            }
            // Preserve the last displayed city view, after layout has settled during the delayed load.
            saved = cityMap.CaptureView();
            transitionUI.Release();
            UIForm mapForm = null;
            deadline = Time.realtimeSinceStartup + 5;
            while ((mapForm == null || cityMap.IsReady) && Time.realtimeSinceStartup < deadline)
            {
                mapForm = ui.GetAllLoadedUIForms().FirstOrDefault(form => form.name.StartsWith("UICityOutskirts", StringComparison.Ordinal));
                yield return null;
            }
            Assert.That(mapForm, Is.Not.Null);
            Assert.That(cityMap.IsReady, Is.False);
            var bindings = mapForm.GetComponent<UIBindings>();
            var map = (Client.Framework.Presentation.CityViewport)bindings.Resolve("Map");
            Assert.That(map.IsReady, Is.True);
            Assert.That(map.WorldRoot.transform.Find("BeginnerOutskirts/BuildingRoot").childCount, Is.EqualTo(45));
            var area = map.WorldRoot.transform.Find("BeginnerOutskirts/BuildingRoot");
            AssertNativeModelHeight(area.Find("Tile_20/ReferenceHome"),
                AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Content/Prefabs/CityOutskirts/ReferenceHome.prefab"),
                map.WorldRoot.transform.position.y);
            Assert.That(area.Find("Tile_9").localPosition.z, Is.GreaterThan(area.Find("Tile_5").localPosition.z));
            Assert.That(area.Find("Tile_10").localPosition.x, Is.EqualTo(1f).Within(.001f));
            Assert.That(area.Find("Tile_37").localPosition.x, Is.EqualTo(4f).Within(.001f));
            Assert.That(area.Find("Tile_17/LandLevel").GetComponent<TextMesh>().text, Is.EqualTo("6"));
            for (int position = 1; position <= Client.Framework.Presentation.OutskirtsHex.Count; position++)
                Assert.That(Client.Framework.Presentation.OutskirtsHex.Pick(
                    Client.Framework.Presentation.OutskirtsHex.Center(position)), Is.EqualTo(position));
            Assert.That(Client.Framework.Presentation.OutskirtsHex.Pick(Vector3.left * 2), Is.Zero);
            Assert.That(context.ResourcesImpl.Live, Is.Zero);
            Assert.That(map.ViewCamera.orthographic, Is.False);
            Assert.That(map.ViewCamera.fieldOfView, Is.EqualTo(29.5f).Within(.001f));
            Assert.That(map.ViewCamera.allowHDR, Is.True);
            Assert.That(map.ViewCamera.GetComponent<Client.Framework.Presentation.OutskirtsPostProcess>(), Is.Not.Null);
            yield return null;
            var mapState = map.CaptureView();
            var inputRect = (RectTransform)map.transform;
            Func<Vector2, Vector2> screenForUv = value => RectTransformUtility.WorldToScreenPoint(null,
                inputRect.TransformPoint(new Vector3(inputRect.rect.xMin + value.x * inputRect.rect.width,
                    inputRect.rect.yMin + value.y * inputRect.rect.height)));
            Func<Vector3, Vector2> uvForWorld = value =>
            {
                var projected = map.ViewCamera.WorldToViewportPoint(value);
                return new Vector2(projected.x, projected.y);
            };
            var anchoredGround = map.WorldRoot.transform.TransformPoint(Client.Framework.Presentation.OutskirtsHex.Center(20));
            var startUv = uvForWorld(anchoredGround);
            var endUv = new Vector2(.55f, .53f);
            var dragEvent = new PointerEventData(EventSystem.current)
            { pointerId = -1, position = screenForUv(startUv), button = PointerEventData.InputButton.Left };
            ExecuteEvents.Execute(map.gameObject, dragEvent, ExecuteEvents.pointerDownHandler);
            dragEvent.position = screenForUv(endUv);
            ExecuteEvents.Execute(map.gameObject, dragEvent, ExecuteEvents.dragHandler);
            ExecuteEvents.Execute(map.gameObject, dragEvent, ExecuteEvents.pointerUpHandler);
            Assert.That(Vector2.Distance(uvForWorld(anchoredGround), endUv), Is.LessThan(.001f), "Dragging must keep the grabbed terrain under the pointer.");
            map.ApplyView(mapState);
            anchoredGround = map.WorldRoot.transform.TransformPoint(Client.Framework.Presentation.OutskirtsHex.Center(20) + new Vector3(.5f, 0, .3f));
            var zoomUv = uvForWorld(anchoredGround);
            var scrollEvent = new PointerEventData(EventSystem.current)
            { position = screenForUv(zoomUv), scrollDelta = Vector2.up };
            ExecuteEvents.Execute(map.gameObject, scrollEvent, ExecuteEvents.scrollHandler);
            Assert.That(map.CaptureView().OrthographicSize, Is.LessThan(mapState.OrthographicSize));
            Assert.That(Vector2.Distance(uvForWorld(anchoredGround), zoomUv), Is.LessThan(.001f), "Zooming must preserve its ground anchor.");
            map.ApplyView(mapState);
            yield return new WaitForEndOfFrame();
            Directory.CreateDirectory("Builds/CityOutskirts");
            var near = ScreenCapture.CaptureScreenshotAsTexture();
            try { File.WriteAllBytes("Builds/CityOutskirts/near.png", near.EncodeToPNG()); }
            finally { Object.Destroy(near); }
            var phaseNames = new[] { "dawn", "day", "dusk", "night" };
            var phaseHours = new[] { 9, 13, 17, 21 };
            for (int phaseIndex = 0; phaseIndex < phaseHours.Length; phaseIndex++)
            {
                map.SetClock(new OutskirtsTestClock
                {
                    GameTime = new DateTimeOffset(2026, 11, 7, phaseHours[phaseIndex], 0, 0, TimeSpan.FromHours(8))
                });
                yield return new WaitForEndOfFrame();
                var phaseImage = ScreenCapture.CaptureScreenshotAsTexture();
                try { File.WriteAllBytes("Builds/CityOutskirts/" + phaseNames[phaseIndex] + ".png", phaseImage.EncodeToPNG()); }
                finally { Object.Destroy(phaseImage); }
            }
            map.SetClock(new Client.Framework.Presentation.LocalOutskirtsClock());
            ((Button)bindings.Resolve("Overview")).onClick.Invoke();
            for (int id = 1; id <= Client.Framework.Presentation.OutskirtsHex.Count; id++)
            for (int edge = 0; edge < 6; edge++)
            {
                float angle = (30 + edge * 60) * Mathf.Deg2Rad;
                var corner = Client.Framework.Presentation.OutskirtsHex.Center(id) +
                    new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle)) * Client.Framework.Presentation.OutskirtsHex.Radius;
                var projected = map.ViewCamera.WorldToViewportPoint(map.WorldRoot.transform.TransformPoint(corner));
                Assert.That(projected.z, Is.GreaterThan(0));
                Assert.That(projected.x, Is.InRange(.001f, .999f), "Overview must include every hex corner.");
                Assert.That(projected.y, Is.InRange(.001f, .999f), "Overview must include every hex corner.");
            }
            var rect = (RectTransform)map.transform;
            var town = Client.Framework.Presentation.OutskirtsHex.Center(34);
            var uv = map.ViewCamera.WorldToViewportPoint(map.WorldRoot.transform.TransformPoint(town));
            var point = RectTransformUtility.WorldToScreenPoint(null, rect.TransformPoint(
                new Vector3(rect.rect.xMin + uv.x * rect.rect.width, rect.rect.yMin + uv.y * rect.rect.height)));
            var click = new PointerEventData(EventSystem.current) { pointerId = -1, position = point, button = PointerEventData.InputButton.Left };
            ExecuteEvents.Execute(map.gameObject, click, ExecuteEvents.pointerDownHandler);
            ExecuteEvents.Execute(map.gameObject, click, ExecuteEvents.pointerUpHandler);
            Assert.That(((Text)bindings.Resolve("Detail")).text, Does.Contain("县城").And.Contain("34"));
            uv = map.ViewCamera.WorldToViewportPoint(map.WorldRoot.transform.TransformPoint(Client.Framework.Presentation.OutskirtsHex.Center(17)));
            point = RectTransformUtility.WorldToScreenPoint(null, rect.TransformPoint(
                new Vector3(rect.rect.xMin + uv.x * rect.rect.width, rect.rect.yMin + uv.y * rect.rect.height)));
            click.position = point;
            ExecuteEvents.Execute(map.gameObject, click, ExecuteEvents.pointerDownHandler);
            ExecuteEvents.Execute(map.gameObject, click, ExecuteEvents.pointerUpHandler);
            Assert.That(((Text)bindings.Resolve("Detail")).text, Does.Contain("石料地").And.Contain("第 17 格").And.Contain("等级 6"));
            yield return new WaitForEndOfFrame();
            var screenshot = ScreenCapture.CaptureScreenshotAsTexture();
            try
            {
                Directory.CreateDirectory("Builds/CityOutskirts");
                File.WriteAllBytes("Builds/CityOutskirts/overview.png", screenshot.EncodeToPNG());
            }
            finally { Object.Destroy(screenshot); }
            ((Button)bindings.Resolve("Back")).onClick.Invoke();
            yield return null;
            Assert.That(map.IsReady, Is.False);
            Assert.That(cityMap.IsReady, Is.True);
            Assert.That(cityMap.CaptureView().OrthographicSize, Is.EqualTo(saved.OrthographicSize).Within(.02f));
            Assert.That(session.Calls, Is.Zero, "Local navigation must not use the host session.");
        }

        [UnityTest] public IEnumerator MeasuredKeyboardLiftsTheAccountControlsAboveOcclusion()
        {
            var bridge=view.Root.GetComponent<UIViewBridge>();
            var table=view.Root.GetComponent<UIBindings>();
            Assert.That(((RectTransform)table.Resolve("AccountRow")).gameObject.activeInHierarchy,Is.True);
            var input=(RectTransform)Control<InputField>("AccountInput").transform;
            var primary=(RectTransform)Control<Button>("LoginButton").transform;
            var scroll=bridge.Layout.GetComponentInChildren<ScrollRect>();
            bool visible=false;Rect keyboard=Rect.zero;
            bridge.Keyboard.KeyboardVisibleSource=()=>visible;
            bridge.Keyboard.KeyboardAreaSource=()=>keyboard;
            bridge.Keyboard.FocusSource=()=>input;
            foreach(var size in new[]{new Vector2Int(720,1280),new Vector2Int(1080,1920)})
            {
                var metrics=new ScreenMetrics(size.x,size.y,new Rect(0,40,size.x,size.y-80));
                var screen=root.GetComponent<ScreenLayoutComponent>().Service;
                screen.Evaluate(metrics);screen.FlushPending(Time.frameCount);
                Canvas.ForceUpdateCanvases();yield return null;
                var rootRect=(RectTransform)root.transform;
                var baseline=RectInRoot(scroll.viewport,rootRect,metrics);
                visible=true;keyboard=new Rect(0,0,size.x,size.y*.4f);
                bridge.Keyboard.Apply(screen.Current,keyboard,input);
                Canvas.ForceUpdateCanvases();yield return null;
                var lifted=RectInRoot(scroll.viewport,rootRect,metrics);
                Assert.That(lifted.yMin,Is.GreaterThanOrEqualTo(keyboard.yMax-.5f),"A measured keyboard must lift a fully covered form above its top edge.");
                Assert.That(lifted.height,Is.GreaterThan(100),"The form must retain a usable viewport, not collapse to one pixel.");
                foreach(var target in new[]{input,primary})
                {
                    var bounds=RectTransformUtility.CalculateRelativeRectTransformBounds(scroll.viewport,target);
                    Assert.That(bounds.min.y,Is.GreaterThanOrEqualTo(scroll.viewport.rect.yMin-.5f));
                    Assert.That(bounds.max.y,Is.LessThanOrEqualTo(scroll.viewport.rect.yMax+.5f));
                }
                visible=false;keyboard=Rect.zero;bridge.Keyboard.Apply(screen.Current,keyboard,null);
                Canvas.ForceUpdateCanvases();
                var restored=RectInRoot(scroll.viewport,rootRect,metrics);
                Assert.That(restored.yMin,Is.EqualTo(baseline.yMin).Within(.5f));
                Assert.That(restored.height,Is.EqualTo(baseline.height).Within(.5f));
            }
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
        [UnityTest] public IEnumerator UnknownKeyboardAllowsTheRealAccountActionAboveOcclusion()
        {
            var bridge=view.Root.GetComponent<UIViewBridge>();
            Assert.That(Control<InputField>("AccountInput").gameObject.activeInHierarchy,Is.True);
            var input=(RectTransform)Control<InputField>("AccountInput").transform;
            var action=(RectTransform)Control<Button>("LoginButton").transform;
            var scroll=bridge.Layout.GetComponentInChildren<ScrollRect>();
            var metrics=new ScreenMetrics(720,1280,new Rect(0,40,720,1200));
            var screen=root.GetComponent<ScreenLayoutComponent>().Service;
            screen.Evaluate(metrics);screen.FlushPending(Time.frameCount);
            Canvas.ForceUpdateCanvases();yield return null;
            var rootRect=(RectTransform)root.transform;
            var baseline=RectInRoot(scroll.viewport,rootRect,metrics);
            bool visible=true;bridge.Keyboard.KeyboardVisibleSource=()=>visible;
            bridge.Keyboard.KeyboardAreaSource=()=>Rect.zero;
            bridge.Keyboard.FocusSource=()=>input;
            bridge.Keyboard.Apply(screen.Current,Rect.zero,input);
            scroll.OnScroll(new PointerEventData(EventSystem.current){scrollDelta=new Vector2(0,-1000)});
            bridge.Keyboard.Apply(screen.Current,Rect.zero,input);
            Canvas.ForceUpdateCanvases();yield return null;
            var pixels=RectInRoot(action,rootRect,metrics);
            Assert.That(pixels.yMin,Is.GreaterThanOrEqualTo(704f),"Manual travel must allow the real action above a test-only lower-screen occlusion.");
            Assert.That(pixels.yMax,Is.LessThanOrEqualTo(1240f));
            visible=false;bridge.Keyboard.Apply(screen.Current,Rect.zero,null);Canvas.ForceUpdateCanvases();
            var restored=RectInRoot(scroll.viewport,rootRect,metrics);
            Assert.That(restored.yMin,Is.EqualTo(baseline.yMin).Within(.5f));
            Assert.That(restored.height,Is.EqualTo(baseline.height).Within(.5f));
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
                var buttonRect=(RectTransform)((Button)table.Resolve("LoginButton")).transform;
                var corners=new Vector3[4];buttonRect.GetWorldCorners(corners);
                var minimum=RectTransformUtility.WorldToScreenPoint(null,corners[0]);
                var maximum=RectTransformUtility.WorldToScreenPoint(null,corners[2]);
                int red=0;
                for(int y=Mathf.Max(0,(int)minimum.y);y<Mathf.Min(pixels.height,(int)maximum.y);y+=2)
                for(int x=Mathf.Max(0,(int)minimum.x);x<Mathf.Min(pixels.width,(int)maximum.x);x+=2)
                {var pixel=pixels.GetPixel(x,y);if(pixel.r>pixel.g+.12f&&pixel.r>pixel.b+.12f)red++;}
                Assert.That(red,Is.GreaterThan(200),"Actual Game-view pixels must contain the bound vermilion login action.");
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
        static void AssertNativeModelHeight(Transform wrapper, GameObject nativePrefab, float mapOriginY)
        {
            var expected = nativePrefab.GetComponentsInChildren<MeshRenderer>(true)
                .Where(renderer => renderer.sharedMaterials.Any(material => material != null &&
                    material.HasProperty("_HeightLightEnabled") && material.GetFloat("_HeightLightEnabled") > .5f))
                .OrderByDescending(renderer => renderer.bounds.max.y).First();
            string path = AnimationUtility.CalculateTransformPath(expected.transform, nativePrefab.transform);
            var visual = wrapper.GetChild(0);
            var actual = (string.IsNullOrEmpty(path) ? visual : visual.Find(path)).GetComponent<MeshRenderer>();
            var properties = new MaterialPropertyBlock();
            actual.GetPropertyBlock(properties);
            float reconstructed = (actual.bounds.max.y - mapOriginY) * properties.GetFloat("_NativeHeightScale") +
                properties.GetFloat("_NativeHeightOffset");
            Assert.That(reconstructed, Is.EqualTo(expected.bounds.max.y).Within(.02f),
                "Height lighting must retain native model height after fitting and offscreen-scene isolation.");
        }
        sealed class DelayedOutskirtsUI : IUIService
        {
            private readonly IUIService inner;
            private readonly TaskCompletionSource<bool> gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            public bool OpenRequested { get; private set; }
            public DelayedOutskirtsUI(IUIService inner) => this.inner = inner;
            public void Release() => gate.TrySetResult(true);
            public async Task<IViewHandle> OpenAsync(string viewId, CancellationToken token)
            {
                if (viewId == "UICityOutskirts")
                {
                    OpenRequested = true;
                    using (token.Register(() => gate.TrySetCanceled(token))) await gate.Task;
                    token.ThrowIfCancellationRequested();
                }
                return await inner.OpenAsync(viewId, token);
            }
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
