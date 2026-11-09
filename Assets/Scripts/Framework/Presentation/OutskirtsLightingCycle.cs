using System;
using System.Collections.Generic;
using UnityEngine;

namespace Client.Framework.Presentation
{
    public interface IOutskirtsClock
    {
        DateTimeOffset GameTime { get; }
    }

    public sealed class LocalOutskirtsClock : IOutskirtsClock
    {
        public DateTimeOffset GameTime => DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(8));
    }

    [Serializable]
    public sealed class OutskirtsLightingProfile
    {
        public string name;
        public int durationSeconds;
        public Color sunColor;
        public float sunIntensity;
        public Quaternion sunRotation;
        public Color ambientColor;
        public float lightmapScale;
    }

    [Serializable]
    public sealed class OutskirtsLightingSettings
    {
        public int startHour;
        public OutskirtsLightingProfile[] phases;

        public int PhaseAt(DateTimeOffset gameTime)
        {
            double elapsed = (gameTime.TimeOfDay.TotalSeconds - startHour * 3600 + 86400) % 86400;
            int end = 0;
            for (int i = 0; i < phases.Length; i++)
            {
                end += phases[i].durationSeconds;
                // HYGD uses an inclusive duration boundary, then switches on the next second.
                if (elapsed <= end) return i;
            }
            throw new InvalidOperationException("Outskirts lighting phases must span a full day.");
        }
    }

    [DisallowMultipleComponent]
    public sealed class OutskirtsLightingCycle : MonoBehaviour
    {
        [SerializeField] private Light referenceLight;
        [SerializeField] private OutskirtsLightingSettings settings;
        private readonly Dictionary<Material, Material> owned = new Dictionary<Material, Material>();
        private readonly List<GameObject> nightEffects = new List<GameObject>();
        private readonly List<GameObject> dayEffects = new List<GameObject>();
        private IOutskirtsClock clock = new LocalOutskirtsClock();
        private int phase = -1;
        private float nextRefresh;
        private bool initialized;
        public string CurrentPhase => phase < 0 ? string.Empty : settings.phases[phase].name;

        public void Configure(Light light, OutskirtsLightingSettings value)
        {
            referenceLight = light;
            settings = value;
            if (Application.isPlaying) RefreshNow();
        }

        public void SetClock(IOutskirtsClock value)
        {
            clock = value ?? throw new ArgumentNullException(nameof(value));
            phase = -1;
            RefreshNow();
        }

        private void OnEnable() { if (Application.isPlaying) RefreshNow(); }
        private void Update()
        {
            if (Time.unscaledTime < nextRefresh) return;
            nextRefresh = Time.unscaledTime + 1;
            RefreshNow();
        }

        public void RefreshNow()
        {
            if (!Application.isPlaying || referenceLight == null || settings?.phases == null) return;
            if (!initialized)
            {
                foreach (var node in GetComponentsInChildren<Transform>(true))
                {
                    if (node.name == "Night_Effects") nightEffects.Add(node.gameObject);
                    if (node.name == "Day_Effects") dayEffects.Add(node.gameObject);
                }
                foreach (var renderer in GetComponentsInChildren<Renderer>(true))
                {
                    var materials = renderer.sharedMaterials;
                    for (int i = 0; i < materials.Length; i++)
                    {
                        var original = materials[i];
                        if (original == null || !original.HasProperty("_ReferenceSun")) continue;
                        if (!owned.TryGetValue(original, out var instance))
                        {
                            instance = new Material(original) { hideFlags = HideFlags.HideAndDontSave };
                            instance.SetFloat("_MapOriginY", transform.position.y);
                            owned.Add(original, instance);
                        }
                        materials[i] = instance;
                    }
                    renderer.sharedMaterials = materials;
                }
                initialized = true;
            }
            int next = settings.PhaseAt(clock.GameTime);
            if (next == phase) return;
            phase = next;
            var profile = settings.phases[phase];
            referenceLight.color = profile.sunColor;
            referenceLight.intensity = profile.sunIntensity;
            referenceLight.transform.localRotation = profile.sunRotation;
            foreach (var effect in nightEffects) effect.SetActive(profile.lightmapScale >= 1);
            foreach (var effect in dayEffects) effect.SetActive(profile.lightmapScale < 1);
            foreach (var material in owned.Values)
            {
                material.SetVector("_ReferenceSun", profile.sunColor.linear * profile.sunIntensity);
                material.SetVector("_ReferenceAmbient", profile.ambientColor.linear);
                material.SetFloat("_BakedLightmapScale", profile.lightmapScale);
            }
        }

        private void OnDestroy()
        {
            foreach (var material in owned.Values)
                if (Application.isPlaying) Destroy(material); else DestroyImmediate(material);
            owned.Clear();
        }
    }
}
