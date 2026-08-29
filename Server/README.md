# Web Swing Escape — Colyseus server

Authoritative-presence / movement-relay server for the Unity client. The swing
physics stays on the client; this server tracks who is in a zone and forwards
each player's transform + swing state to everyone else in that zone.

> This folder is self-contained (its own `package.json`, `node_modules`,
> `.gitignore`). It lives here for convenience and is meant to be moved into its
> own repo later — nothing in it imports from the Unity project.

## Requirements

- Node.js >= 20 (tested on 22)

## Install

```bash
cd Server
npm install
```

## Run (development)

```bash
npm start
```

- Server: `ws://localhost:2567`
- Playground (interactive room tester): http://localhost:2567/playground
- Monitor (live rooms / clients / state): http://localhost:2567/monitor
- Health check: http://localhost:2567/health

`npm start` uses `tsx watch`, so it restarts on file changes.

## Build / run (production)

```bash
npm run build      # tsc -> build/
npm run start:prod # node build/index.js
NODE_ENV=production node build/index.js   # disables the playground route
```

## Load test

```bash
npm run loadtest                 # 20 bots into the FreeRoam zone
npm run loadtest -- --numClients 100
```

## Protocol (must stay in sync with the Unity side)

Room handler: **`game`**, matched per `zone` join option (`.filterBy(["zone"])`),
so `zone: "Lobby"` and `zone: "FreeRoam"` are separate room instances.

**Join options** (`client.JoinOrCreate("game", options)`):

| key    | type   | notes                         |
|--------|--------|-------------------------------|
| zone   | string | `"Lobby"` / `"FreeRoam"`      |
| name   | string | display name (<= 24 chars)   |
| x,y,z  | number | spawn position               |
| rotY   | number | spawn yaw (degrees)          |

**Client → server messages:**

| type   | payload                                                             |
|--------|--------------------------------------------------------------------|
| `state`| `{ x, y, z, rotY, swinging, ax, ay, az, coins? }` — send ~20/s     |
| `ping` | `number` (client timestamp ms) — server replies `pong` with it    |
| `chat` | `string`                                                           |

**Server → client messages:**

| type   | payload                        |
|--------|-------------------------------|
| `pong` | `number` (echoed timestamp)   |
| `chat` | `{ from: string, text: string }` |

**Replicated state** — `GameState.players: Map<sessionId, PlayerState>` where
`PlayerState` = `sessionId, name, zone, x, y, z, rotY, swinging, anchorX,
anchorY, anchorZ, coins, lastUpdate`. Field order is the wire format; mirror any
change in `Assets/Scripts/Net/Schema/PlayerState.cs`.

## Regenerating the Unity schema classes (optional)

The C# schema in `Assets/Scripts/Net/Schema/` is hand-written to match
`src/rooms/schema/GameState.ts`. To generate it instead:

```bash
npx schema-codegen src/rooms/schema/GameState.ts --output ../Assets/Scripts/Net/Schema --csharp --namespace WebSwingEscape.Net
```

## Files

```
src/index.ts              entry point (listen)
src/app.config.ts         room + express (monitor / playground) wiring
src/rooms/GameRoom.ts      the "game" room handler
src/rooms/schema/GameState.ts   replicated state
loadtest/example.ts        bot client for `npm run loadtest`
```

## Deploy notes

- A WebGL build served over `https://` must connect over `wss://` (TLS). Put the
  server behind a TLS-terminating proxy (Caddy / Nginx / a PaaS) and point the
  Unity client's endpoint at `wss://your-host`.
- Colyseus Cloud (https://cloud.colyseus.io) deploys this layout directly.
