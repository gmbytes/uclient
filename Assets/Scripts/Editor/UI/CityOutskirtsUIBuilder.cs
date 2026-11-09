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
    public static class CityOutskirtsUIBuilder
    {
        public const string ViewPath = "Assets/Content/UI/CityOutskirts/UICityOutskirts.prefab";
        public const string LayoutPath = "Assets/Content/Maps/CityOutskirts/Beginner.json";
        public const string LightingPath = "Assets/Content/Maps/CityOutskirts/ReferenceLighting.json";
        public const string LightingCyclePath = "Assets/Content/Maps/CityOutskirts/LightingCycle.json";
        private const string Imported = "Assets/Content/CityOutskirts/HYGD/";
        private const string WorldPrefabs = "Art/Environment/Common/main_map/Prefabs/";
        private const string Prefabs = "Assets/Content/Prefabs/CityOutskirts/";
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

        [MenuItem("Game/City Outskirts/Build")]
        public static void Build()
        {
            foreach (string directory in new[] { Path.GetDirectoryName(ViewPath), Prefabs })
                Directory.CreateDirectory(directory);
            AssetDatabase.Refresh();

            var layout = JsonUtility.FromJson<Layout>(AssetDatabase.LoadAssetAtPath<TextAsset>(LayoutPath).text);
            if (layout.cells.Length != OutskirtsHex.Count || layout.cells.Where((cell, i) => cell.pos != i + 1).Any())
                throw new InvalidOperationException("Invalid outskirts cell order.");
            var model = BuildMap(layout);
            var catalog = AssetDatabase.LoadAssetAtPath<UIViewDefinitions>("Assets/Content/UI/Common/UIViewDefinitions.asset");
            var definition = catalog.Definitions.FirstOrDefault(item => item.ViewID == "UICityOutskirts");
            if (definition == null)
            {
                var settings = ScriptableObject.CreateInstance<AdaptiveLayoutDefinition>();
                AssetDatabase.CreateAsset(settings, Path.ChangeExtension(ViewPath, ".Layout.asset"));
                definition = new UIViewDefinition
                {
                    ViewID = "UICityOutskirts", PrefabAddress = ViewPath, Group = UIGroups.Page, LayoutDefinition = settings
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
            config.ViewID = "UICityOutskirts";
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
            root.name = "UICityOutskirts";
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
                map.ConfigureInitialZoom(.70f);
                var hud = safe.Find("Static");
                safe.Find("Dynamic").gameObject.SetActive(false);
                var header = Rect("Header", hud, new Vector2(0, 1), Vector2.one,
                    new Vector2(18, -115), new Vector2(-18, -12));
                var headerImage = header.gameObject.AddComponent<Image>();
                headerImage.color = new Color(.06f, .10f, .07f, .84f);
                headerImage.raycastTarget = false;
                var heading = Label("Heading", "新手领地", header);
                heading.fontSize = 35;
                var back = Button("Back", "返回主城", hud, new Vector2(0, 1), new Vector2(30, -96), new Vector2(238, -24));
                var overview = Button("Overview", "全览", hud, Vector2.one, new Vector2(-178, -96), new Vector2(-30, -24));
                var title = Rect("Detail", hud, new Vector2(0, 0), new Vector2(1, 0), new Vector2(32, 36), new Vector2(-32, 118));
                var card = title.gameObject.AddComponent<Image>();
                card.color = Ink;
                card.raycastTarget = false;
                var detail = Label("Caption", "新手城外  ·  拖拽浏览，点击地块", title);
                var bindings = root.GetComponent<UIBindings>();
                bindings.Configure(new[] { Bind("Map", map), Bind("Back", back), Bind("Overview", overview), Bind("Detail", detail) }, null);
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
            const string path = "Assets/Content/Materials/CityOutskirts/ReferencePostProcess.mat";
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
            var home = BuildReferenceHome();
            var county = LoadOriginal(WorldPrefabs + "wm_playertown_north_lv1.prefab");
            var camp = LoadOriginal(WorldPrefabs + "field2_camp_lv2_01.prefab");
            var resources = new GameObject[4, 8];
            var resourceNames = new[] { "wood", "ore", "iron", "food" };
            for (int type = 0; type < resourceNames.Length; type++)
            for (int level = 1; level <= 8; level++)
                resources[type, level - 1] = LoadOriginal(WorldPrefabs + "resource/field_" + resourceNames[type] + "_lv" + level.ToString("D2") + ".prefab");
            var fence = LoadOriginal(WorldPrefabs + "PlayerBuilding/wm_playerbuild_fence_01.prefab");
            var lowFence = LoadOriginal(WorldPrefabs + "PlayerBuilding/wm_playerbuild_fence_02.prefab");
            var border = LoadOriginal("Art/VFX/Prefab/WorldMap/FX_WorldMap_Occupy_10.prefab");
            var ground = LoadOriginal("GameMain/Scenes/Map_World_04/Ground/GroundTile_9_21.prefab");
            var root = new GameObject("CityOutskirts");
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
                sun.shadowBias = .025f;
                sun.shadowNormalBias = .02f;
                var cycleSettings = JsonUtility.FromJson<OutskirtsLightingSettings>(
                    AssetDatabase.LoadAssetAtPath<TextAsset>(LightingCyclePath).text);
                root.AddComponent<OutskirtsLightingCycle>().Configure(sun, cycleSettings);

                var homeCenter = OutskirtsHex.Center(layout.home);
                foreach (var renderer in meadow.GetComponentsInChildren<MeshRenderer>())
                foreach (var material in renderer.sharedMaterials)
                {
                    material.SetFloat("_LocalGrassland", 1);
                    material.SetVector("_OwnedHome", new Vector4(homeCenter.x, homeCenter.z, OutskirtsHex.TerritoryRadius, 0));
                    EditorUtility.SetDirty(material);
                }
                foreach (var cell in layout.cells)
                {
                    var tile = new GameObject("Tile_" + cell.pos).transform;
                    tile.SetParent(area, false);
                    tile.localPosition = OutskirtsHex.Center(cell.pos);
                    int ring = OutskirtsHex.Distance(cell.pos, layout.home);
                    if (ring == OutskirtsHex.TerritoryRadius)
                    {
                        DrawTerritory(tile, border);
                        if (cell.kind == 0)
                            Place(cell.fenceVariant == 1 ? fence : lowFence, tile, 1.02f, Vector3.up * .003f, cell.fenceAngle);
                    }

                    if (cell.kind == 5)
                    {
                        var bounds = VisualBounds(home.transform);
                        Place(home, tile, Mathf.Max(bounds.size.x, bounds.size.z) / 5f, Vector3.zero);
                    }
                    else if (cell.kind == 16) Place(county, tile, .85f, Vector3.zero);
                    else if (cell.kind == 15) Place(camp, tile, .25f, Vector3.zero, cell.pos * 17 % 90);
                    else if (ring >= OutskirtsHex.TerritoryRadius)
                    {
                        if (cell.resource != OutskirtsResourceKind.None && cell.level > 0)
                            PlaceResource(resources, cell.resource, cell.level, tile);
                        if (cell.level > 0)
                        {
                            bool behindCastle = ring == OutskirtsHex.TerritoryRadius && tile.localPosition.z > homeCenter.z &&
                                Mathf.Abs(tile.localPosition.x - homeCenter.x) < .25f;
                            AddLandLevel(tile, cell.level, behindCastle ? .35f : -.40f);
                        }
                    }
                }

                // Nearby dressing is visible when panning and stays outside the selectable region.
                for (int row = -3; row <= 11; row++)
                for (int col = -3; col <= 7; col++)
                {
                    if (row >= 0 && row < OutskirtsHex.Rows && col >= 0 && col < OutskirtsHex.Columns) continue;
                    int variety = Mathf.Abs(col * 17 + row * 31) % 13;
                    if (variety > 8) continue;
                    var position = new Vector3(col + (row & 1) * .5f + (variety % 3 - 1) * .07f, 0, row * OutskirtsHex.RowStep);
                    var backgroundCell = new GameObject("SceneryCell_" + col + "_" + row).transform;
                    backgroundCell.SetParent(scenery, false);
                    backgroundCell.localPosition = position;
                    int level = Mathf.Abs(col * 37 + row * 19) % 8 + 1;
                    PlaceResource(resources, (OutskirtsResourceKind)(variety % 4 + 1), level, backgroundCell);
                    AddLandLevel(backgroundCell, level);
                }
                return PrefabUtility.SaveAsPrefabAsset(root, Prefabs + "BeginnerOutskirts.prefab");
            }
            finally { Object.DestroyImmediate(root); }
        }

        private static GameObject LoadOriginal(string relativePath)
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(Imported + relativePath);
            if (model == null) throw new InvalidOperationException("Missing imported HYGD prefab: " + relativePath);
            return model;
        }

        private static void PlaceResource(GameObject[,] resources, OutskirtsResourceKind type, int level, Transform tile)
        {
            if ((int)type < 1 || (int)type > 4 || level < 1 || level > 8)
                throw new InvalidOperationException("Unsupported local resource variant.");
            var model = resources[(int)type - 1, level - 1];
            var bounds = VisualBounds(model.transform);
            float footprint = Mathf.Min(.84f, Mathf.Max(bounds.size.x, bounds.size.z) / 5f);
            Place(model, tile, footprint, Vector3.zero);
        }

        private static void AddLandLevel(Transform tile, int level, float depthOffset = -.40f)
        {
            var marker = new GameObject("LandLevel", typeof(TextMesh));
            marker.transform.SetParent(tile, false);
            marker.transform.localPosition = new Vector3(0, .035f, depthOffset);
            marker.transform.localRotation = Quaternion.Euler(42, 0, 0);
            var text = marker.GetComponent<TextMesh>();
            text.font = AssetDatabase.LoadAssetAtPath<Font>(FontPath);
            text.fontSize = 64;
            text.characterSize = .017f;
            text.anchor = TextAnchor.MiddleCenter;
            text.alignment = TextAlignment.Center;
            text.color = new Color(.96f, .96f, .89f, 1);
            text.text = level.ToString();
            var renderer = marker.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = text.font.material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
        }

        private static GameObject BuildReferenceHome()
        {
            var root = new GameObject("ReferenceHome");
            try
            {
                foreach (string name in new[] { "field2_playertown_YG_base_01", "field2_playertown_YG_lv1_07", "field2_playertown_YG_sword_01" })
                {
                    var part = (GameObject)PrefabUtility.InstantiatePrefab(LoadOriginal(WorldPrefabs + name + ".prefab"), root.transform);
                    part.transform.localPosition = Vector3.zero;
                    part.transform.localRotation = Quaternion.identity;
                    foreach (var renderer in part.GetComponentsInChildren<MeshRenderer>())
                    {
                        bool discarded = renderer.sharedMaterials.All(material => material == null || material.shader.name.Contains("Discard"));
                        renderer.shadowCastingMode = discarded ? UnityEngine.Rendering.ShadowCastingMode.Off : UnityEngine.Rendering.ShadowCastingMode.On;
                        renderer.receiveShadows = true;
                    }
                }
                return PrefabUtility.SaveAsPrefabAsset(root, Prefabs + "ReferenceHome.prefab");
            }
            finally { Object.DestroyImmediate(root); }
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
            foreach (string guid in AssetDatabase.FindAssets("t:Material", new[] { Imported.TrimEnd('/') }))
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
                float brightness = terrain ? FloatProperty(saved, "_TextureBright", 1) : 1;
                float detailTiling = terrain ? FloatProperty(saved, "_NormalDetailTilling1", 1) : 1;
                float normalStrength = terrain ? FloatProperty(saved, "_NormalDetailMapScale1", .8f) : 1;
                // HYGD's opaque architecture uses alpha for source-specific channels.
                bool clip = material.name.IndexOf("tree", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    material.name.IndexOf("grass", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    material.name.IndexOf("flower", StringComparison.OrdinalIgnoreCase) >= 0;
                material.shader = shadow ? invisible : border || transparentOverlay ? transparent : surface;
                material.renderQueue = -1;
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

