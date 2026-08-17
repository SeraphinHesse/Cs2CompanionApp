import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { useValue } from "cs2/api";
import { Button } from "cs2/ui";

import { requestSetting, roster$, settings$, writeMessage } from "./bindings";
import { REGION_CHOICES, regionLabel } from "./regions";
import { closeSettings } from "./state";
import styles from "./SettingsPanel.module.scss";

/**
 * The per-save settings surface.
 *
 * fixplan.md §W3 says the player "may change their mind from the settings surface" without ever
 * saying where that is. This is it, kept deliberately small: without it `ThemeLocked` is a mechanic
 * no player can reach, and W5's two press settings need the same surface anyway.
 *
 * The three voter-model levels at the bottom are the one place a player can reach an engine
 * coefficient. They are levels rather than numbers on purpose: the value each maps to lives in
 * `engine_tuning.json`, so this file holds no coefficient and cannot drift from the engine.
 *
 * It is a row in the dashboard shell rather than a fourth tab — a queued plan puts Parties in that
 * slot, and settings are not a panel of political data.
 *
 * Every control here is a mirror of `agora.state.settings`. Nothing on screen is a value this
 * component owns: it renders what the engine published, asks for a change, and renders what the
 * engine published next. That is the difference between a setting and a control that looks set.
 */

/** Setting keys this surface writes. The wire names, exactly (contract §4.1). */
type SettingKey =
  | "theme"
  | "pauseOnMajorNews"
  | "showAllReports"
  | "voteSharpness"
  | "newsInfluence"
  | "brandDiscipline"
  | "storiesEnabled"
  | "storiesPerCycle"
  | "eventsPerStory"
  | "politicalPowerEnabled";

/** One level of a voter-model setting: the wire name, a label, and what it does. */
interface Level {
  value: string;
  label: string;
}

/**
 * The three voter-model settings.
 *
 * Levels are wire names from contract §4.1 and are sent verbatim — the engine parses them by enum
 * name and rejects anything numeric, so a label must never be sent in place of a value.
 *
 * The hints say what changes in the city, not which coefficient moves. A player choosing "Sharp"
 * wants to know their districts will start disagreeing, not that `affinity.softmaxTemperature` fell
 * to 0.10.
 */
const VOTER_SETTINGS: {
  key: SettingKey;
  label: string;
  hint: string;
  levels: Level[];
  /** Reads this setting's published level. A function rather than an index so the payload's field
   *  names are checked by the compiler instead of cast away. */
  read: (s: Agora.SettingsPayload) => string;
}[] = [
  {
    key: "voteSharpness",
    read: function (s) {
      return s.voteSharpness;
    },
    label: "How decisively voters pick",
    hint:
      "Blurred spreads each group's vote thinly over every party, so districts come out looking alike. " +
      "Sharp makes groups commit to the party that fits them, so a rich district and a poor one vote " +
      "visibly differently. Sharp also magnifies events and broken promises.",
    levels: [
      { value: "Blurred", label: "Blurred" },
      { value: "Default", label: "Default" },
      { value: "Sharp", label: "Sharp" },
    ],
  },
  {
    key: "newsInfluence",
    read: function (s) {
      return s.newsInfluence;
    },
    label: "How much the news moves voters",
    hint:
      "How far a strike, a scandal or a disaster can push people toward the parties that agree with " +
      "them about it. Muted keeps elections about the record; Loud lets one bad month decide them.",
    levels: [
      { value: "Muted", label: "Muted" },
      { value: "Default", label: "Default" },
      { value: "Loud", label: "Loud" },
    ],
  },
  {
    key: "brandDiscipline",
    read: function (s) {
      return s.brandDiscipline;
    },
    label: "How closely parties stick to type",
    hint:
      "Only affects North American cities, and only when parties are created. Locked keeps the two " +
      "main parties recognisably themselves in every city; Loose lets them come out with surprising " +
      "positions. Changing this does nothing until a new set of parties is generated.",
    levels: [
      { value: "Loose", label: "Loose" },
      { value: "Default", label: "Default" },
      { value: "Locked", label: "Locked" },
    ],
  },
];

/**
 * The two story counts, offered as levels rather than as a number field.
 *
 * **`"0"` is not "no stories".** Contract §4.1 makes zero the unset value on both keys: the engine
 * falls back to `stories.storiesPerCycle` / `stories.eventsPerStory`, which is how a player hands the
 * decision back to tuning — the same convention, and the same reason, as `SnapshotRetention`. A row
 * that printed a bare `0` here would tell the player the exact opposite of what the setting does, so
 * the unset level is labelled and the hint says what it falls back to.
 *
 * The ceiling of 5 is the contract's, not a balance number. Values outside [0, 5] answer `BadValue`,
 * and this row cannot produce one — but the range check is still the engine's, not this panel's.
 */
const COUNT_LEVELS: Level[] = [
  { value: "0", label: "Default" },
  { value: "1", label: "1" },
  { value: "2", label: "2" },
  { value: "3", label: "3" },
  { value: "4", label: "4" },
  { value: "5", label: "5" },
];

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

interface LevelRowProps {
  label: string;
  hint: string;
  levels: Level[];
  value: string;
  disabled: boolean;
  onChange: (next: string) => void;
}

