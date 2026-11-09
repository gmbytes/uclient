using UnityEngine;

namespace Client.Framework.Presentation
{
    [DisallowMultipleComponent, RequireComponent(typeof(Camera))]
    public sealed class OutskirtsPostProcess : MonoBehaviour
    {
        private const int MaximumLevels = 6;
        private readonly RenderTexture[] down = new RenderTexture[MaximumLevels];
        private readonly RenderTexture[] up = new RenderTexture[MaximumLevels];
        private Material material;

        public void Configure(Material template)
        {
            ReleaseMaterial();
            if (template != null)
                material = new Material(template) { hideFlags = HideFlags.HideAndDontSave };
        }

        private void OnRenderImage(RenderTexture source, RenderTexture destination)
        {
            if (material == null || !material.shader.isSupported)
            {
                Graphics.Blit(source, destination);
                return;
            }

            int width = Mathf.Max(1, source.width >> 1);
            int height = Mathf.Max(1, source.height >> 1);
            int levels = Mathf.Clamp(Mathf.FloorToInt(Mathf.Log(Mathf.Max(width, height), 2) - 1), 1, MaximumLevels);
            var format = SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.ARGBHalf)
                ? RenderTextureFormat.ARGBHalf : RenderTextureFormat.ARGB32;
            float threshold = Mathf.GammaToLinearSpace(material.GetFloat("_Threshold"));
            material.SetVector("_BloomParams", new Vector4(
                Mathf.Lerp(.05f, .95f, material.GetFloat("_Scatter")), 65472, threshold, threshold * .5f));
            material.SetFloat("_Aspect", (float)source.width / source.height);
            try
            {
                for (int i = 0; i < levels; i++)
                {
                    down[i] = Temporary(width, height, format);
                    up[i] = Temporary(width, height, format);
                    width = Mathf.Max(1, width >> 1);
                    height = Mathf.Max(1, height >> 1);
                }
                Graphics.Blit(source, down[0], material, 0);
                for (int i = 1; i < levels; i++)
                {
                    Graphics.Blit(down[i - 1], up[i], material, 1);
                    Graphics.Blit(up[i], down[i], material, 2);
                }
                RenderTexture bloom = down[levels - 1];
                for (int i = levels - 2; i >= 0; i--)
                {
                    material.SetTexture("_LowMip", bloom);
                    Graphics.Blit(down[i], up[i], material, 3);
                    bloom = up[i];
                }
                material.SetTexture("_BloomTex", bloom);
                Graphics.Blit(source, destination, material, 4);
            }
            finally
            {
                material.SetTexture("_LowMip", null);
                material.SetTexture("_BloomTex", null);
                // Unity checks that OnRenderImage leaves its destination bound.
                RenderTexture.active = destination;
                for (int i = 0; i < levels; i++)
                {
                    if (down[i] != null) RenderTexture.ReleaseTemporary(down[i]);
                    if (up[i] != null) RenderTexture.ReleaseTemporary(up[i]);
                    down[i] = up[i] = null;
                }
            }
        }

        private static RenderTexture Temporary(int width, int height, RenderTextureFormat format)
        {
            var texture = RenderTexture.GetTemporary(width, height, 0, format, RenderTextureReadWrite.Linear);
            texture.filterMode = FilterMode.Bilinear;
            texture.wrapMode = TextureWrapMode.Clamp;
            return texture;
        }

        private void OnDestroy() => ReleaseMaterial();
        private void ReleaseMaterial()
        {
            if (material == null) return;
            if (Application.isPlaying) Destroy(material); else DestroyImmediate(material);
            material = null;
        }
    }
}
