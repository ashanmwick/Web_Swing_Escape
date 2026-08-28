using UnityEngine;

namespace WebSwingEscape.Net
{
    /// <summary>
    /// Put this on the local Player object (the one with HeroCharacterController +
    /// SpiderSwing). It samples the transform a few times per second and pushes it
    /// to the server. It never reads SpiderSwing directly: swing state is inferred
    /// from the same LineRenderer that SpiderSwing drives, so no gameplay code
    /// needs to change.
    /// </summary>
    public class LocalPlayerSync : MonoBehaviour
    {
        [Tooltip("State updates sent per second. 10-20 is plenty for a swing game.")]
        [Range(5f, 30f)] public float sendRate = 20f;

        [Tooltip("The LineRenderer SpiderSwing toggles for the web. Auto-found in children if left empty. " +
                 "Its enabled state = 'is swinging', and its last point = the web anchor.")]
        [SerializeField] LineRenderer web;

        [Tooltip("Also replicate GameManager.coins so other clients can show it.")]
        [SerializeField] bool sendCoins = true;

        float _nextSendTime;
        bool _lastSwinging;

        void Awake()
        {
            if (web == null) web = GetComponentInChildren<LineRenderer>(true);
        }

        void Update()
        {
            var net = NetworkClient.Instance;
            if (net == null || !net.IsConnected) return;

            bool swinging = web != null && web.enabled && web.positionCount >= 2;

            // Steady cadence, but send right away when the swing state changes so
            // the web line / animation on other clients flips without a send-interval delay.
            bool due = Time.time >= _nextSendTime || swinging != _lastSwinging;
            if (!due) return;
            _nextSendTime = Time.time + 1f / Mathf.Max(1f, sendRate);
            _lastSwinging = swinging;
            Vector3 anchor = swinging ? web.GetPosition(web.positionCount - 1) : Vector3.zero;

            int coins = 0;
            if (sendCoins && GameManager.Instance != null) coins = GameManager.Instance.coins;

            net.SendLocalState(transform.position, transform.eulerAngles.y, swinging, anchor, coins);
        }
    }
}
