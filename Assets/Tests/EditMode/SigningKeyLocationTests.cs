using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using Client.Editor.Build;
using NUnit.Framework;
using UnityEditor.Build;

namespace Client.Tests.EditMode
{
    [Category("D1")]
    public sealed class SigningKeyLocationTests
    {
        private string root,key;
        [SetUp] public void Setup(){root=Path.GetFullPath("Builds/Tests/D1/Signer/"+Guid.NewGuid().ToString("N"));key=Path.Combine(root,"keys","synthetic.key");Directory.CreateDirectory(Path.GetDirectoryName(key));File.WriteAllText(key,"synthetic location fixture; not a private key");}
        [TearDown] public void Cleanup(){if(Directory.Exists(root))Directory.Delete(root,true);}
        private BuildRequest Request(string path,string archive=null)=>new BuildRequest(archive==null?BuildKind.Player:BuildKind.Patch,BuildPlatform.Windows64,Path.Combine(root,"output"),archive,null,path,null,"v1",null);
        private static void Validate(BuildRequest request)
        {
            var method=typeof(BuildPreflight).GetMethod("ValidateSigningKeyLocation");Assert.That(method,Is.Not.Null,"A signer collection/location gate is required before any build output mutation.");
            try{method.Invoke(null,new object[]{request});}catch(TargetInvocationException error){System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error.InnerException).Throw();}
        }
        [TestCase("StreamingAssets"),TestCase("Resources")] public void CollectedAssetTreesRejectSyntheticSigner(string kind)
        {
            string path=Path.GetFullPath("Assets/"+kind+"/D1Signer-"+Guid.NewGuid().ToString("N")+"/synthetic.key");
            using(SourceFileTransaction.Capture(new[]{path}))
            {Directory.CreateDirectory(Path.GetDirectoryName(path));File.WriteAllText(path,"synthetic location fixture");Assert.Throws<BuildFailedException>(()=>Validate(Request(path)));}
        }
        [Test] public void GitTrackedNonCollectedSourceCannotBeSelectedAsSigner()
        {Assert.Throws<BuildFailedException>(()=>Validate(Request(Path.GetFullPath(".gitignore"))));}
        [TestCase("output"),TestCase("archive"),TestCase("published")] public void CollectedOutputArchiveAndPublishedTreesRejectSigner(string kind)
        {
            string directory=Path.Combine(root,kind),path=Path.Combine(directory,"synthetic.key");Directory.CreateDirectory(directory);File.Copy(key,path);
            if(kind=="published")File.WriteAllText(Path.Combine(directory,"release.json"),"owned fixture marker");
            Assert.Throws<BuildFailedException>(()=>Validate(Request(path,kind=="archive"?directory:null)));
        }
        [Test] public void IsolatedIgnoredBuildsKeyRemainsAllowed()
        {Assert.DoesNotThrow(()=>Validate(Request(key)));}
        [Test] public void ForbiddenSignerIsRejectedBeforeOutputReservation()
        {
            string path=Path.GetFullPath("Assets/StreamingAssets/D1Signer-"+Guid.NewGuid().ToString("N")+"/synthetic.key");
            using(SourceFileTransaction.Capture(new[]{path}))
            {Directory.CreateDirectory(Path.GetDirectoryName(path));File.Copy(key,path);Assert.Throws<BuildFailedException>(()=>ClientBuild.Execute(Request(path)));Assert.That(Directory.Exists(Path.Combine(root,"output")),Is.False);}
        }
        [Test] public void ReparseAliasCannotHideCollectedSourceLocation()
        {
            string path=Path.GetFullPath("Assets/Resources/D1Signer-"+Guid.NewGuid().ToString("N")+"/synthetic.key"),alias=Path.Combine(root,"alias");
            using(SourceFileTransaction.Capture(new[]{path}))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));File.Copy(key,path);
                RunPowerShell("New-Item -ItemType Junction -Path "+Quote(alias)+" -Target "+Quote(Path.GetDirectoryName(path))+" | Out-Null");
                try{Assert.Throws<BuildFailedException>(()=>Validate(Request(Path.Combine(alias,"synthetic.key"))));}
                finally
                {
                    Assert.That(Path.GetFullPath(alias),Does.StartWith(root+Path.DirectorySeparatorChar));Assert.That((File.GetAttributes(alias)&FileAttributes.ReparsePoint)!=0,Is.True);
                    Assert.That(Path.GetFullPath(RunPowerShell("(Get-Item -LiteralPath "+Quote(alias)+" -Force).Target").Trim()),Is.EqualTo(Path.GetDirectoryName(path)));Directory.Delete(alias,false);
                }
            }
        }
        [Test] public void PhysicalHardLinkIntoAssetsCannotBeHiddenByAllowedOriginalPath()
        {
            string path=Path.GetFullPath("Assets/Resources/D1Signer-"+Guid.NewGuid().ToString("N")+"/synthetic.key");
            using(SourceFileTransaction.Capture(new[]{path}))
            {Directory.CreateDirectory(Path.GetDirectoryName(path));Assert.That(CreateHardLink(path,key,IntPtr.Zero),Is.True,"Owned hard-link fixture creation failed: "+Marshal.GetLastWin32Error());Assert.Throws<BuildFailedException>(()=>Validate(Request(key)));}
        }
        [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true,EntryPoint="CreateHardLinkW")]
        [return:MarshalAs(UnmanagedType.Bool)] private static extern bool CreateHardLink(string path,string target,IntPtr reserved);
        private static string Quote(string value)=>"'"+value.Replace("'","''")+"'";
        private static string RunPowerShell(string command)
        {
            var start=new System.Diagnostics.ProcessStartInfo("pwsh","-NoProfile -NonInteractive -EncodedCommand "+Convert.ToBase64String(Encoding.Unicode.GetBytes("$ErrorActionPreference='Stop'; "+command))){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};
            using(var process=System.Diagnostics.Process.Start(start)){var stdout=process.StandardOutput.ReadToEndAsync();var stderr=process.StandardError.ReadToEndAsync();process.WaitForExit();Assert.That(process.ExitCode,Is.Zero,stderr.Result);return stdout.Result;}
        }
    }
}
