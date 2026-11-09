using System.Linq;
using Client.Editor.UI;
using Client.Framework.UI;
using NUnit.Framework;
using UnityEditor;

namespace Client.Tests
{
    public sealed class CityOutskirtsAuthoringTests
    {
        [Test, Category("CityOutskirts")]
        public void OutskirtsWindowMeetsThePlayerBuildAuthoringContract()
        {
            var catalog = AssetDatabase.LoadAssetAtPath<UIViewDefinitions>(UIBindingValidator.CatalogPath);
            var definition = catalog.Definitions.Single(item => item.ViewID == "UICityOutskirts");
            var report = UIAuthoringValidator.Validate(definition);
            Assert.That(report.IsValid, Is.True, report.ToString());
        }
    }
}
