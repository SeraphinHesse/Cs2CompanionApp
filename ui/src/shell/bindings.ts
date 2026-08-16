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
  storiesEnabled: true, storiesPerCycle: 2, eventsPerStory: 3,
  politicalPowerEnabled: true, powerIntensity: "Default", storyDifficulty: "Default",
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

/**
 * The fallback for `power$`.
 *
 * `enabled` is `false` here on purpose, and it is the one field worth arguing about. Before the
 * engine has published anything we do not know whether this save runs the power layer, and the
 * counter's rule is "hide when off" — so an empty value claiming `true` would flash a balance of 0
 * on every load of a save that has the layer switched off, which reads as "you have no power" rather
 * than as "there is no such currency here".
 */
export const EMPTY_POWER: Agora.Power = {
  enabled: false, balance: 0, lifetimeEarned: 0, lifetimeSpent: 0, inDebt: false, ledger: [],
};

/**
 * The guard a card substitutes for a queue index that no longer exists, so a render racing an ack
 * cannot read a field off `undefined`. Not wired into a binding — `storyAlerts$` is an array and
 * takes `[]`.
 *
 * `major` is `false` here for the same reason it is on `EMPTY_NEWS_ALERT`: an empty card must never
 * be the thing that takes the pause barrier.
 */
export const EMPTY_STORY_ALERT: Agora.StoryAlert = {
  id: "", date: "", headline: "", summary: "", slotCount: 0, major: false,
};

/**
 * The fallback for a story body that has not arrived. Every field empty — a story with no prose at
 * all cannot happen once it has drafted (the canned pool writes one immediately), so this is the
 * shape for an id the map does not hold, not a state the player should ever see.
 */
