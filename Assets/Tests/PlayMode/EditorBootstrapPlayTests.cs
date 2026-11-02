#if UNITY_EDITOR
using System;
using System.Collections;
using System.Linq;
using System.Text.RegularExpressions;
using Client.Framework.UI;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;
using UnityGameFramework.Runtime;

namespace Client.Tests
{
    [Category("E1")]
    public sealed class EditorBootstrapPlayTests
    {
        const string ScenePath = "Assets/Scenes/Bootstrap.unity";

        [UnityTearDown]
        public IEnumerator Cleanup()
        {
            var bootstrap = SceneManager.GetSceneByPath(ScenePath);
            if (bootstrap.isLoaded)
            {
                var empty = SceneManager.CreateScene("After Editor Bootstrap Test");
                SceneManager.SetActiveScene(empty);
                yield return SceneManager.UnloadSceneAsync(bootstrap);
            }
            GameEntry.Shutdown(ShutdownType.None);
            yield return null;
        }

        [UnityTest]
        public IEnumerator OrdinaryBootstrapPlayOpensLocalLoginWithoutPlayerBuildInputs()
        {
            yield return SceneManager.LoadSceneAsync(ScenePath, LoadSceneMode.Single);
            yield return WaitForLogin();
            Assert.That(GameEntry.GetComponent<BaseComponent>().EditorResourceMode, Is.True);
            Assert.That(UnityEngine.Object.FindObjectsOfType<Camera>().Any(c => c.enabled), Is.True,
                "Editor Play must have a rendering camera instead of the GameView no-camera overlay.");
            var bindings = Login();
            Assert.That(((Text)bindings.Resolve("VersionLabel")).text, Does.Contain("本地模拟"));
            ((InputField)bindings.Resolve("AccountInput")).text = "10001";
            ((Button)bindings.Resolve("LoginButton")).onClick.Invoke();
            yield return null;
            yield return null;
            Assert.That(((Text)bindings.Resolve("StatusLabel")).text, Does.Contain("本地模拟"));
            Assert.That(UnityEngine.Object.FindObjectsOfType<MonoBehaviour>().Any(x => x != null && x.GetType().Name == "NativeClientHost"), Is.False);
        }

        [UnityTest]
        public IEnumerator ReloadingBootstrapReleasesPreviousEditorLoginAndCreatesOnlyOneReplacement()
        {
            yield return SceneManager.LoadSceneAsync(ScenePath, LoadSceneMode.Single);
            yield return WaitForLogin();
            var previous = Login();
            yield return SceneManager.LoadSceneAsync(ScenePath, LoadSceneMode.Single);
            yield return WaitForLogin();
            Assert.That(previous == null, Is.True, "The old login must belong to the unloaded scene.");
            Assert.That(UnityEngine.Object.FindObjectsOfType<UIBindings>().Count(x => x.gameObject.name.StartsWith("UILogin", StringComparison.Ordinal)), Is.EqualTo(1));
            Assert.That(UnityEngine.Object.FindObjectsOfType<Camera>().Count(x => x.gameObject.name == "Editor Camera"), Is.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator FailedStartupCanReloadAfterCorrectingTheLocalProfile()
        {
            var profile = AssetDatabase.LoadAssetAtPath<LoginPresentationProfile>("Assets/Settings/Environment/LocalSimulationProfile.asset");
            var original = profile.Mode;
            bool reported = false;
            try
            {
                profile.Mode = (LoginPresentationMode)999;
                LogAssert.Expect(LogType.Exception, new Regex("InvalidOperationException: Unsupported login presentation mode"));
                yield return SceneManager.LoadSceneAsync(ScenePath, LoadSceneMode.Single);
                float deadline = Time.realtimeSinceStartup + 10;
                while (!reported && Time.realtimeSinceStartup < deadline)
                {
                    reported = UnityEngine.Object.FindObjectsOfType<Text>().Any(x => x.text == "Editor startup failed. See Console.");
                    yield return null;
                }
            }
            finally { profile.Mode = original; }
            Assert.That(reported, Is.True, "The malformed source profile must fail visibly before recovery.");
            yield return SceneManager.LoadSceneAsync(ScenePath, LoadSceneMode.Single);
            yield return WaitForLogin();
            Assert.That(UnityEngine.Object.FindObjectsOfType<UIBindings>().Count(x => x.gameObject.name.StartsWith("UILogin", StringComparison.Ordinal)), Is.EqualTo(1));
        }

        static UIBindings Login() => UnityEngine.Object.FindObjectsOfType<UIBindings>()
            .SingleOrDefault(x => x.gameObject.name.StartsWith("UILogin", StringComparison.Ordinal));

        static IEnumerator WaitForLogin()
        {
            float deadline = Time.realtimeSinceStartup + 10;
            while (Login() == null && Time.realtimeSinceStartup < deadline)
            {
                var setupFailure = UnityEngine.Object.FindObjectsOfType<Text>()
                    .FirstOrDefault(x => x.text.Contains("Startup setup is incomplete"));
                Assert.That(setupFailure, Is.Null,
                    "The checked-in Bootstrap scene has no Editor startup composition.");
                yield return null;
            }
            Assert.That(Login(), Is.Not.Null, "Ordinary Editor Play did not open the real login page.");
            // Entry reports local readiness after an actual rendered frame.
            yield return null;
            yield return null;
        }
    }
}
#endif
