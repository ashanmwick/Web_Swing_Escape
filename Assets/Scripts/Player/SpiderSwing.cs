using System.Reflection;
using UnityEngine;
using UnityEngine.InputSystem;
using HeroCharacter;

/// <summary>
/// Spider-Man style web swing implemented as a real pendulum chained with
/// projectile flight.
///
/// <para>Flow (matches the design spec):</para>
/// <list type="number">
/// <item>Double-press <c>Jump</c> (Space) to fire a web. No aiming – the anchor is
/// auto-generated above and ahead of the player, further ahead the faster they
/// are moving.</item>
/// <item>The web becomes a fixed-length pivot. On a <b>fresh</b> swing (standing
/// still / just landed) a big up+forward impulse launches the player into the
/// first arc; mid-air re-fires keep the momentum they already have.</item>
/// <item>Extra gravity + an automatic down-swing assist + optional move-input
/// "pumping" pour energy into the arc, so speed really builds on the way down.</item>
/// <item>Release with a <b>single</b> Space press, or automatically near the top of
/// the forward up-swing. A <b>double</b> press releases and immediately fires the
/// next web (the accelerator chain).</item>
/// <item>After release the player is a free projectile until the next web.</item>
/// </list>
///
/// Lives on the Player GameObject alongside <see cref="HeroCharacterController"/>,
/// <see cref="Rigidbody"/> and <see cref="PlayerInput"/>. While swinging it
/// suspends the hero controller's locomotion (camera + animation keep running)
/// and drives the Rigidbody directly. Gravity is never disabled, so the release
/// hands a clean ballistic velocity back to the hero controller's air handling.
///
/// The web is a 2-point world-space <see cref="LineRenderer"/> – point 0 is the
/// hand, point 1 is the anchor, and its <c>enabled</c> flag is the public
/// "is swinging" signal the net layer reads.
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
    [Tooltip("Used for the forward direction when placing the anchor. Auto-found in children if empty.")]
    [SerializeField] Camera aimCamera;

    [Header("Anchor placement (no aiming)")]
    [Tooltip("Base height of the auto-anchor above the player.")]
    [SerializeField] float anchorHeight = 55f;
    [Tooltip("Base forward distance of the anchor ahead of the player.")]
    [SerializeField] float anchorForwardOffset = 9f;
    [Tooltip("Extra forward offset added per m/s of horizontal speed (faster = flatter, faster arc).")]
    [SerializeField] float anchorForwardPerSpeed = 0.55f;
    [SerializeField] float minRopeLength = 6f;
    [SerializeField] float maxRopeLength = 140f;
    [Tooltip("If solid geometry on these layers is between the player and the virtual anchor, attach to it instead.")]
    [SerializeField] LayerMask anchorMask = ~0;

    [Header("Launch – fresh swing (standing / just landed)")]
    [Tooltip("Straight-up speed injected so the player makes a big jump into the first arc.")]
    [SerializeField] float freshLaunchUpSpeed = 15f;
    [Tooltip("Forward speed injected on a fresh swing.")]
    [SerializeField] float freshLaunchForwardSpeed = 12f;
    [Tooltip("Below this horizontal speed the next swing counts as 'fresh' and gets the big launch.")]
    [SerializeField] float freshSpeedThreshold = 4f;

    [Header("Launch – mid-air re-fire (the chain)")]
    [Tooltip("Small forward nudge when firing a new web while already flying.")]
    [SerializeField] float airLaunchForwardSpeed = 4f;

    [Header("Swing feel")]
    [Tooltip("Gravity multiplier while the rope is taut. >1 makes the down-swing genuinely accelerate.")]
    [SerializeField] float swingGravityMultiplier = 2.2f;
    [Tooltip("Always-on tangential acceleration along the swing direction (the 'automatic pump').")]
    [SerializeField] float autoPumpAccel = 9f;
    [Tooltip("Extra tangential acceleration from the Move input (lean into / out of the swing).")]
    [SerializeField] float inputPumpAccel = 20f;
    [Tooltip("Rope reels in at this speed (m/s) until it reaches the reel-in fraction. 0 = fixed length.")]
    [SerializeField] float reelInSpeed = 6f;
    [Range(0.2f, 1f)]
    [SerializeField] float reelInToFraction = 0.7f;
    [Tooltip("Fraction of speed bled off per second while swinging. Keep small.")]
    [SerializeField] float swingDamping = 0.02f;
    [Tooltip("Hard cap on swing speed (m/s).")]
    [SerializeField] float maxSwingSpeed = 45f;

    [Header("Release")]
    [Tooltip("Extra speed added along the current velocity the instant the web lets go.")]
    [SerializeField] float releaseBoost = 6f;
    [SerializeField] bool autoRelease = true;
    [Tooltip("Degrees from straight-down, measured at the anchor, for the automatic release on the forward up-swing.")]
    [SerializeField] float autoReleaseAngle = 62f;
    [SerializeField] float minSwingTime = 0.2f;
    [SerializeField] float maxSwingTime = 5f;
    [Tooltip("Release early if the ground is within this distance below the player.")]
    [SerializeField] float groundBreakDistance = 1.4f;
    [SerializeField] LayerMask groundMask;

    [Header("Input")]
    [SerializeField] float doubleTapWindow = 0.28f;
    [SerializeField] float reattachCooldown = 0.12f;

    Rigidbody rb;
    InputAction jumpAction;
    InputAction moveAction;
    bool swinging;
    bool everSwung;
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
        moveAction = playerInput != null ? playerInput.actions["Move"] : null;
        if (jumpAction == null)
        {
            Debug.LogWarning("SpiderSwing: no 'Jump' action found on PlayerInput; the web swing is disabled.", this);
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
            bool isDouble = now - lastTapTime <= doubleTapWindow;

            if (swinging)
            {
                // Single press -> release into the projectile phase.
                // Double press -> release AND immediately fire the next web.
                ReleaseWeb();
                if (isDouble && now >= reattachReadyTime) TryStartSwing();
            }
            else if (isDouble && now >= reattachReadyTime)
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

        float dt = Time.fixedDeltaTime;
        swingTime += dt;

        Vector3 toAnchor = anchor - rb.position;
        float dist = toAnchor.magnitude;
        if (dist < 0.001f) { ReleaseWeb(); return; }
        Vector3 toAnchorDir = toAnchor / dist; // player -> anchor

        // Reel the rope in so the player is progressively drawn toward the anchor
        // (this is what lets a swing that starts slack still build a real arc).
        if (reelInSpeed > 0f && ropeLength > reelInTargetLength)
        {
            ropeLength = Mathf.MoveTowards(ropeLength, reelInTargetLength, reelInSpeed * dt);
        }

        // Extra gravity while taut: the engine already integrates 1x gravity, add
        // the rest here so the down-swing genuinely accelerates.
        if (swingGravityMultiplier > 1f)
        {
            Vel += Physics.gravity * ((swingGravityMultiplier - 1f) * dt);
        }

        // Pumping: accelerate along the arc (perpendicular to the rope). The
        // automatic part guarantees a strong swing even with no input; the Move
        // input lets the player lean into or kill the swing.
        Vector3 arcForward = Vector3.ProjectOnPlane(swingForwardDir, toAnchorDir);
        if (arcForward.sqrMagnitude > 0.0001f)
        {
            arcForward.Normalize();
            Vel += arcForward * (autoPumpAccel * dt);

            if (moveAction != null && inputPumpAccel > 0f)
            {
                Vector2 mv = moveAction.ReadValue<Vector2>();
                if (mv.sqrMagnitude > 0.01f)
                {
                    Vector3 right = Vector3.Cross(Vector3.up, swingForwardDir).normalized;
                    Vector3 wish = swingForwardDir * mv.y + right * mv.x;
                    Vector3 wishTangent = Vector3.ProjectOnPlane(wish, toAnchorDir);
                    Vel += wishTangent * (inputPumpAccel * dt);
                }
            }
        }

        // Rope constraint: it only pulls, never pushes. When fully extended,
        // clamp onto the sphere and cancel the outward radial velocity.
        if (dist > ropeLength)
        {
            rb.position = anchor - toAnchorDir * ropeLength;
            float outward = Vector3.Dot(Vel, -toAnchorDir); // > 0 => stretching the rope
            if (outward > 0f) Vel += toAnchorDir * outward;
        }

        if (swingDamping > 0f)
        {
            Vel *= Mathf.Clamp01(1f - swingDamping * dt);
        }

        if (Vel.sqrMagnitude > maxSwingSpeed * maxSwingSpeed)
        {
            Vel = Vel.normalized * maxSwingSpeed;
        }

        // --- release conditions ---
        Vector3 fromAnchor = (rb.position - anchor).normalized;
        float angleFromDown = Vector3.Angle(Vector3.down, fromAnchor);
        bool rising = Vel.y > 0.1f;
        bool movingForward = Vector3.Dot(new Vector3(Vel.x, 0f, Vel.z), swingForwardDir) > 0.1f;

        // Auto-release near the peak of the forward up-swing, so the throw is
        // always forward-and-up, never back the way the player came.
        if (autoRelease && swingTime >= minSwingTime && angleFromDown >= autoReleaseAngle && rising && movingForward)
        {
            ReleaseWeb();
            return;
        }
        if (swingTime >= maxSwingTime) { ReleaseWeb(); return; }
        if (groundMask.value != 0 &&
            Physics.Raycast(rb.position, Vector3.down, groundBreakDistance, groundMask, QueryTriggerInteraction.Ignore))
        {
            ReleaseWeb();
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

        float horizSpeed = new Vector3(Vel.x, 0f, Vel.z).magnitude;
        float forwardOffset = anchorForwardOffset + horizSpeed * anchorForwardPerSpeed;
        Vector3 target = origin + fwd * forwardOffset + Vector3.up * anchorHeight;

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

        // A "fresh" swing (standing still or just landed, and never swung before,
        // or simply slow) gets a big up+forward launch so the first arc is a real
        // leap. A mid-air re-fire keeps whatever momentum the player already has.
        bool fresh = !everSwung || horizSpeed < freshSpeedThreshold;
        Vector3 horizVel = new Vector3(Vel.x, 0f, Vel.z);
        float forwardCarry = Mathf.Max(0f, Vector3.Dot(horizVel, fwd)); // keep only forward run speed

        if (fresh)
        {
            Vel = fwd * (forwardCarry + freshLaunchForwardSpeed) + Vector3.up * freshLaunchUpSpeed;
        }
        else
        {
            Vel = fwd * (forwardCarry + airLaunchForwardSpeed) + Vector3.up * Mathf.Max(Vel.y, 0f);
        }

        everSwung = true;

        if (web != null)
        {
            web.positionCount = 2;
            web.SetPosition(0, handTransform != null ? handTransform.position : transform.position);
            web.SetPosition(1, anchor);
            web.enabled = true;
        }
    }

    void ReleaseWeb()
    {
        if (!swinging) return;
        swinging = false;

        if (web != null) web.enabled = false;

        // Guarantee the release never carries the player backward, whatever path
        // triggered it (single press, auto-angle, timeout or ground).
        Vector3 horiz = new Vector3(Vel.x, 0f, Vel.z);
        Vector3 backward = Vector3.Project(horiz, swingForwardDir);
        if (Vector3.Dot(backward, swingForwardDir) < 0f)
        {
            Vel -= backward; // strip the backward horizontal component
        }

        if (releaseBoost > 0f && Vel.sqrMagnitude > 0.01f)
        {
            Vel += Vel.normalized * releaseBoost; // fling on release
        }

        float cap = maxSwingSpeed * 1.15f;
        if (Vel.sqrMagnitude > cap * cap) Vel = Vel.normalized * cap;

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
