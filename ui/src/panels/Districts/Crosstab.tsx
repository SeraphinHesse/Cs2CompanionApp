import { useMemo, useState } from "react";
import { Tooltip } from "cs2/ui";
import {
  EDUCATION_FULL,
  EDUCATION_LABEL,
  EDUCATION_TIERS,
  NO_VALUE,
  PartyIndex,
  WEALTH_FULL,
  WEALTH_LABEL,
  WEALTH_TIERS,
  cellKey,
  clamp01,
  happinessText,
  hexToRgba,
  int,
  partyColor,
  partyName,
  partyShort,
  pct,
  widthPct,
} from "./format";
import styles from "./Crosstab.module.scss";

/**
 * The wealth x education crosstab: a fixed 3 x 5 matrix, flex only.
 *
 * The payload is contractually always 15 cells, wealth ascending then education ascending. The
 * matrix is still rendered from the axis constants and each cell looked up by key, so the panel
 * neither re-sorts the payload (contract rule 7) nor breaks if a cell is ever absent.
 *
 * Legibility is the design constraint. Gameface hands this panel a narrow column, so a cell shows
 * exactly one number plus a party strength bar; everything else about a cell goes to the readout
 * strip below the grid when the cell is clicked. Fifteen cells each carrying six figures is
 * unreadable at this size, and unreadable is the same as absent.
 */

type CrosstabMetric = "party" | "happiness" | "discontent" | "share";

interface MetricDef {
  id: CrosstabMetric;
  label: string;
  /** Base tint for the heat ramp. Party mode uses the party's own colour instead. */
  tint: string;
  note: string;
}

const METRICS: MetricDef[] = [
  {
    id: "party",
    label: "Lead",
    tint: "",
    note: "Cell tint is the leading party; the number is its share of that cell's vote.",
  },
  {
    id: "happiness",
    label: "Happy",
    tint: "#7aae52",
    note: "Happiness on the game's own 0-100 scale, the one quantity here that is not a share.",
  },
  {
    id: "discontent",
    label: "Discontent",
    tint: "#c25b4a",
    note: "Discontent [0-1]. High discontent in a populous cell is where a new party starts.",
  },
  {
    id: "share",
    label: "Size",
    tint: "#5b8dc2",
    note: "Share of the population in each cell, shaded against the largest cell shown.",
  },
];

function metricNote(metric: CrosstabMetric): string {
  for (let i = 0; i < METRICS.length; i++) {
    if (METRICS[i].id === metric) {
      return METRICS[i].note;
    }
  }
  return "";
}

function metricTint(metric: CrosstabMetric): string {
  for (let i = 0; i < METRICS.length; i++) {
    if (METRICS[i].id === metric) {
      return METRICS[i].tint;
    }
  }
  return "#5b8dc2";
}

/** The already-published number a cell shows in this mode. Nothing is recomputed. */
function metricText(metric: CrosstabMetric, cell: Agora.CrosstabCell): string {
  if (metric === "party") {
    return cell.leadingPartyId ? pct(cell.leadingShare) : NO_VALUE;
  }
  if (metric === "happiness") {
    return happinessText(cell.happiness);
  }
  if (metric === "discontent") {
    return pct(cell.discontent);
  }
  return pct(cell.populationShare, 1);
}

/** [0,1] intensity for the heat ramp only - never displayed as a number. */
function metricIntensity(
  metric: CrosstabMetric,
  cell: Agora.CrosstabCell,
  maxShare: number
): number {
  if (metric === "party") {
    return clamp01(cell.leadingShare);
  }
  if (metric === "happiness") {
    return clamp01(cell.happiness / 100);
  }
  if (metric === "discontent") {
    return clamp01(cell.discontent);
  }
  return maxShare > 0 ? clamp01(cell.populationShare / maxShare) : 0;
}

