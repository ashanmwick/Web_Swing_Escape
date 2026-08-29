namespace WebSwingEscape.Progression
{
    /// <summary>
    /// Save/load hook. A save backend (PlayerPrefs, JSON file, cloud save) can walk
    /// every <see cref="ISaveable"/>, call <see cref="CaptureState"/> to serialise,
    /// and <see cref="RestoreState"/> to reapply. No backend is implemented here on
    /// purpose &mdash; this is only the contract other systems plug into later.
    /// </summary>
    public interface ISaveable
    {
        /// <summary>Stable, unique key the backend files this object's blob under.</summary>
        string SaveKey { get; }

        /// <summary>
        /// Returns a plain, serialisable snapshot of this object's state
        /// (a <c>[System.Serializable]</c> data class / struct).
        /// </summary>
        object CaptureState();

        /// <summary>
        /// Reapplies a snapshot previously produced by <see cref="CaptureState"/>.
        /// Implementations must ignore a payload of an unexpected type.
        /// </summary>
        void RestoreState(object state);
    }
}
