import { useMemo } from "react";
import { useValue } from "cs2/api";
import { Scrollable } from "cs2/ui";
import { PanelBoundary } from "./Boundary";
import { PartyDetailPane } from "./PartyDetail";
import { PartyList } from "./PartyList";
import {
  EMPTY_PARTY_PALETTE,
  EMPTY_STATE_SUMMARY,
  allocation$,
  colorPalette$,
  enabled$,
  factions$,
  government$,
  latestPoll$,
  mandates$,
  ready$,
  roster$,
  selectedPartyId$,
  summary$,
} from "./bindings";
import styles from "./PartiesPanel.module.scss";

/**
 * Panel 26 — Parties. Who each party is, where it stands on the six issues, and how it is doing.
 *
 * Reads only bindings recorded in docs/contracts/ui_bindings.md and computes no politics of its
 * own: every share, position and count on screen was published by the engine.
 *
 * The one structural rule here is the remount. `useMapValue`'s round trip is synchronous and its
 * state initialiser does not re-run when the key changes, so a detail pane that kept its identity
 * across a selection change would show the PREVIOUS party's numbers for one frame. The pane is
 * therefore keyed by the selected id, and no map binding is hoisted into this component - the panel
 * does not remount, so a key changing under a live hook is exactly the bug being avoided.
 */

function findParty(list: Agora.PartyBrief[], id: string): Agora.PartyBrief | null {
  for (let i = 0; i < list.length; i++) {
    if (list[i].id === id) {
      return list[i];
    }
  }
  return null;
}

/**
 * "" means "the player has not chosen yet", and the panel resolves it to the first published row
 * AT RENDER TIME rather than writing the id back from an effect. A write-back would produce exactly
 * the extra render - and the extra key change - that the remount above exists to avoid.
 *
 * A stored id is checked against the live roster before it is honoured. No party id ever leaves the
 * register WITHIN one save, but `selectedPartyId$` is a module-level bindLocalValue and so lives for
 * the session: switching the theme replaces the register wholesale (the engine regenerates it and
 * never merges), and loading a second save swaps it outright. Either leaves a selection pointing at
 * an id that is no longer on the roster, and honouring it unchecked makes the pane report that the
 * save has no parties while the rail beside it lists them all.
 */
function resolveSelection(list: Agora.PartyBrief[], stored: string): string {
  if (stored && findParty(list, stored)) {
    return stored;
  }
  return list.length > 0 ? list[0].id : "";
}

