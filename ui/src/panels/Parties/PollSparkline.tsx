import { Tooltip } from "cs2/ui";
import { clamp01, hexToRgba, pct, points } from "./format";
import styles from "./PollSparkline.module.scss";

/**
 * One party's published poll shares over time, as a column sparkline.
 *
 * Flexbox, not SVG and not a grid: Gameface guarantees neither, and a grid fails silently. The chart
 * is a row of equal `flex: 1 1 0` columns, one per point, each holding a bar whose height is a
 * percentage of the row and which sits on the baseline with `align-self: flex-end`.
 *
 * The series arrives OLDEST FIRST and is drawn in that order (contract section 4.2). It is the one
 * list in the contract that is not newest-first, because a trend reads left to right in time - so
 * this component must never reverse it.
 *
 * `scaleMax` is auto-scaled and therefore PRINTED on the axis. An unlabelled auto-scaled sparkline
 * makes a party polling at 2% look like one polling at 40%, which is not a cosmetic problem: the
 * shape is the whole message and without the ceiling beside it the shape means nothing.
 *
 * Nothing here derives politics. Every share is published; the only arithmetic is the axis ceiling
 * and a bar height, which is re-expression of the same kind as `pct()`.
 */

/** The axis ceiling is rounded up to a whole one of these, so it reads as a round number. */
const SCALE_STEP = 0.05;

/** One point is a dot, not a trend. Two is the fewest that can show a direction. */
const MIN_POINTS = 2;

const FILL_ALPHA = 0.85;

/** A [0,1] fraction of the plot as a CSS height. */
function heightPct(value: number): string {
  return (clamp01(value) * 100).toFixed(3) + "%";
}

/**
 * The highest share in the series, rounded up to the next whole SCALE_STEP and never above 100%.
 * A series that is all zeroes still gets one step of headroom rather than a zero-height axis.
 */
function scaleMaxFor(series: Agora.PollTrendPoint[]): number {
  let max = 0;
  for (let i = 0; i < series.length; i++) {
    const share = series[i] ? series[i].share : 0;
    if (typeof share === "number" && isFinite(share) && share > max) {
      max = share;
    }
  }
  const steps = Math.ceil(clamp01(max) / SCALE_STEP);
  const scale = (steps < 1 ? 1 : steps) * SCALE_STEP;
  return scale > 1 ? 1 : scale;
}

/** The one-line reading of a point, for its tooltip. `weeksToElection` is -1 when none was due. */
function pointNote(point: Agora.PollTrendPoint): string {
  const weeks = point.weeksToElection;
  if (typeof weeks !== "number" || weeks < 0) {
    return "No election was scheduled when this poll was published.";
  }
  if (weeks === 0) {
    return "Published in the week of the ballot.";
  }
  if (weeks === 1) {
    return "One week before the ballot.";
  }
  return weeks + " weeks before the ballot.";
}

export const PollSparkline = (props: {
  /** Oldest first, as published. Never reversed here. */
  points: Agora.PollTrendPoint[];
  /** The party's own colour, so the trend reads as this party's rather than as a generic chart. */
  colorHex: string;
}): JSX.Element => {
  const series = props.points || [];

  if (series.length < MIN_POINTS) {
    return <div className={styles.empty}>Not enough published polls yet.</div>;
  }

  const scaleMax = scaleMaxFor(series);
  const fill = hexToRgba(props.colorHex, FILL_ALPHA);
  const first = series[0];
  const last = series[series.length - 1];

  return (
    <div className={styles.chart}>
      <div className={styles.plotRow}>
        <div className={styles.axis}>
          <span className={styles.axisTop}>{pct(scaleMax)}</span>
          <span className={styles.axisBottom}>0%</span>
        </div>
        <div className={styles.plot}>
          {series.map((point, index) => {
            const share =
              point && typeof point.share === "number" && isFinite(point.share) ? point.share : 0;
            const electionWeek = point && point.weeksToElection === 0;
            return (
              <Tooltip
                // The date is the natural key, but two polls can share one publication date, so the
                // index goes on the end. The series is rebuilt whole on every push, never spliced.
                key={(point && point.date ? point.date : "point") + "-" + index}
                direction="up"
                tooltip={
                  <div className={styles.tip}>
                    <div className={styles.tipTitle}>{point && point.date ? point.date : ""}</div>
                    <div className={styles.tipBody}>
                      {pct(share, 1) + " of the published vote"}
                    </div>
                    <div className={styles.tipBody}>
                      {"Margin of error " + points(point ? point.marginOfError : 0)}
                    </div>
                    <div className={styles.tipNote}>{pointNote(point)}</div>
                  </div>
                }
              >
                <div className={styles.column}>
                  <div
                    className={electionWeek ? styles.barElection : styles.bar}
                    style={
                      electionWeek
                        ? { height: heightPct(share / scaleMax) }
                        : { height: heightPct(share / scaleMax), backgroundColor: fill }
                    }
                  />
                </div>
              </Tooltip>
            );
          })}
        </div>
      </div>
      <div className={styles.footer}>
        <span className={styles.footerEnd}>{first && first.date ? first.date : ""}</span>
        <span className={styles.footerNote}>
          {series.length === 1
            ? "1 published poll"
            : series.length + " published polls, oldest first"}
        </span>
        <span className={styles.footerEndRight}>{last && last.date ? last.date : ""}</span>
      </div>
    </div>
  );
};
