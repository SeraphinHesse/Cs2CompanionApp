import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { useValue } from "cs2/api";
import { Button } from "cs2/ui";

import {
  WriteOutcome,
  colorPalette$,
  editLimits$,
  isAccepted,
  ready$,
  requestColor,
  requestDescription,
  requestRename,
  requestResetColor,
  requestResetDescription,
  requestResetName,
  writeMessage,
} from "./bindings";
import { partyColor } from "./format";
import styles from "./PartyEditor.module.scss";

/**
 * The party editors: rename, rewrite, recolour, and hand each of the three back.
 *
 * Presentation only. It adds NO validation rule of its own - the four lengths and the colour pattern
 * come from `agora.parties.editLimits`, and the verdict on every write comes from the engine. That is
 * the whole reason those two read bindings exist: a counter with a literal in it and `PartyIdentity`
 * in C# are two copies of one number, and when they disagree the wrong one is always the counter, so
 * the player finds out by being refused after typing.
 *
 * Three properties of the write surface shape this component and none of them are cosmetic:
 *
 *  - **Both fields of a pair travel together.** `nameLocked` covers `name` AND `shortName`;
 *    `descriptionLocked` covers `description` AND `slogan`. A description editor with no slogan field
 *    would take ownership of the slogan and freeze it permanently, because a set lock bars flavor
 *    from the whole group from that moment.
 *  - **An empty box is `ValueRequired`, never a reset.** Reset is six separate bindings for exactly
 *    that reason; a cleared field is a slipped keystroke as often as a deliberate hand-back, and the
 *    two have opposite consequences. Nothing here sends "" to a setter to mean "clear".
 *  - **Two of the outcome codes are acceptances.** `isAccepted` decides whether a write took;
 *    `writeMessage` decides what to print. They are different questions - `OkColorInUse` is an
 *    accepted colour that carries a warning - and a falsy check on the message answers the wrong one.
 *
 * Everything on screen is a mirror of `agora.parties.roster`. The component holds a draft while a
 * section is open and holds nothing afterwards: all six handlers bump the engine's state version, so
 * an accepted write comes back on the next UI tick and the editor never has to guess what took.
 */

/** Which section is expanded. "" is the resting state - the editors are opt-in, not always up. */
type Section = "" | "name" | "description" | "color";

/** A refusal reads as a refusal; an accepted write that carries a warning must not. */
type Tone = "warn" | "bad";

interface Feedback {
  text: string;
  tone: Tone;
}

/**
 * Characters used against the published ceiling.
 *
 * It counts what will actually be SENT, which is the trimmed string - the C# validators judge the raw
 * input and deliberately do not trim, so trimming is the panel's job and the counter has to agree
 * with the panel, not with the text box. Over the limit is shown rather than prevented: the engine
 * rejects rather than truncates, and a box that silently refused the keystroke would be a panel-side
 * rule of its own.
 */
const Counter = (props: { used: number; max: number }): JSX.Element => (
  <span className={props.max > 0 && props.used > props.max ? styles.counterOver : styles.counter}>
    {props.used} / {props.max}
  </span>
);

const FieldLabel = (props: { text: string; used: number; max: number }): JSX.Element => (
  <div className={styles.fieldLabel}>
    <span className={styles.fieldLabelText}>{props.text}</span>
    <Counter used={props.used} max={props.max} />
  </div>
);