const PartiesPanelInner = (): JSX.Element | null => {
  const enabled = useValue(enabled$);
  const ready = useValue(ready$);
  const rawSummary = useValue(summary$);
  const rawRoster = useValue(roster$);
  const rawAllocation = useValue(allocation$);
  const rawPoll = useValue(latestPoll$);
  const rawGovernment = useValue(government$);
  const rawFactions = useValue(factions$);
  // Pushed, and subscribed ONCE here like the roster and the faction list - the pane filters it to
  // the open party. This is the News tab's published mandate list read as a per-party scorecard;
  // there is no per-party binding and none should be added (see bindings.ts).
  const rawMandates = useValue(mandates$);
  // Not for a picker here - the editors subscribe their own. This is the discriminator below, and it
  // is subscribed for that alone: the palette is the one binding on this panel that comes from
  // TUNING rather than from state, so it is non-empty on every save whose publisher completed a pass.
  const rawPalette = useValue(colorPalette$);
  const storedId = useValue(selectedPartyId$);

  // A binding can hand over a null payload during a partial deploy; the fallback argument only
  // covers the frames before the first publish. Guard rather than let a null reach a field.
  const summary: Agora.StateSummary = rawSummary || EMPTY_STATE_SUMMARY;
  const roster: Agora.PartyBrief[] = rawRoster || [];
  const allocation: Agora.SeatRow[] = rawAllocation || [];
  const poll: Agora.PollSummary | null = rawPoll || null;
  const government: Agora.GovernmentSummary | null = rawGovernment || null;
  const factions: Agora.FactionBrief[] = rawFactions || [];
  const mandates: Agora.MandateRow[] = rawMandates || [];
  const palette: Agora.PartyPalette = rawPalette || EMPTY_PARTY_PALETTE;

  // The rail's two number columns. Lookups only - neither map is ever iterated for ordering, so
  // they introduce no iteration-order dependence, and neither costs a per-row map subscription.
  const seatsById = useMemo(() => {
    const index: Record<string, number> = {};
    for (let i = 0; i < allocation.length; i++) {
      const row = allocation[i];
      if (row && row.partyId) {
        index[row.partyId] = row.seats;
      }
    }
    return index;
  }, [allocation]);

  const pollShareById = useMemo(() => {
    const index: Record<string, number> = {};
    const shares = poll && poll.shares ? poll.shares : [];
    for (let i = 0; i < shares.length; i++) {
      const share = shares[i];
      if (share && share.partyId) {
        index[share.partyId] = share.share;
      }
    }
    return index;
  }, [poll]);

  // Master toggle off means the player sees no trace of the mod - not a disabled shell.
  if (!enabled) {
    return null;
  }

  const selectedId = resolveSelection(roster, storedId);
  const selected = selectedId ? findParty(roster, selectedId) : null;

  /**
   * The panel is dark for a reason it cannot otherwise name.
   *
   * `ready` and `enabled` are GETTERS on the publisher (AgoraUISystemBase) and answer true whether or
   * not `Publish()` ever completed a pass, so a publisher that threw gates straight through to the
   * empty-roster branch and reports "this save has no parties" - a political claim, and a false one.
   *
   * The palette separates the two. It is built from `EngineTuning.Parties.ColorPalette`, not from
   * state, and the summary date is the engine's own clock: on any save whose publisher finished, both
   * are non-empty even before a single party exists. All three empty at once is not a save with no
   * politics, it is a panel that was never written to, and the only honest thing to print is where to
   * look. A roster genuinely empty beside a populated palette keeps the sentence it always had.
   */
  const publishFailed = ready && roster.length === 0 && palette.colors.length === 0 && !summary.date;

  return (
    <div className={styles.panel}>
      <div className={styles.header}>
        <div className={styles.title}>AGORA / PARTIES</div>
        <div className={styles.headerMeta}>
          <span className={styles.headerDate}>{summary.date || "-"}</span>
          {/* Only once the engine has published a state. The empty summary's system is
              "Proportional", so rendering this chip before the first tick states an electoral
              system the save may not have - a false political fact, briefly, on every NA load. */}
          {ready ? (
            <span className={styles.headerSystem}>
              {summary.system === "FirstPastThePost" ? "First past the post" : "Proportional"}
            </span>
          ) : null}
        </div>
      </div>

      {!ready ? (
        <div className={styles.skeleton}>
          <div className={styles.skeletonTitle}>Waiting for the first political tick</div>
          <div className={styles.skeletonBody}>
            The engine has not published a political state yet. The party register appears on the
            first monthly tick after the save loads.
          </div>
        </div>
      ) : publishFailed ? (
        <div className={styles.skeleton}>
          <div className={styles.skeletonTitle}>This panel was never written to</div>
          <div className={styles.skeletonBody}>
            The engine reports it is running, but nothing reached this tab - not the party register,
            not the colour palette, not even today&apos;s date. That is the dashboard publisher
            failing rather than a city with no politics. Agora.log records the reason; look for
            &quot;could not register its bindings&quot; or a publisher failure near the save load.
          </div>
        </div>
      ) : (
        <div className={styles.body}>
          <PartyList
            parties={roster}
            seatsById={seatsById}
            pollShareById={pollShareById}
            hasPoll={!!poll}
            selectedId={selectedId}
            onSelect={(id: string) => selectedPartyId$.update(id)}
          />
          <div className={styles.detailColumn}>
            <Scrollable vertical className={styles.detailScroll}>
              {selected ? (
                // Keyed by party id so switching parties remounts the pane and its map binding
                // subscription, rather than re-keying a live subscription in place. Do not memoise
                // this element across a selection change - that defeats the key.
                <PartyDetailPane
                  key={selectedId}
                  partyId={selectedId}
                  brief={selected}
                  system={summary.system}
                  government={government}
                  // A counted chamber and a published poll, both save-wide. The coalition section
                  // needs them to say which of its two empty states it is in; nothing else reads
                  // them, and neither is re-derived down there from a list's length.
                  hasChamber={allocation.length > 0}
                  hasCityPoll={!!poll}
                  factions={factions}
                  roster={roster}
                  mandates={mandates}
                />
              ) : (
                <div className={styles.emptyPane}>
                  No parties are published for this save yet. The register is built on the first
                  political tick, and no party ever leaves it afterwards - a party that dies is
                  marked dissolved and stays in the list.
                </div>
              )}
            </Scrollable>
          </div>
        </div>
      )}
    </div>
  );
};

export const PartiesPanel = (): JSX.Element => (
  <PanelBoundary>
    <PartiesPanelInner />
  </PanelBoundary>
);
