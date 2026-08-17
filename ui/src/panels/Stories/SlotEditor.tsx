import { KeyboardEvent, useCallback, useEffect, useRef, useState } from "react";
import { Button } from "cs2/ui";

import {
  WriteOutcome,
  declareManualOutcome,
  isAccepted,
  setStoryResponse,
  spendPowerOverride,
  writeMessage,
} from "../../shell/bindings";
import { cx, responseLabel, slotOutcomeLabel, slotTitle, tierNote } from "./format";
import styles from "./StoriesPanel.module.scss";

/**
 * One event inside a story: what it is, what the player has said about it so far, and the four ways
 * to tackle it.
 *
 * Four properties shape this component and none of them are cosmetic.
 *
 *  - **The engine's verdict is rendered and none is computed.** `tier` arrives on the wire and is
 *    never derived from `severity`, which ships for display only; `outcome` arrives on the wire and
 *    is never inferred from `response`; `isAccepted` and `writeMessage` come from the shell and are
 *    not reimplemented here (contract §4.6, §4.7, and §6 on why a second copy of the acceptance test
 *    is a defect rather than a style point).
 *  - **A purchase travels on its own channel.** `PowerOverride` goes through `spendPowerOverride`,
 *    which charges for it. `setStoryResponse("PowerOverride")` answers `BadValue` deliberately: a
 *    purchase arriving as an ordinary response would be a success nobody paid for.
 *  - **`canAfford` decides what the button LOOKS like, never whether the call is sent.** Whether a
 *    purchase happens is the engine's answer to the press, read at the moment of the press. A panel
 *    that checks affordability and declines to send is computing a rejection the engine did not
 *    return, which contract rule 5 forbids — and `InsufficientPower` and `PowerDisabled` are two
 *    different refusals that only the engine can tell apart.
 *  - **Silence is a state.** `Unaddressed` is rendered as "not answered yet" and `Ignore` as a
 *    decision. They score the same; they are not the same thing.
 */

/** A refusal reads as a refusal; an accepted write that carries a warning must not. */
type Tone = "warn" | "bad";

interface Feedback {
  text: string;
  tone: Tone;
}

/**
 * Keystrokes must not reach the game.
 *
 * These boxes render inside a game whose hotkeys include space (pause), the digits (speed) and `b`
 * (bulldoze), and there are six of them per story. Propagation is stopped on the way up so the
 * player types into the box instead of pausing the simulation; `preventDefault` is deliberately NOT
 * called, because that would stop the character being typed as well.
 *
 * `PartyEditor.tsx` — the only other text entry in `ui/src` — does not do this. There was no pattern
 * in the repo to copy when it was written, and it has never been rendered in game either; copying it
 * is necessary and not sufficient. Wave 6's manual gate 5 is the walk that settles both.
 */
function swallowKeys(event: KeyboardEvent<HTMLTextAreaElement>): void {
  event.stopPropagation();
}

interface SlotEditorProps {
  storyId: string;
  slot: Agora.StorySlot;
}

