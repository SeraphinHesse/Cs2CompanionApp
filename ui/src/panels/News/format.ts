/**
 * Display formatting for the News panel. English only.
 *
 * Everything here is presentation over already-published values. Nothing derives a new political
 * number: no share is recomputed, no progress is recalculated, nothing is re-sorted. Where a value
 * is clamped it is clamped only to keep a CSS width inside its track.
 *
 * `toLocaleString` is deliberately avoided — Gameface ships a trimmed JS runtime and locale data
 * is not guaranteed to be present, so thousands grouping is done by hand.
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
 * "YYYY-MM-DD" -> "Mar 1994". The wire format is engine-owned, so splitting it is safe;
 * an absent date is "" (never null) and renders as an em dash.
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

/** Clamp to [0,1]. Guards a CSS width against a NaN or an out-of-range value on the wire. */
export function clamp01(value: number): number {
  if (typeof value !== "number" || !isFinite(value)) {
    return 0;
  }
  if (value < 0) {
    return 0;
  }
  return value > 1 ? 1 : value;
}

/** A [0,1] share as a whole percentage. C# never pre-multiplies by 100 (contract section 2). */
export function formatPercent(value: number): string {
  return String(Math.round(clamp01(value) * 100)) + "%";
}

/**
 * A metric value in its own units. The contract deliberately does not say what those units are
 * per metric, so no unit suffix is invented here — the metric name is rendered beside it instead.
 */
export function formatNumber(value: number): string {
  if (typeof value !== "number" || !isFinite(value)) {
    return "—";
  }
  const magnitude = Math.abs(value);
  const rounded = magnitude >= 1000 ? Math.round(value) : Math.round(value * 100) / 100;

  let text = String(rounded);
  const negative = text.charAt(0) === "-";
  if (negative) {
    text = text.substring(1);
  }

  const dot = text.indexOf(".");
  let whole = dot >= 0 ? text.substring(0, dot) : text;
  const fraction = dot >= 0 ? text.substring(dot) : "";

  let grouped = "";
  while (whole.length > 3) {
    grouped = "," + whole.substring(whole.length - 3) + grouped;
    whole = whole.substring(0, whole.length - 3);
  }
  grouped = whole + grouped;

  return (negative ? "-" : "") + grouped + fraction;
}

/**
 * "AverageCommuteMinutes" -> "Average Commute Minutes". Enum member names are engine-owned, so
 * spacing them for display is safe; flavor strings are never passed through here.
 */
export function humanizeEnum(name: string): string {
  if (!name) {
    return "";
  }
  return name.replace(/([a-z0-9])([A-Z])/g, "$1 $2");
}

/**
 * Deadline copy. A mandate whose metric is unreadable is HELD, not failing — its clock is not
 * described as running out, because the engine will not defy it for a stall it did not cause.
 */
export function formatMonthsRemaining(monthsRemaining: number, isStalled: boolean): string {
  if (isStalled) {
    return "Clock held";
  }
  if (monthsRemaining > 1) {
    return String(monthsRemaining) + " months left";
  }
  if (monthsRemaining === 1) {
    return "1 month left";
  }
  if (monthsRemaining === 0) {
    return "Due this month";
  }
  const overdue = -monthsRemaining;
  return overdue === 1 ? "1 month overdue" : String(overdue) + " months overdue";
}

/**
 * Split an article body into display paragraphs.
 *
 * This is layout, not parsing: the prose is never inspected for meaning and no number is ever
 * read out of it (non-negotiable 1). Splitting on newlines simply stops a 120-word body being
 * rendered as one undifferentiated wall, without relying on `white-space: pre-wrap` being
 * implemented in Gameface.
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

/** Severity is 1-5 for event-derived items and 0 otherwise. */
export const SEVERITY_STEPS = [1, 2, 3, 4, 5];
