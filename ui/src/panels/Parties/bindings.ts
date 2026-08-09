import { bindLocalValue, bindMap, bindValue } from "cs2/api";

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
