import { ReactNode } from "react";
import { Tooltip } from "cs2/ui";
import { FallbackSet, clamp01, humanizeField, widthPct } from "./format";
import styles from "./Bits.module.scss";

/**
 * Small shared pieces for the Districts panel.
 *
 * The city-fallback treatment lives here because it is the panel's one non-negotiable rendering
 * rule (contract section 4.4) and every surface that shows a district number has to apply it the
 * same way: dimmed, italic, badged, and explained in a tooltip.
 */

const FALLBACK_TIP_LEAD =
  "City-wide value. AGORA could not read this figure for this district, so the city number is " +
  "standing in. It is not a local measurement.";

export const FallbackTip = (props: { field?: string }): JSX.Element => (
  <div className={styles.tooltip}>
    {props.field ? (
      <div className={styles.tooltipTitle}>{humanizeField(props.field)}</div>
    ) : null}
    <div className={styles.tooltipBody}>{FALLBACK_TIP_LEAD}</div>
  </div>
);

/**
 * Wraps a rendered value. When the named field is in `cityFallbackFields`, the value is dimmed,
 * badged CITY and given a tooltip. When it is not, this renders the value untouched.
 */
export const CityValue = (props: {
  field: string;
  fallbacks: FallbackSet;
  children: ReactNode;
}): JSX.Element => {
  if (!props.fallbacks.has(props.field)) {
    return <>{props.children}</>;
  }
  return (
    <Tooltip tooltip={<FallbackTip field={props.field} />} direction="up">
      <span className={styles.cityValue}>
        <span className={styles.cityValueText}>{props.children}</span>
        <span className={styles.cityBadge}>CITY</span>
      </span>
    </Tooltip>
  );
};

/** The banner above a district's figures when any of them fell back to a city number. */
export const CityFallbackBanner = (props: { fallbacks: FallbackSet }): JSX.Element | null => {
  if (!props.fallbacks.any) {
    return null;
  }
  const fields = props.fallbacks.fields;
  return (
    <div className={styles.banner}>
      <div className={styles.bannerHead}>
        <span className={styles.bannerBadge}>CITY DATA</span>
        <span className={styles.bannerText}>
          Some figures below are city-wide values standing in for district data AGORA could not
          read. They are marked and must not be read as local facts.
        </span>
      </div>
      {fields.length > 0 ? (
        <div className={styles.chipRow}>
          {fields.map((field) => (
            <span key={field} className={styles.chip}>
              {humanizeField(field)}
            </span>
          ))}
        </div>
      ) : (
        <div className={styles.bannerNote}>
          The publisher did not name which figures fell back, so treat every number in this
          district as provisional.
        </div>
      )}
    </div>
  );
};

/** A labelled bar. `fill` is [0,1]; `value` is the already-formatted text. */
export const Meter = (props: {
  label: string;
  value: string;
  fill: number;
  tint?: string;
  field?: string;
  fallbacks?: FallbackSet;
}): JSX.Element => {
  const field = props.field || "";
  const fallbacks = props.fallbacks;
  const isFallback = !!(field && fallbacks && fallbacks.has(field));
  const tint = props.tint || "rgba(140, 190, 235, 0.85)";
  const bar = (
    <div className={isFallback ? styles.meterFallback : styles.meter}>
      <div className={styles.meterTop}>
        <span className={styles.meterLabel}>{props.label}</span>
        <span className={styles.meterValue}>
          {props.value}
          {isFallback ? <span className={styles.cityBadge}>CITY</span> : null}
        </span>
      </div>
      <div className={styles.meterTrack}>
        <div
          className={styles.meterFill}
          style={{ width: widthPct(clamp01(props.fill)), backgroundColor: tint }}
        />
      </div>
    </div>
  );
  if (!isFallback) {
    return bar;
  }
  return (
    <Tooltip tooltip={<FallbackTip field={field} />} direction="up">
      {bar}
    </Tooltip>
  );
};

export interface BarSegment {
  key: string;
  label: string;
  share: number;
  color: string;
}

/** A single stacked bar plus a legend. Segment order is the caller's; this never re-sorts. */
export const StackedBar = (props: {
  segments: BarSegment[];
  legendValue: (segment: BarSegment) => string;
  compact?: boolean;
}): JSX.Element => {
  const segments = props.segments;
  return (
    <div className={styles.stack}>
      <div className={styles.stackTrack}>
        {segments.map((segment) => (
          <div
            key={segment.key}
            className={styles.stackSegment}
            style={{ width: widthPct(segment.share), backgroundColor: segment.color }}
          />
        ))}
      </div>
      <div className={props.compact ? styles.legendCompact : styles.legend}>
        {segments.map((segment) => (
          <div key={segment.key} className={styles.legendItem}>
            <span className={styles.legendSwatch} style={{ backgroundColor: segment.color }} />
            <span className={styles.legendLabel}>{segment.label}</span>
            <span className={styles.legendValue}>{props.legendValue(segment)}</span>
          </div>
        ))}
      </div>
    </div>
  );
};

export const SectionTitle = (props: { title: string; note?: ReactNode }): JSX.Element => (
  <div className={styles.sectionTitle}>
    <span className={styles.sectionTitleText}>{props.title}</span>
    {props.note ? <span className={styles.sectionNote}>{props.note}</span> : null}
  </div>
);

export const StatCell = (props: {
  label: string;
  value: string;
  field?: string;
  fallbacks?: FallbackSet;
}): JSX.Element => {
  const fallbacks = props.fallbacks;
  const field = props.field || "";
  const body = <span className={styles.statValue}>{props.value}</span>;
  return (
    <div className={styles.stat}>
      <span className={styles.statLabel}>{props.label}</span>
      {field && fallbacks ? (
        <CityValue field={field} fallbacks={fallbacks}>
          {body}
        </CityValue>
      ) : (
        body
      )}
    </div>
  );
};
