/**
 * Display formatting for the Stories panel. English only.
 *
 * Presentation over already-published values, and nothing else. No tier is derived from a severity,
 * no outcome is inferred from a response, no affordability is judged, and no cycle length is
 * computed: every one of those is the engine's verdict and arrives on the wire (contract §4.7).
 *
 * These are presentation helpers, which contract §6 explicitly allows a panel to hold its own copies
 * of. What may never be copied is a rule that decides whether a write took — `isAccepted` and
 * `writeMessage` are imported from `ui/src/shell/bindings.ts` and are deliberately absent here.
 *
 * `toLocaleString` is avoided for the same reason the News panel avoids it: Gameface ships a trimmed
 * JS runtime and locale data is not guaranteed to be present.
 */

/** Class-name joiner. Hand-rolled so the panel pulls in no runtime dependency. */
export function cx(...parts: (string | false | null | undefined)[]): string {
  const out: string[] = [];
  for (let i = 0; i < parts.length; i++) {
    const part = parts[i];
    if (part) {
      out.push(part);
    }
  }
  return out.join(" ");
}

const MONTHS = [
  "Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec",
];

/**
 * "YYYY-MM-DD" -> "Mar 1994". The wire format is engine-owned (contract §2), so splitting it is
 * safe; an absent date is "" (never null) and renders as an em dash.
 */
export function formatSimDate(date: string): string {
  if (!date) {
    return "—";
  }
  const parts = date.split("-");
  if (parts.length < 2) {
    return date;
  }
  const monthIndex = parseInt(parts[1], 10) - 1;
  if (isNaN(monthIndex) || monthIndex < 0 || monthIndex > 11) {
    return date;
  }
  return MONTHS[monthIndex] + " " + parts[0];
}

/** A wire date as a whole number of months, or null when it is absent or unreadable. */
function totalMonths(date: string): number | null {
  if (!date) {
    return null;
  }
  const parts = date.split("-");
  if (parts.length < 2) {
    return null;
  }
  const year = parseInt(parts[0], 10);
  const month = parseInt(parts[1], 10);
  if (isNaN(year) || isNaN(month) || month < 1 || month > 12) {
    return null;
  }
  return year * 12 + (month - 1);
}

/** Whole months from one wire date to another, or null when either is absent or unreadable. */
export function monthsBetween(from: string, to: string): number | null {
  const a = totalMonths(from);
  const b = totalMonths(to);
  if (a === null || b === null) {
    return null;
  }
  return b - a;
}

/**
 * How long is left on a live story, measured from today's political date to its own published
 * resolve month.
 *
 * **Nothing here knows what `stories.cycleMonths` is, and nothing here may learn.** A story drafts on
 * one phase and resolves on the next, so the window a player can act in is one month shorter than the
 * cycle, and every attempt in this rework to state that window from a cycle length has been wrong.
 * The two dates the engine publishes are the window; this subtracts them and says so.
 *
 * "" when today's date has not been published yet — the panel says nothing rather than guessing.
 */
export function formatTimeLeft(today: string, resolvesDate: string): string {
  const gap = monthsBetween(today, resolvesDate);
  if (gap === null) {
    return "";
  }
  if (gap > 1) {
    return String(gap) + " months left";
  }
  if (gap === 1) {
    return "1 month left";
  }
  if (gap === 0) {
    return "Resolves this month";
  }
  return "Its resolve month has passed";
}

/**
 * The story's own window, from its published pair of dates. Same rule as `formatTimeLeft`: the
 * distance between the two dates is the answer, and no cycle length is involved in getting it.
 */
export function formatWindow(openedDate: string, resolvesDate: string): string {
  const opened = formatSimDate(openedDate);
  const resolves = formatSimDate(resolvesDate);
  const span = monthsBetween(openedDate, resolvesDate);
  const spanText =
    span === null ? "" : span === 1 ? " · one month to answer"
      : span > 1 ? " · " + String(span) + " months to answer"
        : "";
  return "Opened " + opened + ", resolves " + resolves + spanText;
}

