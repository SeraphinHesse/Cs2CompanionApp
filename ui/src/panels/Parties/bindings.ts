import { bindLocalValue, bindMap, bindValue, call } from "cs2/api";
import { SETTING_CALL_TIMEOUT_MS, WriteOutcome } from "../../shell/bindings";

/**
 * Every binding this panel consumes, declared once.
 *
 * Names are copied verbatim from docs/contracts/ui_bindings.md. A renamed binding produces an empty
 * panel at runtime, not a build error, so nothing here may be "tidied".
 *
 * The `Agora` payload namespace is a GLOBAL declaration in ui/types/bindings.d.ts — it is
 * referenced with no import statement, by design (there is no runtime module behind it and
 * isolatedModules would turn an import into a webpack resolution error).
 */

// -- empty / loading values, copied literally from contract section 6 ----------------------------

export const EMPTY_STATE_SUMMARY: Agora.StateSummary = {
  schemaVersion: 0,
  date: "",
  termNumber: 0,
  system: "Proportional",
  theme: "Eu",
  nextElectionDate: "",
  isCampaignSeason: false,
  weeksToElection: -1,
  mayorPartyId: "",
};

/**
 * The C# side answers an unknown key with its own empty payload, so the two must agree field for
 * field. `colorHex` is "#808080" rather than "" for that reason — a swatch with no colour renders
 * as a hole rather than as grey.
 */
export const EMPTY_PARTY_DETAIL: Agora.PartyDetail = {
  id: "",
  name: "",
  shortName: "",
  colorHex: "#808080",
  archetypeId: "",
  description: "",
  slogan: "",
  platform: {
    services: 0,
    costOfLiving: 0,
    environment: 0,
    transit: 0,
    growth: 0,
    heritageOrder: 0,
  },
  lastManifesto: {
    services: 0,
    costOfLiving: 0,
    environment: 0,
    transit: 0,
    growth: 0,
    heritageOrder: 0,
  },
  seats: 0,
  seatShare: 0,
  lastVoteShare: 0,
  hasContestedElection: false,
  passedThreshold: false,
  consecutiveElectionsBelowThreshold: 0,
  currentPollShare: 0,
  hasPoll: false,
  pollDate: "",
  pollDeltaSinceElection: 0,
  currentStandingShare: 0,
  status: "Active",
  foundedDate: "",
  dissolvedDate: "",
  predecessorPartyId: "",
  successorPartyId: "",
  revivalCount: 0,
  absorbedPartyIds: [],
  governmentRole: "None",
  factionIds: [],
};

/** Contract section 6, literally. An empty palette renders NO swatches - never a picker with none. */
export const EMPTY_PARTY_PALETTE: Agora.PartyPalette = { colors: [] };

/**
 * Contract section 6, literally. All zeroes is not a usable limit - a counter reading `nameMax: 0`
 * would declare every keystroke too long - so the editors are gated on `ready`, exactly as that
 * section instructs, rather than treating the empty value as a real ceiling.
 */
export const EMPTY_PARTY_EDIT_LIMITS: Agora.PartyEditLimits = {
  nameMax: 0,
  shortNameMax: 0,
  descriptionMax: 0,
  sloganMax: 0,
  colorPattern: "",
};

// -- agora.state (chrome) -----------------------------------------------------------------------

/** Master toggle. When false the panel renders null, not a disabled shell. */
export const enabled$ = bindValue<boolean>("agora.state", "enabled", false);

/** True once the engine has published a political state at least once. */
export const ready$ = bindValue<boolean>("agora.state", "ready", false);

export const summary$ = bindValue<Agora.StateSummary>(
  "agora.state",
  "summary",
  EMPTY_STATE_SUMMARY
);

// -- agora.parties --------------------------------------------------------------------------------

/** Sorted by id ordinal ascending, in C#. The panel does not re-sort. Dissolved brands stay in it. */
export const roster$ = bindValue<Agora.PartyBrief[]>("agora.parties", "roster", []);

/**
 * Sorted in C# by partyId ordinal ascending, then internalSupport descending, then id ascending.
 * The pane filters it to the open party to name that party's factions; it does not re-sort, and the
 * rail never touches it — one pushed list for the whole panel, not a lookup per row.
 */
export const factions$ = bindValue<Agora.FactionBrief[]>("agora.parties", "factions", []);

/**
 * Map binding, keyed by PartyBrief.id. Only the open party is ever fetched — the rail fills its
 * seats and poll columns from the two pushed bindings below rather than subscribing this per row,
 * which is the whole reason the detail is a map.
 */
export const partyDetail$ = bindMap<string, Agora.PartyDetail>("agora.parties", "detail");

/**
 * Map binding, keyed by PartyBrief.id: one party's published poll shares over time, OLDEST FIRST.
 *
 * That order is contractual and is the one list in the contract that is not newest-first - a trend
 * reads left to right in time (contract section 4.2). Do not reverse it. Subscribed by the detail
 * pane alone, for the open party, like the detail above.
 */
