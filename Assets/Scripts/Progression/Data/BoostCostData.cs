using UnityEngine;

namespace WebSwingEscape.Progression
{
    /// <summary>
    /// Designer-tunable pricing for the coin-purchased boosts wired to the HUD
    /// buttons ("Buy Level", "+10K Speed", "+100K Speed", "+1M Speed"). Costs scale
    /// with how many times a boost has been bought and with the player's level, so
    /// nothing is hard-coded to a fixed price.
    /// Create via <c>Assets &rarr; Create &rarr; Web Swing Escape &rarr; Progression &rarr; Boost Costs</c>.
    /// </summary>
    [CreateAssetMenu(fileName = "BoostCostData",
        menuName = "Web Swing Escape/Progression/Boost Costs")]
    public class BoostCostData : ScriptableObject
    {
        [Header("Buy Level button")]
        [Tooltip("Coin cost of the first BuyLevel purchase at level 0.")]
        public double buyLevelBaseCost = 250d;
        [Tooltip("Cost multiplier applied per previous BuyLevel purchase.")]
        public double buyLevelCostGrowth = 1.15d;

        [Header("Flat Speed boost buttons (+10K / +100K / +1M ...)")]
        [Tooltip("Fixed coin cost added on top of the per-unit cost for any Speed boost.")]
        public double speedBoostBaseCost = 100d;
        [Tooltip("Coin cost per 1 unit of flat Speed granted, before growth / level scaling.")]
        public double speedBoostCostPerUnit = 0.01d;
        [Tooltip("Cost multiplier applied per previous Speed-boost purchase.")]
        public double speedBoostCostGrowth = 1.12d;

        [Header("Shared scaling")]
        [Tooltip("How strongly the player's current level inflates every boost cost.")]
        public double levelWeight = 0.05d;

        /// <summary>Coin cost of the next "Buy Level" purchase.</summary>
        /// <param name="timesPurchased">How many times BuyLevel has already been bought.</param>
        /// <param name="currentLevel">Player's current level.</param>
        public double BuyLevelCost(int timesPurchased, int currentLevel) =>
            ProgressionMath.ScaledBoostCost(buyLevelBaseCost, buyLevelCostGrowth,
                                            timesPurchased, currentLevel, levelWeight);

        /// <summary>Coin cost of the next flat Speed boost of <paramref name="flatAmount"/>.</summary>
        /// <param name="flatAmount">Amount of Speed the boost grants.</param>
        /// <param name="timesPurchased">How many Speed boosts have already been bought.</param>
        /// <param name="currentLevel">Player's current level.</param>
        public double SpeedBoostCost(double flatAmount, int timesPurchased, int currentLevel)
        {
            if (flatAmount < 0d) flatAmount = 0d;
            double baseCost = speedBoostBaseCost + speedBoostCostPerUnit * flatAmount;
            return ProgressionMath.ScaledBoostCost(baseCost, speedBoostCostGrowth,
                                                   timesPurchased, currentLevel, levelWeight);
        }

        void OnValidate()
        {
            if (buyLevelBaseCost < 0d) buyLevelBaseCost = 0d;
            if (buyLevelCostGrowth < 1d) buyLevelCostGrowth = 1d;
            if (speedBoostBaseCost < 0d) speedBoostBaseCost = 0d;
            if (speedBoostCostPerUnit < 0d) speedBoostCostPerUnit = 0d;
            if (speedBoostCostGrowth < 1d) speedBoostCostGrowth = 1d;
            if (levelWeight < 0d) levelWeight = 0d;
        }
    }
}
