import styles from "./NewsPanel.module.scss";
import { Lookups } from "./lookup";
import { cx, formatSimDate, SEVERITY_STEPS } from "./format";

/**
 * The reverse-chronological feed.
 *
 * The list arrives sorted by date DESCENDING then id ascending and capped at 40 items. It is
 * rendered in the order it arrives — re-sorting in the panel (especially by a flavor string like
 * the headline) would reintroduce exactly the ordering nondeterminism the engine removed.
 *
 * `headline`, `summary` and `outletName` are FLAVOR: rendered, never parsed, never sorted by.
 */

interface NewsFeedProps {
  items: Agora.NewsHeadline[];
  lookups: Lookups;
  onOpen: (item: Agora.NewsHeadline) => void;
}

/** Kind drives the badge only, never the layout — an unknown future kind still renders. */
const KIND_LABEL: { [kind: string]: string } = {
  Article: "Report",
  Event: "Event",
  Election: "Election",
  Coalition: "Government",
  Mandate: "Mandate",
  Party: "Party",
};

export const NewsFeed = ({ items, lookups, onOpen }: NewsFeedProps) => {
  if (items.length === 0) {
    return (
      <div className={styles.empty}>
        <div className={styles.emptyTitle}>No news yet</div>
        <div className={styles.emptyText}>
          The press starts writing once the city has a political state and the flavor pass has run
          at least once.
        </div>
      </div>
    );
  }

  return (
    <div className={styles.list}>
      {items.map((item) => (
        <NewsFeedItem key={item.id} item={item} lookups={lookups} onOpen={onOpen} />
      ))}
    </div>
  );
};

interface NewsFeedItemProps {
  item: Agora.NewsHeadline;
  lookups: Lookups;
  onOpen: (item: Agora.NewsHeadline) => void;
}

const NewsFeedItem = ({ item, lookups, onOpen }: NewsFeedItemProps) => {
  const partyLabel = lookups.partyLabel(item.partyId);
  const readable = item.hasArticle;

  return (
    <div
      className={cx(styles.feedItem, readable && styles.feedItemReadable)}
      onClick={readable ? () => onOpen(item) : undefined}
    >
      {/* Party colour rail. Fixed width so a long headline can never squeeze it away. */}
      <div className={styles.rail} style={{ backgroundColor: lookups.partyColor(item.partyId) }} />

      <div className={styles.feedItemBody}>
        <div className={styles.metaRow}>
          <span className={styles.metaDate}>{formatSimDate(item.date)}</span>
          <span className={styles.metaKind}>{KIND_LABEL[item.kind] || item.kind}</span>
          {item.outletName ? (
            <span className={styles.metaOutlet}>{item.outletName}</span>
          ) : null}
          {partyLabel ? <span className={styles.chip}>{partyLabel}</span> : null}
          {item.districtId ? (
            <span className={styles.chip}>{lookups.districtLabel(item.districtId)}</span>
          ) : null}
          {item.severity > 0 ? <SeverityMeter severity={item.severity} /> : null}
        </div>

        {/*
          Headline and summary are of unpredictable length. Both are clamped by an explicit
          max-height (three lines and two lines respectively) rather than -webkit-line-clamp,
          which is not something Gameface can be relied on to implement, and both break long
          unbroken tokens instead of widening the panel.
        */}
        <div className={styles.headline}>{item.headline || "Untitled report"}</div>
        {item.summary ? <div className={styles.summary}>{item.summary}</div> : null}
        {readable ? <div className={styles.readMore}>Read the full piece</div> : null}
      </div>
    </div>
  );
};

const SeverityMeter = ({ severity }: { severity: number }) => (
  <span className={styles.severity}>
    {SEVERITY_STEPS.map((step) => (
      <span key={step} className={cx(styles.sevDot, step <= severity && styles.sevDotOn)} />
    ))}
  </span>
);
