using System.Collections;
using System.Collections.Generic;
using System.Threading;
using Client.Framework.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Client.Tests
{
    [Category("U1")]
    public sealed class ScreenChangeTests
    {
        private GameObject host;

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (host != null) Object.Destroy(host);
            yield return null;
        }

        [UnityTest]
        public IEnumerator StableResizeIsObservedWithinTwoFramesAndDoesNotRepeat()
        {
            host = new GameObject("U1 screen metrics");
            var component = host.AddComponent<ScreenLayoutComponent>();
            var metrics = new ScreenMetrics(720, 1280, new Rect(0, 0, 720, 1280));
            component.MetricsSource = () => metrics;
            var changes = new List<ScreenLayoutSnapshot>();
            var frames = new List<int>();
            var updateObserver = host.AddComponent<ScreenUpdateObserver>();
            updateObserver.PublishedFrames = frames;
            int mainThread = Thread.CurrentThread.ManagedThreadId;
            component.Service.Changed += snapshot =>
            {
                Assert.That(Thread.CurrentThread.ManagedThreadId, Is.EqualTo(mainThread));
                changes.Add(snapshot);
                frames.Add(Time.frameCount);
            };
            yield return null;
            yield return null;
            Assert.That(changes.Count, Is.EqualTo(1));
            Assert.That(component.Service.Current.Width, Is.EqualTo(720));

            int resizeFrame = Time.frameCount;
            metrics = new ScreenMetrics(1280, 720, new Rect(20, 10, 1240, 700));
            yield return null;
            yield return null;
            Assert.That(changes.Count, Is.EqualTo(2));
            Assert.That(frames[1] - resizeFrame, Is.LessThanOrEqualTo(2));
            Assert.That(changes[1].Width, Is.EqualTo(1280));
            Assert.That(changes[1].LayoutMode, Is.EqualTo(LayoutMode.Landscape));
            Assert.That(changes[1].ClampedSafeArea, Is.EqualTo(new Rect(20, 10, 1240, 700)));
            Assert.That(changes[1].Revision, Is.EqualTo(2));
            yield return null;
            yield return null;
            Assert.That(changes.Count, Is.EqualTo(2));
            Assert.That(updateObserver.ObservedUpdates, Is.GreaterThanOrEqualTo(4));
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator ComponentReadsUnityScreenWhenNoSourceIsInjected()
        {
            host = new GameObject("U1 real Screen");
            var component = host.AddComponent<ScreenLayoutComponent>();
            yield return null;
            yield return null;
            Assert.That(component.Service.Current, Is.Not.Null);
            Assert.That(component.Service.Current.Width, Is.EqualTo(Screen.width));
            Assert.That(component.Service.Current.Height, Is.EqualTo(Screen.height));
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator InvalidTransientDimensionsDoNotErasePublishedSnapshot()
        {
            host = new GameObject("U1 transient dimensions");
            var component = host.AddComponent<ScreenLayoutComponent>();
            var metrics = new ScreenMetrics(720, 1280, new Rect(0, 0, 720, 1280));
            component.MetricsSource = () => metrics;
            int callbacks = 0;
            component.Service.Changed += _ => callbacks++;
            yield return null;
            yield return null;
            var previous = component.Service.Current;
            Assert.That(previous, Is.Not.Null);
            metrics = new ScreenMetrics(0, 0, Rect.zero);
            yield return null;
            yield return null;
            Assert.That(component.Service.Current, Is.SameAs(previous));
            Assert.That(callbacks, Is.EqualTo(1));
            LogAssert.NoUnexpectedReceived();
        }
    }

    // Runs after the sampler's Update; publication must still wait for LateUpdate.
    [DefaultExecutionOrder(10000)]
    public sealed class ScreenUpdateObserver : MonoBehaviour
    {
        public List<int> PublishedFrames { get; set; }
        public int ObservedUpdates { get; private set; }

        private void Update()
        {
            Assert.That(PublishedFrames, Has.No.Member(Time.frameCount), "Screen changes must publish after all Update callbacks.");
            ObservedUpdates++;
        }
    }
}
