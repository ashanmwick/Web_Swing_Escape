using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using Colyseus;

namespace WebSwingEscape.Net
{
    /// <summary>
    /// A plain snapshot of one networked player, safe to read from the main
    /// thread. Filled from the Colyseus schema on the SDK's background dispatch
    /// thread, then handed to the main thread by <see cref="NetworkClient"/>.
    /// </summary>
    public struct PlayerSnapshot
    {
        public string sessionId;
        public string name;
        public string zone;
        public Vector3 position;
        public float yaw;
        public bool swinging;
        public Vector3 anchor;
        public int coins;
    }

    /// <summary>
    /// Owns the connection to the Colyseus server and turns its (background-thread)
    /// state callbacks into main-thread events + a snapshot dictionary the rest of
    /// the game can poll. One instance, survives scene loads.
    ///
    /// Nothing here touches gameplay code directly — <see cref="NetSceneController"/>
    /// wires this to the local player and the remote-avatar prefab per scene.
    /// </summary>
    public class NetworkClient : MonoBehaviour
    {
        public static NetworkClient Instance { get; private set; }

        [Tooltip("ws://host:port for local dev. Use wss://host for TLS (required for a WebGL build served over https).")]
        public string endpoint = "ws://localhost:2567";

        [Tooltip("Shown to other players. Left empty -> a random 'Swinger-1234' name.")]
        public string playerName = "";

        // ---- connection ----
        Client _client;
        Room<GameState> _room;
        string _zone;

        // ---- background -> main thread hand-off ----
        readonly object _lock = new object();
        readonly Dictionary<string, PlayerSnapshot> _incoming = new Dictionary<string, PlayerSnapshot>();
        bool _incomingDirty;

        readonly Dictionary<string, PlayerSnapshot> _snapshots = new Dictionary<string, PlayerSnapshot>();
        readonly HashSet<string> _known = new HashSet<string>();
        readonly List<string> _tmpAdded = new List<string>();
        readonly List<string> _tmpRemoved = new List<string>();
        readonly Queue<Action> _mainThread = new Queue<Action>();

        // ---- public surface ----
        public bool IsConnected => _room != null;
        public string SessionId => _room != null ? _room.SessionId : null;
        public string Zone => _zone;
        public int LastRoundTripMs { get; private set; }

        /// <summary>sessionId of a player that just appeared (fires for pre-existing players on join too).</summary>
        public event Action<string> PlayerJoined;
        /// <summary>sessionId of a player that just disappeared.</summary>
        public event Action<string> PlayerLeft;
        /// <summary>Raised on the main thread once a zone join has completed.</summary>
        public event Action<string> JoinedZone;
        /// <summary>Raised on the main thread when the room connection ends (arg = close code).</summary>
        public event Action<int> LeftZone;
        /// <summary>Raised on the main thread when a join / connection attempt fails.</summary>
        public event Action<string> ConnectionFailed;

        public static NetworkClient EnsureInstance()
        {
            if (Instance == null)
            {
                var go = new GameObject("~NetworkClient");
                Instance = go.AddComponent<NetworkClient>();
            }
            return Instance;
        }

        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            DontDestroyOnLoad(gameObject);
            // Keep pumping the socket when the editor / player window loses focus,
            // so two instances on one machine stay in sync while you alt-tab.
            Application.runInBackground = true;
        }

        async void OnDestroy()
        {
            if (Instance == this) Instance = null;
            await LeaveAsync();
        }

        // ------------------------------------------------------------------
        //  Join / leave
        // ------------------------------------------------------------------

        public async void JoinZone(string zone, Vector3 spawnPos, float spawnYaw)
        {
            await LeaveAsync();

            _zone = zone;
            _client = new Client(endpoint);

            var name = string.IsNullOrWhiteSpace(playerName)
                ? "Swinger-" + UnityEngine.Random.Range(1000, 9999)
                : playerName.Trim();

            var options = new Dictionary<string, object>
            {
                { "zone", zone },
                { "name", name },
                { "x", spawnPos.x }, { "y", spawnPos.y }, { "z", spawnPos.z },
                { "rotY", spawnYaw },
            };

            try
            {
                _room = await _client.JoinOrCreate<GameState>("game", options);
            }
            catch (Exception e)
            {
                Debug.LogError($"[NetworkClient] join '{zone}' failed: {e.Message}");
                _room = null;
                ConnectionFailed?.Invoke(e.Message);
                return;
            }

            _room.OnStateChange += OnStateChanged;          // background thread
            _room.OnLeave += code => Enqueue(() =>          // background thread -> queued
            {
                Debug.Log($"[NetworkClient] left zone '{zone}' (code {code})");
                _room = null;
                ClearAll();
                LeftZone?.Invoke(code);
            });
            _room.OnError += (code, msg) => Debug.LogError($"[NetworkClient] room error {code}: {msg}");
            _room.OnMessage<double>("pong", sent =>
                LastRoundTripMs = Mathf.RoundToInt((float)(NowMs() - sent)));
            _room.OnMessage<ChatMessage>("chat", m =>
                Debug.Log($"[chat] <{m.from}> {m.text}"));

            Debug.Log($"[NetworkClient] joined zone '{zone}' as {name} (session {_room.SessionId})");
            JoinedZone?.Invoke(zone);
        }

