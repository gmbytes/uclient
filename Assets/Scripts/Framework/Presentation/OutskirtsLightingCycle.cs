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
        public Color vegetationTint = Color.white;
    }

    [Serializable]
    public sealed class OutskirtsLightingSettings
    {
        public int startHour;
        public float environmentSwitchDelaySeconds = .6f;
        public float lightmapTransitionSeconds = 1f;
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
        private readonly HashSet<Material> vegetation = new HashSet<Material>();
        private Color currentVegetationTint = Color.white, transitionFromVegetationTint;
        private readonly List<GameObject> nightEffects = new List<GameObject>();
        private readonly List<GameObject> dayEffects = new List<GameObject>();
        private IOutskirtsClock clock = new LocalOutskirtsClock();
        private int phase = -1;
        private float nextRefresh;
        private bool initialized;
        private bool snapNext = true;
        private bool transitioning;
        private bool environmentApplied;
        private float transitionStart, transitionFromScale, currentLightmapScale;
        private OutskirtsLightingProfile transitionTarget;
        public bool IsTransitioning => transitioning;
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
            snapNext = true;
            transitioning = false;
            RefreshNow();
        }

        private void OnEnable() { if (Application.isPlaying) RefreshNow(); }
        private void Update()
        {
            if (Time.unscaledTime >= nextRefresh)
            {
                nextRefresh = Time.unscaledTime + 1;
                RefreshNow();
            }
            UpdateTransition();
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
                        if (original.name.IndexOf("tree", StringComparison.OrdinalIgnoreCase) >= 0 && instance.HasProperty("_VegetationTint"))
                            vegetation.Add(instance);
                        materials[i] = instance;
                    }
                    renderer.sharedMaterials = materials;
                }
                initialized = true;
            }
            int next = settings.PhaseAt(clock.GameTime);
            if (next == phase) return;
            bool first = phase < 0 || snapNext;
            phase = next;
            var profile = settings.phases[phase];
            snapNext = false;
            if (first)
            {
                transitioning = false;
                currentLightmapScale = profile.lightmapScale;
                ApplyEnvironment(profile);
                ApplyLightmapScale(currentLightmapScale);
                return;
            }
            // HYGD ColorLookupData.ChangeEnvironment delays the environment switch by .6s;
            // GameSceneBuilding fades the baked lightmap scale over 1s in parallel.
            transitionTarget = profile;
            transitionStart = Time.unscaledTime;
            transitionFromScale = currentLightmapScale;
            transitionFromVegetationTint = currentVegetationTint;
            environmentApplied = false;
            transitioning = true;
            UpdateTransition();
        }

        private void UpdateTransition()
        {
            if (!transitioning) return;
            float elapsed = Time.unscaledTime - transitionStart;
            float duration = Mathf.Max(.001f, settings.lightmapTransitionSeconds);
            currentLightmapScale = Mathf.Lerp(transitionFromScale, transitionTarget.lightmapScale, Mathf.Clamp01(elapsed / duration));
            ApplyLightmapScale(currentLightmapScale);
            ApplyVegetationTint(Color.Lerp(transitionFromVegetationTint, transitionTarget.vegetationTint, Mathf.Clamp01(elapsed / duration)));
            if (!environmentApplied && elapsed >= settings.environmentSwitchDelaySeconds)
            {
                ApplyEnvironment(transitionTarget);
                environmentApplied = true;
            }
            if (environmentApplied && elapsed >= duration) transitioning = false;
        }

        private void ApplyLightmapScale(float value)
        {
            foreach (var material in owned.Values) material.SetFloat("_BakedLightmapScale", value);
        }

        private void ApplyVegetationTint(Color value)
        {
            currentVegetationTint = value;
            foreach (var material in vegetation) material.SetColor("_VegetationTint", value);
        }

        private void ApplyEnvironment(OutskirtsLightingProfile profile)
        {
            foreach (var sprite in GetComponentsInChildren<SpriteRenderer>(true))
                if (sprite.name == "ResourceSprite" || sprite.name == "HomeSprite")
                {
                    var tint = Color.white;
                    tint.a = sprite.color.a;
                    sprite.color = tint;
                }
            if (!transitioning) ApplyVegetationTint(profile.vegetationTint);
            referenceLight.color = profile.sunColor;
            referenceLight.intensity = profile.sunIntensity;
            referenceLight.transform.localRotation = profile.sunRotation;
            foreach (var effect in nightEffects) effect.SetActive(profile.lightmapScale >= 1);
            foreach (var effect in dayEffects) effect.SetActive(profile.lightmapScale < 1);
            foreach (var material in owned.Values)
            {
                material.SetVector("_ReferenceSun", profile.sunColor.linear * profile.sunIntensity);
                material.SetVector("_ReferenceAmbient", profile.ambientColor.linear);
                if (material.HasProperty("_ReferenceDiffuse"))
                    material.SetFloat("_ReferenceDiffuse", Mathf.Max(0, Vector3.Dot(Vector3.up, -referenceLight.transform.forward)));
            }
        }

        private void OnDestroy()
        {
            foreach (var material in owned.Values)
                if (Application.isPlaying) Destroy(material); else DestroyImmediate(material);
            vegetation.Clear();
            owned.Clear();
        }
    }
}
