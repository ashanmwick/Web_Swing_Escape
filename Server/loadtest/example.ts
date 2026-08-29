import { cli, type Options } from "@colyseus/loadtest";
import { Client, getStateCallbacks } from "@colyseus/sdk";
import { GameState } from "../src/rooms/schema/GameState";

/**
 * Fake client for `npm run loadtest`. Each bot joins the FreeRoam zone and flies
 * a lazy circle, occasionally "swinging", so the server does real broadcast work.
 *
 *   npm run loadtest                          # 20 bots (see package.json)
 *   npm run loadtest -- --numClients 100      # override
 *   npm run loadtest -- --room game --endpoint ws://localhost:2567
 *
 * Press q / Ctrl+C to quit the dashboard.
 */
async function main(options: Options) {
  const client = new Client(options.endpoint);

  const room = await client.joinOrCreate<GameState>(options.roomName, {
    name: `bot-${options.clientId}`,
    zone: "FreeRoam",
    x: Math.random() * 40 - 20,
    y: 2,
    z: Math.random() * 40 - 20,
    rotY: Math.random() * 360,
  });

  // Touch the state so the schema decoder is exercised like a real client.
  const $ = getStateCallbacks(room);
  $(room.state).players.onAdd(() => {});

  let t = Math.random() * 10;
  const interval = setInterval(() => {
    t += 0.1;
    room.send("state", {
      x: Math.sin(t) * 20,
      y: 2 + Math.abs(Math.sin(t * 2)) * 3,
      z: Math.cos(t) * 20,
      rotY: (t * 57) % 360,
      swinging: Math.floor(t) % 5 === 0,
      ax: 0,
      ay: 60,
      az: 0,
    });
  }, 100);

  room.onLeave(() => clearInterval(interval));
}

cli(main);
