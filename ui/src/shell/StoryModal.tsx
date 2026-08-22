import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { bindMap, useMapValue, useValue } from "cs2/api";
import { Button, Portal, Scrollable } from "cs2/ui";

import {
  ackStoryAlert, EMPTY_STORY_ALERT, EMPTY_STORY_ARTICLE, enabled$, isAccepted, isFirstRun$,
  settings$, stories$, storyAlerts$, writeMessage,
} from "./bindings";
import { useSimulationHeldPaused } from "./pause";
import { StoryModalBoundary } from "./StoryModalBoundary";
import { cx, formatSimDate, splitParagraphs, SEVERITY_STEPS } from "./format";
import styles from "./StoryModal.module.scss";

/**
 * The interruption: one drafted story, with everything it asks of the player, over whatever they
 * were doing.
 *
 * Shell chrome rather than a panel, and mounted from its own `moduleRegistry.append`, because it has
 * to appear with the dashboard closed. Rendered through `Portal` so it overlays the whole HUD instead
 * of sitting in a hook point's corner, with the scrim and the centring supplied here — `Portal` takes
 * children and nothing else. A sibling of `ArticleModal`, not a variant of it: the two queues are
 * separate by contract (§4.7) and either card may be up while the other is not.
 *
 * Not a `ConfirmationDialog`: that component models confirm-or-cancel and this is a sheet with an
 * acknowledgement. `ArticleModal` and `FirstRunDialog` both rejected it for the same reason.
 *
 * **One card at a time, by construction.** This renders `storyAlerts[0]` or nothing. There is no code
 * path that mounts two, so "queue, never stack" is a property of the component rather than a rule
 * someone has to keep. The queue arrives oldest-first and is never re-sorted here (contract rule 7).
 *
 * **One card per story, never one per event.** The alert is raised per story and carries a
 * `slotCount` rather than an event id; all of the story's slots render inside this one card, below.
 * Two stories drafting in a cycle are two interruptions, not six — six would be six serialised forced
 * pauses on the first frame of the month, each needing its own dismissal round trip.
 *
 * **This card is a notification, not a form.** Dismissing it answers nothing: the story stays live,
 * no response is recorded, and the player tackles it from the Stories tab. The four response options
 * live there, and the interruption budget above is the reason the two surfaces are split.
 *
 * Like the news card, this one must ALWAYS be dismissable. While a major card holds the pause barrier
 * the game forces the speed to zero every frame, so a card with no working way out is a game the
 * player cannot un-pause by any means at all — which is why the boundary's fallback acks too.
 */

/**
 * The story body, fetched per story id. Declared here rather than in `bindings.ts` because this is
 * the only surface in the shell that reads it; the Stories panel fetches the same map for its own
 * mount. A `bindMap` is a subscription factory, not a subscription, so two declarations of the same
 * map are not two subscriptions to the same key the way two `bindValue`s would be.
 */
const storyArticle$ = bindMap<string, Agora.StoryArticle>("agora.stories", "article");

/**
 * The tier, as a reader would name it. A lookup with a fallback rather than the raw member, on the
 * pattern of `ArticleModal`'s `KIND_LABEL`: an untaught tier costs a word on a badge, never a raw
 * enum member on screen.
 */
const TIER_LABEL: { [tier: string]: string } = {
  Mandatory: "Unavoidable",
  Major: "Major",
  Minor: "Minor",
};

/** Used when the wire carries a tier this build was never taught. Never the raw value. */
const UNKNOWN_TIER = "Event";

/**
 * Stands in for a slot whose event the catalog no longer explains. `slot.name` is `""` in that case
 * and **the event id must never be rendered where a name belongs** — a raw id on screen is a defect
 * this repo has fixed twice.
 */
const UNKNOWN_SLOT_NAME = "An event this build no longer explains";

/** Shown while a dismissal is in flight. The buttons go quiet together, not one at a time. */
const WORKING_LABEL = "Closing…";

/** Where the player actually answers this. The card deliberately offers no response of its own. */
const HANDOFF_NOTE =
  "Nothing is decided here. Open the Stories tab of the Agora dashboard to choose how to tackle "
  + "each of these before the story resolves.";

/** The two prose voices, labelled only when both are present — see `StoryBody`. */
const POOL_VOICE_LABEL = "The story";
const CLI_VOICE_LABEL = "A second account";

interface StoryBodyProps {
  id: string;
  /** The alert's own one-liner, shown when the fetch comes back with no prose at all. */
  summary: string;
}

