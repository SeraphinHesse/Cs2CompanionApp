import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { useMapValue, useValue } from "cs2/api";
import { Button, Portal, Scrollable } from "cs2/ui";

import { ArticleModalBoundary } from "./ArticleModalBoundary";
import {
  ackAlert, alerts$, article$, EMPTY_NEWS_ALERT, EMPTY_NEWS_ARTICLE, enabled$, isFirstRun$,
  isAccepted, roster$, settings$, writeMessage,
} from "./bindings";
import { useSimulationHeldPaused } from "./pause";
import { cx, formatSimDate, splitParagraphs, SEVERITY_STEPS } from "./format";
import { NEUTRAL_COLOR } from "./lookup";
import styles from "./ArticleModal.module.scss";

/**
 * The interruption: one alert, as a front page, over whatever the player was doing.
 *
 * Shell chrome rather than a panel, and mounted from its own `moduleRegistry.append`, because it has
 * to appear with the dashboard closed. Rendered through `Portal` so it overlays the whole HUD instead
 * of sitting in a hook point's corner, with the scrim and the centring supplied here — `Portal` takes
 * children and nothing else.
 *
 * Not a `ConfirmationDialog`: that component models confirm-or-cancel and this is a sheet with an
 * acknowledgement. `FirstRunDialog` rejected it for the same reason.
 *
 * **One card at a time, by construction.** This renders `alerts[0]` or nothing. There is no code path
 * that mounts two, so "queue, never stack" is a property of the component rather than a rule someone
 * has to keep. The queue arrives oldest-first and is never re-sorted here (contract rule 7).
 *
 * Unlike the region prompt, this one must ALWAYS be dismissable. While a major alert holds the pause
 * barrier the game forces the speed to zero every frame, so a card with no working way out is a game
 * the player cannot un-pause by any means at all — which is why the boundary's fallback acks too.
 */

/**
 * The masthead's section line, per kind. No `NewsAlertKindName` member reaches the player raw: these
 * are the same words a newsroom would put on the page. The desk line exists only here — the card is
 * the only place that prints a masthead.
 *
 * `KIND_LABEL` below used to be one of two maps of that name — `NewsFeed` held the other, keyed by
 * `NewsKindName`. That file is gone with the News panel in wave 7, so this is now the only one, and
 * there is nothing left for it to drift against. It stays keyed by `NewsAlertKindName` and keeps its
 * `UNKNOWN_KIND` fallback, so a kind this build was never taught costs a word on a badge rather than
 * printing a raw enum member. Contrast `NEUTRAL_COLOR`, which is imported from `lookup.ts` precisely
 * because a second copy would show up as two different greys for one story — a rule that outlived the
 * panel and is why `lookup.ts` moved into the shell rather than being deleted with it.
 */
const DESK_LABEL: { [kind: string]: string } = {
  Article: "City Desk",
  Event: "City Desk",
  Election: "Election Desk",
  Coalition: "Politics Desk",
  Party: "Politics Desk",
};

/** The kind, as a reader would name it. `Coalition` is "Government" — nobody says coalition. */
const KIND_LABEL: { [kind: string]: string } = {
  Article: "Report",
  Event: "Event",
  Election: "Election",
  Coalition: "Government",
  Party: "Party",
};

/** Used when the wire carries a kind this build was never taught. Never the raw value. */
const UNKNOWN_KIND = "Bulletin";

/** Shown while an ack is in flight. Both buttons go quiet, not just the one pressed. */
const WORKING_LABEL = "Filing…";

interface ArticleBodyProps {
  id: string;
  /** The alert's own one-liner, shown when the fetch comes back with no prose. */
  summary: string;
}

