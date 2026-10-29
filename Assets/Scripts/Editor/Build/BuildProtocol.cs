using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Client.Framework.HotUpdate;
using Newtonsoft.Json;
using UnityEditor.Build;

namespace Client.Editor.Build
{
    internal static class BuildProtocol
    {
        internal static void Verify(string output)
        {
            string comm=Environment.GetEnvironmentVariable("SGAME_COMM_ROOT");if(string.IsNullOrWhiteSpace(comm))throw new BuildFailedException("SGAME_COMM_ROOT must identify the read-only shared protocol source.");
            string project=Path.GetFullPath("."),proto=Path.Combine(comm,"proto"),tool=Path.Combine(comm,"tools/ProtocolGen/ProtocolGen.csproj");
            foreach(string kind in new[]{"Game","Envelope"})
            {
                string generated=Path.Combine(output,"Protocol",kind);
                var args=new System.Collections.Generic.List<string>{"run","--project",tool,"-p:SGameClientRoot="+project,"-p:RestoreLockedMode=true","--","--client-root",project,"--proto-root",proto,"--protocol-lock",Path.Combine(proto,"protocol.lock.json"),"--target","csharp"};
                if(kind=="Game")args.AddRange(new[]{"--client-messages",Path.Combine(proto,"client-messages.json"),"--output",generated});else args.AddRange(new[]{"--envelope-output",generated});
                Run("dotnet",args.ToArray(),Path.Combine(output,"Protocol",kind+"-command.json"));
                string current=kind=="Game"?"Assets/Scripts/Game/Protocol/Generated":"Assets/Scripts/Framework/Protocol/Envelope/Generated";
                foreach(string file in Directory.GetFiles(generated))
                {
                    string existing=Path.Combine(current,Path.GetFileName(file));
                    if(!File.Exists(existing)||HotfixLoadPlan.Hash(File.ReadAllBytes(existing))!=HotfixLoadPlan.Hash(File.ReadAllBytes(file)))throw new BuildFailedException("Generated protocol differs from imported source; regenerate/import it before building: "+existing);
                }
                if(!Directory.GetFiles(current,"*.cs").Select(Path.GetFileName).OrderBy(p=>p).SequenceEqual(Directory.GetFiles(generated,"*.cs").Select(Path.GetFileName).OrderBy(p=>p)))throw new BuildFailedException("Protocol C# source inventory differs.");
            }
        }
        internal static void Run(string executable,string[] arguments,string evidence)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(evidence));
            var start=new ProcessStartInfo(executable,string.Join(" ",arguments.Select(Quote))){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};
            using(var process=Process.Start(start))
            {
                File.WriteAllText(evidence,JsonConvert.SerializeObject(new{executable,arguments,pid=process.Id,status="Running"},Formatting.Indented)+"\n");
                var stdout=process.StandardOutput.ReadToEndAsync();var stderr=process.StandardError.ReadToEndAsync();bool exited=process.WaitForExit(300000);if(!exited){process.Kill();process.WaitForExit();}
                File.WriteAllText(evidence+".stdout.log",stdout.GetAwaiter().GetResult());File.WriteAllText(evidence+".stderr.log",stderr.GetAwaiter().GetResult());
                File.WriteAllText(evidence,JsonConvert.SerializeObject(new{executable,arguments,pid=process.Id,status="Exited",exitCode=process.ExitCode,timedOut=!exited},Formatting.Indented)+"\n");
                if(!exited||process.ExitCode!=0)throw new BuildFailedException("Owned protocol process failed: "+evidence);
            }
        }
        private static string Quote(string value)=>"\""+value.Replace("\\","\\").Replace("\"","\\\"").TrimEnd('\\')+"\"";
    }
}
