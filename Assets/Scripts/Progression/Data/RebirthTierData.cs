using UnityEngine;

namespace WebSwingEscape.Progression
{
    /// <summary>
    /// Designer-tunable rebirth tier table plus the rebirth-gate formula.
    /// Supports an arbitrary number of tiers, non-linear thresholds and non-linear
    /// multipliers. The highest tier whose <see cref="Tier.minRebirthCount"/> the
    /// player has reached wins.
    /// Create via <c>Assets &rarr; Create &rarr; Web Swing Escape &rarr; Progression &rarr; Rebirth Tiers</c>.
    /// </summary>
    [CreateAssetMenu(fileName = "RebirthTierData",
        menuName = "Web Swing Escape/Progression/Rebirth Tiers")]
    public class RebirthTierData : ScriptableObject
    {
        /// <summary>One row of the rebirth tier table.</summary>
        [System.Serializable]
        public struct Tier
        {
            [Tooltip("Minimum RebirthCount for this tier to apply.")]
            public int minRebirthCount;

            [Tooltip("Permanent Speed multiplier granted while this tier is the highest reached.")]
            public float multiplier;
        }

        [Tooltip("Tiers in any order. Highest matched minRebirthCount wins; " +
                 "RebirthCount below every entry falls back to 1.0x.")]
        public Tier[] tiers =
        {
            new Tier { minRebirthCount = 1, multiplier = 1.5f },
            new Tier { minRebirthCount = 3, multiplier = 2.0f },
            new Tier { minRebirthCount = 4, multiplier = 3.0f },
        };

        [Header("Rebirth gate")]
        [Tooltip("Level required for the very first rebirth.")]
        public int baseRequiredLevel = 25;

        [Tooltip("Exponent in RequiredLevel = baseRequiredLevel * (rebirthCount + 1)^growthFactor.")]
        public double rebirthGrowthFactor = 1.25d;

        /// <summary>Speed multiplier for a given rebirth count. Returns 1.0 below the first tier.</summary>
        public double MultiplierForRebirthCount(int rebirthCount)
        {
            double best = 1.0d;
            int bestThreshold = int.MinValue;

            if (tiers != null)
            {
                foreach (Tier t in tiers)
                {
                    if (rebirthCount >= t.minRebirthCount && t.minRebirthCount > bestThreshold)
                    {
                        bestThreshold = t.minRebirthCount;
                        best = t.multiplier;
                    }
                }
            }

            return best <= 0d ? 1.0d : best;
        }

        /// <summary>Level the player must reach before the rebirth after <paramref name="currentRebirthCount"/>.</summary>
        public int RequiredLevelForNextRebirth(int currentRebirthCount) =>
            ProgressionMath.RequiredLevelForRebirth(baseRequiredLevel, currentRebirthCount, rebirthGrowthFactor);

        void OnValidate()
        {
            if (baseRequiredLevel < 1) baseRequiredLevel = 1;
            if (tiers == null) return;
            for (int i = 0; i < tiers.Length; i++)
            {
                if (tiers[i].minRebirthCount < 0) tiers[i].minRebirthCount = 0;
                if (tiers[i].multiplier < 0f) tiers[i].multiplier = 0f;
            }
        }
    }
}
