import { useCallback, useMemo, useState } from "react";
import { useValue } from "cs2/api";
import { Button, Scrollable } from "cs2/ui";

import styles from "./NewsPanel.module.scss";
import {
  AGORA_EVENTS_MAX,
  AGORA_NEWS_FEED_MAX,
  enabled$,
  events$,
  feed$,
  flavorStatus$,
  mandates$,
  ready$,
  wakeFlavor,
} from "./bindings";
import { useLookups } from "./lookup";
import { ArticleReader } from "./ArticleReader";
import { EventList } from "./EventList";
import { MandateTracker } from "./MandateTracker";
import { NewsFeed } from "./NewsFeed";
import { cx, formatSimDate } from "./format";

/**
 * Panel 25 — News.
 *
 * Left column: the reverse-chronological feed of LLM-authored headlines, with a reading view for
 * the full body, and a second tab for the timeline events those headlines react to.
 * Right column: the mandate tracker — every mandate, its progress against the metric it is judged
 * on, and its deadline.
 *
 * Layout is flexbox throughout. Gameface has no CSS grid: `display: grid` renders as a broken
 * pile in-game and produces no error anywhere in the build, so there is none here.
 *
 * The feed carries prose of unpredictable length. The panel is a fixed width, every text column
 * sets `min-width: 0` so it can actually shrink, long tokens break rather than widen the panel,
 * and headline/summary/mandate text are clamped to a fixed number of lines by max-height.
 */

type FeedTab = "news" | "world";

/**
 * `flavorStatus.lastError` is an ENGINE-authored short code — never LLM output, never a raw
 * exception message — so it is safe to switch on. An unrecognised code still renders.
 */
const ERROR_TEXT: { [code: string]: string } = {
  CliMissing: "Claude CLI not found",
  Timeout: "Last attempt timed out",
  BadJson: "Last reply was malformed",
  Disabled: "Flavor generation is off",
  Unknown: "Last attempt failed",
};