export const SlotEditor = (props: SlotEditorProps): JSX.Element => {
  const slot = props.slot;

  const [open, setOpen] = useState(false);
  const [busy, setBusy] = useState(false);
  const [feedback, setFeedback] = useState<Feedback | null>(null);
  const [ignoreText, setIgnoreText] = useState("");
  const [manualText, setManualText] = useState("");

  const mounted = useRef(true);
  useEffect(function () {
    return function () {
      mounted.current = false;
    };
  }, []);

  const toggle = useCallback(
    function () {
      setFeedback(null);
      if (open) {
        setOpen(false);
        return;
      }
      // Drafts are seeded from the published slot at the moment it opens, and never afterwards:
      // re-seeding on a republish would wipe what the player was halfway through typing. `playerText`
      // is where the words the player already sent live, for Ignore and for Manual alike.
      setIgnoreText(slot.playerText);
      setManualText(slot.playerText);
      setOpen(true);
    },
    [open, slot.playerText],
  );

  /**
   * Send one write and render what came back.
   *
   * Acceptance is asked of `isAccepted` and never inferred from an empty message — an accepted write
   * can carry one. `answered: false` is treated as "we did not hear that it took", which is a
   * statement about the bridge and not an outcome code (contract rule 5).
   *
   * The slot itself is never updated from here. Every accepted command bumps the engine's state
   * version and `agora.stories.live` is republished on it, so the chips below are the engine's
   * account of this slot and never the panel's guess at one.
   */
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

  const title = slotTitle(slot);
  const nameMissing = !slot.name;
  const response = responseLabel(slot.response);
  const outcome = slotOutcomeLabel(slot.outcome);
  const held = slot.outcome === "Unmeasurable";
  const settled = slot.outcome !== "Pending";

  return (
    <div className={styles.slot}>
      <div className={styles.slotHead}>
        <Button
          variant="flat"
          className={open ? styles.tackleOpen : styles.tackle}
          selected={open}
          disabled={busy}
          onSelect={toggle}
        >
          {"Tackle " + title}
        </Button>

        <div className={styles.chips}>
          <span className={styles.chip}>{slot.role === "Major" ? "Major event" : "Minor event"}</span>
          {/* The tier is the engine's, straight off the wire. Nothing here compares a severity to a
              threshold — see the file header. */}
          <span className={styles.chipTier}>{slot.tier}</span>
          <span className={styles.chipQuiet}>{"Severity " + String(slot.severity) + " of 5"}</span>
          {/* "Not answered yet" is a state of its own and is the only signal that there is work
              outstanding on this story. */}
          {response ? (
            <span
              className={cx(
                styles.chipResponse,
                slot.response === "Unaddressed" && styles.chipUnanswered,
              )}
            >
              {response}
            </span>
          ) : null}
          {settled && outcome ? (
            // `Unmeasurable` is held, not failed: the engine could not read the city, it costs
            // nothing, and it is excluded from the archive row's scored count.
            <span
              className={cx(
                styles.chipOutcome,
                held && styles.chipHeld,
                slot.outcome === "Met" && styles.chipGood,
                slot.outcome === "NotMet" && styles.chipBad,
              )}
            >
              {outcome}
            </span>
          ) : null}
          {slot.manualDeclared ? (
            <span className={styles.chipQuiet}>You declared this one</span>
          ) : null}
        </div>
      </div>

      {/* A name the catalog no longer carries is said in words. The event id is never printed where
          a name belongs — it would look like it worked. */}
      {nameMissing ? (
        <div className={styles.unknownEvent}>
          This build&apos;s civic catalog no longer explains this event, so it has no name or
          description here. It still counts towards the story, and every response below still works.
        </div>
      ) : slot.description ? (
        <div className={styles.slotDescription}>{slot.description}</div>
      ) : null}

      {tierNote(slot.tier) ? <div className={styles.tierNote}>{tierNote(slot.tier)}</div> : null}

      {/* Both aftermath lines ship before resolution so the stakes are readable while there is still
          time to act on them. */}
      {slot.successText || slot.failText ? (
        <div className={styles.stakes}>
          {slot.successText ? (
            <div className={styles.stake}>
              <span className={styles.stakeLabel}>If it goes well</span>
              <span className={styles.stakeText}>{slot.successText}</span>
            </div>
          ) : null}
          {slot.failText ? (
            <div className={styles.stake}>
              <span className={styles.stakeLabel}>If it does not</span>
              <span className={styles.stakeText}>{slot.failText}</span>
            </div>
          ) : null}
        </div>
      ) : null}

      {open ? (
        <div className={styles.options}>
          {/* 1 — Ignore. A decision, with the player's own words attached to it. */}
          <div className={styles.option}>
            <div className={styles.optionTitle}>Let it go</div>
            {slot.ignoreText ? <div className={styles.optionText}>{slot.ignoreText}</div> : null}
            <textarea
              className={styles.textarea}
              rows={3}
              value={ignoreText}
              disabled={busy}
              placeholder="Why you are letting this one go (optional)"
              onKeyDown={swallowKeys}
              onKeyUp={swallowKeys}
              onChange={function (event) {
                setIgnoreText(event.target.value);
              }}
            />
            <Button
              variant="flat"
              className={styles.act}
              disabled={busy}
              onSelect={function () {
                send(function () {
                  return setStoryResponse(props.storyId, slot.eventId, "Ignore", ignoreText.trim());
                });
              }}
            >
              Let it go
            </Button>
            <div className={styles.optionNote}>
              This scores the same as never answering — but it is on the record as your decision, and
              the city hears it that way.
            </div>
          </div>

          {/* 2 — Goal. The engine measures the city and decides. */}
          <div className={styles.option}>
            <div className={styles.optionTitle}>Take it on</div>
            {slot.goalText ? <div className={styles.optionText}>{slot.goalText}</div> : null}
            <Button
              variant="flat"
              className={styles.act}
              disabled={busy}
              onSelect={function () {
                send(function () {
                  return setStoryResponse(props.storyId, slot.eventId, "Goal", "");
                });
              }}
            >
              Take it on
            </Button>
            <div className={styles.optionNote}>
              The engine reads the city when the story resolves. If it cannot read it, this is held
              rather than failed and costs you nothing.
            </div>
          </div>

          {/* 3 — Buy it off. Its OWN channel: a purchase sent as a response would be a success nobody
              paid for, and `setResponse("PowerOverride")` answers `BadValue` by design. The button is
              never withheld on affordability — the engine answers that, at the moment of the press. */}
          <div className={styles.option}>
            <div className={styles.optionTitle}>Buy it off</div>
            {slot.powerOverrideText ? (
              <div className={styles.optionText}>{slot.powerOverrideText}</div>
            ) : null}
            <Button
              variant="flat"
              className={cx(styles.act, !slot.canAfford && styles.actShort)}
              disabled={busy}
              onSelect={function () {
                send(function () {
                  return spendPowerOverride(props.storyId, slot.eventId);
                });
              }}
            >
              {slot.overrideCost > 0
                ? "Spend " + String(slot.overrideCost) + " political power"
                : "Buy it off"}
            </Button>
            <div className={styles.optionNote}>
              {slot.overrideCost > 0
                ? slot.canAfford
                  ? "Bought off, this one counts as met and nothing is measured."
                  : "The published balance does not cover this price. Press it anyway if you like — the engine decides, and it will say why."
                : "No price is published for this event. That is what a save with the political-power system switched off looks like; press it and the engine will say so plainly."}
            </div>
          </div>

          {/* 4 — Manual. Two steps on purpose: choosing it, and then declaring how it went. */}
          <div className={styles.option}>
            <div className={styles.optionTitle}>Handle it yourself</div>
            <div className={styles.optionText}>
              Deal with it in the city however you like, then come back and tell the engine how it
              went. Nothing is measured for you.
            </div>
            <textarea
              className={styles.textarea}
              rows={3}
              value={manualText}
              disabled={busy}
              placeholder="What you did, or are going to do"
              onKeyDown={swallowKeys}
              onKeyUp={swallowKeys}
              onChange={function (event) {
                setManualText(event.target.value);
              }}
            />
            <Button
              variant="flat"
              className={styles.act}
              disabled={busy}
              onSelect={function () {
                send(function () {
                  return setStoryResponse(props.storyId, slot.eventId, "Manual", manualText.trim());
                });
              }}
            >
              Handle it yourself
            </Button>

            {/* Declaring is only legal once the slot is already Manual — anything else answers
                `BadValue`, and the panel does not pre-judge that: the buttons appear when the
                engine's published response says the slot is in that state. */}
            {slot.response === "Manual" ? (
              <div className={styles.declare}>
                <Button
                  variant="flat"
                  className={styles.act}
                  disabled={busy}
                  onSelect={function () {
                    send(function () {
                      return declareManualOutcome(
                        props.storyId, slot.eventId, true, manualText.trim(),
                      );
                    });
                  }}
                >
                  I handled it
                </Button>
                <Button
                  variant="flat"
                  className={styles.act}
                  disabled={busy}
                  onSelect={function () {
                    send(function () {
                      return declareManualOutcome(
                        props.storyId, slot.eventId, false, manualText.trim(),
                      );
                    });
                  }}
                >
                  I did not
                </Button>
              </div>
            ) : null}

            <div className={styles.optionNote}>
              A success you declare yourself needs a line of justification and pays at the smallest
              rate whatever the event&apos;s tier. Admitting a failure needs no explanation.
            </div>
          </div>
        </div>
      ) : null}

      {/* The engine's verdict, in English. Never a code, never an exception message, and worded as a
          warning rather than a refusal when the write was in fact accepted. */}
      {feedback ? (
        <div className={feedback.tone === "warn" ? styles.warning : styles.refusal}>
          {feedback.text}
        </div>
      ) : null}
    </div>
  );
};
