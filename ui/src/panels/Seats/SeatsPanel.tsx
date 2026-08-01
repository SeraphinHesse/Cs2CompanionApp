import { useMemo } from "react";
import { bindValue, useValue } from "cs2/api";

import { buildHemicycle } from "./hemicycle";
import styles from "./SeatsPanel.module.scss";

/**
 * Panel 23 — Seats.
 *
 * A hemicycle coloured by party, the governing bloc against the opposition, and the mayor. The
 * player is looking at their city, not at this panel, so the whole thing has to answer three
 * questions from the corner of the eye: who is in charge, do they have a majority, and how shaky
 * are they. Everything finer than that is deliberately small and at the bottom.
 *
 * Binding names come from docs/contracts/ui_bindings.md and are frozen. A typo here produces an
 * empty panel at runtime, not a build error — the names below are copied verbatim from §4 of the
 * contract, and the empty values from §6.
 *
 * The panel never computes politics. Seat shares, the majority flag, stability and cohesion are
 * all published numbers; the only arithmetic here is turning [0,1] into a percentage for display
 * and turning a seat count into dot positions.
 *
 * Gameface has no CSS grid. Every layout in this file is flex, plus absolute positioning for the
 * seat dots.
 */

// -- empty / loading values (contract §6 — copied literally) -------------------------------------

const EMPTY_STATE_SUMMARY: Agora.StateSummary = {
  schemaVersion: 0,
  date: "",
  termNumber: 0,
  system: "Proportional",
  theme: "Eu",
  nextElectionDate: "",
  isCampaignSeason: false,
  weeksToElection: -1,
  mayorPartyId: "",
};

// -- bindings (contract §4.1, §4.2, §4.3) --------------------------------------------------------

const enabled$ = bindValue<boolean>("agora.state", "enabled", false);
const ready$ = bindValue<boolean>("agora.state", "ready", false);
const summary$ = bindValue<Agora.StateSummary>("agora.state", "summary", EMPTY_STATE_SUMMARY);
const roster$ = bindValue<Agora.PartyBrief[]>("agora.parties", "roster", []);
const total$ = bindValue<number>("agora.seats", "total", 0);
const allocation$ = bindValue<Agora.SeatRow[]>("agora.seats", "allocation", []);
const government$ = bindValue<Agora.GovernmentSummary | null>("agora.seats", "government", null);
const mayor$ = bindValue<Agora.MayorSummary | null>("agora.seats", "mayor", null);

// -- constants -----------------------------------------------------------------------------------

/** Chart width in rem. The panel's min-width is sized from this in the stylesheet. */
const CHART_WIDTH = 296;

/** Used when a party id has no roster entry — a party can be dissolved between two publishes. */
const UNKNOWN_COLOR = "#78828f";

const MONTHS = [
  "Jan", "Feb", "Mar", "Apr", "May", "Jun",
  "Jul", "Aug", "Sep", "Oct", "Nov", "Dec",
];

// -- view model ----------------------------------------------------------------------------------

interface ChartRow {
  partyId: string;
  seats: number;
  seatShare: number;
  voteShare: number;
  passedThreshold: boolean;
  color: string;
  /** shortName if the flavor layer has produced one, else name, else the raw id. */
  label: string;
  inGovernment: boolean;
  isLead: boolean;
}

interface Blocs {
  /** Chart order: governing bloc first (lead party leftmost), then opposition. */
  seated: ChartRow[];
  government: ChartRow[];
  opposition: ChartRow[];
  /** Rows that returned no seats. Rendered as a footnote, never in the chart. */
  unseated: ChartRow[];
  /** Total dots to draw. A rendering count, not an engine number. */
  dotCount: number;
}

function clamp01(value: number): number {
  if (!(value > 0)) return 0; // also catches NaN
  return value > 1 ? 1 : value;
}

function pct(value: number): string {
  return Math.round(clamp01(value) * 100) + "%";
}

