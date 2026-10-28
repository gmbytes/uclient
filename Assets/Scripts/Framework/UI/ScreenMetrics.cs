using UnityEngine;

namespace Client.Framework.UI
{
    public readonly struct ScreenMetrics
    {
        public ScreenMetrics(int width, int height, Rect safeArea)
        {
            Width = width;
            Height = height;
            SafeArea = safeArea;
        }

        public int Width { get; }
        public int Height { get; }
        public Rect SafeArea { get; }
    }
}
