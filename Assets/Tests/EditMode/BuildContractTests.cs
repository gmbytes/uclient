using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Client.Editor.UI;
using Client.Framework.UI;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;
using UnityGameFramework.Editor.ResourceTools;

namespace Client.Tests.EditMode
{
    [Category("D1")]
    public sealed class BuildContractTests
    {
        private string root;
        [SetUp] public void Setup() { root = Path.GetFullPath("Builds/Tests/D1/Cases/" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root); }
        [TearDown] public void Cleanup() { if (Directory.Exists(root)) Directory.Delete(root, true); }
        private static Type Required(string name)
        {
            var type = Type.GetType("Client.Editor.Build." + name + ", Client.Editor");
            Assert.That(type, Is.Not.Null, "D1 requires " + name);
            return type;
        }
        private static object Call(string type, string method, params object[] args)
        {
            try { return Required(type).GetMethod(method).Invoke(null, args); }
            catch (TargetInvocationException error) { System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error.InnerException).Throw(); throw; }
        }
        private static object Get(object value, string name) => value.GetType().GetProperty(name).GetValue(value);
        private object Request(string kind = "Player", string platform = "Windows64", string archive = null)
            => Activator.CreateInstance(Required("BuildRequest"), Enum.Parse(Required("BuildKind"), kind), Enum.Parse(Required("BuildPlatform"), platform), root, archive);

        // A reused root or guessed result path must not overwrite another run or claim success.
        [Test] public void ResolvedOutputIsUniqueAndResultUsesActualDirectory()
        {
            object first = Call("BuildOutput", "Create", Request());
            object second = Call("BuildOutput", "Create", Request());
            string path = (string)Get(first, "OutputDirectory");
            Assert.That(path, Does.StartWith(root + Path.DirectorySeparatorChar + "Build_"));
            Assert.That(Directory.Exists(path), Is.True);
            Assert.That(Get(second, "OutputDirectory"), Is.Not.EqualTo(path));
            Assert.That(Get(first, "ResultFile"), Is.EqualTo(Path.Combine(path, "build-result.json")));
            Assert.That(Get(first, "Status").ToString(), Is.EqualTo("Pending"));
            Assert.That(File.Exists((string)Get(first, "ResultFile")), Is.False);
        }

        [TestCase(null), TestCase("missing"), TestCase("pending")]
        public void PatchRequiresArchivedBaseline(string state)
        {
            string archive = state == null ? null : Path.Combine(root, state);
            if (state == "pending") { Directory.CreateDirectory(archive); File.WriteAllText(Path.Combine(archive, "pending.json"), "{}"); }
            Assert.Throws<BuildFailedException>(() => Call("BuildPreflight", "ValidateRequest", Request("Patch", "Windows64", archive)));
            Assert.That(Directory.GetDirectories(root, "Build_*"), Is.Empty);
        }

        [Test] public void MalformedFinalArchiveFailsClosed()
        {
            string archive = Path.Combine(root, "archive"); Directory.CreateDirectory(archive);
            File.WriteAllText(Path.Combine(archive, "archive.json"), "{}");
            Assert.Throws<BuildFailedException>(() => Call("BuildPreflight", "ValidateRequest", Request("Patch", "Windows64", archive)));
        }

        [Test] public void UnsupportedPlatformAndMismatchedActiveTargetAreRejected()
        {
            object request = Activator.CreateInstance(Required("BuildRequest"), Enum.Parse(Required("BuildKind"), "Player"), Enum.ToObject(Required("BuildPlatform"), 77), root, null);
            Assert.Throws<BuildFailedException>(() => Call("BuildPreflight", "ValidateRequest", request));
            string other = EditorUserBuildSettings.activeBuildTarget == BuildTarget.Android ? "Windows64" : "Android";
            Assert.Throws<BuildFailedException>(() => Call("BuildPreflight", "ValidateEnvironment", Request("Player", other)));
        }

        [Test] public void SecondBuildForSameProjectIsRejected()
        {
            using ((IDisposable)Call("ProjectBuildLock", "Acquire", root))
            {
                Assert.Throws<BuildFailedException>(() => Call("ProjectBuildLock", "Acquire", Path.Combine(root, ".")));
                string other = Path.Combine(root, "OtherProject"); Directory.CreateDirectory(other);
                using ((IDisposable)Call("ProjectBuildLock", "Acquire", other)) { }
            }
            using ((IDisposable)Call("ProjectBuildLock", "Acquire", root)) { }
        }

