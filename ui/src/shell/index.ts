// The dashboard shell. ui/src/index.tsx mounts these two and nothing else:
//   moduleRegistry.append("GameTopLeft", AgoraButton);
//   moduleRegistry.append("GameTopRight", Dashboard);
//
// isolatedModules is on, so these are value re-exports, never type ones.
export { AgoraButton } from "./AgoraButton";
export { Dashboard } from "./Dashboard";
