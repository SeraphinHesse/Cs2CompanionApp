/**
 * Formatting, axis constants and lookup helpers for the Districts panel.
 *
 * Nothing here computes politics. Contract rule 5: the UI reads politics, it never derives it —
 * every share, index and count is published by the engine and only re-expressed for a human here
 * (a ratio rendered as a percentage, an integer rendered with separators).
 */

export const NO_VALUE = "-";

export function clamp01(v: number): number {
  if (!isFinite(v)) {
    return 0;
  }
  if (v < 0) {
    return 0;
  }
  if (v > 1) {
    return 1;
  }
  return v;
}

/** A [0,1] share as a percentage. The engine never pre-multiplies by 100 (contract section 2). */
export function pct(value: number, digits: number = 0): string {
  if (typeof value !== "number" || !isFinite(value)) {
    return NO_VALUE;
  }
  return (value * 100).toFixed(digits) + "%";
}

/** A [0,1] difference as percentage points, for margins. */
export function points(value: number, digits: number = 1): string {
  if (typeof value !== "number" || !isFinite(value)) {
    return NO_VALUE;
  }
  return (value * 100).toFixed(digits) + " pts";
}

/** Happiness is the one 0-100 quantity on the bridge. */
export function happinessText(value: number): string {
  if (typeof value !== "number" || !isFinite(value)) {
    return NO_VALUE;
  }
  return String(Math.round(value));
}

/**
 * Thousands separators without Intl — Gameface's JS runtime is embedded and its locale data is not
 * something this mod should depend on. English only (non-negotiable 10), so the separator is ",".
 */
export function int(value: number): string {
  if (typeof value !== "number" || !isFinite(value)) {
    return NO_VALUE;
  }
  const rounded = Math.round(value);
  const negative = rounded < 0;
  let digits = String(Math.abs(rounded));
  let out = "";
  while (digits.length > 3) {
    out = "," + digits.slice(digits.length - 3) + out;
    digits = digits.slice(0, digits.length - 3);
  }
  out = digits + out;
  return negative ? "-" + out : out;
}

/** A [0,1] share as a CSS width. Widths below a hair are still given a sliver so a tiny party shows. */
export function widthPct(share: number): string {
  const v = clamp01(share);
  return (v * 100).toFixed(3) + "%";
}

const NEUTRAL_RGB = "150, 152, 162";

/** "#RRGGBB" to an rgba() string. An unparsable colour degrades to neutral grey, never to nothing. */
export function hexToRgba(hex: string, alpha: number): string {
  const a = clamp01(alpha);
  const match = /^#?([0-9a-fA-F]{6})$/.exec(hex || "");
  if (!match) {
    return "rgba(" + NEUTRAL_RGB + ", " + a.toFixed(3) + ")";
  }
  const packed = parseInt(match[1], 16);
  const r = (packed >> 16) & 255;
  const g = (packed >> 8) & 255;
  const b = packed & 255;
  return "rgba(" + r + ", " + g + ", " + b + ", " + a.toFixed(3) + ")";
}

// -- axes ---------------------------------------------------------------------------------------

/**
 * The crosstab axis order IS the contract sort order for agora.districts.crosstab:
 * wealth ascending (Low, Middle, High) then education ascending. Rendering the matrix from these
 * constants and looking each cell up by key means the panel neither re-sorts the payload nor
 * breaks if a cell is ever missing.
 */
export const WEALTH_TIERS: Agora.WealthTierName[] = ["Low", "Middle", "High"];

export const EDUCATION_TIERS: Agora.EducationTierName[] = [
  "Uneducated",
  "PoorlyEducated",
  "Educated",
  "WellEducated",
  "HighlyEducated",
];

export const WEALTH_LABEL: Record<Agora.WealthTierName, string> = {
  Low: "Low",
  Middle: "Mid",
  High: "High",
};

export const WEALTH_FULL: Record<Agora.WealthTierName, string> = {
  Low: "Low wealth",
  Middle: "Middle wealth",
  High: "High wealth",
};

/** Short enough to survive the narrowest column Gameface will hand this panel. */
export const EDUCATION_LABEL: Record<Agora.EducationTierName, string> = {
  Uneducated: "None",
  PoorlyEducated: "Poor",
  Educated: "Educ",
  WellEducated: "Well",
  HighlyEducated: "High",
};

export const EDUCATION_FULL: Record<Agora.EducationTierName, string> = {
  Uneducated: "Uneducated",
  PoorlyEducated: "Poorly educated",
  Educated: "Educated",
  WellEducated: "Well educated",
  HighlyEducated: "Highly educated",
};

