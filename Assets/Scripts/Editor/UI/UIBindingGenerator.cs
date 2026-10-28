using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Client.Framework.UI;
using UnityEditor;
using UnityEngine;
namespace Client.Editor.UI
{
    public sealed class BindingGenerationResult
    {
        public bool Success {get; internal set;}
        public bool Changed {get; internal set;}
        public string Error {get; internal set;}
        public string OutputFile {get; internal set;}
    }
    public static class UIBindingGenerator
    {
        static readonly HashSet<string> Keywords=new HashSet<string>(("__arglist __makeref __reftype __refvalue abstract as base bool break byte case catch char checked class const continue decimal default delegate do double else enum event explicit extern false finally fixed float for foreach goto if implicit in int interface internal is lock long namespace new null object operator out override params private protected public readonly ref return sbyte sealed short sizeof stackalloc static string struct switch this throw true try typeof uint ulong unchecked unsafe ushort using virtual void volatile while add alias and ascending async await by descending dynamic equals file from get global group init into join let managed nameof nint not notnull nuint on or orderby partial record remove required scoped select set unmanaged value var when where with yield BindComponents ClearBindings").Split(' '),StringComparer.Ordinal);
        public static bool ValidName(string name)=>name!=null && Regex.IsMatch(name,@"^UI[A-Z][A-Za-z0-9]*$");
        public static bool ValidIdentifier(string id)=>id!=null && Regex.IsMatch(id,@"^[A-Za-z_][A-Za-z0-9_]*$") && !Keywords.Contains(id);
        public static string CodeDirectory(string contentDirectory)=>"Assets/Scripts/Game/"+Path.GetFileName(contentDirectory.TrimEnd('/'));
        public static string OutputFor(string contentDirectory,string className)=>CodeDirectory(contentDirectory)+"/Generated/"+className+".G.Bindings.cs";
        public static BindingGenerationResult Generate(UIBindings source,string namespaceName,string className,string outputFile)
        {
            var result=new BindingGenerationResult{OutputFile=outputFile};
            try
            {
                string code=Render(source,namespaceName,className);
                string path=(outputFile??"").Replace('\\','/');
                string full=Path.GetFullPath(path), game=Path.GetFullPath("Assets/Scripts/Game")+Path.DirectorySeparatorChar;
                if(!full.StartsWith(game,StringComparison.OrdinalIgnoreCase) || Path.GetFileName(path)!=className+".G.Bindings.cs" || Path.GetFileName(Path.GetDirectoryName(path))!="Generated")
                    throw new InvalidOperationException("UI_OUTPUT: output must be Game/<feature>/Generated/"+className+".G.Bindings.cs");
                string parent=Path.GetDirectoryName(Path.GetDirectoryName(path));
                if(!Path.GetFullPath(parent).StartsWith(game,StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("UI_OUTPUT: a feature directory is required.");
                string sourceAsset=AssetDatabase.GetAssetPath(source.gameObject);
                if(!string.IsNullOrEmpty(sourceAsset) && Path.GetFullPath(OutputFor(Path.GetDirectoryName(sourceAsset).Replace('\\','/'),className))!=full)throw new InvalidOperationException("UI_OUTPUT: output must belong to the selected prefab feature.");
                string hand=Path.Combine(parent,className+".cs");
                if(!File.Exists(hand) || !Regex.IsMatch(File.ReadAllText(hand),@"namespace\s+"+Regex.Escape(namespaceName)+@"\s*\{") || !Regex.IsMatch(File.ReadAllText(hand),@"sealed\s+partial\s+class\s+"+Regex.Escape(className)+@"\b"))
                    throw new InvalidOperationException("UI_PARTIAL: matching handwritten sealed partial is required.");
                if(File.Exists(path) && File.ReadAllText(path)==code){ result.Success=true;return result; }
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                string temp=path+"."+Guid.NewGuid().ToString("N")+".tmp";
                try { File.WriteAllText(temp,code,new UTF8Encoding(false)); if(File.Exists(path))File.Replace(temp,path,null);else File.Move(temp,path); }
                finally { if(File.Exists(temp))File.Delete(temp); }
                result.Success=true; result.Changed=true;
            }
            catch(Exception exception){ result.Error=exception.Message; }
            return result;
        }
        public static string Render(UIBindings source,string namespaceName,string className)
        {
            if(source==null || !ValidName(className) || source.name!=className)throw new InvalidOperationException("UI_NAME: class and binding root must share a valid UI name.");
            if(namespaceName!="Client.Game")throw new InvalidOperationException("UI_NAMESPACE: UI partials belong to Client.Game.");
            var errors=UIBindingValidator.Validate(source.gameObject);
            if(errors.Count>0)throw new InvalidOperationException("UI_BINDING: "+string.Join("\n",errors));
            var ids=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var fields=new List<Tuple<string,Type,bool>>();
            foreach(var entry in source.Singles)Add(entry.Id,entry.ComponentType,false);
            foreach(var entry in source.Arrays)Add(entry.Id,entry.ComponentType,true);
            void Add(string id,string typeName,bool array)
            {
                if(!ValidIdentifier(id) || id==className || !ids.Add(id))throw new InvalidOperationException("UI_BINDING_ID: illegal or duplicate field "+id);
                Type type=Type.GetType(typeName,false);
                if(type==null || !type.IsVisible || type.IsGenericType || type.Assembly.GetName().Name=="Client.Game")throw new InvalidOperationException("UI_TYPE: component must be a public AOT type.");
                fields.Add(Tuple.Create(id,type,array));
            }
            string Name(Type type)=>"global::"+type.FullName.Replace('+','.');
            var text=new StringBuilder("// <auto-generated />\nnamespace "+namespaceName+"\n{\n    public sealed partial class "+className+"\n    {\n");
            foreach(var field in fields)text.Append("        public "+Name(field.Item2)+(field.Item3?"[]":"")+" "+field.Item1+" { get; private set; }\n");
            text.Append("\n        public void BindComponents(global::Client.Framework.UI.UIBindings bindings)\n        {\n            if (bindings == null) throw new global::System.ArgumentNullException(nameof(bindings));\n");
            // Resolve into locals first so a failing rebind cannot partially replace the previous table.
            for(int i=0;i<fields.Count;i++)
            {
                var f=fields[i];string type=Name(f.Item2), local="resolved"+i;
                if(!f.Item3)text.Append("            var "+local+" = ("+type+")bindings.Resolve(\""+f.Item1+"\");\n");
                else text.Append("            var values"+i+" = bindings.ResolveMany(\""+f.Item1+"\");\n            var "+local+" = new "+type+"[values"+i+".Count];\n            for (int i = 0; i < "+local+".Length; i++) "+local+"[i] = ("+type+")values"+i+"[i];\n");
            }
            for(int i=0;i<fields.Count;i++)text.Append("            this."+fields[i].Item1+" = resolved"+i+";\n");
            text.Append("        }\n\n        public void ClearBindings()\n        {\n");
            foreach(var f in fields)text.Append("            this."+f.Item1+" = null;\n");
            return text.Append("        }\n    }\n}\n").ToString();
        }
        [MenuItem("Game/UI/Generate Bindings")]
        static void GenerateSelected()
        {
            var source=Selection.activeGameObject?.GetComponent<UIBindings>();
            string asset=AssetDatabase.GetAssetPath(Selection.activeGameObject);
            if(source==null || string.IsNullOrEmpty(asset)){Debug.LogError("Select a saved UI prefab root.");return;}
            var result=Generate(source,"Client.Game",source.name,OutputFor(Path.GetDirectoryName(asset).Replace('\\','/'),source.name));
            if(!result.Success)Debug.LogError(result.Error,source);else { AssetDatabase.Refresh();Debug.Log("UI_BINDINGS_GENERATED "+result.OutputFile,source); }
        }
    }
}
