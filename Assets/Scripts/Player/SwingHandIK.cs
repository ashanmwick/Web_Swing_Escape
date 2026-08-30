using UnityEngine;

/// <summary>
/// Drives the character's right hand toward the live web anchor while
/// <see cref="SpiderSwing"/> is swinging, using Mecanim (Animator) IK.
///
/// <para>Put this on the SAME GameObject as the <see cref="Animator"/> (the
/// <c>player_rigged</c> child), not the Player root. <see cref="SpiderSwing"/> is
/// found automatically on a parent, or wire it in the Inspector.</para>
///
/// Requirements:
/// <list type="bullet">
/// <item>Humanoid rig (this project's <c>player_rigged.fbx</c> is Humanoid).</item>
/// <item>"IK Pass" enabled on the Animator Controller layer named in
/// <see cref="ikLayerIndex"/> (Base Layer / index 0 by default). Already enabled
/// on <c>PlayerAnimator.controller</c>.</item>
/// </list>
/// </summary>
[RequireComponent(typeof(Animator))]
public class SwingHandIK : MonoBehaviour
{
    [Tooltip("The swing script. Auto-found on a parent if left empty.")]
    [SerializeField] SpiderSwing swing;

    [Tooltip("Which hand grabs the web.")]
    [SerializeField] AvatarIKGoal hand = AvatarIKGoal.RightHand;

    [Tooltip("Animator layer that has 'IK Pass' ticked. Base Layer is 0.")]
    [SerializeField] int ikLayerIndex = 0;

    [Tooltip("1 = hand reaches all the way to the anchor (arm fully extended up the rope). " +
             "Lower values pull the IK target back down the rope toward the shoulder for a " +
             "less strained, more natural grip.")]
    [Range(0f, 1f)]
    [SerializeField] float reachAlongRope = 1f;

    [Tooltip("Also rotate the hand so its palm faces up the rope.")]
    [SerializeField] bool matchHandRotation = true;

    [Tooltip("How fast the IK weight fades in when a swing starts and out when it ends " +
             "(per second). Higher = snappier.")]
    [SerializeField] float weightLerpSpeed = 12f;

    Animator anim;
    float weight;

    void Awake()
    {
        anim = GetComponent<Animator>();
        if (swing == null) swing = GetComponentInParent<SpiderSwing>();
    }

    void OnAnimatorIK(int layerIndex)
    {
        if (layerIndex != ikLayerIndex) return;

        bool active = swing != null && swing.IsSwinging;
        weight = Mathf.MoveTowards(weight, active ? 1f : 0f, weightLerpSpeed * Time.deltaTime);

        if (weight <= 0.001f)
        {
            anim.SetIKPositionWeight(hand, 0f);
            anim.SetIKRotationWeight(hand, 0f);
            return;
        }

        Vector3 anchor = swing.AnchorPosition;

        // Blend the target from the current hand position (reachAlongRope = 0) up
        // to the anchor (reachAlongRope = 1) so the pose can be dialled back from a
        // fully locked straight arm.
        Vector3 handPos = anim.GetIKPosition(hand);
        Vector3 target = Vector3.Lerp(handPos, anchor, Mathf.Clamp01(reachAlongRope));

        anim.SetIKPositionWeight(hand, weight);
        anim.SetIKPosition(hand, target);

        if (matchHandRotation)
        {
            Vector3 up = anchor - handPos;
            if (up.sqrMagnitude > 1e-4f)
            {
                anim.SetIKRotationWeight(hand, weight);
                anim.SetIKRotation(hand, Quaternion.LookRotation(up.normalized));
            }
        }
    }
}
