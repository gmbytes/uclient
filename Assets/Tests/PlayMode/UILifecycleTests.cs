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
            var prefab = new GameObject("UINode", typeof(RectTransform), typeof(Canvas), typeof(CanvasGroup), typeof(GraphicRaycaster));
            var button = new GameObject("Action", typeof(RectTransform), typeof(Image), typeof(Button));
            var brand = AdaptiveLayoutTests.Child("Brand", prefab.transform);
            var viewport = AdaptiveLayoutTests.Child("Viewport", prefab.transform);
            var content = AdaptiveLayoutTests.Child("Content", viewport);
            button.transform.SetParent(content, false);
            var action = (RectTransform)button.transform;
            action.anchorMin = action.anchorMax = new Vector2(.5f, 0); action.pivot = new Vector2(.5f, 0); action.sizeDelta = new Vector2(300, 96);
            var scroll = viewport.gameObject.AddComponent<ScrollRect>(); scroll.viewport = viewport; scroll.content = content;
            scroll.inertia = false; scroll.scrollSensitivity = 100;
            var layout = prefab.AddComponent<AdaptiveLayout>();
            layout.Configure(AssetDatabase.LoadAssetAtPath<AdaptiveLayoutDefinition>("Assets/Content/UI/Login/UILogin.Layout.asset"), brand, viewport, scroll, content, action);
            var keyboard = prefab.AddComponent<KeyboardVisibilityController>(); keyboard.Configure(scroll, action);
            var mask = new GameObject("Mask", typeof(RectTransform), typeof(Image), typeof(Button));
            mask.transform.SetParent(prefab.transform, false); mask.transform.SetAsFirstSibling();
            var bindings = prefab.AddComponent(Required("UIBindings"));
            var entry = Activator.CreateInstance(Required("UIBindings").GetNestedType("Binding"));
            Field(entry, "Id", "Action"); Field(entry, "Value", button.GetComponent<Button>());
            Field(entry, "ComponentType", typeof(Button).AssemblyQualifiedName);
            var entries = Array.CreateInstance(entry.GetType(), 1); entries.SetValue(entry, 0);
            Call(bindings, "Configure", entries, null);
            Call(prefab.AddComponent(Required("UIViewBridge")), "Configure", prefab.GetComponent<Canvas>(), prefab.GetComponent<CanvasGroup>(), bindings, layout, keyboard, mask.GetComponent<Button>());
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
            service = Activator.CreateInstance(Required("UIService"), ui, events, catalog, root.GetComponent<AdaptiveCanvas>(), root.transform.Find("SafeArea/Windows"));
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
            Assert.That(Window(upper).GetComponent<Canvas>().sortingOrder, Is.GreaterThan(Window(lower).GetComponent<Canvas>().sortingOrder));
            Assert.That(Window(lower).GetComponent<Canvas>().sortingOrder, Is.GreaterThan(Window(page).GetComponent<Canvas>().sortingOrder));
            Assert.That(Window(page).GetComponent<CanvasGroup>().blocksRaycasts, Is.False);
            Assert.That(Window(lower).GetComponent<CanvasGroup>().interactable, Is.False);
            Assert.That(Window(upper).GetComponent<CanvasGroup>().blocksRaycasts, Is.True);
            Window(upper).transform.Find("Mask").GetComponent<Button>().onClick.Invoke();
            Assert.That(Property<bool>(upper, "IsClosed"), Is.True);
            Assert.That(Window(lower).GetComponent<CanvasGroup>().interactable, Is.True);
            Call(lower, "Close"); Assert.That(Window(page).GetComponent<CanvasGroup>().blocksRaycasts, Is.True);
            t = Open("Overlay"); yield return Await(t); var overlay = Result(t);
            Assert.That(Window(overlay).GetComponent<Canvas>().sortingOrder, Is.GreaterThan(Window(page).GetComponent<Canvas>().sortingOrder));
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
                Assert.That(Window(handle).GetComponent<CanvasScaler>(), Is.Null);
                Assert.That(Window(handle).transform.parent.parent, Is.EqualTo(root.transform.Find("SafeArea/Windows")));
                var brand = Window(handle).transform.Find("Brand") as RectTransform;
                Assert.That(brand.anchorMax, Is.EqualTo(new Vector2(.35f, 1)), "Each open window must consume the already-published landscape snapshot.");
                var keyboard = Window(handle).GetComponent<KeyboardVisibilityController>();
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
                var brand = Window(handle).transform.Find("Brand") as RectTransform;
                Assert.That(brand.anchorMin, Is.EqualTo(new Vector2(0, .75f)), "Both windows consume the next portrait snapshot.");
            }
        }

        [UnityTest]
        public IEnumerator ServiceConstructedBeforeRootEnableAppliesRootBeforeWindowGeometry()
        {
            ((IDisposable)service).Dispose(); root.SetActive(false);
            service = Activator.CreateInstance(Required("UIService"), ui, events, catalog, root.GetComponent<AdaptiveCanvas>(), root.transform.Find("SafeArea/Windows"));
            root.SetActive(true);
            var opening=Open(); yield return Await(opening); var handle=Result(opening);
            var scroll=Window(handle).GetComponentInChildren<ScrollRect>();
            var counter=scroll.content.gameObject.AddComponent<LayoutBuildCounter>();
            Canvas.ForceUpdateCanvases(); counter.VerticalBuilds=0;
            screen.Service.Evaluate(new ScreenMetrics(1280,720,new Rect(10,20,1240,680))); screen.Service.FlushPending(Time.frameCount);
            var safe=(RectTransform)root.transform.Find("SafeArea");
            var viewport=(RectTransform)Window(handle).transform.Find("Viewport");
            Assert.That(root.GetComponent<CanvasScaler>().referenceResolution,Is.EqualTo(new Vector2(1920,1080)));
            Assert.That(safe.anchorMin.x,Is.EqualTo(10f/1280).Within(.0001f));
            Assert.That(safe.anchorMin.y,Is.EqualTo(20f/720).Within(.0001f));
            Assert.That(viewport.rect.height,Is.EqualTo(safe.rect.height-96).Within(1),"Window geometry must use this snapshot's root scaler and safe area, even when the service subscribed first.");
            Canvas.ForceUpdateCanvases();
            Assert.That(counter.VerticalBuilds,Is.EqualTo(1),"The shared root's subscription must not apply the window layout twice.");
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
