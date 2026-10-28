using UnityEngine;

namespace Client.Framework.UI
{
    [DisallowMultipleComponent, RequireComponent(typeof(RectTransform))]
    public sealed class SafeAreaRoot : MonoBehaviour
    {
        /// <summary>Consumes U1's clamped client-pixel safe area; no device policy is duplicated here.</summary>
        public void Apply(Rect safeArea, Vector2 screenSize)
        {
            if (screenSize.x <= 0 || screenSize.y <= 0) return;
            var rect = (RectTransform)transform;
            rect.anchorMin = new Vector2(safeArea.xMin/screenSize.x,safeArea.yMin/screenSize.y);
            rect.anchorMax = new Vector2(safeArea.xMax/screenSize.x,safeArea.yMax/screenSize.y);
            rect.offsetMin = rect.offsetMax = Vector2.zero;
        }
    }
}
