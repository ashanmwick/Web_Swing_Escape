using System.Reflection;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using HeroCharacter;

/// <summary>
/// Shows the player's <b>real</b> movement speed (metres/second), measured from the
/// Rigidbody every frame, so it reflects everything &mdash; walking, sprinting,
/// swinging, falling &mdash; not the abstract "Speed" progression stat.
///
/// Two output paths, use either or both:
///   * assign <see cref="label"/> to a <see cref="TMP_Text"/> anywhere in a Canvas, and/or
///   * leave <see cref="showOverlay"/> on for a corner IMGUI readout (toggle with <see cref="toggleKey"/>).
///
/// Drop it on any scene object. If <see cref="body"/> is empty it finds the
/// <c>Player</c>-tagged object automatically.
/// </summary>
public class LocomotionSpeedReadout : MonoBehaviour
{
    [Header("Source")]
    [SerializeField] Rigidbody body;
    [SerializeField] string playerTag = "Player";
    [Tooltip("Include the vertical component. Off = horizontal ground speed only.")]
    [SerializeField] bool includeVertical = false;
    [Tooltip("Higher = snappier readout, lower = smoother. 0 = raw, no smoothing.")]
    [SerializeField] float smoothing = 8f;

    [Header("Text output (optional)")]
    [SerializeField] TMP_Text label;
    [SerializeField] string labelFormat = "Speed: {0:0.0} m/s";

    [Header("Overlay")]
    [SerializeField] bool showOverlay = true;
    [SerializeField] Key toggleKey = Key.F4;

    static readonly FieldInfo MovementField =
        typeof(HeroCharacterController).GetField("movement", BindingFlags.NonPublic | BindingFlags.Instance);

    float _speed;         // smoothed, displayed
    float _peak;
    object _movement;
    FieldInfo _walkField;
    FieldInfo _sprintField;
    GUIStyle _style;

    void Awake()
    {
        if (body == null)
        {
            var player = GameObject.FindGameObjectWithTag(playerTag);
            if (player != null) body = player.GetComponent<Rigidbody>();
        }

        var hero = body != null ? body.GetComponent<HeroCharacterController>() : null;
        if (hero != null && MovementField != null)
        {
            _movement = MovementField.GetValue(hero);
            _walkField = _movement?.GetType().GetField("walkingSpeed");
            _sprintField = _movement?.GetType().GetField("sprintingSpeed");
        }
    }

    void Update()
    {
        var keyboard = Keyboard.current;
        if (keyboard != null && (int)toggleKey > 0 && (int)toggleKey < Keyboard.KeyCount
            && keyboard[toggleKey].wasPressedThisFrame)
        {
            showOverlay = !showOverlay;
        }

        if (body == null) return;

        Vector3 v = body.linearVelocity;              // Unity 6: linearVelocity, not velocity
        if (!includeVertical) v.y = 0f;
        float raw = v.magnitude;

        _speed = smoothing > 0f
            ? Mathf.Lerp(_speed, raw, 1f - Mathf.Exp(-smoothing * Time.deltaTime))
            : raw;

        if (_speed > _peak) _peak = _speed;

        if (label != null) label.text = string.Format(labelFormat, _speed);
    }

    float ConfiguredWalk => _walkField != null ? (float)_walkField.GetValue(_movement) : -1f;
    float ConfiguredSprint => _sprintField != null ? (float)_sprintField.GetValue(_movement) : -1f;

    void OnGUI()
    {
        if (!showOverlay) return;
        _style ??= new GUIStyle(GUI.skin.label) { fontSize = 13, richText = true };

        var sb = new System.Text.StringBuilder();
        if (body == null)
        {
            sb.AppendLine($"<b>SPEED</b>  no Rigidbody (tag '{playerTag}')");
        }
        else
        {
            sb.AppendLine($"<b>SPEED</b>  <b>{_speed:0.00}</b> m/s   peak {_peak:0.00}");
            if (_walkField != null)
                sb.AppendLine($"cfg walk {ConfiguredWalk:0.00}   sprint {ConfiguredSprint:0.00} m/s");
        }

        int lines = _walkField != null && body != null ? 2 : 1;
        var rect = new Rect(Screen.width - 268, 8, 260, 12 + 16 * lines);
        GUI.Box(rect, GUIContent.none);
        GUI.Label(new Rect(rect.x + 8, rect.y + 6, rect.width - 12, rect.height), sb.ToString(), _style);
    }

    /// <summary>Editor helper: clear the recorded peak speed.</summary>
    [ContextMenu("Reset Peak")]
    void ResetPeak() => _peak = 0f;
}
