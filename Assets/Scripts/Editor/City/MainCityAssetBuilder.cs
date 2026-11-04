using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace Client.Editor.City
{
    /// <summary>Validates the imported art and removes all legacy component bindings.</summary>
    public static class MainCityAssetBuilder
    {
        public const string PrefabPath = "Assets/Content/Prefabs/MainCity/MainCity.prefab";
        private const string MaterialPath = "Assets/Content/Materials/MainCity";
        private const string ValidationReportPath = "Builds/MainCity/ResourceValidation.txt";
        private static readonly string[] ResourceRoots =
        {
            "Assets/Content/Prefabs/MainCity",
            "Assets/Content/Models/MainCity",
            "Assets/Content/Textures/MainCity",
            MaterialPath,
            "Assets/Content/Shaders/MainCity"
        };

        [MenuItem("Game/MainCity/Prepare Resources")]
        public static void PrepareResources()
        {
            if (EditorApplication.isPlaying)
                throw new InvalidOperationException("Stop Play Mode before preparing main-city resources.");
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            if (AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath) == null)
                throw new FileNotFoundException("Migrated main-city prefab is missing.", PrefabPath);

            var issues = new List<string>();
            var fixedMaterials = 0;
            var materialPaths = AssetDatabase.FindAssets("t:Material", new[] { MaterialPath })
                .Select(AssetDatabase.GUIDToAssetPath).ToArray();
            foreach (var path in materialPaths)
            {
                var material = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (material == null) { issues.Add("Material cannot be loaded: " + path); continue; }
                if (material.shader != null && material.shader.isSupported && !ShaderUtil.ShaderHasError(material.shader)) continue;
                RestoreCompatibleShader(material);
                fixedMaterials++;
            }

            var city = PrefabUtility.LoadPrefabContents(PrefabPath);
            var removedScripts = 0;
            var renderers = 0;
            var meshes = 0;
            try
            {
                foreach (var behaviour in city.GetComponentsInChildren<MonoBehaviour>(true))
                    if (behaviour != null) { UnityEngine.Object.DestroyImmediate(behaviour); removedScripts++; }
                foreach (var item in city.GetComponentsInChildren<Transform>(true))
                {
                    removedScripts += GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(item.gameObject);
                    GameObjectUtility.RemoveMonoBehavioursWithMissingScript(item.gameObject);
                    item.gameObject.layer = 0;
                }
                foreach (var collider in city.GetComponentsInChildren<Collider>(true)) UnityEngine.Object.DestroyImmediate(collider);
                foreach (var body in city.GetComponentsInChildren<Rigidbody>(true)) UnityEngine.Object.DestroyImmediate(body);
                foreach (var renderer in city.GetComponentsInChildren<Renderer>(true))
                {
                    renderers++;
                    if (renderer.sharedMaterials.Length == 0 || renderer.sharedMaterials.Any(m => m == null))
                        issues.Add("Missing material on " + HierarchyPath(renderer.transform));
                }
                foreach (var filter in city.GetComponentsInChildren<MeshFilter>(true))
                {
                    if (filter.sharedMesh == null) issues.Add("Missing mesh on " + HierarchyPath(filter.transform));
                    else meshes++;
                }
                foreach (var skin in city.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    if (skin.sharedMesh == null) issues.Add("Missing skinned mesh on " + HierarchyPath(skin.transform));
                    else meshes++;
                }
                var buildings = city.transform.Find("BuildingRoot");
                if (buildings == null) issues.Add("BuildingRoot is missing.");
                else
                {
                    // The original map has no buildParent6; keep its exact visual hierarchy.
                    foreach (var id in Enumerable.Range(1, 16).Where(id => id != 6))
                        if (buildings.Find("buildParent" + id) == null) issues.Add("Missing building parent " + id);
                }
                city.name = "MainCity";
                city.transform.localScale = Vector3.one;
                city.SetActive(true);
                if (issues.Count == 0) PrefabUtility.SaveAsPrefabAsset(city, PrefabPath);
            }
            finally { PrefabUtility.UnloadPrefabContents(city); }

            foreach (var dependency in AssetDatabase.GetDependencies(PrefabPath, true))
            {
                if (dependency.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) || dependency.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                    issues.Add("Code dependency is forbidden: " + dependency);
            }
            ValidateGuidReferences(issues);
            AssetDatabase.SaveAssets();
            var report = new StringBuilder();
            report.AppendLine("Main city resource validation");
            report.AppendLine("Prefab: " + PrefabPath);
            report.AppendLine("Renderers: " + renderers + "; meshes: " + meshes + "; materials: " + materialPaths.Length);
            report.AppendLine("Removed scripts: " + removedScripts + "; repaired shaders: " + fixedMaterials);
            report.AppendLine("Errors: " + issues.Count);
            foreach (var issue in issues.Distinct()) report.AppendLine(issue);
            Directory.CreateDirectory(Path.GetDirectoryName(ValidationReportPath));
            File.WriteAllText(ValidationReportPath, report.ToString());
            AssetDatabase.Refresh();
            if (issues.Count > 0) throw new InvalidOperationException(report.ToString());
            Debug.Log("MAIN_CITY_RESOURCES_READY\n" + report);
        }

        private static void RestoreCompatibleShader(Material material)
        {
            var queue = material.renderQueue;
            var texture = material.HasProperty("_MainTex") ? material.GetTexture("_MainTex") : null;
            var scale = material.HasProperty("_MainTex") ? material.GetTextureScale("_MainTex") : Vector2.one;
            var offset = material.HasProperty("_MainTex") ? material.GetTextureOffset("_MainTex") : Vector2.zero;
            var oldName = material.shader != null ? material.shader.name : string.Empty;
            var transparent = queue >= 3000 || oldName.IndexOf("Transparent", StringComparison.OrdinalIgnoreCase) >= 0;
            var shader = Shader.Find(transparent ? "Unlit/Transparent" : "Unlit/Texture");
            if (shader == null) throw new InvalidOperationException("Required built-in Unlit shader is unavailable.");
            material.shader = shader;
            material.SetTexture("_MainTex", texture);
            material.SetTextureScale("_MainTex", scale);
            material.SetTextureOffset("_MainTex", offset);
            material.renderQueue = queue;
            EditorUtility.SetDirty(material);
        }

        private static void ValidateGuidReferences(List<string> issues)
        {
            var pattern = new Regex(@"guid: ([0-9a-f]{32})");
            var extensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".prefab", ".mat", ".asset", ".meta" };
            var files = new List<string>();
            foreach (var root in ResourceRoots)
            {
                if (!Directory.Exists(root)) { issues.Add("Resource directory is missing: " + root); continue; }
                files.AddRange(Directory.GetFiles(root, "*", SearchOption.AllDirectories));
            }
            foreach (var file in files)
            {
                if (!extensions.Contains(Path.GetExtension(file))) continue;
                foreach (Match match in pattern.Matches(File.ReadAllText(file)))
                {
                    var guid = match.Groups[1].Value;
                    if (guid.StartsWith("0000000000000000", StringComparison.Ordinal)) continue;
                    if (string.IsNullOrEmpty(AssetDatabase.GUIDToAssetPath(guid))) issues.Add("Unresolved GUID " + guid + " in " + file);
                }
            }
        }

        private static string HierarchyPath(Transform transform)
        {
            var path = transform.name;
            while (transform.parent != null) { transform = transform.parent; path = transform.name + "/" + path; }
            return path;
        }
    }
}
