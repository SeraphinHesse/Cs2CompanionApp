import styles from "./NewsPanel.module.scss";
import { Lookups } from "./lookup";
import { cx, formatSimDate, SEVERITY_STEPS } from "./format";

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

/**
 * `origin` in plain English.
 *
 * It was going through `humanizeEnum`, which inserts a space before an inner capital and therefore
 * does nothing at all to any of the three values this enum has. The player was reading the raw
 * member name: "Catalog", "Procedural", "Political". Two of those are engine vocabulary — "Catalog"
 * means the curated real-world timeline in `data/timeline_*.json`, and "Procedural" means generated
 * from a seeded archetype once that timeline runs out. Neither is guessable from the outside, so
 * they are named for what they are instead of for where they came from.
 *
 * An unknown value falls through to the raw string rather than to a placeholder: a member added on
 * the C# side should show up as itself, not silently render as something else.
 */
const ORIGIN_LABEL: { [origin: string]: string } = {
  Catalog: "World news",
  Procedural: "The wider world",
  Political: "City politics",
};

function originLabel(origin: string): string {
  return ORIGIN_LABEL[origin] || origin;
}

/**
 * Shown when an event arrives with no title.
 *
 * The previous fallback chain put `archetypeId` and then `id` into the heading, and neither is
 * player-legible ("housing-squeeze", "proc-2031-04-housing-squeeze-1"). `archetypeId` is not mapped
 * to English the way `origin` is: it is non-empty only for procedural events, and a procedural
 * event's `title` is copied from the *same* archetype, so a map here would only ever restore text
 * the payload was already meant to carry — and the archetype pool is an injectable parameter on the
 * C# side, so the map would silently fall behind a pool the engine grew.
 *
 * Deliberately says only that the heading is missing. Date, origin, region, duration, severity,
 * districts and tags are all still on the row, so the event stays identifiable without this line
 * repeating any of them.
 */
const UNTITLED_EVENT = "Untitled event";

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
        <span className={styles.metaKind}>{originLabel(event.origin)}</span>
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

      <div className={styles.eventTitle}>{event.title || UNTITLED_EVENT}</div>

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