function points(value: number): string {
  return Math.round(clamp01(value) * 100) + " pts";
}

/** "YYYY-MM-DD" -> "May 2003". An absent date is "" by contract, never null. */
function formatMonth(date: string): string {
  if (!date || date.length < 7) return "—";
  const month = Number(date.substring(5, 7));
  if (!(month >= 1 && month <= 12)) return date;
  return MONTHS[month - 1] + " " + date.substring(0, 4);
}

function systemLabel(system: Agora.ElectoralSystemName): string {
  return system === "FirstPastThePost" ? "First past the post" : "Proportional";
}

/**
 * Government heading. Driven by the published status enum, never by sniffing seat counts —
 * a minority government and a collapsed one both have seats.
 */
function governmentLabel(status: Agora.CoalitionStatusName): string {
  switch (status) {
    case "Governing":
      return "Government";
    case "Minority":
      return "Minority government";
    case "Negotiating":
      return "Negotiating";
    case "Collapsed":
      return "Caretaker (collapsed)";
    case "Expired":
      return "Caretaker (expired)";
    default:
      return "Government";
  }
}

function toChartRow(
  row: Agora.SeatRow,
  party: Agora.PartyBrief | undefined,
  inGovernment: boolean,
  isLead: boolean
): ChartRow {
  const label = party ? party.shortName || party.name || party.id : row.partyId;
  return {
    partyId: row.partyId,
    seats: row.seats,
    seatShare: row.seatShare,
    voteShare: row.voteShare,
    passedThreshold: row.passedThreshold,
    color: party && party.colorHex ? party.colorHex : UNKNOWN_COLOR,
    label: label || "—",
    inGovernment,
    isLead,
  };
}

/**
 * Split the allocation into blocs and put the governing side on the left of the chart, which is
 * the arrangement a parliament chart is read with.
 *
 * `allocation` arrives sorted by seats descending then partyId ascending (contract §4.3) and that
 * order is preserved inside each bloc — the panel must not re-sort by a flavor string. The lead
 * party is lifted to the front of the governing bloc with two filters rather than a sort, so the
 * result does not depend on the engine's sort being stable.
 */
function splitBlocs(
  allocation: Agora.SeatRow[],
  government: Agora.GovernmentSummary | null,
  partyById: { [id: string]: Agora.PartyBrief }
): Blocs {
  const isMember: { [id: string]: boolean } = {};
  if (government) {
    for (let i = 0; i < government.memberPartyIds.length; i++) {
      isMember[government.memberPartyIds[i]] = true;
    }
  }
  const leadId = government ? government.leadPartyId : "";

  const governing: ChartRow[] = [];
  const opposition: ChartRow[] = [];
  const unseated: ChartRow[] = [];
  let dotCount = 0;

  for (let i = 0; i < allocation.length; i++) {
    const source = allocation[i];
    const member = isMember[source.partyId] === true;
    const row = toChartRow(
      source,
      partyById[source.partyId],
      member,
      member && source.partyId === leadId
    );
    if (source.seats <= 0) {
      unseated.push(row);
    } else {
      dotCount += source.seats;
      if (member) {
        governing.push(row);
      } else {
        opposition.push(row);
      }
    }
  }

  const lead = governing.filter(function (r) { return r.isLead; });
  const partners = governing.filter(function (r) { return !r.isLead; });
  const orderedGovernment = lead.concat(partners);

  return {
    seated: orderedGovernment.concat(opposition),
    government: orderedGovernment,
    opposition,
    unseated,
    dotCount,
  };
}

// -- component -----------------------------------------------------------------------------------

