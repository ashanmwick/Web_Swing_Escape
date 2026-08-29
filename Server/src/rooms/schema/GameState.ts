import { Schema, MapSchema, type } from "@colyseus/schema";

/**
 * One connected player. Only fields a *remote* client needs in order to draw
 * this player's avatar are replicated here. Swing physics runs on the owning
 * client; the result (position + a "swinging" flag + the web anchor) is what
 * travels over the wire.
 *
 * IMPORTANT: the field declaration order below is the wire format. The Unity
 * schema class (Assets/Scripts/Net/Schema/PlayerState.cs) must list the same
 * fields in the same order with matching types. If you add/remove/reorder a
 * field here, mirror it there (or re-run schema-codegen).
 */
export class PlayerState extends Schema {
  @type("string") sessionId = "";
  @type("string") name = "";
  @type("string") zone = "";

  // World transform (Unity left-handed coords, sent as-is by the client).
  @type("number") x = 0;
  @type("number") y = 0;
  @type("number") z = 0;
  @type("number") rotY = 0; // yaw in degrees

  // Web-swing visuals.
  @type("boolean") swinging = false;
  @type("number") anchorX = 0;
  @type("number") anchorY = 0;
  @type("number") anchorZ = 0;

  // Optional shared progression (mirrors the client's GameManager).
  @type("number") coins = 0;

  // Server clock time (ms) of the last accepted "state" message from this client.
  @type("number") lastUpdate = 0;
}

export class GameState extends Schema {
  @type({ map: PlayerState }) players = new MapSchema<PlayerState>();
}