export const pollTrend$ = bindMap<string, Agora.PollTrendPoint[]>("agora.parties", "pollTrend");

/**
 * Map binding, keyed by PartyBrief.id: one party's result at each election it took part in, OLDEST
 * FIRST and capped at the newest twelve (contract section 4.2). Elections the party had no part in
 * are absent from it, so the series is not a calendar and a gap is not a defeat. Subscribed by the
 * detail pane alone, for the open party, like the two maps above.
 */
export const electionRecord$ = bindMap<string, Agora.PartyElectionRow[]>(
  "agora.parties",
  "electionRecord"
);

/**
 * Map binding, keyed by PartyBrief.id: every viable arrangement of the chamber that contains this
 * party, best first and capped at eight (contract section 4.2). The order is formation order and is
 * contractual - majority first, then minimum-winning, then score - so the pane does not re-sort it.
 *
 * A LIVE view. It is recomputed from where the parties stand TODAY, not the record of who negotiated
 * after the last election, so it answers "who could govern now" and drifts as platforms drift. Empty
 * under first past the post by design.
 *
 * Fetched for the open pane alone, like the three maps above. That is what makes it affordable: the
 * enumeration behind it is bounded but not free, and it must never become a pushed value binding
 * re-running for every party on every tick (contract rule 6).
 */
export const relations$ = bindMap<string, Agora.CoalitionOption[]>("agora.parties", "relations");

/**
 * The tuned chart palette the engine assigns party colours from.
 *
 * Published rather than hard-coded because the array lives in `EngineTuning.Parties.ColorPalette`; a
 * copy in TypeScript would drift the first time the tuning was edited and the drift would be
 * invisible. Rendered in the order published - never re-sorted, never de-duplicated: a swatch's
 * position is how a player recognises it between sessions (contract section 4.2).
 *
 * Not a closed set. `setColor` accepts any legal hex, so the picker offers a free field beside it.
 */
export const colorPalette$ = bindValue<Agora.PartyPalette>(
  "agora.parties",
  "colorPalette",
  EMPTY_PARTY_PALETTE
);

/**
 * What the party editors will accept: four lengths and the colour pattern.
 *
 * The character counters read these and nothing else. A literal in the panel and `PartyIdentity` in
 * C# would be two copies of one number, and when they disagree the wrong one is always the counter -
 * the player finds out by being refused after typing (contract section 4.2).
 */
export const editLimits$ = bindValue<Agora.PartyEditLimits>(
  "agora.parties",
  "editLimits",
  EMPTY_PARTY_EDIT_LIMITS
);

// -- agora.seats (rail columns and the coalition line) ---------------------------------------------

export const allocation$ = bindValue<Agora.SeatRow[]>("agora.seats", "allocation", []);

export const latestPoll$ = bindValue<Agora.PollSummary | null>("agora.seats", "latestPoll", null);

export const government$ = bindValue<Agora.GovernmentSummary | null>(
  "agora.seats",
  "government",
  null
);

// -- agora.news (the mandate scorecard) ------------------------------------------------------------

/**
 * Every mandate in the save, published for the News tab's tracker (contract section 4.5) and read
 * here filtered to the open party as a scorecard: how many it kept, not a second list of rows.
 *
 * The binding is PUBLISHED, not reserved, so consuming it satisfies contract rule 3. Nothing is
 * registered in C# for this - there is deliberately no `agora.parties.mandates`, because a per-party
 * binding would publish the same rows twice.
 *
 * Known cost: this is a pushed list carrying EVERY mandate, so the Parties tab pays its bridge cost
 * whenever it is open. That is already the News panel's cost, it is not per-party, and so it does not
 * grow with the roster. Accepted.
 */
export const mandates$ = bindValue<Agora.MandateRow[]>("agora.news", "mandates", []);

// -- the write channel (contract section 4.2, the six party editors) ---------------------------------

/**
 * Acceptance and the sentence to print, taken from the shell rather than reimplemented.
 *
 * Re-exported so a component in this folder has one import site, but deliberately NOT a second copy:
 * two of the outcome codes are acceptances (`""` and `OkColorInUse`) and a panel that re-derived that
 * rule would be one edit away from disagreeing with the engine. `writeMessage`'s map already covers
 * `NotFound`, `ValueRequired`, `TooLong` and `OkColorInUse`, which are exactly the four these six
 * bindings can return and the settings surface cannot.
 */
export { isAccepted, writeMessage } from "../../shell/bindings";
export type { WriteOutcome } from "../../shell/bindings";

/**
 * One deadline for every write on the dashboard, imported rather than redeclared.
 *
 * `call` hands back a Promise and nothing guarantees it settles; a party editor whose Save button
 * never came back would be indistinguishable from one that had been ignored. The same eight seconds
 * the settings surface waits - the handlers are field writes under one lock, so the round trip is not
 * the thing that would take the time.
 */
