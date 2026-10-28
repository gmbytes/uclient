#if UNITY_EDITOR
using System;
using System.Collections;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Client.Framework.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace Client.Tests
{
    [Category("F7")]
    public sealed class FirstFrameTests
    {
        private readonly UILifecycleTests fixture = new UILifecycleTests();
        [UnitySetUp] public IEnumerator SetUp() => fixture.SetUp();
        [UnityTearDown] public IEnumerator TearDown() => fixture.TearDown();
        private T Fixture<T>(string name) => (T)typeof(UILifecycleTests).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(fixture);
        private object service => Fixture<object>("service");
        private ScreenLayoutComponent screen => Fixture<ScreenLayoutComponent>("screen");
        private static Type Required(string name) => UILifecycleTests.Required(name);
        private static object Call(object target, string method, params object[] args) => UILifecycleTests.Call(target, method, args);
        private Task Open() => ((UIService)service).OpenAsync("Page", CancellationToken.None);
        private static object Result(Task task) { Assert.That(task.IsCompletedSuccessfully, Is.True, task.Exception?.ToString()); return task.GetType().GetProperty("Result").GetValue(task); }
        private static GameObject Window(object handle) => ((UIViewHandle)handle).Root;
        private static RectTransform Node(GameObject window) => (RectTransform)window.transform.Find("UINode");
        private static Canvas WindowCanvas(object handle) => Node(Window(handle)).GetComponent<Canvas>();
        private static CanvasGroup WindowGroup(object handle) => Node(Window(handle)).GetComponent<CanvasGroup>();
        private static IEnumerator Await(Task task)
        {
            float deadline=Time.realtimeSinceStartup+10;
            while (!task.IsCompleted && Time.realtimeSinceStartup<deadline) yield return null;
            Assert.That(task.IsCompleted, Is.True, "F7 async operation timed out.");
        }
        private static Task WaitForFrame(object handle, CancellationToken token = default)
        {
            var producer = Required("UIFirstFrame");
            return (Task)producer.GetMethod("WaitAsync").Invoke(null, new[] { handle, (object)token });
        }

        private void PublishSnapshot()
        {
            screen.Service.Evaluate(new ScreenMetrics(Screen.width, Screen.height, new Rect(0, 0, Screen.width, Screen.height)));
            screen.Service.FlushPending(Time.frameCount);
        }

        // Completing on open, a forced callback, or an earlier frame would miss the bound blue pixels.
        [UnityTest, Category("F7")]
        public IEnumerator FirstFrameWaitRequiresFutureRenderedBoundPixels()
        {
            Assert.That(Application.isBatchMode, Is.False, "F7 frame proof requires -Graphics (non-batch Editor).");
            Assert.That(SystemInfo.graphicsDeviceType, Is.Not.EqualTo(UnityEngine.Rendering.GraphicsDeviceType.Null));
            PublishSnapshot();
            var open = Open(); yield return Await(open); var handle = Result(open);
            Node(Window(handle)).Find("Background").GetComponent<Image>().color = Color.blue;
            int armedFrame = Time.frameCount;
            var wait = WaitForFrame(handle);
            Assert.That(wait.IsCompleted, Is.False, "Open is not presentation evidence.");
            Canvas.ForceUpdateCanvases();
            Assert.That(wait.IsCompleted, Is.False, "A forced callback in the arm frame is not presentation.");
            yield return Await(wait);
            Assert.That(wait.IsCompletedSuccessfully, Is.True, wait.Exception?.ToString());
            Assert.That(Time.frameCount, Is.GreaterThan(armedFrame));
            yield return new WaitForEndOfFrame();
            var pixels = ScreenCapture.CaptureScreenshotAsTexture();
            try
            {
                var pixel = pixels.GetPixel(2, 2);
                Assert.That(pixel.b, Is.GreaterThan(.9f), "Actual Game-view pixels must contain the bound blue background.");
                Assert.That(pixel.r, Is.LessThan(.1f));
                Directory.CreateDirectory("Builds/Tests/F7/frame-evidence");
                File.WriteAllBytes("Builds/Tests/F7/frame-evidence/bound-blue.png", pixels.EncodeToPNG());
            }
            finally { Object.Destroy(pixels); }
        }

        // Null layout, disabled Canvas, hidden GF window and ancestor opacity cannot produce readiness.
        [UnityTest, Category("F7")]
        public IEnumerator FirstFrameWaitRejectsUnrenderableWindowUntilRestored()
        {
            var open = Open(); yield return Await(open); var handle = Result(open);
            using (var cancel = new CancellationTokenSource())
            {
                var wait = WaitForFrame(handle, cancel.Token);
                for (int i = 0; i < 3; i++) yield return null;
                Assert.That(wait.IsCompleted, Is.False, "No snapshot was applied.");
                WindowCanvas(handle).enabled = false; PublishSnapshot();
                for (int i = 0; i < 3; i++) yield return null;
                Assert.That(wait.IsCompleted, Is.False, "Disabled window Canvas.");
                WindowCanvas(handle).enabled = true; WindowGroup(handle).alpha = 0;
                for (int i = 0; i < 3; i++) yield return null;
                Assert.That(wait.IsCompleted, Is.False, "Transparent window.");
                cancel.Cancel(); yield return Await(wait); Assert.That(wait.IsCanceled, Is.True);
            }
        }

        // Cancel/dispose must terminate the wait, and pooled Bridge reuse must get a fresh frame.
        [UnityTest, Category("F7")]
        public IEnumerator FirstFrameWaitCloseAndPoolReuseKeepHandleEvidenceSeparate()
        {
            PublishSnapshot(); var open = Open(); yield return Await(open); var old = Result(open);
            int instance = Window(old).GetInstanceID(); var oldWait = WaitForFrame(old);
            Call(old, "Close"); yield return Await(oldWait); Assert.That(oldWait.IsCanceled, Is.True);
            open = Open(); yield return Await(open); var current = Result(open);
            Assert.That(Window(current).GetInstanceID(), Is.EqualTo(instance));
            var currentWait = WaitForFrame(current);
            Assert.That(currentWait.IsCompleted, Is.False);
            ((IDisposable)service).Dispose(); yield return Await(currentWait);
            Assert.That(currentWait.IsCanceled, Is.True);
        }

        // Evidence must be published before a synchronous waiter continuation can call Ready.
        [UnityTest, Category("F7")]
        public IEnumerator FirstFrameEvidenceIsPublishedBeforeWaitCompletesAndIgnoresCancelledWait()
        {
            PublishSnapshot(); var open = Open(); yield return Await(open); var handle = Result(open);
            int published = 0;
            Action<UIViewHandle> onFrame = value => { Assert.That(value, Is.SameAs(handle)); published++; };
            var evt = service.GetType().GetEvent("FirstFramePresented", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(evt, Is.Not.Null); evt.GetAddMethod(true).Invoke(service, new object[] { onFrame });
            try
            {
                using (var cancel = new CancellationTokenSource())
                {
                    var cancelled = WaitForFrame(handle, cancel.Token); cancel.Cancel();
                    yield return Await(cancelled); Assert.That(cancelled.IsCanceled, Is.True);
                    for (int i = 0; i < 3; i++) yield return null;
                    Assert.That(published, Is.Zero);
                }
                var wait = WaitForFrame(handle);
                bool continuationSawEvidence = false;
                var continuation = wait.ContinueWith(_ => continuationSawEvidence = published == 1,
                    CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
                yield return Await(wait); yield return Await(continuation);
                Assert.That(wait.IsCompletedSuccessfully, Is.True, wait.Exception?.ToString());
                Assert.That(continuationSawEvidence, Is.True);
            }
            finally { evt.GetRemoveMethod(true).Invoke(service, new object[] { onFrame }); }
        }

        // Multiplying ancestor alpha is necessary; checking each alpha alone can bless invisible content.
        [UnityTest]
        public IEnumerator EffectiveAncestorOpacityMustBeVisible()
        {
            PublishSnapshot(); var open = Open(); yield return Await(open); var handle = Result(open);
            WindowGroup(handle).alpha = .01f;
            var ancestor = Window(handle).AddComponent<CanvasGroup>(); ancestor.alpha = .01f;
            using (var cancel = new CancellationTokenSource())
            {
                var wait = WaitForFrame(handle, cancel.Token);
                for (int i = 0; i < 4; i++) yield return null;
                Assert.That(wait.IsCompleted, Is.False, "Effective opacity .0001 is not visible evidence.");
                cancel.Cancel(); yield return Await(wait); Assert.That(wait.IsCanceled, Is.True);
            }
        }

        // Worker cancellation after willRender but before EOF must beat a queued main-thread cancellation callback.
        [UnityTest]
        public IEnumerator WorkerCancellationAtRenderBoundaryCannotPublishFrame()
        {
            PublishSnapshot(); var open = Open(); yield return Await(open); var handle = Result(open);
            using (var cancel = new CancellationTokenSource())
            {
                int armedFrame = Time.frameCount;
                var wait = WaitForFrame(handle, cancel.Token);
                Canvas.WillRenderCanvases atRender = () =>
                {
                    if (Time.frameCount > armedFrame) Task.Run(() => cancel.Cancel()).GetAwaiter().GetResult();
                };
                Canvas.willRenderCanvases += atRender;
                try { yield return Await(wait); Assert.That(wait.IsCanceled, Is.True); }
                finally { Canvas.willRenderCanvases -= atRender; }
            }
        }

    }
}
#endif
