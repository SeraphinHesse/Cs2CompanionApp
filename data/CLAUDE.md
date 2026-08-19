# data/ — schemas, catalogs, tuning

Everything here is content, not code. Nothing in this folder may contain logic, and nothing outside
it may hardcode a tuning constant.

## Files

- `schemas/` — JSON Schema for every contract: `snapshot`, `politics_flavor`, `timeline`, sidecar state.
- `timeline_eu.json`, `timeline_na.json`, `timeline_global.json` — curated real-history catalogs, 1990→.
- `events_global.json`, `events_eu.json`, `events_na.json` — authored **civic events**: the unit a
  political story is assembled from, triggered by the state of the city rather than by a date. Use
  `/add-event`, and read `CivicEventCatalogLoader`'s remarks first — it rejects several shapes that
  look like working goals and are not.
- `timeline_adaptation.json` — which timeline events become civic events, and how. Marking one `none`
  stops it becoming a story; it keeps firing as a timeline event either way.
- `engine_tuning.json` — every coefficient the engine uses. If a number affects behaviour, it lives here.
- `seeds/` — name pools, faction archetypes, outlet names. Fallback flavor when the LLM is unavailable.

## Rules

1. **`schemaVersion` on every file.** Changing a shape means bumping it and running `/schema-change`,
   which also writes the sidecar migration and syncs both sides (C# structs *and* the LLM prompt).
2. **Effect IDs are validated.** Every `effectId` in a timeline *or* civic entry must exist in the
   effect palette registry, and every magnitude must sit within that effect's declared cap. The
   schema suite fails the build otherwise. JSON Schema cannot make that check — it is a cross-file
   one — which is why the shipped-catalog tests exist and why deleting one is never the fix.
3. **No numbers in flavor.** `politics_flavor.json` carries prose, IDs, and dates only. A numeric field
   anywhere else in it is a schema violation and a review-blocking defect (non-negotiable #1).
4. **No tuning constants in C#.** If you catch yourself typing a magic number into the engine, it
   belongs in `engine_tuning.json` and arrives as a parameter.
5. **English only.**

## Authoring events — two kinds, one skill

`/add-event` covers both halves and states which rules belong to which. Read it before touching
either catalog; the two look alike and are not the same job.

- **Timeline events** (`timeline_*.json`, `politicsmodplan.md` §6) fire on a **date**.
  `headlineBrief` is a *prompt input* for the LLM, not published prose — keep it factual and terse,
  or the article reads like a paraphrase of itself.
- **Civic events** (`events_*.json`, `politicsmodplan.md` §15) fire on the **state of the city** and
  their **seven prose fields are published to the player verbatim**. That is why the loader enforces
  six rules a schema cannot express — `CatalogIssueCode` 116–121 — and why the shipped-catalog gate
  holds these files to **zero warnings**, not merely zero errors.

Severity is 1–5 in both and drives effect scaling; be conservative, since severity 5 should feel rare.

**The rule that outranks the rest: an event's prose may only claim what its effect ids can actually
do.** The palette is closed (§7), so a headline promising deaths, a tourism boom or a cut to the
prison budget is contradicted by the player's own city within the month — the mod telling the player
something false about the simulation it is running. `/add-event` Part C carries the specific traps;
none of them is guessable from the effect id's name, which is exactly why they are written down.

Two numbers no authoring file records and every threshold depends on: **a story lives one month, not
`stories.cycleMonths`**, and `serviceCoverage` tops out at **5/9 ≈ 0.5556** with `pollution` at
**0.75**.
