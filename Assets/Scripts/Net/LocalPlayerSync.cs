using UnityEngine;

namespace WebSwingEscape.Net
{
    /// <summary>
    /// Put this on the local Player object (the one with HeroCharacterController +
    /// SpiderSwing). It samples the transform a few times per second and pushes it
    /// to the server. Swing state is taken from SpiderSwing's public API when it is
    /// present; otherwise it falls back to inferring it from the LineRenderer that
    /// SpiderSwing drives (its enabled flag = swinging, its last point = anchor).
    /// The LineRenderer probe alone is unreliable because that line object is not
    /// always parented under the Player.
    /// </summary>
    public class LocalPlayerSync : MonoBehaviour
    {
        [Tooltip("State updates sent per second. 10-20 is plenty for a swing game.")]
        [Range(5f, 30f)] public float sendRate = 20f;

        [Tooltip("Preferred swing-state source. Auto-found on this GameObject if left empty.")]
        [SerializeField] SpiderSwing spiderSwing;

        [Tooltip("Fallback web LineRenderer, used only when there is no SpiderSwing. Auto-found in children " +
                 "if left empty. Its enabled state = 'is swinging', and its last point = the web anchor.")]
        [SerializeField] LineRenderer web;

        [Tooltip("Also replicate GameManager.coins so other clients can show it.")]
        [SerializeField] bool sendCoins = true;

        float _nextSendTime;
        bool _lastSwinging;

        void Awake()
        {
            if (spiderSwing == null) spiderSwing = GetComponent<SpiderSwing>();
            if (web == null) web = GetComponentInChildren<LineRenderer>(true);
        }

        void Update()
        {
            var net = NetworkClient.Instance;
            if (net == null || !net.IsConnected) return;

            bool swinging;
            Vector3 anchor;
            if (spiderSwing != null)
            {
                swinging = spiderSwing.IsSwinging;
                anchor = swinging ? spiderSwing.AnchorPosition : Vector3.zero;
            }
            else
            {
                swinging = web != null && web.enabled && web.positionCount >= 2;
                anchor = swinging ? web.GetPosition(web.positionCount - 1) : Vector3.zero;
            }

            // Steady cadence, but send right away when the swing state changes so
            // the web line / animation on other clients flips without a send-interval delay.
            bool due = Time.time >= _nextSendTime || swinging != _lastSwinging;
            if (!due) return;
            _nextSendTime = Time.time + 1f / Mathf.Max(1f, sendRate);
            _lastSwinging = swinging;

            int coins = 0;
            if (sendCoins && GameManager.Instance != null) coins = (int)GameManager.Instance.coins;

            net.SendLocalState(transform.position, transform.eulerAngles.y, swinging, anchor, coins);
        }
    }
}
