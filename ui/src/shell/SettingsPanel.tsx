import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { useValue } from "cs2/api";
import { Button } from "cs2/ui";

import { requestSetting, roster$, settings$, writeMessage } from "./bindings";
import { REGION_CHOICES, regionLabel } from "./regions";
import { closeSettings } from "./state";
import styles from "./SettingsPanel.module.scss";

/**
 * The per-save settings surface. Three settings, and no more than three.
 *
 * fixplan.md §W3 says the player "may change their mind from the settings surface" without ever
 * saying where that is. This is it, kept deliberately small: without it `ThemeLocked` is a mechanic
 * no player can reach, and W5's two press settings need the same surface anyway.
 *
 * It is a row in the dashboard shell rather than a fourth tab — a queued plan puts Parties in that
 * slot, and settings are not a panel of political data.
 *
 * Every control here is a mirror of `agora.state.settings`. Nothing on screen is a value this
 * component owns: it renders what the engine published, asks for a change, and renders what the
 * engine published next. That is the difference between a setting and a control that looks set.
 */

/** Setting keys this surface writes. The wire names, exactly (contract §4.1). */
type SettingKey = "theme" | "pauseOnMajorNews" | "showAllReports";

/** A write that has been sent and not yet answered. */
interface Pending {
  key: SettingKey;
  value: string;
}

interface ToggleRowProps {
  label: string;
  hint: string;
  value: boolean;
  disabled: boolean;
  onChange: (next: boolean) => void;
}

/**
 * A two-state setting as a pair of buttons rather than a checkbox: `cs2/ui` ships no checkbox, and a
 * pair reads the same as the theme choice above it.
 */
const ToggleRow = ({ label, hint, value, disabled, onChange }: ToggleRowProps): JSX.Element => (
  <div className={styles.row}>
    <div className={styles.rowLabel}>{label}</div>
    <div className={styles.rowHint}>{hint}</div>
    <div className={styles.options}>
      <Button
        variant="flat"
        className={value ? styles.optionSelected : styles.option}
        selected={value}
        disabled={disabled}
        onSelect={function () {
          onChange(true);
        }}
      >
        On
      </Button>
      <Button
        variant="flat"
        className={!value ? styles.optionSelected : styles.option}
        selected={!value}
        disabled={disabled}
        onSelect={function () {
          onChange(false);
        }}
      >
        Off
      </Button>
    </div>
  </div>
);

