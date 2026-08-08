import { Tooltip } from "cs2/ui";
import {
  ISSUE_KEY,
  ISSUE_LABEL,
  ISSUE_NOTE,
  ISSUE_ORDER,
  ISSUE_POLE_HIGH,
  ISSUE_POLE_LOW,
  hexToRgba,
  positionText,
  widthPct,
} from "./format";
import styles from "./PlatformBars.module.scss";

/**
 * The six issue positions as centre-zero bars.
 *
 * A position is in [-1, +1] and the sign carries the meaning, so the track is anchored at its
 * centre rather than at its left edge: a left-anchored bar renders "spend less" as a short "spend
 * more", which is not a cosmetic problem but a wrong reading. The construction is flex only - two
 * halves that grow from the middle outwards - because Gameface has no CSS grid and no absolute
 * positioning is wanted inside a row that has to stay one line high.
 *
 * `values` is one object prop rather than six numbers on purpose: a second series (the manifesto
 * this party ran on) is then one more prop instead of a rewrite.
 */

const FILL_ALPHA = 0.85;

const PlatformBar = (props: {
  issue: Agora.IssueName;
  value: number;
  colorHex: string;
}): JSX.Element => {
  const value = typeof props.value === "number" && isFinite(props.value) ? props.value : 0;
  const fill = hexToRgba(props.colorHex, FILL_ALPHA);

  return (
    <Tooltip
      direction="up"
      tooltip={
        <div className={styles.tip}>
          <div className={styles.tipTitle}>{ISSUE_LABEL[props.issue]}</div>
          <div className={styles.tipBody}>{ISSUE_NOTE[props.issue]}</div>
          <div className={styles.tipPoles}>
            <span className={styles.tipPole}>Left: {ISSUE_POLE_LOW[props.issue]}</span>
            <span className={styles.tipPole}>Right: {ISSUE_POLE_HIGH[props.issue]}</span>
          </div>
        </div>
      }
    >
      <div className={styles.row}>
        <span className={styles.label}>{ISSUE_LABEL[props.issue]}</span>
        <div className={styles.track}>
          <div className={styles.halfLow}>
            <div
              className={styles.fill}
              style={{ width: widthPct(Math.max(0, -value)), backgroundColor: fill }}
            />
          </div>
          <div className={styles.centreRule} />
          <div className={styles.halfHigh}>
            <div
              className={styles.fill}
              style={{ width: widthPct(Math.max(0, value)), backgroundColor: fill }}
            />
          </div>
        </div>
        <span className={styles.readout}>{positionText(value)}</span>
      </div>
    </Tooltip>
  );
};

export const PlatformBars = (props: {
  values: Agora.IssuePositionView;
  /** The party's own colour, so a stance reads as this party's rather than as a generic bar. */
  colorHex: string;
}): JSX.Element => {
  const values = props.values;
  return (
    <div className={styles.bars}>
      <div className={styles.poleRow}>
        <span className={styles.poleSpacer} />
        <span className={styles.poleLow}>less</span>
        <span className={styles.poleCentreSpacer} />
        <span className={styles.poleHigh}>more</span>
        <span className={styles.poleReadoutSpacer} />
      </div>
      {ISSUE_ORDER.map((issue) => (
        <PlatformBar
          key={issue}
          issue={issue}
          value={values[ISSUE_KEY[issue]]}
          colorHex={props.colorHex}
        />
      ))}
    </div>
  );
};
