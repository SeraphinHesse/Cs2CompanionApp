/**
 * AGORA-SEAM(wave-6/6d) — the story card.
 *
 * A compiling stub so `ui/src/index.tsx`'s fifth append and `npx tsc --noEmit` are green from the
 * spine commit onward. **LANE 6d OWNS THIS FILE and replaces it entirely.** It is not finished work.
 *
 * What 6d delivers, per the wave-6 lane table:
 *
 *  - `storyAlerts$[0]` or nothing — ONE card at a time, by construction, exactly as `ArticleModal`
 *    does it. The queue arrives oldest-first and is never re-sorted here.
 *  - **ONE CARD PER STORY, NEVER ONE PER EVENT.** All of a story's slots render inside this one
 *    card. Two stories in a cycle mean two cards, not six — that is manual gate 3b and it is the
 *    single easiest thing in this lane to get wrong.
 *  - The pause barrier through `useSimulationHeldPaused(active)` from `./pause`, taken only when the
 *    card's own `major` flag says so. **`storyPause.ts` is not needed and must not be written** —
 *    `pause.ts` already exposes exactly this hook and a second copy would be a second refcount on
 *    one barrier. The rework plan names a `storyPause.ts`; it predates `pause.ts` landing.
 *  - Always dismissable, through `ackStoryAlert`, including from the boundary's fallback: while the
 *    barrier is held the game forces the speed to zero every frame, so a card with no working way
 *    out is a game the player cannot un-pause by any means at all.
 *  - Its own error boundary, on the pattern of `ArticleModalBoundary`, and rendered through `Portal`
 *    so it overlays the HUD rather than sitting in a hook point's corner.
 *
 * **Dismissing the card answers nothing.** The story stays live and is tackled from the Stories
 * panel; this surface is a notification, not a form. If 6d finds itself putting the four response
 * buttons on the card, that is 6b's job and the interruption budget is the reason.
 */
export const StoryModal = (): JSX.Element | null => null;