export const SettingsPanel = (): JSX.Element => {
  const settings = useValue(settings$);
  const roster = useValue(roster$);

  const [pending, setPending] = useState<Pending | null>(null);
  const [message, setMessage] = useState("");
  const [confirmTheme, setConfirmTheme] = useState("");

  const mounted = useRef(true);
  useEffect(function () {
    return function () {
      mounted.current = false;
    };
  }, []);

  /**
   * Parties the player has taken ownership of. A theme change discards the whole registry — party
   * ids are reused across themes with different meanings — so a renamed or recoloured party is
   * destroyed by it, and the player is owed that warning before the call goes out, not after.
   *
   * This is an affordance, not a verdict: the panel is not deciding the change is illegal, it is
   * asking whether the player meant it. Contract rule 5 is about rejections, and this is not one.
   */
  const ownedParties = useMemo(
    function () {
      return roster.filter(function (party) {
        return party.nameLocked || party.descriptionLocked || party.colorLocked;
      }).length;
    },
    [roster],
  );

  const send = useCallback(function (key: SettingKey, value: string) {
    setPending({ key: key, value: value });
    setMessage("");

    void requestSetting(key, value).then(function (result) {
      if (!mounted.current) {
        return;
      }
      // Dropping the pending value is what reverts the control. Everything on screen comes back from
      // `settings`, which only moved if the engine accepted — so a refused write puts the button
      // back where it was, and a control still showing a value the engine refused cannot happen.
      setPending(null);
      setMessage(writeMessage(result));
    });
  }, []);

  const busy = pending !== null;

  function shownTheme(): string {
    return pending !== null && pending.key === "theme" ? pending.value : settings.theme;
  }

  function shownFlag(key: SettingKey, published: boolean): boolean {
    if (pending !== null && pending.key === key) {
      return pending.value === "true";
    }
    return published;
  }

  function requestTheme(theme: Agora.RegionThemeName): void {
    if (theme === settings.theme) {
      return;
    }
    if (ownedParties > 0) {
      setConfirmTheme(theme);
      return;
    }
    send("theme", theme);
  }

  function confirmChange(): void {
    const theme = confirmTheme;
    setConfirmTheme("");
    send("theme", theme);
  }

  return (
    <div className={styles.settings}>
      <div className={styles.header}>
        <span className={styles.headerTitle}>Settings for this city</span>
        <Button
          variant="flat"
          className={styles.headerClose}
          onSelect={closeSettings}
          tooltipLabel="Close settings"
        >
          &#215;
        </Button>
      </div>

      <div className={styles.row}>
        <div className={styles.rowLabel}>Region theme</div>
        {/*
          The unlocked hint names the region the save is ON, not just what the setting does.

          Both halves are load-bearing. A player who never saw the first-run prompt - it renders
          through `Portal`, its boundary's fallback silently defaults to Europe, and `isFirstRun` is
          one-shot and unpersisted - arrives here with no idea a choice was made on their behalf, and
          a hint that only explains the setting leaves them to infer their region from which button
          looks pressed. Saying it, and saying the deadline, is the difference between a setting that
          is reachable and one that is found.
        */}
        <div className={styles.rowHint}>
          {settings.themeLocked
            ? "This city has held an election, so the choice became history at that election."
            : "This city is set to " +
              regionLabel(settings.theme) +
              ". It decides how the city elects its council, names its parties, and counts a term, " +
              "and it can be changed until the first election."}
        </div>
        <div className={styles.options}>
          {REGION_CHOICES.map(function (choice) {
            const selected = choice.theme === shownTheme();
            return (
              <Button
                key={choice.theme}
                variant="flat"
                className={selected ? styles.optionSelected : styles.option}
                selected={selected}
                // Disabled from a PUBLISHED value only. The panel never decides the theme is locked;
                // it renders the flag the engine published, and reports the code the engine returned.
                disabled={settings.themeLocked || busy}
                onSelect={function () {
                  requestTheme(choice.theme);
                }}
              >
                {choice.label}
              </Button>
            );
          })}
        </div>
      </div>

      {confirmTheme ? (
        <div className={styles.confirm}>
          <div className={styles.confirmText}>
            Switching to {regionLabel(confirmTheme)} discards every party in this city and generates
            a new set. {ownedParties === 1 ? "One party you" : ownedParties + " parties you"}{" "}
            renamed or recoloured will be lost, and cannot be brought back.
          </div>
          <div className={styles.confirmActions}>
            <Button variant="flat" className={styles.confirmCancel} onSelect={function () {
              setConfirmTheme("");
            }}>
              Keep {regionLabel(settings.theme)}
            </Button>
            <Button variant="flat" className={styles.confirmGo} onSelect={confirmChange}>
              Switch to {regionLabel(confirmTheme)}
            </Button>
          </div>
        </div>
      ) : null}

      <ToggleRow
        label="Pause on major news"
        hint="Stop the clock when an election, a change of government, a party's founding or collapse, or a serious event is reported."
        value={shownFlag("pauseOnMajorNews", settings.pauseOnMajorNews)}
        disabled={busy}
        onChange={function (next) {
          send("pauseOnMajorNews", next ? "true" : "false");
        }}
      />

      <ToggleRow
        label="Show every report as a popup"
        hint="On, every report comes up as a card as well; those never stop the clock. Off, the press stays in the News tab."
        value={shownFlag("showAllReports", settings.showAllReports)}
        disabled={busy}
        onChange={function (next) {
          send("showAllReports", next ? "true" : "false");
        }}
      />

      {/* The engine's verdict, in English. Never a code and never an exception message. */}
      {message ? <div className={styles.refusal}>{message}</div> : null}
    </div>
  );
};
