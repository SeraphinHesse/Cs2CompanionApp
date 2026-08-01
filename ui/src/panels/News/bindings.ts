import { bindMap, bindTrigger, bindValue } from "cs2/api";

/**
 * Every binding this panel consumes, declared once at module scope.
 *
 * Binding objects must be created at module scope, never inside a component — a `bindValue`
 * call in a render body would allocate and subscribe a fresh binding on every render.
 *
 * Names are copied verbatim from docs/contracts/ui_bindings.md section 4. A renamed binding
 * produces an empty panel at runtime and no build error, so nothing here may be "tidied".
 * The third argument to bindValue is the value rendered before C# has published anything;
 * omitting it renders `undefined` on the first frame (contract rule 3).
 *
 * The `Agora.*` payload types are GLOBAL (declare namespace Agora in ui/types/bindings.d.ts).
 * Do not import them — there is no runtime module behind them and isolatedModules would turn
 * such an import into a webpack resolution error.
 */

// -- contract caps (section 2, payload budget) --------------------------------------------------

/** The feed is capped in C#; the panel never receives more than this. */
export const AGORA_NEWS_FEED_MAX = 40;

/** Active timeline events are capped in C#. */
export const AGORA_EVENTS_MAX = 25;

// -- empty / loading values (contract section 6, copied literally) ------------------------------

export const EMPTY_NEWS_ARTICLE: Agora.NewsArticle = {
  id: "", date: "", headline: "", byline: "", body: "", tone: "", outletId: "", outletName: "",
  tags: [], partyId: "", districtId: "", eventId: "",
};

export const EMPTY_FLAVOR_STATUS: Agora.FlavorStatus = {
  lastFlavorDate: "", lastAttemptDate: "", isStale: false, providerAvailable: false,
  pendingWake: false, lastError: "", articleCount: 0,
};

// -- agora.state: dashboard chrome --------------------------------------------------------------

/** Master toggle. False means the player sees no trace of the mod — render null, not a shell. */
export const enabled$ = bindValue<boolean>("agora.state", "enabled", false);

/** True once the engine has published a political state at least once. */
export const ready$ = bindValue<boolean>("agora.state", "ready", false);

// -- agora.parties / agora.districts: shared lookup tables --------------------------------------

/**
 * Party names and colours are resolved here and nowhere else. Every other payload carries a
 * partyId only, which is what stops one party rendering in two colours across two panels.
 */
export const roster$ = bindValue<Agora.PartyBrief[]>("agora.parties", "roster", []);

/** Read only to turn a districtId into a district name. */
export const districts$ = bindValue<Agora.DistrictBrief[]>("agora.districts", "list", []);

// -- agora.news ---------------------------------------------------------------------------------

/** Sorted date DESC then id ASC in C#. Never re-sorted here (contract rule 7). */
export const feed$ = bindValue<Agora.NewsHeadline[]>("agora.news", "feed", []);

/** Sorted firedDate DESC then id ASC in C#. */
export const events$ = bindValue<Agora.TimelineEventBrief[]>("agora.news", "events", []);

/** Sorted by status rank, then deadlineDate, then id — so the tracker opens on what is live. */
export const mandates$ = bindValue<Agora.MandateRow[]>("agora.news", "mandates", []);

/** LLM health. Republished on every attempt, success or failure. */
export const flavorStatus$ = bindValue<Agora.FlavorStatus>(
  "agora.news", "flavorStatus", EMPTY_FLAVOR_STATUS,
);

/**
 * Prose bodies deliberately do not ride in the feed payload. A body is fetched per id, only
 * when the reader opens that item — see ArticleReader.
 */
export const article$ = bindMap<string, Agora.NewsArticle>("agora.news", "article");

/**
 * The manual LLM wake. It REQUESTS; the engine decides. A failed wake keeps the last good
 * flavor by design (non-negotiable 7), so the panel must not assume the feed changes.
 */
export const wakeFlavor = bindTrigger("agora.news", "wakeFlavor");
