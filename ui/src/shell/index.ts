// The dashboard shell. ui/src/index.tsx mounts these five and nothing else:
//   moduleRegistry.append("GameTopLeft", AgoraButton);
//   moduleRegistry.append("GameTopRight", Dashboard);
//   moduleRegistry.append("GameTopLeft", FirstRunDialog);
//   moduleRegistry.append("GameTopLeft", ArticleModal);
//   moduleRegistry.append("GameTopLeft", StoryModal);
//
// isolatedModules is on, so these are value re-exports, never type ones.
export { AgoraButton } from "./AgoraButton";
export { ArticleModal } from "./ArticleModal";
export { Dashboard } from "./Dashboard";
export { FirstRunDialog } from "./FirstRunDialog";
export { StoryModal } from "./StoryModal";
