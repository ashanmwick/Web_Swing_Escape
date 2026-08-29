using System;

namespace WebSwingEscape.Progression
{
    /// <summary>
    /// Read-only view of the permanent Speed multiplier granted by rebirths.
    /// <see cref="PlayerProgression"/> depends on this interface instead of the
    /// concrete <see cref="RebirthSystem"/> so progression and rebirth stay
    /// decoupled and communicate through a clean seam.
    /// </summary>
    public interface IRebirthMultiplierProvider
    {
        /// <summary>Current multiplier applied to every Speed gain. 1.0 = no bonus.</summary>
        double RebirthMultiplier { get; }

        /// <summary>Raised whenever <see cref="RebirthMultiplier"/> changes.</summary>
        event Action<double> OnRebirthMultiplierChanged;
    }
}
