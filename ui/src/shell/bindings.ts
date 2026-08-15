import { bindValue, call } from "cs2/api";

/**
 * Every binding the shell reads, declared once at module scope. A `bindValue` call in a render body
 * would allocate and subscribe a fresh binding on every render.
 *
 * Names are copied verbatim from docs/contracts/ui_bindings.md. A rename here produces a dashboard
 * that never opens, at runtime, with no build error. The fallback argument is mandatory
 * (contract rule 3).
 *
 * The `Agora.*` payload types are GLOBAL (declare namespace Agora in ui/types/bindings.d.ts). Do not
 * import them — there is no runtime module behind them and isolatedModules would turn such an import
 * into a webpack resolution error.
 */

// -- empty / loading values (contract §6, copied literally) --------------------------------------

export const EMPTY_SETTINGS: Agora.SettingsPayload = {
  schemaVersion: 0, startYear: 1990, theme: "Eu", system: "Proportional",
  themeLocked: false, pauseOnMajorNews: true, showAllReports: false, effectsEnabled: true,
  voteSharpness: "Default", newsInfluence: "Default", brandDiscipline: "Default",
  voteSharpnessValue: 0, newsInfluenceValue: 0, brandDisciplineValue: 0,
};

/**
 * Not wired into a binding — `alerts$` is an array and takes `[]`. This is the guard a card
 * substitutes for an index the queue no longer holds, so a render racing an ack cannot read a field
 * off `undefined` on the frame between the two.
 *
 * `major` is `false` here on purpose. An empty alert must never be the thing that takes the pause
 * barrier.
 */
export const EMPTY_NEWS_ALERT: Agora.NewsAlert = {
  id: "", kind: "Article", date: "", headline: "", summary: "", outletName: "",
  partyId: "", districtId: "", eventId: "", severity: 0, major: false, hasArticle: false,
};

// -- agora.state (§4.1, dashboard chrome) --------------------------------------------------------

/** Master toggle. False means the player sees no trace of the mod — not even the button. */
export const enabled$ = bindValue<boolean>("agora.state", "enabled", false);

/** True once the engine has published a political state at least once. */
export const ready$ = bindValue<boolean>("agora.state", "ready", false);

/**
 * The per-save settings document, sidecar-backed and never global config (non-negotiable #10).
 *
 * It is a mirror. The settings surface renders this and writes through `setSetting`; it never holds
 * a settings value of its own, because two places holding the same setting is how a control comes to
 * show a value the sidecar never took.
 */
export const settings$ = bindValue<Agora.SettingsPayload>(
  "agora.state", "settings", EMPTY_SETTINGS,
);

/**
 * One-shot lifecycle signal: this save has never chosen a region theme.
 *
 * Deliberately not a field of `SettingsPayload` — it is a value the sidecar never stores. It is a
 * getter rather than a pushed value because it has to flip inside the UI tick that answered the
 * prompt: the sim is held paused while the dialog is up, so there is no engine tick to push on.
 */
export const isFirstRun$ = bindValue<boolean>("agora.state", "isFirstRun", false);

// -- agora.parties (§4.2, shared lookup table) ---------------------------------------------------

/**
 * Read by the settings surface for one purpose only: counting the parties the player has taken
 * ownership of, so a region change can warn about what it is going to discard. Names and colours are
 * rendered from here by the panels, not by the shell.
 */
export const roster$ = bindValue<Agora.PartyBrief[]>("agora.parties", "roster", []);

// -- agora.news (§4.5, the popup lane) -----------------------------------------------------------

/**
 * The unanswered interruptions, OLDEST FIRST — the order they happened in, which is the order the
 * player answers them in. Do not sort it, and do not reverse it: the queue's order is the engine's,
 * and reordering a queue in the view changes which card the player is shown first.
 *
 * Read by the shell rather than by the News panel because the modal is chrome: it has to appear with
 * the dashboard closed, over whatever the player was doing.
 *
 * Each entry points at a feed row that already exists, so everything the modal shows can be found
 * again in the News tab afterwards.
 */
