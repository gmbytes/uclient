using UnityEngine;

namespace Client.Framework.Presentation
{
    /// <summary>Preserves HYGD's native model height after fitting a model into a local hex.</summary>
    [DisallowMultipleComponent]
    public sealed class OutskirtsModelHeight : MonoBehaviour
    {
        [SerializeField] private float nativeHeightScale = 5;
        [SerializeField] private float nativeHeightOffset;

        public void Configure(float scale, float offset)
        {
            nativeHeightScale = scale;
            nativeHeightOffset = offset;
        }

        private void OnEnable()
        {
            if (!Application.isPlaying) return;
            var properties = new MaterialPropertyBlock();
            foreach (var renderer in GetComponentsInChildren<Renderer>(true))
            {
                renderer.GetPropertyBlock(properties);
                properties.SetFloat("_NativeHeightScale", nativeHeightScale);
                properties.SetFloat("_NativeHeightOffset", nativeHeightOffset);
                renderer.SetPropertyBlock(properties);
            }
        }
    }
}
