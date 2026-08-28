using System;
using System.Collections.Generic;
using UnityEngine;

namespace WebSwingEscape.Net
{
    /// <summary>
    /// Put this on the root of the RemotePlayer prefab (a copy of the player
    /// avatar with HeroCharacterController, SpiderSwing, PlayerInput, the camera
    /// and the AudioListener all removed). It follows one server-side player by
    /// session id using a small interpolation buffer: incoming states are stored
    /// with a timestamp and the avatar is rendered ~interpolationDelay seconds in
    /// the past, sliding smoothly between the two states that straddle that time.
    /// </summary>
    public class RemotePlayerSync : MonoBehaviour
    {
        [Header("Interpolation")]
        [Tooltip("How far in the past remote players are rendered, in seconds. " +
                 "Use ~2x the sender's interval (0.10 for 20 Hz). Bigger = smoother but more visual latency.")]
        public float interpolationDelay = 0.12f;
        [Tooltip("Max seconds to extrapolate forward when the next snapshot is late.")]
        public float maxExtrapolation = 0.20f;
        [Tooltip("If a fresh state is further than this from the last one, snap (teleport) instead of sliding.")]
        public float teleportDistance = 25f;
        [Tooltip("Gap between snapshots (seconds) above which we treat the player as having been idle and just resumed.")]
        public float staleGap = 0.5f;

        [Header("Optional visuals")]
        [Tooltip("Animator to feed a movement speed into (leave blank to skip).")]
        [SerializeField] Animator animator;
        [SerializeField] string animatorSpeedParam = "Speed";
        [Tooltip("Seconds of damping applied to the animator Speed parameter.")]
        [SerializeField] float animatorDamp = 0.12f;
        [Tooltip("LineRenderer used to draw this remote player's web (a child of the prefab).")]
        [SerializeField] LineRenderer web;
        [Tooltip("Where the web line starts on the avatar (a hand bone). Falls back to this transform.")]
        [SerializeField] Transform webOrigin;
        [Tooltip("Optional TextMeshPro / TextMesh above the head for the player name.")]
        [SerializeField] Component nameLabel;

        struct Sample
        {
            public double t;          // client time this state is 'for'
            public Vector3 pos;
            public float yaw;
            public bool swinging;
            public Vector3 anchor;
        }

        readonly List<Sample> _buf = new List<Sample>(32);
        string _sessionId;
        int _speedHash;
        bool _hasName;
        Vector3 _lastRenderPos;

        public string SessionId => _sessionId;

        static double Now => Time.timeAsDouble;   // Unity 2020.2+. Older: use (double)Time.unscaledTime

        public void Bind(string sessionId)
        {
            _sessionId = sessionId;
            if (!string.IsNullOrEmpty(animatorSpeedParam))
                _speedHash = Animator.StringToHash(animatorSpeedParam);

            _buf.Clear();
            if (NetworkClient.Instance != null &&
                NetworkClient.Instance.TryGetSnapshot(sessionId, out var s))
            {
                transform.SetPositionAndRotation(s.position, Quaternion.Euler(0f, s.yaw, 0f));
                _lastRenderPos = s.position;
                _buf.Add(ToSample(s, Now));
                ApplyName(s.name);
            }
            if (web != null) web.enabled = false;
        }

        static Sample ToSample(in PlayerSnapshot s, double now) => new Sample
        {
            t = now, pos = s.position, yaw = s.yaw, swinging = s.swinging, anchor = s.anchor
        };