export function cellKey(wealth: Agora.WealthTierName, education: Agora.EducationTierName): string {
  return wealth + "|" + education;
}

// -- party lookup -------------------------------------------------------------------------------

export type PartyIndex = Record<string, Agora.PartyBrief>;

/**
 * Index the roster by id. Lookups only — this map is never iterated for ordering, so it introduces
 * no iteration-order dependence.
 */
export function indexParties(roster: Agora.PartyBrief[]): PartyIndex {
  const index: PartyIndex = {};
  if (!roster) {
    return index;
  }
  for (let i = 0; i < roster.length; i++) {
    const party = roster[i];
    if (party && party.id) {
      index[party.id] = party;
    }
  }
  return index;
}

const UNKNOWN_COLOR = "#9698a2";

/**
 * Shown wherever a party exists but has no usable name yet — either it has aged out of the roster
 * or the flavor layer has not authored one. A raw id is never rendered to the player.
 */
const UNNAMED_PARTY = "Unnamed party";

/** FLAVOR. Render it, never parse it, never sort by it. */
export function partyName(index: PartyIndex, id: string): string {
  if (!id) {
    return "No party";
  }
  const party = index[id];
  if (!party) {
    return UNNAMED_PARTY;
  }
  return party.name || party.shortName || UNNAMED_PARTY;
}

/** FLAVOR, as above. */
export function partyShort(index: PartyIndex, id: string): string {
  if (!id) {
    return NO_VALUE;
  }
  const party = index[id];
  if (!party) {
    return UNNAMED_PARTY;
  }
  return party.shortName || party.name || UNNAMED_PARTY;
}

/** Engine-owned, from the tuned palette. */
export function partyColor(index: PartyIndex, id: string): string {
  if (!id) {
    return UNKNOWN_COLOR;
  }
  const party = index[id];
  if (!party || !party.colorHex) {
    return UNKNOWN_COLOR;
  }
  return party.colorHex;
}

// -- city fallbacks -----------------------------------------------------------------------------

/**
 * `hasCityFallbacks` is a rendering obligation, not decoration (contract section 4.4): when it is
 * true, every field named in `cityFallbackFields` is a city number wearing a district's name, and
 * the panel must never present it as a local fact.
 *
 * The publisher sends property names. This matcher is deliberately forgiving about casing and
 * about group-vs-leaf naming ("indices", "gini" and "indices.gini" all mark the same field),
 * because the cost of a missed match is a city number silently rendered as district truth.
 */
export interface FallbackSet {
  /** True when any field fell back. */
  readonly any: boolean;
  /** The published field names, in the published order (property name ascending). */
  readonly fields: string[];
  /** Does this field carry a city value? */
  has(field: string): boolean;
}

function normalizeField(name: string): string {
  return String(name || "")
    .toLowerCase()
    .replace(/[^a-z0-9.]/g, "");
}

const EMPTY_FALLBACKS: FallbackSet = {
  any: false,
  fields: [],
  has: function () {
    return false;
  },
};

export function makeFallbackSet(hasCityFallbacks: boolean, fields: string[]): FallbackSet {
  if (!hasCityFallbacks) {
    return EMPTY_FALLBACKS;
  }
  // hasCityFallbacks true with an empty field list is still surfaced at district level: the
  // district is flagged as partly city-derived even when nothing can be marked field by field.
  const list = fields && fields.length ? fields : [];
  const keys: Record<string, boolean> = {};
  for (let i = 0; i < list.length; i++) {
    const normalized = normalizeField(list[i]);
    if (!normalized) {
      continue;
    }
    keys[normalized] = true;
    const parts = normalized.split(".");
    for (let p = 0; p < parts.length; p++) {
      if (parts[p]) {
        keys[parts[p]] = true;
      }
    }
  }
  return {
    any: true,
    fields: list,
    has: function (field: string): boolean {
      const normalized = normalizeField(field);
      if (!normalized) {
        return false;
      }
      if (keys[normalized]) {
        return true;
      }
      const parts = normalized.split(".");
      for (let p = 0; p < parts.length; p++) {
        if (parts[p] && keys[parts[p]]) {
          return true;
        }
      }
      return false;
    },
  };
}

/** Turn "eligibleVoters" or "indices.gini" into "Eligible voters" / "Indices gini" for a chip. */
export function humanizeField(name: string): string {
  const raw = String(name || "").replace(/\./g, " ");
  const spaced = raw.replace(/([a-z0-9])([A-Z])/g, "$1 $2").replace(/_/g, " ");
  const lower = spaced.toLowerCase().replace(/\s+/g, " ").trim();
  if (!lower) {
    return NO_VALUE;
  }
  return lower.charAt(0).toUpperCase() + lower.slice(1);
}