/**
 * The prose body, fetched for exactly this id.
 *
 * A separate component for one reason: `useMapValue` may only be called when the alert says there is
 * a body to fetch, and a conditional hook is illegal. The condition therefore lives in the *mount*,
 * not in the call — the parent renders this only when `hasArticle` is true, keyed by the alert id so
 * a changing alert remounts the subscription rather than re-keying a live one. Do not hoist the hook
 * into `ArticleModalInner`.
 *
 * There is no "still fetching" state to render, and nothing here may pretend otherwise: a map binding
 * resolves inside its own subscribe trigger, so what comes back is C#'s final answer for this id.
 * Every field is FLAVOR — render it, parse none of it.
 */
const ArticleBody = ({ id, summary }: ArticleBodyProps): JSX.Element => {
  const fetched = useMapValue(article$, id) as Agora.NewsArticle | undefined;
  const article: Agora.NewsArticle = fetched || EMPTY_NEWS_ARTICLE;

  const paragraphs = useMemo(() => splitParagraphs(article.body), [article.body]);

  if (paragraphs.length === 0) {
    // The piece was retired from prose between the alert and the open — article ids are
    // per-generation. The summary stands in so the sheet is never blank.
    return <div className={styles.para}>{summary}</div>;
  }

  return (
    <>
      {paragraphs.map((paragraph, index) => (
        <div key={index} className={styles.para}>
          {paragraph}
        </div>
      ))}
    </>
  );
};