const PARTY_CALL_TIMEOUT_MS = SETTING_CALL_TIMEOUT_MS;

/**
 * The shared shape of all six: send, wait, and hand back a `WriteOutcome` whatever happens.
 *
 * `answered: false` is not an outcome code and is never turned into one - contract rule 5 forbids a
 * panel reporting a rejection the C# side did not return, and "we never heard back" is a statement
 * about the bridge, not a verdict about the write. This function decides nothing else: it does not
 * validate, does not trim and does not read the code it is carrying. Trimming is the caller's job
 * (the C# validators judge the raw input and deliberately do not trim); judging is the engine's.
 */
function requestPartyWrite(
  label: string,
  send: () => Promise<Agora.CommandOutcomeName>
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
    }, PARTY_CALL_TIMEOUT_MS);

    // The throw guard is for the binding not being registered at all - outside a loaded game the
    // publishing system may not exist, and that must not escape as an unhandled rejection.
    try {
      send().then(
        function (outcome) {
          finish({ answered: true, outcome: outcome });
        },
        function (error) {
          console.warn("[AGORA] " + label + " failed on the bridge", error);
          finish({ answered: false });
        }
      );
    } catch (error) {
      console.warn("[AGORA] " + label + " could not be sent", error);
      finish({ answered: false });
    }
  });
}

/**
 * Rename a party. BOTH fields travel, and that is a requirement rather than a convenience:
 * `nameLocked` covers `name` AND `shortName`, so a rename that could not also set the short name
 * would take ownership of the short name and freeze it - flavor is barred from writing it from that
 * moment and nothing else could.
 */
export function requestRename(
  partyId: string,
  name: string,
  shortName: string
): Promise<WriteOutcome> {
  return requestPartyWrite("rename", function () {
    return call<Agora.CommandOutcomeName>("agora.parties", "rename", partyId, name, shortName);
  });
}

/** Rewrite a party's blurb. Both fields again: `descriptionLocked` covers the slogan as well. */
export function requestDescription(
  partyId: string,
  description: string,
  slogan: string
): Promise<WriteOutcome> {
  return requestPartyWrite("setDescription", function () {
    return call<Agora.CommandOutcomeName>(
      "agora.parties",
      "setDescription",
      partyId,
      description,
      slogan
    );
  });
}

/**
 * Recolour a party. "#RRGGBB"; the engine normalises to upper case, so the value that comes back on
 * the next roster publish may differ in case from what was sent. A colour another party already
 * wears is ACCEPTED, under `OkColorInUse` - a warning to render, not a refusal.
 */
export function requestColor(partyId: string, colorHex: string): Promise<WriteOutcome> {
  return requestPartyWrite("setColor", function () {
    return call<Agora.CommandOutcomeName>("agora.parties", "setColor", partyId, colorHex);
  });
}

/**
 * Hand the name and short name back to flavor. The name RE-ROLLS on the spot - unlike the two resets
 * below, this one has a visible effect immediately.
 *
 * A reset on a field that is not locked is a no-op returning `""`. The resets are idempotent and the
 * panel must not suppress the call to avoid a refusal that does not happen.
 */
export function requestResetName(partyId: string): Promise<WriteOutcome> {
  return requestPartyWrite("resetName", function () {
    return call<Agora.CommandOutcomeName>("agora.parties", "resetName", partyId);
  });
}

/**
 * Hand the description and slogan back to flavor. The text is left EXACTLY as it stands; flavor
 * reclaims the field at its next wake, which may be months of sim time away. A promise about the
 * future, not a visible change, and the copy beside this control has to say so.
 */
export function requestResetDescription(partyId: string): Promise<WriteOutcome> {
  return requestPartyWrite("resetDescription", function () {
    return call<Agora.CommandOutcomeName>("agora.parties", "resetDescription", partyId);
  });
}

/** Hand the colour back to the engine, which reassigns from the tuned palette. */
export function requestResetColor(partyId: string): Promise<WriteOutcome> {
  return requestPartyWrite("resetColor", function () {
    return call<Agora.CommandOutcomeName>("agora.parties", "resetColor", partyId);
  });
}

// -- UI-only selection ------------------------------------------------------------------------------

/**
 * Which party the panel has open. "" means "no choice made yet", which the panel resolves to the
 * first published row at render time — it is never written back from an effect.
 *
 * Contract section 3: selection state is UI-only and uses bindLocalValue — it must never make a
 * round trip through C#. It lives in this panel's folder because there is no shared UI state
 * module yet; if a sibling panel needs to read the same selection, lift this one declaration into
 * a shared module rather than binding a second copy.
 */
export const selectedPartyId$ = bindLocalValue<string>("");
