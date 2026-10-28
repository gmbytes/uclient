using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityGameFramework.Runtime;

namespace Client.Tests
{
    public sealed class ProjectBaselineTests
    {
        [Test, Category("F1")]
        public void FrameworkPrefabContainsOnlyTheAOTFoundation()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/App/GameFramework.prefab");
            Assert.That(prefab, Is.Not.Null);
            Assert.That(prefab.GetComponentsInChildren<GameFrameworkComponent>(true).Length, Is.EqualTo(3));
            Assert.That(prefab.GetComponent<BaseComponent>(), Is.Not.Null);
            Assert.That(prefab.GetComponent<EventComponent>(), Is.Not.Null);
            Assert.That(prefab.GetComponent<DownloadComponent>(), Is.Not.Null);
            Assert.That(prefab.GetComponent<BaseComponent>().EditorResourceMode, Is.False);
        }

        [Test, Category("F1")]
        public void BothPlatformsUseIL2CPPAndNetStandard21()
        {
            Assert.That(Application.unityVersion, Is.EqualTo("2022.3.25f1"));
            foreach (var group in new[] { BuildTargetGroup.Standalone, BuildTargetGroup.Android })
            {
                Assert.That(PlayerSettings.GetScriptingBackend(group), Is.EqualTo(ScriptingImplementation.IL2CPP));
                Assert.That(PlayerSettings.GetApiCompatibilityLevel(group), Is.EqualTo(ApiCompatibilityLevel.NET_Standard));
            }
            Assert.That(PlayerSettings.Android.targetArchitectures, Is.EqualTo(AndroidArchitecture.ARM64));
            Assert.That(EditorBuildSettings.scenes.Length, Is.EqualTo(1));
            Assert.That(EditorBuildSettings.scenes[0].path, Is.EqualTo("Assets/Scenes/Bootstrap.unity"));
            Assert.That(EditorBuildSettings.scenes[0].enabled, Is.True);
        }

        [Test, Category("F1")]
        public void OfficialDependenciesAreAvailableToConsumers()
        {
            Assert.That(typeof(TMPro.TMP_Text).Assembly.GetName().Name, Is.EqualTo("Unity.TextMeshPro"));
            Assert.That(typeof(UnityEngine.UI.Button).Assembly.GetName().Name, Is.EqualTo("UnityEngine.UI"));
            Assert.That(Newtonsoft.Json.JsonConvert.DeserializeObject<int>("42"), Is.EqualTo(42));
            Assert.That(GameFramework.Version.GameFrameworkVersion, Is.Not.Empty);
            Assert.That(File.Exists("Assets/ThirdParty/GameFramework/LICENSE.md"), Is.True);
            Assert.That(File.Exists("Packages/com.code-philosophy.hybridclr/LICENSE"), Is.True);
        }

        [Test, Category("F1")]
        public void ImportedDependenciesMatchTheImmutableLock()
        {
            var dependencyLock = JObject.Parse(File.ReadAllText("docs/dependencies.lock.json"));
            Assert.That(Application.unityVersion, Is.EqualTo((string)dependencyLock["unity"]["version"]));
            Assert.That(GameFramework.Version.GameFrameworkVersion,
                Is.EqualTo((string)dependencyLock["unityGameFramework"]["gameFrameworkLibrary"]["version"]));
            foreach (var library in new[] { dependencyLock["unityGameFramework"]["gameFrameworkLibrary"], dependencyLock["unityGameFramework"]["sharpZipLib"] })
            {
                using var sha256 = SHA256.Create();
                var hash = string.Concat(sha256.ComputeHash(File.ReadAllBytes((string)library["path"])).Select(value => value.ToString("x2")));
                Assert.That(hash, Is.EqualTo((string)library["sha256"]), (string)library["path"]);
            }
            var packages = UnityEditor.PackageManager.PackageInfo.GetAllRegisteredPackages().ToDictionary(package => package.name);
            foreach (var package in ((JObject)dependencyLock["unityPackages"]).Properties())
                Assert.That(packages[package.Name].version, Is.EqualTo((string)package.Value), package.Name);
            Assert.That(packages["com.code-philosophy.hybridclr"].version, Is.EqualTo((string)dependencyLock["hybridCLR"]["version"]));
        }
    }
}
