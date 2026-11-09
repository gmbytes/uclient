#if UNITY_EDITOR
using System;
using System.Collections;
using Client.Framework.Presentation;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Client.Tests
{
    public sealed class OutskirtsLightingCycleTests
    {
        private sealed class Clock : IOutskirtsClock { public DateTimeOffset GameTime { get; set; } }

        [UnityTest, Category("CityOutskirts")]
        public IEnumerator ClockChangesOwnedLightingAndReleasesMaterialCopies()
        {
            var root = new GameObject("Outskirts lighting verification");
            var material = new Material(Shader.Find("Client/OutskirtsSurface"));
            Material copy = null;
            try
            {
                material.SetVector("_ReferenceAmbient", new Vector4(9, 8, 7, 6));
                var mesh = GameObject.CreatePrimitive(PrimitiveType.Cube);
                mesh.transform.SetParent(root.transform, false);
                mesh.GetComponent<Renderer>().sharedMaterial = material;
                var night = new GameObject("Night_Effects"); night.transform.SetParent(root.transform, false);
                var day = new GameObject("Day_Effects"); day.transform.SetParent(root.transform, false);
                var sunObject = new GameObject("Sun", typeof(Light)); sunObject.transform.SetParent(root.transform, false);
                var sun = sunObject.GetComponent<Light>();
                var settings = JsonUtility.FromJson<OutskirtsLightingSettings>(AssetDatabase.LoadAssetAtPath<TextAsset>(
                    "Assets/Content/Maps/CityOutskirts/LightingCycle.json").text);
                var cycle = root.AddComponent<OutskirtsLightingCycle>();
                cycle.Configure(sun, settings);
                var clock = new Clock { GameTime = new DateTimeOffset(2026, 11, 7, 21, 0, 0, TimeSpan.FromHours(8)) };
                cycle.SetClock(clock);
                copy = mesh.GetComponent<Renderer>().sharedMaterial;
                Assert.That(copy, Is.Not.SameAs(material));
                Assert.That(cycle.CurrentPhase, Is.EqualTo("Night"));
                Assert.That(sun.intensity, Is.EqualTo(3));
                Assert.That(copy.GetFloat("_BakedLightmapScale"), Is.EqualTo(1));
                Assert.That(night.activeSelf, Is.True); Assert.That(day.activeSelf, Is.False);
                clock.GameTime = clock.GameTime.AddHours(-8);
                cycle.RefreshNow();
                Assert.That(cycle.CurrentPhase, Is.EqualTo("Day"));
                Assert.That(sun.intensity, Is.EqualTo(1.8f));
                Assert.That(copy.GetFloat("_BakedLightmapScale"), Is.Zero);
                Assert.That(night.activeSelf, Is.False); Assert.That(day.activeSelf, Is.True);
                Assert.That(material.GetVector("_ReferenceAmbient"), Is.EqualTo(new Vector4(9, 8, 7, 6)));
            }
            finally { Object.DestroyImmediate(root); Object.DestroyImmediate(material); }
            yield return null;
            Assert.That(copy == null, Is.True, "Closing the map must release its lighting material instances.");
        }
    }
}
#endif
