//
// Hand-written to mirror Server/src/rooms/schema/GameState.ts (GameState).
//
using Colyseus.Schema;
#if UNITY_5_3_OR_NEWER
using UnityEngine.Scripting;
#endif

namespace WebSwingEscape.Net
{
    public partial class GameState : Schema
    {
#if UNITY_5_3_OR_NEWER
        [Preserve]
#endif
        public GameState() { }

        [Type(0, "map", typeof(MapSchema<PlayerState>))]
        public MapSchema<PlayerState> players = new MapSchema<PlayerState>();
    }
}