/**
 * What a slot's event is called.
 *
 * **A published name of "" means the civic catalog no longer explains that event** — say so in
 * words. The `eventId` is never a fallback here: a raw id on screen looks like it worked, and this
 * repo has fixed that defect twice (contract §4.7 / `StorySlot.name`).
 */
export const UNNAMED_EVENT = "an event this build cannot name";

export function slotTitle(slot: Agora.StorySlot): string {
  return slot.name || UNNAMED_EVENT;
}

/**
 * The player's response, in English.
 *
 * **"Unaddressed" and "Ignore" are different states and both have to be visible.** They score
 * identically — both resolve not-met — and that is a scoring rule, not a claim that they are the
 * same thing. "You have not answered this yet" is the only signal a player has that there is work
 * outstanding, and collapsing the two loses it. See the doc comment on `SlotResponse` in
 * `src/Agora.Core/Stories/Story.cs`.
 */
const RESPONSE_LABEL: { [response: string]: string } = {
  Unaddressed: "Not answered yet",
  Ignore: "You chose to let it go",
  Goal: "You took it on",
  PowerOverride: "Bought off with political power",
  Manual: "You are handling it yourself",
};

/** An unteachable response name degrades to nothing rather than printing engine vocabulary. */
export function responseLabel(response: Agora.SlotResponseName): string {
  return RESPONSE_LABEL[response] || "";
}

/**
 * How a slot came out.
 *
 * **`Unmeasurable` is HELD, not failed.** It means the engine could not read the city, it costs the
 * player nothing, and it is excluded from the archive row's scored count — the same rule §4.5 states
 * for a stalled mandate. It is also not "the player did not respond": silence resolves as `NotMet`.
 */
const OUTCOME_LABEL: { [outcome: string]: string } = {
  Pending: "Still open",
  Met: "Met",
  NotMet: "Not met",
  Unmeasurable: "Held — the city could not be read",
};

export function slotOutcomeLabel(outcome: Agora.SlotOutcomeName): string {
  return OUTCOME_LABEL[outcome] || "";
}

const STORY_OUTCOME_LABEL: { [outcome: string]: string } = {
  Pending: "Open",
  Success: "Success",
  Failure: "Failure",
  Abandoned: "Abandoned — the evidence was gone",
};

export function storyOutcomeLabel(outcome: Agora.StoryOutcomeName): string {
  return STORY_OUTCOME_LABEL[outcome] || "";
}

/**
 * The tier's one-line meaning. The tier itself is `slot.tier`, the engine's projection of a severity
 * through the tuned thresholds — **never derived here from `slot.severity`**, which ships for display
 * only. A second vocabulary would drift into disagreeing with the price the engine charges.
 */
const TIER_NOTE: { [tier: string]: string } = {
  Mandatory: "The city will not let this one pass.",
  Major: "This one carries weight.",
  Minor: "A smaller matter.",
};

export function tierNote(tier: Agora.StoryTierName): string {
  return TIER_NOTE[tier] || "";
}

/**
 * Split prose into display paragraphs. Layout, not parsing: the text is never inspected for meaning
 * and no number is ever read out of it (non-negotiable 1).
 */
export function splitParagraphs(body: string): string[] {
  if (!body) {
    return [];
  }
  const rawParts = body.split(/\n+/);
  const out: string[] = [];
  for (let i = 0; i < rawParts.length; i++) {
    const trimmed = rawParts[i].trim();
    if (trimmed) {
      out.push(trimmed);
    }
  }
  return out;
}

/** How many of a story's slots the player has not answered at all. Counted, never inferred. */
export function unansweredCount(slots: Agora.StorySlot[]): number {
  let count = 0;
  for (let i = 0; i < slots.length; i++) {
    if (slots[i].response === "Unaddressed") {
      count += 1;
    }
  }
  return count;
}
