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
    [Tooltip("ON: every anchor sits at a fixed height above the ground below it (a virtual skyline), so the rope " +
             "shortens as the player rises and there is a natural ceiling. OFF: anchor is placed a fixed distance " +
             "above the player.")]
    [SerializeField] bool anchorAtFixedHeight = true;
    [Tooltip("Fixed-height mode: metres above the ground that the anchor is placed.")]
    [SerializeField] float anchorHeightAboveGround = 38f;
    [Tooltip("How far to search up/down for the ground beneath the anchor point.")]
    [SerializeField] float groundProbeDistance = 400f;
    [Tooltip("What counts as 'ground' for the fixed-height probe.")]
    [SerializeField] LayerMask groundProbeMask = ~0;
    [Tooltip("Player-relative mode: distance the anchor is placed above the player.")]
    [SerializeField] float anchorHeight = 55f;
    [Tooltip("Base forward distance of the anchor ahead of the player.")]
    [SerializeField] float anchorForwardOffset = 9f;
    [Tooltip("Extra forward offset added per m/s of horizontal speed (faster = flatter, faster arc).")]
    [SerializeField] float anchorForwardPerSpeed = 0.55f;
    [SerializeField] float minRopeLength = 6f;
    [SerializeField] float maxRopeLength = 140f;
    [Tooltip("The web won't fire unless the anchor is at least this many metres ABOVE the player. In fixed-height " +
             "mode this is the real ceiling: get within this vertical gap of the skyline and the shot is refused, " +
             "so the player must drop a little before they can swing again.")]
    [SerializeField] float minVerticalClearance = 4f;
    [Tooltip("If solid geometry on these layers is between the player and the virtual anchor, attach to it instead.")]
    [SerializeField] LayerMask anchorMask = ~0;

    [Header("Launch – fresh swing (standing / just landed)")]
    [Tooltip("Straight-up speed injected so the player makes a big jump into the first arc. Sized for the project's " +
             "-20 gravity – lower it if you drop gravity back toward -10.")]
    [SerializeField] float freshLaunchUpSpeed = 20f;
    [Tooltip("Forward speed injected on a fresh swing.")]
    [SerializeField] float freshLaunchForwardSpeed = 12f;
    [Tooltip("Below this horizontal speed the next swing counts as 'fresh' and gets the big launch.")]
    [SerializeField] float freshSpeedThreshold = 4f;

    [Header("Launch – mid-air re-fire (the chain)")]
    [Tooltip("Small forward nudge when firing a new web while already flying.")]
    [SerializeField] float airLaunchForwardSpeed = 4f;

    [Header("Swing feel")]
    [Tooltip("Gravity multiplier while the rope is taut, on top of project gravity (currently -20). >1 makes the " +
             "down-swing accelerate harder than a free fall; ~1.1 is plenty now that base gravity is already high.")]
    [SerializeField] float swingGravityMultiplier = 1.1f;
    [Tooltip("Always-on tangential acceleration along the swing direction (the 'automatic pump').")]
    [SerializeField] float autoPumpAccel = 9f;
    [Tooltip("The auto-pump only adds energy while the along-arc speed is below this. Above it the swing coasts, " +
             "so chained swings settle at a steady height instead of climbing forever.")]
    [SerializeField] float autoPumpTargetSpeed = 20f;
    [Tooltip("Extra tangential acceleration from the Move input (lean into / out of the swing).")]
    [SerializeField] float inputPumpAccel = 20f;
    [Tooltip("Rope reels in at this speed (m/s) until it reaches the reel-in fraction. 0 = fixed length.")]
    [SerializeField] float reelInSpeed = 6f;
    [Range(0.2f, 1f)]
    [SerializeField] float reelInToFraction = 0.7f;
    [Tooltip("Fraction of speed bled off per second while swinging. Keep small.")]
    [SerializeField] float swingDamping = 0.02f;
    [Tooltip("Hard cap on swing speed (m/s).")]
    [SerializeField] float maxSwingSpeed = 34f;

    [Header("Swing orientation (pitch along the rope)")]
    [Tooltip("While swinging, pitch the body about its X axis so the spine lies along the rope – the character " +
             "hangs and swings like a real pendulum bob – then level back out after release. The hero controller " +
             "doesn't touch body rotation as long as its camera Body Alignment Smoothing is 0.")]
    [SerializeField] bool orientToSwing = true;
    [Range(0f, 1.5f)]
    [Tooltip("How closely the body's up axis tracks the rope: 0 = stay upright, 1 = head points exactly at the " +
             "anchor (full rope alignment), >1 over-rotates for a more dramatic lean.")]
    [SerializeField] float ropePitchAmount = 1f;
    [Tooltip("How quickly the body chases the target swing pose (higher = snappier). Exponential, frame-rate independent.")]
    [SerializeField] float orientLerpSpeed = 10f;
    [Tooltip("Seconds taken to ease the swing pitch back to the character's normal upright angle after the web " +
             "detaches. The blend is eased (slow-in / slow-out) and always completes in this time.")]
    [SerializeField] float levelOutDuration = 0.65f;

    // Jump ascent and swing feel are handled by the project's -20 gravity (see
    // ProjectSettings/DynamicsManager.asset). A pure fall still reads a touch light at this
    // world scale, so we add a small DESCENT-ONLY boost here – it never touches the jump rise
    // or the swing, only the way down.
    [Header("Fall feel (descending, not swinging)")]
    [Tooltip("Extra gravity applied only while airborne AND moving downward AND not swinging, on top of the -20 " +
             "project gravity. 1 = off. ~1.7 makes a drop feel heavy without affecting jump height.")]
    [SerializeField] float fallGravityMultiplier = 1.7f;
    [Tooltip("Terminal velocity for the assisted fall (m/s): the drop accelerates to this and no faster.")]
    [SerializeField] float maxFallSpeed = 70f;
    [Tooltip("The fall boost stops only when ground is within this many metres straight below the body. Keep it " +
             "SMALL – the hero controller's own 'grounded' probe reaches ~4 m and would switch the boost off long " +
             "before you land.")]
    [SerializeField] float fallGravityGroundCushion = 0.6f;

    [Header("Release")]
    [Tooltip("Extra speed added along the horizontal release direction the instant the web lets go.")]
    [SerializeField] float releaseBoost = 3f;
    [Tooltip("Upward speed is clamped to this on release, so a chain of swings can't fling the player ever higher.")]
    [SerializeField] float maxReleaseUpSpeed = 9f;
    [Tooltip("Seconds after release to protect the horizontal launch speed from the hero controller's air drag. " +
             "With no move key held the controller bleeds planar velocity toward zero fast, so the release turns " +
             "into a near-vertical sink that reads as floaty. 0 disables the hold.")]
    [SerializeField] float momentumHoldDuration = 1.4f;
    [Tooltip("How hard (m/s²) the hold fights that air drag back toward the release speed. ~9 roughly cancels the " +
             "controller's default airborne deceleration; lower = momentum still bleeds, just slower.")]
    [SerializeField] float momentumRestoreAccel = 9f;
    [SerializeField] bool autoRelease = true;
    [Tooltip("Degrees from straight-down, measured at the anchor, for the automatic release on the forward up-swing. " +
             "Lower = release earlier = more forward and less upward.")]
    [SerializeField] float autoReleaseAngle = 50f;
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
    float releaseLevelStart = -1f;
    float momentumHoldUntil = -1f;
    float releaseHorizSpeed;

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

            // Snap the swing tilt back to upright so the body isn't left leaning.
            Vector3 flatFwd = Vector3.ProjectOnPlane(transform.forward, Vector3.up);
            if (flatFwd.sqrMagnitude > 1e-4f)
                transform.rotation = Quaternion.LookRotation(flatFwd.normalized, Vector3.up);
        }
        releaseLevelStart = -1f;
        momentumHoldUntil = -1f;
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
        if (!swinging)
        {
            HoldReleaseMomentum(Time.fixedDeltaTime);
            ApplyFallGravity(Time.fixedDeltaTime);
            LevelOutAfterRelease(Time.fixedDeltaTime);
            return;
        }

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

            // The auto-pump is a governor, not a rocket: it only tops the swing up
            // toward a cruise speed. Once the along-arc speed reaches the target it
            // stops adding energy, so chained swings converge to a stable height
            // instead of climbing every cycle.
            float alongArc = Vector3.Dot(Vel, arcForward);
            if (alongArc < autoPumpTargetSpeed)
            {
                float falloff = 1f - Mathf.Clamp01(alongArc / autoPumpTargetSpeed);
                Vel += arcForward * (autoPumpAccel * falloff * dt);
            }

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

        if (orientToSwing) UpdateSwingOrientation(toAnchorDir, dt);

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

    /// <summary>
    /// Keep the swing's horizontal launch speed alive for a short window after release.
    /// The hero controller resumes on release and, with no move key held, bleeds planar
    /// velocity toward zero at <c>deceleration * airControl</c> every step – fast enough
    /// that the arc collapses into a near-vertical sink, which reads as "no gravity / slow
    /// drop". Here we push the horizontal speed back toward what it was at release (along
    /// the CURRENT heading, so steering still works), roughly cancelling that drag. Never
    /// adds speed above the release value, and yields the moment the player is already
    /// going faster (e.g. they held forward or dived).
    /// </summary>
    void HoldReleaseMomentum(float dt)
    {
        if (momentumHoldDuration <= 0f || Time.time >= momentumHoldUntil) return;
        if (rb == null || rb.isKinematic || releaseHorizSpeed < 0.1f) return;

        Vector3 horiz = new Vector3(Vel.x, 0f, Vel.z);
        float speed = horiz.magnitude;
        if (speed >= releaseHorizSpeed) return;

        Vector3 dir = speed > 0.1f ? horiz / speed : new Vector3(swingForwardDir.x, 0f, swingForwardDir.z).normalized;
        if (dir.sqrMagnitude < 0.5f) return;

        float restored = Mathf.MoveTowards(speed, releaseHorizSpeed, momentumRestoreAccel * dt);
        Vel = dir * restored + Vector3.up * Vel.y;
    }

    /// <summary>
    /// A drop still reads a little light at this world's scale even under -20 gravity, so add a
    /// small extra pull while the player is airborne, falling and not swinging. Descent only:
    /// the jump rise and the swing arc are untouched. Capped at a terminal speed so a long
    /// plunge stays controllable.
    ///
    /// <para>Deliberately does NOT use <c>hero.IsGrounded</c>: that controller's grounding is a
    /// long spherecast (~4 m for this character) meant for slope/step handling, so it flips true
    /// half a body-height above the floor and would switch the boost off for most of every fall.
    /// We do our own tight ray instead.</para>
    /// </summary>
    void ApplyFallGravity(float dt)
    {
        if (fallGravityMultiplier <= 1f) return;
        if (rb == null || rb.isKinematic) return;
        if (Vel.y >= 0f) return; // rising or level: leave it alone

        // About to land? Hand the body back to the hero controller for the touchdown.
        // Use groundMask if it's been set, else every layer except Ignore Raycast –
        // NOT groundProbeMask, which is the Water-only mask for skyline anchoring.
        int mask = groundMask.value != 0 ? groundMask.value : Physics.DefaultRaycastLayers;
        if (Physics.Raycast(rb.position, Vector3.down, Mathf.Max(0.01f, fallGravityGroundCushion),
                            mask, QueryTriggerInteraction.Ignore))
            return;

        Vel += Physics.gravity * ((fallGravityMultiplier - 1f) * dt);

        if (maxFallSpeed > 0f && Vel.y < -maxFallSpeed)
            Vel = new Vector3(Vel.x, -maxFallSpeed, Vel.z);
    }

    /// <summary>
    /// While the rope is taut, pitch the body about its X axis so its up axis runs
    /// up the rope to the anchor – the character hangs and swings like a pendulum
    /// bob, tilting forward/back exactly as the rope leans. Heading stays locked to
    /// the direction the web was fired, so this is pure pitch with no yaw wobble.
    /// Written straight onto <see cref="Transform.rotation"/> because the Rigidbody
    /// freezes all rotation axes (so <c>MoveRotation</c> would be ignored) and the
    /// hero controller leaves body rotation alone while its camera Body Alignment
    /// Smoothing is 0.
    /// </summary>
    void UpdateSwingOrientation(Vector3 toAnchorDir, float dt)
    {
        // Stable heading: the horizontal direction the swing was fired in.
        Vector3 heading = swingForwardDir;
        if (heading.sqrMagnitude < 1e-4f) heading = Vector3.ProjectOnPlane(transform.forward, Vector3.up);
        if (heading.sqrMagnitude < 1e-4f) return;
        heading.Normalize();

        Quaternion upright = Quaternion.LookRotation(heading, Vector3.up);

        // Fully rope-aligned pose: body up = straight along the rope. Because the
        // heading and the rope share the same vertical plane, the delta from
        // upright is a rotation about the body's right (X) axis – i.e. pure pitch.
        Vector3 fwdOnRope = Vector3.ProjectOnPlane(heading, toAnchorDir);
        Quaternion alongRope = fwdOnRope.sqrMagnitude > 1e-4f
            ? Quaternion.LookRotation(fwdOnRope.normalized, toAnchorDir)
            : upright;

        // Scale that pitch by ropePitchAmount (1 = match the rope exactly).
        Quaternion target = Quaternion.SlerpUnclamped(upright, alongRope, ropePitchAmount);

        float t = 1f - Mathf.Exp(-orientLerpSpeed * dt); // frame-rate independent
        transform.rotation = Quaternion.Slerp(transform.rotation, target, t);
    }

    /// <summary>
    /// After the web lets go, ease the swing pitch back to the character's normal
    /// upright angle over <see cref="levelOutDuration"/> seconds. Yaw is left to
    /// the hero controller (we re-read the current heading every step), so this
    /// only cancels the pitch. The blend is eased and guaranteed to finish inside
    /// the window regardless of frame rate.
    /// </summary>
    void LevelOutAfterRelease(float dt)
    {
        if (!orientToSwing || releaseLevelStart < 0f || levelOutDuration <= 0f) return;

        float elapsed = Time.time - releaseLevelStart;
        if (elapsed >= levelOutDuration)
        {
            releaseLevelStart = -1f; // done
            return;
        }

        Vector3 fwd = Vector3.ProjectOnPlane(transform.forward, Vector3.up);
        if (fwd.sqrMagnitude < 1e-4f) return;
        Quaternion target = Quaternion.LookRotation(fwd.normalized, Vector3.up);

        // Eased progress (slow-in / slow-out) along the fixed window, converted to
        // a per-step amount that reaches 1 exactly at the end of the window.
        float k = Mathf.SmoothStep(0f, 1f, elapsed / levelOutDuration);
        float kPrev = Mathf.SmoothStep(0f, 1f, Mathf.Max(0f, elapsed - dt) / levelOutDuration);
        float step = k >= 1f ? 1f : Mathf.Clamp01((k - kPrev) / (1f - kPrev));
        transform.rotation = Quaternion.Slerp(transform.rotation, target, step);
    }

    /// <summary>
    /// Public so other gameplay scripts (e.g. a death/respawn handler) can cut the
    /// web immediately. Safe to call when not swinging – it is a no-op.
    /// </summary>
    public void ForceRelease() => ReleaseWeb();

    /// <summary>True while a web is attached and this script is driving the body.</summary>
    public bool IsSwinging => swinging;

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
        Vector3 flatTarget = origin + fwd * forwardOffset; // anchor's XZ column

        Vector3 target;
        if (anchorAtFixedHeight)
        {
            // Anchor sits at a constant height above whatever ground is below its
            // XZ column – a virtual skyline. As the player climbs, the rope gets
            // shorter and the arc tighter, so there is a hard, natural ceiling and
            // chained swings can't gain altitude forever.
            // Probe from just above the player's feet straight down for the FLOOR
            // under the anchor column (starting high would catch overhead roofs).
            Vector3 probeStart = new Vector3(flatTarget.x, transform.position.y + 2f, flatTarget.z);
            float groundY = Physics.Raycast(probeStart, Vector3.down, out RaycastHit gh, groundProbeDistance,
                                            groundProbeMask, QueryTriggerInteraction.Ignore)
                ? gh.point.y
                : transform.position.y; // no ground found: fall back to player height
            target = new Vector3(flatTarget.x, groundY + anchorHeightAboveGround, flatTarget.z);

            // NOTE: the line-of-sight snap below is skipped in this mode so the
            // fixed skyline height is actually honoured.
        }
        else
        {
            // Player-relative: place the anchor a set distance above the player,
            // pulled lower while rising so it doesn't chase them upward.
            float height = anchorHeight;
            if (Vel.y > 0f) height = Mathf.Max(anchorHeight * 0.45f, anchorHeight - Vel.y * 2f);
            target = flatTarget + Vector3.up * height;
        }

        // Player-relative mode only: if solid geometry is between the hand and the
        // virtual anchor, attach to that instead. Fixed-height mode keeps its
        // computed skyline point untouched.
        if (!anchorAtFixedHeight &&
            Physics.Raycast(origin, (target - origin).normalized, out RaycastHit hit,
                            Vector3.Distance(origin, target), anchorMask, QueryTriggerInteraction.Ignore))
        {
            target = hit.point;
        }

        if (target.y - transform.position.y < minVerticalClearance) return; // must be clearly above the player

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
            // Carry existing momentum, but don't let a mid-air re-fire bank extra
            // altitude on top of an already-rising jump.
            float upCarry = Mathf.Clamp(Vel.y, -maxSwingSpeed, maxReleaseUpSpeed);
            Vel = fwd * (forwardCarry + airLaunchForwardSpeed) + Vector3.up * upCarry;
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

        // Boost only the horizontal part, so the fling always translates into
        // forward reach rather than extra height.
        Vector3 flat = new Vector3(Vel.x, 0f, Vel.z);
        if (releaseBoost > 0f && flat.sqrMagnitude > 0.01f)
        {
            Vel += flat.normalized * releaseBoost;
        }

        float cap = maxSwingSpeed * 1.15f;
        if (Vel.sqrMagnitude > cap * cap) Vel = Vel.normalized * cap;

        // Cap upward launch so each swing-release can't stack altitude onto the
        // last one. Downward speed is left alone (you can still dive).
        if (Vel.y > maxReleaseUpSpeed)
        {
            Vel = new Vector3(Vel.x, maxReleaseUpSpeed, Vel.z);
        }

        SetHeroMovement(true); // hero resumes: air control now, landing later
        reattachReadyTime = Time.time + reattachCooldown;
        releaseLevelStart = Time.time; // begin easing the swing pitch back to upright

        // Remember the launch speed so HoldReleaseMomentum can protect it from the
        // hero controller's airborne drag for the next momentumHoldDuration seconds.
        releaseHorizSpeed = new Vector3(Vel.x, 0f, Vel.z).magnitude;
        momentumHoldUntil = Time.time + momentumHoldDuration;
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
