using UnityEngine;

namespace Client.Framework.UI
{
    public enum LayoutMode
    {
        Portrait,
        Landscape
    }

    /// <summary>Immutable client-pixel geometry. Published revisions start at one.</summary>
    public sealed class ScreenLayoutSnapshot
    {
        internal ScreenLayoutSnapshot(int width, int height, Rect clampedSafeArea, LayoutMode layoutMode, long revision)
        {
            Width = width;
            Height = height;
            ClampedSafeArea = clampedSafeArea;
            LayoutMode = layoutMode;
            Revision = revision;
        }

        public int Width { get; }
        public int Height { get; }
        public Rect ClampedSafeArea { get; }
        public LayoutMode LayoutMode { get; }
        public long Revision { get; }
    }
}