const ArticleModalInner = (): JSX.Element | null => {
  const enabled = useValue(enabled$);
  const isFirstRun = useValue(isFirstRun$);
  const alerts = useValue(alerts$);
  const settings = useValue(settings$);
  const roster = useValue(roster$);

  const [busy, setBusy] = useState(false);
  const [message, setMessage] = useState("");

  // The card unmounts the instant the engine drops the alert, and the ack is awaited — so the
  // component can easily be gone before the `then` runs. Setting state on an unmounted tree is a
  // warning at best and a leak at worst.
  const mounted = useRef(true);
  useEffect(function () {
    return function () {
      mounted.current = false;
    };
  }, []);

  // Always the head of the queue. `EMPTY_NEWS_ALERT` is the floor for the frame between a dismiss
  // and the republish that removes it, so a render racing an ack cannot read a field off
  // `undefined`; its `id` is "" and its `major` is false, which is what closes the card and releases
  // the barrier rather than holding both on a phantom.
  const current: Agora.NewsAlert = alerts.length > 0 ? alerts[0] || EMPTY_NEWS_ALERT
    : EMPTY_NEWS_ALERT;

  // The first-run interlock. The region prompt has no dismiss by design, so a card over it would be
  // unanswerable; gating on `!isFirstRun` also keeps the very first thing a new player sees to one
  // dialog rather than a frozen game behind two. It self-clears — the instant the region is chosen
  // any alert already in the ring becomes visible, because the ring held it.
  const open = enabled && !isFirstRun && current.id !== "";

  // Two orthogonal questions, and this is the second one. Whether the alert qualifies at all was
  // decided in C# at emit time; whether it holds the clock is decided here, per alert. (Until v10
  // the emit-time gate consulted `showAllReports`; that setting governed the article alert only, and
  // both retired with the feed.) `major` is the engine's verdict and is never recomputed from
  // `severity` — the
  // threshold lives in EngineTuning and a copy of it here would be a second definition of "major".
  // Advancing from a major alert to an ordinary one releases the barrier mid-queue, which is right.
  useSimulationHeldPaused(open && current.major && settings.pauseOnMajorNews);

  // A new card is a clean slate: a refusal left over from the previous one would be read as this
  // one's, and a stuck `busy` would leave both buttons dead.
  useEffect(
    function () {
      setBusy(false);
      setMessage("");
    },
    [current.id],
  );

  const party = useMemo(
    function () {
      if (!current.partyId) {
        return undefined;
      }
      for (let i = 0; i < roster.length; i++) {
        if (roster[i].id === current.partyId) {
          return roster[i];
        }
      }
      return undefined;
    },
    [roster, current.partyId],
  );

  const send = useCallback(function (id: string) {
    setBusy(true);
    setMessage("");

    void ackAlert(id).then(function (result) {
      if (!mounted.current) {
        return;
      }
      // Acceptance is asked of `isAccepted`, never inferred from an empty message. On acceptance
      // there is nothing to do: the engine drops the alert and republishes, and this card either
      // becomes the next one or goes away. The card must NOT close itself on a refusal — that is
      // the whole reason the ack is a call and not a trigger.
      if (!isAccepted(result)) {
        setMessage(writeMessage(result));
      }
      setBusy(false);
    });
  }, []);

  const dismiss = useCallback(
    function () {
      send(current.id);
    },
    [send, current.id],
  );

  const dismissAll = useCallback(
    function () {
      send("*");
    },
    [send],
  );

  // Every hook is above this line — neither the master toggle, nor the first-run flag, nor an empty
  // queue may change the hook order.
  if (!open) {
    return null;
  }

  const desk = DESK_LABEL[current.kind] || UNKNOWN_KIND;
  const kindLabel = KIND_LABEL[current.kind] || UNKNOWN_KIND;
  const nameplate = current.outletName || desk;
  const partyLabel = party ? party.shortName || party.name : "";
  const partyColor = party && party.colorHex ? party.colorHex : NEUTRAL_COLOR;
  const waiting = alerts.length;

  return (
    <Portal>
      <div className={styles.scrim}>
        <div className={styles.card}>
          {/* The spot rule. A newspaper prints one colour on the front page; here it is the party
              the story is about, resolved through the roster and never rendered as an id. */}
          <div className={styles.spot} style={{ backgroundColor: partyColor }} />

          <div className={styles.nameplate}>{nameplate}</div>

          <div className={styles.dateline}>
            <span className={styles.datelineDate}>{formatSimDate(current.date)}</span>
            <span className={styles.datelineSep}>&#183;</span>
            <span className={styles.datelineKind}>{kindLabel}</span>
            {partyLabel ? (
              <>
                <span className={styles.datelineSep}>&#183;</span>
                <span className={styles.datelineParty}>{partyLabel}</span>
              </>
            ) : null}
            {current.severity > 0 ? (
              <span className={styles.severity}>
                {SEVERITY_STEPS.map((step) => (
                  <span
                    key={step}
                    className={cx(styles.sevDot, step <= current.severity && styles.sevDotOn)}
                  />
                ))}
              </span>
            ) : null}
          </div>

          <div className={styles.headline}>{current.headline}</div>

          {/* Single column. Gameface's multi-column support is not something to assume, and at this
              width one column reads fine. */}
          <Scrollable vertical={true} trackVisibility="scrollable" className={styles.scroll}>
            <div className={styles.body}>
              {current.hasArticle ? (
                <ArticleBody key={current.id} id={current.id} summary={current.summary} />
              ) : (
                <div className={styles.para}>{current.summary}</div>
              )}
            </div>
          </Scrollable>

          {/* The engine's verdict, in English. Never a code, never an exception message. */}
          {message ? <div className={styles.refusal}>{message}</div> : null}

          <div className={styles.actions}>
            {waiting > 1 ? (
              <span className={styles.counter}>
                1 of {String(waiting)} waiting
              </span>
            ) : null}
            {busy ? <span className={styles.working}>{WORKING_LABEL}</span> : null}
            <div className={styles.spacer} />
            {waiting > 1 ? (
              <Button
                variant="flat"
                className={styles.secondaryAction}
                disabled={busy}
                onSelect={dismissAll}
              >
                Dismiss all
              </Button>
            ) : null}
            <Button
              variant="flat"
              className={styles.action}
              disabled={busy}
              onSelect={dismiss}
            >
              Dismiss
            </Button>
          </div>
        </div>
      </div>
    </Portal>
  );
};

export const ArticleModal = (): JSX.Element => (
  <ArticleModalBoundary>
    <ArticleModalInner />
  </ArticleModalBoundary>
);