export const alerts$ = bindValue<Agora.NewsAlert[]>("agora.news", "alerts", []);

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

// -- the write channels (§4.1 agora.state.setSetting, §4.5 agora.news.ackAlert) ------------------

/**
 * Ask the engine to change one per-save setting.
 *
 * The contract's first inbound `CallBinding`, and the whole reason it is a call rather than a
 * trigger: the request may be refused, and a setting that silently will not stay set is
 * indistinguishable from a broken panel. This function only sends. It decides nothing — the returned
 * code is the engine's verdict (contract §4.6), and `""` means it took.
 *
 * Keys: `theme` ("Eu" | "Na"), `pauseOnMajorNews`, `showAllReports`, `effectsEnabled`
 * ("true" | "false"), `dismissFirstRun` (value ignored).
 */
export function setSetting(key: string, value: string): Promise<Agora.CommandOutcomeName> {
  return call<Agora.CommandOutcomeName>("agora.state", "setSetting", key, value);
}

/**
 * What the surface actually observed. Either the engine answered — with a code from the closed
 * vocabulary — or the bridge did not answer at all.
 *
 * The two are kept apart on purpose. `answered: false` is not an outcome code and must never be
 * turned into one: contract rule 5 forbids a panel reporting a rejection the C# side did not return,
 * and "we never heard back" is a statement about the bridge, not a verdict about the setting.
 */
export type WriteOutcome =
  | { answered: true; outcome: Agora.CommandOutcomeName }
  | { answered: false };

/**
 * How long to wait for the engine before giving the control back to the player.
 *
 * `call` hands back a Promise and nothing guarantees it settles. The first-run dialog holds the
 * simulation paused while it is open, so a stalled bridge would leave the player looking at two dead
 * buttons with the clock stopped and no way forward. Eight seconds is far longer than the round trip
 * — the handler is a field write — and far shorter than a player's patience with a frozen game.
 */
export const SETTING_CALL_TIMEOUT_MS = 8000;

/**
 * Send an inbound call and answer within the deadline whatever the bridge does. Never rejects; the
 * caller gets a `WriteOutcome` either way.
 *
 * One helper rather than the same promise dance per write channel, so that a second inbound call
 * cannot quietly ship without the timeout — which is the failure that leaves a player looking at a
 * dead button with the clock stopped. `label` is for the console only and never reaches the player.
 */
function withDeadline(
  label: string,
  send: () => Promise<Agora.CommandOutcomeName>,
): Promise<WriteOutcome> {
  return new Promise<WriteOutcome>(function (resolve) {
    let settled = false;

    function finish(result: WriteOutcome): void {
      if (settled) {
        return;
      }
      settled = true;
      clearTimeout(timer);
      resolve(result);
    }

    const timer = setTimeout(function () {
      finish({ answered: false });
    }, SETTING_CALL_TIMEOUT_MS);

    // The throw guard is for the binding not being registered at all — outside a loaded game the
    // publishing system may not exist, and that must not escape as an unhandled rejection.
    try {
      send().then(
        function (outcome) {
          finish({ answered: true, outcome: outcome });
        },
        function (error) {
          console.warn("[AGORA] " + label + " failed on the bridge", error);
          finish({ answered: false });
        },
      );
    } catch (error) {
      console.warn("[AGORA] " + label + " could not be sent", error);
      finish({ answered: false });
    }
  });
}

/**
 * `setSetting` with a deadline. Never rejects; the caller gets a `WriteOutcome` either way.
 */
export function requestSetting(key: string, value: string): Promise<WriteOutcome> {
  return withDeadline("setSetting(" + key + ")", function () {
    return setSetting(key, value);
  });
}

/**
 * Tell the engine the player answered an alert, and drop it from the queue. Pass `"*"` to dismiss
 * every queued alert at once.
 *
 * A call and not a trigger, and it carries the same deadline as `requestSetting` for a sharper
 * reason than that one has: while a major alert is up the modal holds the pause barrier, and the
 * game forces the speed to zero every frame it is held. A dismiss that never answers would leave a
 * player with a card they cannot close and a clock they cannot start. The deadline is what puts the
 * decision back in the caller's hands.
 *
 * Acking an id the engine no longer holds answers `""`, not a refusal — a double-click is not an
 * error. This function only sends; the returned code is the engine's verdict (contract §4.6) and the
 * caller decides through `isAccepted` whether the card may close.
 */
