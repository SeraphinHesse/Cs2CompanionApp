import { ModRegistrar } from "cs2/modding";
import { AgoraButton, Dashboard } from "./shell";

/**
 * UI mod entry point.
 *
 * The registrar receives the game's module registry, which supports four operations:
 * find, override, extend and append. Prefer `append` — it adds to a hook point without
 * replacing game code, so it survives game updates that `override` would break.
 *
 * Exactly two appends, and the split is the point. Agora used to mount its three panels straight
 * onto GameTopRight, all three at once, with no way to dismiss them: News alone is 760rem wide and
 * 640rem tall, so the stack overflowed the hook point at any interface scale and buried the city.
 * Now the only thing the mod puts on screen is one button, and `Dashboard` renders null until that
 * button is pressed — so the default view of the game is the game.
 *
 * Both components read `agora.state.enabled` and render null when the mod is switched off, so
 * mounting them here is not the same as forcing them onto the screen: with Agora disabled, these
 * hook points look exactly as they did before it was installed.
 */
const register: ModRegistrar = (moduleRegistry) => {
  // The toggle, and with it the M0 pipeline proof it absorbed from the retired DebugPanel — the
  // sim date comes off the clock rather than off engine state, so it still answers "is the C# → JS
  // bridge alive?" when every panel behind it is blank.
  moduleRegistry.append("GameTopLeft", AgoraButton);

  // The tab bar plus whichever one of Council / Districts / News is selected.
  moduleRegistry.append("GameTopRight", Dashboard);
};

export default register;
