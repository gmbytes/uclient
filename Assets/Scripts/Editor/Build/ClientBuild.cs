using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Client.Editor.Build
{
    public static class ClientBuild
    {
        public static BuildResult LastMenuResult { get; private set; }
        public const string MenuRequestPreference="Client.Build.RequestFile";
        public static void Run()
        {
            try
            {
                var result=Execute(ReadRequest(Argument("-clientBuildRequest")));
                WriteReceipt(Argument("-clientBuildReceipt"),result);
                Debug.Log("CLIENT_BUILD_RESULT "+result.ResultFile);
                if(result.Status!=BuildStatus.Succeeded)EditorApplication.Exit(1);
            }
            catch(Exception error){Debug.LogException(error);EditorApplication.Exit(1);}
        }
        [MenuItem("Game/Build/Select Request")]
        private static void SelectRequest(){string path=EditorUtility.OpenFilePanel("Select client build request",Path.GetFullPath("Builds"),"json");if(!string.IsNullOrEmpty(path))EditorPrefs.SetString(MenuRequestPreference,path);}
        [MenuItem("Game/Build/Execute Selected Request")]
        private static void ExecuteMenu(){LastMenuResult=Execute(ReadRequest(EditorPrefs.GetString(MenuRequestPreference)));Debug.Log("CLIENT_BUILD_RESULT "+LastMenuResult.ResultFile);}
        public static BuildRequest ReadRequest(string path)
        {
            var json=JObject.Parse(File.ReadAllText(path),new JsonLoadSettings{DuplicatePropertyNameHandling=DuplicatePropertyNameHandling.Error});
            string[] known={"Kind","Platform","OutputRoot","PlayerArchive","PublicProfilePath","SigningKeyPath","LinkerPolicyPath","ReleaseId","HotfixOverlay"};
            if(json.Properties().Any(p=>!known.Contains(p.Name)))throw new BuildFailedException("Unknown build request field.");
            return new BuildRequest((BuildKind)Enum.Parse(typeof(BuildKind),(string)json["Kind"],false),(BuildPlatform)Enum.Parse(typeof(BuildPlatform),(string)json["Platform"],false),(string)json["OutputRoot"],(string)json["PlayerArchive"],(string)json["PublicProfilePath"],(string)json["SigningKeyPath"],(string)json["LinkerPolicyPath"],(string)json["ReleaseId"],(string)json["HotfixOverlay"]);
        }
        public static BuildResult Execute(BuildRequest request)
        {
            BuildPreflight.ValidateSigningKeyLocation(request);
            using(var owner=ProjectBuildLock.Acquire(Path.GetFullPath(".")))
            {
                var result=BuildOutput.Create(request);
                try
                {
                    if(EditorApplication.isCompiling||EditorApplication.isUpdating||EditorApplication.isPlayingOrWillChangePlaymode)throw new BuildFailedException("Wait for a stable imported Editor before building.");
                    if(Enumerable.Range(0,UnityEngine.SceneManagement.SceneManager.sceneCount).Any(i=>UnityEngine.SceneManagement.SceneManager.GetSceneAt(i).isDirty))throw new BuildFailedException("Save or close dirty scenes before building.");
                    BuildPreflight.ValidateEnvironment(request);
                    var inputs=BuildInputs.Prepare(request,result);
                    var scenes=EditorSceneManager.GetSceneManagerSetup();
                    EditorApplication.LockReloadAssemblies();AssetDatabase.DisallowAutoRefresh();
                    try
                    {
                        using(var source=BuildSourceTransaction.Begin(inputs.MutatedPaths))
                        using(var config=ResourceConfigurationScope.Enter(Path.Combine(result.OutputDirectory,"Config")))
                        using(var symbols=ReleaseLinkerSymbolPolicy.Apply(request.LinkerPolicyPath))
                        {
                            inputs.Install();
                            if(request.Kind==BuildKind.Player)PlayerBuilder.Build(request,result,inputs,config);
                            else PatchBuilder.Build(request,result,inputs,config);
                        }
                    }
                    finally
                    {
                        try{AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport|ImportAssetOptions.ForceUpdate);EditorSceneManager.RestoreSceneManagerSetup(scenes);}
                        finally{AssetDatabase.AllowAutoRefresh();EditorApplication.UnlockReloadAssemblies();}
                    }
                    inputs.VerifyRestoration();
                    string release=Path.Combine(result.OutputDirectory,"Release");
                    File.Move(Path.Combine(release,"channel.json.pending"),Path.Combine(release,"channel.json"));
                    File.Move(Path.Combine(release,"channel.sig.pending"),Path.Combine(release,"channel.sig"));
                    result.Status=BuildStatus.Succeeded;
                }
                catch(Exception error)
                {
                    result.Status=BuildStatus.Failed;result.Errors.Add(error.ToString());
                    foreach(string name in new[]{"channel.json","channel.sig"}){string path=Path.Combine(result.OutputDirectory,"Release",name);if(File.Exists(path))File.Delete(path);}
                }
                File.WriteAllText(result.ResultFile,JsonConvert.SerializeObject(result,Formatting.Indented)+"\n",new System.Text.UTF8Encoding(false));
                return result;
            }
        }
        internal static void WriteReceipt(string path,BuildResult result)
        {string full=Path.GetFullPath(path);SourceFileTransaction.RejectReparsePoints(full);Directory.CreateDirectory(Path.GetDirectoryName(full));File.WriteAllText(full,JsonConvert.SerializeObject(new{result.Status,result.OutputDirectory,result.ResultFile},Formatting.Indented)+"\n",new System.Text.UTF8Encoding(false));}
        private static string Argument(string name){string[] args=Environment.GetCommandLineArgs();int i=Array.IndexOf(args,name);if(i<0||i+1>=args.Length)throw new ArgumentException(name+" is required.");return args[i+1];}
    }
}
