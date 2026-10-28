#if UNITY_EDITOR
using System;
using System.Collections;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Client.Framework.UI;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using System.IO;
using UnityEngine.TestTools;
using UnityEngine.UI;
using UnityGameFramework.Runtime;
using Object = UnityEngine.Object;

namespace Client.Tests
{
    [Category("U3")]
    public sealed class UILifecycleTests
    {
        private const string PrefabPath = "Assets/Tests/U3WindowFixture.prefab";
        private GameObject host, root;
        private object service, catalog;
        private UIComponent ui;
        private EventComponent events;
        private ScreenLayoutComponent screen;

        internal static Type Required(string name)
        {
            var type = Type.GetType("Client.Framework.UI." + name + ", Client.Framework");
            Assert.That(type, Is.Not.Null, "U3 requires " + name + " to host native GF window lifetimes.");
            return type;
        }
        internal static object Call(object target, string method, params object[] args) => target.GetType().GetMethod(method).Invoke(target, args);
        internal static T Property<T>(object target, string name) => (T)target.GetType().GetProperty(name).GetValue(target);
        internal static void Field(object target, string name, object value) => target.GetType().GetField(name).SetValue(target, value);

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            Required("UIService"); Required("UIViewBridge"); Required("UIBindings");
            GameEntry.Shutdown(ShutdownType.None);
            yield return null;
            var prefab = new GameObject("UIWindow", typeof(RectTransform));
            var node = new GameObject("UINode", typeof(RectTransform), typeof(Canvas), typeof(CanvasGroup), typeof(GraphicRaycaster));
            node.transform.SetParent(prefab.transform,false); Stretch((RectTransform)node.transform);
            var button = new GameObject("Action", typeof(RectTransform), typeof(Image), typeof(Button));
            var background=AdaptiveLayoutTests.Child("Background",node.transform);Stretch(background);background.gameObject.AddComponent<Image>().color=Color.red;background.GetComponent<Image>().raycastTarget=false;
            var localSafe=AdaptiveLayoutTests.Child("SafeArea",node.transform);Stretch(localSafe);localSafe.gameObject.AddComponent<SafeAreaRoot>();
            var brand = AdaptiveLayoutTests.Child("Brand", localSafe);
            var viewport = AdaptiveLayoutTests.Child("Viewport", localSafe);
            var content = AdaptiveLayoutTests.Child("Content", viewport);
            button.transform.SetParent(content, false);
            var action = (RectTransform)button.transform;
            action.anchorMin = action.anchorMax = new Vector2(.5f, 0); action.pivot = new Vector2(.5f, 0); action.sizeDelta = new Vector2(300, 96);
            var scroll = viewport.gameObject.AddComponent<ScrollRect>(); scroll.viewport = viewport; scroll.content = content;
            scroll.inertia = false; scroll.scrollSensitivity = 100;
            var layout = localSafe.gameObject.AddComponent<AdaptiveLayout>();
            layout.Configure(AssetDatabase.LoadAssetAtPath<AdaptiveLayoutDefinition>("Assets/Content/UI/Login/UILogin.Layout.asset"), brand, viewport, scroll, content, action);
            var keyboard = localSafe.gameObject.AddComponent<KeyboardVisibilityController>(); keyboard.Configure(scroll, action);
            var mask = new GameObject("Mask", typeof(RectTransform), typeof(Image), typeof(Button));
            mask.GetComponent<Image>().color=Color.clear; mask.transform.SetParent(node.transform, false); mask.transform.SetSiblingIndex(1); Stretch((RectTransform)mask.transform);
            var bindings = prefab.AddComponent(Required("UIBindings"));
            var entry = Activator.CreateInstance(Required("UIBindings").GetNestedType("Binding"));
            Field(entry, "Id", "Action"); Field(entry, "Value", button.GetComponent<Button>());
            Field(entry, "ComponentType", typeof(Button).AssemblyQualifiedName);
            var entries = Array.CreateInstance(entry.GetType(), 1); entries.SetValue(entry, 0);
            Call(bindings, "Configure", entries, null);
            Call(prefab.AddComponent(Required("UIViewBridge")), "Configure", node.GetComponent<Canvas>(), node.GetComponent<CanvasGroup>(), bindings, layout, keyboard, mask.GetComponent<Button>(), localSafe.GetComponent<SafeAreaRoot>());
            PrefabUtility.SaveAsPrefabAsset(prefab, PrefabPath); Object.DestroyImmediate(prefab);

