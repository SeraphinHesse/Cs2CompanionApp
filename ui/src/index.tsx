import { ModRegistrar } from "cs2/modding";
import { DebugPanel } from "./panels/DebugPanel";

/**
 * UI mod entry point.
 *
 * The registrar receives the game's module registry, which supports four operations:
 * find, override, extend and append. Prefer `append` — it adds to a hook point without
 * replacing game code, so it survives game updates that `override` would break.
 *
 * M0 mounts a single debug panel at GameTopLeft. The real dashboard replaces it in M2.
 */
const register: ModRegistrar = (moduleRegistry) => {
  moduleRegistry.append("GameTopLeft", DebugPanel);
};

export default register;
