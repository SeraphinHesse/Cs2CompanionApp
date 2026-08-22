import { useCallback } from "react";
import { Button } from "cs2/ui";

import { wakeFlavor } from "../../shell/bindings";
import { formatSimDate } from "./format";
import styles from "./StoriesPanel.module.scss";

/**
 * Whether the prose writer ran, and the one control that asks it to run again.
 *
 * **Moved here from the News panel in wave 7.** It is the only place in the mod that says whether
 * Claude is reachable and why the last attempt failed, and the only manual route to a refresh — both
 * outlive the panel they were built in, because the story system still writes prose.
 *
 * Nothing here is a political number. `articleCount` is a count of held prose, not a fact about the
 * city, and no field on this payload ever enters engine state (non-negotiable 1).
 */

/**
 * `flavorStatus.lastError` is an ENGINE-authored short code — never LLM output, never a raw
 * exception message — so it is safe to switch on (contract §4.5). An unrecognised code still
 * renders, as a sentence rather than as the code.
 */
const ERROR_TEXT: { [code: string]: string } = {
  CliMissing: "Claude CLI not found",
  Timeout: "Last attempt timed out",
  BadJson: "Last reply was malformed",
  Disabled: "Flavor generation is off",
  Unknown: "Last attempt failed",
};

const UNKNOWN_ERROR = "Flavor unavailable";

export const FlavorStatusLine = (props: { status: Agora.FlavorStatus }): JSX.Element => {
  const status = props.status;

  // The wake REQUESTS; the engine decides. Disabled while one is in flight (contract §4.5), and
  // mounted only inside the ready branch of the panel, so the trigger is never fired at a publisher
  // that does not exist yet. A failed wake keeps the last good flavor by design (non-negotiable 7),
  // so nothing here assumes any prose changes as a result — the only visible consequence may be
  // `lastError` on the next republish.
  const onWake = useCallback(function () {
    wakeFlavor();
  }, []);

  const errorText = status.lastError ? ERROR_TEXT[status.lastError] || UNKNOWN_ERROR : "";

  return (
    <div className={styles.flavor}>
      <div className={styles.chips}>
        <span className={status.providerAvailable ? styles.chipGood : styles.chipBad}>
          {status.providerAvailable ? "Writer online" : "Writer offline"}
        </span>
        {status.isStale ? <span className={styles.chipHeld}>Prose is stale</span> : null}
        {errorText ? <span className={styles.chipHeld}>{errorText}</span> : null}
        {status.pendingWake ? <span className={styles.chipQuiet}>Waking…</span> : null}
        <span className={styles.chipQuiet}>
          {status.lastFlavorDate
            ? "Last filed " + formatSimDate(status.lastFlavorDate)
            : "Nothing filed yet"}
        </span>
        <span className={styles.chipQuiet}>
          {status.articleCount === 1 ? "1 article held"
            : String(status.articleCount) + " articles held"}
        </span>
      </div>

      <div className={styles.flavorActions}>
        <Button
          variant="flat"
          className={styles.wake}
          disabled={status.pendingWake}
          onSelect={onWake}
        >
          Wake writer
        </Button>
        {/* Said plainly, because the button looks like it should do something and often will not:
            a wake is a request, and a refused one leaves the prose exactly as it was. */}
        <span className={styles.flavorNote}>
          Asks the writer for a fresh pass. The engine decides whether it runs; if it does not, the
          prose already on screen stands.
        </span>
      </div>
    </div>
  );
};
