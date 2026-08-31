# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this is

`Web_Swing_Escape` is a Unity 6 game: a Spider-Man–style web-swinging traversal
game with a lobby and a free-roam world, plus real-time multiplayer. Target
platform is WebGL (the networking notes assume a browser build served over
http/https).

- **Unity version:** `6000.5.10f1` (see [ProjectSettings/ProjectVersion.txt](ProjectSettings/ProjectVersion.txt)). Open with this exact editor.
- **Render pipeline:** Universal RP (URP).
- **Input:** the new Input System (`com.unity.inputsystem`); no legacy `Input.*`.
- **Third-party controller:** `HeroCharacterController` comes from the
  `com.herocharacter.herocombat` git package ([hero-combat-controller](https://github.com/mdj128/hero-combat-controller.git)) — it is *not* in this repo. Gameplay code composes with it rather than modifying it.

## Build / run / test

There is no CLI build. This is an Editor-driven project.

- **Run:** open the project in Unity, open [Assets/Scenes/Lobby.unity](Assets/Scenes/Lobby.unity), press Play. `Lobby` then `FreeRoam` are the two build scenes (in that order).
- **Build:** `File → Build Profiles` / `Build`, target WebGL. For local WebGL testing serve the output over http (`npx serve Build`) — opening `index.html` from disk breaks the WebSocket connection.
- **Tests:** Unity Test Framework (`com.unity.test-framework`) is installed but there are currently **no test assemblies or `.asmdef` files** in the project — all scripts compile into the default `Assembly-CSharp`. Run tests via `Window → General → Test Runner` if tests are added.
- **Second client for multiplayer testing:** `Window → Multiplayer → Multiplayer Play Mode` (package already installed) with 1 virtual player, or a standalone/WebGL build alongside the Editor.

The `.csproj`/`.slnx` files in the root are Unity-generated — do not hand-edit; they regenerate on focus.

## Multiplayer architecture (the part that spans files)

The game server is a **Colyseus** app that lives in a **separate repository**
(it was previously a `/Server` folder here; references to `/Server` in
[Assets/Scripts/Net/README.md](Assets/Scripts/Net/README.md) are stale). This
repo contains only the Unity client side, under [Assets/Scripts/Net/](Assets/Scripts/Net/).

**Colyseus SDK 0.18.1 is vendored as an embedded package** at
[Packages/io.colyseus.sdk/](Packages/io.colyseus.sdk/) (not in `manifest.json`).
This is deliberate: the official `#upm` git install is broken at 0.18.0/0.18.1
(missing `.meta` files). If you see `CS0246: RoomClock / InputHandle / InputOptions
could not be found`, the build picked up the git-URL SDK instead of the embedded
one — see the Troubleshooting section of [Assets/Scripts/Net/README.md](Assets/Scripts/Net/README.md).
Keep the SDK on the 0.18.x line to stay wire-compatible with the server.

### Model: client-authoritative, no server physics

Each client simulates its own physics locally and broadcasts its transform +
swing state ~20×/s. Everyone else is drawn as a lightweight "remote" avatar.
There is no server-side reconciliation. **No gameplay script is modified for
networking** — the net layer observes existing components (e.g. it reads swing
state off the `LineRenderer` that `SpiderSwing` drives rather than calling into
`SpiderSwing`).

### The four moving parts

| Component | Lives on | Responsibility |
|-----------|----------|----------------|
| [NetworkClient.cs](Assets/Scripts/Net/NetworkClient.cs) | auto-created `~NetworkClient` singleton, `DontDestroyOnLoad` | owns the Colyseus connection; converts background-thread schema callbacks into main-thread `PlayerSnapshot`s + `PlayerJoined`/`PlayerLeft`/`JoinedZone`/`LeftZone` events |
| [NetSceneController.cs](Assets/Scripts/Net/NetSceneController.cs) | one empty GameObject per multiplayer scene | ensures `NetworkClient` exists, adds `LocalPlayerSync` to the `Player`-tagged object, joins this scene's **zone**, spawns/despawns the RemotePlayer prefab per other player |
| [LocalPlayerSync.cs](Assets/Scripts/Net/LocalPlayerSync.cs) | the local Player (added automatically) | samples transform + swing state + coins, sends at `sendRate` (also sends immediately on swing-state change) |
| [RemotePlayerSync.cs](Assets/Scripts/Net/RemotePlayerSync.cs) | [Assets/Player/RemotePlayer.prefab](Assets/Player/RemotePlayer.prefab) root | follows one session id via a ~1s interpolation buffer; renders `interpolationDelay` in the past, extrapolates briefly, teleports on large jumps; drives Animator speed + web line |

Threading: `NetworkClient.OnStateChanged` runs on the Colyseus dispatch thread
and may only touch plain data; everything else is marshalled to `Update()` via a
lock + queue. Respect that boundary when adding features.

### Zones

There is a single Colyseus room type, `"game"`, entered with `JoinOrCreate`. The
`zone` option (`"Lobby"` / `"FreeRoam"`, one per scene) partitions players —
players in different zones never see each other. Switching scenes leaves the old
zone in `OnDestroy` and the new scene's `NetSceneController` joins the new one.

### Schema mirror — keep in sync

[Assets/Scripts/Net/Schema/GameState.cs](Assets/Scripts/Net/Schema/GameState.cs)
and [PlayerState.cs](Assets/Scripts/Net/Schema/PlayerState.cs) are **hand-written
mirrors** of the server's Colyseus schema. The `[Type(index, wiretype)]`
attributes must match the server's field order exactly — field order is wire
order. A mismatch surfaces as a schema decode error on join. When the server
schema changes, update these files (or regenerate with `npx schema-codegen`).

## Gameplay architecture

- **Swing mechanic** — [Assets/Scripts/Player/SpiderSwing.cs](Assets/Scripts/Player/SpiderSwing.cs).
  Double-tap the `Jump` action to fire a web at an auto-placed anchor above/ahead
  (no aiming). While swinging it **suspends `HeroCharacterController` locomotion**
  by reflecting into the controller's private `movement.enableMovementControl`
  field and drives the `Rigidbody` directly; gravity is never turned off, so the
  post-release fall is normal physics + the hero controller's air handling. The
  web is a 2-point world-space `LineRenderer` whose `enabled` state is the public
  signal the net layer reads. Note Unity 6's `Rigidbody.linearVelocity` (not
  `.velocity`).
- **Camera / cursor** — [Assets/Scripts/Player/ClickToLookCamera.cs](Assets/Scripts/Player/ClickToLookCamera.cs).
  The `HeroCharacterController`'s "Look" action is kept disabled by default, so the
  cursor stays free and on-screen UI is clickable. A left click in the world enters
  look mode (cursor locked/hidden, camera orbits with the mouse); `Esc` or a second
  click exits. Clicks on UI are ignored so they never grab the camera.
- **Scene flow** — [SceneLoader.cs](Assets/Scripts/Managers/SceneLoader.cs)
  (`DontDestroyOnLoad` singleton) exposes `LoadLobby()` / `LoadFreeRoam()`.
  [FreeRoamPortal.cs](Assets/Scripts/World/FreeRoamPortal.cs) is a trigger that
  sends the `Player`-tagged object to FreeRoam;
  [LobbyButton.cs](Assets/Scripts/World/LobbyButton.cs) is a UI-button hook back
  to the Lobby.
- **Global state** — [GameManager.cs](Assets/Scripts/Managers/GameManager.cs)
  (`DontDestroyOnLoad` singleton): `coins`, `speedMultiplier`, `rebirthCount`.
  Currently minimal; `LocalPlayerSync` replicates `coins`.
- **HUD** — [GameHud.cs](Assets/Scripts/UI/GameHud.cs) drives level/XP/speed/
  rebirth labels from **hard-coded placeholder values** (edited in the Inspector);
  wire real stats through `Refresh()` later. `[ExecuteAlways]`.
- **Billboard** — [Billboard.cs](Assets/Scripts/UI/Billboard.cs) faces world-space
  labels toward `Camera.main`.

## Conventions

- Net code is namespaced `WebSwingEscape.Net`; older gameplay/manager scripts are
  in the global namespace.
- The local player is found by the `"Player"` tag — keep that tag on the player
  object in every scene.
- `NetDebugHud` (`F3` to toggle) is an IMGUI test overlay — remove or disable for
  production builds.
- The repo uses Git LFS (`.gitattributes`) for binary assets (fbx, textures,
  audio, etc.).
