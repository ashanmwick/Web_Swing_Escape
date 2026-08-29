using System;
using UnityEngine;

namespace WebSwingEscape.Progression
{
    /// <summary>
    /// Core idle/incremental loop: Speed accumulation, leveling, and coin-purchased
    /// boosts. Pure data + events &mdash; no UI or visual code. Put this on a
    /// persistent manager object (e.g. alongside <see cref="GameManager"/>, which is
    /// already <c>DontDestroyOnLoad</c>) together with a <see cref="RebirthSystem"/>.
    ///
    /// The Rebirth multiplier is applied automatically inside <see cref="AddSpeed"/>;
    /// this class only knows about the <see cref="IRebirthMultiplierProvider"/>
    /// interface, never the concrete rebirth system.
    /// </summary>
    public class PlayerProgression : MonoBehaviour, ISaveable
    {
        [Header("Tunable data (optional — sensible fallbacks used if empty)")]
        [SerializeField] LevelCurveData levelCurve;
        [SerializeField] BoostCostData boostCosts;

        [Header("Rebirth link")]
        [Tooltip("Object implementing IRebirthMultiplierProvider (usually the RebirthSystem). " +
                 "Auto-resolved from this GameObject / scene if left empty.")]
        [SerializeField] MonoBehaviour rebirthProviderSource;

        // Hard guard so a mis-tuned curve (threshold near 0) can never hang the frame.
        const int MaxLevelUpsPerCall = 100000;

        double _speed;               // headline stat since last rebirth (monotonic up)
        double _levelXp;             // progress toward the next level (consumed on level-up)
        int _level = 1;
        double _coins;
        int _buyLevelPurchases;
        int _speedBoostPurchases;

        IRebirthMultiplierProvider _rebirthProvider;

        /// <summary>Total Speed accumulated since the last rebirth. Core stat / currency-like resource.</summary>
        public double Speed => _speed;

        /// <summary>Current level.</summary>
        public int Level => _level;

        /// <summary>Speed banked toward the next level (0 .. <see cref="XpForNextLevel"/>).</summary>
        public double CurrentLevelXp => _levelXp;

        /// <summary>Speed required to advance from the current level to the next.</summary>
        public double XpForNextLevel => GetXPRequiredForLevel(_level);

        /// <summary>Fraction of the way to the next level, 0..1.</summary>
        public double LevelProgress => XpForNextLevel > 0d ? Math.Min(1d, _levelXp / XpForNextLevel) : 0d;

        /// <summary>Spendable coin currency. Persists across rebirths.</summary>
        public double Coins => _coins;

        /// <summary>Current permanent Speed multiplier from rebirths (1 = none).</summary>
        public double RebirthMultiplier => _rebirthProvider != null ? _rebirthProvider.RebirthMultiplier : 1d;

        /// <summary>Level the player returns to after a rebirth.</summary>
        public int StartingLevel => levelCurve != null ? Mathf.Max(1, levelCurve.startingLevel) : 1;

        /// <summary>Raised after Speed changes, with the new <see cref="Speed"/> value.</summary>
        public event Action<double> OnSpeedChanged;

        /// <summary>Raised once per level gained, with the new <see cref="Level"/>.</summary>
        public event Action<int> OnLevelUp;

        /// <summary>Raised on any level change including resets / restores, with the new <see cref="Level"/>.</summary>
        public event Action<int> OnLevelChanged;

        /// <summary>Raised after <see cref="Coins"/> changes, with the new balance.</summary>
        public event Action<double> OnCoinsChanged;

        void Awake()
        {
            ResolveDependencies();
            _level = StartingLevel;
        }

        void ResolveDependencies()
        {
            if (rebirthProviderSource is IRebirthMultiplierProvider fromField)
            {
                _rebirthProvider = fromField;
                return;
            }

            var local = GetComponent<IRebirthMultiplierProvider>();
            if (local != null) { _rebirthProvider = local; return; }

            foreach (var mb in FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None))
            {
                if (mb is IRebirthMultiplierProvider provider)
                {
                    _rebirthProvider = provider;
                    return;
                }
            }
        }

