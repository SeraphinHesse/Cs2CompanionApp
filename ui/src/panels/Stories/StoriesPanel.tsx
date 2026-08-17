import { useValue } from "cs2/api";
import { Scrollable } from "cs2/ui";

import {
  EMPTY_FLAVOR_STATUS,
  EMPTY_POWER,
  EMPTY_SETTINGS,
  enabled$,
  flavorStatus$,
  mandates$,
  power$,
  ready$,
  settings$,
  stories$,
  storyArchive$,
} from "../../shell/bindings";
import { useLookups } from "../../shell/lookup";
import { ArchiveList } from "./ArchiveList";
import { PanelBoundary } from "./Boundary";
import { FlavorStatusLine } from "./FlavorStatusLine";
import { MandateTracker } from "./MandateTracker";
import { StoryCard } from "./StoryCard";
import { EMPTY_STATE_SUMMARY, summary$ } from "./bindings";
import styles from "./StoriesPanel.module.scss";

/**
 * Panel 28 — Stories. The live stories the player can still act on, then the ones that have closed.
 *
 * Every number and every verdict on screen was published by the engine. This panel derives no tier
 * from a severity, no outcome from a response, no rejection from an affordability flag and no window
 * from a cycle length: all four are the engine's, and each has its own paragraph in contract §4.7
 * saying why a panel-side copy drifts.
 *
 * The `agora.stories` group is bound once in `ui/src/shell/bindings.ts` and imported from there —
 * three surfaces read it from three React trees, and one declaration is what makes that one
 * subscription. Only the story article map and `agora.state.summary` are declared locally, in
 * `./bindings.ts`, which says why.
 *
 * **Two things below came from the News panel in wave 7**, which is deleted: the mandate tracker and
 * the flavor status line with its manual wake. Both read `agora.news.*` — the binding group keeps its
 * name because renaming a live binding in place is what contract §7 forbids. Both sit INSIDE the
 * scroll region rather than under it: this panel's height is the column's to budget, and content
 * parked below the scrollable would be unreachable on a short screen.
 */

const StoriesPanelInner = (): JSX.Element | null => {
  const enabled = useValue(enabled$);
  const ready = useValue(ready$);
  const rawSettings = useValue(settings$);
  const rawSummary = useValue(summary$);
  const rawStories = useValue(stories$);
  const rawArchive = useValue(storyArchive$);
  const rawPower = useValue(power$);
  const rawMandates = useValue(mandates$);
  const rawFlavor = useValue(flavorStatus$);
  const lookups = useLookups();

  // A binding can hand over a null payload during a partial deploy; the fallback argument only covers
  // the frames before the first publish. Guard rather than let a null reach a field.
  const settings: Agora.SettingsPayload = rawSettings || EMPTY_SETTINGS;
  const summary: Agora.StateSummary = rawSummary || EMPTY_STATE_SUMMARY;
  const stories: Agora.Story[] = rawStories || [];
  const archive: Agora.StoryBrief[] = rawArchive || [];
  const power: Agora.Power = rawPower || EMPTY_POWER;
  const mandates: Agora.MandateRow[] = rawMandates || [];
  const flavor: Agora.FlavorStatus = rawFlavor || EMPTY_FLAVOR_STATUS;

  // Master toggle off means the player sees no trace of the mod - not a disabled shell.
  if (!enabled) {
    return null;
  }

  return (
    <div className={styles.panel}>
      <div className={styles.header}>
        <div className={styles.title}>AGORA / STORIES</div>
        <div className={styles.headerMeta}>
          <span className={styles.headerDate}>{summary.date || "-"}</span>
          {/* `enabled` false is not a balance of zero — a save with the power layer off has no such
              currency, so nothing is quoted at all. The counter proper lives beside the mod icon. */}
          {power.enabled ? (
            <span className={power.inDebt ? styles.headerPowerDebt : styles.headerPower}>
              {String(power.balance) + " political power"}
            </span>
          ) : null}
        </div>
      </div>

      {!ready ? (
        <div className={styles.skeleton}>
          <div className={styles.skeletonTitle}>Waiting for the first political tick</div>
          <div className={styles.skeletonBody}>
            The engine has not published a political state yet. Stories are drafted on a monthly tick
            after the save loads.
          </div>
        </div>
      ) : (
        <Scrollable vertical className={styles.scroll}>
          <div className={styles.body}>
            <div className={styles.sectionLabel}>Live</div>

            {stories.length === 0 ? (
              <div className={styles.bodyNote}>
                {settings.storiesEnabled
                  ? "No story is live right now. The next one is drafted on a coming month."
                  : "Stories are switched off for this save. Nothing new will be drafted; anything already live still resolves on its own month."}
              </div>
            ) : null}

            {/* In the order received — sorted by id ordinal in the engine and never re-sorted here. */}
            {stories.map(function (story) {
              return <StoryCard key={story.id} story={story} today={summary.date} />;
            })}

            <div className={styles.sectionLabel}>Closed</div>
            <ArchiveList rows={archive} />

            {/* In the engine's order — status rank, then deadline, then id (contract §4.5). Never
                re-sorted here, so the tracker opens on what is live and closest to due. */}
            <div className={styles.sectionLabel}>Mandates</div>
            <MandateTracker mandates={mandates} lookups={lookups} />

            <div className={styles.sectionLabel}>Prose writer</div>
            <FlavorStatusLine status={flavor} />
          </div>
        </Scrollable>
      )}
    </div>
  );
};

export const StoriesPanel = (): JSX.Element => (
  <PanelBoundary>
    <StoriesPanelInner />
  </PanelBoundary>
);
