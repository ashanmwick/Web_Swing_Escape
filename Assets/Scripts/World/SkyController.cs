using UnityEngine;

namespace WebSwingEscape.World
{
    /// <summary>
    /// Applies a <see cref="SkyPreset"/> to a runtime copy of the skybox material
    /// and keeps ambient lighting in sync. Add one per multiplayer scene on an
    /// empty GameObject (e.g. <c>~Sky</c>). Call <see cref="Apply(SkyPreset)"/> to
    /// switch skies live (settings menu, time-of-day, zone change).
    ///
    /// URP has no Volume-based sky, so the sky is just <c>RenderSettings.skybox</c>.
    /// The source material is cloned so the shared asset is never dirtied.
    /// Purely cosmetic and client-side &mdash; nothing in the net layer depends on it.
    /// </summary>
    [ExecuteAlways]
    public class SkyController : MonoBehaviour
    {
        [Tooltip("Skybox/Procedural material to copy from. Leave null to clone RenderSettings.skybox.")]
        [SerializeField] Material skyboxSource;
        [SerializeField] SkyPreset preset;
        [Tooltip("Scene directional light to treat as the sun (optional).")]
        [SerializeField] Light sun;
        [Tooltip("Re-bake the ambient probe after each apply so world lighting follows the sky.")]
        [SerializeField] bool refreshAmbient = true;

        Material _runtime;

        static readonly int SkyTintId          = Shader.PropertyToID("_SkyTint");
        static readonly int GroundColorId      = Shader.PropertyToID("_GroundColor");
        static readonly int AtmosThicknessId   = Shader.PropertyToID("_AtmosphereThickness");
        static readonly int ExposureId         = Shader.PropertyToID("_Exposure");
        static readonly int SunSizeId          = Shader.PropertyToID("_SunSize");
        static readonly int SunSizeConvergeId  = Shader.PropertyToID("_SunSizeConvergence");

        /// <summary>The currently applied preset (read-only).</summary>
        public SkyPreset Preset => preset;

        void OnEnable() => Apply(preset);

        void OnValidate()
        {
            if (isActiveAndEnabled) Apply(preset);
        }

        void OnDestroy()
        {
            if (_runtime == null) return;
            if (Application.isPlaying) Destroy(_runtime);
            else DestroyImmediate(_runtime);
        }

        /// <summary>Switch to <paramref name="next"/> and push all its values to the sky.</summary>
        public void Apply(SkyPreset next)
        {
            preset = next;
            if (preset == null) return;

            var src = skyboxSource != null ? skyboxSource : RenderSettings.skybox;
            if (src == null)
            {
                Debug.LogWarning("SkyController: no skybox material to drive.", this);
                return;
            }

            if (_runtime == null || _runtime.shader != src.shader)
            {
                _runtime = new Material(src)
                {
                    name = src.name + " (runtime)",
                    hideFlags = HideFlags.DontSave
                };
            }

            _runtime.SetColor(SkyTintId, preset.skyTint);
            _runtime.SetColor(GroundColorId, preset.groundColor);
            _runtime.SetFloat(AtmosThicknessId, preset.atmosphereThickness);
            _runtime.SetFloat(ExposureId, preset.exposure);
            _runtime.SetFloat(SunSizeId, preset.sunSize);
            _runtime.SetFloat(SunSizeConvergeId, preset.sunSizeConvergence);

            RenderSettings.skybox = _runtime;

            if (preset.controlSun && sun != null)
            {
                sun.transform.rotation = Quaternion.Euler(preset.sunAngles.x, preset.sunAngles.y, 0f);
                sun.color = preset.sunColor;
                sun.intensity = preset.sunIntensity;
            }

            if (refreshAmbient) DynamicGI.UpdateEnvironment();
        }

        // --- Per-property live setters (for sliders in a settings menu) ------------
        // These mutate the runtime material only; call RefreshAmbient() once the
        // user stops dragging to avoid re-baking the probe every frame.

        public void SetExposure(float value)
        {
            EnsureRuntime();
            _runtime?.SetFloat(ExposureId, Mathf.Max(0f, value));
        }

        public void SetAtmosphereThickness(float value)
        {
            EnsureRuntime();
            _runtime?.SetFloat(AtmosThicknessId, Mathf.Clamp(value, 0f, 5f));
        }

        public void SetSkyTint(Color value)
        {
            EnsureRuntime();
            _runtime?.SetColor(SkyTintId, value);
        }

        public void SetGroundColor(Color value)
        {
            EnsureRuntime();
            _runtime?.SetColor(GroundColorId, value);
        }

        /// <summary>Call after a batch of live setter changes (e.g. slider release).</summary>
        public void RefreshAmbient()
        {
            if (_runtime != null) DynamicGI.UpdateEnvironment();
        }

        void EnsureRuntime()
        {
            if (_runtime != null) return;
            var src = skyboxSource != null ? skyboxSource : RenderSettings.skybox;
            if (src == null) return;
            _runtime = new Material(src) { name = src.name + " (runtime)", hideFlags = HideFlags.DontSave };
            RenderSettings.skybox = _runtime;
        }
    }
}
