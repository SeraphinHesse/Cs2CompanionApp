/**
 * AGORA-SEAM(wave-6/6b) — the Stories panel.
 *
 * A compiling stub so that the shell's tab strip, `Dashboard.renderTab` and `npx tsc --noEmit` are
 * green from the spine commit onward. **LANE 6b OWNS THIS FILE and replaces it entirely.** It is not
 * finished work and must not be mistaken for any.
 *
 * What 6b delivers here, per the wave-6 lane table:
 *
 *  - The live stories from `stories$`, in the engine's order, each with its headline and its article
 *    fetched from `agora.stories.article`. Render BOTH prose voices when both exist — the pool's
 *    text is what is always shown and the model's appears beside it, never instead of it.
 *  - Three **"Tackle <event name>"** controls per story, one per slot, expanding into the four
 *    response options. Never render a raw `eventId` where a name belongs; a slot whose `name` is ""
 *    says the catalog no longer explains it.
 *  - Textareas for Ignore and Manual — six per story. Copy `PartyEditor.tsx`'s pattern AND stop key
 *    propagation on `onKeyDown`, or space, digits, `b` and `p` reach the game's hotkeys.
 *  - A **Resolve now** control, and the archive below.
 *
 * Flexbox only: Gameface has no CSS grid.
 */
// An empty fragment rather than `null`: `Dashboard.renderTab` is typed `JSX.Element`, and widening
// its return type to accommodate a stub would outlive the stub.
export const StoriesPanel = (): JSX.Element => <></>;