/**
 * A multi-level setting, rendered with the same button row as the theme choice above it so the
 * panel reads as one surface rather than three control styles.
 */
const LevelRow = ({ label, hint, levels, value, disabled, onChange }: LevelRowProps): JSX.Element => (
  <div className={styles.row}>
    <div className={styles.rowLabel}>{label}</div>
    <div className={styles.rowHint}>{hint}</div>
    <div className={styles.options}>
      {levels.map(function (level) {
        const selected = level.value === value;
        return (
          <Button
            key={level.value}
            variant="flat"
            className={selected ? styles.optionSelected : styles.option}
            selected={selected}
            disabled={disabled}
            onSelect={function () {
              onChange(level.value);
            }}
          >
            {level.label}
          </Button>
        );
      })}
    </div>
  </div>
);

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

  /**
   * The same optimistic render as `shownFlag`, for the two counts. A string because that is what
   * crosses the wire and what the level buttons compare against — the panel never does arithmetic on
   * a setting it only mirrors.
   */
  function shownCount(key: SettingKey, published: number): string {
    if (pending !== null && pending.key === key) {
      return pending.value;
    }
    return String(published);
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

      {VOTER_SETTINGS.map(function (setting) {
        // Same optimistic-render rule as the toggles: show the pending value while a write is in
        // flight, and fall back to what the engine published, so a refused write puts the row back.
        const shown =
          pending !== null && pending.key === setting.key
            ? pending.value
            : setting.read(settings);

        return (
          <LevelRow
            key={setting.key}
            label={setting.label}
            hint={setting.hint}
            levels={setting.levels}
            value={shown}
            disabled={busy}
            onChange={function (next) {
              if (next !== shown) {
                send(setting.key, next);
              }
            }}
          />
        );
      })}

      <div className={styles.sectionTitle}>Stories</div>

      <ToggleRow
        label="Draft stories"
        hint="On, the city sets you a small bundle of civic problems every cycle and judges how you answered them. Turning it off stops the next draft only — a story already running still finishes on its own month, and nothing is ever generated retrospectively."
        value={shownFlag("storiesEnabled", settings.storiesEnabled)}
        disabled={busy}
        onChange={function (next) {
          send("storiesEnabled", next ? "true" : "false");
        }}
      />

      <LevelRow
        label="Stories per cycle"
        hint="How many stories the city drafts at once. Default hands the number back to the mod's own tuning rather than meaning none — pick a figure only if you want more or fewer than the city would choose."
        levels={COUNT_LEVELS}
        value={shownCount("storiesPerCycle", settings.storiesPerCycle)}
        disabled={busy}
        onChange={function (next) {
          if (next !== shownCount("storiesPerCycle", settings.storiesPerCycle)) {
            send("storiesPerCycle", next);
          }
        }}
      />

      <LevelRow
        label="Events per story"
        hint="How many civic problems each story bundles together. Default hands the number back to the mod's own tuning, the same as above; it does not mean a story with nothing in it."
        levels={COUNT_LEVELS}
        value={shownCount("eventsPerStory", settings.eventsPerStory)}
        disabled={busy}
        onChange={function (next) {
          if (next !== shownCount("eventsPerStory", settings.eventsPerStory)) {
            send("eventsPerStory", next);
          }
        }}
      />

      <ToggleRow
        label="Political power"
        hint="On, answering a story well earns political power you can spend to make an awkward problem go away, and answering badly costs it. Off, nothing can be bought off in this city and no debt can build up — stories still draft and still resolve."
        value={shownFlag("politicalPowerEnabled", settings.politicalPowerEnabled)}
        disabled={busy}
        onChange={function (next) {
          send("politicalPowerEnabled", next ? "true" : "false");
        }}
      />

      {/*
        Two published story settings and NO control for either, deliberately.

        Contract §4.1's key table lists every writable key and states plainly that there is none for
        `powerIntensity` or `storyDifficulty`: the preset tables behind them do not exist yet, so a
        write would persist a value, republish it, and change no number in the engine — and would be
        answered `UnknownKey` in the meantime. A switch that does nothing under hint text promising
        behaviour there is none of is exactly what `PauseOnMajorNews` and `ShowAllReports` were before
        W5, and it is not being shipped again. They are shown as text because a player who finds them
        in a save file is owed an explanation of why they cannot be reached, and the note says when
        they arrive rather than leaving that to be guessed at.
      */}
      <div className={styles.row}>
        <div className={styles.rowLabel}>How stories are pitched</div>
        <div className={styles.rowHint}>
          What this city currently carries in its save. Neither can be changed yet.
        </div>
        <div className={styles.readOnlyValue}>
          Power intensity: {settings.powerIntensity} &#183; Story difficulty:{" "}
          {settings.storyDifficulty}
        </div>
        <div className={styles.readOnlyNote}>
          Both are recorded and neither changes anything in this build. The settings that would give
          them meaning arrive in a later pass, and they become adjustable in the same update — a
          control here now would look like a choice and be none.
        </div>
      </div>

      {/* The engine's verdict, in English. Never a code and never an exception message. */}
      {message ? <div className={styles.refusal}>{message}</div> : null}
    </div>
  );
};
