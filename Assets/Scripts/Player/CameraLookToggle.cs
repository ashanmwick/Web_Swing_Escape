using UnityEngine;
using UnityEngine.InputSystem;
using HeroCharacter;

/// <summary>
/// Lets the player toggle between "camera look" mode (mouse drives the third-person
/// camera, cursor hidden and locked) and "mouse" mode (cursor free and visible so the
/// player can click UI on screen). Press the toggle key once to leave camera look,
/// press it again to return.
/// </summary>
public class CameraLookToggle : MonoBehaviour
{
    [Tooltip("The HeroCharacterController that owns the camera look. Auto-found on this object or its children if left empty.")]
    [SerializeField] HeroCharacterController characterController;

    [Tooltip("Key that switches between camera look and free mouse cursor.")]
    [SerializeField] Key toggleKey = Key.Tab;

    [Tooltip("Start the scene in free-cursor (mouse) mode instead of camera look.")]
    [SerializeField] bool startInMouseMode = false;

    bool mouseMode;

    void Awake()
    {
        if (characterController == null)
        {
            characterController = GetComponentInChildren<HeroCharacterController>();
        }
    }

    void OnEnable()
    {
        SetMouseMode(startInMouseMode);
    }

    void Update()
    {
        var keyboard = Keyboard.current;
        if (keyboard != null && keyboard[toggleKey].wasPressedThisFrame)
        {
            SetMouseMode(!mouseMode);
        }

        // Keep the cursor free while in mouse mode; the browser / window focus can
        // silently re-lock it, so re-assert every frame.
        if (mouseMode && Cursor.lockState != CursorLockMode.None)
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
    }

    /// <summary>Public so it can also be wired to an on-screen button's OnClick.</summary>
    public void ToggleMouseMode()
    {
        SetMouseMode(!mouseMode);
    }

    public void SetMouseMode(bool enabled)
    {
        mouseMode = enabled;

        // Disabling the controller stops camera look and locomotion; its own
        // OnDisable frees the cursor. Re-enabling restores look and re-locks the
        // cursor via its OnEnable.
        if (characterController != null)
        {
            characterController.enabled = !mouseMode;
        }

        if (mouseMode)
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
    }
}
