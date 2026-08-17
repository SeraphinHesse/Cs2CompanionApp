import { useCallback, useEffect, useRef, useState } from "react";
import { Button } from "cs2/ui";

import { WriteOutcome, isAccepted, resolveStoryNow, writeMessage } from "../../shell/bindings";
import { SlotEditor } from "./SlotEditor";
import { StoryBody } from "./StoryBody";
import { cx, formatTimeLeft, formatWindow, unansweredCount } from "./format";
import styles from "./StoriesPanel.module.scss";

/**
 * One live story: its headline, its prose, its events, and the way to close it early.
 *
 * The slots are rendered in the order they arrive — major first, then minors ascending by event id
 * — because that order is a declared total order the engine writes (contract §4.7). Nothing here
 * re-sorts it, and nothing here re-sorts the stories either.
 *
 * The deadline is derived from the story's own published `openedDate` and `resolvesDate` against
 * today's political date, and from nothing else. **No cycle length is computed anywhere in this
 * panel.** A story drafts on one phase and resolves on the next, so the window is one month shorter
 * than `stories.cycleMonths`, and stating that window from the cycle length has been the costliest
 * mistake in this rework — see `format.ts`.
 */

/** A refusal reads as a refusal; an accepted write that carries a warning must not. */
type Tone = "warn" | "bad";

interface Feedback {
  text: string;
  tone: Tone;
}

interface StoryCardProps {
  story: Agora.Story;
  /** Today's political date, from `agora.state.summary`. "" before the first publish. */
  today: string;
}

export const StoryCard = (props: StoryCardProps): JSX.Element => {
  const story = props.story;

  const [busy, setBusy] = useState(false);
  const [feedback, setFeedback] = useState<Feedback | null>(null);

  const mounted = useRef(true);
  useEffect(function () {
    return function () {
      mounted.current = false;
    };
  }, []);

  const send = useCallback(function (run: () => Promise<WriteOutcome>) {
    setBusy(true);
    setFeedback(null);

    void run().then(function (result) {
      if (!mounted.current) {
        return;
      }
      setBusy(false);
      const took = isAccepted(result);
      const text = writeMessage(result);
      setFeedback(text ? { text: text, tone: took ? "warn" : "bad" } : null);
    });
  }, []);

  const slots = story.slots || [];
  const unanswered = unansweredCount(slots);
  const timeLeft = formatTimeLeft(props.today, story.resolvesDate);

  return (
    <div className={styles.story}>
      <div className={styles.storyHead}>
        <div className={styles.storyHeadline}>{story.headline}</div>
        <div className={styles.chips}>
          {story.isMandatory ? <span className={styles.chipTier}>Mandatory</span> : null}
          {/* Silence and refusal are different states, and this is the count that says there is work
              outstanding at all. A story with every slot answered says so instead. */}
          {slots.length > 0 ? (
            <span className={cx(styles.chip, unanswered > 0 && styles.chipUnanswered)}>
              {unanswered > 0
                ? String(unanswered) + " of " + String(slots.length) + " not answered yet"
                : "All " + String(slots.length) + " answered"}
            </span>
          ) : null}
          {timeLeft ? <span className={styles.chipQuiet}>{timeLeft}</span> : null}
          {story.resolveEarlyRequested ? (
            <span className={styles.chipQuiet}>Early resolution asked for</span>
          ) : null}
        </div>
        <div className={styles.storyDates}>{formatWindow(story.openedDate, story.resolvesDate)}</div>
      </div>

      {/* Keyed by story id: the map subscription belongs to this id and remounts rather than
          re-keying when the list underneath changes. */}
      {story.id ? <StoryBody key={story.id} storyId={story.id} /> : null}

      <div className={styles.slots}>
        {slots.length === 0 ? (
          <div className={styles.bodyNote}>This story carries no events.</div>
        ) : null}
        {slots.map(function (slot) {
          return <SlotEditor key={slot.eventId} storyId={story.id} slot={slot} />;
        })}
      </div>

      <div className={styles.storyFoot}>
        <Button
          variant="flat"
          className={styles.act}
          disabled={busy}
          onSelect={function () {
            send(function () {
              return resolveStoryNow(story.id);
            });
          }}
        >
          Resolve now
        </Button>
        <div className={styles.optionNote}>
          Closes the story on this month instead of waiting. Every event is scored exactly as it
          stands, including the ones you have not answered.
        </div>
      </div>

      {feedback ? (
        <div className={feedback.tone === "warn" ? styles.warning : styles.refusal}>
          {feedback.text}
        </div>
      ) : null}
    </div>
  );
};
