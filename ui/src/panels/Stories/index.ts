// Stories — the tab the player acts on. ui/src/shell/Dashboard.tsx mounts StoriesPanel.
//
// AGORA-SEAM(wave-6/6b): the barrel is real; what it points at is a stub. Lane 6b keeps this export
// name and this path, because `Dashboard.renderTab` imports through it and that import is spine.
// 6b is expected to add a `PanelBoundary` here too, on the pattern of ui/src/panels/Parties — this
// panel reads four bindings across a publisher that also carries five write channels, and a render
// failure inside a moduleRegistry-appended component can blank the game's whole interface.
export { StoriesPanel } from "./StoriesPanel";
