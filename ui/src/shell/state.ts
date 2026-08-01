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

export type AgoraTab = "council" | "districts" | "news";

/** Left-to-right order of the tab strip. */
export const TAB_ORDER: AgoraTab[] = ["council", "districts", "news"];

export const TAB_LABEL: { [tab in AgoraTab]: string } = {
  council: "Council",
  districts: "Districts",
  news: "News",
};

/**
 * Closed on load, every load. The player asked for their city, not for a dashboard — the mod puts
 * one button on screen and nothing else until it is pressed.
 */
export const dashboardOpen$ = bindLocalValue<boolean>(false);

export const activeTab$ = bindLocalValue<AgoraTab>("council");

export function toggleDashboard(): void {
  dashboardOpen$.update(!dashboardOpen$.value);
}

export function closeDashboard(): void {
  dashboardOpen$.update(false);
}

/** Selecting a tab also opens the dashboard, so the tab strip works as a shortcut from closed. */
export function showTab(tab: AgoraTab): void {
  activeTab$.update(tab);
  dashboardOpen$.update(true);
}
