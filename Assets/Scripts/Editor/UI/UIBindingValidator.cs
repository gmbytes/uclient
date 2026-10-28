using System;
using System.Collections.Generic;
using Client.Framework.UI;
using TMPro;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.UI;

namespace Client.Editor.UI
{
    /// <summary>Explicit reference validation shared by authoring and the player build gate.</summary>
    public sealed class UIBindingValidator : IPreprocessBuildWithReport
    {
        public int callbackOrder => 0;
        public const string CatalogPath = "Assets/Content/UI/Common/UIViewDefinitions.asset";

        public static IReadOnlyList<string> Validate(GameObject root, UIViewDefinition definition = null)
        {
            var errors = new List<string>();
            if (root == null) { errors.Add("UI prefab is missing."); return errors; }
            var bridges = root.GetComponentsInChildren<UIViewBridge>(true);
            foreach (var bridge in bridges)
            {
                if (bridge.gameObject != root) errors.Add("UIViewBridge belongs only on the stable prefab root; child components use UIBindings.");
                if (bridge.GetComponent<RectTransform>() == null) errors.Add("Window root requires RectTransform.");
                if (bridge.GetComponent<Canvas>() != null) errors.Add("Window root must not have Canvas; the child UINode owns Canvas.");
                if (bridge.RootCanvas == null) errors.Add("RootCanvas is required.");
                else
                {
                    if (bridge.RootCanvas.transform.parent != bridge.transform || bridge.RootCanvas.name != "UINode")
                        errors.Add("RootCanvas must reference the direct child UINode.");
                    if (bridge.RootCanvas.GetComponent<GraphicRaycaster>() == null)
                        errors.Add("The referenced UINode Canvas requires its own GraphicRaycaster.");
                }
                if (bridge.RootCanvasGroup == null) errors.Add("RootCanvasGroup is required.");
                else if (bridge.RootCanvas == null || bridge.RootCanvasGroup.gameObject != bridge.RootCanvas.gameObject)
                    errors.Add("RootCanvasGroup must share the referenced child UINode Canvas.");
                if (bridge.LocalSafeArea == null) errors.Add("LocalSafeArea is required.");
                else if (bridge.RootCanvas == null || bridge.LocalSafeArea.transform.parent != bridge.RootCanvas.transform || bridge.LocalSafeArea.name != "SafeArea")
                    errors.Add("LocalSafeArea must reference the direct child SafeArea of UINode.");
                if (bridge.Bindings == null) errors.Add("Bindings is required.");
                else if (bridge.Bindings.gameObject != root) errors.Add("Bindings must reference UIBindings on the stable prefab root.");
                if (bridge.GetComponentsInChildren<CanvasScaler>(true).Length != 0) errors.Add("Window must use the shared root CanvasScaler.");
            }
            if (definition != null)
            {
                if (bridges.Length != 1 || root.GetComponent<UIViewBridge>() == null) errors.Add("View prefab requires exactly one main UIViewBridge.");
                var bridge = root.GetComponent<UIViewBridge>();
                if (definition.CloseOnMask && !definition.Modal) errors.Add("CloseOnMask requires Modal.");
                if (bridge != null && definition.Modal && bridge.Mask == null) errors.Add("Modal view requires a mask Button.");
                if (definition.LayoutDefinition != null && (bridge == null || bridge.Layout == null || bridge.Layout.Definition != definition.LayoutDefinition))
                    errors.Add("LayoutDefinition must match the window AdaptiveLayout.");
            }
            foreach (var table in root.GetComponentsInChildren<UIBindings>(true))
            {
                var ids = new HashSet<string>(StringComparer.Ordinal);
                foreach (var entry in table.Singles)
                {
                    if (entry == null) { errors.Add("Null single binding entry."); continue; }
                    ValidateId(entry.Id, ids, errors);
                    ValidateValue(root, entry.Id, entry.Value, entry.ComponentType, entry.Required, errors);
                }
                foreach (var entry in table.Arrays)
                {
                    if (entry == null) { errors.Add("Null array binding entry."); continue; }
                    ValidateId(entry.Id, ids, errors);
                    if (entry.Values == null || entry.Values.Length == 0)
                    {
                        if (entry.Required) errors.Add(entry.Id + ": required array is empty.");
                        ValidateType(entry.Id, entry.ComponentType, errors);
                        continue;
                    }
                    for (int i = 0; i < entry.Values.Length; i++)
                        ValidateValue(root, entry.Id + "[" + i + "]", entry.Values[i], entry.ComponentType, entry.Required, errors);
                }
            }
            foreach (var text in root.GetComponentsInChildren<Text>(true))
                if (text.font == null) errors.Add(text.name + ": missing font.");
            foreach (var text in root.GetComponentsInChildren<TMP_Text>(true))
                if (text.font == null) errors.Add(text.name + ": missing TMP font.");
            return errors;
        }

        private static void ValidateId(string id, HashSet<string> ids, List<string> errors)
        {
            if (string.IsNullOrWhiteSpace(id)) errors.Add("Binding ID is required.");
            else if (!ids.Add(id)) errors.Add(id + ": duplicate binding ID.");
        }
        private static Type ValidateType(string id, string name, List<string> errors)
        {
            var type = string.IsNullOrWhiteSpace(name) ? null : Type.GetType(name, false);
            if (type == null || !typeof(Component).IsAssignableFrom(type)) errors.Add(id + ": invalid component type: " + name);
            return type;
        }
        private static void ValidateValue(GameObject root, string id, Component value, string name, bool required, List<string> errors)
        {
            var type = ValidateType(id, name, errors);
            if (value == null) { if (required) errors.Add(id + ": required component reference is missing."); return; }
            if (type != null && !type.IsInstanceOfType(value)) errors.Add(id + ": component type mismatch; expected " + name);
            if (value.transform != root.transform && !value.transform.IsChildOf(root.transform)) errors.Add(id + ": component reference is outside the prefab.");
        }

        public void OnPreprocessBuild(BuildReport report)
        {
            var errors = new List<string>();
            foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/Content/UI" }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                foreach (string error in Validate(prefab)) errors.Add(path + ": " + error);
            }
            foreach (string guid in AssetDatabase.FindAssets("t:UIViewDefinitions"))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var catalog = AssetDatabase.LoadAssetAtPath<UIViewDefinitions>(path);
                var ids = new HashSet<string>(StringComparer.Ordinal);
                foreach (var definition in catalog.Definitions)
                {
                    if (definition == null) { errors.Add(path + ": null view definition."); continue; }
                    ValidateId(definition.ViewID, ids, errors);
                    try { UIGroups.Depth(definition.Group); }
                    catch (ArgumentException exception) { errors.Add(path + ": " + exception.Message); }
                    var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(definition.PrefabAddress);
                    foreach (string error in Validate(prefab, definition)) errors.Add(path + "/" + definition.ViewID + ": " + error);
                }
            }
            if (errors.Count > 0) throw new BuildFailedException("UI binding validation failed:\n" + string.Join("\n", errors));
        }

        [MenuItem("Game/UI/Create Empty View Catalog")]
        public static void CreateEmptyCatalog()
        {
            if (AssetDatabase.LoadAssetAtPath<UIViewDefinitions>(CatalogPath) == null)
            {
                System.IO.Directory.CreateDirectory("Assets/Content/UI/Common");
                AssetDatabase.CreateAsset(ScriptableObject.CreateInstance<UIViewDefinitions>(), CatalogPath);
                AssetDatabase.SaveAssets();
            }
        }
    }
}
