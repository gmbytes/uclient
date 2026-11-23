using System;
using System.IO;
using System.Linq;
using Client.Framework.Presentation;
using Client.Framework.UI;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace Client.Editor.UI
{
    public static class OutskirtsUIBuilder
    {
        public const string ViewPath = "Assets/Content/UI/Outskirts/UIOutskirts.prefab";
        public const string LayoutPath = "Assets/Content/Maps/Outskirts/Beginner.json";
        public const string LightingPath = "Assets/Content/Maps/Outskirts/ReferenceLighting.json";
        public const string LightingCyclePath = "Assets/Content/Maps/Outskirts/LightingCycle.json";
        private static readonly string[] EnvironmentMaterialDirectories = { "Assets/Content/Materials/Outskirts/Environment", "Assets/Content/Materials/Outskirts/Terrain", "Assets/Content/Materials/Outskirts/Effects" };
        private const string Prefabs = "Assets/Content/Prefabs/Outskirts/";
        private const string FontPath = "Assets/Content/UI/Common/Fonts/NotoSansCJKsc-Regular.otf";
        private static readonly Color Ink = new Color(.10f, .15f, .11f, .96f);
        private static readonly Color Gold = new Color(.98f, .86f, .60f);

        [Serializable] private sealed class Layout { public int home; public Cell[] cells; }
        [Serializable] private sealed class Cell
        {
            public int pos;
            public int kind;
            public int level;
            public OutskirtsResourceKind resource;
            public float fenceAngle;
            public int fenceVariant;
        }
        [Serializable] private sealed class ReferenceLighting
        {
            public Color sunColor;
            public float sunIntensity;
            public Quaternion sunRotation;
            public Color ambientColor;
            public float lightmapScale;
        }

        public static void Build()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Build outskirts assets outside Play Mode; runtime material instances must not be saved into prefabs.");
            foreach (string directory in new[] { Path.GetDirectoryName(ViewPath), Prefabs })
                Directory.CreateDirectory(directory);
            AssetDatabase.Refresh();
            ImportAllMapSprites();

            var layout = JsonUtility.FromJson<Layout>(AssetDatabase.LoadAssetAtPath<TextAsset>(LayoutPath).text);
            if (layout.cells.Length != OutskirtsHex.Count || layout.cells.Where((cell, i) => cell.pos != i + 1).Any())
                throw new InvalidOperationException("Invalid outskirts cell order.");
            var model = BuildMap(layout);
            var catalog = AssetDatabase.LoadAssetAtPath<UIViewDefinitions>("Assets/Content/UI/Common/UIViewDefinitions.asset");
            var definition = catalog.Definitions.FirstOrDefault(item => item.ViewID == "UIOutskirts");
            if (definition == null)
            {
                var settings = ScriptableObject.CreateInstance<AdaptiveLayoutDefinition>();
                AssetDatabase.CreateAsset(settings, Path.ChangeExtension(ViewPath, ".Layout.asset"));
                definition = new UIViewDefinition
                {
                    ViewID = "UIOutskirts", PrefabAddress = ViewPath, Group = UIGroups.Page, LayoutDefinition = settings
                };
                catalog.Configure(catalog.Definitions.Concat(new[] { definition }).ToArray());
                EditorUtility.SetDirty(catalog);
            }
            string contentDirectory = Path.GetDirectoryName(ViewPath).Replace('\\', '/');
            string configPath = Path.ChangeExtension(ViewPath, ".Config.asset");
            var config = AssetDatabase.LoadAssetAtPath<UIWindowConfig>(configPath);
            if (config == null)
            {
                config = ScriptableObject.CreateInstance<UIWindowConfig>();
                AssetDatabase.CreateAsset(config, configPath);
            }
            config.Catalog = catalog;
            config.ViewID = "UIOutskirts";
            EditorUtility.SetDirty(config);
            string textPath = Path.ChangeExtension(ViewPath, ".Text.asset");
            if (AssetDatabase.LoadAssetAtPath<UITextDefinition>(textPath) == null)
            {
                var texts = ScriptableObject.CreateInstance<UITextDefinition>();
                texts.Entries = new[]
                {
                    new UITextDefinition.Entry { Key = "Heading", Value = "新手领地" },
                    new UITextDefinition.Entry { Key = "Back", Value = "返回主城" },
                    new UITextDefinition.Entry { Key = "Overview", Value = "全览" },
                    new UITextDefinition.Entry { Key = "Detail", Value = "新手城外  ·  拖拽浏览，点击地块" }
                };
                AssetDatabase.CreateAsset(texts, textPath);
            }

            var template = AssetDatabase.LoadAssetAtPath<GameObject>(UIWindowCreator.TemplatePath);
            var root = Object.Instantiate(template);
            root.name = "UIOutskirts";
            try
            {
                var node = root.transform.Find("UINode");
                var safe = node.Find("SafeArea");
                var mapRect = Rect("Map", node.Find("Background"), Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
                mapRect.gameObject.AddComponent<RawImage>();
                var map = mapRect.gameObject.AddComponent<CityViewport>();
                map.Configure(model, mapRect);
                map.ConfigurePostProcessing(BuildPostProcess());
                float lastRow = (OutskirtsHex.Rows - 1) * OutskirtsHex.RowStep;
                var bounds = new Bounds(new Vector3((OutskirtsHex.Columns - .5f) * .5f, 0, lastRow * .5f),
                    new Vector3(OutskirtsHex.Columns + .5f, 1, lastRow + 2 * OutskirtsHex.Radius));
                map.ConfigureGroundView(OutskirtsHex.Center(layout.home), bounds);
                map.ConfigureInitialZoom(.52f);
                map.ConfigureReferenceDistance((40 - (-11)) / 5f);
                var hud = safe.Find("Static");
                safe.Find("Dynamic").gameObject.SetActive(false);
                var header = Rect("Header", hud, new Vector2(0, 1), Vector2.one,
                    new Vector2(18, -96), new Vector2(-18, -12));
                var headerImage = header.gameObject.AddComponent<Image>();
                headerImage.color = new Color(.06f, .10f, .07f, .84f);
                headerImage.raycastTarget = false;
                var heading = Label("Heading", "新手领地", header);
                heading.fontSize = 30;
                var back = Button("Back", "返回主城", hud, new Vector2(0, 1), new Vector2(30, -80), new Vector2(210, -26));
                var overview = Button("Overview", "全览", hud, Vector2.one, new Vector2(-170, -80), new Vector2(-30, -26));
                var timeIds = new[] { "TimeDawn", "TimeDay", "TimeDusk", "TimeNight" };
                var timeCaptions = new[] { "清晨", "白天", "黄昏", "夜晚" };
                var timeButtons = new Button[4];
                for (int i = 0; i < timeButtons.Length; i++)
                {
                    float x = -450 + i * 230;
                    timeButtons[i] = Button(timeIds[i], timeCaptions[i], hud, new Vector2(.5f, 1),
                        new Vector2(x, -176), new Vector2(x + 210, -118));
                    timeButtons[i].GetComponentInChildren<Text>().fontSize = 24;
                }
                var title = Rect("Detail", hud, new Vector2(0, 0), new Vector2(1, 0), new Vector2(32, 26), new Vector2(-32, 90));
                var card = title.gameObject.AddComponent<Image>();
                card.color = Ink;
                card.raycastTarget = false;
                var detail = Label("Caption", "新手城外  ·  拖拽浏览，点击地块", title);
                var bindings = root.GetComponent<UIBindings>();
                bindings.Configure(new[] { Bind("Map", map), Bind("Back", back), Bind("Overview", overview), Bind("Detail", detail) }
                    .Concat(timeButtons.Select((button, i) => Bind(timeIds[i], button))).ToArray(), null);
                var adaptive = safe.GetComponent<AdaptiveLayout>();
                adaptive.Configure(definition.LayoutDefinition, null, null, null, null, null);
                root.GetComponent<UIViewBridge>().Configure(node.GetComponent<Canvas>(), node.GetComponent<CanvasGroup>(),
                    bindings, adaptive, null, null, safe.GetComponent<SafeAreaRoot>());
                PrefabUtility.SaveAsPrefabAsset(root, ViewPath);
                var generated = UIBindingGenerator.Regenerate(bindings, contentDirectory);
                if (!generated.Success) throw new InvalidOperationException(generated.Error);
            }
            finally { Object.DestroyImmediate(root); }

            const string cityPath = "Assets/Content/UI/MainCity/UIMainCity.prefab";
            var city = PrefabUtility.LoadPrefabContents(cityPath);
            try { AttachEntry(city); PrefabUtility.SaveAsPrefabAsset(city, cityPath); }
            finally { PrefabUtility.UnloadPrefabContents(city); }
            AssetDatabase.SaveAssets();
        }

        public static void AttachEntry(GameObject root)
        {
            var hud = root.transform.Find("UINode/SafeArea/Static");
            if (hud.Find("OutskirtsButton") != null) return;
            Button("OutskirtsButton", "城外", hud, new Vector2(1, .75f), new Vector2(-184, -36), new Vector2(-30, 36));
        }

        private static Material BuildPostProcess()
        {
            const string path = "Assets/Content/Materials/Outskirts/ReferencePostProcess.mat";
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            var shader = Shader.Find("Hidden/Client/OutskirtsPostProcess");
            if (shader == null) throw new InvalidOperationException("Missing outskirts post-process shader.");
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(shader);
                AssetDatabase.CreateAsset(material, path);
            }
            material.shader = shader;
            // Map_World_04's shared Map_World_100X100_Volume, including non-overridden defaults.
            material.SetFloat("_Threshold", .8f);
            material.SetFloat("_Intensity", .5f);
            material.SetFloat("_Scatter", .5f);
            material.SetVector("_Vignette", new Vector4(.5f, .65f, .168f, .28f));
            EditorUtility.SetDirty(material);
            return material;
        }

        private static GameObject BuildMap(Layout layout)
        {
            var lighting = JsonUtility.FromJson<ReferenceLighting>(AssetDatabase.LoadAssetAtPath<TextAsset>(LightingPath).text);
            AdaptImportedMaterials(lighting);
            var composition = JsonUtility.FromJson<Composition>(File.ReadAllText("Assets/Content/Maps/Outskirts/BeginnerComposition.json"));
            if (composition.cells.Length != OutskirtsHex.Count) throw new InvalidOperationException("Expected 45 composition cells.");
            var border = LoadEnvironmentPrefab("Effects/FX_WorldMap_Occupy_10.prefab");
            var ground = LoadEnvironmentPrefab("Terrain/Map04/GroundTile_9_21.prefab");
            var root = new GameObject("Outskirts");
            try
            {
                var area = new GameObject("BuildingRoot").transform;
                area.SetParent(root.transform, false);
                var scenery = new GameObject("Landscape").transform;
                scenery.SetParent(root.transform, false);
                // HYGD's 325-unit terrain chunk spans 65 five-unit cells.
                var meadow = Place(ground, scenery, 65, new Vector3(2.25f, 0, 3.46f));
                // A full world-map chunk has a larger elevation range than this single gameplay area.
                var groundBounds = VisualBounds(meadow);
                float heightScale = Mathf.Min(1, .024f / Mathf.Max(.001f, groundBounds.size.y));
                meadow.localScale = new Vector3(meadow.localScale.x, meadow.localScale.y * heightScale, meadow.localScale.z);
                meadow.localPosition += Vector3.down * (groundBounds.size.y * heightScale + .015f);

                var sun = new GameObject("HYGD reference light", typeof(Light)).GetComponent<Light>();
                sun.transform.SetParent(scenery, false);
                sun.transform.localRotation = lighting.sunRotation;
                sun.type = LightType.Directional;
                sun.color = lighting.sunColor;
                sun.intensity = lighting.sunIntensity;
                sun.shadows = LightShadows.Soft;
                sun.shadowCustomResolution = 4096;
                sun.shadowStrength = 1;
                sun.shadowBias = .005f;
                sun.shadowNormalBias = .004f;
                var cycleSettings = JsonUtility.FromJson<OutskirtsLightingSettings>(
                    AssetDatabase.LoadAssetAtPath<TextAsset>(LightingCyclePath).text);
                root.AddComponent<OutskirtsLightingCycle>().Configure(sun, cycleSettings);

                var homeCenter = OutskirtsHex.Center(layout.home);
                foreach (var renderer in meadow.GetComponentsInChildren<MeshRenderer>())
                foreach (var material in renderer.sharedMaterials)
                {
                    material.SetFloat("_LocalGrassland", 0);
                    material.SetFloat("_TerrainFlattenScale", heightScale);
                    material.SetVector("_OwnedHome", new Vector4(homeCenter.x, homeCenter.z, OutskirtsHex.TerritoryRadius, 0));
                    EditorUtility.SetDirty(material);
                }
                foreach (var cell in layout.cells)
                {
                    var tile = new GameObject("Tile_" + cell.pos).transform;
                    tile.SetParent(area, false);
                    tile.localPosition = OutskirtsHex.Center(cell.pos);
                    DrawTerritory(tile, border);

                    var center = tile.localPosition;
                    int row = Mathf.RoundToInt(center.z / OutskirtsHex.RowStep);
                    int column = Mathf.RoundToInt(center.x - (row & 1) * .5f);
                    int index = (OutskirtsHex.Rows - 1 - row) * OutskirtsHex.Columns + column;
                    var placement = composition.cells[index];
                    tile.gameObject.AddComponent<OutskirtsResourceIdentity>().Configure(placement.sprite);
                    if (!string.IsNullOrEmpty(placement.model))
                    {
                        var model = Place(LoadEnvironmentPrefab("Resources/" + placement.model + ".prefab"),
                            tile, placement.width, Vector3.up * .012f);
                        model.name = "ResourceModel";
                        foreach (var renderer in model.GetComponentsInChildren<MeshRenderer>())
                        {
                            bool invisible = renderer.sharedMaterials.All(material => material == null || material.shader.name.Contains("Discard"));
                            renderer.shadowCastingMode = invisible ? UnityEngine.Rendering.ShadowCastingMode.Off : UnityEngine.Rendering.ShadowCastingMode.TwoSided;
                            renderer.receiveShadows = true;
                        }
                    }
                    else PlaceMapSprite(placement.sprite, tile, "ResourceSprite", placement.width);
                    // Meadow PNG is a subtle accent, leaving clear ground rather than identical dark clumps.
                    if (placement.sprite == "S_map_4_1") tile.Find("ResourceSprite").GetComponent<SpriteRenderer>().color = new Color(1, 1, 1, .16f);
                    if (placement.sprite == "S_map_12_1") MarkOwnCity(tile);
                    if (placement.shadow > 0) AddContactShadow(tile, placement.width, placement.shadow);
                }

                foreach (var renderer in root.GetComponentsInChildren<MeshRenderer>(true))
                {
                    if (!renderer.enabled || renderer.GetComponent<MeshFilter>() == null) continue;
                    if (renderer.sharedMaterials.Length == 0 || renderer.sharedMaterials.Any(material => material == null))
                        throw new InvalidOperationException("Missing outskirts material on " + renderer.name);
                    if (renderer.sharedMaterials.Any(material => string.IsNullOrEmpty(AssetDatabase.GetAssetPath(material))))
                        throw new InvalidOperationException("Non-persistent outskirts material on " + renderer.name);
                }
                return PrefabUtility.SaveAsPrefabAsset(root, Prefabs + "BeginnerOutskirts.prefab");
            }
            finally { Object.DestroyImmediate(root); }
        }

        private static GameObject LoadEnvironmentPrefab(string relativePath)
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(Prefabs + relativePath);
            if (model == null) throw new InvalidOperationException("Missing outskirts environment prefab: " + relativePath);
            return model;
        }

        private static void ImportAllMapSprites()
        {
            const string directory = "Assets/Content/Textures/Outskirts/Fields/";
            var entries = JsonUtility.FromJson<SpriteManifest>(File.ReadAllText(directory + "sprites.json")).sprites;
            foreach (var entry in entries)
            {
                var importer = (TextureImporter)AssetImporter.GetAtPath(directory + entry.name + ".png");
                var pivot = new Vector2(entry.pivotX, entry.pivotY);
                if (importer.textureType == TextureImporterType.Sprite && importer.spritePivot == pivot &&
                    importer.textureCompression == TextureImporterCompression.Uncompressed && importer.maxTextureSize == 2048) continue;
                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;
                importer.spritePixelsPerUnit = 100;
                importer.spritePivot = pivot;
                var settings = new TextureImporterSettings(); importer.ReadTextureSettings(settings);
                settings.spriteAlignment = (int)SpriteAlignment.Custom;
                importer.SetTextureSettings(settings);
                importer.alphaIsTransparency = true;
                importer.filterMode = FilterMode.Bilinear;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.maxTextureSize = 2048;
                importer.mipmapEnabled = false;
                importer.SaveAndReimport();
            }
        }

        private static void PlaceMapSprite(string spriteName, Transform tile, string objectName, float width)
        {
            const string directory = "Assets/Content/Textures/Outskirts/Fields/";
            var entry = JsonUtility.FromJson<SpriteManifest>(File.ReadAllText(directory + "sprites.json")).sprites
                .Single(item => item.name == spriteName);
            string path = directory + entry.name + ".png";
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            if (importer.textureType != TextureImporterType.Sprite || importer.spritePivot != new Vector2(entry.pivotX, entry.pivotY))
            {
                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;
                importer.spritePixelsPerUnit = 100;
                importer.filterMode = FilterMode.Bilinear;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.maxTextureSize = 2048;
                importer.spritePivot = new Vector2(entry.pivotX, entry.pivotY);
                var settings = new TextureImporterSettings(); importer.ReadTextureSettings(settings);
                settings.spriteAlignment = (int)SpriteAlignment.Custom;
                importer.SetTextureSettings(settings);
                importer.alphaIsTransparency = true;
                importer.mipmapEnabled = false;
                importer.SaveAndReimport();
            }
            var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
            if (sprite == null) throw new InvalidOperationException("Missing resource sprite: " + path);
            var visual = new GameObject(objectName, typeof(SpriteRenderer));
            visual.transform.SetParent(tile, false);
            visual.transform.localPosition = new Vector3(0, .035f, 0);
            visual.transform.localRotation = Quaternion.Euler(CityViewport.OutskirtsSpritePitch, 0, 0);
            visual.transform.localScale = Vector3.one * (width * 100 / entry.width);
            var renderer = visual.GetComponent<SpriteRenderer>();
            renderer.sprite = sprite;
            const string materialPath = "Assets/Content/Materials/Outskirts/OutskirtsSprite.mat";
            var spriteMaterial = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
            if (spriteMaterial == null)
            {
                spriteMaterial = new Material(Shader.Find("Client/OutskirtsSprite"));
                AssetDatabase.CreateAsset(spriteMaterial, materialPath);
            }
            renderer.sharedMaterial = spriteMaterial;
            renderer.sortingOrder = 1000 - Mathf.RoundToInt(tile.localPosition.z * 100);
        }

        [Serializable] private sealed class SpriteManifest { public SpriteEntry[] sprites; }
        [Serializable] private sealed class SpriteEntry { public string name; public float pivotX, pivotY; public int width; }

        [Serializable] private sealed class Composition { public Placement[] cells; }
        [Serializable] private sealed class Placement { public string sprite, model; public float width, shadow; }

        private static void AddContactShadow(Transform tile, float width, float opacity)
        {
            // Analytic soft decal: no changes to the supplied PNG files.
            string path = "Assets/Content/Materials/Outskirts/ContactShadow_" + Mathf.RoundToInt(opacity * 100) + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null) { material = new Material(Shader.Find("Client/OutskirtsContactShadow")); AssetDatabase.CreateAsset(material, path); }
            material.SetColor("_Color", new Color(.08f, .12f, .07f, opacity));
            EditorUtility.SetDirty(material);
            var shadow = GameObject.CreatePrimitive(PrimitiveType.Quad);
            shadow.name = "ContactShadow";
            Object.DestroyImmediate(shadow.GetComponent<Collider>());
            shadow.transform.SetParent(tile, false);
            shadow.transform.localPosition = new Vector3(.07f, .018f, -.055f);
            shadow.transform.localRotation = Quaternion.Euler(90, 0, 0);
            shadow.transform.localScale = new Vector3(width * .86f, width * .48f, 1);
            var renderer = shadow.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
        }

        private static void DrawTerritory(Transform tile, GameObject source)
        {
            var bounds = VisualBounds(source.transform);
            var border = Place(source, tile, Mathf.Max(bounds.size.x, bounds.size.z) / 5f, Vector3.up * .012f);
            foreach (var renderer in border.GetComponentsInChildren<MeshRenderer>())
            {
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                renderer.receiveShadows = false;
            }
        }

        private static void MarkOwnCity(Transform tile)
        {
            var border = tile.Find("FX_WorldMap_Occupy_10");
            foreach (var renderer in border.GetComponentsInChildren<MeshRenderer>())
            {
                var materials = renderer.sharedMaterials;
                for (int i = 0; i < materials.Length; i++)
                {
                    string path = "Assets/Content/Materials/Outskirts/OwnCity_" + materials[i].name + ".mat";
                    var material = AssetDatabase.LoadAssetAtPath<Material>(path);
                    if (material == null)
                    {
                        material = new Material(materials[i]);
                        AssetDatabase.CreateAsset(material, path);
                    }
                    material.color = new Color(1f, .82f, .32f, .8f);
                    material.renderQueue = 2970;
                    EditorUtility.SetDirty(material);
                    materials[i] = material;
                }
                renderer.sharedMaterials = materials;
            }
            var label = new GameObject("OwnCityLabel", typeof(TextMesh));
            label.transform.SetParent(tile, false);
            label.transform.localPosition = new Vector3(0, .5f, .08f);
            label.transform.localRotation = Quaternion.Euler(CityViewport.OutskirtsSpritePitch, 0, 0);
            var text = label.GetComponent<TextMesh>();
            text.font = AssetDatabase.LoadAssetAtPath<Font>(FontPath);
            text.fontSize = 64;
            text.characterSize = .014f;
            text.anchor = TextAnchor.MiddleCenter;
            text.alignment = TextAlignment.Center;
            text.color = Gold;
            text.text = "我的主城";
            var mesh = label.GetComponent<MeshRenderer>();
            mesh.sharedMaterial = text.font.material;
            mesh.sortingOrder = 2000;
            mesh.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        private static Transform Place(GameObject source, Transform anchor, float footprint, Vector3 offset, float yaw = 0, float pitch = 0)
        {
            var wrapper = new GameObject(source.name).transform;
            wrapper.SetParent(anchor, false);
            var visual = (GameObject)PrefabUtility.InstantiatePrefab(source, wrapper);
            visual.transform.localPosition = Vector3.zero;
            visual.transform.localRotation = Quaternion.Euler(pitch, 0, 0);
            var bounds = VisualBounds(visual.transform);
            visual.transform.position -= new Vector3(bounds.center.x - wrapper.position.x, bounds.min.y - wrapper.position.y,
                bounds.center.z - wrapper.position.z);
            wrapper.localScale = Vector3.one * (footprint / Mathf.Max(bounds.size.x, bounds.size.z, .001f));
            wrapper.localPosition = offset;
            wrapper.localRotation = Quaternion.Euler(0, yaw, 0);
            float nativeHeightScale = 1 / wrapper.localScale.y;
            wrapper.gameObject.AddComponent<OutskirtsModelHeight>().Configure(nativeHeightScale,
                bounds.min.y - offset.y * nativeHeightScale);
            return wrapper;
        }

        private static Bounds VisualBounds(Transform root)
        {
            var renderers = root.GetComponentsInChildren<MeshRenderer>().Where(renderer =>
                renderer.enabled && !renderer.sharedMaterials.All(material =>
                    material != null && material.name.IndexOf("shadow", StringComparison.OrdinalIgnoreCase) >= 0)).ToArray();
            if (renderers.Length == 0) throw new InvalidOperationException("HYGD prefab has no visible mesh: " + root.name);
            var bounds = renderers[0].bounds;
            foreach (var renderer in renderers) bounds.Encapsulate(renderer.bounds);
            return bounds;
        }

        private static void AdaptImportedMaterials(ReferenceLighting lighting)
        {
            var surface = Shader.Find("Client/OutskirtsSurface");
            var transparent = Shader.Find("Sprites/Default");
            var invisible = Shader.Find("Hidden/Client/OutskirtsDiscard");
            if (surface == null || transparent == null || invisible == null)
                throw new InvalidOperationException("Outskirts compatibility shaders are missing.");
            foreach (string guid in AssetDatabase.FindAssets("t:Material", EnvironmentMaterialDirectories.Where(AssetDatabase.IsValidFolder).ToArray()))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (!path.EndsWith(".mat", StringComparison.OrdinalIgnoreCase)) continue;
                var material = AssetDatabase.LoadAssetAtPath<Material>(path);
                var saved = new SerializedObject(material).FindProperty("m_SavedProperties");
                bool terrain = material.name.StartsWith("Map_World_", StringComparison.Ordinal);
                bool occupied = material.name.StartsWith("OccupylLine", StringComparison.OrdinalIgnoreCase);
                bool border = occupied || material.name.StartsWith("ControlLine", StringComparison.OrdinalIgnoreCase);
                bool shadow = material.name.IndexOf("shadow", StringComparison.OrdinalIgnoreCase) >= 0;
                var particleTexture = ReadTexture(saved, "_MainTextureA") ?? ReadTexture(saved, "_BaseTexA");
                bool particle = particleTexture.HasValue;
                bool transparentOverlay = particle || FloatProperty(saved, "_Surface", 0) > .5f;
                var albedo = particle ? particleTexture : terrain || occupied ? ReadTexture(saved, "_MainTex") :
                    ReadTexture(saved, "_Albedo") ?? ReadTexture(saved, "_BaseMap") ?? ReadTexture(saved, "_MainTex");
                var dirt = terrain ? ReadTexture(saved, "_MainTex2") : null;
                var soil = terrain ? ReadTexture(saved, "_TerrainMask") : null;
                var normal = terrain ? ReadTexture(saved, "_Normal_detail") :
                    ReadTexture(saved, "_NormalMap") ?? ReadTexture(saved, "_Normalmap") ?? ReadTexture(saved, "_BumpMap");
                bool useNormal = normal.HasValue &&
                    AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(normal.Value.Texture)) is TextureImporter importer &&
                    importer.textureType == TextureImporterType.NormalMap;
                var tint = terrain ? Color.white : particle ? ColorProperty(saved, "_Color") ?? Color.white : border ? ColorProperty(saved, "_MainColor") ?? Color.white :
                    ColorProperty(saved, "_Maincolor") ?? ColorProperty(saved, "_BaseColor") ?? ColorProperty(saved, "_Color") ?? Color.white;
                if (border) tint = new Color(.58f, .64f, .34f, .32f);
                float brightness = terrain ? FloatProperty(saved, "_TextureBright", 1) : 1;
                float detailTiling = terrain ? FloatProperty(saved, "_NormalDetailTilling1", 1) : 1;
                float normalStrength = terrain ? FloatProperty(saved, "_NormalDetailMapScale1", .8f) : 1;
                // HYGD's opaque architecture uses alpha for source-specific channels.
                bool clip = material.name.IndexOf("tree", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    material.name.IndexOf("grass", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    material.name.IndexOf("flower", StringComparison.OrdinalIgnoreCase) >= 0;
                material.shader = shadow ? invisible : border || transparentOverlay ? transparent : surface;
                material.renderQueue = border ? 2970 : -1;
                if (!shadow)
                {
                    ApplyTexture(material, "_MainTex", albedo);
                    material.color = tint;
                    if (!border && !transparentOverlay)
                    {
                        material.SetVector("_ReferenceSun", lighting.sunColor.linear * lighting.sunIntensity);
                        material.SetVector("_ReferenceAmbient", lighting.ambientColor.linear);
                        material.SetFloat("_Terrain", terrain ? 1 : 0);
                        material.SetFloat("_WorldTerrainOverlay", terrain ? 1 : 0);
                        if (terrain)
                        {
                            foreach (string property in new[] { "_DirtMaskSize1", "_DirtMaskSize2", "_dirtsize", "_dirtpower", "_ColorMixMaskSize1", "_ColorMixMaskSize2", "_colorlightsize" })
                                material.SetFloat(property, FloatProperty(saved, property, material.GetFloat(property)));
                            material.SetColor("_ColorMix1", ColorProperty(saved, "_ColorMix1") ?? Color.white);
                            material.SetFloat("_BaseLayerByGon", FloatProperty(saved, "_BaseLayerByGon", 0));
                            material.SetColor("_WaterColor", ColorProperty(saved, "_watercolor") ?? new Color(.285048f, .8446f, .96226f));
                            ApplyTexture(material, "_Layer2Tex", ReadTexture(saved, "_MainTex3"));
                            ApplyTexture(material, "_Layer2Dirt", ReadTexture(saved, "_MainTex4"));
                            ApplyTexture(material, "_Layer3Tex", ReadTexture(saved, "_MainTex5"));
                            ApplyTexture(material, "_Layer3Dirt", ReadTexture(saved, "_MainTex6"));
                            ApplyTexture(material, "_TerrainLayers", ReadTexture(saved, "_TerrainMaskTexture"));
                            ApplyTexture(material, "_MacroNormal", ReadTexture(saved, "_Normal1"));
                            material.SetFloat("_MacroNormalStrength", FloatProperty(saved, "_NormalMapScale", .877f));
                            ApplyTexture(material, "_Layer2Normal", ReadTexture(saved, "_Normal_detail_2"), FloatProperty(saved, "_NormalDetailTilling2", 300));
                            ApplyTexture(material, "_Layer3Normal", ReadTexture(saved, "_Normal_detail_3"), FloatProperty(saved, "_NormalDetailTilling3", 400));
                            material.SetFloat("_Layer2NormalStrength", FloatProperty(saved, "_NormalDetailMapScale2", .611f));
                            material.SetFloat("_Layer3NormalStrength", FloatProperty(saved, "_NormalDetailMapScale3", .719f));
                            material.SetColor("_ColorMix2", ColorProperty(saved, "_ColorMix2") ?? Color.white);
                            material.SetColor("_ColorMix3", ColorProperty(saved, "_ColorMix3") ?? Color.white);
                        }
                        ApplyTexture(material, "_DirtTex", dirt);
                        ApplyTexture(material, "_TerrainMask", soil);
                        if (useNormal) ApplyTexture(material, "_NormalMap", normal, detailTiling);
                        material.SetFloat("_NormalStrength", useNormal ? normalStrength : 0);
                        material.SetFloat("_TextureBright", brightness);
                        material.SetFloat("_Cutoff", clip ? .4f : 0);
                        material.SetFloat("_Cull", clip ? 0 : 2);
                        var ao = ReadTexture(saved, "_AO_mask") ?? ReadTexture(saved, "_AO");
                        material.SetFloat("_UseBakedAO", ao.HasValue && !terrain ? 1 : 0);
                        ApplyTexture(material, "_BakedAO", ao);
                        material.SetFloat("_AOScale", FloatProperty(saved, "_AOScale", 0));
                        var bakedLight = ReadTexture(saved, "_Lightmap");
                        ApplyTexture(material, "_BakedLightmap", bakedLight);
                        material.SetFloat("_UseBakedLightmap", !terrain && bakedLight.HasValue &&
                            FloatProperty(saved, "_LightmapOn", 0) > .5f ? 1 : 0);
                        material.SetFloat("_BakedLightmapScale", lighting.lightmapScale);
                        material.SetFloat("_EmissiveStrength", terrain ? 0 : FloatProperty(saved, "_EmissiveScale", 0));
                        material.SetFloat("_EmissionFromAlbedoAlpha", FloatProperty(saved, "_EmissivebyBasemap", 0));
                        material.SetFloat("_HeightLightEnabled", terrain ? 0 : FloatProperty(saved, "_Heightlightswitch", 0));
                        material.SetFloat("_HeightLightBright", FloatProperty(saved, "_heightlight", 1));
                        material.SetFloat("_HeightLightDark", FloatProperty(saved, "_Heightlightdark", 1));
                        material.SetFloat("_HeightLightHeight", FloatProperty(saved, "_brightHeight", 1));
                        material.SetFloat("_HeightLightRange", FloatProperty(saved, "_heightrange", 1));
                    }
                }
                EditorUtility.SetDirty(material);
            }
        }

        private static SerializedProperty SavedValue(SerializedProperty saved, string arrayName, string property)
        {
            var list = saved.FindPropertyRelative(arrayName);
            for (int i = 0; i < list.arraySize; i++)
            {
                var pair = list.GetArrayElementAtIndex(i);
                if (pair.FindPropertyRelative("first").stringValue == property) return pair.FindPropertyRelative("second");
            }
            return null;
        }

        private static (Texture Texture, Vector2 Scale, Vector2 Offset)? ReadTexture(SerializedProperty saved, string property)
        {
            var value = SavedValue(saved, "m_TexEnvs", property);
            var texture = value?.FindPropertyRelative("m_Texture").objectReferenceValue as Texture;
            if (texture == null) return null;
            return (texture, value.FindPropertyRelative("m_Scale").vector2Value, value.FindPropertyRelative("m_Offset").vector2Value);
        }

        private static void ApplyTexture(Material material, string property,
            (Texture Texture, Vector2 Scale, Vector2 Offset)? source, float tiling = 1)
        {
            if (!source.HasValue) return;
            var value = source.Value;
            material.SetTexture(property, value.Texture);
            material.SetTextureScale(property, value.Scale * tiling);
            material.SetTextureOffset(property, value.Offset);
        }

        private static Color? ColorProperty(SerializedProperty saved, string property)
            => SavedValue(saved, "m_Colors", property)?.colorValue;
        private static float FloatProperty(SerializedProperty saved, string property, float fallback)
            => SavedValue(saved, "m_Floats", property)?.floatValue ?? fallback;

        private static RectTransform Rect(string name, Transform parent, Vector2 min, Vector2 max, Vector2 insetMin, Vector2 insetMax)
        {
            var rect = (RectTransform)new GameObject(name, typeof(RectTransform)).transform;
            rect.SetParent(parent, false);
            rect.anchorMin = min;
            rect.anchorMax = max;
            rect.offsetMin = insetMin;
            rect.offsetMax = insetMax;
            return rect;
        }

        private static Button Button(string name, string caption, Transform parent, Vector2 anchor, Vector2 min, Vector2 max)
        {
            var rect = Rect(name, parent, anchor, anchor, min, max);
            var image = rect.gameObject.AddComponent<Image>();
            image.color = Ink;
            var outline = rect.gameObject.AddComponent<Outline>();
            outline.effectColor = Gold;
            var button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            Label("Caption", caption, rect);
            return button;
        }

        private static Text Label(string name, string caption, Transform parent)
        {
            var rect = Rect(name, parent, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            var label = rect.gameObject.AddComponent<Text>();
            label.font = AssetDatabase.LoadAssetAtPath<Font>(FontPath);
            label.fontSize = 30;
            label.text = caption;
            label.color = Gold;
            label.alignment = TextAnchor.MiddleCenter;
            label.raycastTarget = false;
            return label;
        }

        private static UIBindings.Binding Bind(string id, Component component) => new UIBindings.Binding
        {
            Id = id, Value = component, ComponentType = component.GetType().AssemblyQualifiedName
        };
    }
}

