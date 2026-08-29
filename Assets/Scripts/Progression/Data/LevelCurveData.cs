using UnityEngine;

namespace WebSwingEscape.Progression
{
    /// <summary>
    /// Designer-tunable level XP curve. "XP" here means the amount of accumulated
    /// Speed needed to advance one level. Hybrid polynomial &times; exponential so
    /// higher levels cost disproportionately more.
    /// Create via <c>Assets &rarr; Create &rarr; Web Swing Escape &rarr; Progression &rarr; Level Curve</c>.
    /// </summary>
    [CreateAssetMenu(fileName = "LevelCurveData",
        menuName = "Web Swing Escape/Progression/Level Curve")]
    public class LevelCurveData : ScriptableObject
    {
        [Tooltip("Level the player starts at and returns to after a rebirth.")]
        public int startingLevel = 1;

        [Tooltip("XP required to leave level 1, before the curve terms scale it.")]
        public double baseCost = 100d;

        [Tooltip("Polynomial exponent applied to the level number. 1 = linear, 2 = quadratic.")]
        public double polynomialExponent = 1.6d;

        [Tooltip("Exponential base compounded per level. 1 = pure polynomial; 1.05-1.20 is typical.")]
        public double exponentialBase = 1.07d;

        /// <summary>XP (accumulated Speed) needed to advance from <paramref name="level"/> to the next.</summary>
        public double XpRequiredForLevel(int level) =>
            ProgressionMath.XpToNextLevel(level, baseCost, polynomialExponent, exponentialBase);

        void OnValidate()
        {
            if (startingLevel < 1) startingLevel = 1;
            if (baseCost < 1d) baseCost = 1d;
            if (polynomialExponent < 0d) polynomialExponent = 0d;
            if (exponentialBase < 1d) exponentialBase = 1d;
        }
    }
}
