using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Client.Framework.UI;
using TMPro;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
namespace Client.Editor.UI
{
    public enum UIValidationSeverity { Warning,Error }
    public sealed class UIValidationIssue
    {
        public string Code,AssetPath,ObjectPath,Message;
        public UIValidationSeverity Severity;
        public UnityEngine.Object Context;
        public override string ToString()=>Severity+" "+Code+" "+AssetPath+" "+ObjectPath+": "+Message;
    }
    public sealed class UIValidationReport
    {
        public List<UIValidationIssue> Issues {get;}=new List<UIValidationIssue>();
        public bool IsValid=>Issues.All(i=>i.Severity!=UIValidationSeverity.Error);
        public override string ToString()=>string.Join("\n",Issues.Select(i=>i.ToString()));
    }
    public sealed class UIAuthoringValidator : IPreprocessBuildWithReport
    {
        public int callbackOrder=>10;
        public static UIValidationReport Validate(UIViewDefinition definition)
        {
            var report=new UIValidationReport();string path=definition?.PrefabAddress??"";
            void Add(string code,string message,UnityEngine.Object context=null,UIValidationSeverity severity=UIValidationSeverity.Error)
            {
                var component=context as Component;var go=context as GameObject;
                string objectPath=component!=null?ObjectPath(component.transform):go!=null?ObjectPath(go.transform):"";
                report.Issues.Add(new UIValidationIssue{Code=code,AssetPath=path,ObjectPath=objectPath,Message=message,Severity=severity,Context=context});
            }
            if(definition==null){Add("UI_DEFINITION","Definition is missing.");return report;}
            var root=AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if(root==null){Add("UI_PREFAB","Prefab address does not resolve to an asset.");return report;}
            if(!UIBindingGenerator.ValidName(definition.ViewID) || root.name!=definition.ViewID || Path.GetFileNameWithoutExtension(path)!=definition.ViewID)
                Add("UI_NAME","ViewID, prefab root and prefab filename must share UI{Name}.",root);
            try{UIGroups.Depth(definition.Group);}catch(ArgumentException e){Add("UI_GROUP",e.Message,root);}
            foreach(string error in UIBindingValidator.Validate(root,definition))Add("UI_BINDING",error,root);
            var catalog=AssetDatabase.LoadAssetAtPath<UIViewDefinitions>(UIBindingValidator.CatalogPath);
            if(catalog==null || catalog.Definitions.Count(d=>d!=null && d.ViewID==definition.ViewID)!=1)Add("UI_DUPLICATE_VIEW","ViewID must occur exactly once in the authoritative catalog.",root);
            string directory=Path.GetDirectoryName(path).Replace('\\','/'),name=root.name;
            var config=AssetDatabase.LoadAssetAtPath<UIWindowConfig>(directory+"/"+name+".Config.asset");
            if(config==null || config.Catalog!=catalog || config.ViewID!=name)Add("UI_CONFIG","Missing or drifting config locator.",root);
            else try{if(config.Resolve().PrefabAddress!=path)Add("UI_CONFIG","Config resolves to a different prefab.",config);}catch(InvalidOperationException e){Add("UI_CONFIG",e.Message,config);}
            var layout=AssetDatabase.LoadAssetAtPath<AdaptiveLayoutDefinition>(directory+"/"+name+".Layout.asset");
            if(layout==null || layout!=definition.LayoutDefinition)Add("UI_LAYOUT","The named layout asset must be the catalog layout.",root);
            if(AssetDatabase.LoadAssetAtPath<UITextDefinition>(directory+"/"+name+".Text.asset")==null)Add("UI_TEXT","Named text asset is missing.",root);
            foreach(var events in root.GetComponentsInChildren<EventSystem>(true))Add("UI_EVENT_SYSTEM","Window prefabs cannot own an EventSystem.",events);
            foreach(var table in root.GetComponentsInChildren<UIBindings>(true))
            {
                var ids=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach(string id in table.Singles.Select(e=>e?.Id).Concat(table.Arrays.Select(e=>e?.Id)))
                    if(!UIBindingGenerator.ValidIdentifier(id) || id==name || !ids.Add(id))Add("UI_BINDING_ID","Invalid, reserved or case-conflicting field: "+id,table);
            }
            foreach(var transform in root.GetComponentsInChildren<Transform>(true))
            {
                if(GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(transform.gameObject)>0)Add("UI_MISSING_SCRIPT","Missing script.",transform);
                foreach(var component in transform.GetComponents<Component>())
                    if(component!=null && component.GetType().Assembly.GetName().Name=="Client.Game")Add("UI_HOT_COMPONENT","Hot-update types cannot be serialized on prefabs.",component);
                if(transform!=root.transform && transform.GetComponent<UIBindings>()!=null && transform.GetComponent<UIViewBridge>()!=null)Add("UI_CHILD_BRIDGE","Reusable widgets use UIBindings without UIViewBridge.",transform);
                if(transform!=root.transform && !PrefabUtility.IsPartOfPrefabInstance(transform.gameObject))
                {
                    string common="Assets/Content/UI/Common/"+transform.name+".prefab";
                    if(File.Exists(common) && common!=UIWindowCreator.TemplatePath)Add("UI_PUBLIC_UNPACKED","Object matches a public prefab name but has no nested prefab connection: "+common,transform,UIValidationSeverity.Warning);
                }
            }
            var tmpTexts=root.GetComponentsInChildren<TMP_Text>(true);
            if(tmpTexts.Length>0 && !AssetDatabase.FindAssets("t:TMP_Settings").Select(AssetDatabase.GUIDToAssetPath).Any(p=>p.EndsWith("/Resources/TMP Settings.asset",StringComparison.Ordinal)))
                Add("UI_TMP_SETTINGS","TMP text requires saved TMP Settings in a Resources folder; import licensed project font resources explicitly before preview.",tmpTexts[0]);
            foreach(var text in tmpTexts)
            {
                if(text.font==null || !AssetDatabase.Contains(text.font))Add("UI_TMP_FONT","Text requires a saved project TMP font asset.",text);
                if(text.font!=null && text.isTextOverflowing)Add("UI_TEXT_OVERFLOW","Text overflows its authored bounds; verify long localized content.",text,UIValidationSeverity.Warning);
            }
            foreach(var text in root.GetComponentsInChildren<Text>(true))Add("UI_LEGACY_TEXT","Use the project TMP font for authored text.",text,UIValidationSeverity.Warning);
            foreach(var graphic in root.GetComponentsInChildren<Graphic>(true))
                if(graphic.raycastTarget && graphic.GetComponent<Selectable>()==null && graphic.GetComponentInParent<Selectable>()==null)Add("UI_DECORATION_RAYCAST","Graphic receives rays without an interactive owner.",graphic,UIValidationSeverity.Warning);
            foreach(var graphic in root.GetComponentsInChildren<Graphic>(true))
                if(graphic.material!=null && graphic.material.name.EndsWith("(Instance)",StringComparison.Ordinal))Add("UI_MATERIAL_COPY","Graphic has a material instance; prefer a shared authored material.",graphic,UIValidationSeverity.Warning);
            foreach(var rect in root.GetComponentsInChildren<RectTransform>(true))
                if(rect.anchorMin==rect.anchorMax && rect.anchoredPosition!=Vector2.zero && rect.GetComponent<LayoutElement>()==null && rect.GetComponentInParent<LayoutGroup>()==null)
                    Add("UI_FIXED_POSITION","Point anchors and a fixed offset have no layout-group constraint; verify both orientations and short screens.",rect,UIValidationSeverity.Warning);
            var bridge=root.GetComponent<UIViewBridge>();
            foreach(var canvas in root.GetComponentsInChildren<Canvas>(true))if(bridge!=null && canvas!=bridge.RootCanvas)Add("UI_EXTRA_CANVAS","Additional canvas requires a measured performance reason.",canvas,UIValidationSeverity.Warning);
            foreach(var raycaster in root.GetComponentsInChildren<GraphicRaycaster>(true))if(bridge!=null && (bridge.RootCanvas==null || raycaster.gameObject!=bridge.RootCanvas.gameObject))Add("UI_EXTRA_RAYCASTER","Additional raycaster requires an interactive canvas reason.",raycaster,UIValidationSeverity.Warning);
            if(root.transform.localScale!=Vector3.one || root.transform.localRotation!=Quaternion.identity)Add("UI_ROOT_TRANSFORM","Root requires unit scale and identity rotation.",root);
            string[] structure={"UINode/Background","UINode/SafeArea","UINode/SafeArea/Static","UINode/SafeArea/Dynamic"};
            foreach(string required in structure)if(root.transform.Find(required)==null)Add("UI_STRUCTURE","Missing template node: "+required,root);
            var safeRoot=root.transform.Find("UINode/SafeArea");
            if(safeRoot!=null && safeRoot.GetComponent<SafeAreaRoot>()==null)Add("UI_STRUCTURE","SafeArea requires SafeAreaRoot.",safeRoot);
            var pool=root.transform.Find("PoolNode");if(pool==null || pool.gameObject.activeSelf)Add("UI_POOL","PoolNode must exist and be inactive.",root);
            string output=UIBindingGenerator.OutputFor(directory,name),hand=UIBindingGenerator.CodeDirectory(directory)+"/"+name+".cs";
            if(!File.Exists(hand) || !File.ReadAllText(hand).Contains("sealed partial class "+name))Add("UI_PARTIAL","Matching handwritten partial is missing.",root);
            try
            {
                string expected=UIBindingGenerator.Render(root.GetComponent<UIBindings>(),"Client.Game",name);
                if(!File.Exists(output) || File.ReadAllText(output)!=expected)Add("UI_GENERATED_DRIFT","Regenerate bindings; generated bytes differ from the explicit table.",root);
            }
            catch(Exception e){Add("UI_GENERATED_DRIFT",e.Message,root);}
            foreach(var scene in EditorBuildSettings.scenes)
                if(scene.enabled && scene.path==UIWindowCreator.PreviewScenePath)Add("UI_PREVIEW_IN_PLAYER","UIEdit preview scene must be excluded from Player.",root);
            return report;
        }
        static string ObjectPath(Transform transform)
        { var parts=new List<string>();while(transform!=null){parts.Add(transform.name);transform=transform.parent;}parts.Reverse();return string.Join("/",parts); }
        [MenuItem("Game/UI/Validate Selected")]
        public static void ValidateSelected()
        {
            string path=AssetDatabase.GetAssetPath(Selection.activeObject);
            var config=Selection.activeObject as UIWindowConfig;
            if(config==null && Selection.activeGameObject!=null)config=AssetDatabase.LoadAssetAtPath<UIWindowConfig>(Path.ChangeExtension(path,null)+".Config.asset");
            if(config==null){Debug.LogError("Select a UI prefab or its Config asset.");return;}
            UIValidationReport report;
            try{report=Validate(config.Resolve());}catch(Exception e){Debug.LogError(e.Message,config);return;}
            foreach(var issue in report.Issues)if(issue.Severity==UIValidationSeverity.Error)Debug.LogError(issue.ToString(),issue.Context);else Debug.LogWarning(issue.ToString(),issue.Context);
            if(report.IsValid)Debug.Log("UI_VALIDATE_PASS "+config.ViewID,config);
        }
        public void OnPreprocessBuild(BuildReport report)
        {
            var catalog=AssetDatabase.LoadAssetAtPath<UIViewDefinitions>(UIBindingValidator.CatalogPath);
            if(catalog==null)throw new BuildFailedException("UI_CATALOG: authoritative catalog missing.");
            var errors=catalog.Definitions.Select(Validate).Where(r=>!r.IsValid).Select(r=>r.ToString()).ToArray();
            if(errors.Length>0)throw new BuildFailedException(string.Join("\n",errors));
            if(EditorBuildSettings.scenes.Any(s=>s.enabled && s.path==UIWindowCreator.PreviewScenePath))throw new BuildFailedException("UI_PREVIEW_IN_PLAYER: UIEdit is editor-only.");
        }
    }
}
