using System;
using System.Linq;
using Client.Framework.Presentation;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Client.Tests
{
    public sealed class OutskirtsLightingTests
    {
        [TestCase(5, 59, 59, "Night")]
        [TestCase(6, 0, 0, "Dawn")]
        [TestCase(12, 0, 0, "Dawn")]
        [TestCase(12, 0, 1, "Day")]
        [TestCase(16, 0, 0, "Day")]
        [TestCase(16, 0, 1, "Dusk")]
        [TestCase(20, 0, 0, "Dusk")]
        [TestCase(20, 0, 1, "Night")]
        [TestCase(23, 59, 59, "Night")]
        [Category("Outskirts")]
        public void PhaseBoundariesMatchTheOriginalGameClock(int hour, int minute, int second, string expected)
        {
            var json = AssetDatabase.LoadAssetAtPath<TextAsset>("Assets/Content/Maps/Outskirts/LightingCycle.json");
            var settings = JsonUtility.FromJson<OutskirtsLightingSettings>(json.text);
            Assert.That(settings.phases.Sum(profile => profile.durationSeconds), Is.EqualTo(86400));
            var time = new DateTimeOffset(2026, 11, 7, hour, minute, second, TimeSpan.FromHours(8));
            Assert.That(settings.phases[settings.PhaseAt(time)].name, Is.EqualTo(expected));
        }
    }
}