        [Test] public void ExternalOwnerLockIsRejectedWithoutDeletingIt()
        {
            string path = Path.Combine(root, "Library", "ClientBuild.lock"); Directory.CreateDirectory(Path.GetDirectoryName(path));
            using (var owner = new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
                Assert.Throws<BuildFailedException>(() => Call("ProjectBuildLock", "Acquire", root));
            Assert.That(File.Exists(path), Is.True);
            using ((IDisposable)Call("ProjectBuildLock", "Acquire", root)) { }
        }

        [Test] public void FailureRestoresSourceSettings()
        {
            string source = Path.Combine(root, "source.asset"), created = Path.Combine(root, "new", "generated.cs");
            byte[] bytes = { 0, 255, 10, 13 }, meta = { 5, 4, 3 };
            File.WriteAllBytes(source, bytes); File.WriteAllBytes(source + ".meta", meta);
            int width = PlayerSettings.defaultScreenWidth; bool development = EditorUserBuildSettings.development;
            string settings = Path.GetFullPath("ProjectSettings/ProjectSettings.asset"); byte[] settingsBefore = File.ReadAllBytes(settings);
            try
            {
                Assert.Throws<InvalidOperationException>(() =>
                {
                    using ((IDisposable)Call("BuildSourceTransaction", "Begin", (object)new string[] { source, created }))
                    {
                        File.WriteAllText(source, "changed"); File.Delete(source + ".meta");
                        Directory.CreateDirectory(Path.GetDirectoryName(created)); File.WriteAllText(created, "new"); File.WriteAllText(created + ".meta", "new-guid");
                        PlayerSettings.defaultScreenWidth = width + 17; EditorUserBuildSettings.development = !development;
                        AssetDatabase.SaveAssets(); throw new InvalidOperationException("injected build failure");
                    }
                });
                Assert.That(File.ReadAllBytes(source), Is.EqualTo(bytes)); Assert.That(File.ReadAllBytes(source + ".meta"), Is.EqualTo(meta));
                Assert.That(File.Exists(created), Is.False); Assert.That(File.Exists(created + ".meta"), Is.False);
                Assert.That(Directory.Exists(Path.GetDirectoryName(created)), Is.False);
                Assert.That(PlayerSettings.defaultScreenWidth, Is.EqualTo(width)); Assert.That(EditorUserBuildSettings.development, Is.EqualTo(development));
                Assert.That(File.ReadAllBytes(settings), Is.EqualTo(settingsBefore));
            }
            finally { PlayerSettings.defaultScreenWidth = width; EditorUserBuildSettings.development = development; File.WriteAllBytes(settings, settingsBefore); }
        }

        [Test] public void TransactionPreservesUnownedSiblingAndOrphanMeta()
        {
            string path = Path.Combine(root, "fresh", "owned.asset"), sibling = Path.Combine(root, "fresh", "unowned.txt");
            string orphan = Path.Combine(root, "orphan.asset"); File.WriteAllText(orphan + ".meta", "original-guid");
            using ((IDisposable)Call("SourceFileTransaction", "Capture", (object)new string[] { path, orphan }))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)); File.WriteAllText(path, "owned"); File.WriteAllText(sibling, "keep");
                File.WriteAllText(orphan, "temporary"); File.WriteAllText(orphan + ".meta", "changed-guid");
            }
            Assert.That(File.Exists(path), Is.False); Assert.That(File.ReadAllText(sibling), Is.EqualTo("keep"));
            Assert.That(File.Exists(orphan), Is.False); Assert.That(File.ReadAllText(orphan + ".meta"), Is.EqualTo("original-guid"));
        }

        [Test] public void NativeConfigurationSaveLoadUsesScopedPathsAndPreservesDefaults()
        {
            string defaults = Path.GetFullPath("Assets/GameFramework/Configs");
            string[] defaultPaths = { Path.Combine(defaults, "ResourceCollection.xml"), Path.Combine(defaults, "ResourceBuilder.xml") };
            var defaultMetas = defaultPaths.ToDictionary(p => p, p => "fileFormatVersion: 2\nguid: " + Guid.NewGuid().ToString("N") + "\n");
            using ((IDisposable)Call("SourceFileTransaction", "Capture", (object)defaultPaths))
            {
                Directory.CreateDirectory(defaults); foreach (string path in defaultPaths) { File.WriteAllText(path, "default-sentinel"); File.WriteAllText(path + ".meta", defaultMetas[path]); }
                string scoped = Path.Combine(root, "Config");
                using (var scope = (IDisposable)Call("ResourceConfigurationScope", "Enter", scoped))
                {
                    var collection = (ResourceCollection)scope.GetType().GetMethod("CreateCollection").Invoke(scope, null); Assert.That(collection.AddResource("d1/test", null, null, LoadType.LoadFromFile, true), Is.True);
                    Assert.That(collection.Save(), Is.True);
                    var loaded = new ResourceCollection(); Assert.That(loaded.Load(), Is.True); Assert.That(loaded.ResourceCount, Is.EqualTo(1));
                    var builder = (ResourceBuilderController)scope.GetType().GetMethod("CreateBuilder").Invoke(scope, null); builder.InternalResourceVersion = 42; Assert.That(builder.Save(), Is.True);
                    var readBuilder = new ResourceBuilderController(); Assert.That(readBuilder.Load(), Is.True); Assert.That(readBuilder.InternalResourceVersion, Is.EqualTo(43), "UGF Load advances the saved build version.");
                    Assert.That(File.Exists(Path.Combine(scoped, "ResourceCollection.xml")), Is.True); Assert.That(File.Exists(Path.Combine(scoped, "ResourceBuilder.xml")), Is.True);
                    Assert.Throws<BuildFailedException>(() => Call("ResourceConfigurationScope", "Enter", Path.Combine(root, "nested")));
                }
                foreach (string path in defaultPaths) { Assert.That(File.ReadAllText(path), Is.EqualTo("default-sentinel")); Assert.That(File.ReadAllText(path + ".meta"), Is.EqualTo(defaultMetas[path])); }
                Assert.That(File.Exists(Path.Combine(scoped, "ResourceCollection.xml")), Is.False);
            }
        }

        [Test] public void ConfigurationFailureRestoresExistingEffectiveBytesAndMetas()
        {
            string path = Path.Combine(root, "ResourceCollection.xml"); File.WriteAllText(path, "original"); File.WriteAllText(path + ".meta", "original-guid");
            Assert.Throws<InvalidOperationException>(() =>
            {
                using ((IDisposable)Call("ResourceConfigurationScope", "Enter", root))
                { Assert.That(new ResourceCollection().Save(), Is.True); File.WriteAllText(path + ".meta", "changed"); throw new InvalidOperationException(); }
            });
            Assert.That(File.ReadAllText(path), Is.EqualTo("original")); Assert.That(File.ReadAllText(path + ".meta"), Is.EqualTo("original-guid"));
        }

        [Test] public void HotfixAssembliesExcludedFromAot()
        {
            Call("BuildPreflight", "ValidateHotfixExclusion");
            var filterType = Type.GetType("HybridCLR.BuildProcessors.FilterHotFixAssemblies, HybridCLR.Editor");
            Assert.That(filterType, Is.Not.Null);
            var filtered = (string[])filterType.GetMethod("OnFilterAssemblies").Invoke(Activator.CreateInstance(filterType), new object[] { BuildOptions.None, new[] { "Library/Client.Framework.dll", "Library/Client.Game.dll", "Library/Client.Game.Protocol.dll" } });
            Assert.That(filtered, Is.EqualTo(new[] { "Library/Client.Framework.dll" }));
        }

        [Test] public void UIEditSceneExcludedFromPlayer()
        {
            var scenes = new[] { new EditorBuildSettingsScene("Assets/Scenes/Bootstrap.unity", true), new EditorBuildSettingsScene(UIWindowCreator.PreviewScenePath, true) };
            Assert.Throws<BuildFailedException>(() => Call("BuildPreflight", "ValidateScenes", (object)scenes));
            Call("BuildPreflight", "ValidateScenes", (object)new[] { scenes[0] });
        }

        [Test] public void InvalidUIBindingsBlockBuild()
        {
            string path = "Assets/Tests/Generated/D1InvalidUI.prefab";
            using ((IDisposable)Call("SourceFileTransaction", "Capture", (object)new[] { Path.GetFullPath(path) }))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)); var go = new GameObject("UIBroken", typeof(RectTransform), typeof(UIBindings));
                try
                {
                    go.GetComponent<UIBindings>().Configure(new[] { new UIBindings.Binding { Id = "Missing", Required = true, ComponentType = typeof(UnityEngine.UI.Button).AssemblyQualifiedName } }, Array.Empty<UIBindings.ArrayBinding>());
                    var prefab = PrefabUtility.SaveAsPrefabAsset(go, path);
                    Assert.Throws<BuildFailedException>(() => Call("BuildPreflight", "ValidateBindings", prefab, null));
                }
                finally { UnityEngine.Object.DestroyImmediate(go); }
            }
        }
    }
}
