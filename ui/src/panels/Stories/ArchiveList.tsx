import { cx, formatSimDate, storyOutcomeLabel } from "./format";
import styles from "./StoriesPanel.module.scss";

/**
 * The stories that have already closed, newest first — the engine's order, never re-sorted here.
 *
 * The score reads "met N of M scored", and **M is `scoredCount`, not `slotCount`**. Slots the engine
 * could not read are held rather than failed and are excluded from both halves of the ratio, so
 * "1 of 2" on a three-slot story is correct arithmetic. Substituting `slotCount` to tidy it would
 * report a held slot as a failure. The difference between the two counts is stated beside the ratio
 * instead, so a player can see where the missing slot went.
 */
export const ArchiveList = (props: { rows: Agora.StoryBrief[] }): JSX.Element => {
  if (props.rows.length === 0) {
    return (
      <div className={styles.bodyNote}>
        No story has closed yet. A story resolves on its own month, or the moment you press resolve.
      </div>
    );
  }

  return (
    <div className={styles.archive}>
      {props.rows.map(function (row) {
        const held = row.slotCount - row.scoredCount;
        const outcome = storyOutcomeLabel(row.outcome);
        return (
          <div key={row.id} className={styles.archiveRow}>
            <div className={styles.archiveMain}>
              <div className={styles.archiveHeadline}>{row.headline}</div>
              <div className={styles.archiveDates}>
                {"Opened " + formatSimDate(row.openedDate) +
                  ", resolved " + formatSimDate(row.resolvesDate)}
              </div>
            </div>
            <div className={styles.chips}>
              {outcome ? (
                <span
                  className={cx(
                    styles.chipOutcome,
                    row.outcome === "Success" && styles.chipGood,
                    row.outcome === "Failure" && styles.chipBad,
                    row.outcome === "Abandoned" && styles.chipHeld,
                  )}
                >
                  {outcome}
                </span>
              ) : null}
              <span className={styles.chipQuiet}>
                {"Met " + String(row.metCount) + " of " + String(row.scoredCount) + " scored"}
              </span>
              {held > 0 ? (
                <span className={styles.chipHeld}>
                  {held === 1
                    ? "1 of " + String(row.slotCount) + " held — unreadable"
                    : String(held) + " of " + String(row.slotCount) + " held — unreadable"}
                </span>
              ) : null}
            </div>
          </div>
        );
      })}
    </div>
  );
};
