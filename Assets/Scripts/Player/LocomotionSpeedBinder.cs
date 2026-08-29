using System.Reflection;
using UnityEngine;
using HeroCharacter;
using WebSwingEscape.Progression;

/// <summary>
/// Bridges the progression layer to the character's real movement speed. Lives on
/// the Player alongside <see cref="HeroCharacterController"/> and
/// <see cref="SpiderSwing"/>.
///
/// It reflects into the hero controller's private <c>movement</c> settings (the
/// same field <see cref="SpiderSwing"/> touches), caches the authored
/// <c>walkingSpeed</c> / <c>sprintingSpeed</c>, then rescales them whenever the
/// player levels up or rebirths:
/// <c>speed = base * clamp((1 + perLevelBonus * (level - 1)) * rebirthMultiplier, 1 .. maxMultiplier)</c>.
///
/// No gameplay or third-party code is modified &mdash; this only observes events
/// and writes the public fields of the movement settings object.
/// </summary>
[RequireComponent(typeof(HeroCharacterController))]
public class LocomotionSpeedBinder : MonoBehaviour
{
    [Header("Refs (auto-resolved if left empty)")]
    [SerializeField] HeroCharacterController hero;
    [SerializeField] PlayerProgression progression;
    [SerializeField] RebirthSystem rebirth;
    [SerializeField] LocomotionScalingData scaling;

    [Header("Options")]
    [Tooltip("Also scale sprint speed. Off = only walk speed grows with progression.")]
    [SerializeField] bool applyToSprint = true;

    [Tooltip("Multiplier units per second eased toward the new target on a level-up / rebirth. 0 = snap instantly.")]
    [SerializeField] float lerpRate = 3f;

    static readonly FieldInfo MovementField =
        typeof(HeroCharacterController).GetField("movement", BindingFlags.NonPublic | BindingFlags.Instance);

    object _movement;
    FieldInfo _walkField;
    FieldInfo _sprintField;
    float _baseWalk;
    float _baseSprint;
    float _currentMult = 1f;
    float _targetMult = 1f;
    bool _bound;

    void Awake()
    {
        if (hero == null) hero = GetComponent<HeroCharacterController>();
        if (progression == null) progression = FindFirstObjectByType<PlayerProgression>();
        if (rebirth == null) rebirth = FindFirstObjectByType<RebirthSystem>();

        if (hero == null || MovementField == null)
        {
            Debug.LogWarning("LocomotionSpeedBinder: HeroCharacterController / 'movement' field not found; disabling.", this);
            enabled = false;
            return;
        }

        _movement = MovementField.GetValue(hero);
        _walkField = _movement?.GetType().GetField("walkingSpeed");
        _sprintField = _movement?.GetType().GetField("sprintingSpeed");

        if (_movement == null || _walkField == null)
        {
            Debug.LogWarning("LocomotionSpeedBinder: movement.walkingSpeed not found; disabling.", this);
            enabled = false;
            return;
        }

        _baseWalk = (float)_walkField.GetValue(_movement);
        if (_sprintField != null) _baseSprint = (float)_sprintField.GetValue(_movement);
        _bound = true;
    }

    void OnEnable()
    {
        if (!_bound) return;

        if (progression != null)
        {
            progression.OnLevelUp += HandleProgressionChanged;
            progression.OnLevelChanged += HandleProgressionChanged;
        }
        if (rebirth != null) rebirth.OnRebirth += HandleProgressionChanged;

        RecomputeTarget();
        _currentMult = _targetMult;   // no easing on first apply
        ApplySpeeds();
    }

    void OnDisable()
    {
        if (progression != null)
        {
            progression.OnLevelUp -= HandleProgressionChanged;
            progression.OnLevelChanged -= HandleProgressionChanged;
        }
        if (rebirth != null) rebirth.OnRebirth -= HandleProgressionChanged;

        // Restore the authored speeds so a disabled binder never leaves the
        // controller permanently boosted.
        if (_bound)
        {
            _currentMult = 1f;
            ApplySpeeds();
        }
    }

    void HandleProgressionChanged(int _) => RecomputeTarget();

    void RecomputeTarget()
    {
        if (!_bound) return;

        int level = progression != null ? progression.Level : 1;
        double rb = rebirth != null
            ? rebirth.RebirthMultiplier
            : progression != null ? progression.RebirthMultiplier : 1d;

        double m = scaling != null
            ? scaling.Multiplier(level, rb)
            : ProgressionMath.LocomotionMultiplier(level, rb, 0.03d, 4d);

        _targetMult = Mathf.Max(1f, (float)m);

        if (lerpRate <= 0f)
        {
            _currentMult = _targetMult;
            ApplySpeeds();
        }
    }

    void Update()
    {
        if (!_bound || Mathf.Approximately(_currentMult, _targetMult)) return;
        _currentMult = Mathf.MoveTowards(_currentMult, _targetMult, lerpRate * Time.deltaTime);
        ApplySpeeds();
    }

    void ApplySpeeds()
    {
        if (!_bound) return;
        _walkField.SetValue(_movement, _baseWalk * _currentMult);
        if (applyToSprint && _sprintField != null)
            _sprintField.SetValue(_movement, _baseSprint * _currentMult);
    }

    /// <summary>Editor diagnostic: dump the resolved refs and live speed values to the Console.</summary>
    [ContextMenu("Log Locomotion State")]
    void LogState()
    {
        if (!_bound)
        {
            Debug.LogWarning("LocomotionSpeedBinder: not bound (reflection failed or component disabled in Awake).", this);
            return;
        }

        int level = progression != null ? progression.Level : -1;
        double rb = rebirth != null ? rebirth.RebirthMultiplier : -1d;
        float liveWalk = (float)_walkField.GetValue(_movement);

        Debug.Log(
            $"[LocomotionSpeedBinder] progression={(progression ? "ok" : "NULL")} rebirth={(rebirth ? "ok" : "NULL")} " +
            $"scaling={(scaling ? scaling.name : "NULL (using fallback)")}\n" +
            $"Level={level} RebirthMult={rb:0.###} -> targetMult={_targetMult:0.###} currentMult={_currentMult:0.###}\n" +
            $"baseWalk={_baseWalk:0.###} liveWalk={liveWalk:0.###} (ratio {liveWalk / Mathf.Max(0.0001f, _baseWalk):0.###})", this);
    }
}