export const SeatsPanel = () => {
  const enabled = useValue(enabled$);
  const ready = useValue(ready$);
  const summary = useValue(summary$);
  const roster = useValue(roster$);
  const total = useValue(total$);
  const allocation = useValue(allocation$);
  const government = useValue(government$);
  const mayor = useValue(mayor$);

  // Party metadata lives in exactly one binding so two panels cannot disagree about a colour.
  const partyById = useMemo(function () {
    const map: { [id: string]: Agora.PartyBrief } = {};
    for (let i = 0; i < roster.length; i++) {
      map[roster[i].id] = roster[i];
    }
    return map;
  }, [roster]);

  const blocs = useMemo(function () {
    return splitBlocs(allocation, government, partyById);
  }, [allocation, government, partyById]);

  const layout = useMemo(function () {
    return buildHemicycle(blocs.dotCount, CHART_WIDTH);
  }, [blocs]);

  const dots = useMemo(function () {
    const out: { key: string; left: number; top: number; color: string }[] = [];
    let slot = 0;
    for (let i = 0; i < blocs.seated.length; i++) {
      const row = blocs.seated[i];
      for (let s = 0; s < row.seats && slot < layout.slots.length; s++, slot++) {
        out.push({
          key: row.partyId + ":" + s,
          left: layout.slots[slot].x - layout.dot / 2,
          top: layout.slots[slot].y - layout.dot / 2,
          color: row.color,
        });
      }
    }
    return out;
  }, [blocs, layout]);

  // Every hook is above this line — the master toggle must not change the hook order.
  // "Off" means the player sees no trace of the mod, so render nothing rather than a dead shell.
  if (!enabled) {
    return null;
  }

  if (!ready || blocs.seated.length === 0) {
    return (
      <div className={styles.panel}>
        <div className={styles.header}>
          <span className={styles.title}>Council</span>
        </div>
        <div className={styles.skeleton}>
          <span className={styles.skeletonText}>
            {ready ? "No council seated yet." : "Waiting for the first political tick…"}
          </span>
        </div>
      </div>
    );
  }

  const mayorParty = mayor ? partyById[mayor.partyId] : undefined;
  const mayorColor = mayorParty && mayorParty.colorHex ? mayorParty.colorHex : UNKNOWN_COLOR;
  const mayorPartyLabel = mayorParty
    ? mayorParty.shortName || mayorParty.name || mayorParty.id
    : mayor
    ? mayor.partyId
    : "";

  return (
    <div className={styles.panel}>
      <div className={styles.header}>
        <span className={styles.title}>Council</span>
        <span className={styles.headerMeta}>
          {total > 0 ? total + " seats" : ""}
          {total > 0 ? " · " : ""}
          {systemLabel(summary.system)}
        </span>
      </div>

      <div
        className={styles.chart}
        style={{ width: layout.width + "rem", height: layout.height + "rem" }}
      >
        {dots.map(function (dot) {
          return (
            <div
              key={dot.key}
              className={styles.seat}
              style={{
                left: dot.left + "rem",
                top: dot.top + "rem",
                width: layout.dot + "rem",
                height: layout.dot + "rem",
                borderRadius: layout.dot + "rem",
                backgroundColor: dot.color,
              }}
            />
          );
        })}
      </div>

      {/*
        One bar across every seat, government side first, with a tick on the majority line. This is
        the "can they pass anything" read. Widths come straight from the published seatShare; the
        panel does not add seats up.
      */}
      <div className={styles.bar}>
        {blocs.seated.map(function (row) {
          return (
            <div
              key={row.partyId}
              className={row.inGovernment ? styles.barSegment : styles.barSegmentOpposition}
              style={{ width: clamp01(row.seatShare) * 100 + "%", backgroundColor: row.color }}
            />
          );
        })}
        <div className={styles.barTick} />
      </div>

      <div className={styles.barCaption}>
        <span className={styles.barCaptionLeft}>
          {government
            ? governmentLabel(government.status) +
              (total > 0 ? " · " + government.seats + " of " + total : "")
            : "No government formed"}
        </span>
        {government ? (
          <span className={government.hasMajority ? styles.majorityYes : styles.majorityNo}>
            {government.hasMajority ? "Majority" : "No majority"}
          </span>
        ) : null}
      </div>

      <div className={styles.blocs}>
        <div className={styles.bloc}>
          <div className={styles.blocHead}>
            {/* The status qualifier is carried once, in the caption above — not repeated here. */}
            <span className={styles.blocLabel}>Government</span>
            <span className={styles.blocMeta}>
              {government ? pct(government.seatShare) : "—"}
            </span>
          </div>
          <div className={styles.chips}>
            {blocs.government.length === 0 ? (
              <span className={styles.chipEmpty}>None</span>
            ) : (
              blocs.government.map(function (row) {
                return <PartyChip key={row.partyId} row={row} />;
              })
            )}
          </div>
        </div>

        <div className={styles.blocDivider} />

        <div className={styles.bloc}>
          <div className={styles.blocHead}>
            <span className={styles.blocLabel}>Opposition</span>
            <span className={styles.blocMeta}>{blocs.opposition.length + " parties"}</span>
          </div>
          <div className={styles.chips}>
            {blocs.opposition.length === 0 ? (
              <span className={styles.chipEmpty}>None</span>
            ) : (
              blocs.opposition.map(function (row) {
                return <PartyChip key={row.partyId} row={row} />;
              })
            )}
          </div>
        </div>
      </div>

      {government ? (
        <div className={styles.meters}>
          <Meter label="Stability" value={government.stability} />
          <Meter label="Cohesion" value={government.cohesion} />
        </div>
      ) : null}

      {/*
        The mayor is gated on the payload, not on the theme: under a pure list system the engine
        publishes null here and the block disappears on its own, which keeps one code path for both
        electoral systems as the contract asks.
      */}
      {mayor ? (
        <div className={styles.mayor}>
          <span className={styles.mayorTag}>Mayor</span>
          <div className={styles.swatchLarge} style={{ backgroundColor: mayorColor }} />
          <div className={styles.mayorText}>
            <span className={styles.mayorName}>{mayor.name || mayorPartyLabel}</span>
            <span className={styles.mayorMeta}>
              {mayorPartyLabel} {"·"} since {formatMonth(mayor.sinceDate)} {"·"} won by{" "}
              {points(mayor.margin)}
            </span>
          </div>
        </div>
      ) : null}

      {blocs.unseated.length > 0 ? (
        <div className={styles.footer}>
          <span className={styles.footerLabel}>No seats</span>
          {blocs.unseated.map(function (row) {
            return (
              <span key={row.partyId} className={styles.footerItem}>
                <span className={styles.footerSwatch} style={{ backgroundColor: row.color }} />
                {row.label} {pct(row.voteShare)}
                {row.passedThreshold ? "" : "*"}
              </span>
            );
          })}
          {blocs.unseated.some(function (row) { return !row.passedThreshold; }) ? (
            <span className={styles.footerNote}>* below the threshold</span>
          ) : null}
        </div>
      ) : null}
    </div>
  );
};

// -- small pieces --------------------------------------------------------------------------------

const PartyChip = (props: { row: ChartRow }) => {
  const row = props.row;
  return (
    <div className={row.isLead ? styles.chipLead : styles.chip}>
      <div className={styles.swatch} style={{ backgroundColor: row.color }} />
      <span className={styles.chipName}>{row.label}</span>
      <span className={styles.chipSeats}>{row.seats}</span>
      <span className={styles.chipShare}>{pct(row.voteShare)}</span>
    </div>
  );
};

const Meter = (props: { label: string; value: number }) => {
  return (
    <div className={styles.meterRow}>
      <span className={styles.meterLabel}>{props.label}</span>
      <div className={styles.meterTrack}>
        <div className={styles.meterFill} style={{ width: clamp01(props.value) * 100 + "%" }} />
      </div>
      <span className={styles.meterValue}>{pct(props.value)}</span>
    </div>
  );
};
