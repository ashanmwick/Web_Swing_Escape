using System.Collections.Generic;
using HeroCharacter;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Escape-key pause menu with just <b>Resume</b> and <b>Exit</b>. The whole thing
/// — canvas, background dim, card, buttons, and an EventSystem if the scene has
/// none — is built in code at runtime, so there is nothing to wire in the Editor.
///
/// It auto-installs itself into every scene via <see cref="Bootstrap"/> (same
/// trick the Colyseus SDK and <c>NetConnectOverlay</c> use), survives scene loads
/// with <see cref="Object.DontDestroyOnLoad"/>, and pauses the game with
/// <see cref="Time.timeScale"/> while open.
///
/// "Exit" returns to the Lobby from any other scene; if you are already in the
/// Lobby it quits the application (a no-op in the Editor / harmless on WebGL,
/// where it just stops the player loop).
/// </summary>
public class PauseMenu : MonoBehaviour
{
    const string LobbySceneName = "Lobby";

    static PauseMenu _instance;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        if (_instance != null) return;
        var go = new GameObject("~PauseMenu");
        _instance = go.AddComponent<PauseMenu>();
        DontDestroyOnLoad(go);
    }

    bool _open;
    GameObject _root;          // the dim + card, toggled on/off

    // saved state so Resume can put things back exactly
    float _savedTimeScale = 1f;
    CursorLockMode _savedLock;
    bool _savedCursorVisible;
    readonly List<Behaviour> _suspendedControllers = new();

    void Awake()
    {
        if (_instance != null && _instance != this) { Destroy(gameObject); return; }
        _instance = this;
        BuildUi();
        SetOpen(false);
    }

    void OnDestroy()
    {
        if (_instance == this) _instance = null;
    }

    void Update()
    {
        var kb = Keyboard.current;
        if (kb != null && kb.escapeKey.wasPressedThisFrame)
            SetOpen(!_open);
    }

    // ---------------------------------------------------------------- actions

    void Resume() => SetOpen(false);

    void Exit()
    {
        // Always restore the clock before we leave, or the next scene loads paused.
        Time.timeScale = 1f;
        _open = false;

        if (SceneManager.GetActiveScene().name == LobbySceneName)
        {
            Application.Quit();
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#endif
            return;
        }

        if (SceneLoader.Instance != null) SceneLoader.Instance.LoadLobby();
        else SceneManager.LoadScene(LobbySceneName);
    }

    // ---------------------------------------------------------------- open / close

    void SetOpen(bool open)
    {
        _open = open;
        if (_root != null) _root.SetActive(open);

        if (open)
        {
            _savedTimeScale = Time.timeScale;
            _savedLock = Cursor.lockState;
            _savedCursorVisible = Cursor.visible;

            Time.timeScale = 0f;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;

            // Stop camera-look / locomotion so clicks land on the menu, not the world.
            _suspendedControllers.Clear();
            var player = GameObject.FindGameObjectWithTag("Player");
            if (player != null)
            {
                foreach (var c in player.GetComponentsInChildren<HeroCharacterController>(true))
                    if (c != null && c.enabled) { c.enabled = false; _suspendedControllers.Add(c); }
            }

            if (EventSystem.current != null)
                EventSystem.current.SetSelectedGameObject(_resumeButton);
        }
        else
        {
            Time.timeScale = Mathf.Approximately(_savedTimeScale, 0f) ? 1f : _savedTimeScale;

            bool restoredAController = _suspendedControllers.Count > 0;
            foreach (var c in _suspendedControllers)
                if (c != null) c.enabled = true;
            _suspendedControllers.Clear();

            // A re-enabled controller re-locks the cursor in its own OnEnable; only
            // restore the cursor by hand when nothing took ownership back.
            if (!restoredAController)
            {
                Cursor.lockState = _savedLock;
                Cursor.visible = _savedCursorVisible;
            }
        }
    }

    // ---------------------------------------------------------------- UI construction

    GameObject _resumeButton;

    void BuildUi()
    {
        EnsureEventSystem();

        // Canvas -------------------------------------------------------------
        var canvasGo = new GameObject("PauseCanvas",
            typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        canvasGo.transform.SetParent(transform, false);
        var canvas = canvasGo.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 1000; // above HUD / connect screen
        var scaler = canvasGo.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        // Root (dim background, full screen) -------------------------------
        _root = new GameObject("Root", typeof(Image));
        _root.transform.SetParent(canvasGo.transform, false);
        Stretch(_root.GetComponent<RectTransform>());
        _root.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.72f);

        // Card ------------------------------------------------------------
        var card = new GameObject("Card", typeof(Image), typeof(VerticalLayoutGroup),
            typeof(ContentSizeFitter));
        card.transform.SetParent(_root.transform, false);
        var cardRt = card.GetComponent<RectTransform>();
        cardRt.anchorMin = cardRt.anchorMax = new Vector2(0.5f, 0.5f);
        cardRt.pivot = new Vector2(0.5f, 0.5f);
        cardRt.sizeDelta = new Vector2(460f, 0f);
        card.GetComponent<Image>().color = new Color(0.10f, 0.11f, 0.13f, 0.98f);
        var vlg = card.GetComponent<VerticalLayoutGroup>();
        vlg.padding = new RectOffset(40, 40, 36, 36);
        vlg.spacing = 18f;
        vlg.childAlignment = TextAnchor.MiddleCenter;
        vlg.childControlWidth = true;
        vlg.childControlHeight = true;
        vlg.childForceExpandWidth = true;
        vlg.childForceExpandHeight = false;
        var fit = card.GetComponent<ContentSizeFitter>();
        fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        AddLabel(card.transform, "Paused", 44f, FontStyles.Bold, new Color(1f, 1f, 1f, 1f), 64f);

        _resumeButton = AddButton(card.transform, "Resume",
            new Color(0.20f, 0.55f, 0.95f, 1f), Resume);
        AddButton(card.transform, "Exit",
            new Color(0.32f, 0.34f, 0.40f, 1f), Exit);
    }

    void EnsureEventSystem()
    {
        if (EventSystem.current != null) return;
        var es = new GameObject("EventSystem", typeof(EventSystem),
            typeof(InputSystemUIInputModule));
        DontDestroyOnLoad(es);
    }

    static void AddLabel(Transform parent, string text, float size, FontStyles style,
        Color color, float height)
    {
        var go = new GameObject("Label", typeof(TextMeshProUGUI), typeof(LayoutElement));
        go.transform.SetParent(parent, false);
        var t = go.GetComponent<TextMeshProUGUI>();
        t.text = text;
        t.fontSize = size;
        t.fontStyle = style;
        t.color = color;
        t.alignment = TextAlignmentOptions.Center;
        go.GetComponent<LayoutElement>().minHeight = height;
    }

    GameObject AddButton(Transform parent, string label, Color bg,
        UnityEngine.Events.UnityAction onClick)
    {
        var go = new GameObject($"{label}Button", typeof(Image), typeof(Button),
            typeof(LayoutElement));
        go.transform.SetParent(parent, false);
        go.GetComponent<Image>().color = bg;
        go.GetComponent<LayoutElement>().minHeight = 68f;

        var btn = go.GetComponent<Button>();
        btn.targetGraphic = go.GetComponent<Image>();
        var colors = btn.colors;
        colors.highlightedColor = new Color(1f, 1f, 1f, 1f);
        colors.pressedColor = new Color(0.85f, 0.85f, 0.85f, 1f);
        colors.fadeDuration = 0.08f;
        btn.colors = colors;
        // We run at timeScale 0, so the click event still needs to fire — Buttons
        // use unscaled time for their transition, and onClick is not time-gated.
        btn.onClick.AddListener(onClick);

        var txtGo = new GameObject("Text", typeof(TextMeshProUGUI));
        txtGo.transform.SetParent(go.transform, false);
        Stretch(txtGo.GetComponent<RectTransform>());
        var t = txtGo.GetComponent<TextMeshProUGUI>();
        t.text = label;
        t.fontSize = 30f;
        t.fontStyle = FontStyles.Bold;
        t.color = Color.white;
        t.alignment = TextAlignmentOptions.Center;

        return go;
    }

    static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }
}
