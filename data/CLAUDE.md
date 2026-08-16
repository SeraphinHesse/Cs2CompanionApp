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
2. **Effect IDs are validated.** Every `effectId` in a timeline entry must exist in the effect palette
   registry, and every magnitude must sit within that effect's declared cap. The schema suite fails
   the build otherwise.
3. **No numbers in flavor.** `politics_flavor.json` carries prose, IDs, and dates only. A numeric field
   anywhere else in it is a schema violation and a review-blocking defect (non-negotiable #1).
4. **No tuning constants in C#.** If you catch yourself typing a magic number into the engine, it
   belongs in `engine_tuning.json` and arrives as a parameter.
5. **English only.**

## Authoring timeline events

Entries follow `politicsmodplan.md` §6. Use `/add-event`. Keep `headlineBrief` factual and terse —
it is a *prompt input* for the LLM, not published prose. Severity is 1–5 and drives effect scaling;
be conservative, since severity 5 should feel rare.
