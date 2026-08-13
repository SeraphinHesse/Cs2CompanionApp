import { useMemo } from "react";
import { useMapValue } from "cs2/api";
import { relations$ } from "./bindings";
import { int, partyColor, partyLabel, partyShortLabel, pct, widthPct } from "./format";
import styles from "./CoalitionRelations.module.scss";

/**
 * Who this party could govern with, as the chamber stands today.
 *
 * The list is a LIVE view (contract section 4.2): the engine re-ranks the arrangements from where
 * every party stands NOW, so it answers "who could govern now" and not "who negotiated after the
 * last election". It drifts between elections, and that is the point of it.
 *
 * Three rules govern what this component is allowed to say.
 *
 *   1. **Absence is not refusal.** Nothing in the engine models a party declining a partner. An
 *      arrangement that never appears was simply never built: the ranking rejects a set either
 *      because the lead is too small or because two members are too far apart, and it cannot say
 *      which for a set it did not build - which is exactly why the payload carries no per-partner
 *      flag. So a party that shares no arrangement is filed under "No workable arrangement" and is
 *      never described as having refused, walked out or turned anyone down.
 *   2. **Branch on the electoral system, never on the list being empty** (contract section 4.3).
 *      Under first past the post the list is empty BY DESIGN, and inferring that from a zero-length
 *      array rather than from `summary.system` is the sniffing the contract forbids.
 *   3. **No raw id reaches the player.** Every member is resolved to a name through the roster, with
 *      the same "Unnamed party" fallback the history strip uses.
 *
 * The order is contractual - majority first, then minimum-winning, then score - and arrives capped
 * at eight. It is rendered as published: nothing here sorts, reverses or re-slices it.
 */

interface Pairings {
  /** Roster order, which is id ordinal ascending and therefore stable across runs. */
  workable: string[];
  unworkable: string[];
}

/**
 * Which other parties share at least one listed arrangement with this one.
 *
 * The membership index is a lookup and is never iterated: the two lists below are built by walking
 * the ROSTER, whose order is contractual, so what reaches the screen does not depend on the order
 * keys happened to be inserted.
 *
 * Parties that have left the chamber for good are left out entirely. A dissolved or merged brand
 * stays in the register for the life of the save, and listing one under "No workable arrangement"
 * would read as a live political judgement about a party that no longer exists.
 */
function pairings(
  options: Agora.CoalitionOption[],
  roster: Agora.PartyBrief[],
  partyId: string,
  labelOf: (id: string) => string
): Pairings {
  const shares: Record<string, boolean> = {};
  for (let i = 0; i < options.length; i++) {
    const members = options[i] ? options[i].memberPartyIds : null;
    if (!members) {
      continue;
    }
    for (let j = 0; j < members.length; j++) {
      if (members[j] && members[j] !== partyId) {
        shares[members[j]] = true;
      }
    }
  }

  const workable: string[] = [];
  const unworkable: string[] = [];
  for (let i = 0; i < roster.length; i++) {
    const brief = roster[i];
    if (!brief || !brief.id || brief.id === partyId) {
      continue;
    }
    if (brief.status === "Dissolved" || brief.status === "Merged") {
      continue;
    }
    (shares[brief.id] ? workable : unworkable).push(labelOf(brief.id));
  }

  return { workable: workable, unworkable: unworkable };
}

