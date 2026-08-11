import { useValue } from "cs2/api";
import { Button } from "cs2/ui";

import { DistrictsPanel } from "../panels/Districts";
import { NewsPanel } from "../panels/News";
import { PartiesPanel } from "../panels/Parties";
import { SeatsPanel } from "../panels/Seats";
import { ShellBoundary } from "./Boundary";
import { SettingsPanel } from "./SettingsPanel";
import { enabled$, settings$ } from "./bindings";
import { regionLabel } from "./regions";
import {
  AgoraTab,
  TAB_LABEL,
  TAB_ORDER,
  activeTab$,
  closeDashboard,
  dashboardOpen$,
  openSettings,
  settingsOpen$,
  showTab,
  toggleSettings,
} from "./state";
import styles from "./Shell.module.scss";

/**
 * The dashboard shell. Mounted once at GameTopRight, replacing the three separate appends the
 * panels used to make.
 *
 * Two things changed and both were the point. The panels are behind an open/closed flag, so the
 * default view of the game is the game. And only one panel renders at a time — News alone is
 * 760rem wide and 640rem tall, so three of them stacked in one corner exceeded the height of the
 * hook point on any interface scale and buried the city underneath.
 *
 * Rendering one tab at a time also unmounts the other two, which drops their binding
 * subscriptions. That is a feature for the Districts panel in particular: its detail and crosstab
 * map bindings are fetched per open district and there is no reason to hold them while the player
 * is reading the news.
 */

// Every entry in TAB_ORDER needs a case here. The default falls through to the Council panel, so a
// missing case is not a blank tab - it is the wrong panel, silently, with no error to find.
function renderTab(tab: AgoraTab): JSX.Element {
  switch (tab) {
    case "parties":
      return <PartiesPanel />;
    case "districts":
      return <DistrictsPanel />;
    case "news":
      return <NewsPanel />;
    case "council":
    default:
      return <SeatsPanel />;
  }
}

const DashboardInner = (): JSX.Element | null => {
  const enabled = useValue(enabled$);
  const open = useValue(dashboardOpen$);
  const tab = useValue(activeTab$);
  const settingsOpen = useValue(settingsOpen$);
  const settings = useValue(settings$);

  // Every hook is above this line — neither the master toggle nor the open flag may change the
  // hook order.
  if (!enabled || !open) {
    return null;
  }

  return (
    <div className={styles.shell}>
      <div className={styles.bar}>
        <span className={styles.barTitle}>AGORA</span>

        <div className={styles.tabs}>
          {TAB_ORDER.map(function (candidate) {
            return (
              <Button
                key={candidate}
                variant="flat"
                className={candidate === tab ? styles.tabSelected : styles.tab}
                selected={candidate === tab}
                onSelect={function () {
                  showTab(candidate);
                }}
              >
                {TAB_LABEL[candidate]}
              </Button>
            );
          })}
        </div>

        {/*
          The region, while it is still a choice, as a control rather than as a label.

          The first-run prompt is the intended route and is not always taken: it renders through
          `Portal` out of a hook point's DOM position, its own boundary's fallback dismisses it and
          defaults the save to Europe, and `isFirstRun` is one-shot and unpersisted, so a save that
          reaches a second load without having answered never sees the prompt again. Any of those
          leaves a player on the initialiser theme with no idea the choice existed - which is
          precisely the "locked to EU" report. This chip is the standing second route: it names the
          region the save is actually on, says it is still changeable, and opens the picker.

          Gated on `themeLocked` alone, from the published value, so it disappears exactly when the
          choice does - at the first election, which is ratified and is not being touched here.
        */}
        {settings.themeLocked ? null : (
          <Button
            variant="flat"
            className={styles.regionChip}
            onSelect={openSettings}
            tooltipLabel="This city's region decides its electoral system, party names and term length. It can still be changed - until the first election."
          >
            {regionLabel(settings.theme) + " - change"}
          </Button>
        )}

        {/* Not a fifth tab. The tab strip is political data - Council, Parties, Districts, News -
            and the per-save settings are chrome, so they sit beside the close control instead. */}
        <Button
          variant="flat"
          className={settingsOpen ? styles.settingsToggleOpen : styles.settingsToggle}
          selected={settingsOpen}
          onSelect={toggleSettings}
          tooltipLabel="Settings for this city"
        >
          Settings
        </Button>

        <Button
          variant="flat"
          className={styles.close}
          onSelect={closeDashboard}
          tooltipLabel="Close the Agora dashboard"
        >
          &#215;
        </Button>
      </div>

      {settingsOpen ? <SettingsPanel /> : null}

      {/*
        Keyed by tab so switching tabs remounts rather than reconciling one panel's tree into
        another's. The panels are structurally unrelated and each subscribes to its own bindings on
        mount; reusing the tree across a switch is the kind of thing that works until it silently
        does not.
      */}
      <div key={tab} className={styles.panelSlot}>
        {renderTab(tab)}
      </div>
    </div>
  );
};

export const Dashboard = (): JSX.Element => (
  <ShellBoundary>
    <DashboardInner />
  </ShellBoundary>
);
