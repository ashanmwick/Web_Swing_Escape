using UnityEngine;
using WebSwingEscape.Progression;

/// <summary>
/// <c>DontDestroyOnLoad</c> global-state singleton and composition root for the
/// progression systems. When a <see cref="PlayerProgression"/> / <see cref="RebirthSystem"/>
/// is wired (or sits on this same GameObject), it becomes the single source of
/// truth and the legacy <see cref="coins"/> / <see cref="speedMultiplier"/> /
/// <see cref="rebirthCount"/> members simply mirror it.
/// </summary>
public class GameManager : MonoBehaviour
{
    public static GameManager Instance;

    [Header("Progression systems (optional; auto-found on this GameObject)")]
    [SerializeField] PlayerProgression progression;
    [SerializeField] RebirthSystem rebirth;

    [SerializeField, Tooltip("Coins granted on first run, and the store used until a PlayerProgression exists.")]
    double startingCoins;

    /// <summary>The progression system, if one is present.</summary>
    public PlayerProgression Progression => progression;

    /// <summary>The rebirth system, if one is present.</summary>
    public RebirthSystem Rebirth => rebirth;

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);

        if (progression == null) progression = GetComponent<PlayerProgression>();
        if (rebirth == null) rebirth = GetComponent<RebirthSystem>();

        if (progression != null && progression.Coins <= 0d && startingCoins > 0d)
            progression.SetCoins(startingCoins);
    }

    /// <summary>Spendable coin currency. Backed by <see cref="PlayerProgression"/> when one is wired.</summary>
    public double coins
    {
        get => progression != null ? progression.Coins : startingCoins;
        set
        {
            if (progression != null) progression.SetCoins(value);
            else startingCoins = value;
        }
    }

    /// <summary>Permanent Speed multiplier from rebirths (1 = none). Read-only mirror of <see cref="RebirthSystem"/>.</summary>
    public float speedMultiplier => rebirth != null ? (float)rebirth.RebirthMultiplier : 1f;

    /// <summary>Rebirths performed. Read-only mirror of <see cref="RebirthSystem"/>.</summary>
    public int rebirthCount => rebirth != null ? rebirth.RebirthCount : 0;
}
