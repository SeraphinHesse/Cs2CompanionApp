import { Scrollable } from "cs2/ui";
import { NO_VALUE, STATUS_CHIP, int, partyColor, partyLabel, partyShortLabel, pct } from "./format";
import styles from "./PartyList.module.scss";

/**
 * The party picker. Rows are rendered in the order C# published them (id ordinal ascending) - the
 * panel never re-sorts and never filters by name, because name is flavor.
 *
 * The two number columns come from `agora.seats.allocation` and `agora.seats.latestPoll`, NOT from
 * the detail map: subscribing a per-key map binding once per row would defeat the entire reason the
 * detail is a map. A party with no allocation row shows "-" rather than 0 - "holds no seats" and
 * "the last count did not list it" are different facts and the rail must not merge them.
 *
 * Dissolved brands stay in the list. A party id never leaves the roster (the registry marks it
 * Dissolved so it can revive), and a save's dead parties are half of what this tab is for, so they
 * are shown at reduced emphasis with a chip rather than hidden. The chip is derived from
 * `brief.status` - never from a name string.
 */

export const PartyList = (props: {
  parties: Agora.PartyBrief[];
  seatsById: Record<string, number>;
  pollShareById: Record<string, number>;
  hasPoll: boolean;
  selectedId: string;
  onSelect: (id: string) => void;
}): JSX.Element => {
  const parties = props.parties;

  return (
    <div className={styles.list}>
      <div className={styles.listHead}>
        <span className={styles.listTitle}>Parties</span>
        <span className={styles.listCount}>{int(parties.length)}</span>
      </div>

      <Scrollable vertical className={styles.scroll}>
        {parties.length === 0 ? (
          <div className={styles.emptyRow}>
            No parties published yet. The registry is built on the first political tick after the
            save loads.
          </div>
        ) : null}

        {parties.map((party) => {
          const selected = party.id === props.selectedId;
          const gone = party.status === "Dissolved" || party.status === "Merged";
          const chip = STATUS_CHIP[party.status];
          const seats = props.seatsById[party.id];
          const share = props.pollShareById[party.id];
          const full = partyLabel(party.name, party.shortName);
          const short = partyShortLabel(party.shortName, party.name);

          return (
            <div
              key={party.id}
              className={selected ? styles.rowSelected : gone ? styles.rowGone : styles.row}
              onClick={() => props.onSelect(party.id)}
            >
              <div className={styles.rowStripe} style={{ backgroundColor: partyColor(party.colorHex) }} />
              <div className={styles.rowBody}>
                <div className={styles.rowTop}>
                  <span className={styles.rowShort}>{short}</span>
                  {chip ? <span className={styles.rowChip}>{chip}</span> : null}
                </div>
                <div className={styles.rowName}>{full}</div>
                <div className={styles.rowMeta}>
                  <span className={styles.rowSeats}>
                    {typeof seats === "number" ? int(seats) + " seats" : NO_VALUE + " seats"}
                  </span>
                  <span className={styles.rowSep}>/</span>
                  <span className={styles.rowPoll}>
                    {props.hasPoll && typeof share === "number"
                      ? pct(share) + " poll"
                      : NO_VALUE + " poll"}
                  </span>
                </div>
              </div>
            </div>
          );
        })}
      </Scrollable>
    </div>
  );
};