        void Update()
        {
            var net = NetworkClient.Instance;
            if (net == null || _sessionId == null) return;
            if (!net.TryGetSnapshot(_sessionId, out var s)) return;

            double now = Now;
            IngestSnapshot(s, now);

            if (!_hasName) ApplyName(s.name);
            if (_buf.Count == 0) return;

            double renderTime = now - interpolationDelay;
            EvaluateAt(renderTime, out var pos, out var yaw, out var swinging, out var anchor);

            // Teleport on a huge jump between where we are and where we should be.
            if ((pos - transform.position).sqrMagnitude > teleportDistance * teleportDistance)
                _lastRenderPos = pos;

            transform.SetPositionAndRotation(pos, Quaternion.Euler(0f, yaw, 0f));

            // Animator speed, from the *rendered* (already smooth) planar motion, damped.
            if (animator != null && _speedHash != 0)
            {
                Vector3 d = transform.position - _lastRenderPos;
                d.y = 0f;
                float target = d.magnitude / Mathf.Max(Time.deltaTime, 1e-4f);
                animator.SetFloat(_speedHash, target, animatorDamp, Time.deltaTime);
            }
            _lastRenderPos = transform.position;

            // Web line
            if (web != null)
            {
                if (swinging)
                {
                    if (web.positionCount < 2) web.positionCount = 2;
                    web.enabled = true;
                    web.SetPosition(0, webOrigin != null ? webOrigin.position : transform.position);
                    web.SetPosition(1, anchor);
                }
                else if (web.enabled)
                {
                    web.enabled = false;
                }
            }
        }

        /// <summary>Append a buffer sample only when the server value actually advanced.</summary>
        void IngestSnapshot(in PlayerSnapshot s, double now)
        {
            if (_buf.Count > 0)
            {
                var last = _buf[_buf.Count - 1];
                bool moved = (s.position - last.pos).sqrMagnitude > 1e-4f ||
                             Mathf.Abs(Mathf.DeltaAngle(s.yaw, last.yaw)) > 0.05f ||
                             s.swinging != last.swinging;
                if (!moved) return;

                // Idle -> resumed: drop stale history so we don't lerp across a long gap.
                if (now - last.t > staleGap)
                    _buf.Clear();
            }

            _buf.Add(ToSample(s, now));

            // Keep ~1s of history.
            double cutoff = now - 1.0;
            int drop = 0;
            while (drop + 1 < _buf.Count && _buf[drop + 1].t < cutoff) drop++;
            if (drop > 0) _buf.RemoveRange(0, drop);
        }

        void EvaluateAt(double t, out Vector3 pos, out float yaw, out bool swinging, out Vector3 anchor)
        {
            // Older than everything we have -> clamp to the oldest.
            if (t <= _buf[0].t)
            {
                var o = _buf[0];
                pos = o.pos; yaw = o.yaw; swinging = o.swinging; anchor = o.anchor;
                return;
            }

            // Between two samples -> interpolate.
            for (int i = 0; i < _buf.Count - 1; i++)
            {
                Sample a = _buf[i], b = _buf[i + 1];
                if (t >= a.t && t <= b.t)
                {
                    float u = (float)((t - a.t) / Math.Max(b.t - a.t, 1e-4));
                    pos = Vector3.LerpUnclamped(a.pos, b.pos, u);
                    yaw = Mathf.LerpAngle(a.yaw, b.yaw, u);
                    swinging = u < 0.5f ? a.swinging : b.swinging;
                    anchor = Vector3.Lerp(a.anchor, b.anchor, u);
                    return;
                }
            }

            // Newer than the last sample -> extrapolate briefly from the last two.
            Sample p1 = _buf[_buf.Count - 1];
            if (_buf.Count >= 2)
            {
                Sample p0 = _buf[_buf.Count - 2];
                float dt = (float)Math.Max(p1.t - p0.t, 1e-4);
                float over = (float)Math.Min(t - p1.t, maxExtrapolation);
                pos = p1.pos + (p1.pos - p0.pos) / dt * over;
            }
            else
            {
                pos = p1.pos;
            }
            yaw = p1.yaw;
            swinging = p1.swinging;
            anchor = p1.anchor;
        }

        void ApplyName(string value)
        {
            if (string.IsNullOrEmpty(value) || nameLabel == null) return;
            switch (nameLabel)
            {
                case TextMesh tm: tm.text = value; break;
                default:
                    var prop = nameLabel.GetType().GetProperty("text");
                    prop?.SetValue(nameLabel, value);
                    break;
            }
            _hasName = true;
        }
    }
}
