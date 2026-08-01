// Panel 23 — Seats. Registered from ui/src/index.tsx:
//   import { SeatsPanel } from "./panels/Seats";
//   moduleRegistry.append("GameTopRight", SeatsPanel);
//
// Exporting from a folder index keeps the mount point's import stable if the panel is later split
// into more files. `isolatedModules` is on, so this must be a value re-export, not a type one.
export { SeatsPanel } from "./SeatsPanel";