export const EMPTY_STORY_ARTICLE: Agora.StoryArticle = {
  storyId: "", poolHeadline: "", poolArticle: "", cliHeadline: "", cliArticle: "",
  poolResolutionHeadline: "", poolResolutionArticle: "",
  cliResolutionHeadline: "", cliResolutionArticle: "",
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

// -- agora.stories (§4.7) ------------------------------------------------------------------------
//
// Declared HERE rather than in ui/src/panels/Stories/bindings.ts, and that is a wave-6 structural
// decision rather than an accident. Three separate surfaces read this group — the Stories panel, the
// power counter beside the mod icon, and the story card, which are three different mount points in
// three different React trees. A `bindValue` is a subscription, so declaring each one at module
// scope in one module is what makes them a single shared instance rather than three; and it is what
// keeps the binding NAMES in one place, since a rename here produces a panel that renders nothing,
// at runtime, with no build error.

/**
 * The live stories, sorted by id ordinal. Bodies are not here — fetch them per story from
 * `agora.stories.article` keyed on the same id.
 */
export const stories$ = bindValue<Agora.Story[]>("agora.stories", "live", []);

/** The resolved archive, newest first. Do not re-sort it. */
export const storyArchive$ = bindValue<Agora.StoryBrief[]>("agora.stories", "archive", []);

/**
 * The political-power counter.
 *
 * `enabled` false means this save has the power layer switched off, and the counter must HIDE rather
 * than render a zero — a zero is a balance, and "off" is not one.
 */
export const power$ = bindValue<Agora.Power>("agora.stories", "power", EMPTY_POWER);

/**
 * The unanswered story cards, OLDEST FIRST — the order they happened in. Do not sort it and do not
 * reverse it, for the same reason as `alerts$`.
 *
 * Read by the shell rather than by the Stories panel because the card is chrome: it has to appear
 * with the dashboard closed, over whatever the player was doing.
 */
export const storyAlerts$ = bindValue<Agora.StoryAlert[]>("agora.stories", "alerts", []);

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

// -- the story write channels (§4.7) -------------------------------------------------------------
//
// FIVE, not the three the rework plan's table lists. The plan assumed a purchase would travel as an
// ordinary response through `setResponse`; wave 4 refuses that, because a `PowerOverride` arriving
// that way would be a free `Met` nobody paid for — so the purchase has its own channel. The fifth is
// the card dismissal.
//
// Every one carries the same deadline as `requestSetting`, and on the same reasoning as `ackAlert`:
// a story card may hold the pause barrier, and a call that never answers would leave a player with a
// card they cannot close and a clock they cannot start.
//
// All five only SEND. The returned code is the engine's verdict and the panel renders it; a panel
// that computes a rejection of its own violates contract rule 5. In particular: do not check
// affordability in the UI before sending an override. `canAfford` on the slot is for what the button
// LOOKS like; whether the purchase happens is `spendPowerOverride`'s answer, and the two are read at
// different moments.

/**
 * Choose how to tackle one event.
 *
 * `mode` is a `SlotResponseName` other than `"Unaddressed"` — that is the state before a choice, not
 * a choice. **`"PowerOverride"` is rejected here with `BadValue`**: a purchase goes through
 * `spendPowerOverride`, which is the channel that charges for it.
 *
 * `text` is the player's own words for `"Ignore"` and `"Manual"`, capped at
 * `stories.freeTextMaxLength` and answered with `TooLong` when over. It is prose and is never parsed
 * for a number.
 */
export function setStoryResponse(
  storyId: string, eventId: string, mode: Agora.SlotResponseName, text: string,
): Promise<WriteOutcome> {
  return withDeadline("setStoryResponse(" + storyId + "/" + eventId + ")", function () {
    return call<Agora.CommandOutcomeName>(
      "agora.stories", "setResponse", storyId, eventId, mode, text,
    );
  });
}

/**
 * Declare the outcome of a `"Manual"` slot yourself.
 *
 * Only legal on a slot whose response is already `"Manual"` — anything else answers `BadValue`. A
 * declared SUCCESS requires a justification and answers `ValueRequired` on an empty box; a declared
 * FAILURE does not, because nobody has to explain admitting one.
 *
 * The award for a self-declared success is capped at the MINOR rate whatever the event's tier. The
 * penalty for a self-declared failure is charged at the real tier — see `PoliticalPowerState` for
 * why capping both sides is a trap.
 */
export function declareManualOutcome(
  storyId: string, eventId: string, met: boolean, text: string,
): Promise<WriteOutcome> {
  return withDeadline("declareManualOutcome(" + storyId + "/" + eventId + ")", function () {
    return call<Agora.CommandOutcomeName>(
      "agora.stories", "declareManual", storyId, eventId, met, text,
    );
  });
}

/**
 * Close a story early rather than waiting for its resolve month.
 *
 * Answers `AlreadyResolved` on a story whose window has closed — which is a different answer from
 * `NotFound`, and the difference matters: the record exists, the moment passed. Pressing it more than
 * once is accepted each time and resolves once.
 */
export function resolveStoryNow(storyId: string): Promise<WriteOutcome> {
  return withDeadline("resolveStoryNow(" + storyId + ")", function () {
    return call<Agora.CommandOutcomeName>("agora.stories", "resolveNow", storyId);
  });
}

/**
 * Buy one slot off with political power.
 *
 * `InsufficientPower` and `PowerDisabled` are DIFFERENT refusals and must not be collapsed: one says
 * "not yet", the other says "not in this save". Buying a slot that is already bought answers `""`
 * and charges nothing — the guard is in the engine, so a double-press cannot double-charge.
 */
export function spendPowerOverride(storyId: string, eventId: string): Promise<WriteOutcome> {
  return withDeadline("spendPowerOverride(" + storyId + "/" + eventId + ")", function () {
    return call<Agora.CommandOutcomeName>(
      "agora.stories", "spendPowerOverride", storyId, eventId,
    );
  });
}

/**
 * Dismiss a story card, or all of them with `"*"`.
 *
 * **This answers nothing.** It closes the interruption; the story stays live and is still answered
 * from the Stories panel. Acking an id the queue no longer holds answers `""`, not a refusal — a
 * double-click is not an error.
 */
export function ackStoryAlert(id: string): Promise<WriteOutcome> {
  return withDeadline("ackStoryAlert(" + id + ")", function () {
    return call<Agora.CommandOutcomeName>("agora.stories", "ackAlert", id);
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
  // No number in these two either, and for a sharper reason than TooLong's. The price is published
  // per slot as `overrideCost` and the balance as `power.balance`; a literal here would be a third
  // copy of an amount the engine charges, and it would be wrong the moment the power economy is
  // retuned. The two are kept apart because they are different refusals: one is "not yet", the other
  // is "not in this save", and telling a player to save up for a purchase that can never happen is
  // worse than saying nothing.
  InsufficientPower: "There is not enough political power to buy this one off.",
  PowerDisabled: "This city is not running the political-power system, so nothing can be bought off.",
  AlreadyResolved: "This story has already closed. Its verdict is in the archive.",
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
