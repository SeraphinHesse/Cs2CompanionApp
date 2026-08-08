// The dashboard shell. ui/src/index.tsx mounts these three and nothing else:
//   moduleRegistry.append("GameTopLeft", AgoraButton);
//   moduleRegistry.append("GameTopRight", Dashboard);
//   moduleRegistry.append("GameTopLeft", FirstRunDialog);
//
// isolatedModules is on, so these are value re-exports, never type ones.
export { AgoraButton } from "./AgoraButton";
export { Dashboard } from "./Dashboard";
export { FirstRunDialog } from "./FirstRunDialog";
