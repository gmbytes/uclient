#if UNITY_EDITOR
using Client.Framework.UI;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Client.Tests
{
    [Category("U2")]
    public sealed class AdaptiveUIAssetTests
    {
        [Test]
        public void SharedRootAndStartupUseOneScalerAndSafeContent()
        {
            var root=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Content/UI/Common/AdaptiveUIRoot.prefab");
            Assert.That(root,Is.Not.Null); Assert.That(root.GetComponent<Canvas>().renderMode,Is.EqualTo(RenderMode.ScreenSpaceOverlay));
            Assert.That(root.GetComponentsInChildren<CanvasScaler>(true).Length,Is.EqualTo(1));
            Assert.That(root.GetComponent<AdaptiveCanvas>(),Is.Not.Null); Assert.That(root.GetComponent<ScreenLayoutComponent>(),Is.Not.Null);
            Assert.That(root.GetComponentInChildren<SafeAreaRoot>(true),Is.Not.Null);
            Assert.That(root.GetComponentsInChildren<EventSystem>(true),Is.Empty);
            Assert.That(root.transform.Find("Windows"),Is.Not.Null);Assert.That(root.transform.Find("SafeArea/Windows"),Is.Null,"Windows must be a full-frame sibling of global-only safe content.");
            var startup=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/App/UIUpdate.prefab");
            Assert.That(startup.GetComponent<AdaptiveCanvas>(),Is.Not.Null);
            var status=startup.GetComponentInChildren<Text>(true);
            Assert.That(status.GetComponentInParent<SafeAreaRoot>(true),Is.Not.Null,"Startup status belongs inside the shared safe area.");
            var gameRoot=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/App/GameRoot.prefab");
            Assert.That(gameRoot.activeSelf,Is.False);
            Assert.That(gameRoot.GetComponentsInChildren<AdaptiveCanvas>(true).Length,Is.EqualTo(1));
            var nested=gameRoot.GetComponentInChildren<AdaptiveCanvas>(true);Assert.That(nested.transform.Find("Windows"),Is.Not.Null);Assert.That(nested.transform.Find("SafeArea/Windows"),Is.Null);
        }
    }
}
#endif
