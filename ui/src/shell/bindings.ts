import { bindValue } from "cs2/api";

/**
 * Every binding the shell reads, declared once at module scope. A `bindValue` call in a render body
 * would allocate and subscribe a fresh binding on every render.
 *
 * Names are copied verbatim from docs/contracts/ui_bindings.md. A rename here produces a dashboard
 * that never opens, at runtime, with no build error. The fallback argument is mandatory
 * (contract rule 3).
 */

// -- agora.state (§4.1, dashboard chrome) --------------------------------------------------------

/** Master toggle. False means the player sees no trace of the mod — not even the button. */
export const enabled$ = bindValue<boolean>("agora.state", "enabled", false);

/** True once the engine has published a political state at least once. */
export const ready$ = bindValue<boolean>("agora.state", "ready", false);

// -- agora.debug (§4.0, M0 pipeline proof) -------------------------------------------------------

/**
 * The M0 liveness readout, folded into the toggle button when DebugPanel was retired.
 *
 * These two are read here and nowhere else. Note what is deliberately NOT read: contract §4.0 bars
 * dashboard chrome from taking the master toggle off `agora.debug.enabled` even though it returns
 * the same value today — that comes from `enabled$` above.
 *
 * The date is taken from here rather than from `agora.state.summary.date` on purpose. `summary` is
 * published on the engine's monthly tick and is empty until `ready`; these are UI-tick getters
 * straight off the clock, so they are alive from the first frame in a loaded game. That is what
 * makes them a pipeline proof — they show the C# → JS bridge working before the political engine
 * has produced anything to show.
 */
export const simDate$ = bindValue<string>("agora.debug", "simDate", "");

/** Political month, 1–12. Named `simDay` in the binding: §4.0 is closed, so the name cannot change. */
export const simMonth$ = bindValue<number>("agora.debug", "simDay", 0);
