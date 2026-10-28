using System.Collections.Generic;
using Client.Framework.UI;
using NUnit.Framework;
using UnityEngine;

namespace Client.Tests
{
    [Category("U1")]
    public sealed class ScreenLayoutTests
    {
        [TestCase(1080, 1920, LayoutMode.Portrait)]
        [TestCase(1920, 1080, LayoutMode.Landscape)]
        [TestCase(1080, 1080, LayoutMode.Landscape)]
        [TestCase(719, 1280, LayoutMode.Portrait)]
        public void ActualAspectRatioSelectsLayout(int width, int height, LayoutMode expected)
        {
            var snapshot = new ScreenLayoutService().Evaluate(Full(width, height));
            Assert.That(snapshot, Is.Not.Null);
            Assert.That(snapshot.Width, Is.EqualTo(width));
            Assert.That(snapshot.Height, Is.EqualTo(height));
            Assert.That(snapshot.LayoutMode, Is.EqualTo(expected));
        }

        [Test]
        public void NoSnapshotExistsBeforeValidMetrics()
        {
            var service = new ScreenLayoutService();
            Assert.That(service.Current, Is.Null);
            Assert.That(service.Evaluate(Full(0, 0)), Is.Null);
            Assert.That(service.FlushPending(0), Is.False);
            Assert.That(service.Current, Is.Null);
        }

        [TestCase(0, 1920)]
        [TestCase(1080, 0)]
        [TestCase(-1, 1920)]
        [TestCase(1080, -1)]
        public void InvalidDimensionsHoldLastValidSnapshot(int width, int height)
        {
            var service = Published(1080, 1920);
            var previous = service.Current;
            Assert.That(service.Evaluate(Full(width, height)), Is.SameAs(previous));
            Assert.That(service.FlushPending(2), Is.False);
            Assert.That(service.Current, Is.SameAs(previous));
        }

        [Test]
        public void InvalidDimensionsPreservePendingValidMetrics()
        {
            var service = new ScreenLayoutService();
            var pending = service.Evaluate(Full(720, 1280));
            Assert.That(service.Evaluate(Full(0, 0)), Is.SameAs(pending));
            Assert.That(service.FlushPending(1), Is.True);
            Assert.That(service.Current.Width, Is.EqualTo(720));
        }

        [TestCase(-20, -30, 800, 1400, 0, 0, 720, 1280)]
        [TestCase(20, 40, 800, 1400, 20, 40, 700, 1240)]
        [TestCase(-20, 40, 700, 1200, 0, 40, 680, 1200)]
        [TestCase(20, 40, 680, 1200, 20, 40, 680, 1200)]
        public void SafeAreaIsIntersectedWithClientRect(float x, float y, float width, float height,
            float expectedX, float expectedY, float expectedWidth, float expectedHeight)
        {
            var snapshot = new ScreenLayoutService().Evaluate(new ScreenMetrics(720, 1280, new Rect(x, y, width, height)));
            Assert.That(snapshot, Is.Not.Null);
            Assert.That(snapshot.ClampedSafeArea, Is.EqualTo(new Rect(expectedX, expectedY, expectedWidth, expectedHeight)));
        }

        [TestCase(0, 0, 0, 0)]
        [TestCase(0, 0, 0, 100)]
        [TestCase(800, 0, 100, 100)]
        [TestCase(0, 1400, 100, 100)]
        [TestCase(20, 20, -10, 50)]
        [TestCase(float.NaN, 0, 100, 100)]
        [TestCase(0, 0, float.PositiveInfinity, 100)]
        public void UnusableSafeAreaFallsBackToFullClient(float x, float y, float width, float height)
        {
            var snapshot = new ScreenLayoutService().Evaluate(new ScreenMetrics(720, 1280, new Rect(x, y, width, height)));
            Assert.That(snapshot, Is.Not.Null);
            Assert.That(snapshot.ClampedSafeArea, Is.EqualTo(new Rect(0, 0, 720, 1280)));
        }

        [Test]
        public void SameMetricsPublishNoEvent()
        {
            var service = Published(720, 1280);
            int callbacks = 0;
            service.Changed += _ => callbacks++;
            for (int frame = 2; frame < 6; frame++)
            {
                service.Evaluate(Full(720, 1280));
                Assert.That(service.FlushPending(frame), Is.False);
            }
            Assert.That(callbacks, Is.Zero);
            Assert.That(service.Current.Revision, Is.EqualTo(1));
        }

