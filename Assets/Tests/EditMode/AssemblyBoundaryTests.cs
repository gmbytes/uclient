using System;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor.Compilation;
using Client.Contracts;
using Client.Framework.HotUpdate;
using System.Security.Cryptography;

namespace Client.Tests.EditMode
{
    [Category("F3")]
    public sealed class AssemblyBoundaryTests
    {
        [Test]
        public void AotAssembliesNeverReferenceGame()
        {
            var assemblies = CompilationPipeline.GetAssemblies(AssembliesType.Player);
            foreach (string name in new[] { "Client.Contracts", "Client.Framework", "Client.Bootstrap", "Client.Game" })
                Assert.That(assemblies.Any(a => a.name == name), Is.True, "Missing assembly " + name);
            foreach (var assembly in assemblies.Where(a => !a.name.StartsWith("Client.Game")))
            {
                var compiled = System.Reflection.Assembly.Load(File.ReadAllBytes(assembly.outputPath));
                Assert.That(compiled.GetReferencedAssemblies().Any(a => a.Name.StartsWith("Client.Game")), Is.False, assembly.name);
            }
        }

        [Test]
        public void FrameworkNeverReferencesBootstrap()
        {
            var assemblies = CompilationPipeline.GetAssemblies(AssembliesType.Player);
            Assert.That(assemblies.Any(a => a.name == "Client.Bootstrap"), Is.True, "Bootstrap must have a separate asmdef.");
            var framework = assemblies.Single(a => a.name == "Client.Framework");
            var compiled = System.Reflection.Assembly.Load(File.ReadAllBytes(framework.outputPath));
            Assert.That(compiled.GetReferencedAssemblies().Any(a => a.Name == "Client.Bootstrap"), Is.False);
        }

        [Test]
        public void EntryIsLoadedOnce()
        {
            var loaderType = Type.GetType("Client.Framework.HotUpdate.HotfixAssemblyLoader, Client.Framework");
            Assert.That(loaderType, Is.Not.Null, "A once-only loader is required.");
            var verifiedType = Type.GetType("Client.Framework.HotUpdate.VerifiedAssembly, Client.Framework");
            Assert.That(verifiedType, Is.Not.Null);
            var loader = Activator.CreateInstance(loaderType, "Client.Game.Entry");
            var empty = Array.CreateInstance(verifiedType, 0);
            var load = loaderType.GetMethods().Single(m => m.Name == "LoadOnce" && m.GetParameters().Length == 1);
            var gate = loaderType.GetField("attempted", BindingFlags.Static | BindingFlags.NonPublic);
            object before = gate.GetValue(null);
            gate.SetValue(null, 0);
            try
            {
            var first = Assert.Throws<TargetInvocationException>(() => load.Invoke(loader, new object[] { empty }));
            Assert.That(first.InnerException, Is.TypeOf<ArgumentException>());
            var second = Assert.Throws<TargetInvocationException>(() => load.Invoke(loader, new object[] { empty }));
            Assert.That(second.InnerException, Is.TypeOf<InvalidOperationException>());
            var anotherLoader = Activator.CreateInstance(loaderType, "Client.Game.Entry");
            var anotherAttempt = Assert.Throws<TargetInvocationException>(() => load.Invoke(anotherLoader, new object[] { empty }));
            Assert.That(anotherAttempt.InnerException, Is.TypeOf<InvalidOperationException>());
            }
            finally { gate.SetValue(null, before); }
        }

        [Test]
        public void ContractsNeverReferenceImplementations()
        {
            var references = typeof(IClientContext).Assembly.GetReferencedAssemblies();
            Assert.That(references.Any(a => a.Name.StartsWith("Client.")), Is.False);
            Assert.That(typeof(IClientContext).GetProperty("Resources").PropertyType, Is.EqualTo(typeof(IResourceService)));
            Assert.That(typeof(IClientContext).GetProperty("UI").PropertyType, Is.EqualTo(typeof(IUIService)));
            Assert.That(typeof(IClientContext).GetProperty("Session").PropertyType, Is.EqualTo(typeof(ISessionClient)));
            Assert.That(typeof(SessionIdentity).GetProperty("Epoch").PropertyType, Is.EqualTo(typeof(ulong)));
            Assert.That(typeof(SessionIdentity).GetProperty("ResumeTokenTtlMs").PropertyType, Is.EqualTo(typeof(ulong)));
            Assert.That(typeof(LoginResult).GetProperty("ServerErrorCode").PropertyType, Is.EqualTo(typeof(uint)));
        }

        [TestCase(null, null)]
        [TestCase("", "")]
        [TestCase("credential-secret", "resume-secret")]
        [TestCase(" ", "\t")]
        public void LoginInputRejectsBothAndNeither(string credential, string resumeToken)
        {
            var error = Assert.Throws<ArgumentException>(() => new LoginInput(credential, resumeToken));
            Assert.That(error.Message, Does.Not.Contain("credential-secret").And.Not.Contain("resume-secret"));
        }

        [Test]
        public void LoginInputAcceptsExactlyOneValue()
        {
            Assert.That(new LoginInput("credential", null).Credential, Is.EqualTo("credential"));
            Assert.That(new LoginInput(null, "resume").ResumeToken, Is.EqualTo("resume"));
        }

        [Test]
        public void VerifiedAssemblyRejectsMismatchedHash()
        {
            Assert.Throws<ArgumentException>(() => new VerifiedAssembly("Client.Game", new byte[] { 1, 2, 3 }, new string('0', 64), Array.Empty<string>()));
        }

        [Test]
        public void VerifiedAssemblyOwnsAnImmutableCopy()
        {
            var bytes = new byte[] { 1, 2, 3 };
            var dependencies = new[] { "Client.Contracts" };
            string hash;
            using (var sha = SHA256.Create()) hash = BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
            var verified = new VerifiedAssembly("Client.Game", bytes, hash, dependencies);
            bytes[0] = 99;
            dependencies[0] = "Changed";
            var exposedBytes = verified.Bytes;
            exposedBytes[0] = 42;
            Assert.That(verified.Bytes, Is.EqualTo(new byte[] { 1, 2, 3 }));
            Assert.That(verified.Dependencies, Is.EqualTo(new[] { "Client.Contracts" }));
        }
    }
}
