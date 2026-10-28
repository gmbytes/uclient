using System.Diagnostics;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Client.Tests.Managed;

[TestClass]
public sealed class BoundaryToolTests
{
    static string Root => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../../.."));
    [DataTestMethod]
    [DataRow("using UnityEditor;", "Unity dependency")]
    [DataRow("using Client.Game.Hotfix;", "reverse source dependency")]
    public void ActualCSharpRootsRejectReverseDependencies(string content,string diagnostic)
    {
        var probe=Path.Combine(Root,"Assets/Scripts/Framework/Network/Core/N4BoundaryProbe.cs");
        try { File.WriteAllText(probe,content); var result=Run(Root); Assert.AreNotEqual(0,result.Exit); StringAssert.Contains(result.Output,diagnostic); }
        finally { File.Delete(probe); }
    }
    [TestMethod]
    public void MissingAndEmptyRequiredRootsCannotPass()
    {
        var root=Path.Combine(Root,"Builds/ProtocolInterop/boundary-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var missing=Run(root); Assert.AreNotEqual(0,missing.Exit); StringAssert.Contains(missing.Output,"Required CSharp root missing");
        Directory.CreateDirectory(Path.Combine(root,"Assets/Scripts/Framework/Protocol"));
        var empty=Run(root); Assert.AreNotEqual(0,empty.Exit); StringAssert.Contains(empty.Output,"scanned zero source files");
    }
    [DataTestMethod]
    [DataRow("using Hot = Client.Game.Hotfix;")]
    [DataRow("using static Client.Game.Hotfix.Entry;")]
    [DataRow("class Probe { object value = Client.Game.Hotfix.Entry.Value; }")]
    [DataRow("class Probe { object value = global::Client.Game.Hotfix.Entry.Value; }")]
    [DataRow("using Hot = Client.Hotfix.Entry;")]
    [DataRow("using static Client.Editor.Entry;")]
    [DataRow("class Probe { object value = Client.Tests.Entry.Value; }")]
    [DataRow("class Probe { object value = global::GameFramework.Entry.Value; }")]
    [DataRow("using Framework = UnityGameFramework.Runtime;")]
    public void EveryReferenceFormIsRejectedInPureRootsAndSelectedContracts(string content)
    {
        var root=CreateFixture();var failures=new List<string>();
        foreach(var relative in new[]{"Assets/Scripts/Framework/Network/Core/N4BoundaryProbe.cs",
            "Assets/Scripts/Framework/Contracts/SessionContracts.cs","Assets/Scripts/Framework/Contracts/ISessionClient.cs"})
        {
            var path=Path.Combine(root,relative);var existed=File.Exists(path);var original=existed?File.ReadAllBytes(path):null;
            try
            {
                File.WriteAllText(path,content);var result=Run(root);
                var diagnostic=relative.Contains("Contracts/")?"CSharp contract reverse dependency":"CSharp reverse source dependency";
                if(result.Exit==0 || !result.Output.Contains(diagnostic))failures.Add($"Accepted or missed diagnostic for {content} in {relative}: {result.Output}");
            }
            finally { if(existed)File.WriteAllBytes(path,original!);else File.Delete(path); }
        }
        Assert.AreEqual(0,failures.Count,string.Join("\n",failures));
    }
    [TestMethod]
    public void CSharpIgnoresRetiredRuntimeWhileExplicitAndDefaultLegacyStillInspectIt()
    {
        var root=CreateFixture();var path=Path.Combine(root,"Packages/com.sgame.client/Runtime/Network/Residue.cs");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);File.WriteAllText(path,"using UnityEngine;\nusing SGame.Client.Host;\n");
        var csharp=Run(root);Assert.AreEqual(0,csharp.Exit,csharp.Output);
        var legacy=Run(root,"Legacy");Assert.AreNotEqual(0,legacy.Exit);StringAssert.Contains(legacy.Output,"Pure module has Unity dependency");StringAssert.Contains(legacy.Output,"Runtime reverse source dependency");
        var defaults=Run(root,null);Assert.AreEqual(legacy.Exit,defaults.Exit);StringAssert.Contains(defaults.Output,"Pure module has Unity dependency");StringAssert.Contains(defaults.Output,"Runtime reverse source dependency");
    }
    [TestMethod]
    public void CSharpStillRejectsRepositorySecretFilenamesOutsidePureRoots()
    {
        var root=CreateFixture();File.WriteAllText(Path.Combine(root,"credentials.key"),"fixture-only");
        var result=Run(root);Assert.AreNotEqual(0,result.Exit);StringAssert.Contains(result.Output,"Secret filename");
    }
    static string CreateFixture()
    {
        var root=Path.Combine(Root,"Builds/ProtocolInterop/boundary-fixture-"+Guid.NewGuid().ToString("N"));
        foreach(var relative in new[]{"Assets/Scripts/Framework/Protocol","Assets/Scripts/Framework/Network/Core","Assets/Scripts/Framework/Contracts"})
            foreach(var source in Directory.GetFiles(Path.Combine(Root,relative),"*",SearchOption.AllDirectories))
            {
                var path=Path.Combine(root,Path.GetRelativePath(Root,source));Directory.CreateDirectory(Path.GetDirectoryName(path)!);File.Copy(source,path);
            }
        var start=new ProcessStartInfo("git"){UseShellExecute=false,RedirectStandardOutput=true,RedirectStandardError=true};
        foreach(var argument in new[]{"-C",root,"init","--quiet"})start.ArgumentList.Add(argument);
        using var process=Process.Start(start)!;Assert.IsTrue(process.WaitForExit(10000));Assert.AreEqual(0,process.ExitCode,process.StandardError.ReadToEnd());
        return root;
    }
    static (int Exit,string Output) Run(string root,string target="CSharp")
    {
        var start=new ProcessStartInfo("pwsh") { UseShellExecute=false, RedirectStandardOutput=true, RedirectStandardError=true };
        foreach(var arg in new[]{"-NoProfile","-File",Path.GetFullPath(Path.Combine(Root,"../comm/tools/Validate-Boundaries.ps1")),"-ClientRoot",root})start.ArgumentList.Add(arg);
        if(target!=null){start.ArgumentList.Add("-Target");start.ArgumentList.Add(target);}
        using var process=Process.Start(start)!; var stdout=process.StandardOutput.ReadToEndAsync();var stderr=process.StandardError.ReadToEndAsync();
        Assert.IsTrue(process.WaitForExit(30000),"Boundary validation did not terminate");
        return (process.ExitCode,stdout.GetAwaiter().GetResult()+stderr.GetAwaiter().GetResult());
    }
}
