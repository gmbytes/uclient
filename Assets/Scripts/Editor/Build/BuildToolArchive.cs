using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Client.Framework.HotUpdate;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Client.Editor.Build
{
    // Producer evidence accompanies the existing archive; it is not a release payload or another archive schema.
    public static class BuildToolArchive
    {
        private static readonly string[] Names={"DisableSymbolsStep.cs","Client.DisableLinkerSymbols.dll","csc.dll","policy.original.json"};
        public static void Capture(string archiveRoot,string policyPath,string originalBuildProfile,string signingKeyPath)
        {
            archiveRoot=Path.GetFullPath(archiveRoot);SourceFileTransaction.RejectReparsePoints(archiveRoot);
            if(!File.Exists(Path.Combine(archiveRoot,"pending.json"))||File.Exists(Path.Combine(archiveRoot,"archive.json")))throw new InvalidDataException("Producer inputs must be captured before archive finalization.");
            string output=Path.Combine(archiveRoot,"BuildTools");if(Directory.Exists(output)||File.Exists(output))throw new InvalidDataException("Archived producer inputs cannot be overwritten.");
            var policy=Read(policyPath);var expected=SymbolProfile(originalBuildProfile);
            string source=Path.GetFullPath((string)policy["sourcePath"]),helper=Path.GetFullPath((string)policy["helperPath"]),compiler=Path.GetFullPath((string)policy["compilerPath"]);
            string signer=Path.GetFullPath(signingKeyPath);
            var inputs=new Dictionary<string,string>{{Names[0],source},{Names[1],helper},{Names[2],compiler},{Names[3],Path.GetFullPath(policyPath)}};
            foreach(string input in inputs.Values)
            {SourceFileTransaction.RejectReparsePoints(input);if(input.Equals(signer,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Selected private signer cannot be collected as build-tool evidence.");}
            if(signer.StartsWith(archiveRoot.TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Private signer cannot reside in its archive.");
            RequireHash(source,(string)policy["sourceSha256"]);RequireHash(helper,(string)policy["helperSha256"]);RequireHash(compiler,(string)policy["compilerSha256"]);
            RequireHash("Tool/Client/Linker/DisableSymbolsStep.cs",(string)policy["sourceSha256"]);
            if((int?)policy["schemaVersion"]!=1||(int?)policy["compilerExitCode"]!=0||policy["compilerArguments"]?.Type!=JTokenType.Array)throw new InvalidDataException("Invalid linker producer evidence.");
            foreach(string field in new[]{"sourceSha256","helperSha256","monoLinkerSha256","unityLinkerSha256"})if((string)policy[field]!=(string)expected[field])throw new InvalidDataException("Producer evidence differs from original Player profile: "+field);
            if(AssemblyNameOf(helper)!="Client.DisableLinkerSymbols"||AssemblyNameOf(compiler)!="csc")throw new InvalidDataException("Unexpected producer assembly identity.");
            Directory.CreateDirectory(output);
            foreach(var input in inputs)File.Copy(input.Value,Path.Combine(output,input.Key));
            var records=PlayerBaselineArchive.Inventory(output);
            File.WriteAllText(Path.Combine(output,"manifest.json"),JsonConvert.SerializeObject(new{schemaVersion=1,originalBuildProfile,files=records},Formatting.Indented)+"\n",new System.Text.UTF8Encoding(false));
            Verify(archiveRoot,originalBuildProfile);
        }
        public static void Verify(string archiveRoot,string originalBuildProfile)
        {
            string root=Path.GetFullPath(Path.Combine(archiveRoot,"BuildTools"));SourceFileTransaction.RejectReparsePoints(root);
            var manifest=Read(Path.Combine(root,"manifest.json"));
            if((int?)manifest["schemaVersion"]!=1||(string)manifest["originalBuildProfile"]!=originalBuildProfile)throw new InvalidDataException("Archived producer profile differs.");
            var records=manifest["files"]?.ToObject<PlayerBaselineArchive.FileRecord[]>();
            if(records==null||records.Length!=Names.Length||!new HashSet<string>(records.Select(r=>r.path),StringComparer.Ordinal).SetEquals(Names))throw new InvalidDataException("Incomplete producer input inventory.");
            if(!Directory.GetFiles(root,"*",SearchOption.AllDirectories).Select(Path.GetFileName).OrderBy(n=>n,StringComparer.Ordinal).SequenceEqual(Names.Concat(new[]{"manifest.json"}).OrderBy(n=>n,StringComparer.Ordinal)))throw new InvalidDataException("Unexpected producer artifact files.");
            foreach(var record in records){string path=Path.Combine(root,record.path);SourceFileTransaction.RejectReparsePoints(path);if(new FileInfo(path).Length!=record.length)throw new InvalidDataException("Producer input length differs.");RequireHash(path,record.sha256);}
            var expected=SymbolProfile(originalBuildProfile);var policy=Read(Path.Combine(root,"policy.original.json"));
            RequireHash(Path.Combine(root,Names[0]),(string)expected["sourceSha256"]);RequireHash(Path.Combine(root,Names[1]),(string)expected["helperSha256"]);RequireHash(Path.Combine(root,Names[2]),(string)policy["compilerSha256"]);
            foreach(string field in new[]{"sourceSha256","helperSha256","monoLinkerSha256","unityLinkerSha256"})if((string)policy[field]!=(string)expected[field])throw new InvalidDataException("Archived linker input differs from original fingerprint.");
        }
        private static JObject SymbolProfile(string buildProfile)
        {var profile=JObject.Parse(buildProfile);if((bool?)profile["linkSymbols"]!=false||profile["symbolPolicy"]?.Type!=JTokenType.String)throw new InvalidDataException("Original deterministic symbol policy is required.");return JObject.Parse((string)profile["symbolPolicy"]);}
        private static JObject Read(string path)
        {if(!File.Exists(path)||new FileInfo(path).Length>256*1024)throw new InvalidDataException("Missing or oversized producer evidence.");return JObject.Parse(File.ReadAllText(path),new JsonLoadSettings{DuplicatePropertyNameHandling=DuplicatePropertyNameHandling.Error});}
        private static void RequireHash(string path,string expected)
        {if(string.IsNullOrEmpty(expected)||HotfixLoadPlan.Hash(File.ReadAllBytes(path))!=expected)throw new InvalidDataException("Producer input hash differs: "+Path.GetFileName(path));}
        private static string AssemblyNameOf(string path)
        {using(var module=dnlib.DotNet.ModuleDefMD.Load(File.ReadAllBytes(path)))return module.Assembly?.Name.String;}
    }
}
