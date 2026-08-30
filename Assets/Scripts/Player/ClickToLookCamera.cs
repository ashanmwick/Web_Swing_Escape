using UnityEngine;
using UnityEngine.InputSystem;
using HeroCharacter;

/// <summary>
/// Click-to-look third-person camera scheme layered on top of <see cref="HeroCharacterController"/>.
///
/// <para>The hero controller normally feeds the "Look" action into the camera every frame, so
/// the camera orbits the player continuously with the mouse. Here we keep that action
/// <b>disabled by default</b>: the camera then simply trails the player at whatever yaw/pitch it
/// last had. At scene start that means it sits behind the player, and WASD moves the player
/// without the camera turning.</para>
///
/// <para>A single left click <b>enters look mode</b>: we re-enable the action and lock/hide the
/// cursor, so the camera now moves with every mouse movement. Look mode stays on until the
/// player <b>exits</b> it &#8211; press <c>Esc</c> (on WebGL the browser also drops pointer lock
/// on Esc), or click again. On exit the action is disabled and the camera simply holds its
/// current angle; the cursor is freed so on-screen UI stays clickable.</para>
///
/// <para>Coexists with <see cref="CameraLookToggle"/> (Tab free-cursor mode): while the hero
/// controller is disabled by that toggle this component stays out of the way.</para>
///
/// Non-invasive: only toggles the existing "Look" input action and the cursor state. The hero
/// controller and the swing code are untouched.
/// </summary>
public class ClickToLookCamera : MonoBehaviour
{
    [Tooltip("Hero controller that owns the camera. Auto-found on this object or its children if left empty.")]
    [SerializeField] HeroCharacterController hero;

    [Tooltip("PlayerInput that holds the 'Look' action. Auto-found on this object or its children if left empty.")]
    [SerializeField] PlayerInput playerInput;

    [Tooltip("Name of the camera-look action on the PlayerInput asset.")]
    [SerializeField] string lookActionName = "Look";

    [Tooltip("Use the RIGHT mouse button instead of the left to enter look mode.")]
    [SerializeField] bool useRightMouseButton = false;

    [Tooltip("A second click (while already in look mode) exits it. Off: only Esc / lost pointer lock exits.")]
    [SerializeField] bool clickAgainToExit = true;

    [Tooltip("Lock and hide the OS cursor while in look mode (recommended for WebGL).")]
    [SerializeField] bool lockCursorWhileLooking = true;

    InputAction lookAction;
    bool looking;
    // Pointer lock is granted asynchronously on WebGL, so only treat "lock lost" as an
    // exit once we have actually seen it become Locked at least once this session.
    bool lockConfirmed;

    void Awake()
    {
        if (hero == null) hero = GetComponentInChildren<HeroCharacterController>();
        if (playerInput == null) playerInput = GetComponentInChildren<PlayerInput>();
        ResolveLookAction();
    }

    void OnDisable()
    {
        looking = false;
        lockConfirmed = false;
        SetLookEnabled(false);
    }

    // LateUpdate so we run after HeroCharacterController.Update (which re-enables the Look
    // action and re-locks the cursor whenever it is enabled) and win the frame.
    void LateUpdate()
    {
        if (lookAction == null) ResolveLookAction();

        // While the hero controller is off (e.g. CameraLookToggle's free-cursor mode) leave
        // the cursor and the Look action to whoever owns that mode.
        if (hero == null || !hero.isActiveAndEnabled)
        {
            looking = false;
            return;
        }

        if (looking && lockCursorWhileLooking && Cursor.lockState == CursorLockMode.Locked)
        {
            lockConfirmed = true;
        }

        bool clicked = LookButtonPressedThisFrame();
        bool exitRequested = EscPressedThisFrame() ||
                             (looking && clickAgainToExit && clicked) ||
                             // WebGL / Editor: Esc drops pointer lock outside our control.
                             (looking && lockConfirmed && Cursor.lockState != CursorLockMode.Locked);

        if (!looking && clicked)
        {
            looking = true;
            lockConfirmed = false;
            if (lockCursorWhileLooking)
            {
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
            }
        }
        else if (looking && exitRequested)
        {
            looking = false;
            lockConfirmed = false;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        // Not looking: keep the cursor free so UI stays clickable. The hero controller
        // re-locks it in its OnEnable, so re-assert here.
        if (!looking && Cursor.lockState != CursorLockMode.None)
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        SetLookEnabled(looking);
    }

    void ResolveLookAction()
    {
        if (playerInput != null && playerInput.actions != null)
        {
            lookAction = playerInput.actions.FindAction(lookActionName, throwIfNotFound: false);
        }
    }

    bool LookButtonPressedThisFrame()
    {
        var mouse = Mouse.current;
        if (mouse == null) return false;
        return useRightMouseButton
            ? mouse.rightButton.wasPressedThisFrame
            : mouse.leftButton.wasPressedThisFrame;
    }

    static bool EscPressedThisFrame()
    {
        var keyboard = Keyboard.current;
        return keyboard != null && keyboard.escapeKey.wasPressedThisFrame;
    }

    void SetLookEnabled(bool enabled)
    {
        if (lookAction == null) return;
        if (enabled)
        {
            if (!lookAction.enabled) lookAction.Enable();
        }
        else if (lookAction.enabled)
        {
            lookAction.Disable();
        }
    }
}
