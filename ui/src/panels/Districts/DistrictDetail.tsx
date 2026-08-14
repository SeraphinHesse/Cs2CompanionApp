import { useMemo } from "react";
import { useMapValue } from "cs2/api";
import { Tooltip } from "cs2/ui";
import { EMPTY_DISTRICT_DETAIL, districtCrosstab$, districtDetail$ } from "./bindings";
import {
  BarSegment,
  CityFallbackBanner,
  CityValue,
  Meter,
  SectionTitle,
  StackedBar,
  StatCell,
} from "./Bits";
import { Crosstab } from "./Crosstab";
import {
  NO_VALUE,
  PartyIndex,
  clamp01,
  happinessText,
  int,
  makeFallbackSet,
  money,
  partyColor,
  partyName,
  pct,
  points,
} from "./format";
import styles from "./DetailPane.module.scss";

/**
 * One district: who it voted for, how it lives, and the wealth x education crosstab underneath.
 *
 * Both payloads are map bindings fetched for this key alone (contract section 4.4) - a district
 * can be deleted while its pane is open, and an unknown key returns the empty value rather than
 * throwing, so the pane has to render that state instead of assuming data.
 */

const WEALTH_COLORS = ["#6f8398", "#9db8d0", "#e0c489"];

const EDUCATION_COLORS = ["#5c6a78", "#748b9a", "#8fa8ab", "#a9c1b4", "#c7dcc9"];

const AGE_COLORS = ["#c9a06a", "#b98b8b", "#7f9fc0", "#8d8fa8"];

/**
 * `decidedByTieBreak` used to render as a bare "TIE-BREAK" chip, which names a thing without
 * saying what it means or what it implies about the number sitting next to it. The badge now
 * carries a phrase, with the rule behind a tooltip so the row stays one line.
 */
const TieBreakBadge = (): JSX.Element => (
  <Tooltip
    direction="up"
    tooltip={
      <div className={styles.tip}>
        The top two parties finished too close to separate, so the seat went to the engine's
        tie-break rather than to a lead. The tie-break is seeded, not a live coin flip: the same
        save on the same date always resolves it the same way. Read the margin beside it as
        effectively nothing.
      </div>
    }
  >
    <span className={styles.tieBadge}>Too close to call - tie-break</span>
  </Tooltip>
);

