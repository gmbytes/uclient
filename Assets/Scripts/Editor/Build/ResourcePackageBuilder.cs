using System;
using System.IO;
using Client.Framework.HotUpdate;
using Client.Framework.UI;
using Newtonsoft.Json;
using UnityEditor;
using UnityEditor.Build;
using UnityGameFramework.Editor.ResourceTools;

namespace Client.Editor.Build
{
    public static class ResourcePackageBuilder
    {
        internal static void Build(BuildRequest request,BuildResult result,ResourceConfigurationScope scope,string destination)
        {
            foreach(string asset in new[]{"Assets/Settings/Environment/LocalSimulationProfile.asset","Assets/Settings/Environment/DevelopmentProfile.asset"})
            {var profile=AssetDatabase.LoadAssetAtPath<LoginPresentationProfile>(asset);profile.Version="Framework "+request.ReleaseId;EditorUtility.SetDirty(profile);}
            AssetDatabase.SaveAssets();
            var collection=scope.CreateCollection();
            if(!collection.AddResource("client/ui",null,null,LoadType.LoadFromFile,true)||!collection.AddResource("client/profiles",null,null,LoadType.LoadFromFile,true))throw new BuildFailedException("Cannot create the native resource collection.");
            foreach(string asset in new[]{"Assets/Content/UI/Login/UILogin.prefab","Assets/Content/UI/Common/UIViewDefinitions.asset","Assets/Settings/Environment/LocalSimulationProfile.asset","Assets/Settings/Environment/DevelopmentProfile.asset"})
                if(!collection.AssignAsset(AssetDatabase.AssetPathToGUID(asset),asset.StartsWith("Assets/Settings/",StringComparison.Ordinal)?"client/profiles":"client/ui",null))throw new BuildFailedException("Cannot assign native resource: "+asset);
            if(!collection.Save())throw new BuildFailedException("Cannot save the effective native collection.");
            string captured=Path.Combine(result.OutputDirectory,"ResourceCollection.xml");File.Copy(scope.CollectionPath,captured);
            var builder=scope.CreateBuilder();builder.Platforms=request.Platform==BuildPlatform.Android?Platform.Android:Platform.Windows64;builder.InternalResourceVersion=request.Kind==BuildKind.Player?1:2;
            builder.OutputDirectory=Path.Combine(result.OutputDirectory,"NativePackage");builder.OutputPackageSelected=true;builder.OutputFullSelected=false;builder.OutputPackedSelected=false;builder.ForceRebuildAssetBundleSelected=true;
            Directory.CreateDirectory(builder.OutputDirectory);if(!builder.BuildResources())throw new BuildFailedException("Actual native GF Package build failed.");
            string output=Path.GetFullPath(Path.Combine(builder.OutputPackagePath,request.Platform==BuildPlatform.Android?"Android":"Windows64"));
            BuildFiles.CopyTree(output,Path.Combine(destination,"Resources"));
            File.WriteAllText(Path.Combine(result.OutputDirectory,"resource-package.json"),JsonConvert.SerializeObject(new {collectionPath=scope.CollectionPath,collectionSha256=HotfixLoadPlan.Hash(File.ReadAllBytes(captured)),builderPath=scope.BuilderPath,outputPackagePath=output,files=PlayerBaselineArchive.Inventory(Path.Combine(destination,"Resources")),dependencies=AssetDatabase.GetDependencies(new[]{"Assets/Content/UI/Login/UILogin.prefab","Assets/Content/UI/Common/UIViewDefinitions.asset"},true)},Formatting.Indented)+"\n");
            result.EvidenceFiles.Add(captured);result.EvidenceFiles.Add(Path.Combine(result.OutputDirectory,"resource-package.json"));
        }
    }
    internal static class BuildFiles
    {
        internal static void Put(string path,byte[] bytes){Directory.CreateDirectory(Path.GetDirectoryName(path));File.WriteAllBytes(path,bytes);}
        internal static void CopyTree(string source,string destination)
        {string root=Path.GetFullPath(source);foreach(string file in Directory.GetFiles(root,"*",SearchOption.AllDirectories))if(!file.EndsWith(".meta",StringComparison.OrdinalIgnoreCase))Put(Path.Combine(destination,file.Substring(root.TrimEnd(Path.DirectorySeparatorChar).Length+1)),File.ReadAllBytes(file));}
    }
}
