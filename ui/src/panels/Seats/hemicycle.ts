/**
 * Hemicycle geometry — pure arithmetic, no bindings, no DOM, no React.
 *
 * Produces one seat slot per seat, ordered LEFT to RIGHT across the arc, so a caller can paint
 * parties into the slots in display order and get contiguous wedges. Keeping this separate from
 * the component means the layout can be reasoned about (and later tested) without a renderer.
 *
 * All values are in `rem`, which is the CS2 UI's scaling unit — the game scales rem with the
 * interface size setting, so a rem-based chart tracks the rest of the HUD.
 *
 * Everything here is deterministic: same seat count in, same layout out. No randomness, no
 * dependence on object key order.
 */

export interface SeatSlot {
  /** Centre of the seat dot, in rem, relative to the chart box's top-left corner. */
  x: number;
  y: number;
  /** Distance from the hemicycle centre, in rem. Used as a stable tie-break: inner rows first. */
  r: number;
  /** PI at the far left of the arc, 0 at the far right. */
  angle: number;
}

export interface HemicycleLayout {
  /** Left-to-right ordered slots. Length equals the seat count that was asked for. */
  slots: SeatSlot[];
  width: number;
  height: number;
  /** Dot diameter in rem. 0 when there is nothing to draw. */
  dot: number;
}

/** Breathing room around the arc, in rem. Must exceed MAX_DOT / 2 or edge dots clip. */
const PAD = 8;
/** Innermost row radius as a fraction of the outermost. 0.4-0.45 reads as a parliament. */
const INNER_RATIO = 0.42;
const MAX_ROWS = 7;
const MIN_DOT = 3;
const MAX_DOT = 13;

function clamp(value: number, lo: number, hi: number): number {
  if (value < lo) return lo;
  if (value > hi) return hi;
  return value;
}

/**
 * Lay out `seatCount` seats in a hemicycle `width` rem wide.
 *
 * Rows scale with the square root of the seat count, so a 15-seat council and a 120-seat assembly
 * both read as an arc rather than as a thin ribbon or a solid blob. Seats are shared out between
 * rows in proportion to each row's radius, so dot density stays even.
 */
export function buildHemicycle(seatCount: number, width: number): HemicycleLayout {
  const outerR = width / 2 - PAD;
  if (seatCount <= 0 || outerR <= 0) {
    return { slots: [], width: width > 0 ? width : 0, height: PAD * 2, dot: 0 };
  }

  const rows = clamp(Math.ceil(Math.sqrt(seatCount / 3)), 1, MAX_ROWS);
  const innerR = outerR * INNER_RATIO;
  const rowStep = rows > 1 ? (outerR - innerR) / (rows - 1) : 0;

  const radii: number[] = [];
  let radiusSum = 0;
  for (let i = 0; i < rows; i++) {
    const r = innerR + rowStep * i;
    radii.push(r);
    radiusSum += r;
  }

  // Seats per row, proportional to radius. Floor first, then hand the remainder to the outermost
  // rows, which have the most arc length to absorb it.
  const counts: number[] = [];
  let assigned = 0;
  for (let i = 0; i < rows; i++) {
    const c = Math.floor((seatCount * radii[i]) / radiusSum);
    counts.push(c);
    assigned += c;
  }
  let remainder = seatCount - assigned;
  let cursor = rows - 1;
  while (remainder > 0) {
    counts[cursor] += 1;
    remainder -= 1;
    cursor = cursor === 0 ? rows - 1 : cursor - 1;
  }

  // Dot size is bounded by whichever is tighter: the gap between rows, or the tightest spacing
  // along a row. Otherwise a crowded outer row overlaps itself.
  let tightest = rowStep > 0 ? rowStep : outerR - innerR;
  if (tightest <= 0) tightest = outerR;
  for (let i = 0; i < rows; i++) {
    if (counts[i] > 1) {
      const along = (Math.PI * radii[i]) / (counts[i] - 1);
      if (along < tightest) tightest = along;
    }
  }
  const dot = clamp(tightest * 0.72, MIN_DOT, MAX_DOT);

  const cx = width / 2;
  const cy = PAD + outerR;

  const slots: SeatSlot[] = [];
  for (let i = 0; i < rows; i++) {
    const count = counts[i];
    const r = radii[i];
    for (let j = 0; j < count; j++) {
      // A single-seat row sits at the top of the arc; otherwise spread evenly from PI to 0.
      const t = count === 1 ? 0.5 : j / (count - 1);
      const angle = Math.PI * (1 - t);
      slots.push({
        angle,
        r,
        x: cx + r * Math.cos(angle),
        y: cy - r * Math.sin(angle),
      });
    }
  }

  // Left to right. The radius tie-break keeps the order stable for slots that share an angle,
  // so the chart does not reshuffle between identical updates.
  slots.sort(function (a, b) {
    if (a.angle !== b.angle) return b.angle - a.angle;
    return a.r - b.r;
  });

  return { slots, width, height: cy + dot / 2 + 2, dot };
}
