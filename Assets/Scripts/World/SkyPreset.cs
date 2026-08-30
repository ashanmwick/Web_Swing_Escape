using UnityEngine;

namespace WebSwingEscape.World
{
    /// <summary>
    /// Designer-tunable sky settings. Applied by <see cref="SkyController"/> to a
    /// runtime copy of the active <c>Skybox/Procedural</c> material.
    /// Create via <c>Assets &rarr; Create &rarr; Web Swing Escape &rarr; World &rarr; Sky Preset</c>.
    /// </summary>
    [CreateAssetMenu(fileName = "SkyPreset",
        menuName = "Web Swing Escape/World/Sky Preset")]
    public class SkyPreset : ScriptableObject
    {
        [Header("Skybox/Procedural")]
        [ColorUsage(false, true)] public Color skyTint = new(0.5f, 0.5f, 0.5f);
        [ColorUsage(false, true)] public Color groundColor = new(0.369f, 0.349f, 0.341f);
        [Range(0f, 5f)]  public float atmosphereThickness = 1f;
        [Range(0f, 8f)]  public float exposure = 1.3f;
        [Range(0f, 1f)]  public float sunSize = 0.04f;
        [Range(1f, 10f)] public float sunSizeConvergence = 5f;

        [Header("Optional: drive the directional (sun) light")]
        public bool controlSun = true;
        [Tooltip("x = pitch (elevation), y = yaw (compass direction), in degrees.")]
        public Vector2 sunAngles = new(50f, -30f);
        [ColorUsage(false, true)] public Color sunColor = Color.white;
        [Range(0f, 8f)] public float sunIntensity = 1f;

        void OnValidate()
        {
            if (atmosphereThickness < 0f) atmosphereThickness = 0f;
            if (exposure < 0f) exposure = 0f;
            if (sunIntensity < 0f) sunIntensity = 0f;
        }
    }
}
