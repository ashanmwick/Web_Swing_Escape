using System;

namespace WebSwingEscape.Progression
{
    /// <summary>
    /// Pure, Unity-free math for the progression systems. Kept separate from the
    /// <see cref="UnityEngine.MonoBehaviour"/> wrappers so the formulas can be
    /// unit-tested in isolation and tuned without touching gameplay glue.
    /// All Speed / cost values are <see cref="double"/> to survive very large numbers.
    /// </summary>
    public static class ProgressionMath
    {
        /// <summary>
        /// XP (accumulated Speed) required to advance from <paramref name="level"/> to the next.
        /// Hybrid curve: <c>baseCost * level^polyExponent * expBase^(level - 1)</c>.
        /// </summary>
        /// <param name="level">Current level (clamped to a minimum of 1).</param>
        /// <param name="baseCost">Cost to leave level 1 before the scaling terms.</param>
        /// <param name="polyExponent">Polynomial exponent on the level number.</param>
        /// <param name="expBase">Exponential base compounded once per level.</param>
        public static double XpToNextLevel(int level, double baseCost, double polyExponent, double expBase)
        {
            if (level < 1) level = 1;
            if (baseCost < 0d) baseCost = 0d;
            if (expBase < 1d) expBase = 1d;

            double poly = Math.Pow(level, polyExponent);
            double exp = Math.Pow(expBase, level - 1);
            double result = baseCost * poly * exp;
            return result < 1d ? 1d : result;
        }

        /// <summary>
        /// Scaled cost of a coin-purchased boost.
        /// <c>baseCost * costGrowth^purchaseIndex * (1 + levelWeight * currentLevel)</c>
        /// so boosts get more expensive both with repeat buys and with progression.
        /// </summary>
        /// <param name="baseCost">Cost of the first purchase at level 0.</param>
        /// <param name="costGrowth">Multiplier applied per previous purchase (&gt;= 1).</param>
        /// <param name="purchaseIndex">How many times this boost has already been bought.</param>
        /// <param name="currentLevel">Player's current level, used for the level term.</param>
        /// <param name="levelWeight">How strongly the current level inflates the cost.</param>
        public static double ScaledBoostCost(double baseCost, double costGrowth, int purchaseIndex,
                                             int currentLevel, double levelWeight)
        {
            if (baseCost < 0d) baseCost = 0d;
            if (costGrowth < 1d) costGrowth = 1d;
            if (purchaseIndex < 0) purchaseIndex = 0;
            if (currentLevel < 0) currentLevel = 0;
            if (levelWeight < 0d) levelWeight = 0d;

            double growth = Math.Pow(costGrowth, purchaseIndex);
            double levelScale = 1d + levelWeight * currentLevel;
            return baseCost * growth * levelScale;
        }

        /// <summary>
        /// Multiplier applied to the character's real walking / running speed:
        /// <c>clamp((1 + perLevelBonus * (level - 1)) * rebirthMultiplier, 1 .. maxMultiplier)</c>.
        /// Level 1 with no rebirths returns exactly 1.
        /// </summary>
        /// <param name="level">Current level (clamped to a minimum of 1).</param>
        /// <param name="rebirthMultiplier">Permanent rebirth multiplier (clamped to a minimum of 1).</param>
        /// <param name="perLevelBonus">Fractional locomotion bonus per level above 1 (e.g. 0.03 = +3%/level).</param>
        /// <param name="maxMultiplier">Hard cap on the result. Values below 1 disable the cap.</param>
        public static double LocomotionMultiplier(int level, double rebirthMultiplier,
                                                  double perLevelBonus, double maxMultiplier)
        {
            if (level < 1) level = 1;
            if (perLevelBonus < 0d) perLevelBonus = 0d;
            if (rebirthMultiplier < 1d) rebirthMultiplier = 1d;

            double m = (1d + perLevelBonus * (level - 1)) * rebirthMultiplier;
            if (m < 1d) m = 1d;
            if (maxMultiplier >= 1d && m > maxMultiplier) m = maxMultiplier;
            return m;
        }

        /// <summary>
        /// Level the player must reach before the next rebirth is allowed:
        /// <c>ceil(baseLevel * (rebirthCount + 1)^growthFactor)</c>.
        /// </summary>
        /// <param name="baseLevel">Level required for the very first rebirth.</param>
        /// <param name="rebirthCount">Rebirths already performed.</param>
        /// <param name="growthFactor">Exponent controlling how fast the gate rises.</param>
        public static int RequiredLevelForRebirth(int baseLevel, int rebirthCount, double growthFactor)
        {
            if (baseLevel < 1) baseLevel = 1;
            if (rebirthCount < 0) rebirthCount = 0;

            double required = baseLevel * Math.Pow(rebirthCount + 1, growthFactor);
            double ceil = Math.Ceiling(required);
            if (ceil < baseLevel) ceil = baseLevel;
            if (ceil > int.MaxValue) return int.MaxValue;
            return (int)ceil;
        }
    }
}
