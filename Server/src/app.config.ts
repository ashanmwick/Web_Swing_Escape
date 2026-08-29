import config from "@colyseus/tools";
import { monitor } from "@colyseus/monitor";
import { playground } from "@colyseus/playground";

import { GameRoom } from "./rooms/GameRoom";

export default config({
  initializeGameServer: (gameServer) => {
    /**
     * One room handler, "game". `filterBy(["zone"])` makes the matchmaker keep a
     * separate room instance per distinct `zone` join option, so the Unity
     * client joins the same handler for both the Lobby and FreeRoam scenes and
     * just passes a different `zone`.
     */
    gameServer.define("game", GameRoom).filterBy(["zone"]);
  },

  initializeExpress: (app) => {
    // Dev-only: interactive room tester at http://localhost:2567/playground
    if (process.env.NODE_ENV !== "production") {
      app.use("/playground", playground());
    }

    // Live room / client inspector at http://localhost:2567/monitor
    app.use("/monitor", monitor());

    app.get("/health", (_req: any, res: any) => res.json({ ok: true }));
  },

  beforeListen: () => {
    // Place migrations / warm-up here if ever needed.
  },
});
