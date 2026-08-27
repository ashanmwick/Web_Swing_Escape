using System.Reflection;
using UnityEngine;
using UnityEngine.InputSystem;
using HeroCharacter;

/// <summary>
/// Spider-Man style web swing. Double-tap the jump key to fire a web at an
/// automatically placed anchor above and in front of the player, swing from it
/// under physics, then have the web snap once the player swings past a set angle
/// so they release and fall naturally.
///
/// Lives on the Player GameObject alongside <see cref="HeroCharacterController"/>,
/// <see cref="Rigidbody"/> and <see cref="PlayerInput"/>. While swinging it
/// suspends the hero controller's locomotion (its camera and animation keep
/// running) and drives the Rigidbody directly; gravity is never disabled, so the
/// post-break fall is handled by the physics engine and the hero controller's
/// air handling once control is returned.
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public class SpiderSwing : MonoBehaviour
{
    [Header("Refs (same GameObject unless noted)")]
    [SerializeField] HeroCharacterController hero;
    [SerializeField] PlayerInput playerInput;
    [Tooltip("Right-hand bone – purely the visual origin of the web line.")]
    [SerializeField] Transform handTransform;
    [Tooltip("Line Renderer with 2 positions in world space. Its renderer is toggled by this script.")]
    [SerializeField] LineRenderer web;
    [Tooltip("Used only for a forward direction when placing the anchor. Auto-found in children if empty.")]
    [SerializeField] Camera aimCamera;

    [Header("Anchor placement (no aiming)")]
    [Tooltip("How far above the player the web anchor is placed. Keep it high enough that the anchor point stays off-screen.")]
    [SerializeField] float anchorHeight = 80f;
    [SerializeField] float anchorForwardOffset = 10f;
    [SerializeField] float minRopeLength = 3f;
    [SerializeField] float maxRopeLength = 120f;
    [Tooltip("Optional: if something on these layers is between the player and the virtual anchor, attach to it instead.")]
    [SerializeField] LayerMask anchorMask = ~0;

    [Header("Launch (the instant the web attaches)")]
    [Tooltip("Instant speed added along the player's forward direction when the web deploys.")]
    [SerializeField] float launchForwardSpeed = 7f;
    [Tooltip("Instant speed added straight up when the web deploys.")]
    [SerializeField] float launchUpSpeed = 4f;

    [Header("Swing feel")]
    [Tooltip("If > 0, the rope reels in at this speed (m/s) until it reaches the reel-in target length.")]
    [SerializeField] float reelInSpeed = 4f;
    [Tooltip("Reel-in stops at this fraction of the initial rope length.")]
    [Range(0.1f, 1f)]
    [SerializeField] float reelInToFraction = 0.6f;
    [Tooltip("Fraction of speed bled off per second while swinging. 0 = no energy loss.")]
    [SerializeField] float swingDamping = 0.05f;
    [Tooltip("Extra speed added along the current velocity the instant the web snaps.")]
    [SerializeField] float releaseBoost = 3f;

    [Header("Break condition")]
    [Tooltip("Degrees from straight-down measured at the anchor. 0 = bottom of the arc, 90 = level with the anchor.")]
    [SerializeField] float breakAngle = 55f;
    [SerializeField] float minSwingTime = 0.25f;
    [SerializeField] float maxSwingTime = 4f;
    [SerializeField] float groundBreakDistance = 1.1f;
    [SerializeField] LayerMask groundMask;

    [Header("Input")]
    [SerializeField] float doubleTapWindow = 0.3f;
    [SerializeField] float reattachCooldown = 0.2f;

    Rigidbody rb;
    InputAction jumpAction;
    bool swinging;
    Vector3 anchor;
    Vector3 swingForwardDir;
    float ropeLength;
    float reelInTargetLength;
    float swingTime;
    float lastTapTime = -10f;
    float reattachReadyTime;

    static readonly FieldInfo MovementField =
        typeof(HeroCharacterController).GetField("movement", BindingFlags.NonPublic | BindingFlags.Instance);

    // Unity 6 renamed Rigidbody.velocity -> linearVelocity.
    Vector3 Vel
    {
        get => rb.linearVelocity;
        set => rb.linearVelocity = value;
    }

    void Awake()
    {
        rb = GetComponent<Rigidbody>();
        if (hero == null) hero = GetComponent<HeroCharacterController>();
        if (playerInput == null) playerInput = GetComponent<PlayerInput>();
        if (aimCamera == null) aimCamera = GetComponentInChildren<Camera>();

        jumpAction = playerInput != null ? playerInput.actions["Jump"] : null;
        if (jumpAction == null)
        {
            Debug.LogWarning("SpiderSwing: no 'Jump' action found on PlayerInput; double-tap detection is disabled.", this);
        }

        if (web != null) web.enabled = false;
    }

    void OnDisable()
    {
        // Never leave the player frozen if this component is disabled mid-swing.
        if (swinging)
        {
            swinging = false;
            if (web != null) web.enabled = false;
        }
        SetHeroMovement(true);
    }

    void Update()
    {
        if (jumpAction != null && jumpAction.WasPressedThisFrame())
        {
            float now = Time.time;
            if (now - lastTapTime <= doubleTapWindow && !swinging && now >= reattachReadyTime)
            {
                TryStartSwing();
            }
            lastTapTime = now;
        }

        if (swinging && web != null)
        {
            web.SetPosition(0, handTransform != null ? handTransform.position : transform.position);
            web.SetPosition(1, anchor);
        }
    }

    void FixedUpdate()
    {
        if (!swinging) return;

        swingTime += Time.fixedDeltaTime;

        // Gravity is integrated by the engine (rb.useGravity stays true). Enforce
        // the rope: it only pulls when fully extended.
        Vector3 toAnchor = anchor - rb.position;
        float dist = toAnchor.magnitude;
        if (dist < 0.001f) { BreakWeb(); return; }

        Vector3 dir = toAnchor / dist; // player -> anchor

        // Reel the rope in so the player keeps being drawn toward the anchor.
        if (reelInSpeed > 0f && ropeLength > reelInTargetLength)
        {
            ropeLength = Mathf.MoveTowards(ropeLength, reelInTargetLength, reelInSpeed * Time.fixedDeltaTime);
        }

        if (dist > ropeLength)
        {
            rb.position = anchor - dir * ropeLength;      // clamp onto the sphere
            float outward = Vector3.Dot(Vel, -dir);       // > 0 means stretching the rope
            if (outward > 0f) Vel += dir * outward;       // cancel the radial component
        }

        if (swingDamping > 0f)
        {
            Vel *= Mathf.Clamp01(1f - swingDamping * Time.fixedDeltaTime);
        }

        float angleFromDown = Vector3.Angle(Vector3.down, (rb.position - anchor).normalized);
        bool rising = Vel.y > 0.1f;
        bool movingForward = Vector3.Dot(new Vector3(Vel.x, 0f, Vel.z), swingForwardDir) > 0.1f;

        // Break only on the forward up-swing, so the release always throws the
        // player forward and up – never back the way they came.
        if (swingTime >= minSwingTime && angleFromDown >= breakAngle && rising && movingForward) { BreakWeb(); return; }
        if (swingTime >= maxSwingTime) { BreakWeb(); return; }
        if (groundMask.value != 0 &&
            Physics.Raycast(rb.position, Vector3.down, groundBreakDistance, groundMask, QueryTriggerInteraction.Ignore))
        {
            BreakWeb();
        }
    }

    /// <summary>Public so an on-screen button or another script can trigger it too.</summary>
    public void TryStartSwing()
    {
        if (swinging || Time.time < reattachReadyTime) return;

        Vector3 origin = transform.position + Vector3.up * 1.5f;
        Vector3 fwd = aimCamera != null ? aimCamera.transform.forward : transform.forward;
        fwd = Vector3.ProjectOnPlane(fwd, Vector3.up).normalized;
        if (fwd.sqrMagnitude < 0.0001f) fwd = transform.forward;

        Vector3 target = origin + fwd * anchorForwardOffset + Vector3.up * anchorHeight;

        if (Physics.Raycast(origin, (target - origin).normalized, out RaycastHit hit,
                            Vector3.Distance(origin, target), anchorMask, QueryTriggerInteraction.Ignore))
        {
            target = hit.point;
        }

        if (target.y <= transform.position.y + 1f) return; // anchor must be above the player

        float dist = Vector3.Distance(rb.position, target);
        if (dist < minRopeLength || dist > maxRopeLength) return;

        anchor = target;
        swingForwardDir = fwd;
        ropeLength = dist;
        reelInTargetLength = Mathf.Max(minRopeLength, dist * reelInToFraction);
        swingTime = 0f;
        swinging = true;

        SetHeroMovement(false); // hand physics control to this script

        // Realistic launch: the web yanks the player a bit forward and a bit up,
        // then gravity turns that into a swing. No pull backward toward the anchor.
        Vector3 horizVel = new Vector3(Vel.x, 0f, Vel.z);
        float forwardCarry = Mathf.Max(0f, Vector3.Dot(horizVel, fwd)); // keep only forward run speed
        Vel = fwd * (forwardCarry + launchForwardSpeed) + Vector3.up * Mathf.Max(Vel.y, 0f) + Vector3.up * launchUpSpeed;

        if (web != null)
        {
            web.positionCount = 2;
            web.enabled = true;
        }
    }

    void BreakWeb()
    {
        if (!swinging) return;
        swinging = false;

        if (web != null) web.enabled = false;

        // Guarantee the release never carries the player backward, whatever path
        // triggered the break (angle, timeout or ground).
        Vector3 backward = Vector3.Project(new Vector3(Vel.x, 0f, Vel.z), swingForwardDir);
        if (Vector3.Dot(backward, swingForwardDir) < 0f)
        {
            Vel -= backward; // strip the backward horizontal component
        }

        if (releaseBoost > 0f && Vel.sqrMagnitude > 0.01f)
        {
            Vel += Vel.normalized * releaseBoost; // fling on snap
        }

        SetHeroMovement(true); // hero resumes: air control now, landing later
        reattachReadyTime = Time.time + reattachCooldown;
    }

    void SetHeroMovement(bool enabled)
    {
        if (hero == null || MovementField == null) return;

        object movement = MovementField.GetValue(hero);
        if (movement == null) return;

        FieldInfo flag = movement.GetType().GetField("enableMovementControl");
        flag?.SetValue(movement, enabled);
    }
}