            root = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Content/UI/Common/AdaptiveUIRoot.prefab"));
            screen = root.GetComponent<ScreenLayoutComponent>(); screen.enabled = false;
            host = new GameObject("U3 native GF host"); host.SetActive(false);
            host.AddComponent<BaseComponent>(); events = host.AddComponent<EventComponent>();
            var editorResource = host.AddComponent<EditorResourceComponent>();
            SetPrivate(editorResource, "m_MinLoadAssetRandomDelaySeconds", .05f);
            SetPrivate(editorResource, "m_MaxLoadAssetRandomDelaySeconds", .05f);
            host.AddComponent<ResourceComponent>(); host.AddComponent<ObjectPoolComponent>();
            ui = host.AddComponent<UIComponent>();
            SetPrivate(ui, "m_UIFormHelperTypeName", typeof(U3ObservedUIFormHelper).FullName);
            SetPrivate(ui, "m_UIGroups", Array.CreateInstance(typeof(UIComponent).GetNestedType("UIGroup", BindingFlags.NonPublic), 0));
            U3ObservedUIFormHelper.Releases = U3ObservedUIFormHelper.Instances = 0;
            U3ObservedUIFormHelper.LastReleasedInstance = null;
            host.SetActive(true); yield return null;
            catalog = ScriptableObject.CreateInstance(Required("UIViewDefinitions"));
            var definitions = Array.CreateInstance(Required("UIViewDefinition"), 3);
            definitions.SetValue(Definition("Page", "Page", false, false), 0);
            definitions.SetValue(Definition("Popup", "Popup", true, true), 1);
            definitions.SetValue(Definition("Overlay", "System", false, false), 2);
            Call(catalog, "Configure", definitions);
            service = Activator.CreateInstance(Required("UIService"), ui, events, catalog, root.GetComponent<AdaptiveCanvas>(), root.transform.Find("Windows"));
        }

        private static object Definition(string id, string group, bool modal, bool closeOnMask)
        {
            var value = Activator.CreateInstance(Required("UIViewDefinition"));
            Field(value, "ViewID", id); Field(value, "PrefabAddress", PrefabPath); Field(value, "Group", group);
            Field(value, "LayoutDefinition", AssetDatabase.LoadAssetAtPath<AdaptiveLayoutDefinition>("Assets/Content/UI/Login/UILogin.Layout.asset"));
            Field(value, "Modal", modal); Field(value, "CloseOnMask", closeOnMask); return value;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            (service as IDisposable)?.Dispose(); service = null;
            GameEntry.Shutdown(ShutdownType.None);
            if (host != null) Object.Destroy(host); if (root != null) Object.Destroy(root);
            if (catalog is Object asset) Object.Destroy(asset);
            yield return null;
            AssetDatabase.DeleteAsset(PrefabPath);
        }

