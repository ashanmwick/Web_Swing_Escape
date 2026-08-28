//
// Hand-written to mirror Server/src/rooms/schema/GameState.ts (PlayerState).
// The [Type] index + wire-type MUST match the server field order exactly.
// If you change the server schema, change this file too (or replace this folder
// with the output of `npx schema-codegen`, see Net/README.md).
//
using Colyseus.Schema;
#if UNITY_5_3_OR_NEWER
using UnityEngine.Scripting;
#endif

namespace WebSwingEscape.Net
{
    public partial class PlayerState : Schema
    {
#if UNITY_5_3_OR_NEWER
        [Preserve]
#endif
        public PlayerState() { }

        [Type(0, "string")] public string sessionId = default(string);
        [Type(1, "string")] public string name = default(string);
        [Type(2, "string")] public string zone = default(string);

        [Type(3, "number")] public float x = default(float);
        [Type(4, "number")] public float y = default(float);
        [Type(5, "number")] public float z = default(float);
        [Type(6, "number")] public float rotY = default(float);

        [Type(7, "boolean")] public bool swinging = default(bool);
        [Type(8, "number")] public float anchorX = default(float);
        [Type(9, "number")] public float anchorY = default(float);
        [Type(10, "number")] public float anchorZ = default(float);

        [Type(11, "number")] public float coins = default(float);
        [Type(12, "number")] public float lastUpdate = default(float);
    }
}
