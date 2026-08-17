// Stories — the tab the player acts on. ui/src/shell/Dashboard.tsx mounts StoriesPanel through this
// barrel, and that import is spine: the export name and the path do not change.
//
// `StoriesPanel` is wrapped in this directory's own `PanelBoundary` (see StoriesPanel.tsx), on the
// pattern of ui/src/panels/Parties — the panel subscribes a map binding, and `useMapValue` throws
// outright when the C# side has not registered it, which a render failure inside a
// moduleRegistry-appended component would turn into a blank game interface.
export { StoriesPanel } from "./StoriesPanel";