/**
 * The prose body, fetched for exactly this story id — the alert id **is** the story id and **is** the
 * map key, bare and unprefixed.
 *
 * A separate component for the reason `ArticleBody` is one: `useMapValue` may not be called
 * conditionally, so the condition lives in the *mount* and not in the call. The parent mounts this
 * only when a card is open, keyed by the alert id so a changing alert remounts the subscription
 * rather than re-keying a live one. Do not hoist the hook into `StoryModalInner`.
 *
 * There is no "still fetching" state to render, and nothing here may pretend otherwise: a map binding
 * resolves inside its own subscribe trigger, so what comes back is C#'s final answer for this id.
 *
 * **Both voices, and neither replaces the other** (contract §4.7). The pool always answers and is
 * always shown; the model's account appears beside it when it exists and never instead of it, because
 * showing only the newest would rewrite text the player had already read. The `*Resolution*` fields
 * are not read here — a card is raised when a story drafts, and those are empty until it closes.
 * Every field is FLAVOR: render it, parse none of it.
 */
const StoryBody = ({ id, summary }: StoryBodyProps): JSX.Element => {
  const fetched = useMapValue(storyArticle$, id) as Agora.StoryArticle | undefined;
  const article: Agora.StoryArticle = fetched || EMPTY_STORY_ARTICLE;

  const poolParagraphs = useMemo(() => splitParagraphs(article.poolArticle), [article.poolArticle]);
  const cliParagraphs = useMemo(() => splitParagraphs(article.cliArticle), [article.cliArticle]);

  if (poolParagraphs.length === 0 && cliParagraphs.length === 0) {
    // No prose reached the map for this id. A drafted story always has the pool's, so this is the
    // shape of a story the map has never heard of rather than a state the player should ever see —
    // the summary stands in so the sheet is never blank.
    return <div className={styles.para}>{summary}</div>;
  }

  // Labels only earn their space when there are two voices to tell apart. One voice reads better as
  // plain prose than as a labelled section.
  const labelled = poolParagraphs.length > 0 && cliParagraphs.length > 0;

  return (
    <>
      {poolParagraphs.length > 0 ? (
        <div className={styles.voice}>
          {labelled ? <div className={styles.voiceLabel}>{POOL_VOICE_LABEL}</div> : null}
          {poolParagraphs.map((paragraph, index) => (
            <div key={index} className={styles.para}>
              {paragraph}
            </div>
          ))}
        </div>
      ) : null}

      {cliParagraphs.length > 0 ? (
        <div className={styles.voice}>
          {labelled ? <div className={styles.voiceLabel}>{CLI_VOICE_LABEL}</div> : null}
          {article.cliHeadline ? (
            <div className={styles.voiceHeadline}>{article.cliHeadline}</div>
          ) : null}
          {cliParagraphs.map((paragraph, index) => (
            <div key={index} className={styles.para}>
              {paragraph}
            </div>
          ))}
        </div>
      ) : null}
    </>
  );
};

interface SlotRowProps {
  slot: Agora.StorySlot;
}

/**
 * One event inside the story. Read-only by design — the four response controls are the Stories
 * panel's surface, and putting them here would spend the interruption budget this card exists to
 * protect.
 *
 * `tier` is rendered, never derived: the UI does not compare a severity to a threshold of its own
 * (contract §4.7, in bold), because a second definition of "major" drifts from the engine's on the
 * next tuning pass. `severity` is the display meter beside it and nothing more.
 */
const SlotRow = ({ slot }: SlotRowProps): JSX.Element => {
  const named = slot.name !== "";
  const tierLabel = TIER_LABEL[slot.tier] || UNKNOWN_TIER;

  return (
    <div className={styles.slot}>
      <div className={styles.slotHead}>
        <span className={cx(styles.slotName, !named && styles.slotNameUnknown)}>
          {named ? slot.name : UNKNOWN_SLOT_NAME}
        </span>
        <span className={styles.slotSpacer} />
        {slot.severity > 0 ? (
          <span className={styles.sev}>
            {SEVERITY_STEPS.map((step) => (
              <span
                key={step}
                className={cx(styles.sevDot, step <= slot.severity && styles.sevDotOn)}
              />
            ))}
          </span>
        ) : null}
        <span className={cx(styles.tier, slot.tier === "Mandatory" && styles.tierMandatory)}>
          {tierLabel}
        </span>
      </div>
      {slot.description ? <div className={styles.slotDesc}>{slot.description}</div> : null}
    </div>
  );
};

