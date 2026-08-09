import { Tooltip } from "cs2/ui";
import {
  hexToRgba,
  int,
  joinNames,
  monthYear,
  partyLabel,
  pct,
  revivalPhrase,
  yearOf,
} from "./format";
import styles from "./HistoryStrip.module.scss";

/**
 * Where a party came from, and how it has done at the ballot box.
 *
 * Two parts. The lineage sentence is assembled from four scalars on the detail payload; the seat
 * bars are one column per row of `agora.parties.electionRecord`, oldest first as published.
 *
 * Every party named in the sentence is resolved through the ROSTER, never from the detail payload -
 * a predecessor or successor may itself be dissolved, and a dissolved brand stays in the register
 * for the life of the save, so the label always resolves. An id that somehow does not resolve is
 * rendered as "Unnamed party": a raw party id must never reach the player.
 *
 * The series is not a calendar. An election this party had no part in contributes no row at all
 * (contract section 4.2), so a gap between two columns is a party that had not been founded yet or
 * had been dissolved - never a defeat. A row that IS present with no seats is a real result and is
 * drawn as a sliver: `.bar` carries a minimum height so that "stood and won nothing" still leaves a
 * findable column with a year under it, rather than collapsing to nothing the player cannot hover.
 *
 * The threshold note is rendered only for a row the seat table actually contains (`hasSeatRecord`).
 * A row that is on the ballot with no matching allocation carries `passedThreshold` false because
 * nobody set it, not because the engine judged the party short of the threshold, and the pane must
 * not put a political fact on screen the engine never stated.
 */

/** A [0,1] fraction of the plot as a CSS height. */
function heightPct(value: number): string {
  if (typeof value !== "number" || !isFinite(value) || value <= 0) {
    return "0%";
  }
  return (value > 1 ? 100 : value * 100).toFixed(3) + "%";
}

/** The tallest result in the series, so the strip scales to this party's own best showing. */
function maxSeats(rows: Agora.PartyElectionRow[]): number {
  let max = 0;
  for (let i = 0; i < rows.length; i++) {
    const seats = rows[i] ? rows[i].seats : 0;
    if (typeof seats === "number" && isFinite(seats) && seats > max) {
      max = seats;
    }
  }
  return max;
}

const FILL_ALPHA = 0.85;

