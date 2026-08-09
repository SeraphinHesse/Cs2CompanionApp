import { useMemo } from "react";
import { MANDATE_STATUS_LABEL, MANDATE_STATUS_ORDER, int, pct, widthPct } from "./format";
import styles from "./MandateScorecard.module.scss";

/**
 * One party's mandate RECORD - not its mandates.
 *
 * The News tab's tracker already shows every mandate in full: metric, direction, progress bar,
 * baseline and target, deadline. Repeating that here filtered to one party would be a second, worse
 * copy of it, so this component deliberately shows no mandate row at all. It is one line per STATUS
 * plus a delivery rate, and it answers the one question the tracker cannot: does this party deliver.
 * The line at the foot points at the tracker so the split reads as a division of labour rather than
 * as missing information.
 *
 * The counts come off the published list; nothing here is derived politics (contract rule 5). The
 * delivery rate is a ratio of two of those counts, re-expression of the same kind as `pct()`.
 *
 * `isMeasurementStalled` is a rendering obligation: such a mandate is HELD, not failing - its metric
 * is currently unreadable and the clock cannot defy it. It is counted under its own status like any
 * other and called out separately, never folded into a failure.
 */

interface Tally {
  /** Keyed by status name, every status present. */
  byStatus: Record<Agora.MandateStatusName, number>;
  total: number;
  /** Judged: fulfilled + partly met + defied. Active and Pending are not yet judged. */
  resolved: number;
  fulfilled: number;
  held: number;
}

function tally(mandates: Agora.MandateRow[], partyId: string): Tally {
  const byStatus: Record<Agora.MandateStatusName, number> = {
    Pending: 0,
    Active: 0,
    Fulfilled: 0,
    PartiallyFulfilled: 0,
    Defied: 0,
    Abandoned: 0,
  };
  let total = 0;
  let held = 0;

  for (let i = 0; i < mandates.length; i++) {
    const row = mandates[i];
    if (!row || row.partyId !== partyId) {
      continue;
    }
    total++;
    // A status the payload carries but this build has no word for is counted nowhere rather than
    // shown as itself: an enum member name must never reach the player.
    if (MANDATE_STATUS_LABEL[row.status]) {
      byStatus[row.status]++;
    }
    if (row.isMeasurementStalled) {
      held++;
    }
  }

  // A promise that is not yet due is not a promise broken, so Active and Pending are outside the
  // denominator. Abandoned is outside it too: an abandoned mandate was withdrawn rather than judged
  // against its metric, and counting it as a failure would score the party on a verdict nobody gave.
  const resolved = byStatus.Fulfilled + byStatus.PartiallyFulfilled + byStatus.Defied;

  return {
    byStatus: byStatus,
    total: total,
    resolved: resolved,
    fulfilled: byStatus.Fulfilled,
    held: held,
  };
}

/**
 * The held sentence. Written out rather than left to a chip because a held mandate looks like a
 * stalled one on any bar, and the reason it is stalled is the point.
 */
function heldSentence(held: number): string {
  return held === 1
    ? "1 held: the metric is currently unreadable."
    : int(held) + " held: the metric is currently unreadable.";
}

export const MandateScorecard = (props: {
  partyId: string;
  /** The whole published mandate list, subscribed once by the panel - never once per rail row. */
  mandates: Agora.MandateRow[];
}): JSX.Element | null => {
  const counts = useMemo(
    () => tally(props.mandates || [], props.partyId),
    [props.mandates, props.partyId]
  );

  // A party that has never held a mandate gets no section at all - not a heading over an empty box.
  // The heading therefore lives inside this component rather than in the pane, which cannot know
  // whether there is anything to head without doing this filter a second time.
  if (counts.total === 0) {
    return null;
  }

  // Fulfilled ALONE is the numerator. A partly met mandate was judged, so it stays in the
  // denominator, but half a delivery is a weighting no engine published and the panel does not get
  // to invent one - it would be a number on screen that nothing in the model backs. The label says
  // "in full" so the figure describes exactly what was counted.
  const rate = counts.resolved > 0 ? counts.fulfilled / counts.resolved : 0;

  return (
    <div className={styles.card}>
      <div className={styles.sectionTitle}>
        <span className={styles.sectionTitleText}>Mandate record</span>
        <span className={styles.sectionNote}>
          {counts.total === 1 ? "1 mandate on record" : int(counts.total) + " mandates on record"}
        </span>
      </div>

      <div className={styles.chipRow}>
        {MANDATE_STATUS_ORDER.map((status) => {
          const count = counts.byStatus[status];
          return (
            <span key={status} className={count > 0 ? styles.chip : styles.chipEmpty}>
              <span className={styles.chipCount}>{int(count)}</span>
              <span className={styles.chipLabel}>{MANDATE_STATUS_LABEL[status]}</span>
            </span>
          );
        })}
      </div>

      <div className={styles.rateLabel}>Delivered in full</div>

      {counts.resolved > 0 ? (
        <>
          {/* The percentage sits on the bar's own row, as the tracker's does: a bar with its figure
              two rows away asks the reader to pair them up. */}
          <div className={styles.rateRow}>
            <div className={styles.rateTrack}>
              <div className={styles.rateFill} style={{ width: widthPct(rate) }} />
            </div>
            <span className={styles.rateValue}>{pct(rate)}</span>
          </div>
          <div className={styles.rateNote}>
            {int(counts.fulfilled) + " of " + int(counts.resolved) + " judged mandates met in full. "}
            {"A partly met mandate counts as judged but not as delivered; one still active or " +
              "pending is not judged yet and is left out of both figures."}
          </div>
        </>
      ) : (
        <div className={styles.rateEmpty}>Nothing judged yet</div>
      )}

      {counts.held > 0 ? <div className={styles.held}>{heldSentence(counts.held)}</div> : null}

      <div className={styles.pointer}>Full detail in the News tab&rsquo;s mandate tracker.</div>
    </div>
  );
};
