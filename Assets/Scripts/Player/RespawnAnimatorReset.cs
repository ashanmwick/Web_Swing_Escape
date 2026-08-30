using UnityEngine;
using HeroCharacter;

/// <summary>
/// Clears the leftover <c>Death</c> animation trigger when the local player is
/// revived, so it can't fire again the next time a transition graph re-evaluates
/// (e.g. on landing after a small hop) and drop the already-respawned player back
/// into the death pose.
///
/// <para>The third-party <see cref="HeroCharacterController"/> calls
/// <c>animator.SetTrigger("Death")</c> on death but never resets it on revive. If
/// the Animator Controller doesn't consume that trigger immediately (Death only
/// reachable from certain states, not Any State), it stays queued and pops the
/// character into the death clip later. This component listens to the controller's
/// <c>Revived</c> event and resets it – no other behaviour, the player still
/// respawns and stands up normally.</para>
///
/// Lives on the <c>Player</c> GameObject next to <see cref="HeroCharacterController"/>.
/// </summary>
public class RespawnAnimatorReset : MonoBehaviour
{
    [Header("Refs (auto-found on this GameObject if empty)")]
    [SerializeField] HeroCharacterController hero;
    [Tooltip("Animator that plays the death / locomotion. Auto-found in children if empty.")]
    [SerializeField] Animator animator;

    [Header("Triggers to clear on revive")]
    [Tooltip("Must match the controller's Animation Settings > Death Trigger (default 'Death').")]
    [SerializeField] string deathTrigger = "Death";
    [Tooltip("Also cleared on revive so a queued hit-react can't resurface. Blank = skip.")]
    [SerializeField] string damageTrigger = "Damage";

    [Header("Force back to locomotion")]
    [Tooltip("If the animator is still sitting in the death state on revive, cross-fade to this state. " +
             "Set to your locomotion / ground blend state name (e.g. 'Grounded', 'Locomotion', 'Idle'). Blank = skip.")]
    [SerializeField] string locomotionStateName = "Grounded";
    [SerializeField] int animatorLayer = 0;
    [SerializeField] float crossFade = 0.1f;

    void Awake()
    {
        if (hero == null) hero = GetComponent<HeroCharacterController>();
        if (animator == null) animator = GetComponentInChildren<Animator>();
    }

    void OnEnable()
    {
        if (hero != null) hero.Revived += HandleRevived;
    }

    void OnDisable()
    {
        if (hero != null) hero.Revived -= HandleRevived;
    }

    void HandleRevived()
    {
        if (animator == null) return;

        if (!string.IsNullOrEmpty(deathTrigger)) animator.ResetTrigger(deathTrigger);
        if (!string.IsNullOrEmpty(damageTrigger)) animator.ResetTrigger(damageTrigger);

        if (!string.IsNullOrEmpty(locomotionStateName))
        {
            var st = animator.GetCurrentAnimatorStateInfo(animatorLayer);
            bool inDeath = !string.IsNullOrEmpty(deathTrigger) && st.IsName(deathTrigger);
            if (inDeath || animator.IsInTransition(animatorLayer))
                animator.CrossFadeInFixedTime(locomotionStateName, crossFade, animatorLayer);
        }
    }
}
