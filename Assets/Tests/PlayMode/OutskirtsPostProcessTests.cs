#if UNITY_EDITOR
using Client.Framework.Presentation;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Client.Tests
{
    public sealed class OutskirtsPostProcessTests
    {
        [Test, Category("Outskirts")]
        public void ReferenceEffectsPreserveMidtonesAndSpreadOnlyBrightHighlights()
        {
            var template = AssetDatabase.LoadAssetAtPath<Material>(
                "Assets/Content/Materials/Outskirts/ReferencePostProcess.mat");
            Assert.That(template, Is.Not.Null);
            var host = new GameObject("Outskirts post-process verification", typeof(Camera));
            var camera = host.GetComponent<Camera>();
            var target = new RenderTexture(96, 160, 24, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear);
            var sample = new Texture2D(96, 160, TextureFormat.RGBAHalf, false, true);
            GameObject highlight = null;
            Material highlightMaterial = null;
            var previous = RenderTexture.active;
            try
            {
                camera.enabled = false;
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = new Color(.2f, .3f, .2f, 1);
                camera.cullingMask = 0;
                camera.orthographic = true;
                camera.orthographicSize = 2;
                camera.allowHDR = true;
                camera.allowMSAA = false;
                camera.targetTexture = target;
                camera.transform.position = new Vector3(0, 0, -10);
                var effect = host.AddComponent<OutskirtsPostProcess>();
                effect.Configure(template);
                camera.Render();
                RenderTexture.active = target;
                sample.ReadPixels(new Rect(0, 0, 96, 160), 0, 0);
                sample.Apply();
                var center = sample.GetPixel(48, 104);
                Assert.That(center.r, Is.EqualTo(.2f).Within(.015f));
                Assert.That(center.g, Is.EqualTo(.3f).Within(.015f));
                Assert.That(sample.GetPixel(1, 1).g, Is.LessThan(center.g - .01f), "The original vignette must darken the edge.");

                camera.backgroundColor = Color.black;
                camera.cullingMask = 1 << 29;
                highlight = GameObject.CreatePrimitive(PrimitiveType.Quad);
                highlight.layer = 29;
                highlight.transform.localScale = Vector3.one * .4f;
                highlightMaterial = new Material(Shader.Find("Unlit/Color"));
                highlightMaterial.color = new Color(5, 4, 2, 1);
                highlight.GetComponent<Renderer>().sharedMaterial = highlightMaterial;
                effect.enabled = false;
                camera.Render();
                RenderTexture.active = target;
                sample.ReadPixels(new Rect(0, 0, 96, 160), 0, 0);
                sample.Apply();
                float withoutBloom = sample.GetPixel(60, 80).g;
                effect.enabled = true;
                camera.Render();
                RenderTexture.active = target;
                sample.ReadPixels(new Rect(0, 0, 96, 160), 0, 0);
                sample.Apply();
                Assert.That(sample.GetPixel(60, 80).g, Is.GreaterThan(withoutBloom + .002f),
                    "A bright lamp must illuminate surrounding pixels through bloom.");
            }
            finally
            {
                RenderTexture.active = previous;
                camera.targetTexture = null;
                Object.DestroyImmediate(host);
                if (highlight != null) Object.DestroyImmediate(highlight);
                if (highlightMaterial != null) Object.DestroyImmediate(highlightMaterial);
                target.Release();
                Object.DestroyImmediate(target);
                Object.DestroyImmediate(sample);
            }
        }
    }
}
#endif