export const PartyEditor = (props: {
  partyId: string;
  brief: Agora.PartyBrief;
}): JSX.Element | null => {
  const ready = useValue(ready$);
  const rawLimits = useValue(editLimits$);
  const rawPalette = useValue(colorPalette$);

  const brief = props.brief;

  const [section, setSection] = useState<Section>("");
  const [busy, setBusy] = useState(false);
  const [feedback, setFeedback] = useState<Feedback | null>(null);

  const [name, setName] = useState("");
  const [shortName, setShortName] = useState("");
  const [description, setDescription] = useState("");
  const [slogan, setSlogan] = useState("");
  const [hex, setHex] = useState("");

  const mounted = useRef(true);
  useEffect(function () {
    return function () {
      mounted.current = false;
    };
  }, []);

  const limits = rawLimits || null;
  const palette = rawPalette && rawPalette.colors ? rawPalette.colors : [];

  /**
   * The colour pattern the engine will validate against, compiled once.
   *
   * Used to keep Apply from firing on something that cannot possibly be a colour - not as a rule of
   * this panel's, but as the engine's own published pattern applied a moment earlier. If it is absent
   * or will not compile, nothing is pre-checked and the engine decides, which is the correct default:
   * the panel must never refuse a write the C# side would have taken.
   */
  const colorRe = useMemo(
    function () {
      const source = limits ? limits.colorPattern : "";
      if (!source) {
        return null;
      }
      try {
        return new RegExp(source);
      } catch (error) {
        console.warn("[AGORA] editLimits.colorPattern is not a usable regex", error);
        return null;
      }
    },
    [limits]
  );

  const open = useCallback(
    function (next: Section) {
      setFeedback(null);
      if (next === section) {
        setSection("");
        return;
      }
      // Drafts are seeded from the published brief at the moment the section opens, and never
      // afterwards: re-seeding on a republish would wipe what the player was halfway through typing.
      if (next === "name") {
        setName(brief.name);
        setShortName(brief.shortName);
      } else if (next === "description") {
        setDescription(brief.description);
        setSlogan(brief.slogan);
      } else if (next === "color") {
        setHex(brief.colorHex);
      }
      setSection(next);
    },
    [brief, section]
  );

  /**
   * Send one write and render what came back.
   *
   * Acceptance is asked of `isAccepted` and never inferred from an empty message. An accepted write
   * that carries a warning - `OkColorInUse` - leaves the section closed and the warning on screen; a
   * refusal keeps the section open with the draft intact, so the player can fix it rather than retype
   * it. `answered: false` is neither: it prints the generic sentence and is treated as "did not take",
   * because we did not hear that it did.
   */
  const send = useCallback(function (run: () => Promise<WriteOutcome>, closeOnAccept: boolean) {
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

      if (took && closeOnAccept) {
        setSection("");
      }
    });
  }, []);

  // Master gate, exactly as contract section 6 instructs: the empty limits are all zeroes, which is
  // not a usable ceiling, so the editors wait for the first publish rather than counting against nil.
  if (!ready || !limits || !brief.id) {
    return null;
  }

  // What will be sent. Trimmed here, once, so the counter, the Save button and the call all agree.
  const nameOut = name.trim();
  const shortOut = shortName.trim();
  const descriptionOut = description.trim();
  const sloganOut = slogan.trim();
  const hexOut = hex.trim();

  const currentColor = partyColor(brief.colorHex);

  return (
    <div className={styles.editor}>
      <div className={styles.tabs}>
        <Button
          variant="flat"
          className={section === "name" ? styles.tabOpen : styles.tab}
          selected={section === "name"}
          disabled={busy}
          onSelect={function () {
            open("name");
          }}
        >
          Name
        </Button>
        <Button
          variant="flat"
          className={section === "description" ? styles.tabOpen : styles.tab}
          selected={section === "description"}
          disabled={busy}
          onSelect={function () {
            open("description");
          }}
        >
          Description
        </Button>
        <Button
          variant="flat"
          className={section === "color" ? styles.tabOpen : styles.tab}
          selected={section === "color"}
          disabled={busy}
          onSelect={function () {
            open("color");
          }}
        >
          Colour
        </Button>

        {/* The locks, in the player's terms. A locked field is the player's own words and is NEVER
            described as generated or as AI output - a player who names their party and then reads
            that it was generated has been told the mod is going to overwrite it. */}
        <div className={styles.owned}>
          {brief.nameLocked ? <span className={styles.ownedChip}>Your name</span> : null}
          {brief.descriptionLocked ? <span className={styles.ownedChip}>Your words</span> : null}
          {brief.colorLocked ? <span className={styles.ownedChip}>Your colour</span> : null}
        </div>
      </div>

      {section === "name" ? (
        <div className={styles.section}>
          <FieldLabel text="Full name" used={nameOut.length} max={limits.nameMax} />
          <input
            type="text"
            className={styles.input}
            value={name}
            disabled={busy}
            onChange={function (event) {
              setName(event.target.value);
            }}
          />

          {/* The short name is not optional. `nameLocked` covers both, so a rename sent without it
              would take ownership of the short name and leave nothing able to write it again. */}
          <FieldLabel text="Short name" used={shortOut.length} max={limits.shortNameMax} />
          <input
            type="text"
            className={styles.input}
            value={shortName}
            disabled={busy}
            onChange={function (event) {
              setShortName(event.target.value);
            }}
          />

          <div className={styles.hint}>
            Both are yours once you save. The generator stops writing either of them, and the press
            uses what you put here.
          </div>

          <div className={styles.actions}>
            <Button
              variant="flat"
              className={styles.save}
              disabled={busy}
              onSelect={function () {
                send(function () {
                  return requestRename(props.partyId, nameOut, shortOut);
                }, true);
              }}
            >
              Save name
            </Button>
            {brief.nameLocked ? (
              <Button
                variant="flat"
                className={styles.reset}
                disabled={busy}
                onSelect={function () {
                  send(function () {
                    return requestResetName(props.partyId);
                  }, true);
                }}
              >
                Reset name
              </Button>
            ) : null}
          </div>

          {brief.nameLocked ? (
            <div className={styles.hint}>
              Reset picks a new name straight away and you will see it change on this card.
            </div>
          ) : null}
        </div>
      ) : null}

      {section === "description" ? (
        <div className={styles.section}>
          <FieldLabel
            text="Description"
            used={descriptionOut.length}
            max={limits.descriptionMax}
          />
          <textarea
            className={styles.textarea}
            rows={5}
            value={description}
            disabled={busy}
            onChange={function (event) {
              setDescription(event.target.value);
            }}
          />

          {/* The slogan travels with the description for the same reason the short name travels with
              the name: one lock covers both fields of the pair. */}
          <FieldLabel text="Slogan" used={sloganOut.length} max={limits.sloganMax} />
          <input
            type="text"
            className={styles.input}
            value={slogan}
            disabled={busy}
            onChange={function (event) {
              setSlogan(event.target.value);
            }}
          />

          <div className={styles.hint}>
            Both are yours once you save. Leaving one empty is not a way to clear it - use reset for
            that.
          </div>

          <div className={styles.actions}>
            <Button
              variant="flat"
              className={styles.save}
              disabled={busy}
              onSelect={function () {
                send(function () {
                  return requestDescription(props.partyId, descriptionOut, sloganOut);
                }, true);
              }}
            >
              Save description
            </Button>
            {brief.descriptionLocked ? (
              <Button
                variant="flat"
                className={styles.reset}
                disabled={busy}
                onSelect={function () {
                  send(function () {
                    return requestResetDescription(props.partyId);
                  }, true);
                }}
              >
                Reset description
              </Button>
            ) : null}
          </div>

          {brief.descriptionLocked ? (
            <div className={styles.hint}>
              Reset here changes nothing you can see. The text below stays exactly as it is; you are
              handing the field back, and the press rewrites it the next time it looks at this party,
              which can be months of city time away.
            </div>
          ) : null}
        </div>
      ) : null}

      {section === "color" ? (
        <div className={styles.section}>
          <div className={styles.fieldLabel}>
            <span className={styles.fieldLabelText}>Palette</span>
            <span className={styles.counter}>{currentColor}</span>
          </div>

          {/* In the order published: never re-sorted, never de-duplicated. A swatch's position is how
              a player recognises it between sessions, and the engine assigns from this same order.
              The key carries the index for that reason - two identical colours are allowed here. */}
          <div className={styles.swatches}>
            {palette.length === 0 ? (
              <span className={styles.hint}>The palette has not been published yet.</span>
            ) : null}
            {palette.map(function (color, index) {
              const selected = color.toUpperCase() === (brief.colorHex || "").toUpperCase();
              return (
                <Button
                  key={index + ":" + color}
                  variant="flat"
                  className={selected ? styles.swatchOn : styles.swatch}
                  style={{ backgroundColor: partyColor(color) }}
                  selected={selected}
                  disabled={busy}
                  tooltipLabel={color}
                  onSelect={function () {
                    setHex(color);
                    send(function () {
                      return requestColor(props.partyId, color);
                    }, false);
                  }}
                >
                  {/* No glyph. The selected swatch is marked by its ring in CSS - a tick would be a
                      character the Gameface font is not guaranteed to carry. */}
                </Button>
              );
            })}
          </div>

          <div className={styles.fieldLabel}>
            <span className={styles.fieldLabelText}>Any colour</span>
            <span className={styles.counter}>#RRGGBB</span>
          </div>
          <div className={styles.hexRow}>
            <input
              type="text"
              className={styles.hexInput}
              value={hex}
              disabled={busy}
              onChange={function (event) {
                setHex(event.target.value);
              }}
            />
            <span className={styles.hexPreview} style={{ backgroundColor: partyColor(hexOut) }} />
            <Button
              variant="flat"
              className={styles.save}
              // The only pre-check in this component, and it is the engine's own pattern rather than
              // a rule of the panel's. When the pattern is missing this is simply not applied.
              disabled={busy || (colorRe !== null && !colorRe.test(hexOut))}
              onSelect={function () {
                send(function () {
                  return requestColor(props.partyId, hexOut);
                }, false);
              }}
            >
              Apply
            </Button>
          </div>

          <div className={styles.actions}>
            {brief.colorLocked ? (
              <Button
                variant="flat"
                className={styles.reset}
                disabled={busy}
                onSelect={function () {
                  send(function () {
                    return requestResetColor(props.partyId);
                  }, true);
                }}
              >
                Reset colour
              </Button>
            ) : null}
          </div>

          <div className={styles.hint}>
            A colour another party already wears is allowed - you will be told, and the two parties
            will look alike on every chart.
            {brief.colorLocked
              ? " Reset gives the colour back to the engine, which picks one from the palette straight away."
              : ""}
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