const StoryModalInner = (): JSX.Element | null => {
  const enabled = useValue(enabled$);
  const isFirstRun = useValue(isFirstRun$);
  const alerts = useValue(storyAlerts$);
  const stories = useValue(stories$);
  const settings = useValue(settings$);

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

  // Always the head of the queue, never a re-sort of it. `EMPTY_STORY_ALERT` is the floor for the
  // frame between a dismiss and the republish that removes it, so a render racing an ack cannot read
  // a field off `undefined`; its `id` is "" and its `major` is false, which is what closes the card
  // and releases the barrier rather than holding both on a phantom.
  const current: Agora.StoryAlert = alerts.length > 0 ? alerts[0] || EMPTY_STORY_ALERT
    : EMPTY_STORY_ALERT;

  // The first-run interlock, the same three conditions `ArticleModal` honours. The region prompt has
  // no dismiss by design, so a card over it would be unanswerable; it self-clears, because the ring
  // holds every alert raised while the prompt was up.
  const open = enabled && !isFirstRun && current.id !== "";

  // The slots, from the live story this alert names. The alert carries a `slotCount` and no event id
  // precisely because all of them belong to this one card. The engine's order — major first, then
  // minors ascending by event id — is the published order and is not re-sorted here.
  const story = useMemo(
    function () {
      if (current.id === "") {
        return undefined;
      }
      for (let i = 0; i < stories.length; i++) {
        if (stories[i].id === current.id) {
          return stories[i];
        }
      }
      return undefined;
    },
    [stories, current.id],
  );

  // Whether this card is GRAVE ENOUGH to hold the clock is the ENGINE'S verdict, decided once when
  // the alert was raised from the story's own major slot against the tuned threshold. It is never
  // recomputed from a severity here — a copy of the threshold in the UI would be a second definition
  // of "major", and it would drift into disagreeing with the price the engine charges. Advancing from
  // a major card to an ordinary one releases the barrier mid-queue, which is right.
  //
  // Whether the clock is ACTUALLY held is the separate question `pauseOnMajorStory` answers, and it
  // is deliberately not `pauseOnMajorNews`: that control's hint enumerates elections, governments,
  // party lifecycle and serious events — all news — so neither of its positions is an answer about
  // stories, and reading it here would enforce a choice the player made about something else. Wave 6
  // shipped with the hold unconditional and no way to stop it short of turning stories off entirely;
  // this is that gap closed. The card still APPEARS either way and is still always dismissable —
  // this decides only whether the sim stops while it is up.
  const holdsClock = open && current.major && settings.pauseOnMajorStory;
  useSimulationHeldPaused(holdsClock);

  // A new card is a clean slate: a refusal left over from the previous one would be read as this
  // one's, and a stuck `busy` would leave both buttons dead.
  useEffect(
    function () {
      setBusy(false);
      setMessage("");
    },
    [current.id],
  );

  const send = useCallback(function (id: string) {
    setBusy(true);
    setMessage("");

    void ackStoryAlert(id).then(function (result) {
      if (!mounted.current) {
        return;
      }
      // Acceptance is asked of `isAccepted`, never inferred from an empty message. On acceptance
      // there is nothing to do: the engine drops the alert and republishes, and this card either
      // becomes the next one or goes away. The card must NOT close itself on a refusal, nor on a call
      // that never answered — that is the whole reason the ack is a call with a deadline rather than
      // a trigger. Acking an id the queue no longer holds answers `""`, so a double-click closes the
      // card rather than reporting an error the player did not cause.
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

  const slots = story ? story.slots : [];
  const waiting = alerts.length;

  return (
    <Portal>
      <div className={styles.scrim}>
        <div className={styles.card}>
          {/* One rule across the top, warmer when the card is holding the clock. The only visual
              difference a major card gets — the badge beside the date is what says it in words. */}
          <div className={cx(styles.rule, current.major && styles.ruleMajor)} />

          <div className={styles.kicker}>
            <span className={styles.kickerLabel}>Story</span>
            <span className={styles.kickerSep}>&#183;</span>
            <span className={styles.kickerDate}>{formatSimDate(current.date)}</span>
            {/* The badge tracks whether the clock is ACTUALLY held, not merely whether the engine
                called the story major — a card that says "Clock held" over a running sim is worse
                than no badge at all. */}
            {holdsClock ? <span className={styles.held}>Clock held</span> : null}
          </div>

          <div className={styles.headline}>{current.headline}</div>
          {current.summary ? <div className={styles.summary}>{current.summary}</div> : null}

          <Scrollable vertical={true} trackVisibility="scrollable" className={styles.scroll}>
            <div className={styles.body}>
              <StoryBody key={current.id} id={current.id} summary={current.summary} />

              {/* All of the story's events, inside the one card. */}
              <div className={styles.slots}>
                {slots.length > 0 ? (
                  slots.map((slot) => <SlotRow key={slot.eventId} slot={slot} />)
                ) : (
                  // The alert names a story the live list does not carry — it resolved, or the
                  // republish has not landed yet. The count the alert itself carries stands in, so
                  // the card still says how much is at stake.
                  <div className={styles.slotFallback}>
                    {current.slotCount === 1 ? "1 event in this story"
                      : String(current.slotCount) + " events in this story"}
                  </div>
                )}
              </div>

              <div className={styles.note}>{HANDOFF_NOTE}</div>
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

export const StoryModal = (): JSX.Element => (
  <StoryModalBoundary>
    <StoryModalInner />
  </StoryModalBoundary>
);
