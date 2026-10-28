using UnityEngine;

namespace Client.Framework.UI
{
    [System.Serializable]
    public sealed class AdaptiveLayoutProfile
    {
        public Vector2 ReferenceResolution = new Vector2(1080,1920);
        public Rect BrandAnchors = new Rect(0,.75f,1,.25f);
        public Rect FormAnchors = new Rect(0,0,1,.75f);
        // Left, bottom, right, top; all values use reference pixels.
        public Vector4 Margins = new Vector4(48,48,48,48);
        public float MaximumFormWidth = 760;
        public float MinimumContentHeight = 720;
        public float MinimumButtonHeight = 96;
    }

    [CreateAssetMenu(menuName="Client/UI/Adaptive Layout",fileName="UIWindow.Layout")]
    public sealed class AdaptiveLayoutDefinition : ScriptableObject
    {
        public AdaptiveLayoutProfile Portrait = new AdaptiveLayoutProfile();
        public AdaptiveLayoutProfile Landscape = new AdaptiveLayoutProfile
        {
            ReferenceResolution = new Vector2(1920,1080),
            BrandAnchors = new Rect(0,0,.35f,1),
            FormAnchors = new Rect(.35f,0,.65f,1)
        };
        public AdaptiveLayoutProfile For(LayoutMode mode) => mode == LayoutMode.Portrait ? Portrait : Landscape;
        public static Vector2 ReferenceFor(LayoutMode mode) => mode == LayoutMode.Portrait ? new Vector2(1080,1920) : new Vector2(1920,1080);
    }
}