export const DistrictDetailPane = (props: {
  districtId: string;
  brief: Agora.DistrictBrief;
  parties: PartyIndex;
  system: Agora.ElectoralSystemName;
}): JSX.Element => {
  const rawDetail = useMapValue(districtDetail$, props.districtId);
  const rawCrosstab = useMapValue(districtCrosstab$, props.districtId);

  const detail: Agora.DistrictDetail = rawDetail || EMPTY_DISTRICT_DETAIL;
  const cells: Agora.CrosstabCell[] = rawCrosstab && rawCrosstab.length ? rawCrosstab : [];

  // The nested groups are contractual, but a missing one would take the whole pane down for a
  // cosmetic payload gap. Fall back to the documented empty shape instead.
  const wealth = detail.wealth || EMPTY_DISTRICT_DETAIL.wealth;
  const education = detail.education || EMPTY_DISTRICT_DETAIL.education;
  const age = detail.age || EMPTY_DISTRICT_DETAIL.age;
  const indices = detail.indices || EMPTY_DISTRICT_DETAIL.indices;
  const budget = detail.budget || EMPTY_DISTRICT_DETAIL.budget;

  const fallbacks = useMemo(
    () => makeFallbackSet(detail.hasCityFallbacks, detail.cityFallbackFields),
    [detail.hasCityFallbacks, detail.cityFallbackFields]
  );

  const wealthSegments: BarSegment[] = useMemo(
    () => [
      { key: "low", label: "Low", share: wealth.low, color: WEALTH_COLORS[0] },
      { key: "middle", label: "Middle", share: wealth.middle, color: WEALTH_COLORS[1] },
      { key: "high", label: "High", share: wealth.high, color: WEALTH_COLORS[2] },
    ],
    [wealth]
  );

  const educationSegments: BarSegment[] = useMemo(
    () => [
      {
        key: "uneducated",
        label: "Uneducated",
        share: education.uneducated,
        color: EDUCATION_COLORS[0],
      },
      {
        key: "poorlyEducated",
        label: "Poorly educated",
        share: education.poorlyEducated,
        color: EDUCATION_COLORS[1],
      },
      {
        key: "educated",
        label: "Educated",
        share: education.educated,
        color: EDUCATION_COLORS[2],
      },
      {
        key: "wellEducated",
        label: "Well educated",
        share: education.wellEducated,
        color: EDUCATION_COLORS[3],
      },
      {
        key: "highlyEducated",
        label: "Highly educated",
        share: education.highlyEducated,
        color: EDUCATION_COLORS[4],
      },
    ],
    [education]
  );

  const ageSegments: BarSegment[] = useMemo(
    () => [
      { key: "child", label: "Child", share: age.child, color: AGE_COLORS[0] },
      { key: "teen", label: "Teen", share: age.teen, color: AGE_COLORS[1] },
      { key: "adult", label: "Adult", share: age.adult, color: AGE_COLORS[2] },
      { key: "elderly", label: "Elderly", share: age.elderly, color: AGE_COLORS[3] },
    ],
    [age]
  );

  // Sorted by partyId ascending in C#. Rendered in that order; the panel does not re-sort.
  const voteSegments: BarSegment[] = useMemo(() => {
    const shares = detail.shares || [];
    const segments: BarSegment[] = [];
    for (let i = 0; i < shares.length; i++) {
      const share = shares[i];
      segments.push({
        key: share.partyId,
        label: partyName(props.parties, share.partyId),
        share: share.share,
        color: partyColor(props.parties, share.partyId),
      });
    }
    return segments;
  }, [detail.shares, props.parties]);

  const detailPublished = !!detail.id;
  const brief = props.brief;

  // The composition panel is a city stand-in when the demographic inputs behind it fell back.
  const compositionIsStandIn =
    fallbacks.has("wealth") || fallbacks.has("education") || fallbacks.has("population");

  return (
    <div className={styles.pane}>
      <div className={styles.paneHead}>
        <div className={styles.paneTitleBlock}>
          <div className={styles.paneName}>{detail.name || brief.name || props.districtId}</div>
          <div className={styles.paneId}>{props.districtId}</div>
        </div>
      </div>

      {!detailPublished ? (
        <div className={styles.notice}>
          No detail published for this district yet. It was either drawn since the last political
          tick, or the player deleted it while this pane was open. The list figures beside it are
          the last thing the engine published: {int(brief.population)} people,{" "}
          {pct(brief.turnout)} turnout.
        </div>
      ) : null}

      <CityFallbackBanner fallbacks={fallbacks} />

      <div className={styles.statRow}>
        <StatCell
          label="Population"
          value={int(detail.population)}
          field="population"
          fallbacks={fallbacks}
        />
        <StatCell
          label="Households"
          value={int(detail.households)}
          field="households"
          fallbacks={fallbacks}
        />
        <StatCell
          label="Eligible"
          value={int(detail.eligibleVoters)}
          field="eligibleVoters"
          fallbacks={fallbacks}
        />
        <StatCell
          label="Votes cast"
          value={int(detail.votesCast)}
          field="votesCast"
          fallbacks={fallbacks}
        />
      </div>

      <SectionTitle
        title="Vote split"
        note={
          props.system === "FirstPastThePost"
            ? "First past the post - this district returns its own seat"
            : "Proportional - this district feeds the city list"
        }
      />

      <div className={styles.winnerRow}>
        <span
          className={styles.winnerSwatch}
          style={{ backgroundColor: partyColor(props.parties, detail.winningPartyId) }}
        />
        <CityValue field="winningPartyId" fallbacks={fallbacks}>
          <span className={styles.winnerName}>
            {detail.winningPartyId ? partyName(props.parties, detail.winningPartyId) : NO_VALUE}
          </span>
        </CityValue>
        <span className={styles.winnerSep}>leads by</span>
        <CityValue field="margin" fallbacks={fallbacks}>
          <span className={styles.winnerMargin}>{points(detail.margin)}</span>
        </CityValue>
        {detail.decidedByTieBreak ? <TieBreakBadge /> : null}
        {props.system === "FirstPastThePost" ? (
          <span className={styles.seatBadge}>{int(detail.seats)} seat</span>
        ) : null}
      </div>

      <div className={fallbacks.has("shares") ? styles.dimmedBlock : styles.block}>
        {voteSegments.length > 0 ? (
          <StackedBar segments={voteSegments} legendValue={(s) => pct(s.share, 1)} />
        ) : (
          <div className={styles.notice}>No vote shares published for this district yet.</div>
        )}
      </div>

      <SectionTitle title="Conditions" />
      <div className={styles.meterRow}>
        <Meter
          label="Turnout"
          value={pct(detail.turnout)}
          fill={detail.turnout}
          tint="#4fb3a5"
          field="turnout"
          fallbacks={fallbacks}
        />
        <Meter
          label="Happiness"
          value={happinessText(detail.happiness) + " / 100"}
          fill={clamp01(detail.happiness / 100)}
          tint="#7aae52"
          field="happiness"
          fallbacks={fallbacks}
        />
        <Meter
          label="Unemployment"
          value={pct(detail.unemployment, 1)}
          fill={detail.unemployment}
          tint="#c25b4a"
          field="unemployment"
          fallbacks={fallbacks}
        />
      </div>

      <SectionTitle
        title="Household budget"
        note="What the game bills a household here, and what survives it"
      />
      <div className={styles.meterRow}>
        <Meter
          label="Rent burden"
          value={pct(budget.rentBurden)}
          fill={budget.rentBurden}
          tint="#c9a06a"
          field="rentBurden"
          fallbacks={fallbacks}
        />
        <Meter
          label={budget.disposableMargin < 0 ? "Left after costs (overdrawn)" : "Left after costs"}
          value={pct(budget.disposableMargin)}
          fill={budget.disposableMargin}
          tint={budget.disposableMargin < 0 ? "#c25b4a" : "#4fb3a5"}
          field="disposableMargin"
          fallbacks={fallbacks}
        />
        <div className={styles.meterSpacer} />
      </div>
      <div className={styles.statRow}>
        <StatCell
          label="Rent / month"
          value={money(budget.averageRent)}
          field="averageRent"
          fallbacks={fallbacks}
        />
        <StatCell
          label="Upkeep / day"
          value={money(budget.averageHouseholdUpkeep)}
          field="averageHouseholdUpkeep"
          fallbacks={fallbacks}
        />
        <StatCell
          label="Goods / day"
          value={money(budget.averageHouseholdResourceSpend)}
          field="averageHouseholdResourceSpend"
          fallbacks={fallbacks}
        />
        <StatCell
          label="Fees / day"
          value={money(budget.averageHouseholdFees)}
          field="averageHouseholdFees"
          fallbacks={fallbacks}
        />
      </div>

      <SectionTitle title="Indices" note="All 0-1, published by the engine" />
      <div className={styles.meterRow}>
        <Meter
          label="Gentrification"
          value={pct(indices.gentrification)}
          fill={indices.gentrification}
          tint="#b98bc0"
          field="indices.gentrification"
          fallbacks={fallbacks}
        />
        <Meter
          label="Commute misery"
          value={pct(indices.commuteMisery)}
          fill={indices.commuteMisery}
          tint="#c9a06a"
          field="indices.commuteMisery"
          fallbacks={fallbacks}
        />
        <Meter
          label="Service coverage"
          value={pct(indices.serviceCoverage)}
          fill={indices.serviceCoverage}
          tint="#5b8dc2"
          field="indices.serviceCoverage"
          fallbacks={fallbacks}
        />
      </div>
      <div className={styles.meterRow}>
        <Meter
          label="Discontent"
          value={pct(indices.discontent)}
          fill={indices.discontent}
          tint="#c25b4a"
          field="indices.discontent"
          fallbacks={fallbacks}
        />
        <Meter
          label="Inequality (Gini)"
          value={pct(indices.gini)}
          fill={indices.gini}
          tint="#8d8fa8"
          field="indices.gini"
          fallbacks={fallbacks}
        />
        <div className={styles.meterSpacer} />
      </div>

      <SectionTitle title="Composition" />
      <div className={fallbacks.has("wealth") ? styles.dimmedBlock : styles.block}>
        <div className={styles.splitLabel}>Wealth</div>
        <StackedBar segments={wealthSegments} legendValue={(s) => pct(s.share)} compact />
      </div>
      <div className={fallbacks.has("education") ? styles.dimmedBlock : styles.block}>
        <div className={styles.splitLabel}>Education</div>
        <StackedBar segments={educationSegments} legendValue={(s) => pct(s.share)} compact />
      </div>
      <div className={fallbacks.has("age") ? styles.dimmedBlock : styles.block}>
        <div className={styles.splitLabel}>Age</div>
        <StackedBar segments={ageSegments} legendValue={(s) => pct(s.share)} compact />
      </div>

      <SectionTitle title="Wealth x education" note="Age bands collapsed into each cell" />
      <Crosstab
        cells={cells}
        parties={props.parties}
        scopeLabel={detail.name || brief.name || props.districtId}
        isCityStandIn={compositionIsStandIn}
      />
    </div>
  );
};
