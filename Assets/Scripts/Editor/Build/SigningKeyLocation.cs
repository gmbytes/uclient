using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using UnityEditor.Build;

namespace Client.Editor.Build
{
    internal static class SigningKeyLocation
    {
        private static readonly string[] SourceTrees={"Assets","Packages","ProjectSettings","Library","HybridCLRData","Temp","obj","Tool","Tests","docs"};
        internal static void Require(BuildRequest request)
        {
            if(request==null)throw new BuildFailedException("A build request is required.");
            // Missing signer is handled as a normal request failure; no selected secret exists to collect.
            if(string.IsNullOrWhiteSpace(request.SigningKeyPath))return;
            try
            {
                string key=Path.GetFullPath(request.SigningKeyPath);SourceFileTransaction.RejectReparsePoints(key);
                if(!File.Exists(key))throw new BuildFailedException("The selected private signer must be an existing regular file.");
                string project=Path.GetFullPath(".");RejectSourceTrees(key,project);
                foreach(string collected in new[]{request.OutputRoot,request.PlayerArchive,request.HotfixOverlay})
                    if(!string.IsNullOrWhiteSpace(collected)){string tree=Path.GetFullPath(collected);SourceFileTransaction.RejectReparsePoints(tree);if(Within(key,tree))throw new BuildFailedException("Private signer cannot be inside a collected build/archive/overlay tree.");}
                if(!string.IsNullOrWhiteSpace(request.LinkerPolicyPath)){string policy=Path.GetFullPath(request.LinkerPolicyPath);SourceFileTransaction.RejectReparsePoints(policy);if(Within(key,Path.GetDirectoryName(policy)))throw new BuildFailedException("Private signer cannot be inside collected linker inputs.");}
                if(!string.IsNullOrWhiteSpace(request.PublicProfilePath)&&key.Equals(Path.GetFullPath(request.PublicProfilePath),StringComparison.OrdinalIgnoreCase))throw new BuildFailedException("Private signer and public profile must be separate inputs.");
                string comm=Environment.GetEnvironmentVariable("SGAME_COMM_ROOT");
                if(!string.IsNullOrWhiteSpace(comm))foreach(string tree in new[]{"proto","tools"})if(Within(key,Path.Combine(Path.GetFullPath(comm),tree)))throw new BuildFailedException("Private signer cannot be a shared protocol/tool source input.");
                string repository=null;
                for(string parent=Path.GetDirectoryName(key);parent!=null;parent=Path.GetDirectoryName(parent))
                {
                    if(new[]{".build-owner","archive.json","pending.json","release.json","channel.json","channel.json.pending"}.Any(name=>File.Exists(Path.Combine(parent,name))))throw new BuildFailedException("Private signer cannot reside in an existing build or published artifact tree.");
                    if(repository==null&&(File.Exists(Path.Combine(parent,".git"))||Directory.Exists(Path.Combine(parent,".git"))))repository=parent;
                }
                if(repository!=null)
                {
                    RejectSourceTrees(key,repository);
                    string parent=Path.GetDirectoryName(key),name=Path.GetFileName(key);
                    int tracked=Git(parent,"ls-files --error-unmatch -- "+Quote(":(literal)"+name));
                    if(tracked==0)throw new BuildFailedException("Private signer cannot be Git-tracked source.");
                    if(tracked!=1)throw new BuildFailedException("Cannot verify private signer Git ownership.");
                    if(Git(parent,"check-ignore --no-index -- "+Quote(name))!=0)throw new BuildFailedException("A project-relative private signer must be isolated in an ignored directory.");
                }
#if UNITY_EDITOR_WIN
                using(var handle=new FileStream(key,FileMode.Open,FileAccess.Read,FileShare.Read))
                {
                    if(!GetFileInformationByHandle(handle.SafeFileHandle.DangerousGetHandle(),out var information))throw new BuildFailedException("Cannot verify private signer physical file identity.");
                    if(information.Links!=1)throw new BuildFailedException("Private signer cannot have physical hard-link aliases.");
                }
#endif
            }
            catch(BuildFailedException){throw;}
            catch(Exception error){throw new BuildFailedException("Unsafe private signer location: "+error.Message);}
        }
        private static void RejectSourceTrees(string key,string root)
        {foreach(string tree in SourceTrees)if(Within(key,Path.Combine(root,tree)))throw new BuildFailedException("Private signer cannot be a source/native/resource input.");}
        private static bool Within(string path,string root)=>path.Equals(root,StringComparison.OrdinalIgnoreCase)||path.StartsWith(root.TrimEnd(Path.DirectorySeparatorChar,Path.AltDirectorySeparatorChar)+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase);
        private static int Git(string directory,string arguments)
        {
            var start=new ProcessStartInfo("git","-C "+Quote(directory)+" "+arguments){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};
            using(var process=Process.Start(start)){var output=process.StandardOutput.ReadToEndAsync();var error=process.StandardError.ReadToEndAsync();if(!process.WaitForExit(10000)){process.Kill();process.WaitForExit();throw new BuildFailedException("Owned Git signer-location query timed out.");}output.GetAwaiter().GetResult();error.GetAwaiter().GetResult();return process.ExitCode;}
        }
        private static string Quote(string value)
        {if(value.IndexOfAny(new[]{'"','\r','\n'})>=0)throw new BuildFailedException("Invalid signer-location process argument.");int trailing=value.Reverse().TakeWhile(c=>c=='\\').Count();return "\""+value+new string('\\',trailing)+"\"";}
#if UNITY_EDITOR_WIN
        [StructLayout(LayoutKind.Sequential)] private struct FileInformation
        {public uint Attributes,CreationLow,CreationHigh,AccessLow,AccessHigh,WriteLow,WriteHigh,Volume,SizeHigh,SizeLow,Links,IndexHigh,IndexLow;}
        [DllImport("kernel32.dll",SetLastError=true)]
        [return:MarshalAs(UnmanagedType.Bool)] private static extern bool GetFileInformationByHandle(IntPtr file,out FileInformation information);
#endif
    }
}
