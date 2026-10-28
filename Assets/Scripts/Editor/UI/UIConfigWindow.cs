using System;
using System.Linq;
using Client.Framework.UI;
using UnityEditor;
using UnityEngine;
namespace Client.Editor.UI
{
    public sealed class UIConfigWindow : EditorWindow
    {
        Vector2 scroll;UIViewDefinitions catalog;
        [MenuItem("Game/UI/Config")]static void Open()=>GetWindow<UIConfigWindow>("UI Config");
        void OnGUI()
        {
            catalog=(UIViewDefinitions)EditorGUILayout.ObjectField("Authoritative catalog",catalog??AssetDatabase.LoadAssetAtPath<UIViewDefinitions>(UIBindingValidator.CatalogPath),typeof(UIViewDefinitions),false);
            if(catalog==null)return;
            var serialized=new SerializedObject(catalog);serialized.Update();scroll=EditorGUILayout.BeginScrollView(scroll);
            EditorGUILayout.PropertyField(serialized.FindProperty("m_Definitions"),true);
            EditorGUILayout.EndScrollView();serialized.ApplyModifiedProperties();
            if(GUILayout.Button("Save catalog"))AssetDatabase.SaveAssetIfDirty(catalog);
            EditorGUILayout.HelpBox("Config assets locate this catalog by ViewID. Keep UI name, prefab root, layout and code in sync; Validate Selected reports drift.",MessageType.Info);
        }
    }
    [CustomEditor(typeof(UIWindowConfig))]
    public sealed class UIWindowConfigEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();var locator=(UIWindowConfig)target;
            try
            {
                var definition=locator.Resolve();EditorGUILayout.LabelField("Prefab",definition.PrefabAddress);
                if(GUILayout.Button("Locate prefab"))EditorGUIUtility.PingObject(AssetDatabase.LoadMainAssetAtPath(definition.PrefabAddress));
                if(GUILayout.Button("Edit catalog")){Selection.activeObject=locator.Catalog;EditorGUIUtility.PingObject(locator.Catalog);}
            }
            catch(Exception e){EditorGUILayout.HelpBox(e.Message,MessageType.Error);}
            if(GUILayout.Button("Save config locator"))AssetDatabase.SaveAssetIfDirty(locator);
        }
    }
}
