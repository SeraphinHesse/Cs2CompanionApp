import { useCallback, useEffect, useRef, useState } from "react";
import { useValue } from "cs2/api";
import { Button, Portal } from "cs2/ui";

import { EuFlag, UsFlag } from "./Flags";
import { FirstRunBoundary } from "./FirstRunBoundary";
import { enabled$, isFirstRun$, requestSetting, writeMessage } from "./bindings";
import { useSimulationHeldPaused } from "./pause";
import { REGION_CHOICES } from "./regions";
import styles from "./FirstRunDialog.module.scss";

/**
 * The one question Agora asks before a save begins: Europe or the United States.
 *
 * It has to be asked, and it has to be asked once. The theme drives the electoral system, the naming
 * vocabulary, term length and which timeline catalogs apply, and it is locked at the first election
 * — so a save that was never asked is a save that is silently European whatever map it is on, which
 * is the defect fixplan.md §W3 exists to close.
 *
 * Rendered through `Portal` so it overlays the whole HUD instead of sitting in a hook point's
 * corner. Mounted from its own `moduleRegistry.append`, separate from the dashboard's, so a failure
 * in either cannot take the other down.
 *
 * Not a `ConfirmationDialog`: that component models confirm-or-cancel, and this is a two-way branch
 * with no cancel. There is deliberately no dismiss — every save gets a region either way, and
 * "close the box" would mean "choose Europe without being told you did".
 */

/** How the copy reads while a choice is in flight. Both cards go quiet, not just the one pressed. */
const WORKING_LABEL = "Setting up…";

const FirstRunDialogInner = (): JSX.Element | null => {
  const enabled = useValue(enabled$);
  const isFirstRun = useValue(isFirstRun$);

  const [busy, setBusy] = useState(false);
  const [message, setMessage] = useState("");

  // The dialog unmounts the instant the engine answers, and both writes are awaited — so the
  // component can easily be gone before the last `then` runs. Setting state on an unmounted tree is
  // a warning at best and a leak at worst.
  const mounted = useRef(true);
  useEffect(function () {
    return function () {
      mounted.current = false;
    };
  }, []);

  // Hold the sim while the prompt is up, and only while it is up. The barrier is refcounted by the
  // game, so releasing it is what restores the player's speed — see pause.ts.
  const open = enabled && isFirstRun;
  useSimulationHeldPaused(open);

  const choose = useCallback(function (theme: Agora.RegionThemeName) {
    setBusy(true);
    setMessage("");

    // The theme write and the dismissal are two requests, in that order, and the second only runs if
    // the first was accepted. A save that dismissed the prompt without a theme taking would be
    // stuck with the default and no prompt left to change it.
    void requestSetting("theme", theme).then(function (chosen) {
      if (!mounted.current) {
        return;
      }

      const refusal = writeMessage(chosen);
      if (refusal) {
        // Leave the dialog open. This is the entire reason `setSetting` is a CallBinding rather than
        // a trigger: the player is told what the engine said and can press again.
        setMessage(refusal);
        setBusy(false);
        return;
      }

      void requestSetting("dismissFirstRun", "").then(function (dismissed) {
        if (!mounted.current) {
          return;
        }
        const dismissRefusal = writeMessage(dismissed);
        if (dismissRefusal) {
          setMessage(dismissRefusal);
        }
        setBusy(false);
      });
    });
  }, []);

  // Every hook is above this line — neither the master toggle nor the first-run flag may change the
  // hook order.
  if (!open) {
    return null;
  }

  return (
    <Portal>
      <div className={styles.scrim}>
        <div className={styles.dialog}>
          <div className={styles.title}>Where is this city?</div>
          <div className={styles.subtitle}>
            This sets how the city elects its council, what its parties are called, and how long a
            term lasts. It can be changed from Agora&apos;s settings until the first election, and
            not after.
          </div>

          <div className={styles.choices}>
            {REGION_CHOICES.map(function (choice) {
              return (
                <Button
                  key={choice.theme}
                  variant="flat"
                  className={styles.choice}
                  disabled={busy}
                  onSelect={function () {
                    choose(choice.theme);
                  }}
                >
                  {/* A div inside a <button> is what the game's own Button renders into, and the
                      card needs a column: the flag, the heading and one line beneath both. */}
                  <div className={styles.choiceBody}>
                    <div className={styles.choiceFlag}>
                      {choice.theme === "Eu" ? <EuFlag /> : <UsFlag />}
                    </div>
                    <div className={styles.choiceLabel}>{choice.label}</div>
                    <div className={styles.choiceText}>{choice.consequence}</div>
                  </div>
                </Button>
              );
            })}
          </div>

          {busy ? <div className={styles.working}>{WORKING_LABEL}</div> : null}

          {/* The engine's verdict, in English. Never a code, never an exception message. */}
          {message ? <div className={styles.refusal}>{message}</div> : null}
        </div>
      </div>
    </Portal>
  );
};

export const FirstRunDialog = (): JSX.Element => (
  <FirstRunBoundary>
    <FirstRunDialogInner />
  </FirstRunBoundary>
);