export function ackAlert(id: string): Promise<WriteOutcome> {
  return withDeadline("ackAlert(" + id + ")", function () {
    return call<Agora.CommandOutcomeName>("agora.news", "ackAlert", id);
  });
}

/**
 * The closed outcome vocabulary (§4.6) in plain English, one sentence each.
 *
 * Same shape and same rule as the News panel's `ORIGIN_LABEL`: a lookup with a fallback, so the map
 * is the only place this copy lives and both the first-run dialog and the settings surface say the
 * same thing about the same code. Where the two differ is the fallback. `ORIGIN_LABEL` falls through
 * to the raw value because a new event origin is a word a player can read; an outcome code is engine
 * vocabulary, and "BadValue" on a modal is worse than saying nothing useful politely. An untaught
 * code therefore degrades to the generic sentence below.
 *
 * `""` is not in here. It means accepted, and there is nothing to tell the player. `OkColorInUse`
 * IS in here and is also an acceptance — see `isAccepted`. Its sentence is worded as a warning about
 * a write that went through, because a refusal-shaped sentence over an applied colour would send the
 * player back to change something the engine already changed.
 */
const OUTCOME_MESSAGE: { [outcome: string]: string } = {
  NoActiveSave: "No save is loaded, so there is nothing to change yet.",
  UnknownKey: "This build does not recognise that setting.",
  BadValue: "That is not a value this setting will accept.",
  ThemeLocked: "This save has already held an election, so the region is now history.",
  Busy: "Something this would tear down is still running. Try again in a moment.",
  Failed: "That did not take. The reason is in Agora.log.",
  NotFound: "That party is no longer part of this save.",
  ValueRequired: "This field needs some text. To hand it back to the generator, use reset.",
  // No number in this sentence on purpose. The limits are published by `agora.parties.editLimits`
  // and rendered by the character counter; a literal here would be a second copy of the same number
  // that can drift from the engine's, which is the whole reason that binding exists.
  TooLong: "That is longer than this field will hold. Shorten it and try again.",
  OkColorInUse: "Saved. Another party already wears this colour, so the two will look alike.",
};

/** Shown when the engine gave a code this build was never taught, and when it gave none at all. */
const GENERIC_FAILURE = "That did not take, and this build has no explanation why. See Agora.log.";

/**
 * Did the write go through? The mirror of `CommandOutcomes.IsAccepted` on the C# side: two of the
 * codes are acceptances, `""` and `OkColorInUse`.
 *
 * This exists because acceptance and "is there something to say" stopped being the same question the
 * moment an accepted write could carry a message. An `outcome === ""` test — or a falsy test on
 * `writeMessage` — reads `OkColorInUse` as a failure, so the panel would revert the swatch while the
 * engine kept the new colour, and the two would disagree until the next republish (contract §4.6).
 *
 * `answered: false` is false here. We did not hear that it took, so we must not act as though it
 * did, and contract rule 5 bars us from inventing an outcome code to explain the silence.
 */
export function isAccepted(result: WriteOutcome): boolean {
  return result.answered && (result.outcome === "" || result.outcome === "OkColorInUse");
}

/**
 * The one sentence to put in front of the player about a write, or `""` when there is nothing to
 * say.
 *
 * An empty message no longer means the write was accepted. `OkColorInUse` is an acceptance
 * that carries a warning, so a falsy check on this string reports a colour that was applied as a
 * failure. Callers deciding whether the write took must ask `isAccepted`; this function only decides
 * what to print, and the two answers are independent.
 */
export function writeMessage(result: WriteOutcome): string {
  if (!result.answered) {
    return GENERIC_FAILURE;
  }
  if (result.outcome === "") {
    return "";
  }
  return OUTCOME_MESSAGE[result.outcome] || GENERIC_FAILURE;
}
