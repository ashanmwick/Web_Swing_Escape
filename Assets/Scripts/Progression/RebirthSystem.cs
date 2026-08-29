using System;
using UnityEngine;

namespace WebSwingEscape.Progression
{
    /// <summary>
    /// Rebirth loop: spend accumulated progress for a permanent Speed multiplier.
    /// Decoupled from <see cref="PlayerProgression"/> &mdash; it exposes
    /// <see cref="IRebirthMultiplierProvider"/> (which progression reads) and calls
    /// back into progression only through the public <see cref="PlayerProgression.ResetForRebirth"/>
    /// hook. Put this on the same persistent object as <see cref="PlayerProgression"/>.
    ///
    /// Coins, gems and cosmetic unlocks are deliberately left untouched by a rebirth.
    /// </summary>
    public class RebirthSystem : MonoBehaviour, IRebirthMultiplierProvider, ISaveable
    {
        [Header("Tunable data")]
        [SerializeField] RebirthTierData tierData;

        [Header("Links")]
        [Tooltip("Progression system this rebirth resets. Auto-resolved from this GameObject / scene if empty.")]
        [SerializeField] PlayerProgression progression;

        int _rebirthCount;
        double _multiplier = 1.0d;

        /// <summary>Number of rebirths performed.</summary>
        public int RebirthCount => _rebirthCount;

        /// <inheritdoc/>
        public double RebirthMultiplier => _multiplier;

        /// <inheritdoc/>
        public event Action<double> OnRebirthMultiplierChanged;

        /// <summary>Raised after a successful rebirth, with the new <see cref="RebirthCount"/>.</summary>
        public event Action<int> OnRebirth;

        void Awake()
        {
            if (progression == null) progression = GetComponent<PlayerProgression>();
            if (progression == null) progression = FindFirstObjectByType<PlayerProgression>();
            RecalculateMultiplier();
        }

        /// <summary>Level the player must reach before the next rebirth is allowed.</summary>
        public int RequiredLevelForNextRebirth() =>
            tierData != null ? tierData.RequiredLevelForNextRebirth(_rebirthCount) : int.MaxValue;

        /// <summary>True when the player's current level meets the next rebirth threshold.</summary>
        public bool CanRebirth()
        {
            if (progression == null) return false;
            return progression.Level >= RequiredLevelForNextRebirth();
        }

        /// <summary>
        /// Performs a rebirth if <see cref="CanRebirth"/>: increments the count,
        /// recalculates the multiplier from the tier table, resets Speed/Level via
        /// <see cref="PlayerProgression.ResetForRebirth"/> (coins/gems/cosmetics kept),
        /// then raises <see cref="OnRebirth"/>.
        /// </summary>
        /// <returns><c>true</c> if the rebirth happened.</returns>
        public bool PerformRebirth()
        {
            if (!CanRebirth()) return false;

            _rebirthCount++;
            RecalculateMultiplier();
            progression.ResetForRebirth();
            OnRebirth?.Invoke(_rebirthCount);
            return true;
        }

        void RecalculateMultiplier()
        {
            double next = tierData != null ? tierData.MultiplierForRebirthCount(_rebirthCount) : 1.0d;
            if (next <= 0d) next = 1.0d;

            bool changed = Math.Abs(next - _multiplier) > double.Epsilon;
            _multiplier = next;
            if (changed) OnRebirthMultiplierChanged?.Invoke(_multiplier);
        }

        // ---- Save / load --------------------------------------------------------

        /// <summary>Serialisable snapshot of <see cref="RebirthSystem"/>.</summary>
        [Serializable]
        public class RebirthSave
        {
            public int rebirthCount;
        }

        /// <inheritdoc/>
        public string SaveKey => "player.rebirth";

        /// <inheritdoc/>
        public object CaptureState() => new RebirthSave { rebirthCount = _rebirthCount };

        /// <inheritdoc/>
        public void RestoreState(object state)
        {
            if (state is not RebirthSave s) return;
            _rebirthCount = Mathf.Max(0, s.rebirthCount);
            RecalculateMultiplier();
        }
    }
}