        public async Task LeaveAsync()
        {
            var room = _room;
            _room = null;
            ClearAll();
            if (room != null)
            {
                try { await room.Leave(true); } catch { /* already gone */ }
            }
        }

        // ------------------------------------------------------------------
        //  Outbound
        // ------------------------------------------------------------------

        public void SendLocalState(Vector3 pos, float yaw, bool swinging, Vector3 anchor, int coins)
        {
            if (_room == null) return;
            _room.Send("state", new StateMessage
            {
                x = pos.x, y = pos.y, z = pos.z, rotY = yaw,
                swinging = swinging,
                ax = anchor.x, ay = anchor.y, az = anchor.z,
                coins = coins,
            });
        }

        public void SendChat(string text)
        {
            if (_room == null || string.IsNullOrWhiteSpace(text)) return;
            _room.Send("chat", text.Trim());
        }

        public void SendPing()
        {
            if (_room == null) return;
            _room.Send("ping", NowMs());
        }

        // ------------------------------------------------------------------
        //  Snapshot access (main thread)
        // ------------------------------------------------------------------

        public bool TryGetSnapshot(string sessionId, out PlayerSnapshot snapshot)
            => _snapshots.TryGetValue(sessionId, out snapshot);

        public IReadOnlyDictionary<string, PlayerSnapshot> Snapshots => _snapshots;

        // ------------------------------------------------------------------
        //  Background -> main thread plumbing
        // ------------------------------------------------------------------

        void OnStateChanged(GameState state, bool isFirstState)
        {
            // Runs on Colyseus.WebSocketDispatch thread. Only plain data here.
            lock (_lock)
            {
                _incoming.Clear();
                state.players.ForEach((key, p) =>
                {
                    _incoming[key] = new PlayerSnapshot
                    {
                        sessionId = string.IsNullOrEmpty(p.sessionId) ? key : p.sessionId,
                        name = p.name,
                        zone = p.zone,
                        position = new Vector3(p.x, p.y, p.z),
                        yaw = p.rotY,
                        swinging = p.swinging,
                        anchor = new Vector3(p.anchorX, p.anchorY, p.anchorZ),
                        coins = Mathf.RoundToInt(p.coins),
                    };
                });
                _incomingDirty = true;
            }
        }

        void Update()
        {
            // Drain queued main-thread actions (OnLeave, etc.)
            while (true)
            {
                Action a = null;
                lock (_lock)
                {
                    if (_mainThread.Count > 0) a = _mainThread.Dequeue();
                }
                if (a == null) break;
                try { a(); } catch (Exception e) { Debug.LogException(e); }
            }

            if (!_incomingDirty) return;

            _tmpAdded.Clear();
            _tmpRemoved.Clear();

            lock (_lock)
            {
                _incomingDirty = false;

                foreach (var kv in _incoming)
                {
                    _snapshots[kv.Key] = kv.Value;
                    if (_known.Add(kv.Key)) _tmpAdded.Add(kv.Key);
                }
                foreach (var known in _known)
                {
                    if (!_incoming.ContainsKey(known)) _tmpRemoved.Add(known);
                }
            }

            foreach (var key in _tmpRemoved)
            {
                _known.Remove(key);
                _snapshots.Remove(key);
                try { PlayerLeft?.Invoke(key); } catch (Exception e) { Debug.LogException(e); }
            }
            foreach (var key in _tmpAdded)
            {
                try { PlayerJoined?.Invoke(key); } catch (Exception e) { Debug.LogException(e); }
            }
        }

        void Enqueue(Action a)
        {
            lock (_lock) { _mainThread.Enqueue(a); }
        }

        void ClearAll()
        {
            lock (_lock)
            {
                _incoming.Clear();
                _incomingDirty = false;
            }
            // Report removals so scene controllers can despawn avatars.
            foreach (var key in new List<string>(_known))
            {
                _snapshots.Remove(key);
                try { PlayerLeft?.Invoke(key); } catch (Exception e) { Debug.LogException(e); }
            }
            _known.Clear();
        }

        static readonly DateTime Epoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        static double NowMs() => DateTime.UtcNow.Subtract(Epoch).TotalMilliseconds;

        // ---- wire payloads (public fields -> msgpack map keys the server reads) ----

        [Serializable]
        public class StateMessage
        {
            public float x, y, z, rotY;
            public bool swinging;
            public float ax, ay, az;
            public int coins;
        }

        [Serializable]
        public class ChatMessage
        {
            public string from;
            public string text;
        }
    }
}