export const HistoryStrip = (props: {
  detail: Agora.PartyDetail;
  /** From the roster brief, which is correct for the selected id on every frame. */
  foundedDate: string;
  dissolvedDate: string;
  /** Oldest first, as published. Never reversed or re-sorted here. */
  rows: Agora.PartyElectionRow[];
  /** The whole published register, for resolving predecessor, successor and absorbed labels. */
  roster: Agora.PartyBrief[];
  colorHex: string;
}): JSX.Element => {
  const detail = props.detail;
  const rows = props.rows || [];
  const roster = props.roster || [];

  // A lookup, never an iteration whose order reaches the screen: the absorbed list arrives sorted
  // in C# and the sentence follows that order, not this map's.
  const labelOf = (id: string): string => {
    for (let i = 0; i < roster.length; i++) {
      const brief = roster[i];
      if (brief && brief.id === id) {
        return partyLabel(brief.name, brief.shortName);
      }
    }
    return partyLabel("", "");
  };

  // A party as it appears inside a sentence, article included. "the Unnamed party" is not English,
  // so the placeholder takes its own article rather than the definite one.
  const partyPhrase = (id: string): string => {
    const label = labelOf(id);
    return label === partyLabel("", "") ? "an unnamed party" : "the " + label;
  };

  const clauses: string[] = [];

  if (props.foundedDate) {
    clauses.push("Founded " + monthYear(props.foundedDate) + ".");
  }

  if (detail.predecessorPartyId) {
    clauses.push("Split from " + partyPhrase(detail.predecessorPartyId) + ".");
  }

  const revivals = revivalPhrase(detail.revivalCount);
  if (revivals) {
    clauses.push("Revived " + revivals + ".");
  }

  const absorbed = detail.absorbedPartyIds || [];
  if (absorbed.length > 0) {
    const names: string[] = [];
    for (let i = 0; i < absorbed.length; i++) {
      names.push(partyPhrase(absorbed[i]));
    }
    clauses.push("Absorbed " + joinNames(names) + ".");
  }

  if (detail.successorPartyId) {
    clauses.push("Merged into " + partyPhrase(detail.successorPartyId) + ".");
  }

  if (props.dissolvedDate) {
    clauses.push("Dissolved " + monthYear(props.dissolvedDate) + ".");
  }

  const scale = maxSeats(rows);
  const fill = hexToRgba(props.colorHex, FILL_ALPHA);

  return (
    <div className={styles.strip}>
      <div className={styles.lineage}>
        {clauses.length > 0 ? clauses.join(" ") : "Nothing is recorded about this party's origins."}
      </div>

      {rows.length === 0 ? (
        <div className={styles.empty}>No elections held yet.</div>
      ) : (
        <div className={styles.plot}>
          {rows.map((row, index) => {
            const seats = row && typeof row.seats === "number" && isFinite(row.seats) ? row.seats : 0;
            // Only a row the seat table actually produced can be reported as short of the
            // threshold; without one the flag is an unset default, not a verdict.
            const below = !!row && row.hasSeatRecord && !row.passedThreshold;
            return (
              <Tooltip
                // The election id is the natural key; the index guards against a duplicate id in a
                // save written by an older build. The series is rebuilt whole, never spliced.
                key={(row && row.electionId ? row.electionId : "election") + "-" + index}
                direction="up"
                tooltip={
                  <div className={styles.tip}>
                    <div className={styles.tipTitle}>{row && row.date ? row.date : ""}</div>
                    <div className={styles.tipBody}>
                      {int(seats) +
                        (seats === 1 ? " seat, " : " seats, ") +
                        pct(row.seatShare) +
                        " of the chamber"}
                    </div>
                    <div className={styles.tipBody}>
                      {"Took " + pct(row.voteShare, 1) + " of the vote"}
                    </div>
                    {row.isSnapElection ? (
                      <div className={styles.tipNote}>
                        A snap election, called by a coalition collapse rather than by the calendar.
                      </div>
                    ) : null}
                    {below ? (
                      <div className={styles.tipNote}>
                        Fell below the electoral threshold at this count.
                      </div>
                    ) : null}
                    {!row.wasOnBallot ? (
                      <div className={styles.tipNote}>
                        These seats are recorded against a party the ballot list does not name.
                      </div>
                    ) : null}
                  </div>
                }
              >
                <div className={styles.column}>
                  <div className={styles.barTrack}>
                    <div
                      className={below ? styles.barBelow : styles.bar}
                      style={below ? { height: heightPct(scale > 0 ? seats / scale : 0) } : {
                        height: heightPct(scale > 0 ? seats / scale : 0),
                        backgroundColor: fill,
                      }}
                    />
                  </div>
                  {/* A tick rather than a differently shaped bar, so the run still reads as one
                      series and the snap elections read as annotations on it. */}
                  <div className={row.isSnapElection ? styles.snapTick : styles.snapTickHidden} />
                  <div className={below ? styles.yearFaint : styles.year}>
                    {yearOf(row.date) || "-"}
                  </div>
                </div>
              </Tooltip>
            );
          })}
        </div>
      )}

      {rows.length > 0 ? (
        <div className={styles.footnote}>
          {(rows.length === 1 ? "1 election contested" : int(rows.length) + " elections contested") +
            ", oldest first. Tallest bar is " +
            (scale === 1 ? "1 seat" : int(scale) + " seats") +
            "."}
        </div>
      ) : null}
    </div>
  );
};
