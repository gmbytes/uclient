using System;
using System.IO;
using System.Reflection;
using Client.Editor.Build;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace Client.Tests.EditMode
{
    [Category("D1")]
    public sealed class BuildToolArchiveTests
    {
        private string root,caseRoot;
        [SetUp] public void Setup(){caseRoot=Path.GetFullPath("Builds/Tests/D1/ToolArchive/"+Guid.NewGuid().ToString("N"));root=Path.Combine(caseRoot,"Archive");Directory.CreateDirectory(root);File.WriteAllText(Path.Combine(root,"pending.json"),"owned stage fixture");}
        [TearDown] public void Cleanup(){if(Directory.Exists(caseRoot))Directory.Delete(caseRoot,true);}
        private static object Call(string name,params object[] values)
        {
            var type=Type.GetType("Client.Editor.Build.BuildToolArchive, Client.Editor");Assert.That(type,Is.Not.Null,"Independent Patch requires archived public producer inputs.");
            try{return type.GetMethod(name).Invoke(null,values);}catch(TargetInvocationException error){System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error.InnerException).Throw();throw;}
        }
        [Test] public void ProducerInputsAreCapturedAndMutationIsRejectedAgainstOriginalProfile()
        {
            string policy=Path.GetFullPath("Builds/LinkerPolicy/policy.json");Assert.That(File.Exists(policy),Is.True,"Run the explicit linker-policy producer prerequisite.");
            using(ReleaseLinkerSymbolPolicy.Apply(policy))
            {
                string profile=PlayerBaselineArchive.CurrentBuildProfile();
                Call("Capture",root,policy,profile,Path.Combine(caseRoot,"unused-private.key"));Call("Verify",root,profile);
                Assert.That(File.Exists(Path.Combine(root,"BuildTools/DisableSymbolsStep.cs")),Is.True);
                string helper=Path.Combine(root,"BuildTools/Client.DisableLinkerSymbols.dll");byte[] bytes=File.ReadAllBytes(helper);bytes[0]^=1;File.WriteAllBytes(helper,bytes);
                Assert.Throws<InvalidDataException>(()=>Call("Verify",root,profile));
            }
        }
        [Test] public void SelectedPrivateSignerCannotBecomeProducerEvidence()
        {
            string original=Path.GetFullPath("Builds/LinkerPolicy/policy.json");
            using(ReleaseLinkerSymbolPolicy.Apply(original))
            {
                string key=Path.Combine(caseRoot,"synthetic-private.key");File.WriteAllText(key,"synthetic location sentinel");
                var policy=JObject.Parse(File.ReadAllText(original));policy["helperPath"]=key;
                string forged=Path.Combine(caseRoot,"policy.json");File.WriteAllText(forged,policy.ToString());
                Assert.Throws<InvalidDataException>(()=>Call("Capture",root,forged,PlayerBaselineArchive.CurrentBuildProfile(),key));
                Assert.That(Directory.Exists(Path.Combine(root,"BuildTools")),Is.False);
            }
        }
    }
}
