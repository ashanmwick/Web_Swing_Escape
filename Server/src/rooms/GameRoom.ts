import { Room, Client, CloseCode } from "@colyseus/core";
import { GameState, PlayerState } from "./schema/GameState";

/** Options the Unity client passes to client.JoinOrCreate("game", options). */
interface JoinOptions {
  name?: string;
  zone?: string;
  x?: number;
  y?: number;
  z?: number;
  rotY?: number;
}

/** Payload of the "state" message the owning client sends ~20x per second. */
interface StateMessage {
  x: number;
  y: number;
  z: number;
  rotY: number;
  swinging: boolean;
  ax: number; // web anchor
  ay: number;
  az: number;
  coins?: number;
}

const MAX_NAME_LEN = 24;
const MAX_CHAT_LEN = 240;

/**
 * A single zone instance (one per distinct `zone` option, thanks to
 * `.filterBy(["zone"])` in app.config.ts). "Lobby" players and "FreeRoam"
 * players therefore end up in different room instances and never see each other.
 *
 * The server is authoritative for *presence* (who is in the zone) and acts as a
 * *relay* for movement. It deliberately does not simulate the swing physics.
 */
export class GameRoom extends Room<{ state: GameState }> {
  maxClients = 24;

  onCreate(options: JoinOptions) {
    this.setState(new GameState());
    this.setMetadata({ zone: options.zone ?? "default" });

    // 20 Hz state broadcast. Lower = less bandwidth, more visible lag.
    this.setPatchRate(50);

    this.onMessage("state", (client, msg: StateMessage) => {
      const p = this.state.players.get(client.sessionId);
      if (!p || !msg) return;

      p.x = num(msg.x);
      p.y = num(msg.y);
      p.z = num(msg.z);
      p.rotY = num(msg.rotY);
      p.swinging = !!msg.swinging;
      p.anchorX = num(msg.ax);
      p.anchorY = num(msg.ay);
      p.anchorZ = num(msg.az);
      if (typeof msg.coins === "number" && isFinite(msg.coins)) {
        p.coins = msg.coins;
      }
      p.lastUpdate = this.clock.currentTime;
    });

    // Round-trip time helper for the client HUD. Echo the client's timestamp back.
    this.onMessage("ping", (client, clientTime: number) => {
      client.send("pong", clientTime);
    });

    this.onMessage("chat", (client, text: string) => {
      const p = this.state.players.get(client.sessionId);
      if (!p || typeof text !== "string") return;
      const clean = text.trim().slice(0, MAX_CHAT_LEN);
      if (clean.length === 0) return;
      this.broadcast("chat", { from: p.name, text: clean });
    });

    console.log(`[${this.roomId}] created  zone=${this.metadata?.zone}`);
  }

  onJoin(client: Client, options: JoinOptions) {
    const p = new PlayerState();
    p.sessionId = client.sessionId;
    p.name = (options?.name || `Swinger-${client.sessionId.substring(0, 4)}`)
      .toString()
      .slice(0, MAX_NAME_LEN);
    p.zone = options?.zone ?? "default";
    p.x = num(options?.x);
    p.y = num(options?.y);
    p.z = num(options?.z);
    p.rotY = num(options?.rotY);
    p.lastUpdate = this.clock.currentTime;

    this.state.players.set(client.sessionId, p);
    console.log(
      `[${this.roomId}] + ${p.name} (${client.sessionId})  ${this.clients.length}/${this.maxClients}`
    );
  }

  async onLeave(client: Client, code?: number) {
    const p = this.state.players.get(client.sessionId);
    if (p) p.swinging = false;

    const consented =
      code === CloseCode.CONSENTED || code === CloseCode.NORMAL_CLOSURE;

    // Give an unexpectedly dropped client a short window to reconnect (e.g.
    // browser tab backgrounded, brief network blip) before removing their avatar.
    if (!consented) {
      try {
        await this.allowReconnection(client, 8);
        console.log(`[${this.roomId}] ~ ${client.sessionId} reconnected`);
        return;
      } catch {
        /* window expired -> fall through and remove */
      }
    }

    this.state.players.delete(client.sessionId);
    console.log(`[${this.roomId}] - ${client.sessionId}  ${this.clients.length}/${this.maxClients}`);
  }

  onDispose() {
    console.log(`[${this.roomId}] disposed`);
  }
}

function num(v: unknown): number {
  return typeof v === "number" && isFinite(v) ? v : 0;
}
