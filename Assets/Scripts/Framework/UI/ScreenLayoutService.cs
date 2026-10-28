using System;
using UnityEngine;

namespace Client.Framework.UI
{
    /// <summary>
    /// Main-thread geometry service. Evaluate stages the latest valid geometry;
    /// the frame owner calls FlushPending once at its end-of-frame boundary.
    /// </summary>
    public sealed class ScreenLayoutService
    {
        private ScreenLayoutSnapshot pending;
        private int lastPublishedFrame;

        /// <summary>The last published snapshot, or null before the first valid publication.</summary>
        public ScreenLayoutSnapshot Current { get; private set; }

        public event Action<ScreenLayoutSnapshot> Changed;

        /// <summary>
        /// Stages valid metrics without invoking Changed or consuming a revision.
        /// The returned preview carries the last published revision (zero initially).
        /// Invalid dimensions retain the latest valid pending or published snapshot.
        /// </summary>
        public ScreenLayoutSnapshot Evaluate(ScreenMetrics metrics)
        {
            if (metrics.Width <= 0 || metrics.Height <= 0) return pending ?? Current;

            Rect safeArea = ClampSafeArea(metrics);
            if (Matches(pending, metrics, safeArea)) return pending;
            if (Matches(Current, metrics, safeArea))
            {
                pending = null;
                return Current;
            }

            var mode = (float)metrics.Width / metrics.Height < 1f ? LayoutMode.Portrait : LayoutMode.Landscape;
            pending = new ScreenLayoutSnapshot(metrics.Width, metrics.Height, safeArea, mode, Current?.Revision ?? 0);
            return pending;
        }

        /// <summary>
        /// Publishes one changed snapshot for a Unity frame number. A second publication
        /// in that frame is deferred; its pending geometry remains available next frame.
        /// Current and its revision are updated before subscribers are invoked.
        /// </summary>
        public bool FlushPending(int frameNumber)
        {
            if (pending == null || (Current != null && frameNumber == lastPublishedFrame)) return false;

            var next = pending;
            pending = null;
            Current = new ScreenLayoutSnapshot(next.Width, next.Height, next.ClampedSafeArea, next.LayoutMode,
                (Current?.Revision ?? 0) + 1);
            lastPublishedFrame = frameNumber;
            Changed?.Invoke(Current);
            return true;
        }

        private static bool Matches(ScreenLayoutSnapshot snapshot, ScreenMetrics metrics, Rect safeArea) =>
            snapshot != null && snapshot.Width == metrics.Width && snapshot.Height == metrics.Height &&
            snapshot.ClampedSafeArea.Equals(safeArea);

        private static Rect ClampSafeArea(ScreenMetrics metrics)
        {
            Rect full = new Rect(0, 0, metrics.Width, metrics.Height);
            Rect safe = metrics.SafeArea;
            if (!IsFinite(safe.x) || !IsFinite(safe.y) || !IsFinite(safe.width) || !IsFinite(safe.height) ||
                safe.width <= 0 || safe.height <= 0) return full;

            float left = Mathf.Clamp(safe.xMin, 0, metrics.Width);
            float right = Mathf.Clamp(safe.xMax, 0, metrics.Width);
            float bottom = Mathf.Clamp(safe.yMin, 0, metrics.Height);
            float top = Mathf.Clamp(safe.yMax, 0, metrics.Height);
            return right > left && top > bottom ? Rect.MinMaxRect(left, bottom, right, top) : full;
        }

        private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
