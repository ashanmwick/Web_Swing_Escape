using UnityEngine;

namespace WebSwingEscape.Net
{
    /// <summary>
    /// Minimal full-screen overlay that covers the gap while the Colyseus server
    /// cold-starts. Free hosts (Render's free tier) spin the server down after
    /// ~15 min idle, so the first join of the day waits 30-60s for it to boot.
    /// <see cref="NetworkClient"/> keeps retrying the join; this just explains the
    /// wait and offers a Retry once it finally gives up.
    ///
    /// Auto-added to the <c>~NetworkClient</c> object, so it needs no Canvas and
    /// no scene wiring (same idea as <see cref="NetDebugHud"/>). Delete it for a
    /// production build, or replace with a real uGUI screen.
    /// </summary>
    public class NetConnectOverlay : MonoBehaviour
    {
        [Tooltip("Master switch. Turned back on automatically whenever a new connect sequence starts.")]
        [SerializeField] bool show = true;
        [Tooltip("Seconds to keep the 'Connected' flash on screen before hiding.")]
        [SerializeField] float onlineFlashSeconds = 0.4f;
        [Tooltip("Dim strength behind the card (0 = clear, 1 = black).")]
        [Range(0f, 1f)][SerializeField] float dim = 0.6f;

        NetworkClient _net;
        GUIStyle _title, _body;
        float _onlineSince = -1f;

        void OnEnable()
        {
            _net = NetworkClient.Instance;
            if (_net != null) _net.PhaseChanged += OnPhaseChanged;
        }

        void OnDisable()
        {
            if (_net != null) _net.PhaseChanged -= OnPhaseChanged;
        }

        // Re-arm the overlay each time a fresh connect sequence begins so a manual
        // Dismiss during one scene doesn't suppress it forever.
        void OnPhaseChanged(NetPhase p)
        {
            if (p == NetPhase.Connecting) show = true;
            if (p != NetPhase.Online) _onlineSince = -1f;
        }

        void OnGUI()
        {
            if (!show) return;
            var net = _net ?? NetworkClient.Instance;
            if (net == null) return;

            var phase = net.Phase;
            if (phase == NetPhase.Offline) return;

            if (phase == NetPhase.Online)
            {
                if (_onlineSince < 0f) _onlineSince = Time.realtimeSinceStartup;
                if (Time.realtimeSinceStartup - _onlineSince > Mathf.Max(0f, onlineFlashSeconds)) return;
            }

            EnsureStyles();

            // --- dim the whole screen so the card reads as modal ---
            var prev = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, dim);
            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Texture2D.whiteTexture);
            GUI.color = prev;

            // --- centered card ---
            const float w = 440f, h = 190f;
            var card = new Rect((Screen.width - w) * 0.5f, (Screen.height - h) * 0.5f, w, h);
            GUI.Box(card, GUIContent.none);

            GUILayout.BeginArea(new Rect(card.x + 26f, card.y + 22f, w - 52f, h - 44f));

            string dots = new string('.', 1 + (int)(Time.realtimeSinceStartup * 2f) % 3);

            switch (phase)
            {
                case NetPhase.Connecting:
                    GUILayout.Label("Connecting" + dots, _title);
                    GUILayout.Space(10f);
                    GUILayout.Label("Reaching the game server.", _body);
                    break;

                case NetPhase.Waking:
                    GUILayout.Label("Waking up the server" + dots, _title);
                    GUILayout.Space(10f);
                    GUILayout.Label(
                        "The server went to sleep after being idle. The first " +
                        "connection can take up to a minute — hang tight.", _body);
                    GUILayout.Space(8f);
                    GUILayout.Label($"{Mathf.FloorToInt(net.ConnectElapsed)}s elapsed  ·  attempt {net.ConnectAttempt}", _body);
                    break;

                case NetPhase.Online:
                    GUILayout.Label("Connected", _title);
                    break;

                case NetPhase.Failed:
                    GUILayout.Label("Can't reach the server", _title);
                    GUILayout.Space(10f);
                    GUILayout.Label("It might still be starting up, or it's offline right now.", _body);
                    GUILayout.Space(14f);
                    GUILayout.BeginHorizontal();
                    if (GUILayout.Button("Retry", GUILayout.Height(30f), GUILayout.Width(120f)))
                        net.RetryNow();
                    GUILayout.Space(10f);
                    if (GUILayout.Button("Dismiss", GUILayout.Height(30f), GUILayout.Width(120f)))
                        show = false;
                    GUILayout.EndHorizontal();
                    break;
            }

            GUILayout.EndArea();
        }

        void EnsureStyles()
        {
            _title ??= new GUIStyle(GUI.skin.label)
            {
                fontSize = 18,
                fontStyle = FontStyle.Bold,
                wordWrap = true,
            };
            _body ??= new GUIStyle(GUI.skin.label)
            {
                fontSize = 13,
                wordWrap = true,
            };
        }
    }
}
