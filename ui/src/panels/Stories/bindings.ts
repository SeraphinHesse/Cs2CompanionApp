import { bindMap, bindValue } from "cs2/api";

/**
 * What this panel binds for itself, and nothing more.
 *
 * The whole of `agora.stories` — `stories$`, `storyArchive$`, `power$` and the five write channels —
 * is declared in `ui/src/shell/bindings.ts` and imported from there. That is a wave-6 structural
 * decision recorded in that file: three surfaces read this group from three different React trees,
 * and a `bindValue` is a subscription, so one module-scope declaration is what makes them one
 * subscription rather than three. Nothing in this file may re-declare one of them.
 *
 * Two things are left over, and both are here for a reason the shell does not cover:
 *
 *  - `agora.stories.article` is a MAP binding. It is fetched per story id, only for the stories on
 *    screen, which is why bodies do not ride in `live` at all (contract §4.7, payload caps).
 *  - `agora.state.summary` is not bound by the shell and is the only published source of the current
 *    political date. Contract §4.0 is explicit that anything *about* the political state reads its
 *    date from here rather than from `agora.debug.simDate`, which exists to prove the bridge works.
 *
 * Binding objects are created at module scope, never in a render body — a `bindValue` call in one
 * would allocate and subscribe a fresh binding on every render. Names are copied verbatim from
 * docs/contracts/ui_bindings.md; a rename here is an empty panel at runtime with no build error.
 *
 * The `Agora.*` payload types are GLOBAL (declare namespace Agora in ui/types/bindings.d.ts). Do not
 * import them — there is no runtime module behind them and isolatedModules would turn such an import
 * into a webpack resolution error.
 */

// -- empty / loading values (contract §6, copied literally) --------------------------------------

export const EMPTY_STATE_SUMMARY: Agora.StateSummary = {
  schemaVersion: 0, date: "", termNumber: 0, system: "Proportional", theme: "Eu",
  nextElectionDate: "", isCampaignSeason: false, weeksToElection: -1, mayorPartyId: "",
};

// -- agora.state ---------------------------------------------------------------------------------

/**
 * The current political date, and nothing else off this payload.
 *
 * Read for one purpose: the countdown on a live story. The window itself is the distance between the
 * story's own published `openedDate` and `resolvesDate` — no cycle length is computed anywhere in
 * this panel — but "how long is left" is that resolve month measured against today, and today only
 * exists here.
 */
export const summary$ = bindValue<Agora.StateSummary>(
  "agora.state", "summary", EMPTY_STATE_SUMMARY,
);

// -- agora.stories ---------------------------------------------------------------------------------

/**
 * A story's prose, fetched for exactly the id being rendered.
 *
 * `useMapValue` throws outright if the C# side has not registered the binding — a real state during a
 * partial deploy — and it may not be called conditionally, so the condition lives in the *mount*:
 * `StoryBody` is a component of its own, mounted per story and keyed by the story id. See
 * `ui/src/shell/ArticleModal.tsx`, which documents the same hazard for the news article map.
 */
export const article$ = bindMap<string, Agora.StoryArticle>("agora.stories", "article");
