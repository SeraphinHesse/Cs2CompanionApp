import { bindLocalValue, bindMap, bindValue } from "cs2/api";

/**
 * Every binding this panel consumes, declared once.
 *
 * Names are copied verbatim from docs/contracts/ui_bindings.md (frozen for M4). A renamed binding
 * produces an empty panel at runtime, not a build error, so nothing here may be "tidied".
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

export const EMPTY_CITY_INDICES: Agora.CityIndices = {
  gini: 0,
  brainDrain: 0,
  serviceInequality: 0,
  commuteMisery: 0,
  polarization: 0,
  legitimacy: 0,
  discontent: 0,
};

export const EMPTY_DISTRICT_DETAIL: Agora.DistrictDetail = {
  id: "",
  name: "",
  population: 0,
  households: 0,
  eligibleVoters: 0,
  votesCast: 0,
  turnout: 0,
  happiness: 0,
  unemployment: 0,
  winningPartyId: "",
  margin: 0,
  seats: 0,
  decidedByTieBreak: false,
  shares: [],
  wealth: { low: 0, middle: 0, high: 0 },
  education: {
    uneducated: 0,
    poorlyEducated: 0,
    educated: 0,
    wellEducated: 0,
    highlyEducated: 0,
  },
  age: { child: 0, teen: 0, adult: 0, elderly: 0 },
  indices: {
    gentrification: 0,
    commuteMisery: 0,
    serviceCoverage: 0,
    discontent: 0,
    gini: 0,
  },
  budget: {
    averageRent: 0,
    rentBurden: 0,
    averageHouseholdUpkeep: 0,
    averageHouseholdResourceSpend: 0,
    averageHouseholdFees: 0,
    disposableMargin: 0,
  },
  hasCityFallbacks: false,
  cityFallbackFields: [],
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

// -- agora.parties (shared lookup) --------------------------------------------------------------

/** The only place party names and colours come from. Never duplicate them into a district row. */
export const roster$ = bindValue<Agora.PartyBrief[]>("agora.parties", "roster", []);

// -- agora.districts ----------------------------------------------------------------------------

/** Sorted by id ordinal ascending, in C#. The panel does not re-sort. */
export const districtList$ = bindValue<Agora.DistrictBrief[]>("agora.districts", "list", []);

/** Map binding, keyed by DistrictBrief.id. Only the open district is ever fetched. */
export const districtDetail$ = bindMap<string, Agora.DistrictDetail>("agora.districts", "detail");

/** Map binding, keyed by DistrictBrief.id. Exactly 15 cells, wealth then education ascending. */
export const districtCrosstab$ = bindMap<string, Agora.CrosstabCell[]>(
  "agora.districts",
  "crosstab"
);

export const cityCrosstab$ = bindValue<Agora.CrosstabCell[]>("agora.districts", "cityCrosstab", []);

export const cityIndices$ = bindValue<Agora.CityIndices>(
  "agora.districts",
  "cityIndices",
  EMPTY_CITY_INDICES
);

// -- UI-only selection --------------------------------------------------------------------------

/**
 * Which district the panel has open. "" means the city-wide view.
 *
 * Contract section 3: selection state is UI-only and uses bindLocalValue — it must never make a
 * round trip through C#. It lives in this panel's folder because there is no shared UI state
 * module yet; if a sibling panel (or the M6 map overlay) needs to read the same selection, lift
 * this one declaration into a shared module rather than binding a second copy.
 */
export const selectedDistrictId$ = bindLocalValue<string>("");
