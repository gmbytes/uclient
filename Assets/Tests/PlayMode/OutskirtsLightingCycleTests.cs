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

        [UnityTest, Category("Outskirts")]
        public IEnumerator ClockChangesOwnedLightingAndReleasesMaterialCopies()
        {
            var root = new GameObject("Outskirts lighting verification");
            var material = new Material(Shader.Find("Client/OutskirtsSurface"));
            Material copy = null;
            var spriteMaterial = new Material(Shader.Find("Client/OutskirtsSprite"));
            Material spriteCopy = null;
            var treeMaterial = new Material(Shader.Find("Client/OutskirtsSurface")) { name = "main_map_tree_verification" };
            Material treeCopy = null;
            try
            {
                material.SetVector("_ReferenceAmbient", new Vector4(9, 8, 7, 6));
                var mesh = GameObject.CreatePrimitive(PrimitiveType.Cube);
                mesh.transform.SetParent(root.transform, false);
                mesh.GetComponent<Renderer>().sharedMaterial = material;
                var tree = GameObject.CreatePrimitive(PrimitiveType.Cube);
                tree.transform.SetParent(root.transform, false);
                tree.GetComponent<Renderer>().sharedMaterial = treeMaterial;
                var sprite = new GameObject("ResourceSprite", typeof(SpriteRenderer)).GetComponent<SpriteRenderer>();
                sprite.transform.SetParent(root.transform, false);
                sprite.sharedMaterial = spriteMaterial;
                sprite.color = new Color(1, 1, 1, .4f);
                var night = new GameObject("Night_Effects"); night.transform.SetParent(root.transform, false);
                var day = new GameObject("Day_Effects"); day.transform.SetParent(root.transform, false);
                var sunObject = new GameObject("Sun", typeof(Light)); sunObject.transform.SetParent(root.transform, false);
                var sun = sunObject.GetComponent<Light>();
                var settings = JsonUtility.FromJson<OutskirtsLightingSettings>(AssetDatabase.LoadAssetAtPath<TextAsset>(
                    "Assets/Content/Maps/Outskirts/LightingCycle.json").text);
                var cycle = root.AddComponent<OutskirtsLightingCycle>();
                cycle.Configure(sun, settings);
                var clock = new Clock { GameTime = new DateTimeOffset(2026, 11, 7, 21, 0, 0, TimeSpan.FromHours(8)) };
                cycle.SetClock(clock);
                copy = mesh.GetComponent<Renderer>().sharedMaterial;
                Assert.That(copy, Is.Not.SameAs(material));
                treeCopy = tree.GetComponent<Renderer>().sharedMaterial;
                Assert.That(treeCopy.GetColor("_VegetationTint"), Is.EqualTo(settings.phases[3].vegetationTint));
                Assert.That(copy.GetColor("_VegetationTint"), Is.EqualTo(Color.white), "Only tree materials receive the additional vegetation tint.");
                spriteCopy = sprite.sharedMaterial;
                Assert.That(spriteCopy, Is.Not.SameAs(spriteMaterial));
                Assert.That(spriteCopy.GetVector("_ReferenceAmbient"), Is.EqualTo(copy.GetVector("_ReferenceAmbient")));
                Assert.That(sprite.color, Is.EqualTo(new Color(1, 1, 1, .4f)), "Environment radiance must be applied once in the shader, preserving opacity.");
                var nightRadiance = spriteCopy.GetVector("_ReferenceSun");
                Assert.That(cycle.CurrentPhase, Is.EqualTo("Night"));
                Assert.That(sun.intensity, Is.EqualTo(3));
                Assert.That(copy.GetFloat("_BakedLightmapScale"), Is.EqualTo(1));
                Assert.That(night.activeSelf, Is.True); Assert.That(day.activeSelf, Is.False);
                clock.GameTime = clock.GameTime.AddHours(-8);
                cycle.RefreshNow();
                Assert.That(cycle.IsTransitioning, Is.True);
                Assert.That(sun.intensity, Is.EqualTo(3), "HYGD retains the old environment during its .6-second switch delay.");
                yield return new WaitForSecondsRealtime(.3f);
                Assert.That(copy.GetFloat("_BakedLightmapScale"), Is.InRange(.1f, .9f));
                Assert.That(treeCopy.GetColor("_VegetationTint"), Is.Not.EqualTo(settings.phases[3].vegetationTint));
                Assert.That(treeCopy.GetColor("_VegetationTint"), Is.Not.EqualTo(settings.phases[1].vegetationTint));
                Assert.That(sun.intensity, Is.EqualTo(3));
                yield return new WaitForSecondsRealtime(.8f);
                Assert.That(cycle.IsTransitioning, Is.False);
                Assert.That(treeCopy.GetColor("_VegetationTint"), Is.EqualTo(settings.phases[1].vegetationTint));
                Assert.That(cycle.CurrentPhase, Is.EqualTo("Day"));
                Assert.That(sun.intensity, Is.EqualTo(1.8f));
                Assert.That(spriteCopy.GetVector("_ReferenceSun"), Is.Not.EqualTo(nightRadiance));
                Assert.That(spriteCopy.GetVector("_ReferenceSun"), Is.EqualTo(copy.GetVector("_ReferenceSun")));
                Assert.That(spriteCopy.GetFloat("_ReferenceDiffuse"), Is.EqualTo(Mathf.Max(0, -sun.transform.forward.y)).Within(.001f));
                Assert.That(copy.GetFloat("_BakedLightmapScale"), Is.Zero);
                Assert.That(night.activeSelf, Is.False); Assert.That(day.activeSelf, Is.True);
                Assert.That(material.GetVector("_ReferenceAmbient"), Is.EqualTo(new Vector4(9, 8, 7, 6)));
            }
            finally { Object.DestroyImmediate(root); Object.DestroyImmediate(material); Object.DestroyImmediate(spriteMaterial); Object.DestroyImmediate(treeMaterial); }
            yield return null;
            Assert.That(spriteCopy == null, Is.True);
            Assert.That(treeCopy == null, Is.True);
            Assert.That(copy == null, Is.True, "Closing the map must release its lighting material instances.");
        }
    }
}
#endif
