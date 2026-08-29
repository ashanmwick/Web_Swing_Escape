using TMPro;
using UnityEngine;
using UnityEngine.UI;
using WebSwingEscape.Progression;

/// <summary>
/// Drives the top-of-screen HUD: Level badge, XP progress bar, Speed and
/// Rebirth labels. Values are hard-coded stand-ins for now (edit them in the
/// Inspector); swap <see cref="Refresh"/>'s inputs for real stats later.
/// </summary>
[ExecuteAlways] // keep the labels in sync while editing so the layout is easy to tune
public class GameHud : MonoBehaviour
{
    [Header("Placeholder stats (replace with real data later)")]
    [SerializeField] int level = 19;
    [Tooltip("Current XP toward the next level.")]
    [SerializeField] float currentXp = 3400f;
    [Tooltip("XP needed to reach the next level.")]
    [SerializeField] float xpForNextLevel = 22000f;
    [Tooltip("Current move speed shown as 'Speed: X'.")]
    [SerializeField] float speed = 3400f;
    [Tooltip("Rebirth bonus in percent, shown as 'Rebirth: +X%'.")]
    [SerializeField] float rebirthBonusPercent = 125f;

    [Header("Scene references")]
    [SerializeField] TMP_Text levelText;      // "Level: 19" on the blue badge
    [SerializeField] TMP_Text progressText;   // "3.4k / 22.0k" over the bar
    [SerializeField] TMP_Text speedText;      // "Speed: 3.4K"
    [SerializeField] TMP_Text rebirthText;    // "Rebirth: +125%"
    [SerializeField] Slider progressBar;      // XP fill (Min 0, Max 1, non-interactable)

    [Header("Live data source (optional — overrides the placeholders in Play mode)")]
    [SerializeField] PlayerProgression progression;
    [SerializeField] RebirthSystem rebirth;

    bool _boundProgression;
    bool _boundRebirth;

    void OnEnable()
    {
        Bind();
        Refresh();
    }

    void OnDisable() => Unbind();

    // Fired whenever a serialized field changes in the Inspector.
    void OnValidate() => Refresh();

    // Subscribe to the progression systems so the HUD refreshes itself from real
    // data at runtime. In edit mode ([ExecuteAlways]) it stays on the Inspector
    // placeholder values.
    void Bind()
    {
        if (!Application.isPlaying) return;

        if (progression == null) progression = FindFirstObjectByType<PlayerProgression>();
        if (rebirth == null) rebirth = FindFirstObjectByType<RebirthSystem>();

        if (progression != null && !_boundProgression)
        {
            progression.OnSpeedChanged += HandleSpeedChanged;
            progression.OnLevelUp += HandleLevelChanged;
            progression.OnLevelChanged += HandleLevelChanged;
            progression.OnCoinsChanged += HandleCoinsChanged;
            _boundProgression = true;
        }

        if (rebirth != null && !_boundRebirth)
        {
            rebirth.OnRebirthMultiplierChanged += HandleMultiplierChanged;
            rebirth.OnRebirth += HandleRebirth;
            _boundRebirth = true;
        }

        PullFromSystems();
    }

    void Unbind()
    {
        if (progression != null && _boundProgression)
        {
            progression.OnSpeedChanged -= HandleSpeedChanged;
            progression.OnLevelUp -= HandleLevelChanged;
            progression.OnLevelChanged -= HandleLevelChanged;
            progression.OnCoinsChanged -= HandleCoinsChanged;
            _boundProgression = false;
        }

        if (rebirth != null && _boundRebirth)
        {
            rebirth.OnRebirthMultiplierChanged -= HandleMultiplierChanged;
            rebirth.OnRebirth -= HandleRebirth;
            _boundRebirth = false;
        }
    }

    void HandleSpeedChanged(double _) => PullFromSystems();
    void HandleCoinsChanged(double _) => PullFromSystems();
    void HandleLevelChanged(int _) => PullFromSystems();
    void HandleMultiplierChanged(double _) => PullFromSystems();
    void HandleRebirth(int _) => PullFromSystems();

    /// <summary>Copies live values off the progression systems into the label fields, then re-formats.</summary>
    void PullFromSystems()
    {
        if (progression != null)
        {
            level = progression.Level;
            currentXp = (float)progression.CurrentLevelXp;
            xpForNextLevel = (float)progression.XpForNextLevel;
            speed = (float)progression.Speed;
        }

        if (rebirth != null)
            rebirthBonusPercent = (float)((rebirth.RebirthMultiplier - 1d) * 100d);

        Refresh();
    }

    /// <summary>Re-formats every label from the current stat values.</summary>
    public void Refresh()
    {
        if (levelText != null)
            levelText.text = $"Level: {level}";

        if (progressText != null)
            progressText.text = $"{Abbreviate(currentXp)} / {Abbreviate(xpForNextLevel)}";

        if (progressBar != null)
            progressBar.value = xpForNextLevel > 0f ? Mathf.Clamp01(currentXp / xpForNextLevel) : 0f;

        if (speedText != null)
            speedText.text = $"Speed: {Abbreviate(speed)}";

        if (rebirthText != null)
            rebirthText.text = $"Rebirth: +{rebirthBonusPercent:0.###}%";
    }

    // 3_400 -> "3.4k", 22_000 -> "22.0k", 1_500_000 -> "1.5M". Keeps the
    // idle-game look from the reference screenshot.
    static string Abbreviate(float value)
    {
        float abs = Mathf.Abs(value);
        if (abs >= 1_000_000_000f) return (value / 1_000_000_000f).ToString("0.0") + "B";
        if (abs >= 1_000_000f)     return (value / 1_000_000f).ToString("0.0") + "M";
        if (abs >= 1_000f)         return (value / 1_000f).ToString("0.0") + "k";
        return value.ToString("0");
    }
}