        [Test]
        public void SameFrameChangesPublishOnce()
        {
            var service = new ScreenLayoutService();
            var changes = new List<ScreenLayoutSnapshot>();
            service.Changed += changes.Add;
            service.Evaluate(Full(720, 1280));
            service.Evaluate(Full(1080, 1920));
            service.Evaluate(Full(1920, 1080));
            Assert.That(changes, Is.Empty, "Evaluation must not publish before the frame boundary.");
            Assert.That(service.Current, Is.Null);
            Assert.That(service.FlushPending(10), Is.True);
            Assert.That(changes.Count, Is.EqualTo(1));
            Assert.That(changes[0].Width, Is.EqualTo(1920));
            Assert.That(changes[0].Revision, Is.EqualTo(1));

            service.Evaluate(Full(720, 1280));
            Assert.That(service.FlushPending(10), Is.False);
            Assert.That(changes.Count, Is.EqualTo(1));
            Assert.That(service.FlushPending(11), Is.True);
            Assert.That(changes.Count, Is.EqualTo(2));
            Assert.That(changes[1].Width, Is.EqualTo(720));
            Assert.That(changes[1].Revision, Is.EqualTo(2));
        }

        [Test]
        public void OneValidResizeAdvancesOneRevision()
        {
            var service = Published(720, 1280);
            var preview = service.Evaluate(Full(1280, 720));
            Assert.That(preview, Is.Not.Null);
            Assert.That(preview.Revision, Is.EqualTo(1), "An unpublished evaluation must not consume a revision.");
            Assert.That(service.Current.Width, Is.EqualTo(720));
            Assert.That(service.FlushPending(2), Is.True);
            Assert.That(service.Current.Revision, Is.EqualTo(2));
            Assert.That(service.Current.LayoutMode, Is.EqualTo(LayoutMode.Landscape));
            Assert.That(service.FlushPending(3), Is.False);
            Assert.That(service.Current.Revision, Is.EqualTo(2));
        }

        [Test]
        public void SafeAreaOnlyChangePublishesNextRevision()
        {
            var service = Published(720, 1280);
            service.Evaluate(new ScreenMetrics(720, 1280, new Rect(10, 20, 700, 1240)));
            Assert.That(service.FlushPending(2), Is.True);
            Assert.That(service.Current.ClampedSafeArea, Is.EqualTo(new Rect(10, 20, 700, 1240)));
            Assert.That(service.Current.Revision, Is.EqualTo(2));
        }

        [Test]
        public void EquivalentClampedGeometryPublishesNoChange()
        {
            var service = Published(720, 1280);
            service.Evaluate(new ScreenMetrics(720, 1280, new Rect(-10, -10, 1000, 1500)));
            Assert.That(service.FlushPending(2), Is.False);
            Assert.That(service.Current.Revision, Is.EqualTo(1));
        }

        [Test]
        public void ReturnToCurrentBeforeFlushPublishesNothing()
        {
            var service = Published(720, 1280);
            service.Evaluate(Full(1280, 720));
            service.Evaluate(Full(720, 1280));
            Assert.That(service.FlushPending(2), Is.False);
            Assert.That(service.Current.Revision, Is.EqualTo(1));
        }

        [Test]
        public void SubscriberSeesCurrentAndReentrantFlushIsDeferred()
        {
            var service = new ScreenLayoutService();
            int callbacks = 0;
            service.Changed += snapshot =>
            {
                callbacks++;
                Assert.That(service.Current, Is.SameAs(snapshot));
                if (callbacks != 1) return;
                service.Evaluate(Full(1280, 720));
                Assert.That(service.FlushPending(1), Is.False);
            };
            service.Evaluate(Full(720, 1280));
            Assert.That(service.FlushPending(1), Is.True);
            Assert.That(callbacks, Is.EqualTo(1));
            Assert.That(service.Current.Width, Is.EqualTo(720));
            Assert.That(service.FlushPending(2), Is.True);
            Assert.That(callbacks, Is.EqualTo(2));
            Assert.That(service.Current.Width, Is.EqualTo(1280));
        }

        private static ScreenMetrics Full(int width, int height) => new ScreenMetrics(width, height, new Rect(0, 0, width, height));

        private static ScreenLayoutService Published(int width, int height)
        {
            var service = new ScreenLayoutService();
            service.Evaluate(Full(width, height));
            Assert.That(service.FlushPending(1), Is.True);
            return service;
        }
    }
}
