using System.Collections.Generic;
using UnityEngine;

namespace WebSwingEscape.Net
{
    /// <summary>
    /// Drop one of these into each multiplayer scene (Lobby, FreeRoam). It:
    ///   1. makes sure a <see cref="NetworkClient"/> exists and points at the server,
    ///   2. attaches a <see cref="LocalPlayerSync"/> to the tagged local player,
    ///   3. joins this scene's zone,
    ///   4. spawns / despawns a RemotePlayer prefab for every other player.
    ///
    /// Switching scenes (Lobby &lt;-&gt; FreeRoam) leaves the old zone in OnDestroy
    /// and the new scene's controller joins the new one.
    /// </summary>
    public class NetSceneController : MonoBehaviour
    {
        [Header("Server")]
        [Tooltip("ws://host:port  (wss://host for TLS). Copied onto the shared NetworkClient.")]
        public string serverEndpoint = "ws://localhost:2567";

        [Tooltip("Zone name for this scene. Players in different zones never see each other.")]
        public string zone = "Lobby";

        [Tooltip("Optional display name. Empty -> random 'Swinger-1234'.")]
        public string playerName = "";

        [Header("Local player")]
        [Tooltip("Tag used to find the local player object in the scene.")]
        public string localPlayerTag = "Player";
        [Tooltip("Add a LocalPlayerSync to the local player automatically if it has none.")]
        public bool autoAddLocalSync = true;
        [Tooltip("Optional explicit spawn point. If unset, the local player's current transform is used as the reported spawn.")]
        public Transform spawnPoint;

        [Header("Remote players")]
        [Tooltip("Prefab instantiated for every OTHER player (see Net/README.md for how to build it).")]
        public GameObject remotePlayerPrefab;
        [Tooltip("Parent for spawned remote avatars (optional; keeps the hierarchy tidy).")]
        public Transform remoteParent;

        readonly Dictionary<string, RemotePlayerSync> _remotes = new Dictionary<string, RemotePlayerSync>();
        NetworkClient _net;

        void Start()
        {
            _net = NetworkClient.EnsureInstance();
            _net.endpoint = serverEndpoint;
            if (!string.IsNullOrWhiteSpace(playerName)) _net.playerName = playerName;

            _net.PlayerJoined += HandlePlayerJoined;
            _net.PlayerLeft += HandlePlayerLeft;

            Vector3 spawnPos;
            float spawnYaw;
            var local = GameObject.FindGameObjectWithTag(localPlayerTag);
            if (local != null)
            {
                if (autoAddLocalSync && local.GetComponent<LocalPlayerSync>() == null)
                    local.AddComponent<LocalPlayerSync>();

                spawnPos = spawnPoint != null ? spawnPoint.position : local.transform.position;
                spawnYaw = spawnPoint != null ? spawnPoint.eulerAngles.y : local.transform.eulerAngles.y;
            }
            else
            {
                Debug.LogWarning($"[NetSceneController] no object tagged '{localPlayerTag}' found; " +
                                 "joining without a local avatar to broadcast.");
                spawnPos = spawnPoint != null ? spawnPoint.position : Vector3.zero;
                spawnYaw = spawnPoint != null ? spawnPoint.eulerAngles.y : 0f;
            }

            _net.JoinZone(zone, spawnPos, spawnYaw);
        }

        void OnDestroy()
        {
            if (_net != null)
            {
                _net.PlayerJoined -= HandlePlayerJoined;
                _net.PlayerLeft -= HandlePlayerLeft;
                _ = _net.LeaveAsync();
            }
            foreach (var r in _remotes.Values)
                if (r != null) Destroy(r.gameObject);
            _remotes.Clear();
        }

        void HandlePlayerJoined(string sessionId)
        {
            if (_net != null && sessionId == _net.SessionId) return;   // that's us
            if (_remotes.ContainsKey(sessionId)) return;
            if (remotePlayerPrefab == null)
            {
                Debug.LogWarning("[NetSceneController] remotePlayerPrefab not assigned.");
                return;
            }

            var go = Instantiate(remotePlayerPrefab, remoteParent);
            go.name = $"Remote_{sessionId}";
            var sync = go.GetComponent<RemotePlayerSync>();
            if (sync == null) sync = go.AddComponent<RemotePlayerSync>();
            sync.Bind(sessionId);
            _remotes[sessionId] = sync;
        }

        void HandlePlayerLeft(string sessionId)
        {
            if (_remotes.TryGetValue(sessionId, out var sync))
            {
                if (sync != null) Destroy(sync.gameObject);
                _remotes.Remove(sessionId);
            }
        }
    }
}
