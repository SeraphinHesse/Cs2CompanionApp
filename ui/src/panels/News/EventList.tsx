import styles from "./NewsPanel.module.scss";
import { Lookups } from "./lookup";
import { cx, formatSimDate, humanizeEnum, SEVERITY_STEPS } from "./format";

/**
 * Active timeline events — the world the headlines are reacting to.
 *
 * Sorted by firedDate DESCENDING then id ascending in C# and capped at 25. Rendered in arrival
 * order. `title` is catalog-authored; `localAngle` is FLAVOR and is "" until the LLM has run,
 * which is a normal state, not an error.
 */

interface EventListProps {
  events: Agora.TimelineEventBrief[];
  lookups: Lookups;
}

/** Districts named on one event, before the rest collapse into a "+N" chip. */
const MAX_DISTRICT_CHIPS = 3;

/** Tags shown on one event. The list is unbounded on the wire; the row is not. */
const MAX_TAG_CHIPS = 4;

export const EventList = ({ events, lookups }: EventListProps) => {
  if (events.length === 0) {
    return (
      <div className={styles.empty}>
        <div className={styles.emptyTitle}>Nothing is happening</div>
        <div className={styles.emptyText}>
          No timeline event is currently active. Events appear here while they are running and
          disappear when they expire.
        </div>
      </div>
    );
  }

  return (
    <div className={styles.list}>
      {events.map((event) => (
        <EventItem key={event.id} event={event} lookups={lookups} />
      ))}
    </div>
  );
};

interface EventItemProps {
  event: Agora.TimelineEventBrief;
  lookups: Lookups;
}

const EventItem = ({ event, lookups }: EventItemProps) => {
  const shownDistricts = event.districtIds.slice(0, MAX_DISTRICT_CHIPS);
  const hiddenDistricts = event.districtIds.length - shownDistricts.length;
  const shownTags = event.tags.slice(0, MAX_TAG_CHIPS);

  return (
    <div className={styles.eventItem}>
      <div className={styles.metaRow}>
        {/* firedDate is when it landed on this city; date is the catalog date it belongs to. */}
        <span className={styles.metaDate}>{formatSimDate(event.firedDate || event.date)}</span>
        <span className={styles.metaKind}>{humanizeEnum(event.origin)}</span>
        <span className={styles.chip}>{event.region}</span>
        {event.durationMonths > 0 ? (
          <span className={styles.chip}>{String(event.durationMonths)} mo</span>
        ) : null}
        {event.expiresDate ? (
          <span className={styles.chip}>Until {formatSimDate(event.expiresDate)}</span>
        ) : null}
        {event.severity > 0 ? (
          <span className={styles.severity}>
            {SEVERITY_STEPS.map((step) => (
              <span
                key={step}
                className={cx(styles.sevDot, step <= event.severity && styles.sevDotOn)}
              />
            ))}
          </span>
        ) : null}
      </div>

      <div className={styles.eventTitle}>{event.title || event.archetypeId || event.id}</div>

      {event.localAngle ? <div className={styles.localAngle}>{event.localAngle}</div> : null}

      {event.districtIds.length > 0 || shownTags.length > 0 ? (
        <div className={styles.tagRow}>
          {shownDistricts.map((districtId) => (
            <span key={districtId} className={styles.chip}>
              {lookups.districtLabel(districtId)}
            </span>
          ))}
          {hiddenDistricts > 0 ? (
            <span className={styles.chip}>+{String(hiddenDistricts)} more</span>
          ) : null}
          {shownTags.map((tag) => (
            <span key={tag} className={styles.chipDim}>
              {tag}
            </span>
          ))}
        </div>
      ) : (
        <div className={styles.tagRow}>
          <span className={styles.chipDim}>Citywide</span>
        </div>
      )}
    </div>
  );
};
