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
///
/// <para><b>Background-tab robustness (WebGL):</b> when the browser tab is hidden
/// the whole Unity loop (physics included) freezes, and on resume Unity steps
/// physics in large catch-up increments. A fast fall can then tunnel straight
/// through the water collider without ever raising <c>OnTriggerEnter</c> /
/// <c>OnCollisionEnter</c>. So in addition to those callbacks this component does
/// a <b>swept</b> check every <c>FixedUpdate</c> (a linecast + short spherecast
/// along the movement since the last step) and re-checks on focus/visibility
/// regain. The world-height fallback is on by default as a final backstop.</para>
/// </summary>
[RequireComponent(typeof(Collider))]
public class WaterDeath : MonoBehaviour
{
    [Header("Detection")]
    [Tooltip("Colliders on these layers count as water. Defaults to the 'Water' layer.")]
    [SerializeField] LayerMask waterLayers = 1 << 4;
    [Tooltip("Also treat any collider whose GameObject has this tag as water. Leave 'Untagged' to disable.")]
    [SerializeField] string waterTag = "Untagged";
    [Tooltip("Radius of the swept catch-up check (roughly the character's body radius). " +
             "Widens the tunnelling test so a thin water collider is still caught after a frozen tab resumes.")]
    [SerializeField] float sweepRadius = 0.5f;
    [Tooltip("Fallback: also die when the player's Y drops below this world height. " +
             "Left ON as a final backstop – set it a couple of metres BELOW the water surface for this scene.")]
    [SerializeField] bool killBelowHeight = true;
    [SerializeField] float killHeight = -20f;

    [Header("Refs (auto-found on this GameObject if empty)")]
    [SerializeField] HeroCharacterController hero;
    [SerializeField] CharacterCombatAgent combat;
    [SerializeField] SpiderSwing swing;

    [Header("Events")]
    [Tooltip("Fired once each time the player is killed by water (hook up SFX / VFX).")]
    public UnityEvent onWaterDeath = new UnityEvent();

    bool killed;
    Vector3 lastPos;
    bool hasLastPos;

    void Awake()
    {
        if (hero == null) hero = GetComponent<HeroCharacterController>();
        if (combat == null) combat = GetComponent<CharacterCombatAgent>();
        if (swing == null) swing = GetComponent<SpiderSwing>();
    }

    void OnEnable()
    {
        lastPos = transform.position;
        hasLastPos = true;
    }

    void Update()
    {
        // Re-arm once the controller has revived us.
        if (killed && IsAlive) killed = false;

        CheckHeight();
    }

    void FixedUpdate()
    {
        // Re-arm here too – on a resumed tab FixedUpdate catches up before Update.
        if (killed && IsAlive) killed = false;

        SweepForWater();
        CheckHeight();

        lastPos = transform.position;
        hasLastPos = true;
    }

    // The tab was hidden and is now visible again: physics is about to catch up in
    // big steps, so evaluate the swept + height checks against where we are right
    // now before that happens.
    void OnApplicationFocus(bool hasFocus) { if (hasFocus) ResumeCheck(); }
    void OnApplicationPause(bool paused) { if (!paused) ResumeCheck(); }

    void ResumeCheck()
    {
        if (!isActiveAndEnabled) return;
        if (killed && IsAlive) killed = false;
        SweepForWater();
        CheckHeight();
        lastPos = transform.position;
        hasLastPos = true;
    }

    void OnTriggerEnter(Collider other) { if (IsWater(other)) Kill(); }
    void OnCollisionEnter(Collision c) { if (IsWater(c.collider)) Kill(); }

    bool IsAlive => hero != null ? hero.IsAlive : (combat == null || combat.IsAlive);

    void CheckHeight()
    {
        if (killBelowHeight && !killed && IsAlive && transform.position.y < killHeight)
            Kill();
    }

    /// <summary>
    /// Catch a fall that moved far enough in one physics step to skip past the
    /// water collider entirely (tab resume, frame-rate hitch). Tests the segment
    /// travelled since the previous step.
    /// </summary>
    void SweepForWater()
    {
        if (killed || !IsAlive || waterLayers.value == 0) return;
        if (!hasLastPos) { lastPos = transform.position; hasLastPos = true; return; }

        Vector3 now = transform.position;
        Vector3 delta = now - lastPos;
        float dist = delta.magnitude;
        if (dist < 0.001f) return;

        Vector3 dir = delta / dist;

        if (Physics.Linecast(lastPos, now, out RaycastHit hit, waterLayers, QueryTriggerInteraction.Collide)
            && IsWater(hit.collider))
        {
            Kill();
            return;
        }

        if (sweepRadius > 0f &&
            Physics.SphereCast(lastPos, sweepRadius, dir, out RaycastHit sphereHit, dist,
                               waterLayers, QueryTriggerInteraction.Collide)
            && IsWater(sphereHit.collider))
        {
            Kill();
        }
    }

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
