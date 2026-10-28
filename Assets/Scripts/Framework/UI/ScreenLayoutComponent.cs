using System;
using UnityEngine;

namespace Client.Framework.UI
{
    /// <summary>Samples Unity Screen on the main thread and publishes changes in LateUpdate.</summary>
    [DisallowMultipleComponent]
    public sealed class ScreenLayoutComponent : MonoBehaviour
    {
        public ScreenLayoutService Service { get; } = new ScreenLayoutService();

        /// <summary>Optional source for previews or controlled hosts; null uses Unity Screen.</summary>
        public Func<ScreenMetrics> MetricsSource { get; set; }

        private void Update()
        {
            Service.Evaluate(MetricsSource != null
                ? MetricsSource()
                : new ScreenMetrics(Screen.width, Screen.height, Screen.safeArea));
        }

        private void LateUpdate() => Service.FlushPending(Time.frameCount);
    }
}
