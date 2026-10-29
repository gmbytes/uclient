using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Client.Framework.HotUpdate;
using Client.Framework.Release;
using Newtonsoft.Json;
using UnityEditor;
using UnityEditor.Build;

namespace Client.Editor.Build
{
    internal sealed class BuildInputs
    {
        internal const string PublicAsset="Assets/Tests/Generated/D1/NativeProfile.json";
        internal const string GeneratedAot="Assets/Scripts/Framework/HotUpdate/Generated/AOTGenericReferences.cs";
        internal readonly NativeBuildProfile Profile;
        internal readonly string[] MutatedPaths;
        private readonly BuildRequest request;private readonly BuildResult result;
        private readonly Dictionary<string,byte[]> overlay=new Dictionary<string,byte[]>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string,string> before;
        private BuildInputs(BuildRequest request,BuildResult result,NativeBuildProfile profile,Dictionary<string,byte[]> prepared)
        {
            this.request=request;this.result=result;Profile=profile;overlay=prepared;
            var paths=new List<string>{PublicAsset,"Assets/Tests/Generated/D1/Bootstrap.unity","Assets/StreamingAssets/Baseline","Assets/StreamingAssets/ClientIdentity.json","Assets/Scenes/Bootstrap.unity","Assets/Prefabs/App/UIUpdate.prefab","Assets/Prefabs/App/GameRoot.prefab","Assets/Prefabs/App/GameFramework.prefab",GeneratedAot,"Assets/Scripts/Framework/HotUpdate/Generated/link.xml","Assets/Settings/Environment/LocalSimulationProfile.asset","Assets/Settings/Environment/DevelopmentProfile.asset","ProjectSettings/PackageManagerSettings.asset","ProjectSettings/SceneTemplateSettings.json"};
            paths.AddRange(overlay.Keys);paths.AddRange(Directory.GetFiles("ProjectSettings"));
            // Baseline is a newly owned subtree; pre-existing StreamingAssets content is preserved.
            if(Directory.Exists("Assets/StreamingAssets/Baseline"))throw new BuildFailedException("Build-owned Baseline staging already exists; inspect it before another build.");
            MutatedPaths=paths.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();before=SourceHashes();
            File.WriteAllText(Path.Combine(result.OutputDirectory,"source-before.json"),JsonConvert.SerializeObject(before,Formatting.Indented)+"\n");
        }
        internal static BuildInputs Prepare(BuildRequest request,BuildResult result)
        {
            if(string.IsNullOrWhiteSpace(request.SigningKeyPath)||!File.Exists(request.SigningKeyPath))throw new BuildFailedException("An external signing key file is required.");
            if(string.IsNullOrWhiteSpace(request.LinkerPolicyPath)||!File.Exists(request.LinkerPolicyPath))throw new BuildFailedException("Verified linker-policy evidence is required.");
            RelativePathPolicy.RequireIdentifier(request.ReleaseId);
            var prepared=new Dictionary<string,byte[]>(StringComparer.OrdinalIgnoreCase);
            string profilePath=request.Kind==BuildKind.Patch?Path.Combine(request.PlayerArchive,"Inputs",PublicAsset):request.PublicProfilePath;
            if(string.IsNullOrWhiteSpace(profilePath)||!File.Exists(profilePath))throw new BuildFailedException("An independently trusted native public profile is required.");
            var profile=NativeBuildProfile.Parse(File.ReadAllText(profilePath));
            using(var key=System.Security.Cryptography.RSA.Create()){key.FromXmlString(File.ReadAllText(request.SigningKeyPath));profile.RequireSigner(key);}
            if(request.Kind==BuildKind.Patch)
            {
                if(request.PublicProfilePath!=null&&NativeBuildProfile.Parse(File.ReadAllText(request.PublicProfilePath)).Json!=profile.Json)throw new BuildFailedException("Patch public profile differs from the selected Player archive.");
                foreach(string path in new[]{"ProjectSettings/ProjectSettings.asset","ProjectSettings/HybridCLRSettings.asset",GeneratedAot,"Assets/Scripts/Framework/HotUpdate/Generated/link.xml"})prepared[path]=File.ReadAllBytes(Path.Combine(request.PlayerArchive,"Inputs",path));
            }
            prepared[PublicAsset]=System.Text.Encoding.UTF8.GetBytes(profile.Json+"\n");
            if(!string.IsNullOrEmpty(request.HotfixOverlay))
            {
                string root=Path.GetFullPath(request.HotfixOverlay);SourceFileTransaction.RejectReparsePoints(root);
                foreach(string source in Directory.GetFiles(root,"*.cs",SearchOption.AllDirectories))
                {
                    SourceFileTransaction.RejectReparsePoints(source);string relative=source.Substring(root.TrimEnd(Path.DirectorySeparatorChar).Length+1).Replace('\\','/');
                    if(!relative.StartsWith("Assets/Scripts/Game/",StringComparison.Ordinal))throw new BuildFailedException("HotfixOverlay may contain only C# inputs beneath Assets/Scripts/Game.");
                    prepared[relative]=File.ReadAllBytes(source);
                }
            }
            return new BuildInputs(request,result,profile,prepared);
        }
        internal void Install()
        {
            foreach(var pair in overlay){string target=Path.GetFullPath(pair.Key);RequireOwnedSource(target);SourceFileTransaction.RejectReparsePoints(target);Directory.CreateDirectory(Path.GetDirectoryName(target));File.WriteAllBytes(target,pair.Value);AssetDatabase.ImportAsset(pair.Key,ImportAssetOptions.ForceSynchronousImport|ImportAssetOptions.ForceUpdate);}
        }
        internal void VerifyRestoration()
        {
            var after=SourceHashes();File.WriteAllText(Path.Combine(result.OutputDirectory,"source-after.json"),JsonConvert.SerializeObject(after,Formatting.Indented)+"\n");
            if(JsonConvert.SerializeObject(before)!=JsonConvert.SerializeObject(after))throw new BuildFailedException("Source/config/settings restoration differs; see source-before/source-after.json.");
        }
        internal static void RequireOwnedSource(string full)
        {string project=Path.GetFullPath(".")+Path.DirectorySeparatorChar;if(!full.StartsWith(project,StringComparison.OrdinalIgnoreCase))throw new BuildFailedException("Build mutation escapes the locked project.");}
        private static Dictionary<string,string> SourceHashes()
        {return new[]{"Assets","Packages","ProjectSettings"}.SelectMany(p=>Directory.GetFiles(p,"*",SearchOption.AllDirectories)).OrderBy(p=>p,StringComparer.Ordinal).ToDictionary(p=>p.Replace('\\','/'),p=>HotfixLoadPlan.Hash(File.ReadAllBytes(p)),StringComparer.Ordinal);}
    }
}
