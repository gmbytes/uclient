using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityGameFramework.Runtime;

namespace Client.Tests
{
    public sealed class BootstrapSmokeTests
    {
        [UnityTest, Category("F1")]
        public IEnumerator BootstrapCreatesOneFrameworkHost()
        {
            yield return SceneManager.LoadSceneAsync("Bootstrap", LoadSceneMode.Single);
            yield return null;

            var components = Object.FindObjectsOfType<GameFrameworkComponent>(true);
            var hosts = components.Select(component => component.transform.root.gameObject).Distinct().ToArray();
            Assert.That(hosts.Length, Is.EqualTo(1), "Bootstrap must create exactly one framework root host.");
            Assert.That(Object.FindObjectsOfType<BaseComponent>(true).Length, Is.EqualTo(1));
            Assert.That(Object.FindObjectsOfType<EventComponent>(true).Length, Is.EqualTo(1));
            Assert.That(Object.FindObjectsOfType<DownloadComponent>(true).Length, Is.EqualTo(1));
            Assert.That(Object.FindObjectsOfType<DownloadComponent>(true)[0].TotalAgentCount, Is.EqualTo(3));
            Assert.That(Object.FindObjectsOfType<ResourceComponent>(true), Is.Empty);
            Assert.That(Object.FindObjectsOfType<UIComponent>(true), Is.Empty);
            LogAssert.NoUnexpectedReceived();
        }
    }
}
