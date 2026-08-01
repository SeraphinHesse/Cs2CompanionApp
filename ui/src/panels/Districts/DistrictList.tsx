import { Scrollable, Tooltip } from "cs2/ui";
import { PartyIndex, int, partyColor, partyShort, pct, points } from "./format";
import styles from "./DistrictList.module.scss";

/**
 * The district picker. Rows are rendered in the order C# published them (id ordinal ascending) -
 * the panel never re-sorts and never filters by name, because name is flavor.
 *
 * A district whose figures are city stand-ins is marked on the row itself, not only inside the
 * detail pane: the list is where a player compares districts, and a city number sitting in a
 * comparison unmarked is exactly the failure contract section 4.4 forbids.
 */

const CITY_ROW_TIP =
  "AGORA could not read some figures for this district, so city-wide numbers are standing in. " +
  "Everything shown for it is provisional.";

export const DistrictList = (props: {
  districts: Agora.DistrictBrief[];
  parties: PartyIndex;
  selectedId: string;
  fallbackCount: number;
  onSelect: (id: string) => void;
}): JSX.Element => {
  const districts = props.districts;

  return (
    <div className={styles.list}>
      <div className={styles.listHead}>
        <span className={styles.listTitle}>Districts</span>
        <span className={styles.listCount}>{int(districts.length)}</span>
      </div>

      <Scrollable vertical className={styles.scroll}>
        <div
          className={props.selectedId === "" ? styles.cityRowSelected : styles.cityRow}
          onClick={() => props.onSelect("")}
        >
          <div className={styles.rowBody}>
            <div className={styles.rowName}>City-wide</div>
            <div className={styles.rowMeta}>
              {districts.length === 0
                ? "No districts drawn"
                : props.fallbackCount > 0
                ? int(props.fallbackCount) + " of " + int(districts.length) + " on city data"
                : "All districts reporting"}
            </div>
          </div>
        </div>

        {districts.length === 0 ? (
          <div className={styles.emptyRow}>
            The city has no districts. AGORA still models the electorate city-wide - open the
            city-wide view above.
          </div>
        ) : null}

        {districts.map((district) => {
          const selected = district.id === props.selectedId;
          const color = partyColor(props.parties, district.leadingPartyId);
          return (
            <div
              key={district.id}
              className={selected ? styles.rowSelected : styles.row}
              onClick={() => props.onSelect(district.id)}
            >
              <div className={styles.rowStripe} style={{ backgroundColor: color }} />
              <div className={styles.rowBody}>
                <div className={styles.rowTop}>
                  <span className={styles.rowName}>{district.name || district.id}</span>
                  {district.hasCityFallbacks ? (
                    <Tooltip direction="up" tooltip={<div className={styles.tip}>{CITY_ROW_TIP}</div>}>
                      <span className={styles.rowBadge}>CITY</span>
                    </Tooltip>
                  ) : null}
                </div>
                <div
                  className={district.hasCityFallbacks ? styles.rowMetaFallback : styles.rowMeta}
                >
                  <span className={styles.rowLead}>
                    {partyShort(props.parties, district.leadingPartyId)} {pct(district.leadingShare)}
                  </span>
                  <span className={styles.rowSep}>/</span>
                  <span className={styles.rowMargin}>+{points(district.margin, 0)}</span>
                  <span className={styles.rowSep}>/</span>
                  <span className={styles.rowTurnout}>{pct(district.turnout)} turnout</span>
                </div>
                <div className={styles.rowPop}>{int(district.population)} people</div>
              </div>
            </div>
          );
        })}
      </Scrollable>
    </div>
  );
};
