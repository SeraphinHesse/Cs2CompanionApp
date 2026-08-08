/**
 * Formatting, issue-axis constants and label helpers for the Parties panel.
 *
 * Nothing here computes politics. Contract rule 5: the UI reads politics, it never derives it —
 * every share, position and count is published by the engine and only re-expressed for a human here
 * (a ratio rendered as a percentage, a signed difference rendered with its sign).
 *
 * There is no shared runtime module between panels, by design (contract §6), so the few helpers
 * that also exist in the Districts panel are copied rather than imported.
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

/** A [0,1] difference as percentage points. */
export function points(value: number, digits: number = 1): string {
  if (typeof value !== "number" || !isFinite(value)) {
    return NO_VALUE;
  }
  return (value * 100).toFixed(digits) + " pts";
}

/**
 * A signed [0,1] difference as percentage points, with the sign always shown. A movement of zero
 * reads "0.0 pts" rather than "+0.0 pts": the engine publishes an exact zero when there is nothing
 * to compare, and a plus sign in front of it would read as a gain.
 */
export function signedPoints(value: number, digits: number = 1): string {
  if (typeof value !== "number" || !isFinite(value)) {
    return NO_VALUE;
  }
  const text = (Math.abs(value) * 100).toFixed(digits) + " pts";
  if (value > 0) {
    return "+" + text;
  }
  if (value < 0) {
    return "-" + text;
  }
  return text;
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

/** A [0,1] share as a CSS width. Widths below a hair are still given a sliver so a tiny bar shows. */
export function widthPct(share: number): string {
  const v = clamp01(share);
  return (v * 100).toFixed(3) + "%";
}

const NEUTRAL_RGB = "150, 152, 162";

const UNKNOWN_COLOR = "#9698a2";

/** Engine-owned, from the tuned palette. A party with no colour degrades to grey, never to a hole. */
export function partyColor(colorHex: string): string {
  return colorHex || UNKNOWN_COLOR;
}

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

// -- party labels ---------------------------------------------------------------------------------

/**
 * Shown wherever a party exists but has no usable name yet — the flavor layer has not authored one.
 * A raw id is never rendered to the player.
 */
const UNNAMED_PARTY = "Unnamed party";

/** FLAVOR. Render it, never parse it, never sort by it. */
export function partyLabel(name: string, shortName: string): string {
  return name || shortName || UNNAMED_PARTY;
}

/** FLAVOR, as above. */
export function partyShortLabel(shortName: string, name: string): string {
  return shortName || name || UNNAMED_PARTY;
}

/** "A", "A and B", "A, B and C". English only (non-negotiable 10), so the conjunction is fixed. */
export function joinNames(names: string[]): string {
  if (names.length === 0) {
    return "";
  }
  if (names.length === 1) {
    return names[0];
  }
  return names.slice(0, names.length - 1).join(", ") + " and " + names[names.length - 1];
}

// -- dates in prose -------------------------------------------------------------------------------

/** English only (non-negotiable 10), so the month names are fixed rather than looked up by locale. */
const MONTH_NAMES = [
  "January",
  "February",
  "March",
  "April",
  "May",
  "June",
  "July",
  "August",
  "September",
  "October",
  "November",
  "December",
];

/**
 * A wire date ("YYYY-MM-DD") as "March 1994", for a sentence rather than a table. Anything that does
 * not parse is returned untouched: a date the pane cannot read is still a date, and dropping it would
 * lose the only thing the sentence was carrying.
 */
export function monthYear(date: string): string {
  const match = /^(\d{4})-(\d{2})-\d{2}$/.exec(date || "");
  if (!match) {
    return date || "";
  }
  const month = parseInt(match[2], 10);
  if (!(month >= 1 && month <= 12)) {
    return date;
  }
  return MONTH_NAMES[month - 1] + " " + match[1];
}

/** The year alone, for a bar label with no room for more. "" when the date does not parse. */
export function yearOf(date: string): string {
  const match = /^(\d{4})-/.exec(date || "");
  return match ? match[1] : "";
}

/**
 * A revival count in words. Never a bare "1": "Revived 1" is not a sentence, and the count is a
 * number of occasions rather than a measurement.
 */
export function revivalPhrase(count: number): string {
  if (typeof count !== "number" || !isFinite(count) || count <= 0) {
    return "";
  }
  if (count === 1) {
    return "once";
  }
  if (count === 2) {
    return "twice";
  }
  return int(count) + " times";
}

/**
 * The pane's one-line faction summary. Names are FLAVOR and come from `agora.parties.factions`; a
 * faction id is never rendered, so a faction the flavor layer has not named yet is counted but not
 * listed. The EU theme models no factions at all, which the sentence states in words rather than
 * leaving an empty box behind a heading.
 */
export function factionSentence(count: number, names: string[]): string {
  if (count <= 0) {
    return "No internal factions are modelled inside this party.";
  }
  const head =
    count === 1
      ? "One internal faction is modelled inside this party"
      : int(count) + " internal factions are modelled inside this party";
  return names.length > 0 ? head + ": " + joinNames(names) + "." : head + ".";
}

// -- issue axes -----------------------------------------------------------------------------------

/** Issues.All order (Issues.cs) — declaration order, and the order every engine sum uses. */
export const ISSUE_ORDER: Agora.IssueName[] = [
  "Services",
  "CostOfLiving",
  "Environment",
  "Transit",
  "Growth",
  "HeritageOrder",
];

/** Plain English. `HeritageOrder` is an enum member name and must never reach the player. */
export const ISSUE_LABEL: Record<Agora.IssueName, string> = {
  Services: "Public services",
  CostOfLiving: "Cost of living",
  Environment: "Environment",
  Transit: "Transit",
  Growth: "Growth",
  HeritageOrder: "Heritage and order",
};

/**
 * What each end of the axis means, from the sign convention documented on `IssuePosition` in
 * `Issues.cs`. `+1` is "spend/protect/restrict more", `-1` is "less".
 */
export const ISSUE_POLE_LOW: Record<Agora.IssueName, string> = {
  Services: "Spend less",
  CostOfLiving: "Revenue first",
  Environment: "Fewer restrictions",
  Transit: "Roads and cars",
  Growth: "Restrain building",
  HeritageOrder: "Open to change",
};

export const ISSUE_POLE_HIGH: Record<Agora.IssueName, string> = {
  Services: "Spend more",
  CostOfLiving: "Affordability first",
  Environment: "Stricter protection",
  Transit: "Buses and trains",
  Growth: "Build more",
  HeritageOrder: "Order and preservation",
};

/** The enum's own one-line description, so a tooltip can say what an axis covers. */
export const ISSUE_NOTE: Record<Agora.IssueName, string> = {
  Services: "Health, education, police, fire, garbage, utilities - is the city looked after.",
  CostOfLiving: "Rent, land value, taxes, unemployment - can people afford to live here.",
  Environment: "Air, ground, noise and water pollution; parks and green space.",
  Transit: "Commute time, transit coverage, traffic, parking.",
  Growth: "Development, jobs, new construction, densification.",
  HeritageOrder: "Crime, order, stability, and resistance to change.",
};

/** Payload key for each issue — `IssuePositionView`'s properties are camelCased enum members. */
export const ISSUE_KEY: Record<Agora.IssueName, keyof Agora.IssuePositionView> = {
  Services: "services",
  CostOfLiving: "costOfLiving",
  Environment: "environment",
  Transit: "transit",
  Growth: "growth",
  HeritageOrder: "heritageOrder",
};

/**
 * A position in [-1, +1] as a readout. Anything inside a fortieth of the axis of dead centre reads
 * as a word rather than as a number pretending to a precision the model does not have.
 */
export const CENTRE_BAND = 0.02;

export function positionText(value: number): string {
  if (typeof value !== "number" || !isFinite(value)) {
    return NO_VALUE;
  }
  if (Math.abs(value) < CENTRE_BAND) {
    return "Centre";
  }
  return (value > 0 ? "+" : "-") + Math.abs(value).toFixed(2);
}

// -- manifesto drift ------------------------------------------------------------------------------

/**
 * How far a position has to have moved before the pane is willing to call it a move.
 *
 * This is a DISPLAY threshold and nothing in the engine reads it. `Agora.Core` has no drift concept,
 * no mandate is scored against it, and no number here re-enters engine state - the two vectors are
 * published, and comparing them is re-expression of the same kind as `pct()`. It exists only so a
 * platform that has wandered a hundredth of an axis between ticks is not announced as a change of
 * mind.
 */
export const MANIFESTO_DRIFT_THRESHOLD = 0.15;

/**
 * Movement between the manifesto a party ran on and where it stands today.
 *
 * `points` counts only the issues that cleared the threshold, so the two figures in the sentence
 * below account for each other: the points reported are the points those issues moved, not a total
 * inflated by five axes that barely twitched. A position is in [-1, +1] and is reported the way
 * every other figure in this panel is - a hundred points to one unit of the axis.
 */
export function manifestoDrift(
  platform: Agora.IssuePositionView,
  manifesto: Agora.IssuePositionView
): { moved: number; points: number } {
  let moved = 0;
  let total = 0;
  for (let i = 0; i < ISSUE_ORDER.length; i++) {
    const key = ISSUE_KEY[ISSUE_ORDER[i]];
    const now = platform ? platform[key] : 0;
    const then = manifesto ? manifesto[key] : 0;
    if (typeof now !== "number" || !isFinite(now) || typeof then !== "number" || !isFinite(then)) {
      continue;
    }
    const delta = Math.abs(now - then);
    if (delta >= MANIFESTO_DRIFT_THRESHOLD) {
      moved++;
      total += delta;
    }
  }
  return { moved: moved, points: Math.round(total * 100) };
}

/**
 * The drift line. Only ever rendered for a party that has contested an election - `LastManifesto`
 * defaults to dead centre, so a party that has never stood would otherwise be reported as having
 * abandoned a platform it never ran on.
 *
 * A party that has not moved is told so in words rather than shown a zero, which would read as a
 * measurement rather than as "nothing happened".
 */
export function manifestoDriftSentence(drift: { moved: number; points: number }): string {
  if (drift.moved <= 0) {
    return "Still standing on the platform it ran on: no issue has moved far from the manifesto.";
  }
  const movement = drift.points === 1 ? "1 point" : int(drift.points) + " points";
  return (
    "Moved " +
    movement +
    " from its manifesto on " +
    int(drift.moved) +
    " of " +
    int(ISSUE_ORDER.length) +
    " issues."
  );
}

// -- government role ------------------------------------------------------------------------------

/**
 * `PartyGovernmentRoleName` is a C# member name and must never reach the player. "None" covers two
 * different situations - no government at all, and a party the government does not name - so the
 * pane says the weaker of the two and lets the coalition line carry the rest.
 */
export const ROLE_CHIP: Record<Agora.PartyGovernmentRoleName, string> = {
  None: "",
  Lead: "Leads the government",
  Member: "In government",
  Opposition: "In opposition",
};

export const ROLE_SENTENCE: Record<Agora.PartyGovernmentRoleName, string> = {
  None: "Not named by the sitting government.",
  Lead: "Leads the governing coalition.",
  Member: "Sits in the governing coalition without leading it.",
  Opposition: "Sits in opposition to the governing coalition.",
};

/** Party statuses are engine-owned enum member names; the rail and the header show these instead. */
export const STATUS_CHIP: Record<Agora.PartyStatusName, string> = {
  Active: "",
  Endangered: "Endangered",
  Dissolved: "Dissolved",
  Merged: "Merged",
  Revived: "Revived",
};