export const Crosstab = (props: {
  cells: Agora.CrosstabCell[];
  parties: PartyIndex;
  /** "City-wide" or the district's name. Flavor-safe: display only. */
  scopeLabel: string;
  /** True when this composition is a city-wide stand-in rather than a district measurement. */
  isCityStandIn: boolean;
}): JSX.Element => {
  const [metric, setMetric] = useState<CrosstabMetric>("party");
  const [selectedKey, setSelectedKey] = useState<string>("");

  const cells = props.cells;

  const byKey = useMemo(() => {
    const map: Record<string, Agora.CrosstabCell> = {};
    if (cells) {
      for (let i = 0; i < cells.length; i++) {
        const cell = cells[i];
        if (cell) {
          map[cellKey(cell.wealth, cell.education)] = cell;
        }
      }
    }
    return map;
  }, [cells]);

  const maxShare = useMemo(() => {
    let max = 0;
    if (cells) {
      for (let i = 0; i < cells.length; i++) {
        const cell = cells[i];
        if (cell && cell.populationShare > max) {
          max = cell.populationShare;
        }
      }
    }
    return max;
  }, [cells]);

  const selected = selectedKey ? byKey[selectedKey] : undefined;

  if (!cells || cells.length === 0) {
    return (
      <div className={styles.crosstab}>
        <div className={styles.empty}>
          No crosstab published yet for {props.scopeLabel}. The engine publishes it on the monthly
          political tick.
        </div>
      </div>
    );
  }

  return (
    <div className={props.isCityStandIn ? styles.crosstabStandIn : styles.crosstab}>
      {props.isCityStandIn ? (
        <div className={styles.standInNotice}>
          <span className={styles.standInBadge}>CITY DATA</span>
          <span className={styles.standInText}>
            This district's composition could not be read, so the city-wide crosstab is shown in its
            place. Every cell below describes the city, not this district.
          </span>
        </div>
      ) : null}

      <div className={styles.toolbar}>
        {METRICS.map((definition) => (
          <div
            key={definition.id}
            className={definition.id === metric ? styles.toggleOn : styles.toggle}
            onClick={() => setMetric(definition.id)}
          >
            {definition.label}
          </div>
        ))}
      </div>

      <div className={styles.matrix}>
        <div className={styles.headRow}>
          <div className={styles.corner}>
            <span className={styles.cornerWealth}>Wealth</span>
            <span className={styles.cornerEdu}>Education</span>
          </div>
          {EDUCATION_TIERS.map((education) => (
            <Tooltip
              key={education}
              direction="up"
              tooltip={<div className={styles.tip}>{EDUCATION_FULL[education]}</div>}
            >
              <div className={styles.headCell}>{EDUCATION_LABEL[education]}</div>
            </Tooltip>
          ))}
        </div>

        {WEALTH_TIERS.map((wealth) => (
          <div key={wealth} className={styles.row}>
            <div className={styles.rowLabel}>{WEALTH_LABEL[wealth]}</div>
            {EDUCATION_TIERS.map((education) => {
              const key = cellKey(wealth, education);
              const cell = byKey[key];
              const isSelected = key === selectedKey;

              if (!cell || cell.population <= 0) {
                return (
                  <div
                    key={key}
                    className={isSelected ? styles.cellEmptySelected : styles.cellEmpty}
                    onClick={() => setSelectedKey(isSelected ? "" : key)}
                  >
                    <div className={styles.cellValueMuted}>{NO_VALUE}</div>
                  </div>
                );
              }

              const intensity = metricIntensity(metric, cell, maxShare);
              const alpha = 0.1 + 0.6 * intensity;
              const background =
                metric === "party"
                  ? hexToRgba(partyColor(props.parties, cell.leadingPartyId), alpha)
                  : hexToRgba(metricTint(metric), alpha);

              return (
                <div
                  key={key}
                  className={isSelected ? styles.cellSelected : styles.cell}
                  style={{ backgroundColor: background }}
                  onClick={() => setSelectedKey(isSelected ? "" : key)}
                >
                  {metric === "party" ? (
                    <div className={styles.cellParty}>
                      {partyShort(props.parties, cell.leadingPartyId)}
                    </div>
                  ) : null}
                  <div className={styles.cellValue}>{metricText(metric, cell)}</div>
                  <div className={styles.cellBarTrack}>
                    <div
                      className={styles.cellBar}
                      style={{
                        width: widthPct(cell.leadingShare),
                        backgroundColor: partyColor(props.parties, cell.leadingPartyId),
                      }}
                    />
                  </div>
                </div>
              );
            })}
          </div>
        ))}
      </div>

      <div className={styles.readout}>
        {selected ? (
          <div className={styles.readoutBody}>
            <div className={styles.readoutTitle}>
              {WEALTH_FULL[selected.wealth]} x {EDUCATION_FULL[selected.education]}
            </div>
            <div className={styles.readoutFacts}>
              <span className={styles.fact}>
                <span className={styles.factLabel}>People</span>
                <span className={styles.factValue}>
                  {int(selected.population)} ({pct(selected.populationShare, 1)})
                </span>
              </span>
              <span className={styles.fact}>
                <span className={styles.factLabel}>Eligible</span>
                <span className={styles.factValue}>{int(selected.eligibleVoters)}</span>
              </span>
              <span className={styles.fact}>
                <span className={styles.factLabel}>Leading</span>
                <span className={styles.factValue}>
                  <span
                    className={styles.factSwatch}
                    style={{ backgroundColor: partyColor(props.parties, selected.leadingPartyId) }}
                  />
                  {selected.leadingPartyId
                    ? partyName(props.parties, selected.leadingPartyId) +
                      " " +
                      pct(selected.leadingShare)
                    : "No voters"}
                </span>
              </span>
              <span className={styles.fact}>
                <span className={styles.factLabel}>Happiness</span>
                <span className={styles.factValue}>{happinessText(selected.happiness)} / 100</span>
              </span>
              <span className={styles.fact}>
                <span className={styles.factLabel}>Discontent</span>
                <span className={styles.factValue}>{pct(selected.discontent)}</span>
              </span>
            </div>
          </div>
        ) : (
          <div className={styles.readoutHint}>
            Click a cell for its full figures. {metricNote(metric)}
          </div>
        )}
      </div>

      <div className={styles.footnote}>
        Each cell sums the engine's four age bands, so the grid is 15 cells rather than 60. The bar
        under every number is the leading party's share of that cell.
      </div>
    </div>
  );
};
