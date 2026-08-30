using UnityEngine;
using UnityEngine.Events;
using HeroCharacter;

/// <summary>
/// Kills the local player the instant they touch water, then lets the
/// <see cref="HeroCharacterController"/>'s built-in auto-respawn take them back to
/// the scene spawn point.
///
/// <para>Lives on the <c>Player</c> GameObject next to <see cref="SpiderSwing"/>,
/// <see cref="HeroCharacterController"/> and its <see cref="CharacterCombatAgent"/>.
/// Nothing on the water object is required beyond a collider – by default any
/// collider on the <c>Water</c> layer counts, whether it is solid or a trigger.
/// An optional world-height fallback also kills the player if they drop below a
/// given Y (useful where the water has no collider).</para>
///
/// The kill is applied as one big <see cref="DamageType.True"/>, unblockable hit
/// through <see cref="IDamageable"/>, so the controller's normal death → respawn
/// flow runs unmodified.
/// </summary>
[RequireComponent(typeof(Collider))]
public class WaterDeath : MonoBehaviour
{
    [Header("Detection")]
    [Tooltip("Colliders on these layers count as water. Defaults to the 'Water' layer.")]
    [SerializeField] LayerMask waterLayers = 1 << 4;
    [Tooltip("Also treat any collider whose GameObject has this tag as water. Leave 'Untagged' to disable.")]
    [SerializeField] string waterTag = "Untagged";
    [Tooltip("Fallback: also die when the player's Y drops below this world height. " +
             "Enable only if the water has no collider.")]
    [SerializeField] bool killBelowHeight = false;
    [SerializeField] float killHeight = -20f;

    [Header("Refs (auto-found on this GameObject if empty)")]
    [SerializeField] HeroCharacterController hero;
    [SerializeField] CharacterCombatAgent combat;
    [SerializeField] SpiderSwing swing;

    [Header("Events")]
    [Tooltip("Fired once each time the player is killed by water (hook up SFX / VFX).")]
    public UnityEvent onWaterDeath = new UnityEvent();

    bool killed;

    void Awake()
    {
        if (hero == null) hero = GetComponent<HeroCharacterController>();
        if (combat == null) combat = GetComponent<CharacterCombatAgent>();
        if (swing == null) swing = GetComponent<SpiderSwing>();
    }

    void Update()
    {
        // Re-arm once the controller has revived us.
        if (killed && IsAlive) killed = false;

        if (killBelowHeight && !killed && IsAlive && transform.position.y < killHeight)
        {
            Kill();
        }
    }

    void OnTriggerEnter(Collider other) { if (IsWater(other)) Kill(); }
    void OnCollisionEnter(Collision c) { if (IsWater(c.collider)) Kill(); }

    bool IsAlive => hero != null ? hero.IsAlive : (combat == null || combat.IsAlive);

    bool IsWater(Collider other)
    {
        if (other == null) return false;
        if ((waterLayers.value & (1 << other.gameObject.layer)) != 0) return true;
        return waterTag != "Untagged" && waterTag.Length > 0 && other.CompareTag(waterTag);
    }

    void Kill()
    {
        if (killed || !IsAlive) return;
        killed = true;

        // Cut the web first so the pendulum constraint doesn't fight the respawn
        // teleport, and hand locomotion back to the hero controller.
        if (swing != null && swing.IsSwinging) swing.ForceRelease();

        var lethal = new DamageInfo(
            amount: 999999f,
            damageType: DamageType.True,
            source: gameObject,
            instigator: gameObject,
            point: transform.position,
            normal: Vector3.up,
            unblockable: true);

        if (hero != null) hero.ApplyDamage(lethal);
        else combat?.ApplyDamage(lethal);

        onWaterDeath.Invoke();
    }
}
