# Multiplayer (Colyseus) — Unity side

Client integration for the server in `/Server`. Physics stays local; each client
broadcasts its transform + swing state ~20x/s and renders everyone else as a
lightweight "remote" avatar.

## 1. One-time setup

### a. SDK package (embedded — nothing to install)

The Colyseus Unity SDK **0.18.1** is vendored as an *embedded package* at
`Packages/io.colyseus.sdk/` (NativeWebSocket bundled). Unity auto-discovers any
folder under `Packages/` that has a `package.json`, so it just works on open —
no Package Manager step, no `manifest.json` entry.

**Why embedded instead of the git URL?** The official
`https://github.com/colyseus/colyseus-unity-sdk.git#upm` install is currently
**broken at 0.18.0/0.18.1**: several source files and the whole `Runtime/Colyseus/Predict/`
folder were pushed to that branch without their `.meta` files, so Unity drops
them and the SDK fails to compile (`RoomClock`, `InputHandle`, `InputOptions`,
`QuantizeDescriptor` "could not be found"). The embedded copy here is the same
0.18.1 tree with the 5 missing `.meta` files regenerated.

If a future SDK release fixes the `#upm` branch, you can delete
`Packages/io.colyseus.sdk/` and add back to `Packages/manifest.json`:
`"io.colyseus.sdk": "https://github.com/colyseus/colyseus-unity-sdk.git#upm"`
(keep it on the **0.18.x** line to match the server).

### b. Scripts in this folder

| script | goes on | role |
|--------|---------|------|
| `NetworkClient` | auto-created singleton (`~NetworkClient`, `DontDestroyOnLoad`) | owns the connection, turns server state into main-thread snapshots + events |
| `NetSceneController` | one empty GameObject per multiplayer scene | joins this scene's zone, spawns/despawns remote avatars, adds `LocalPlayerSync` to the local player |
| `LocalPlayerSync` | the local Player (added automatically) | samples transform + web state, sends it |
| `RemotePlayerSync` | the RemotePlayer prefab root | follows one remote player, smooths motion, draws their web |
| `NetDebugHud` | any GameObject (optional) | on-screen connection / player list, `F3` toggles |
| `Schema/*` | — | hand-written mirror of the server schema |

No gameplay script is modified. `LocalPlayerSync` reads swing state from the
`LineRenderer` that `SpiderSwing` already drives (enabled = swinging, last point
= anchor), so nothing in `SpiderSwing.cs` needs to become public.

## 2. Build the RemotePlayer prefab

1. Duplicate your Player prefab/object, rename to `RemotePlayer`.
2. **Remove** these components: `HeroCharacterController`, `SpiderSwing`,
   `CameraLookToggle`, `PlayerInput`, `Rigidbody` (or set it kinematic), any
   `Camera` + `AudioListener` in its children, and colliders you don't want
   other players to bump into.
3. Keep the mesh + `Animator`.
4. Give it a child `LineRenderer` for the web (same material/width as the
   local one, `useWorldSpace = true`, `positionCount = 2`, start disabled).
5. Add `RemotePlayerSync` to the root and wire its optional fields:
   - `animator` → the avatar Animator (+ set `animatorSpeedParam` to your
     locomotion blend param, or clear it to skip)
   - `web` → the child LineRenderer
   - `webOrigin` → a hand bone (optional)
   - `nameLabel` → a `TextMeshPro` above the head (optional)
6. Save as a prefab under `Assets/`.

## 3. Wire each scene

**Lobby.unity** and **FreeRoam.unity**:

1. Create an empty GameObject `Net`.
2. Add `NetSceneController`:
   - `serverEndpoint` = `ws://localhost:2567` (local) / `wss://host` (deployed)
   - `zone` = `Lobby` in the Lobby scene, `FreeRoam` in the FreeRoam scene
   - `remotePlayerPrefab` = the prefab from step 2
   - `spawnPoint` = optional; otherwise the local player's start transform is
     reported as the spawn
3. (Optional) add `NetDebugHud` to the same object.

The local player must have the `Player` tag (it already does — `FreeRoamPortal`
relies on it).

## 4. Run / test locally

1. `cd Server && npm start`
2. Enter Play mode in Unity. `NetDebugHud` should show `zone=Lobby`, your
   session id and `players (1)`.
3. Second client — pick one:
   - **Multiplayer Play Mode**: Window → Multiplayer → Multiplayer Play Mode,
     enable 1 virtual player (`com.unity.multiplayer.center` is already in the
     project). Both players share the zone; each sees the other's avatar.
   - **ParrelSync / a standalone build** running alongside the Editor.
   - **Two WebGL builds**: `File → Build`, then `npx serve Build` and open two
     tabs (don't open `index.html` directly — WebSocket needs an http origin).
4. Verify: avatars appear/disappear on join/leave, remote motion is smooth,
   the web line shows on a remote client while that player double-taps jump to
   swing, and scene changes (portal / lobby button) re-join the right zone.
5. Server-side: http://localhost:2567/monitor lists the rooms and clients.

## 5. Deploy checklist

- WebGL over https ⇒ `serverEndpoint` must be `wss://` and the server behind TLS.
- Keep `Assets/Scripts/Net/Schema/*` in sync with `Server/src/rooms/schema/` —
  a mismatch shows up as a schema decode error on join.
- Set a real `playerName` (e.g. from your lobby UI) on `NetSceneController` or
  `NetworkClient.Instance.playerName` before `JoinZone`.

## Troubleshooting

**`error CS0246: RoomClock / InputHandle / InputOptions could not be found`, or
`QuantizeDescriptor does not exist in Colyseus.Schema.Utils`** — you're building
against the broken git-URL SDK, not the embedded one. Make sure
`Packages/manifest.json` has **no** `io.colyseus.sdk` line and that
`Packages/io.colyseus.sdk/` exists with `Runtime/Colyseus/RoomClock.cs.meta`,
`InputHandle.cs.meta`, `Serializer/Schema/InputEncoder.cs.meta`,
`Serializer/Schema/Utils/Quantize.cs.meta` and `Runtime/Colyseus/Predict.meta`
present. Then delete `Library/PackageCache/io.colyseus.sdk@*` and reopen.

**Schema decode error on join** — the C# schema in `Schema/` drifted from
`Server/src/rooms/schema/GameState.ts`. Field order = wire order; fix or
regenerate (see `Server/README.md`).
