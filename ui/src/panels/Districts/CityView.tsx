import { Meter, SectionTitle } from "./Bits";
import { Crosstab } from "./Crosstab";
import { PartyIndex, int, pct } from "./format";
import styles from "./DetailPane.module.scss";

/**
 * The city-wide view: the default landing state, and the honest home for the city crosstab.
 *
 * Nothing here is a stand-in - these numbers are city numbers by definition. That is exactly why
 * the district panes have to mark theirs when they are not.
 */
export const CityView = (props: {
  cells: Agora.CrosstabCell[];
  indices: Agora.CityIndices;
  parties: PartyIndex;
  districtCount: number;
  fallbackCount: number;
}): JSX.Element => {
  const indices = props.indices;

  return (
    <div className={styles.pane}>
      <div className={styles.paneHead}>
        <div className={styles.paneTitleBlock}>
          <div className={styles.paneName}>City-wide</div>
          <div className={styles.paneId}>
            {props.districtCount === 0
              ? "No districts drawn"
              : int(props.districtCount) + " districts"}
          </div>
        </div>
      </div>

      {props.fallbackCount > 0 ? (
        <div className={styles.warnNotice}>
          {int(props.fallbackCount)} of {int(props.districtCount)} districts are reporting city-wide
          stand-ins for at least one figure. Those districts are badged CITY in the list, and their
          stand-in figures are marked inside their panes.
        </div>
      ) : null}

      {props.districtCount === 0 ? (
        <div className={styles.notice}>
          The player has not drawn any districts. AGORA models the whole city as one electorate
          until they do - nothing below is missing, there is simply nothing finer to show.
        </div>
      ) : null}

      <SectionTitle title="City indices" note="All 0-1, published by the engine" />
      <div className={styles.meterRow}>
        <Meter label="Inequality (Gini)" value={pct(indices.gini)} fill={indices.gini} tint="#8d8fa8" />
        <Meter
          label="Brain drain"
          value={pct(indices.brainDrain)}
          fill={indices.brainDrain}
          tint="#b98bc0"
        />
        <Meter
          label="Service inequality"
          value={pct(indices.serviceInequality)}
          fill={indices.serviceInequality}
          tint="#5b8dc2"
        />
      </div>
      <div className={styles.meterRow}>
        <Meter
          label="Commute misery"
          value={pct(indices.commuteMisery)}
          fill={indices.commuteMisery}
          tint="#c9a06a"
        />
        <Meter
          label="Polarization"
          value={pct(indices.polarization)}
          fill={indices.polarization}
          tint="#c25b4a"
        />
        <Meter
          label="Legitimacy"
          value={pct(indices.legitimacy)}
          fill={indices.legitimacy}
          tint="#4fb3a5"
        />
      </div>
      <div className={styles.meterRow}>
        <Meter
          label="Discontent"
          value={pct(indices.discontent)}
          fill={indices.discontent}
          tint="#c25b4a"
        />
        <div className={styles.meterSpacer} />
        <div className={styles.meterSpacer} />
      </div>

      <SectionTitle title="Wealth x education" note="Whole city, age bands collapsed" />
      <Crosstab
        cells={props.cells}
        parties={props.parties}
        scopeLabel="the city"
        isCityStandIn={false}
      />
    </div>
  );
};