        private Task Open(string id = "Page", CancellationToken token = default) => (Task)Call(service, "OpenAsync", id, token);
        private static object Result(Task task) { Assert.That(task.IsCompletedSuccessfully, Is.True, task.Exception?.ToString()); return task.GetType().GetProperty("Result").GetValue(task); }
        private static GameObject Window(object handle) => Property<GameObject>(handle, "Root");
        private static Button ActionButton(GameObject window) => (Button)Call(window.GetComponent(Required("UIViewBridge")), "Resolve", "Action");
        private static RectTransform Node(GameObject window) => (RectTransform)window.transform.Find("UINode");
        private static Canvas WindowCanvas(object handle) => Node(Window(handle)).GetComponent<Canvas>();
        private static CanvasGroup WindowGroup(object handle) => Node(Window(handle)).GetComponent<CanvasGroup>();
        private static void Stretch(RectTransform rect) { rect.anchorMin=Vector2.zero; rect.anchorMax=Vector2.one; rect.offsetMin=rect.offsetMax=Vector2.zero; }
        private static void AssertFills(RectTransform child,RectTransform parent)
        {
            var corners=new Vector3[4]; child.GetWorldCorners(corners);
            var min=parent.InverseTransformPoint(corners[0]); var max=parent.InverseTransformPoint(corners[2]);
            Assert.That(min.x,Is.EqualTo(parent.rect.xMin).Within(.02f)); Assert.That(min.y,Is.EqualTo(parent.rect.yMin).Within(.02f));
            Assert.That(max.x,Is.EqualTo(parent.rect.xMax).Within(.02f)); Assert.That(max.y,Is.EqualTo(parent.rect.yMax).Within(.02f));
        }
        private void AssertMountedHierarchy(object handle)
        {
            var window=Window(handle); var bridge=(UIViewBridge)window.GetComponent(Required("UIViewBridge")); var node=Node(window);
            Assert.That(window.GetComponent<Canvas>(),Is.Null,"Stable GF window root must not own Canvas.");
            Assert.That(window.GetComponent<CanvasGroup>(),Is.Null);
            Assert.That(bridge.Bindings,Is.SameAs(window.GetComponent<UIBindings>()));
            Assert.That(bridge.RootCanvas,Is.SameAs(node.GetComponent<Canvas>()));
            Assert.That(bridge.RootCanvasGroup,Is.SameAs(node.GetComponent<CanvasGroup>()));
            Assert.That(node.GetComponent<GraphicRaycaster>(),Is.Not.Null);
            Assert.That(window.GetComponentsInChildren<CanvasScaler>(true),Is.Empty);
            Assert.That(window.transform.parent.parent,Is.EqualTo(root.transform.Find("Windows")));
            Assert.That(bridge.RootCanvas.rootCanvas,Is.SameAs(root.GetComponent<Canvas>()));
            AssertFills((RectTransform)window.transform,(RectTransform)window.transform.parent);
            AssertFills(node,(RectTransform)window.transform);
            AssertFills((RectTransform)node.Find("Mask"),node);
        }
        private static IEnumerator Await(Task task)
        {
            float deadline = Time.realtimeSinceStartup + 10;
            while (!task.IsCompleted && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.That(task.IsCompleted, Is.True, "Native GF open timed out.");
        }
        private static void SetPrivate(object value, string name, object field) => value.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(value, field);

        // Removing close cleanup causes both retained callbacks and a non-baseline native listener count.
        [UnityTest]
        public IEnumerator OpenCloseReuseHasOneSubscription()
        {
            int baseline = events.EventHandlerCount, clicks = 0, firstInstance = 0;
            for (int i = 0; i < 20; i++)
            {
                var task = Open(); yield return Await(task); var handle = Result(task);
                var window = Window(handle);
                AssertMountedHierarchy(handle);
                if (i == 0) firstInstance = window.GetInstanceID();
                else Assert.That(window.GetInstanceID(), Is.EqualTo(firstInstance), "Native GF pool should reuse the window.");
                Call(handle, "Listen", "Action", (UnityAction)(() => clicks++));
                var button = ActionButton(window); button.onClick.Invoke();
                Assert.That(clicks, Is.EqualTo(i + 1));
                Call(handle, "Close"); Call(handle, "Close"); button.onClick.Invoke();
                Assert.That(clicks, Is.EqualTo(i + 1), "Closed window must release the presenter listener.");
                Assert.That(events.EventHandlerCount, Is.EqualTo(baseline));
                yield return null;
            }
            Assert.That(U3ObservedUIFormHelper.Instances, Is.EqualTo(1), "No duplicate prefab load/instantiate route.");
        }

        // Missing native close-on-cancellation leaves a late-loaded active or pooled reference behind.
        [UnityTest]
        public IEnumerator CancelledOpenReleasesLateAsset()
        {
            int baseline = events.EventHandlerCount;
            using (var cancellation = new CancellationTokenSource())
            {
                var task = Open("Page", cancellation.Token);
                Assert.That(ui.GetAllLoadingUIFormSerialIds().Length, Is.EqualTo(1));
                cancellation.Cancel(); yield return Await(task);
                Assert.That(task.IsCanceled, Is.True);
                float deadline = Time.realtimeSinceStartup + 10;
                while ((ui.GetAllLoadingUIFormSerialIds().Length > 0 || U3ObservedUIFormHelper.Releases == 0) && Time.realtimeSinceStartup < deadline) yield return null;
                Assert.That(ui.GetAllLoadedUIForms(), Is.Empty);
                Assert.That(U3ObservedUIFormHelper.Releases, Is.EqualTo(1), "Late resource must pass through the native default helper release.");
                yield return null;
                Assert.That(U3ObservedUIFormHelper.LastReleasedInstance == null,Is.True,"Default native release must destroy the late-loaded window instance.");
                Assert.That(events.EventHandlerCount, Is.EqualTo(baseline));
            }
        }

        [UnityTest]
        public IEnumerator CancelledQueuedSuccessCannotCloseReusedWindow()
        {
            var manager = GameFramework.GameFrameworkEntry.GetModule<GameFramework.UI.IUIManager>();
            using (var cancellation = new CancellationTokenSource())
            {
                EventHandler<GameFramework.UI.OpenUIFormSuccessEventArgs> cancelAtNativeOpen = (sender, args) => cancellation.Cancel();
                manager.OpenUIFormSuccess += cancelAtNativeOpen;
                var cancelled = Open("Page", cancellation.Token);
                yield return Await(cancelled);
                manager.OpenUIFormSuccess -= cancelAtNativeOpen;
                Assert.That(cancelled.IsCanceled, Is.True);
                var replacement = Open(); yield return Await(replacement);
                var current = Result(replacement); yield return null;
                Assert.That(Property<bool>(current,"IsClosed"),Is.False);
                Assert.That(ui.GetAllLoadedUIForms().Length,Is.EqualTo(1));
            }
        }

        // A stale handle must neither deliver a callback nor close a newly reused GF serial generation.
        [UnityTest]
        public IEnumerator ClosedWindowIgnoresLateCallback()
        {
            var opening = Open(); yield return Await(opening); var old = Result(opening); int callbacks = 0;
            var guarded = (Action)Call(old, "Guard", (Action)(() => callbacks++));
            Call(old, "Close"); yield return null;
            opening = Open(); yield return Await(opening); var current = Result(opening);
            guarded(); Call(old, "Close");
            Assert.That(callbacks, Is.Zero); Assert.That(Property<bool>(current, "IsClosed"), Is.False);
            Assert.That(ui.GetAllLoadedUIForms().Length, Is.EqualTo(1));
            Assert.That(Property<CancellationToken>(old, "Lifetime").IsCancellationRequested, Is.True);
        }

        // Removing modal recomputation admits page input or leaves the page blocked after top-popup close.
        [UnityTest]
        public IEnumerator PopupOrderingAndInputBlockRemainCorrect()
        {
            var t = Open(); yield return Await(t); var page = Result(t);
            t = Open("Popup"); yield return Await(t); var lower = Result(t);
            t = Open("Popup"); yield return Await(t); var upper = Result(t);
            AssertMountedHierarchy(page); AssertMountedHierarchy(lower); AssertMountedHierarchy(upper);
            Assert.That(WindowCanvas(upper).sortingOrder, Is.GreaterThan(WindowCanvas(lower).sortingOrder));
            Assert.That(WindowCanvas(lower).sortingOrder, Is.GreaterThan(WindowCanvas(page).sortingOrder));
            Assert.That(WindowGroup(page).blocksRaycasts, Is.False);
            Assert.That(WindowGroup(lower).interactable, Is.False);
            Assert.That(WindowGroup(upper).blocksRaycasts, Is.True);
            Assert.That(ActionButton(Window(page)).IsInteractable(),Is.False,"Child CanvasGroup must block actual descendant Selectables.");
            Assert.That(ActionButton(Window(lower)).IsInteractable(),Is.False);
            Assert.That(ActionButton(Window(upper)).IsInteractable(),Is.True);
            var mask=Node(Window(upper)).Find("Mask").GetComponent<Button>();
            var point=RectTransformUtility.WorldToScreenPoint(null,mask.transform.position);
            Assert.That(RectTransformUtility.RectangleContainsScreenPoint((RectTransform)mask.transform,point),Is.True);
            Assert.That(mask.GetComponent<Image>().Raycast(point,null),Is.True,"Top modal mask must pass real Graphic canvas-group filtering.");
            Assert.That(ActionButton(Window(page)).GetComponent<Image>().Raycast(point,null),Is.False,"Covered page graphics must fail the child CanvasGroup raycast filter.");
            mask.onClick.Invoke();
            Assert.That(Property<bool>(upper, "IsClosed"), Is.True);
            Assert.That(WindowGroup(lower).interactable, Is.True); Assert.That(ActionButton(Window(lower)).IsInteractable(),Is.True);
            Call(lower, "Close"); Assert.That(WindowGroup(page).blocksRaycasts, Is.True); Assert.That(ActionButton(Window(page)).IsInteractable(),Is.True);
            t = Open("Overlay"); yield return Await(t); var overlay = Result(t);
            Assert.That(WindowCanvas(overlay).sortingOrder, Is.GreaterThan(WindowCanvas(page).sortingOrder));
        }

        [UnityTest]
        public IEnumerator NativeCloseInvalidatesHandleAndDisposalRestoresListenerBaseline()
        {
            int baseline = events.EventHandlerCount;
            var t = Open(); yield return Await(t); var handle = Result(t); int clicks = 0;
            var button = ActionButton(Window(handle));
            Call(handle, "Listen", "Action", (UnityAction)(() => clicks++));
            ui.CloseUIForm(Window(handle).GetComponent<UIForm>());
            button.onClick.Invoke(); Assert.That(clicks, Is.Zero); Assert.That(Property<bool>(handle, "IsClosed"), Is.True);
            ((IDisposable)service).Dispose(); Assert.That(events.EventHandlerCount, Is.EqualTo(baseline));
        }

        [UnityTest]
        public IEnumerator SharedSnapshotsApplyInitiallyAndToMultipleWindows()
        {
            Assert.That(screen.Service.Current, Is.Null);
            var t = Open(); yield return Await(t); var first = Result(t);
            screen.Service.Evaluate(new ScreenMetrics(1280, 720, new Rect(10, 20, 1240, 680))); screen.Service.FlushPending(Time.frameCount);
            t = Open(); yield return Await(t); var second = Result(t);
            Assert.That(root.GetComponent<CanvasScaler>().referenceResolution, Is.EqualTo(new Vector2(1920,1080)));
            foreach (var handle in new[] { first, second })
            {
                AssertMountedHierarchy(handle);
                var brand = Node(Window(handle)).Find("SafeArea/Brand") as RectTransform;
                Assert.That(brand.anchorMax, Is.EqualTo(new Vector2(.35f, 1)), "Each open window must consume the already-published landscape snapshot.");
                var keyboard = Node(Window(handle)).GetComponentInChildren<KeyboardVisibilityController>();
                keyboard.KeyboardVisibleSource = () => true; keyboard.KeyboardAreaSource = () => Rect.zero;
            }
            yield return null;
            foreach (var handle in new[] { first, second })
            {
                var scroll = Window(handle).GetComponentInChildren<ScrollRect>();
                KeyboardLayoutTests.ScrollUp(scroll); yield return null;
                var bounds = RectTransformUtility.CalculateRelativeRectTransformBounds(scroll.viewport, ActionButton(Window(handle)).transform);
                Assert.That(bounds.min.y, Is.GreaterThanOrEqualTo(scroll.viewport.rect.center.y - 1), "Every window retains U2's bounded manual keyboard fallback.");
            }
            // No new screen policy is introduced by a window; both remain mounted through rotation.
            int a = Window(first).GetInstanceID(), b = Window(second).GetInstanceID();
            screen.Service.Evaluate(new ScreenMetrics(720,1280,new Rect(0,0,720,1280))); screen.Service.FlushPending(Time.frameCount + 1);
            Assert.That(root.GetComponent<CanvasScaler>().referenceResolution, Is.EqualTo(new Vector2(1080,1920)));
            Assert.That(Window(first).GetInstanceID(), Is.EqualTo(a)); Assert.That(Window(second).GetInstanceID(), Is.EqualTo(b));
            foreach (var handle in new[] { first, second })
            {
                AssertMountedHierarchy(handle);
                var brand = Node(Window(handle)).Find("SafeArea/Brand") as RectTransform;
                Assert.That(brand.anchorMin, Is.EqualTo(new Vector2(0, .75f)), "Both windows consume the next portrait snapshot.");
            }
        }

        [UnityTest]
        public IEnumerator ServiceConstructedBeforeRootEnableAppliesRootBeforeWindowGeometry()
        {
            ((IDisposable)service).Dispose(); root.SetActive(false);
            service = Activator.CreateInstance(Required("UIService"), ui, events, catalog, root.GetComponent<AdaptiveCanvas>(), root.transform.Find("Windows"));
            root.SetActive(true);
            var opening=Open(); yield return Await(opening); var handle=Result(opening);
            var scroll=Window(handle).GetComponentInChildren<ScrollRect>();
            var counter=scroll.content.gameObject.AddComponent<LayoutBuildCounter>();
            Canvas.ForceUpdateCanvases(); counter.VerticalBuilds=0;
            screen.Service.Evaluate(new ScreenMetrics(1280,720,new Rect(10,20,1240,680))); screen.Service.FlushPending(Time.frameCount);
            var safe=(RectTransform)root.transform.Find("SafeArea");
            var viewport=(RectTransform)Node(Window(handle)).Find("SafeArea/Viewport");
            Assert.That(root.GetComponent<CanvasScaler>().referenceResolution,Is.EqualTo(new Vector2(1920,1080)));
            Assert.That(safe.anchorMin.x,Is.EqualTo(10f/1280).Within(.0001f));
            Assert.That(safe.anchorMin.y,Is.EqualTo(20f/720).Within(.0001f));
            Assert.That(viewport.rect.height,Is.EqualTo(safe.rect.height-96).Within(1),"Window geometry must use this snapshot's root scaler and safe area, even when the service subscribed first.");
            Canvas.ForceUpdateCanvases();
            Assert.That(counter.VerticalBuilds,Is.EqualTo(1),"The shared root's subscription must not apply the window layout twice.");
            AssertMountedHierarchy(handle);
        }

        [UnityTest]
        public IEnumerator CompliantPrefabPassesAuthoringGateAndNativeChildCanvasOpen()
        {
            var validator=Type.GetType("Client.Editor.UI.UIBindingValidator, Client.Editor");
            Assert.That(validator,Is.Not.Null);
            var asset=AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            var errors=(System.Collections.Generic.IEnumerable<string>)validator.GetMethod("Validate").Invoke(null,new object[]{asset,null});
            Assert.That(errors,Is.Empty,"The actual native GF fixture must pass the U4 authoring/build hierarchy gate.");
            var opening=Open(); yield return Await(opening); var handle=Result(opening);
            AssertMountedHierarchy(handle);
            Assert.That(Window(handle).GetComponent<UIForm>(),Is.Not.Null,"Native GF must attach UIForm to the stable root, independent of its child Canvas.");
        }

        [UnityTest]
        [Category("U4")]
        public IEnumerator NativeWindowImageryFillsInsetsAndInteractionsAreSafeOnce()
        {
            root.transform.Find("Background").GetComponent<Image>().color=Color.green;
            var opening=Open("Popup");yield return Await(opening);var handle=Result(opening);var window=Window(handle);int identity=window.GetInstanceID();
            var safe=(RectTransform)Node(window).Find("SafeArea");var viewport=(RectTransform)safe.Find("Viewport");
            var input=new GameObject("Input",typeof(RectTransform),typeof(InputField));input.transform.SetParent(viewport.GetComponent<ScrollRect>().content,false);input.GetComponent<InputField>().text="preserved";
            GameObject ownedEvents=null;var selected=EventSystem.current;if(selected==null){ownedEvents=new GameObject("U4 Focus EventSystem",typeof(EventSystem));selected=ownedEvents.GetComponent<EventSystem>();}selected.SetSelectedGameObject(input);
            var cameraObject=new GameObject("U4 Native Camera",typeof(Camera));var camera=cameraObject.GetComponent<Camera>();camera.orthographic=true;camera.transform.position=new Vector3(0,0,-10);
            var canvas=root.GetComponent<Canvas>();canvas.renderMode=RenderMode.ScreenSpaceCamera;canvas.worldCamera=camera;canvas.planeDistance=1;
            var cases=new[]{new ScreenMetrics(720,1280,new Rect(40,90,650,1100)),new ScreenMetrics(1280,720,new Rect(80,30,1120,650)),new ScreenMetrics(1280,720,new Rect(0,0,1180,720)),new ScreenMetrics(720,1280,new Rect(60,0,660,1280))};RenderTexture texture=null;
            try
            {
                foreach(var value in cases)
                {
                    bool graphics=SystemInfo.graphicsDeviceType!=UnityEngine.Rendering.GraphicsDeviceType.Null;
                    if(graphics){if(texture!=null){camera.targetTexture=null;texture.Release();Object.DestroyImmediate(texture);}texture=new RenderTexture(value.Width,value.Height,24);texture.Create();camera.targetTexture=texture;yield return null;}
                    screen.Service.Evaluate(value);screen.Service.FlushPending(Time.frameCount);Canvas.ForceUpdateCanvases();yield return null;
                    var rootRect=(RectTransform)root.transform;var full=new Rect(0,0,value.Width,value.Height);
                    AssertNativeRect(NativePixels((RectTransform)window.transform,rootRect,value),full,"stable root full frame");
                    AssertNativeRect(NativePixels(Node(window),rootRect,value),full,"UINode full frame");
                    AssertNativeRect(NativePixels((RectTransform)Node(window).Find("Background"),rootRect,value),full,"window background full frame");
                    AssertNativeRect(NativePixels((RectTransform)Node(window).Find("Mask"),rootRect,value),full,"modal mask full frame");
                    AssertNativeRect(NativePixels(safe,rootRect,value),value.SafeArea,"local safe area exactly once");
                    var bounds=NativePixels(viewport,rootRect,value);Assert.That(bounds.xMin,Is.GreaterThanOrEqualTo(value.SafeArea.xMin-.1f));Assert.That(bounds.xMax,Is.LessThanOrEqualTo(value.SafeArea.xMax+.1f));Assert.That(bounds.yMin,Is.GreaterThanOrEqualTo(value.SafeArea.yMin-.1f));Assert.That(bounds.yMax,Is.LessThanOrEqualTo(value.SafeArea.yMax+.1f));
                    Assert.That(window.GetInstanceID(),Is.EqualTo(identity));Assert.That(selected.currentSelectedGameObject,Is.SameAs(input));Assert.That(input.GetComponent<InputField>().text,Is.EqualTo("preserved"));Assert.That(window.GetComponentsInChildren<CanvasScaler>(true),Is.Empty);
                    if(graphics)
                    {
                        camera.Render();var previous=RenderTexture.active;RenderTexture.active=texture;var image=new Texture2D(value.Width,value.Height,TextureFormat.RGB24,false);image.ReadPixels(full,0,0);image.Apply();
                        Assert.That(image.GetPixel(2,2).r,Is.GreaterThan(.9f));Assert.That(image.GetPixel(2,2).g,Is.LessThan(.1f));Assert.That(image.GetPixel(value.Width-3,value.Height-3).r,Is.GreaterThan(.9f));
                        Directory.CreateDirectory("Builds/Tests/U4/fix1-native");File.WriteAllBytes("Builds/Tests/U4/fix1-native/"+value.Width+"x"+value.Height+"-"+value.SafeArea.xMin+"-"+value.SafeArea.width+".png",image.EncodeToPNG());Object.DestroyImmediate(image);RenderTexture.active=previous;
                    }
                }
            }
            finally{camera.targetTexture=null;if(texture!=null){texture.Release();Object.DestroyImmediate(texture);}Object.Destroy(cameraObject);if(ownedEvents!=null)Object.Destroy(ownedEvents);}
        }
        [UnityTest,Category("U4")]
        public IEnumerator ServiceRejectsSafeAreaConstrainedWindowsMount()
        {
            var wrong=new GameObject("Windows",typeof(RectTransform));wrong.transform.SetParent(root.transform.Find("SafeArea"),false);
            var error=Assert.Throws<TargetInvocationException>(()=>Activator.CreateInstance(Required("UIService"),ui,events,catalog,root.GetComponent<AdaptiveCanvas>(),wrong.transform));
            Assert.That(error.InnerException,Is.TypeOf<ArgumentException>());Assert.That(error.InnerException.Message,Does.Contain("full-frame direct child"));
            wrong.transform.SetParent(root.transform,false);
            error=Assert.Throws<TargetInvocationException>(()=>Activator.CreateInstance(Required("UIService"),ui,events,catalog,root.GetComponent<AdaptiveCanvas>(),wrong.transform));Assert.That(error.InnerException,Is.TypeOf<ArgumentException>());Assert.That(error.InnerException.Message,Does.Contain("stretch anchors"));Object.Destroy(wrong);yield return null;
        }
        static Rect NativePixels(RectTransform target,RectTransform rootRect,ScreenMetrics value)
        {var points=new Vector3[4];target.GetWorldCorners(points);var min=(Vector2)rootRect.InverseTransformPoint(points[0])-rootRect.rect.min;var max=(Vector2)rootRect.InverseTransformPoint(points[2])-rootRect.rect.min;return Rect.MinMaxRect(min.x/rootRect.rect.width*value.Width,min.y/rootRect.rect.height*value.Height,max.x/rootRect.rect.width*value.Width,max.y/rootRect.rect.height*value.Height);}
        static void AssertNativeRect(Rect actual,Rect expected,string reason)
        {Assert.That(actual.xMin,Is.EqualTo(expected.xMin).Within(.1f),reason);Assert.That(actual.yMin,Is.EqualTo(expected.yMin).Within(.1f),reason);Assert.That(actual.xMax,Is.EqualTo(expected.xMax).Within(.1f),reason);Assert.That(actual.yMax,Is.EqualTo(expected.yMax).Within(.1f),reason);}
        [UnityTest]
        public IEnumerator ChildUINodeFillsStableRootAcrossSafeAreaResizeAndRotation()
        {
            var opening=Open(); yield return Await(opening); var handle=Result(opening);
            int rootId=Window(handle).GetInstanceID(), nodeId=Node(Window(handle)).GetInstanceID();
            var metrics=new[]{
                new ScreenMetrics(720,1280,new Rect(20,40,680,1200)),
                new ScreenMetrics(1280,720,new Rect(60,20,1160,680)),
                new ScreenMetrics(720,480,new Rect(0,20,720,440))};
            foreach(var value in metrics)
            {
                screen.Service.Evaluate(value); screen.Service.FlushPending(Time.frameCount);
                Canvas.ForceUpdateCanvases(); yield return null;
                AssertMountedHierarchy(handle);
                Assert.That(Window(handle).GetInstanceID(),Is.EqualTo(rootId)); Assert.That(Node(Window(handle)).GetInstanceID(),Is.EqualTo(nodeId));
            }
        }
    }

    // Observe the resource boundary while retaining native GF pooling and default helper behavior.
    public sealed class U3ObservedUIFormHelper : DefaultUIFormHelper
    {
        public static int Instances, Releases;
        public static GameObject LastReleasedInstance;
        public override object InstantiateUIForm(object asset) { Instances++; return base.InstantiateUIForm(asset); }
        public override void ReleaseUIForm(object asset, object instance) { Releases++; LastReleasedInstance=instance as GameObject; base.ReleaseUIForm(asset, instance); }
    }
}
#endif
