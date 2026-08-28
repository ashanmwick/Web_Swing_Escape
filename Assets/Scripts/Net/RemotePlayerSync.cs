using UnityEngine;

namespace WebSwingEscape.Net
{
    /// <summary>
    /// Put this on the root of the RemotePlayer prefab (a copy of the player
    /// avatar with HeroCharacterController, SpiderSwing, PlayerInput, the camera
    /// and the AudioListener all removed). It follows one server-side player by
    /// session id, smoothing position / rotation and drawing the web line while
    /// that player is swinging.
    /// </summary>
    public class RemotePlayerSync : MonoBehaviour
    {
        [Header("Smoothing")]
        [Tooltip("Higher = snappier / less lag, lower = smoother / more floaty.")]
        public float positionLerp = 14f;
        public float rotationLerp = 12f;
        [Tooltip("If the target jumps further than this in one update, teleport instead of sliding.")]
        public float teleportDistance = 25f;

        [Header("Optional visuals")]
        [Tooltip("Animator to feed a movement speed into (leave blank to skip).")]
        [SerializeField] Animator animator;
        [SerializeField] string animatorSpeedParam = "Speed";
        [Tooltip("LineRenderer used to draw this remote player's web (a child of the prefab).")]
        [SerializeField] LineRenderer web;
        [Tooltip("Where the web line starts on the avatar (a hand bone). Falls back to this transform.")]
        [SerializeField] Transform webOrigin;
        [Tooltip("Optional TextMeshPro / TextMesh above the head for the player name.")]
        [SerializeField] Component nameLabel;

        string _sessionId;
        Vector3 _lastPos;
        bool _hasName;
        int _speedHash;

        public string SessionId => _sessionId;

        public void Bind(string sessionId)
        {
            _sessionId = sessionId;
            if (!string.IsNullOrEmpty(animatorSpeedParam))
                _speedHash = Animator.StringToHash(animatorSpeedParam);

            if (NetworkClient.Instance != null &&
                NetworkClient.Instance.TryGetSnapshot(sessionId, out var s))
            {
                transform.SetPositionAndRotation(s.position, Quaternion.Euler(0f, s.yaw, 0f));
                _lastPos = s.position;
                ApplyName(s.name);
            }
            if (web != null) web.enabled = false;
        }

        void Update()
        {
            var net = NetworkClient.Instance;
            if (net == null || _sessionId == null) return;
            if (!net.TryGetSnapshot(_sessionId, out var s)) return;

            // Position
            if ((s.position - transform.position).sqrMagnitude > teleportDistance * teleportDistance)
                transform.position = s.position;
            else
                transform.position = Vector3.Lerp(transform.position, s.position,
                    1f - Mathf.Exp(-positionLerp * Time.deltaTime));

            // Yaw
            var targetRot = Quaternion.Euler(0f, s.yaw, 0f);
            transform.rotation = Quaternion.Slerp(transform.rotation, targetRot,
                1f - Mathf.Exp(-rotationLerp * Time.deltaTime));

            // Animator speed (planar), derived from how fast the avatar is actually moving
            if (animator != null && _speedHash != 0)
            {
                Vector3 delta = transform.position - _lastPos;
                delta.y = 0f;
                float speed = delta.magnitude / Mathf.Max(Time.deltaTime, 1e-4f);
                animator.SetFloat(_speedHash, speed);
            }
            _lastPos = transform.position;

            // Web line
            if (web != null)
            {
                if (s.swinging)
                {
                    if (web.positionCount < 2) web.positionCount = 2;
                    web.enabled = true;
                    web.SetPosition(0, webOrigin != null ? webOrigin.position : transform.position);
                    web.SetPosition(1, s.anchor);
                }
                else if (web.enabled)
                {
                    web.enabled = false;
                }
            }

            if (!_hasName) ApplyName(s.name);
        }

        void ApplyName(string value)
        {
            if (string.IsNullOrEmpty(value) || nameLabel == null) return;
            switch (nameLabel)
            {
                case TextMesh tm: tm.text = value; break;
                default:
                    // TMP_Text (TextMeshPro / TextMeshProUGUI) via reflection so this
                    // script doesn't hard-depend on the TMP assembly.
                    var prop = nameLabel.GetType().GetProperty("text");
                    prop?.SetValue(nameLabel, value);
                    break;
            }
            _hasName = true;
        }
    }
}