export const CoalitionRelations = (props: {
  partyId: string;
  /**
   * `Agora.StateSummary.system`, threaded down by the panel. The electoral system is a property of
   * the save, not of a coalition - `GovernmentSummary` carries no system field - and it is the only
   * thing the empty branch below is allowed to read.
   */
  system: Agora.ElectoralSystemName;
  /**
   * True once this city has held an election - `agora.seats.allocation` is non-empty, which is a
   * seated chamber and nothing else. Threaded because it is the ONE thing that separates the two
   * reasons an empty list can arrive on a proportional save: a chamber that produced no arrangement
   * containing this party, or a save with no chamber to read yet. Without it this component would
   * have to guess, and the guess is what read as broken.
   */
  hasChamber: boolean;
  /**
   * True once a poll has been published. Before the first election the engine seats a PROVISIONAL
   * chamber from the latest published poll (`ProvisionalChamber`), so a save with a poll has
   * arithmetic to show and a save without one genuinely has nothing yet - which is a sentence worth
   * printing rather than a bare "nothing".
   */
  hasPoll: boolean;
  /** The whole published register, for resolving member ids to names and colours. */
  roster: Agora.PartyBrief[];
}): JSX.Element => {
  // Subscribed HERE, in a component the panel remounts on every selection change, for the same
  // reason the pane's other three map subscriptions live where they do: `useMapValue`'s state
  // initialiser does not re-run when the key changes, so a subscription that outlived a selection
  // change would render the previous party's arrangements under this party's name.
  const rawOptions = useMapValue(relations$, props.partyId);
  const options: Agora.CoalitionOption[] = rawOptions || [];
  const roster = props.roster || [];

  // The same lookup the history strip makes, against the same register, with the same fallback: a
  // dissolved brand never leaves the roster, so a member id always has a name to resolve to, and an
  // id that somehow does not resolve reads as "Unnamed party" rather than as itself.
  const briefOf = (id: string): Agora.PartyBrief | null => {
    for (let i = 0; i < roster.length; i++) {
      if (roster[i] && roster[i].id === id) {
        return roster[i];
      }
    }
    return null;
  };

  const labelOf = (id: string): string => {
    const brief = briefOf(id);
    return brief ? partyLabel(brief.name, brief.shortName) : partyLabel("", "");
  };

  const shortLabelOf = (id: string): string => {
    const brief = briefOf(id);
    return brief ? partyShortLabel(brief.shortName, brief.name) : partyShortLabel("", "");
  };

  const colorOf = (id: string): string => {
    const brief = briefOf(id);
    return partyColor(brief ? brief.colorHex : "");
  };

  const pairs = useMemo(
    () => pairings(options, roster, props.partyId, labelOf),
    [options, roster, props.partyId]
  );

  // The note is not decoration: before the first election the seats behind these arrangements are
  // projected from a poll, not counted, and a player reading seat totals is owed that distinction.
  const title = (
    <div className={styles.sectionTitle}>
      <span className={styles.sectionTitleText}>Coalition arithmetic</span>
      <span className={styles.sectionNote}>
        {props.hasChamber
          ? "Recomputed from where the parties stand today"
          : "Projected from the latest poll - this city has not voted yet"}
      </span>
    </div>
  );

  // Rule 2. The system decides which sentence is shown; the length of the list never does. Under
  // first past the post there is no arithmetic to report at all, whatever the list happens to hold.
  if (props.system === "FirstPastThePost") {
    return (
      <div className={styles.card}>
        {title}
        <div className={styles.note}>
          Under first past the post the winning party governs alone.
        </div>
      </div>
    );
  }

  // Two genuinely different states, and saying so is the whole of it. With neither a chamber nor a
  // poll there is nothing to seat and no judgement has been made about this party; with either one
  // there is, and the arithmetic came up short - which rule 1 forbids describing as a refusal.
  if (options.length === 0) {
    return (
      <div className={styles.card}>
        {title}
        <div className={styles.note}>
          {!props.hasChamber && !props.hasPoll
            ? "Coalition options appear once the first poll is published. Until then there is no " +
              "measure of where the parties stand, so there is no chamber to add up - not even a " +
              "projected one."
            : "No arrangement of the current chamber that includes this party is viable as things " +
              "stand. That is the arithmetic coming up short, not a partner turning it down."}
        </div>
      </div>
    );
  }

  return (
    <div className={styles.card}>
      {title}

      <div className={styles.blockLabel}>Possible governments</div>

      {options.map((option, index) => {
        const members = option.memberPartyIds || [];
        return (
          <div
            // Member ids are ascending and unique per arrangement, so the joined key is the natural
            // one; the index guards a duplicate from an older build. The list is rebuilt whole.
            key={members.join("+") + "-" + index}
            className={option.isCurrentGovernment ? styles.optionRowGoverning : styles.optionRow}
          >
            <div className={styles.optionMembers}>
              {members.map((id) => (
                <span key={id} className={styles.member}>
                  <span
                    className={styles.memberSwatch}
                    style={{ backgroundColor: colorOf(id) }}
                  />
                  <span className={styles.memberName}>{shortLabelOf(id)}</span>
                </span>
              ))}
            </div>

            <div className={styles.optionMeta}>
              <span className={styles.metaSeats}>
                {int(option.seats) +
                  (option.seats === 1 ? " seat, " : " seats, ") +
                  pct(option.seatShare) +
                  " of the chamber"}
              </span>
              <span className={option.hasMajority ? styles.chip : styles.chipQuiet}>
                {option.hasMajority ? "Majority" : "Short of a majority"}
              </span>
              {option.isGrandCoalition ? (
                <span className={styles.chipQuiet}>Grand coalition</span>
              ) : null}
              {option.isCurrentGovernment ? (
                <span className={styles.chipGoverning}>Currently governing</span>
              ) : null}
            </div>

            <div className={styles.cohesionRow}>
              <span className={styles.cohesionLabel}>Cohesion</span>
              <div className={styles.cohesionTrack}>
                <div
                  className={styles.cohesionFill}
                  style={{ width: widthPct(option.cohesion) }}
                />
              </div>
              <span className={styles.cohesionValue}>{pct(option.cohesion)}</span>
            </div>

            {/* Only worth a line when there is more than one party to lead. */}
            {members.length > 1 ? (
              <div className={styles.leadLine}>{"Led by " + labelOf(option.leadPartyId)}</div>
            ) : null}

            {/* The flag is always true for an arrangement without a majority, so this note can only
                ever appear on one that has a majority and does not need every partner in it. */}
            {!option.isMinimumWinning ? (
              <div className={styles.surplus}>
                More partners than it needs - it would still hold a majority a party short.
              </div>
            ) : null}
          </div>
        );
      })}

      <div className={styles.blockLabel}>Who it can work with</div>

      <div className={styles.pairBlock}>
        <div className={styles.pairLabel}>Shares a workable arrangement with</div>
        <div className={pairs.workable.length > 0 ? styles.pairNames : styles.pairEmpty}>
          {pairs.workable.length > 0
            ? pairs.workable.join(", ")
            : "No other party appears alongside it in any arrangement above."}
        </div>
      </div>

      {pairs.unworkable.length > 0 ? (
        <div className={styles.pairBlock}>
          <div className={styles.pairLabel}>No workable arrangement</div>
          <div className={styles.pairNames}>{pairs.unworkable.join(", ")}</div>
        </div>
      ) : null}

      <div className={styles.footnote}>
        No party here has turned another down. An arrangement is listed when the seats add up and the
        platforms are close enough; a pairing that is absent is one the arithmetic never produced,
        and the engine records no reason for it either way.
      </div>
    </div>
  );
};