        // ---- Core loop -------------------------------------------------------

        /// <summary>
        /// Adds Speed after applying the current Rebirth multiplier, then levels up
        /// as many times as the new total allows.
        /// </summary>
        /// <param name="amount">Raw Speed to add (pre-multiplier). Non-positive values are ignored.</param>
        public void AddSpeed(double amount)
        {
            if (amount <= 0d || double.IsNaN(amount) || double.IsInfinity(amount)) return;
            ApplyRawSpeed(amount * RebirthMultiplier);
        }

        /// <summary>
        /// Called every frame while the local player stands on a treadmill trigger
        /// volume. Feeds straight into <see cref="AddSpeed"/> (so the multiplier applies).
        /// </summary>
        /// <param name="deltaSpeed">Speed earned this frame (already scaled by <c>Time.deltaTime</c>).</param>
        public void OnTreadmillTick(float deltaSpeed) => AddSpeed(deltaSpeed);

        /// <summary>Speed (accumulated XP) required to go from <paramref name="level"/> to the next.</summary>
        public double GetXPRequiredForLevel(int level)
        {
            double value = levelCurve != null
                ? levelCurve.XpRequiredForLevel(level)
                : ProgressionMath.XpToNextLevel(level, 100d, 1.6d, 1.07d);
            return value < 1d ? 1d : value;
        }

        void ApplyRawSpeed(double gain)
        {
            _speed += gain;
            _levelXp += gain;
            ResolveLevelUps();
            OnSpeedChanged?.Invoke(_speed);
        }

        void ResolveLevelUps()
        {
            int gained = 0;
            double need = GetXPRequiredForLevel(_level);
            while (_levelXp >= need && gained < MaxLevelUpsPerCall)
            {
                _levelXp -= need;
                _level++;
                gained++;
                OnLevelUp?.Invoke(_level);
                need = GetXPRequiredForLevel(_level);
            }

            if (gained > 0) OnLevelChanged?.Invoke(_level);
        }

        // ---- Coin economy -------------------------------------------------------

        /// <summary>Adds coins and raises <see cref="OnCoinsChanged"/>. Non-positive values are ignored.</summary>
        public void AddCoins(double amount)
        {
            if (amount <= 0d || double.IsNaN(amount)) return;
            _coins += amount;
            OnCoinsChanged?.Invoke(_coins);
        }

        /// <summary>Overwrites the coin balance (clamped at 0) and raises <see cref="OnCoinsChanged"/>.</summary>
        public void SetCoins(double value)
        {
            _coins = value < 0d || double.IsNaN(value) ? 0d : value;
            OnCoinsChanged?.Invoke(_coins);
        }

        /// <summary>
        /// Spends coins if the balance covers <paramref name="amount"/>.
        /// </summary>
        /// <returns><c>true</c> and deducts on success; <c>false</c> and no change otherwise.</returns>
        public bool TrySpendCoins(double amount)
        {
            if (amount < 0d || double.IsNaN(amount) || _coins < amount) return false;
            _coins -= amount;
            OnCoinsChanged?.Invoke(_coins);
            return true;
        }

        // ---- Coin-purchased boosts (HUD buttons) ------------------------------

        /// <summary>Coin cost of the next <see cref="BuyLevel"/> purchase.</summary>
        public double GetBuyLevelCost() =>
            boostCosts != null
                ? boostCosts.BuyLevelCost(_buyLevelPurchases, _level)
                : ProgressionMath.ScaledBoostCost(250d, 1.15d, _buyLevelPurchases, _level, 0.05d);

        /// <summary>Coin cost of the next flat Speed boost of <paramref name="flatAmount"/>.</summary>
        public double GetSpeedBoostCost(double flatAmount) =>
            boostCosts != null
                ? boostCosts.SpeedBoostCost(flatAmount, _speedBoostPurchases, _level)
                : ProgressionMath.ScaledBoostCost(100d + 0.01d * Math.Max(0d, flatAmount),
                                                  1.12d, _speedBoostPurchases, _level, 0.05d);

