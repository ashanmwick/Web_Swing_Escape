using UnityEngine;

namespace WebSwingEscape.Progression
{
    /// <summary>
    /// Designer-tunable mapping from progression (Level + Rebirth multiplier) to the
    /// character's real walking / running speed multiplier. Consumed by
    /// <c>LocomotionSpeedBinder</c>.
    /// Create via <c>Assets &rarr; Create &rarr; Web Swing Escape &rarr; Progression &rarr; Locomotion Scaling</c>.
    /// </summary>
    [CreateAssetMenu(fileName = "LocomotionScalingData",
        menuName = "Web Swing Escape/Progression/Locomotion Scaling")]
    public class LocomotionScalingData : ScriptableObject
    {
        [Tooltip("Fractional walk/run speed added per level above 1. 0.03 = +3% per level.")]
        public double perLevelBonus = 0.03d;

        [Tooltip("Hard cap on the total locomotion multiplier (level term * rebirth multiplier). " +
                 "Below 1 disables the cap.")]
        public double maxMultiplier = 4d;

        /// <summary>Locomotion speed multiplier for the given level and rebirth multiplier.</summary>
        public double Multiplier(int level, double rebirthMultiplier) =>
            ProgressionMath.LocomotionMultiplier(level, rebirthMultiplier, perLevelBonus, maxMultiplier);

        void OnValidate()
        {
            if (perLevelBonus < 0d) perLevelBonus = 0d;
            if (maxMultiplier < 1d && maxMultiplier != 0d) maxMultiplier = 1d;
        }
    }
}
