import { ReactNode, useMemo } from "react";
import { useMapValue } from "cs2/api";
import { EMPTY_PARTY_DETAIL, partyDetail$ } from "./bindings";
import {
  NO_VALUE,
  ROLE_CHIP,
  ROLE_SENTENCE,
  STATUS_CHIP,
  factionSentence,
  int,
  partyColor,
  partyLabel,
  partyShortLabel,
  pct,
  signedPoints,
} from "./format";
import { PlatformBars } from "./PlatformBars";
import styles from "./PartyDetail.module.scss";

/**
 * One party: who it is, where it stands on the six issues, and how it is doing.
 *
 * The detail is a map binding fetched for this key alone (contract section 4.2). The round trip is
 * synchronous and `useMapValue`'s state initialiser does not re-run when the key changes, so this
 * component is REMOUNTED on every selection change (`key={selectedId}` in PartiesPanel) and then
 * checks, before rendering a single number, that the payload it holds is the one it was mounted
 * for:
 *
 *     const published = detail.id === props.partyId;
 *
 * That is tighter than "the payload is non-empty" on purpose. It makes rendering the previous
 * party's figures under this party's name structurally impossible, whatever the hook does. When it
 * is false the pane still has a correct header and lifecycle line, because those come from the
 * roster brief - a pushed ValueBinding that is always right for the selected id.
 *
 * There is deliberately no loading state: a map binding cannot answer a defined key with undefined,
 * so a spinner branch here would be dead code.
 */

const SectionTitle = (props: { title: string; note?: ReactNode }): JSX.Element => (
  <div className={styles.sectionTitle}>
    <span className={styles.sectionTitleText}>{props.title}</span>
    {props.note ? <span className={styles.sectionNote}>{props.note}</span> : null}
  </div>
);

const StatCell = (props: {
  label: string;
  value: string;
  sub?: string;
  tone?: "good" | "bad";
}): JSX.Element => (
  <div className={styles.stat}>
    <span className={styles.statLabel}>{props.label}</span>
    <span
      className={
        props.tone === "good"
          ? styles.statValueGood
          : props.tone === "bad"
          ? styles.statValueBad
          : styles.statValue
      }
    >
      {props.value}
    </span>
    <span className={styles.statSub}>{props.sub || " "}</span>
  </div>
);

/**
 * The header, kept as its own component and rendering only text.
 *
 * This is the seam fixplan W4 builds into: the rename field, the colour picker and the lock
 * affordances go INSIDE this component, reading `brief.nameLocked` / `brief.colorLocked`. W6 adds
 * no button, no input and no trigger - the tab is read-only.
 */
export const PartyDetailHeader = (props: {
  partyId: string;
  detail: Agora.PartyDetail;
  brief: Agora.PartyBrief;
}): JSX.Element => {
  const detail = props.detail;
  const brief = props.brief;

  // The same identity test the pane makes, against the same key. Testing `detail.id === brief.id`
  // instead would only be equivalent because the panel looks the brief up by the selected id - an
  // invariant of the caller, not of this component. The key is passed in so the check is local.
  const published = detail.id === props.partyId;
  const color = partyColor(published ? detail.colorHex || brief.colorHex : brief.colorHex);
  const name = published
    ? partyLabel(detail.name || brief.name, detail.shortName || brief.shortName)
    : partyLabel(brief.name, brief.shortName);
  const short = published
    ? partyShortLabel(detail.shortName || brief.shortName, detail.name || brief.name)
    : partyShortLabel(brief.shortName, brief.name);

  const statusChip = STATUS_CHIP[brief.status];
  const roleChip = published ? ROLE_CHIP[detail.governmentRole] : "";

  return (
    <div className={styles.paneHead}>
      <span className={styles.headSwatch} style={{ backgroundColor: color }} />
      <div className={styles.headTitleBlock}>
        <div className={styles.headName}>{name}</div>
        <div className={styles.headChips}>
          <span className={styles.headShort}>{short}</span>
          {statusChip ? <span className={styles.headChip}>{statusChip}</span> : null}
          {roleChip ? <span className={styles.headChip}>{roleChip}</span> : null}
          {brief.isIncumbent ? <span className={styles.headChip}>Holds the mayoralty</span> : null}
        </div>
      </div>
    </div>
  );
};

