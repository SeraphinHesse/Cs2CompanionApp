import { Tooltip } from "cs2/ui";
import {
  ISSUE_KEY,
  ISSUE_LABEL,
  ISSUE_NOTE,
  ISSUE_ORDER,
  ISSUE_POLE_HIGH,
  ISSUE_POLE_LOW,
  clamp01,
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
 * `values` is one object prop rather than six numbers on purpose: the second series (the manifesto
 * this party ran on) arrived as one more prop rather than as a rewrite. It is drawn as a tick in the
 * same centre-zero track, never as a second fill - two fills on one track read as a stacked total.
 */

const FILL_ALPHA = 0.85;

/**
 * One half of a track, laid out from the centre rule outwards.
 *
 * The tick and the fill cannot be drawn on top of one another: two flex siblings never overlap, and
 * neither absolute positioning nor a negative margin is on the table here. So the tick is a segment
 * of the flow, and the fill is SPLIT around it when the manifesto sits inside today's stance. Either
 * way the tick's centre-facing edge lands exactly where the fill for the marker's own value would
 * end, which is the one thing the mark has to be right about.
 *
 * `segments` is built centre-outwards. The low half runs right to left, so its list is reversed
 * before it is rendered - the half is `justify-content: flex-end`, and the child nearest the centre
 * rule is its last.
 */
const TrackHalf = (props: {
  side: "low" | "high";
  /** [0,1] of this half - the current stance's magnitude, zero when it points the other way. */
  fill: number;
  /** [0,1] of this half, or null when the marker is absent or belongs to the other half. */
  marker: number | null;
  color: string;
}): JSX.Element => {
  const fill = clamp01(props.fill);
  const bar = (key: string, width: number): JSX.Element => (
    <div
      key={key}
      className={styles.fill}
      style={{ width: widthPct(width), backgroundColor: props.color }}
    />
  );

  const segments: JSX.Element[] = [];
  if (props.marker === null) {
    segments.push(bar("fill", fill));
  } else {
    const marker = clamp01(props.marker);
    if (marker >= fill) {
      // The party has retreated towards the centre since it ran: fill, then the ground it gave up,
      // then the tick standing out beyond it.
      segments.push(bar("fill", fill));
      segments.push(
        <div key="gap" className={styles.gap} style={{ width: widthPct(marker - fill) }} />
      );
      segments.push(<div key="tick" className={styles.marker} />);
    } else {
      // It has hardened past the manifesto: the fill is broken at the manifesto's offset and the
      // tick drawn through the break.
      segments.push(bar("inner", marker));
      segments.push(<div key="tick" className={styles.marker} />);
      segments.push(bar("outer", fill - marker));
    }
  }
  if (props.side === "low") {
    segments.reverse();
  }

  return (
    <div className={props.side === "low" ? styles.halfLow : styles.halfHigh}>{segments}</div>
  );
};

const PlatformBar = (props: {
  issue: Agora.IssueName;
  value: number;
  marker?: number;
  markerLabel?: string;
  colorHex: string;
}): JSX.Element => {
  const value = typeof props.value === "number" && isFinite(props.value) ? props.value : 0;
  const fill = hexToRgba(props.colorHex, FILL_ALPHA);

  // A marker of exactly zero is put in the high half, where it sits against the centre rule; the
  // side of a dead-centre mark is arbitrary, but it has to be decided the same way every render.
  const marker =
    typeof props.marker === "number" && isFinite(props.marker) ? props.marker : null;
  const markerLow = marker !== null && marker < 0 ? -marker : null;
  const markerHigh = marker !== null && marker >= 0 ? marker : null;

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
          {marker !== null ? (
            <div className={styles.tipMarker}>
              {(props.markerLabel || "Ran on") + ": " + positionText(marker)}
            </div>
          ) : null}
        </div>
      }
    >
      <div className={styles.row}>
        <span className={styles.label}>{ISSUE_LABEL[props.issue]}</span>
        <div className={styles.track}>
          <TrackHalf
            side="low"
            fill={Math.max(0, -value)}
            marker={markerLow}
            color={fill}
          />
          <div className={styles.centreRule} />
          <TrackHalf
            side="high"
            fill={Math.max(0, value)}
            marker={markerHigh}
            color={fill}
          />
        </div>
        <span className={styles.readout}>{positionText(value)}</span>
      </div>
    </Tooltip>
  );
};

export const PlatformBars = (props: {
  values: Agora.IssuePositionView;
  /** Second series drawn as a tick, not a fill: the position this party RAN on. */
  marker?: Agora.IssuePositionView;
  markerLabel?: string;
  /** The party's own colour, so a stance reads as this party's rather than as a generic bar. */
  colorHex: string;
}): JSX.Element => {
  const values = props.values;
  const marker = props.marker;
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
          marker={marker ? marker[ISSUE_KEY[issue]] : undefined}
          markerLabel={props.markerLabel}
          colorHex={props.colorHex}
        />
      ))}
      {marker ? (
        <div className={styles.legend}>
          <span className={styles.legendTick} />
          <span className={styles.legendText}>
            {props.markerLabel || "Ran on"} - where this party stood at the last election
          </span>
        </div>
      ) : null}
    </div>
  );
};