export const NewsPanel = () => {
  const enabled = useValue(enabled$);
  const ready = useValue(ready$);
  const feed = useValue(feed$);
  const events = useValue(events$);
  const mandates = useValue(mandates$);
  const flavor = useValue(flavorStatus$);
  const lookups = useLookups();

  const [tab, setTab] = useState<FeedTab>("news");
  const [openId, setOpenId] = useState<string>("");
  const [collapsed, setCollapsed] = useState<boolean>(false);

  // Resolve the open item against the live feed rather than holding a copy: an item can age out
  // of the 40-item cap while it is open, and when it does the reader closes itself.
  const openItem = useMemo<Agora.NewsHeadline | undefined>(() => {
    if (!openId) {
      return undefined;
    }
    for (let i = 0; i < feed.length; i++) {
      if (feed[i].id === openId) {
        return feed[i];
      }
    }
    return undefined;
  }, [feed, openId]);

  const openArticle = useCallback((item: Agora.NewsHeadline) => {
    if (item.hasArticle) {
      setOpenId(item.id);
    }
  }, []);

  const closeArticle = useCallback(() => setOpenId(""), []);

  const showNews = useCallback(() => setTab("news"), []);
  const showWorld = useCallback(() => setTab("world"), []);
  const toggleCollapsed = useCallback(() => setCollapsed((value) => !value), []);

  // The wake REQUESTS; the engine decides. The control is disabled while a wake is in flight
  // (contract section 4.5) and before the engine is ready, so the trigger is never fired at a
  // publisher that does not exist yet. A failed wake keeps the last good flavor by design, so
  // nothing here assumes the feed changes as a result.
  const onWake = useCallback(() => {
    wakeFlavor();
  }, []);

  // Master toggle: off means the player sees no trace of the mod, not a disabled shell.
  if (!enabled) {
    return null;
  }

  const wakeDisabled = flavor.pendingWake || !ready;
  const errorText = flavor.lastError ? ERROR_TEXT[flavor.lastError] || "Flavor unavailable" : "";
  const atFeedCap =
    tab === "news" ? feed.length >= AGORA_NEWS_FEED_MAX : events.length >= AGORA_EVENTS_MAX;

  return (
    <div className={cx(styles.panel, collapsed && styles.panelCollapsed)}>
      <div className={styles.header}>
        <div className={styles.headerMain}>
          <div className={styles.title}>AGORA</div>
          <div className={styles.subtitle}>News &amp; Mandates</div>
        </div>

        <div className={styles.headerStatus}>
          {ready ? (
            <>
              <span className={flavor.providerAvailable ? styles.chipGood : styles.chipWarn}>
                {flavor.providerAvailable ? "Writer online" : "Writer offline"}
              </span>
              {flavor.isStale ? <span className={styles.chipWarn}>Prose is stale</span> : null}
              {errorText ? <span className={styles.chipWarn}>{errorText}</span> : null}
              {flavor.pendingWake ? <span className={styles.chipDim}>Waking…</span> : null}
              <span className={styles.chipDim}>
                {flavor.lastFlavorDate
                  ? "Last filed " + formatSimDate(flavor.lastFlavorDate)
                  : "Nothing filed yet"}
              </span>
            </>
          ) : (
            <span className={styles.chipDim}>Waiting for the first political tick</span>
          )}
        </div>

        <div className={styles.headerActions}>
          <Button
            variant="flat"
            className={styles.smallButton}
            disabled={wakeDisabled}
            onSelect={onWake}
          >
            Wake writer
          </Button>
          <Button variant="flat" className={styles.smallButton} onSelect={toggleCollapsed}>
            {collapsed ? "Show" : "Hide"}
          </Button>
        </div>
      </div>

      {collapsed ? (
        <div className={styles.collapsedBar}>
          <span className={styles.chipDim}>{String(feed.length)} stories</span>
          <span className={styles.chipDim}>{String(events.length)} active events</span>
          <span className={styles.chipDim}>{String(mandates.length)} mandates</span>
        </div>
      ) : (
        <div className={styles.body}>
          {/* ---- left: feed / world ---- */}
          <div className={cx(styles.column, styles.columnFeed)}>
            {openItem ? (
              <ArticleReader headline={openItem} lookups={lookups} onClose={closeArticle} />
            ) : (
              <>
                <div className={styles.columnHeader}>
                  <div className={styles.tabs}>
                    <Button
                      variant="flat"
                      className={cx(styles.tab, tab === "news" && styles.tabSelected)}
                      selected={tab === "news"}
                      onSelect={showNews}
                    >
                      Feed
                    </Button>
                    <Button
                      variant="flat"
                      className={cx(styles.tab, tab === "world" && styles.tabSelected)}
                      selected={tab === "world"}
                      onSelect={showWorld}
                    >
                      World
                    </Button>
                  </div>
                  <span className={styles.spacer} />
                  {/*
                    The caps are part of the contract, not an accident, and they are surfaced
                    because they are observable: at the cap the oldest story silently drops off
                    the end, which is also why the reading view resolves its item against the
                    live feed rather than holding a copy.
                  */}
                  {atFeedCap ? <span className={styles.chipDim}>capped</span> : null}
                  <span className={styles.columnCount}>
                    {tab === "news"
                      ? String(feed.length) + " stories"
                      : String(events.length) + " active"}
                  </span>
                </div>

                <Scrollable
                  vertical={true}
                  trackVisibility="scrollable"
                  className={styles.scroll}
                >
                  {!ready ? (
                    <LoadingBlock />
                  ) : tab === "news" ? (
                    <NewsFeed items={feed} lookups={lookups} onOpen={openArticle} />
                  ) : (
                    <EventList events={events} lookups={lookups} />
                  )}
                </Scrollable>
              </>
            )}
          </div>

          <div className={styles.divider} />

          {/* ---- right: mandate tracker ---- */}
          <div className={cx(styles.column, styles.columnMandates)}>
            <div className={styles.columnHeader}>
              <span className={styles.columnTitle}>Mandates</span>
              <span className={styles.spacer} />
              <span className={styles.columnCount}>{String(mandates.length)} tracked</span>
            </div>

            <Scrollable vertical={true} trackVisibility="scrollable" className={styles.scroll}>
              {ready ? (
                <MandateTracker mandates={mandates} lookups={lookups} />
              ) : (
                <LoadingBlock />
              )}
            </Scrollable>
          </div>
        </div>
      )}
    </div>
  );
};

/**
 * Rendered until `agora.state.ready` goes true. Every binding in the contract is still at its
 * empty value before then, so an empty list would read as "nothing happened" rather than
 * "nothing has been published yet".
 */
const LoadingBlock = () => (
  <div className={styles.empty}>
    <div className={styles.emptyTitle}>Waiting for the engine</div>
    <div className={styles.emptyText}>
      The political state has not been published yet. The feed and the mandate tracker fill in on
      the first political tick.
    </div>
    <div className={styles.skeletonRow} />
    <div className={styles.skeletonRow} />
    <div className={styles.skeletonRowShort} />
  </div>
);
