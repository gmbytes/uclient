using System.Linq;
using Client.Editor.UI;
using Client.Framework.UI;
using NUnit.Framework;
using UnityEditor;

namespace Client.Tests
{
    public sealed class OutskirtsAuthoringTests
    {
        [Test, Category("Outskirts")]
        public void GeneratedMapKeepsPersistentGroundAndModelMaterials()
        {
            var root = AssetDatabase.LoadAssetAtPath<UnityEngine.GameObject>("Assets/Content/Prefabs/Outskirts/BeginnerOutskirts.prefab");
            Assert.That(root, Is.Not.Null);
            foreach (var renderer in root.GetComponentsInChildren<UnityEngine.MeshRenderer>(true))
            {
                if (!renderer.enabled || renderer.GetComponent<UnityEngine.MeshFilter>() == null) continue;
                Assert.That(renderer.sharedMaterials, Is.Not.Empty, renderer.name);
                Assert.That(renderer.sharedMaterials.All(material => material != null && !string.IsNullOrEmpty(AssetDatabase.GetAssetPath(material))), Is.True,
                    "Missing or transient material on " + renderer.name);
            }
        }

        [Test, Category("Outskirts")]
        public void OutskirtsWindowMeetsThePlayerBuildAuthoringContract()
        {
            var catalog = AssetDatabase.LoadAssetAtPath<UIViewDefinitions>(UIBindingValidator.CatalogPath);
            var definition = catalog.Definitions.Single(item => item.ViewID == "UIOutskirts");
            var report = UIAuthoringValidator.Validate(definition);
            Assert.That(report.IsValid, Is.True, report.ToString());
        }
    }
}
