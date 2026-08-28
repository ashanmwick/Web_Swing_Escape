using UnityEngine;
using UnityEngine.InputSystem;

namespace WebSwingEscape.Net
{
    /// <summary>
    /// Tiny on-screen readout for testing: connection state, zone, session id,
    /// round-trip time and the list of players the client currently knows about.
    /// Drop it on any GameObject in the scene. Remove for production.
    /// </summary>
    public class NetDebugHud : MonoBehaviour
    {
        [SerializeField] bool show = true;
        [SerializeField] Key toggleKey = Key.F3;
        [SerializeField] float pingInterval = 1f;

        float _nextPing;
        GUIStyle _style;

        static bool IsValid(Key k) => (int)k > 0 && (int)k < Keyboard.KeyCount;

        void Awake()
        {
            // Heal a stale value serialized before this field was a `Key`
            // (e.g. a leftover KeyCode int like 284).
            if (!IsValid(toggleKey)) toggleKey = Key.F3;
        }

        void Update()
        {
            var keyboard = Keyboard.current;
            if (keyboard != null && IsValid(toggleKey)
                && keyboard[toggleKey].wasPressedThisFrame)
            {
                show = !show;
            }

            var net = NetworkClient.Instance;
            if (net != null && net.IsConnected && Time.unscaledTime >= _nextPing)
            {
                _nextPing = Time.unscaledTime + Mathf.Max(0.25f, pingInterval);
                net.SendPing();
            }
        }

        void OnGUI()
        {
            if (!show) return;
            _style ??= new GUIStyle(GUI.skin.label) { fontSize = 13, richText = true };

            var net = NetworkClient.Instance;
            var sb = new System.Text.StringBuilder();
            if (net == null)
            {
                sb.AppendLine("<b>NET</b>  no NetworkClient");
            }
            else if (!net.IsConnected)
            {
                sb.AppendLine($"<b>NET</b>  connecting to {net.endpoint} ...");
            }
            else
            {
                sb.AppendLine($"<b>NET</b>  zone=<b>{net.Zone}</b>  session={net.SessionId}  rtt={net.LastRoundTripMs}ms");
                sb.AppendLine($"players ({net.Snapshots.Count}):");
                foreach (var kv in net.Snapshots)
                {
                    var s = kv.Value;
                    string me = kv.Key == net.SessionId ? "  (me)" : "";
                    sb.AppendLine($"  {s.name}{me}  ({s.position.x:0.0}, {s.position.y:0.0}, {s.position.z:0.0})  " +
                                  $"{(s.swinging ? "swinging" : "")}  coins={s.coins}");
                }
            }

            GUI.Box(new Rect(8, 8, 430, 26 + 16 * (net != null && net.IsConnected ? net.Snapshots.Count + 2 : 1)), GUIContent.none);
            GUI.Label(new Rect(16, 12, 420, 600), sb.ToString(), _style);
        }
    }
}
