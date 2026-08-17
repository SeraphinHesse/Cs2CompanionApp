import { bindLocalValue } from "cs2/api";

/**
 * Dashboard shell state: whether the dashboard is open, and which panel is showing.
 *
 * This is view state, not engine state. Contract §3 sanctions `bindLocalValue` for exactly this
 * ("UI-only state shared between panels — never a round trip through C#"), so there is no entry in
 * docs/contracts/ui_bindings.md for anything in this file and none may be added. It also is not a
 * setting: non-negotiable #10 puts per-save settings in the sidecar, and "is the dashboard open
 * right now" is not one — it resets to closed every session, deliberately.
 *
 * A module-scope binding is what makes this work across mount points. `AgoraButton` is appended to
 * GameTopLeft and `Dashboard` to GameTopRight, so they are two separate React trees and no context
 * or shared hook state can span them. They do share this module, because webpack bundles the mod
 * once — so the binding object below is a single instance both trees subscribe to.
 */

export type AgoraTab = "council" | "parties" | "stories" | "districts";

/**
 * Left-to-right order of the tab strip. Parties sits second deliberately: Council answers "who
 * governs" and Parties answers "who are they", and the two are read together. Districts is a
 * drill-down.
 *
 * Stories sits third — after the two that describe the city's politics and before the one that
 * drills into them — because it is the only tab the player must ACT on. A tab holding decisions
 * with a deadline does not belong behind the archives.
 *
 * **News was the fifth and is gone as of wave 7.** The feed was a rear-view mirror assembled from
 * scratch on every publish, and the story system replaced what it was for: prose the player can act
 * on rather than a record of what already happened. The one part worth keeping, the mandate tracker,
 * moved into Stories. Elections, coalitions and party lifecycle still interrupt through the alert
 * card — that lane outlived the panel deliberately, because nothing else in the mod announces them.
 *
 * Every tab here needs a matching `case` in `renderTab` (Dashboard.tsx). The `default:` branch
 * falls through to the Council panel, so a tab added here and nowhere else renders the wrong panel
 * with no error anywhere.
 */
export const TAB_ORDER: AgoraTab[] = ["council", "parties", "stories", "districts"];

export const TAB_LABEL: { [tab in AgoraTab]: string } = {
  council: "Council",
  parties: "Parties",
  stories: "Stories",
  districts: "Districts",
};

/**
 * Closed on load, every load. The player asked for their city, not for a dashboard — the mod puts
 * one button on screen and nothing else until it is pressed.
 */
export const dashboardOpen$ = bindLocalValue<boolean>(false);

export const activeTab$ = bindLocalValue<AgoraTab>("council");

/**
 * Whether the settings drawer under the tab bar is showing.
 *
 * View state, like the two above it — the settings it *contains* are per-save and live in the
 * sidecar, but "is the drawer open" resets every session and never crosses the bridge. It is not a
 * tab: the tab strip carries political data and now runs Council, Parties, Districts, News, while
 * the settings are chrome and sit beside the close control.
 */
export const settingsOpen$ = bindLocalValue<boolean>(false);

export function toggleDashboard(): void {
  dashboardOpen$.update(!dashboardOpen$.value);
}

export function closeDashboard(): void {
  dashboardOpen$.update(false);
  // Closing the dashboard closes the drawer with it, so reopening lands on the panels rather than on
  // whatever was last being configured.
  settingsOpen$.update(false);
}

export function toggleSettings(): void {
  settingsOpen$.update(!settingsOpen$.value);
}

export function closeSettings(): void {
  settingsOpen$.update(false);
}

/**
 * Open the settings drawer, whatever it was doing. Not `toggleSettings` — this backs the region
 * chip, whose entire job is that pressing it always lands the player on the theme picker, and a
 * toggle would close the drawer for a player who already had it open and pressed the chip to find
 * out where the picker was.
 */
export function openSettings(): void {
  dashboardOpen$.update(true);
  settingsOpen$.update(true);
}

/** Selecting a tab also opens the dashboard, so the tab strip works as a shortcut from closed. */
export function showTab(tab: AgoraTab): void {
  activeTab$.update(tab);
  dashboardOpen$.update(true);
}
