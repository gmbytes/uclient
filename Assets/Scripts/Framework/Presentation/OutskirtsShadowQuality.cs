using UnityEngine;

namespace Client.Framework.Presentation
{
    [DisallowMultipleComponent, RequireComponent(typeof(Camera))]
    public sealed class OutskirtsShadowQuality : MonoBehaviour
    {
        private float cameraDistance, previousDistance;
        private bool applied;
        public float LastAppliedDistance { get; private set; }
        public void Configure(float distance) => cameraDistance = distance;

        private void OnPreCull()
        {
            Restore();
            previousDistance = QualitySettings.shadowDistance;
            // HYGD: GameShadowDistance(65) - ViewDistance * 1.5, scaled from five-unit cells.
            LastAppliedDistance = Mathf.Max(4, 13 - (8 - cameraDistance) * 1.5f);
            QualitySettings.shadowDistance = LastAppliedDistance;
            applied = true;
        }

        private void OnPostRender() => Restore();
        private void OnDisable() => Restore();
        private void OnDestroy() => Restore();
        private void Restore()
        {
            if (!applied) return;
            QualitySettings.shadowDistance = previousDistance;
            applied = false;
        }
    }
}
