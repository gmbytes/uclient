using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Client.Framework.HotUpdate;
using Client.Framework.Release;
using HybridCLR;
using HybridCLR.Commands;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.Build;

namespace Client.Editor.Build
{
    public static class PatchBuilder
    {
        internal static void Build(BuildRequest request,BuildResult result,BuildInputs inputs,ResourceConfigurationScope scope)
        {
            var archive=PlayerBaselineArchive.Open(request.PlayerArchive);PlayerBuilder.ApplySettings(request,inputs.Profile);AssetDatabase.SaveAssets();
            // Exact serialized native inputs are restored after live settings are applied, before hashing/compilation.
            inputs.Install();BuildProtocol.Verify(result.OutputDirectory);
            CompileDllCommand.CompileDll(BuildPreflight.Target(request.Platform),false);string compiled=SettingsUtil.GetHotUpdateDllsOutputDirByTarget(BuildPreflight.Target(request.Platform));HotfixCompatibilityCheck.RecordCompileInputs(compiled);Check(request.PlayerArchive,compiled,result);
            string release=Path.Combine(result.OutputDirectory,"Release");ResourcePackageBuilder.Build(request,result,scope,release);
            BuildFiles.CopyTree(Path.Combine(request.PlayerArchive,"Metadata"),Path.Combine(release,"Metadata"));BuildFiles.CopyTree(Path.Combine(request.PlayerArchive,"Facades"),Path.Combine(release,"References"));
            foreach(string name in new[]{"Client.Game.Protocol","Client.Game"})BuildFiles.Put(Path.Combine(release,"Hotfix",name+".dll"),File.ReadAllBytes(Path.Combine(compiled,name+".dll")));
            SignPayload(request,inputs.Profile,archive,compiled,release,true);BuildFiles.CopyTree(compiled,Path.Combine(result.OutputDirectory,"Compiled"));
            result.PlayerBuildId=archive.playerBuildId;result.ReleaseId=request.ReleaseId;
        }
        internal static void Check(string archive,string compiled,BuildResult result)
        {var report=HotfixCompatibilityCheck.Check(archive,compiled);string path=Path.Combine(result.OutputDirectory,"compatibility.json");File.WriteAllText(path,JsonConvert.SerializeObject(report,Formatting.Indented)+"\n");result.EvidenceFiles.Add(path);if(report.RequiresNewPlayer)throw new BuildFailedException("Original Player compatibility failed: "+string.Join("; ",report.MissingAotReferences));}
        internal static void SignPayload(BuildRequest request,NativeBuildProfile profile,PlayerBaselineArchive.Archive archive,string compiled,string release,bool channel)
        {
            string[] names={"Client.Game.Protocol","Client.Game"};var files=PlayerBaselineArchive.Inventory(release);
            string protocol=(string)JObject.Parse(File.ReadAllText("Assets/Scripts/Game/Protocol/Generated/protocol-manifest.json"))["schema_sha256"];
            var manifest=new JObject{["schemaVersion"]=1,["keyId"]=profile.KeyId,["releaseId"]=request.ReleaseId,["player"]=JObject.FromObject(new {archive.playerBuildId,archive.platform,archive.architecture,archive.unityVersion,archive.contractVersion,archive.envelopeSha256,archive.aotMetadataSetId}),["protocolSha256"]=protocol,["resourceVersionListPath"]="Resources/GameFrameworkVersion.dat",["files"]=new JArray(files.Select(f=>new JObject{["path"]=f.path,["length"]=f.length,["sha256"]=f.sha256,["kind"]=f.path.StartsWith("Metadata/")?"AotMetadata":f.path.StartsWith("Hotfix/")?"HotfixDll":f.path.StartsWith("References/")?"Configuration":"NativeResource"})),["assemblies"]=new JArray(names.Select(name=>{using(var module=dnlib.DotNet.ModuleDefMD.Load(File.ReadAllBytes(Path.Combine(compiled,name+".dll"))))return new JObject{["name"]=name,["path"]="Hotfix/"+name+".dll",["dependencies"]=new JArray(module.GetAssemblyRefs().Select(a=>a.Name.String))};})),["assemblyOrder"]=new JArray(names)};
            using(var key=RSA.Create()){key.FromXmlString(File.ReadAllText(request.SigningKeyPath));ReleaseManifestWriter.WriteSignedWithProfile(release,System.Text.Encoding.UTF8.GetBytes(manifest.ToString(Formatting.None)),profile,key,archive.Identity);}
            if(channel)SignChannel(request,profile,archive,release);
        }
        internal static void SignChannel(BuildRequest request,NativeBuildProfile profile,PlayerBaselineArchive.Archive archive,string release)
        {
            byte[] json=System.Text.Encoding.UTF8.GetBytes(new JObject{["schemaVersion"]=1,["keyId"]=profile.KeyId,["releaseId"]=request.ReleaseId,["manifestRelativePath"]="release.json",["manifestSha256"]=HotfixLoadPlan.Hash(File.ReadAllBytes(Path.Combine(release,"release.json")))}.ToString(Formatting.None));
            using(var key=RSA.Create())
            {key.FromXmlString(File.ReadAllText(request.SigningKeyPath));profile.RequireSigner(key);byte[] signature=key.SignData(json,HashAlgorithmName.SHA256,RSASignaturePadding.Pkcs1);var verifier=profile.CreateVerifier();var channel=verifier.VerifyChannel(json,signature);verifier.VerifyPinnedRelease(channel,File.ReadAllBytes(Path.Combine(release,"release.json")),File.ReadAllBytes(Path.Combine(release,"release.sig")),archive.Identity);File.WriteAllBytes(Path.Combine(release,"channel.json.pending"),json);File.WriteAllBytes(Path.Combine(release,"channel.sig.pending"),signature);}
        }
    }
}