export const PartyDetailPane = (props: {
  partyId: string;
  brief: Agora.PartyBrief;
  system: Agora.ElectoralSystemName;
  government: Agora.GovernmentSummary | null;
  /** The whole published faction list, subscribed once by the panel - never once per rail row. */
  factions: Agora.FactionBrief[];
}): JSX.Element => {
  const rawDetail = useMapValue(partyDetail$, props.partyId);

  // This party's slice of the published faction list. A filter, never a sort: the list arrives
  // ordered by partyId then internal support then id (contract section 4.2), and filtering keeps
  // that order, so the names below read strongest-first and read the same way on every run.
  const factions = useMemo(() => {
    const rows: Agora.FactionBrief[] = [];
    const names: string[] = [];
    const all = props.factions || [];
    for (let i = 0; i < all.length; i++) {
      const row = all[i];
      if (row && row.partyId === props.partyId) {
        rows.push(row);
        // Names only. `row.id` is an identifier and must never reach the player, so an unnamed
        // faction is counted and left off the list rather than listed as itself.
        if (row.name) {
          names.push(row.name);
        }
      }
    }
    return { count: rows.length, names: names };
  }, [props.factions, props.partyId]);

  const detail: Agora.PartyDetail = rawDetail || EMPTY_PARTY_DETAIL;

  // The nested groups are contractual, but a missing one would take the whole pane down for a
  // cosmetic payload gap. Fall back to the documented empty shape instead.
  const platform = detail.platform || EMPTY_PARTY_DETAIL.platform;

  const published = detail.id === props.partyId;
  const brief = props.brief;

  const hasElection = detail.hasContestedElection;
  const hasPoll = detail.hasPoll;

  // "Polling" is a published poll and "Standing" is the engine's modelled city-wide support. They
  // are different claims, so the cell changes its label rather than quietly swapping the number.
  //
  // The cell is gated on hasPoll ALONE, not on hasElection. A party founded mid-term - a split, or
  // a new entry - has never been on a ballot but is polled like every other party currently
  // standing, so a published poll for it is a real figure and needs no prior vote share to be true.
  // Only the delta below needs one, and only the delta is gated on both.
  const standingLabel = hasElection && !hasPoll ? "Standing" : "Polling";
  const standingValue = hasPoll
    ? pct(detail.currentPollShare, 1)
    : hasElection
    ? pct(detail.currentStandingShare, 1)
    : NO_VALUE;

  const deltaValue =
    hasElection && hasPoll ? signedPoints(detail.pollDeltaSinceElection) : NO_VALUE;
  const deltaTone =
    hasElection && hasPoll && detail.pollDeltaSinceElection > 0
      ? "good"
      : hasElection && hasPoll && detail.pollDeltaSinceElection < 0
      ? "bad"
      : undefined;

  const standingNote = !hasElection
    ? "This party has never been on a ballot, so it holds no seats, has no last vote share, and " +
      "has no movement since an election to report. Those cells stay blank rather than showing a " +
      "zero that would read as a result." +
      (hasPoll ? " Its polling is a published figure and stands on its own." : "")
    : !hasPoll
    ? "No poll has been published yet. Standing is the engine's modelled city-wide support, not a " +
      "published poll, and it is labelled differently for that reason."
    : "";

  // A count off a published list, not a computed one: how many other parties sit in the coalition
  // this party is part of. Opposition and None get no count - the coalition is not theirs.
  const government = props.government;
  const inGovernment = detail.governmentRole === "Lead" || detail.governmentRole === "Member";
  const partnerCount =
    published && inGovernment && government && government.memberPartyIds
      ? Math.max(0, government.memberPartyIds.length - 1)
      : 0;


  return (
    <div className={styles.pane}>
      <PartyDetailHeader partyId={props.partyId} detail={detail} brief={brief} />

      {!published ? (
        <div className={styles.notice}>
          The engine has not published a detail for this party yet - it was most likely founded
          since the last political tick. The name, status and dates above and below come from the
          roster, which is current; the figures appear on the next tick.
        </div>
      ) : null}

      {published ? (
        <>
          {detail.slogan ? <div className={styles.slogan}>&ldquo;{detail.slogan}&rdquo;</div> : null}

          {detail.description ? (
            <div className={styles.description}>{detail.description}</div>
          ) : null}

          {!detail.slogan && !detail.description ? (
            <div className={styles.description}>The press has not written this party up yet.</div>
          ) : null}

          <SectionTitle
            title="Standing"
            note={
              hasPoll && detail.pollDate ? "Poll published " + detail.pollDate : "No published poll"
            }
          />
          <div className={styles.statRow}>
            <StatCell
              label="Seats"
              value={hasElection ? int(detail.seats) : NO_VALUE}
              sub={hasElection ? pct(detail.seatShare) + " of the chamber" : ""}
            />
            <StatCell
              label="Last vote"
              value={hasElection ? pct(detail.lastVoteShare, 1) : NO_VALUE}
            />
            <StatCell label={standingLabel} value={standingValue} />
            <StatCell label="Since the election" value={deltaValue} tone={deltaTone} />
          </div>
          {standingNote ? <div className={styles.note}>{standingNote}</div> : null}

          <SectionTitle title="Threshold" />
          <div className={styles.line}>
            {props.system === "FirstPastThePost"
              ? "First past the post - seats are won district by district and no electoral " +
                "threshold applies."
              : !hasElection
              ? "No election has been counted yet, so the electoral threshold has not been tested."
              : detail.passedThreshold
              ? "Cleared the electoral threshold at the last count."
              : "Fell below the electoral threshold at the last count."}
          </div>
          <div className={styles.lineDim}>
            {detail.consecutiveElectionsBelowThreshold > 0
              ? "Below the threshold at the last " +
                int(detail.consecutiveElectionsBelowThreshold) +
                " elections in a row. Enough consecutive misses and the party dissolves."
              : "No run of missed thresholds on record."}
          </div>

          <SectionTitle title="Issue priorities" note="Each axis runs from less to more" />
          <PlatformBars values={platform} colorHex={partyColor(detail.colorHex || brief.colorHex)} />
        </>
      ) : null}

      <SectionTitle title="Lifecycle" />
      <div className={styles.line}>
        Founded {brief.foundedDate || NO_VALUE}
        {brief.dissolvedDate ? ", dissolved " + brief.dissolvedDate : ""}.
      </div>
      {published ? (
        <>
          <div className={styles.line}>
            {ROLE_SENTENCE[detail.governmentRole]}
            {partnerCount === 1
              ? " Governing alongside one other party."
              : partnerCount > 1
              ? " Governing alongside " + int(partnerCount) + " other parties."
              : ""}
          </div>
          <div className={styles.lineDim}>{factionSentence(factions.count, factions.names)}</div>
        </>
      ) : null}
    </div>
  );
};
