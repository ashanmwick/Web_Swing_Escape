import { listen } from "@colyseus/tools";
import app from "./app.config";

// Binds Colyseus + Express to PORT (default 2567).
listen(app);
