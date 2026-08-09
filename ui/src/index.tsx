import { ModRegistrar } from "cs2/modding";
import { AgoraButton, ArticleModal, Dashboard, FirstRunDialog } from "./shell";

/**
 * UI mod entry point.
 *
 * The registrar receives the game's module registry, which supports four operations:
 * find, override, extend and append. Prefer `append` — it adds to a hook point without
 * replacing game code, so it survives game updates that `override` would break.
 *
 * Four appends, and the splits are the point. Agora used to mount its three panels straight
 * onto GameTopRight, all three at once, with no way to dismiss them: News alone is 760rem wide and
 * 640rem tall, so the stack overflowed the hook point at any interface scale and buried the city.
 * Now the only thing the mod puts on screen is one button, and `Dashboard` renders null until that
 * button is pressed — so the default view of the game is the game.
 *
 * Every one of them reads `agora.state.enabled` and renders null when the mod is switched off, so
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

  // The first-load region prompt, on its own append. It renders through `Portal` and overlays the
  // whole HUD, so the hook point is only an anchor and which one it is barely matters — what matters
  // is that it is not the dashboard's. This subtree holds the simulation paused while it is up; a
  // failure in it must not be able to take the dashboard with it, or the other way round. It renders
  // null on every load after the first, and on every load with the mod switched off.
  moduleRegistry.append("GameTopLeft", FirstRunDialog);

  // The news alert, on its own append for the same reasons and one more. Like the region prompt it
  // renders through `Portal`, so the hook point is only an anchor; unlike it, this subtree comes and
  // goes all game and may hold the simulation paused each time. Keeping it off the dashboard's mount
  // means a card that fails to render cannot take the dashboard down — and cannot take the News tab
  // down with it, which is the one place the player could otherwise still read what happened. It
  // renders null while the region prompt is up, with an empty queue, and with the mod switched off.
  moduleRegistry.append("GameTopLeft", ArticleModal);
};

export default register;
