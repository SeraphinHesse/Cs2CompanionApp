import styles from "./NewsPanel.module.scss";
import { Lookups } from "./lookup";
import {
  clamp01,
  cx,
  formatMonthsRemaining,
  formatNumber,
  formatPercent,
  formatSimDate,
  humanizeEnum,
} from "./format";

/**
 * The mandate tracker: every mandate, its progress against the metric it is judged on, and its
 * deadline.
 *
 * The list arrives sorted by STATUS RANK ascending (Active, Pending, PartiallyFulfilled,
 * Fulfilled, Defied, Abandoned), then deadlineDate ascending, then id — so it already opens on
 * what is live and closest to due. It is rendered in that order and never re-sorted.
 *
 * `text` is FLAVOR. Every number on a MandateRow is engine-owned and is displayed as published:
 * the bar width is `progress`, not a ratio this panel worked out from baseline and target.
 *
 * `isMeasurementStalled` is a rendering obligation. Such a mandate is HELD, not failing: its bar
 * is drawn in a neutral held style, its deadline reads "Clock held", and no overdue emphasis is
 * applied — the clock running out while the metric was unreadable is not the mandate's failure.
 */

interface MandateTrackerProps {
  mandates: Agora.MandateRow[];
  lookups: Lookups;
}

const STATUS_LABEL: { [status: string]: string } = {
  Pending: "Pending",
  Active: "Active",
  Fulfilled: "Fulfilled",
  PartiallyFulfilled: "Partly met",
  Defied: "Defied",
  Abandoned: "Abandoned",
};

const STATUS_CLASS: { [status: string]: string } = {
  Pending: styles.statusPending,
  Active: styles.statusActive,
  Fulfilled: styles.statusFulfilled,
  PartiallyFulfilled: styles.statusPartial,
  Defied: styles.statusDefied,
  Abandoned: styles.statusAbandoned,
};

export const MandateTracker = ({ mandates, lookups }: MandateTrackerProps) => {
  if (mandates.length === 0) {
    return (
      <div className={styles.empty}>
        <div className={styles.emptyTitle}>No mandates</div>
        <div className={styles.emptyText}>
          A government issues mandates when it forms. Each one names a metric, a target and a
          deadline it will be judged against.
        </div>
      </div>
    );
  }

  return (
    <div className={styles.list}>
      {mandates.map((mandate) => (
        <MandateItem key={mandate.id} mandate={mandate} lookups={lookups} />
      ))}
    </div>
  );
};

interface MandateItemProps {
  mandate: Agora.MandateRow;
  lookups: Lookups;
}

const MandateItem = ({ mandate, lookups }: MandateItemProps) => {
  const stalled = mandate.isMeasurementStalled;
  const resolved = mandate.resolvedDate !== "";

  // Overdue emphasis is suppressed for a held mandate and for one that is already resolved —
  // neither is "running out of time".
  const overdue = !stalled && !resolved && mandate.monthsRemaining < 0;

  const progressPercent = clamp01(mandate.progress) * 100;
  const partyLabel = lookups.partyLabel(mandate.partyId);

  return (
    <div className={cx(styles.mandateItem, stalled && styles.mandateItemHeld)}>
      <div className={styles.rail} style={{ backgroundColor: lookups.partyColor(mandate.partyId) }} />

      <div className={styles.mandateBody}>
        <div className={styles.metaRow}>
          <span className={cx(styles.statusChip, STATUS_CLASS[mandate.status])}>
            {STATUS_LABEL[mandate.status] || mandate.status}
          </span>
          <span className={styles.metaKind}>{humanizeEnum(mandate.issue)}</span>
          {partyLabel ? <span className={styles.chip}>{partyLabel}</span> : null}
          <span className={styles.chip}>{lookups.districtLabel(mandate.districtId)}</span>
          {stalled ? <span className={styles.heldChip}>Measurement paused</span> : null}
        </div>

        {/* Flavor text of unpredictable length: clamped to two lines, long tokens broken. */}
        {mandate.text ? <div className={styles.mandateText}>{mandate.text}</div> : null}

        <div className={styles.metricRow}>
          <span className={styles.metricName}>{humanizeEnum(mandate.metric)}</span>
          <span className={styles.chipDim}>{humanizeEnum(mandate.direction)}</span>
        </div>

        {/*
          The percentage sits on the bar's own row rather than a line above it. A bar with its
          number two rows away asks the reader to pair them up; this way the figure and the fill it
          describes are read as one thing. `progress` is published — the panel does not work it out
          from baseline and target.
        */}
        <div className={styles.progressRow}>
          <div className={styles.progressTrack}>
            <div
              className={cx(
                styles.progressFill,
                stalled && styles.progressFillHeld,
                !stalled && mandate.progress >= 1 && styles.progressFillDone,
              )}
              style={{ width: String(progressPercent) + "%" }}
            />
          </div>
          <span className={cx(styles.progressValue, stalled && styles.progressValueHeld)}>
            {formatPercent(mandate.progress)}
          </span>
        </div>

        <div className={styles.numberRow}>
          <NumberBlock label="From" value={mandate.baselineValue} />
          <NumberBlock label="Now" value={mandate.currentValue} dimmed={stalled} />
          <NumberBlock label="Target" value={mandate.targetValue} />
        </div>

        <div className={styles.mandateFooter}>
          <span className={styles.footerLabel}>
            {resolved
              ? "Resolved " + formatSimDate(mandate.resolvedDate)
              : "Due " + formatSimDate(mandate.deadlineDate)}
          </span>
          <span className={styles.spacer} />
          {resolved ? null : (
            <span
              className={cx(
                styles.deadline,
                overdue && styles.deadlineOverdue,
                stalled && styles.deadlineHeld,
              )}
            >
              {formatMonthsRemaining(mandate.monthsRemaining, stalled)}
            </span>
          )}
        </div>

        <div className={styles.salienceRow}>
          <span className={styles.footerLabel}>Voter interest</span>
          <span className={styles.salienceTrack}>
            <span
              className={styles.salienceFill}
              style={{ width: String(clamp01(mandate.salience) * 100) + "%" }}
            />
          </span>
          <span className={styles.footerValue}>{formatPercent(mandate.salience)}</span>
        </div>
      </div>
    </div>
  );
};

interface NumberBlockProps {
  label: string;
  value: number;
  dimmed?: boolean;
}

/**
 * A metric value in its own units. No unit suffix is invented: the contract does not define one
 * per metric, and guessing would put a fabricated fact on screen.
 */
const NumberBlock = ({ label, value, dimmed }: NumberBlockProps) => (
  <div className={styles.numberBlock}>
    <div className={styles.numberLabel}>{label}</div>
    <div className={cx(styles.numberValue, dimmed && styles.numberValueDim)}>
      {formatNumber(value)}
    </div>
  </div>
);
