using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using WebSwingEscape.Net;

/// <summary>
/// uGUI version of the "waking up the server" overlay. Put this on an always-active
/// Canvas object and give it a child <see cref="panel"/> that it shows / hides in
/// response to <see cref="NetworkClient.PhaseChanged"/>.
///
/// It disables the built-in IMGUI <see cref="NetConnectOverlay"/> at runtime so the
/// two don't stack. See the wiring steps in the PR / commit message.
/// </summary>
public class NetConnectScreen : MonoBehaviour
{
    [Header("Root (child of this object; starts inactive)")]
    [SerializeField] GameObject panel;

    [Header("Card contents")]
    [SerializeField] TMP_Text titleText;
    [SerializeField] TMP_Text bodyText;
    [Tooltip("Small line under the body, e.g. '12s elapsed · attempt 3'. Only shown while waking.")]
    [SerializeField] TMP_Text statusText;
    [Tooltip("Optional image; spun while visible.")]
    [SerializeField] GameObject spinner;
    [SerializeField] float spinnerDegPerSec = 180f;

    [Header("Failed state")]
    [SerializeField] Button retryButton;
    [SerializeField] Button dismissButton;

    [Header("Behaviour")]
    [Tooltip("How long the 'Connected' confirmation stays up before the panel hides.")]
    [SerializeField] float onlineHideDelay = 0.4f;
    [Tooltip("Free the mouse cursor while the panel is up so the buttons are clickable, then restore it.")]
    [SerializeField] bool releaseCursorWhileShown = true;

    NetworkClient _net;
    Coroutine _hideRoutine;
    CursorLockMode _savedLock;
    bool _savedCursorVisible;
    bool _cursorOverridden;

    void Awake()
    {
        _net = NetworkClient.EnsureInstance();

        // We are the connection UI now — turn off the IMGUI fallback.
        _net.autoConnectOverlay = false;
        var imgui = _net.GetComponent<NetConnectOverlay>();
        if (imgui != null) Destroy(imgui);

        if (retryButton != null) retryButton.onClick.AddListener(HandleRetry);
        if (dismissButton != null) dismissButton.onClick.AddListener(Hide);

        if (panel != null) panel.SetActive(false);
    }

    void OnEnable()
    {
        if (_net != null) _net.PhaseChanged += Apply;
        Apply(_net != null ? _net.Phase : NetPhase.Offline);
    }

    void OnDisable()
    {
        if (_net != null) _net.PhaseChanged -= Apply;
        RestoreCursor();
    }

    void HandleRetry()
    {
        if (_net != null) _net.RetryNow();
    }

    void Apply(NetPhase phase)
    {
        if (_hideRoutine != null) { StopCoroutine(_hideRoutine); _hideRoutine = null; }

        switch (phase)
        {
            case NetPhase.Offline:
                Hide();
                break;

            case NetPhase.Connecting:
                Show();
                Set("Connecting…", "Reaching the game server.",
                    status: false, buttons: false, spin: true);
                break;

            case NetPhase.Waking:
                Show();
                Set("Waking up the server…",
                    "The server went to sleep after being idle. The first " +
                    "connection can take up to a minute — hang tight.",
                    status: true, buttons: false, spin: true);
                break;

            case NetPhase.Online:
                Set("Connected", "", status: false, buttons: false, spin: false);
                if (panel != null && panel.activeSelf)
                    _hideRoutine = StartCoroutine(HideAfter(onlineHideDelay));
                else
                    Hide();
                break;

            case NetPhase.Failed:
                Show();
                Set("Can’t reach the server",
                    "It might still be starting up, or it’s offline right now.",
                    status: false, buttons: true, spin: false);
                break;
        }
    }

    void Update()
    {
        if (panel == null || !panel.activeSelf || _net == null) return;

        if (_net.Phase == NetPhase.Waking && statusText != null)
            statusText.text = $"{Mathf.FloorToInt(_net.ConnectElapsed)}s elapsed  ·  attempt {_net.ConnectAttempt}";

        if (spinner != null && spinner.activeSelf)
            spinner.transform.Rotate(0f, 0f, -spinnerDegPerSec * Time.unscaledDeltaTime);
    }

    void Set(string title, string body, bool status, bool buttons, bool spin)
    {
        if (titleText != null) titleText.text = title;
        if (bodyText != null)
        {
            bodyText.text = body;
            bodyText.gameObject.SetActive(!string.IsNullOrEmpty(body));
        }
        if (statusText != null) statusText.gameObject.SetActive(status);
        if (spinner != null) spinner.SetActive(spin);
        if (retryButton != null) retryButton.gameObject.SetActive(buttons);
        if (dismissButton != null) dismissButton.gameObject.SetActive(buttons);
    }

    void Show()
    {
        if (panel == null || panel.activeSelf) return;
        panel.SetActive(true);

        if (releaseCursorWhileShown && !_cursorOverridden)
        {
            _savedLock = Cursor.lockState;
            _savedCursorVisible = Cursor.visible;
            _cursorOverridden = true;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
    }

    void Hide()
    {
        if (panel != null) panel.SetActive(false);
        RestoreCursor();
    }

    void RestoreCursor()
    {
        if (!_cursorOverridden) return;
        Cursor.lockState = _savedLock;
        Cursor.visible = _savedCursorVisible;
        _cursorOverridden = false;
    }

    IEnumerator HideAfter(float seconds)
    {
        yield return new WaitForSecondsRealtime(Mathf.Max(0f, seconds));
        Hide();
        _hideRoutine = null;
    }
}
