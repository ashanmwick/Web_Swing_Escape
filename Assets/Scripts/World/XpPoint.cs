using TMPro;
using UnityEngine;
using HeroCharacter;
using WebSwingEscape.Progression;

/// <summary>
/// A world point that grants a tunable amount of coins when the <c>Player</c>-tagged
/// object touches it, then sends the player back to the scene's normal respawn
/// position. The point itself is left untouched — it is never destroyed,
/// deactivated or moved, so it can be hit again after the player respawns.
///
/// The amount is customizable in the Inspector via <see cref="coinAmount"/>, and
/// that same number is pushed onto a <see cref="TMP_Text"/> found in this
/// object's children so the floating label always matches the reward.
///
/// Put this on the point root together with a trigger <see cref="Collider"/>, and
/// parent a TextMeshPro label (e.g. under a <see cref="Billboard"/>) beneath it.
/// <c>[ExecuteAlways]</c> keeps the label in sync while editing.
///
/// Respawn is driven the same way <see cref="WaterDeath"/> does it: a single
/// lethal, unblockable <see cref="DamageType.True"/> hit through the
/// <see cref="HeroCharacterController"/>, so the controller's normal
/// death → respawn flow (and <see cref="RespawnAnimatorReset"/>) runs unmodified
/// and drops the player at the scene spawn point.
/// </summary>
[RequireComponent(typeof(Collider))]
[ExecuteAlways]
public class XpPoint : MonoBehaviour
{
    [Header("Reward")]
    [Tooltip("Coins this point produces when collected. Editable per instance; " +
             "the child label is updated to match automatically.")]
    [Min(0)]
    [SerializeField] int coinAmount = 100;

    [Header("Label")]
    [Tooltip("Child TextMeshPro label that shows the amount. Auto-found in children if left empty.")]
    [SerializeField] TMP_Text amountLabel;

    [Tooltip("Format for the label. {0} is replaced with the coin amount (e.g. \"+{0}\").")]
    [SerializeField] string labelFormat = "+{0}";

    [Header("Pickup")]
    [Tooltip("Tag of the local player object.")]
    [SerializeField] string playerTag = "Player";

    [Tooltip("Target progression system. Auto-found in the scene if left empty.")]
    [SerializeField] PlayerProgression progression;

    [Tooltip("Seconds to ignore further hits after one fires, so the respawn teleport " +
             "through the trigger can't re-trigger it.")]
    [SerializeField] float retriggerCooldown = 1.5f;

    /// <summary>Coins this point produces when collected.</summary>
    public int CoinAmount
    {
        get => coinAmount;
        set
        {
            coinAmount = Mathf.Max(0, value);
            RefreshLabel();
        }
    }

    float _readyTime;

    void Reset()
    {
        var col = GetComponent<Collider>();
        if (col != null) col.isTrigger = true;
        amountLabel = GetComponentInChildren<TMP_Text>(true);
        RefreshLabel();
    }

    void OnValidate()
    {
        coinAmount = Mathf.Max(0, coinAmount);
        RefreshLabel();
    }

    void Awake()
    {
        if (progression == null) progression = FindFirstObjectByType<PlayerProgression>();
        RefreshLabel();
    }

    /// <summary>Push <see cref="coinAmount"/> onto the child label.</summary>
    void RefreshLabel()
    {
        if (amountLabel == null) amountLabel = GetComponentInChildren<TMP_Text>(true);
        if (amountLabel == null) return;

        string text = string.IsNullOrEmpty(labelFormat)
            ? coinAmount.ToString()
            : string.Format(labelFormat, coinAmount);

        if (amountLabel.text != text) amountLabel.text = text;
    }

    void OnTriggerEnter(Collider other)
    {
        if (!Application.isPlaying || Time.time < _readyTime) return;
        if (!other.CompareTag(playerTag)) return;

        _readyTime = Time.time + Mathf.Max(0f, retriggerCooldown);

        if (coinAmount > 0)
        {
            if (progression == null) progression = FindFirstObjectByType<PlayerProgression>();
            if (progression != null) progression.AddCoins(coinAmount);
        }

        RespawnPlayer(other);
    }

    /// <summary>
    /// Send the touching player back to the scene spawn point via the hero
    /// controller's built-in death → respawn, exactly like <see cref="WaterDeath"/>.
    /// </summary>
    void RespawnPlayer(Collider playerCollider)
    {
        var hero = playerCollider.GetComponentInParent<HeroCharacterController>();
        var combat = playerCollider.GetComponentInParent<CharacterCombatAgent>();
        var swing = playerCollider.GetComponentInParent<SpiderSwing>();

        if (hero == null && combat == null) return;
        if (hero != null && !hero.IsAlive) return;
        if (hero == null && combat != null && !combat.IsAlive) return;

        // Cut the web first so the pendulum constraint doesn't fight the teleport.
        if (swing != null && swing.IsSwinging) swing.ForceRelease();

        var lethal = new DamageInfo(
            amount: 999999f,
            damageType: DamageType.True,
            source: gameObject,
            instigator: gameObject,
            point: playerCollider.transform.position,
            normal: Vector3.up,
            unblockable: true);

        if (hero != null) hero.ApplyDamage(lethal);
        else combat.ApplyDamage(lethal);
    }
}
