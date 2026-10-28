using System;
using System.Linq;
using Client.Framework.UI;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;
namespace Client.Editor.UI
{
    [CustomEditor(typeof(UIBindings))]
    public sealed class UIBindingsEditor : UnityEditor.Editor
    {
        ReorderableList singles,arrays;Component candidate;int typeIndex;bool asArray;
        static Type[] types;
        static Type[] Types=>types??(types=TypeCache.GetTypesDerivedFrom<Component>().Where(t=>t.IsVisible&&!t.IsGenericType&&!t.IsAbstract&&t.Assembly.GetName().Name!="Client.Game").OrderBy(t=>t.FullName,StringComparer.Ordinal).ToArray());
        void OnEnable(){singles=List("m_Bindings",false);arrays=List("m_Arrays",true);}
        ReorderableList List(string property,bool array)
        {
            var list=new ReorderableList(serializedObject,serializedObject.FindProperty(property),true,true,true,true);
            list.drawHeaderCallback=r=>EditorGUI.LabelField(r,array?"Ordered arrays":"Single component references");
            list.elementHeightCallback=i=>array?EditorGUI.GetPropertyHeight(list.serializedProperty.GetArrayElementAtIndex(i).FindPropertyRelative("Values"),true)+84:84;
            list.drawElementCallback=(rect,index,active,focused)=>
            {
                var item=list.serializedProperty.GetArrayElementAtIndex(index);rect.height=18;
                EditorGUI.PropertyField(rect,item.FindPropertyRelative("Id"));rect.y+=20;
                var type=item.FindPropertyRelative("ComponentType");var current=Type.GetType(type.stringValue??"",false);int selected=Array.IndexOf(Types,current);
                int next=EditorGUI.Popup(rect,"Expected type",selected,Types.Select(t=>t.FullName).ToArray());if(next>=0 && next!=selected)type.stringValue=Types[next].AssemblyQualifiedName;rect.y+=20;
                EditorGUI.PropertyField(rect,item.FindPropertyRelative("Required"));rect.y+=20;
                var value=item.FindPropertyRelative(array?"Values":"Value");
                if(array){rect.height=EditorGUI.GetPropertyHeight(value,true);EditorGUI.PropertyField(rect,value,true);}
                else {value.objectReferenceValue=EditorGUI.ObjectField(rect,"Component",value.objectReferenceValue,current??typeof(Component),true);}
            };
            list.onAddCallback=l=>{int index=l.serializedProperty.arraySize++;var item=l.serializedProperty.GetArrayElementAtIndex(index);item.FindPropertyRelative("Id").stringValue="";item.FindPropertyRelative("ComponentType").stringValue=typeof(RectTransform).AssemblyQualifiedName;item.FindPropertyRelative("Required").boolValue=true;if(array)item.FindPropertyRelative("Values").arraySize=0;else item.FindPropertyRelative("Value").objectReferenceValue=null;};
            return list;
        }
        public override void OnInspectorGUI()
        {
            serializedObject.Update();singles.DoLayoutList();arrays.DoLayoutList();serializedObject.ApplyModifiedProperties();
            EditorGUILayout.Space();EditorGUILayout.LabelField("Add explicit reference",EditorStyles.boldLabel);
            candidate=(Component)EditorGUILayout.ObjectField("Drag component",candidate,typeof(Component),true);
            if(candidate!=null)
            {
                var available=candidate.GetComponents<Component>().Where(c=>c!=null).ToArray();typeIndex=Mathf.Clamp(typeIndex,0,available.Length-1);
                typeIndex=EditorGUILayout.Popup("Component type",typeIndex,available.Select(c=>c.GetType().FullName).ToArray());asArray=EditorGUILayout.Toggle("New ordered array",asArray);
                if(GUILayout.Button("Add reference")){AddReference((UIBindings)target,available[typeIndex],asArray);candidate=null;}
                if(GUILayout.Button("Locate object"))EditorGUIUtility.PingObject(candidate.gameObject);
            }
            if(GUILayout.Button("Add Selected"))foreach(var selected in Selection.gameObjects.Where(g=>g!=((UIBindings)target).gameObject))
                AddReference((UIBindings)target,selected.transform,false);
            if(GUILayout.Button("Save prefab changes"))
            {
                var stage=UnityEditor.SceneManagement.PrefabStageUtility.GetCurrentPrefabStage();
                if(stage!=null)PrefabUtility.SaveAsPrefabAsset(stage.prefabContentsRoot,stage.assetPath);
                else if(PrefabUtility.IsPartOfPrefabInstance(target))PrefabUtility.ApplyPrefabInstance(((UIBindings)target).gameObject,InteractionMode.UserAction);
                else if(PrefabUtility.IsPartOfPrefabAsset(target))PrefabUtility.SavePrefabAsset(((UIBindings)target).transform.root.gameObject);
                else AssetDatabase.SaveAssetIfDirty(target);
            }
        }
        public static void AddReference(UIBindings bindings,Component value,bool array)
        {
            if(bindings==null || value==null || (value.transform!=bindings.transform && !value.transform.IsChildOf(bindings.transform)))throw new ArgumentException("Reference must belong to this binding root.");
            string suggestion=value.name;if(!UIBindingGenerator.ValidIdentifier(suggestion))suggestion="Component";
            string id=suggestion;int suffix=2;var ids=bindings.Singles.Select(e=>e.Id).Concat(bindings.Arrays.Select(e=>e.Id)).ToArray();
            while(ids.Contains(id,StringComparer.OrdinalIgnoreCase))id=suggestion+suffix++;
            Undo.RecordObject(bindings,"Add UI binding");
            if(array)bindings.Configure(bindings.Singles.ToArray(),bindings.Arrays.Concat(new[]{new UIBindings.ArrayBinding{Id=id,ComponentType=value.GetType().AssemblyQualifiedName,Values=new[]{value}}}).ToArray());
            else bindings.Configure(bindings.Singles.Concat(new[]{new UIBindings.Binding{Id=id,ComponentType=value.GetType().AssemblyQualifiedName,Value=value}}).ToArray(),bindings.Arrays.ToArray());
            EditorUtility.SetDirty(bindings);PrefabUtility.RecordPrefabInstancePropertyModifications(bindings);
        }
    }
}