        /// <summary>
        /// Spends coins to gain exactly one level's worth of Speed (guaranteeing a level-up).
        /// The granted Speed bypasses the Rebirth multiplier &mdash; it is a fixed purchase.
        /// </summary>
        /// <returns><c>true</c> if the purchase succeeded.</returns>
        public bool BuyLevel()
        {
            if (!TrySpendCoins(GetBuyLevelCost())) return false;
            _buyLevelPurchases++;
            double need = Math.Max(0d, GetXPRequiredForLevel(_level) - _levelXp);
            ApplyRawSpeed(need);
            return true;
        }

        /// <summary>
        /// Spends coins to add a fixed amount of Speed (for buttons like "+10K Speed").
        /// The granted Speed bypasses the Rebirth multiplier &mdash; it is a fixed purchase.
        /// </summary>
        /// <param name="flatAmount">Speed to grant. Must be positive.</param>
        /// <returns><c>true</c> if the purchase succeeded.</returns>
        public bool BuySpeedBoost(double flatAmount)
        {
            if (flatAmount <= 0d || double.IsNaN(flatAmount)) return false;
            if (!TrySpendCoins(GetSpeedBoostCost(flatAmount))) return false;
            _speedBoostPurchases++;
            ApplyRawSpeed(flatAmount);
            return true;
        }

        // ---- Rebirth hook ---------------------------------------------------

        /// <summary>
        /// Resets Speed and Level to their starting values. Called by
        /// <see cref="RebirthSystem.PerformRebirth"/>. Does NOT touch coins.
        /// </summary>
        public void ResetForRebirth()
        {
            _speed = 0d;
            _levelXp = 0d;
            _level = StartingLevel;
            _buyLevelPurchases = 0;
            _speedBoostPurchases = 0;

            OnSpeedChanged?.Invoke(_speed);
            OnLevelChanged?.Invoke(_level);
        }

        // ---- Editor diagnostics ----------------------------------------------

        [ContextMenu("DEBUG: Add 100,000 Speed")]
        void DebugAddSpeed() => AddSpeed(100_000d);

        [ContextMenu("DEBUG: Force +5 Levels")]
        void DebugForceLevels()
        {
            for (int i = 0; i < 5; i++)
            {
                double need = Math.Max(1d, GetXPRequiredForLevel(_level) - _levelXp);
                ApplyRawSpeed(need);
            }
        }

        [ContextMenu("DEBUG: Add 1,000,000 Coins")]
        void DebugAddCoins() => AddCoins(1_000_000d);

        // ---- Save / load --------------------------------------------------------

        /// <summary>Serialisable snapshot of <see cref="PlayerProgression"/>.</summary>
        [Serializable]
        public class ProgressionSave
        {
            public double speed;
            public double levelXp;
            public int level;
            public double coins;
            public int buyLevelPurchases;
            public int speedBoostPurchases;
        }

        /// <inheritdoc/>
        public string SaveKey => "player.progression";

        /// <inheritdoc/>
        public object CaptureState() => new ProgressionSave
        {
            speed = _speed,
            levelXp = _levelXp,
            level = _level,
            coins = _coins,
            buyLevelPurchases = _buyLevelPurchases,
            speedBoostPurchases = _speedBoostPurchases,
        };

        /// <inheritdoc/>
        public void RestoreState(object state)
        {
            if (state is not ProgressionSave s) return;

            _speed = Math.Max(0d, s.speed);
            _levelXp = Math.Max(0d, s.levelXp);
            _level = Mathf.Max(StartingLevel, s.level);
            _coins = Math.Max(0d, s.coins);
            _buyLevelPurchases = Mathf.Max(0, s.buyLevelPurchases);
            _speedBoostPurchases = Mathf.Max(0, s.speedBoostPurchases);

            OnSpeedChanged?.Invoke(_speed);
            OnLevelChanged?.Invoke(_level);
            OnCoinsChanged?.Invoke(_coins);
        }
    }
}
