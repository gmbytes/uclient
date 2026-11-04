using System;
using System.IO;
using System.Linq;
using Client.Framework.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace Client.Editor.UI
{
    /// <summary>Updates the existing login asset without rebuilding its host or session composition.</summary>
    public static class LoginArtworkBuilder
    {
        const string Root = "Assets/Content/UI/Login/";
        const string BackgroundPath = Root + "Art/ThreeKingdomsWar.png";
        const float ActionWidth = 276;
        const float InputWidth = ActionWidth * 2;
        static readonly Color Gold = new Color(.79f, .60f, .31f);
        static readonly Color Ivory = new Color(1f, .89f, .65f);
        static readonly Color Ink = new Color(.035f, .026f, .018f, .96f);
        const float FormHeight = 552;

        [MenuItem("Game/UI/Apply Three Kingdoms Login")]
        public static void Apply()
        {
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            var importer = AssetImporter.GetAtPath(BackgroundPath) as TextureImporter;
            if (importer == null) throw new InvalidOperationException("Import the supplied login artwork first.");
            importer.textureType = TextureImporterType.Default;
            importer.mipmapEnabled = false;
            importer.maxTextureSize = 4096;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.textureCompression = TextureImporterCompression.Compressed;
            importer.SaveAndReimport();
            importer.GetSourceTextureWidthAndHeight(out int artWidth, out int artHeight);
            if (artWidth <= 0 || artHeight <= 0) throw new InvalidOperationException("The artwork must have valid source dimensions.");
            var art = AssetDatabase.LoadAssetAtPath<Texture2D>(BackgroundPath);
            var font = AssetDatabase.LoadAssetAtPath<Font>("Assets/Content/UI/Common/Fonts/NotoSansCJKsc-Regular.otf");
            MakeTexture("LoginShade", false);
            MakeTexture("VermilionButton", true);
            var root = PrefabUtility.LoadPrefabContents(Root + "UILogin.prefab");
            try
            {
                var node = root.transform.Find("UINode");
                var background = node.Find("Background");
                background.GetComponent<Image>().color = new Color(.035f, .025f, .03f);
                foreach (Transform child in background.Cast<Transform>().ToArray()) Object.DestroyImmediate(child.gameObject);
                if (background.GetComponent<RectMask2D>() == null) background.gameObject.AddComponent<RectMask2D>();
                var illustration = Child("Artwork", background);
                var picture = illustration.gameObject.AddComponent<RawImage>();
                picture.texture = art; picture.raycastTarget = false;
                illustration.pivot = new Vector2(.5f, .5f);
                var aspect = illustration.gameObject.AddComponent<AspectRatioFitter>();
                aspect.aspectMode = AspectRatioFitter.AspectMode.EnvelopeParent;
                aspect.aspectRatio = (float)artWidth / artHeight;
                var shade = Child("BottomShade", background);
                shade.anchorMax = new Vector2(1, .45f);
                var shadeImage = shade.gameObject.AddComponent<RawImage>();
                shadeImage.texture = AssetDatabase.LoadAssetAtPath<Texture2D>(Root + "Art/LoginShade.png");
                shadeImage.raycastTarget = false;

                var safe = node.Find("SafeArea");
                var brand = safe.Find("Static");
                foreach (Transform child in brand.Cast<Transform>().ToArray()) Object.DestroyImmediate(child.gameObject);
                var title = Label("Title", brand, font, "", 1, 1);
                title.color = Color.clear;
                var viewport = (RectTransform)safe.Find("Dynamic");
                var content = (RectTransform)viewport.Find("Content");
                foreach (Transform child in content.Cast<Transform>().ToArray()) Object.DestroyImmediate(child.gameObject);
                var group = content.GetComponent<VerticalLayoutGroup>() ?? content.gameObject.AddComponent<VerticalLayoutGroup>();
                group.spacing = 12;
                group.padding = new RectOffset(12, 12, 8, 8);
                group.childAlignment = TextAnchor.LowerCenter;
                group.childControlHeight = group.childControlWidth = true;
                group.childForceExpandHeight = group.childForceExpandWidth = false;
                var contentSize = content.GetComponent<ContentSizeFitter>() ?? content.gameObject.AddComponent<ContentSizeFitter>();
                contentSize.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
                contentSize.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
                var contentMinimum = content.GetComponent<LayoutElement>() ?? content.gameObject.AddComponent<LayoutElement>();
                contentMinimum.minHeight = FormHeight;

                var input = Credential("AccountRow", "AccountInput", content, font, "账号", false);
                var accountRow = (RectTransform)input.transform.parent;
                var password = Credential("PasswordRow", "PasswordInput", content, font, "密码", true);
                var passwordRow = (RectTransform)password.transform.parent;

                var server = Action("ServerButton", content, font, "", 72, Ink, 28);
                var serverSize = server.GetComponent<LayoutElement>();
                serverSize.preferredWidth = InputWidth;
                serverSize.flexibleWidth = 0;
                server.interactable = false;
                var serverColors = server.colors; serverColors.disabledColor = Color.white; server.colors = serverColors;
                Object.DestroyImmediate(server.transform.Find("Label").gameObject);
                DecoratedFrame((RectTransform)server.transform, false);
                var serverLabel = Label("ServerLabel", server.transform, font, "本地体验  ·  默认区服", 27, 72);
                Stretch(serverLabel.rectTransform, 70, 4); serverLabel.alignment = TextAnchor.MiddleCenter;
                var dot = Child("StatusSeal", server.transform);
                dot.anchorMin = dot.anchorMax = new Vector2(.065f, .5f); dot.sizeDelta = new Vector2(13, 13);
                dot.localRotation = Quaternion.Euler(0, 0, 45);
                var seal = dot.gameObject.AddComponent<Image>(); seal.color = new Color(.40f, .70f, .27f); seal.raycastTarget = false;
                server.gameObject.SetActive(false);

                var login = Action("LoginButton", content, font, "登 录", 96, Color.white, 40);
                var loginSize = login.GetComponent<LayoutElement>();
                loginSize.preferredWidth = ActionWidth;
                loginSize.flexibleWidth = 0;
                var buttonImage = login.GetComponent<Image>();
                buttonImage.sprite = AssetDatabase.LoadAssetAtPath<Sprite>(Root + "Art/VermilionButton.png");
                DecoratedFrame((RectTransform)login.transform, true);

                var agreementRow = Row("Agreement", content, 48);
                var agreement = agreementRow.gameObject.AddComponent<Toggle>();
                var box = Child("Box", agreementRow);
                box.anchorMin = box.anchorMax = new Vector2(.04f, .5f); box.sizeDelta = new Vector2(31, 31);
                var boxImage = box.gameObject.AddComponent<Image>(); boxImage.color = new Color(.07f, .045f, .02f, .9f);
                Frame(box, 2, Gold);
                var checkRect = Child("Check", box); Stretch(checkRect, 3, 3);
                var check = checkRect.gameObject.AddComponent<Image>();
                check.sprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Checkmark.psd");
                check.color = Ivory; check.raycastTarget = false;
                agreement.targetGraphic = boxImage; agreement.graphic = check; agreement.isOn = true;
                var policy = Label("AgreementLabel", agreementRow, font, "我已阅读并同意《用户协议》《隐私政策》", 21, 48);
                policy.rectTransform.anchorMin = new Vector2(.095f, 0); policy.rectTransform.anchorMax = Vector2.one;
                policy.rectTransform.offsetMin = policy.rectTransform.offsetMax = Vector2.zero;
                policy.alignment = TextAnchor.MiddleLeft;
                var cancel = Action("CancelButton", content, font, "取消连接", 45, new Color(.08f, .05f, .03f, .8f), 24);
                cancel.gameObject.SetActive(false);
                var status = Label("StatusLabel", content, font, "本地模拟 · 开发演示，无需真实密码", 21, 44);
                status.alignment = TextAnchor.MiddleCenter; status.color = new Color(.94f, .85f, .68f, .9f);
                var version = Label("VersionLabel", content, font, "Framework demo v1 · 本地模拟", 17, 28);
                version.alignment = TextAnchor.MiddleCenter; version.color = new Color(.76f, .7f, .59f, .8f);
                var bindings = root.GetComponent<UIBindings>();
                bindings.Configure(new[] {
                    Binding("Title",title), Binding("AccountInput",input), Binding("LoginButton",login),
                    Binding("CancelButton",cancel), Binding("StatusLabel",status), Binding("VersionLabel",version),
                    Binding("AgreementToggle",agreement), Binding("ServerButton",server), Binding("ServerLabel",serverLabel),
                    Binding("AccountRow",accountRow), Binding("PasswordInput",password), Binding("PasswordRow",passwordRow)
                }, null);
                var bridge = root.GetComponent<UIViewBridge>();
                bridge.Layout.Configure(bridge.Layout.Definition, (RectTransform)brand, viewport, viewport.GetComponent<ScrollRect>(), content, (RectTransform)login.transform);
                bridge.Keyboard.Configure(viewport.GetComponent<ScrollRect>(), (RectTransform)login.transform);
                bridge.Keyboard.ExpandUnmeasuredWithinSafeArea = true;
                var layout = bridge.Layout.Definition;
                foreach (var profile in new[] { layout.Portrait, layout.Landscape })
                {
                    profile.ReferenceResolution = new Vector2(1080, 1920);
                    profile.BrandAnchors = new Rect(0, .75f, 1, .25f);
                    profile.FormAnchors = new Rect(.1f, .008f, .8f, .29f);
                    profile.Margins = new Vector4(0, 0, 0, 0);
                    profile.MaximumFormWidth = 850; profile.MinimumContentHeight = FormHeight; profile.MinimumButtonHeight = 96;
                }
                EditorUtility.SetDirty(layout);
                PrefabUtility.SaveAsPrefabAsset(root, Root + "UILogin.prefab");
                var generated = UIBindingGenerator.Regenerate(bindings, Root.TrimEnd('/'));
                if (!generated.Success) throw new InvalidOperationException(generated.Error);
                PlayerSettings.defaultScreenWidth = 1080; PlayerSettings.defaultScreenHeight = 1920;
                PlayerSettings.defaultInterfaceOrientation = UIOrientation.Portrait;
                PlayerSettings.allowedAutorotateToPortrait = true; PlayerSettings.allowedAutorotateToPortraitUpsideDown = false;
                PlayerSettings.allowedAutorotateToLandscapeLeft = PlayerSettings.allowedAutorotateToLandscapeRight = false;
                AssetDatabase.SaveAssets();
                Debug.Log("LOGIN_ARTWORK_APPLIED 1080x1920");
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }

        [MenuItem("Game/UI/Capture Three Kingdoms Login")]
        public static void Capture()
        {
            if (string.IsNullOrEmpty(UnityEngine.SceneManagement.SceneManager.GetActiveScene().path))
                EditorSceneManager.OpenScene("Assets/Scenes/Bootstrap.unity");
            var definition = AssetDatabase.LoadAssetAtPath<UIWindowConfig>(Root + "UILogin.Config.asset").Resolve();
            Directory.CreateDirectory("Builds/LoginUI");
            foreach (var size in new[] { new Vector2Int(1080, 1920), new Vector2Int(720, 1280) })
            {
                using (var preview = new UIPreviewSession(definition))
                {
                    preview.Apply(new ScreenMetrics(size.x, size.y, new Rect(0, 0, size.x, size.y)), false, Rect.zero, null);
                    if (preview.Texture == null) throw new InvalidOperationException("A graphics-enabled editor is required.");
                    var previous = RenderTexture.active; RenderTexture.active = preview.Texture;
                    var image = new Texture2D(size.x, size.y, TextureFormat.RGB24, false);
                    try { image.ReadPixels(new Rect(0, 0, size.x, size.y), 0, 0); image.Apply(); File.WriteAllBytes($"Builds/LoginUI/login-{size.x}x{size.y}.png", image.EncodeToPNG()); }
                    finally { RenderTexture.active = previous; Object.DestroyImmediate(image); }
                }
            }
            Debug.Log("LOGIN_ARTWORK_CAPTURED");
        }
        [MenuItem("Game/UI/Apply and Capture Three Kingdoms Login")]
        public static void ApplyAndCapture() { Apply(); Capture(); }

        static void MakeTexture(string name, bool button)
        {
            var texture = new Texture2D(8, 256, TextureFormat.RGBA32, false);
            for (int y = 0; y < 256; y++)
            {
                float t = y / 255f;
                var color = button ? Color.Lerp(new Color(.27f, .015f, .008f), new Color(.7f, .10f, .025f), Mathf.Sin(t * Mathf.PI) * .7f + t * .3f)
                    : new Color(.035f, .021f, .027f, Mathf.Pow(1 - t, 1.25f) * .96f);
                for (int x = 0; x < 8; x++) texture.SetPixel(x, y, color);
            }
            texture.Apply(); string path = Root + "Art/" + name + ".png";
            File.WriteAllBytes(path, texture.EncodeToPNG()); Object.DestroyImmediate(texture);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType = button ? TextureImporterType.Sprite : TextureImporterType.Default;
            importer.mipmapEnabled = false; importer.wrapMode = TextureWrapMode.Clamp;
            importer.textureCompression = TextureImporterCompression.Uncompressed; importer.SaveAndReimport();
        }
        static RectTransform Child(string name, Transform parent)
        { var go = new GameObject(name, typeof(RectTransform)); go.transform.SetParent(parent, false); var rect = (RectTransform)go.transform; Stretch(rect); return rect; }
        static RectTransform Row(string name, Transform parent, float height)
        { var rect = Child(name, parent); var item = rect.gameObject.AddComponent<LayoutElement>(); item.minHeight = item.preferredHeight = height; item.flexibleWidth = 1; return rect; }
        static Text Label(string name, Transform parent, Font font, string value, int size, float height)
        { var rect = Row(name, parent, height); var text = rect.gameObject.AddComponent<Text>(); text.font = font; text.text = value; text.fontSize = size; text.color = Ivory; text.alignment = TextAnchor.MiddleLeft; text.raycastTarget = false; return text; }
        static InputField Credential(string rowName, string inputName, Transform parent, Font font, string caption, bool password)
        {
            var row = Row(rowName, parent, 72);
            var rowSize = row.GetComponent<LayoutElement>();
            rowSize.preferredWidth = InputWidth;
            rowSize.flexibleWidth = 0;
            row.gameObject.AddComponent<Image>().color = Ink;
            DecoratedFrame(row, false);
            foreach (Transform decoration in row)
            {
                var element = decoration.GetComponent<LayoutElement>() ?? decoration.gameObject.AddComponent<LayoutElement>();
                element.ignoreLayout = true;
            }
            var layout = row.gameObject.AddComponent<HorizontalLayoutGroup>();
            layout.padding = new RectOffset(56, 50, 0, 0); layout.spacing = 14;
            layout.childAlignment = TextAnchor.MiddleLeft;
            layout.childControlHeight = layout.childControlWidth = true;
            layout.childForceExpandHeight = layout.childForceExpandWidth = false;
            var label = Label("Caption", row, font, caption, 26, 68);
            var labelSize = label.GetComponent<LayoutElement>(); labelSize.preferredWidth = 52; labelSize.flexibleWidth = 0;
            var field = Row(inputName, row, 68);
            var fieldSize = field.GetComponent<LayoutElement>(); fieldSize.preferredWidth = 0; fieldSize.flexibleWidth = 1;
            var background = field.gameObject.AddComponent<Image>(); background.color = Color.clear;
            var input = field.gameObject.AddComponent<InputField>(); input.targetGraphic = background; input.characterLimit = password ? 64 : 20;
            input.contentType = password ? InputField.ContentType.Password : InputField.ContentType.IntegerNumber;
            var value = Label("Value", field, font, "", 26, 68); Stretch(value.rectTransform, 4, 4);
            value.alignment = TextAnchor.MiddleCenter;
            input.textComponent = value;
            if (password)
            {
                var placeholder = Label("Placeholder", field, font, "无需真实密码", 20, 68);
                placeholder.alignment = TextAnchor.MiddleCenter;
                Stretch(placeholder.rectTransform, 4, 4); placeholder.color = new Color(.77f, .71f, .6f, .65f);
                input.placeholder = placeholder;
            }
            else input.text = "10001";
            return input;
        }
        static Button Action(string name, Transform parent, Font font, string caption, float height, Color color, int size)
        {
            var rect = Row(name, parent, height); var image = rect.gameObject.AddComponent<Image>(); image.color = color;
            var action = rect.gameObject.AddComponent<Button>(); action.targetGraphic = image;
            var colors = action.colors; colors.highlightedColor = new Color(1, .93f, .8f); colors.pressedColor = new Color(.7f, .55f, .4f); colors.disabledColor = new Color(.45f, .4f, .35f, .8f); action.colors = colors;
            var label = Label("Label", rect, font, caption, size, height); Stretch(label.rectTransform, 24, 3); label.alignment = TextAnchor.MiddleCenter; label.fontStyle = FontStyle.Bold;
            var shadow = label.gameObject.AddComponent<Shadow>(); shadow.effectColor = new Color(.1f, .02f, 0, .9f); shadow.effectDistance = new Vector2(1, -3);
            return action;
        }
        static void DecoratedFrame(RectTransform parent, bool prominent)
        {
            Frame(parent, 4, new Color(.025f, .018f, .01f));
            var rim = Child("GoldRim", parent); Stretch(rim, 2, 2); Frame(rim, 2, Gold);
            if (!prominent)
            {
                var glint = Child("RimHighlight", parent); Stretch(glint, 5, 5); Frame(glint, 1, new Color(1f, .83f, .51f, .8f));
            }
            var inner = Child("InnerFrame", parent); Stretch(inner, 10, 10);
            Frame(inner, 1, new Color(.83f, .57f, .27f, prominent ? .9f : .55f));
            foreach (var corner in new[] { Vector2.zero, Vector2.right, Vector2.up, Vector2.one })
            {
                var ornament = Child("GoldCorner", parent);
                ornament.anchorMin = ornament.anchorMax = corner; ornament.pivot = Vector2.zero;
                ornament.anchoredPosition = new Vector2(corner.x == 0 ? 7 : -7, corner.y == 0 ? 7 : -7);
                ornament.sizeDelta = new Vector2(46, 46);
                float ornamentScale = prominent ? .74f : 1f;
                ornament.localScale = new Vector3((corner.x == 0 ? 1 : -1) * ornamentScale, (corner.y == 0 ? 1 : -1) * ornamentScale, 1);
                Stroke("OuterFoot", new Vector2(0, 0), new Vector2(43, 3));
                Stroke("OuterStem", new Vector2(0, 0), new Vector2(3, 43));
                Stroke("InnerFoot", new Vector2(8, 8), new Vector2(26, 2));
                Stroke("InnerStem", new Vector2(8, 8), new Vector2(2, 26));
                var flourish = Stroke("Flourish", new Vector2(12, 28), new Vector2(23, 2));
                flourish.localRotation = Quaternion.Euler(0, 0, -45);
                var jewel = Stroke("Jewel", new Vector2(17, 17), new Vector2(6, 6));
                jewel.localRotation = Quaternion.Euler(0, 0, 45);
                RectTransform Stroke(string name, Vector2 position, Vector2 size)
                {
                    var stroke = Child(name, ornament); stroke.anchorMin = stroke.anchorMax = Vector2.zero;
                    stroke.pivot = Vector2.zero; stroke.anchoredPosition = position; stroke.sizeDelta = size;
                    var image = stroke.gameObject.AddComponent<Image>(); image.color = Ivory; image.raycastTarget = false;
                    return stroke;
                }
            }
        }
        static void Frame(RectTransform parent, float width, Color color)
        {
            Edge("Top", new Vector2(0, 1), Vector2.one, new Vector2(0, -width), Vector2.zero);
            Edge("Bottom", Vector2.zero, new Vector2(1, 0), Vector2.zero, new Vector2(0, width));
            Edge("Left", Vector2.zero, new Vector2(0, 1), Vector2.zero, new Vector2(width, 0));
            Edge("Right", new Vector2(1, 0), Vector2.one, new Vector2(-width, 0), Vector2.zero);
            void Edge(string name, Vector2 min, Vector2 max, Vector2 offsetMin, Vector2 offsetMax)
            { var rect = Child(name, parent); rect.anchorMin = min; rect.anchorMax = max; rect.offsetMin = offsetMin; rect.offsetMax = offsetMax; rect.gameObject.AddComponent<LayoutElement>().ignoreLayout = true; var image = rect.gameObject.AddComponent<Image>(); image.color = color; image.raycastTarget = false; }
        }
        static void Stretch(RectTransform rect, float horizontal = 0, float vertical = 0)
        { rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.offsetMin = new Vector2(horizontal, vertical); rect.offsetMax = new Vector2(-horizontal, -vertical); }
        static UIBindings.Binding Binding(string id, Component value)
        { return new UIBindings.Binding { Id = id, Value = value, ComponentType = value.GetType().AssemblyQualifiedName }; }
    }
}
