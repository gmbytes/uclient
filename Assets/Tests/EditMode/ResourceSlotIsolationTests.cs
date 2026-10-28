using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Build.Player;
using UnityEngine;

namespace Client.Tests
{
    public sealed class ResourceSlotIsolationTests
    {
        [Test, Category("F2")]
        public void ProofHarnessIsExcludedFromNormalPlayerCompilation()
        {
            Assert.That(PlayerSettings.GetScriptingDefineSymbolsForGroup(BuildTargetGroup.Standalone).Split(';'),
                Does.Not.Contain("RESOURCE_SLOT_PROOF"));
            string[] dependencies = AssetDatabase.GetDependencies("Assets/Scenes/Bootstrap.unity", true);
            Assert.That(dependencies.Any(path => path.EndsWith("ResourceSlotProof.cs", StringComparison.Ordinal)), Is.False);
            Assert.That(dependencies.Any(path => path.Contains("Assets/Tests/Generated")), Is.False);
            string output = Path.GetFullPath(Path.Combine("Builds", "Tests", "F2", "ReleaseIsolation-" + Guid.NewGuid().ToString("N")));
            Directory.CreateDirectory(output);
            var settings = new ScriptCompilationSettings
            {
                group = BuildTargetGroup.Standalone,
                target = BuildTarget.StandaloneWindows64,
                options = ScriptCompilationOptions.None
            };
            PlayerBuildInterface.CompilePlayerScripts(settings, output);
            string[] assemblies = Directory.GetFiles(output, "*.dll").Select(Path.GetFileName).OrderBy(name => name).ToArray();
            File.WriteAllText(Path.Combine(output, "assembly-list.json"), JsonUtility.ToJson(new AssemblyList { assemblies = assemblies }, true) + "\n");
            Assert.That(assemblies, Does.Contain("Client.Framework.dll"));
            Assert.That(assemblies, Does.Contain("UnityGameFramework.Runtime.dll"));
            Assert.That(assemblies, Does.Not.Contain("Client.ResourceSlot.Proof.dll"));
            Debug.Log("RESOURCE_SLOT_RELEASE_ISOLATION_PASS " + output);
        }

        [Serializable]
        private sealed class AssemblyList { public string[] assemblies; }
    }
}
