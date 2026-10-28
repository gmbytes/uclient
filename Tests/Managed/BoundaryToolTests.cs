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
    static (int Exit,string Output) Run(string root)
    {
        var start=new ProcessStartInfo("pwsh") { UseShellExecute=false, RedirectStandardOutput=true, RedirectStandardError=true };
        foreach(var arg in new[]{"-NoProfile","-File",Path.GetFullPath(Path.Combine(Root,"../comm/tools/Validate-Boundaries.ps1")),"-Target","CSharp","-ClientRoot",root})start.ArgumentList.Add(arg);
        using var process=Process.Start(start)!; var stdout=process.StandardOutput.ReadToEndAsync();var stderr=process.StandardError.ReadToEndAsync();
        Assert.IsTrue(process.WaitForExit(30000),"Boundary validation did not terminate");
        return (process.ExitCode,stdout.GetAwaiter().GetResult()+stderr.GetAwaiter().GetResult());
    }
}
