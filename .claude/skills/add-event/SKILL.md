---
name: add-event
description: Add a historical or procedural event to Agora's timeline catalogs — entry shape, schema validation, effect ID checking, headline brief. Use when authoring the 1990-onward history.
---

# /add-event

Events are content. They live in `data/timeline_eu.json`, `timeline_na.json`, `timeline_global.json`
and carry no logic — the scheduler and the effect palette do the work.

## Entry shape

```json
{
  "id": "gfc-2008-collapse",
  "dateISO": "2008-09-15",
  "region": "global",
  "title": "Global financial crisis",
  "severity": 4,
  "durationMonths": 24,
  "effects": [
    { "effectId": "loan-interest-spike", "scope": "city", "magnitude": 0.35, "durationMonths": 18 }
  ],
  "headlineBrief": "Credit markets seize; lending costs spike and construction stalls.",
  "tags": ["economy", "finance", "recession"]
}
```

## Rules

1. **Every `effectId` must exist in the palette registry** and every magnitude must sit inside that
   effect's cap. The schema suite fails the build otherwise — that check is the point.
2. **`headlineBrief` is a prompt input, not published prose.** Keep it factual and terse. The LLM
   writes the article; this tells it what happened. Writing it as finished copy produces articles
   that read like paraphrases of themselves.
3. **Severity 1–5, and be conservative.** Severity drives effect scaling, so a catalog where
   everything is a 4 flattens into no dynamic range at all. Severity 5 should feel rare.
4. **Region routing:** `global` fires everywhere; `eu` and `na` fire only under the matching theme.
   An event with a genuinely different local meaning gets two entries, not one hedged entry.
5. **Real dates.** Whether the scheduler jitters them is an open decision (§14) and not the
   author's call — put the true date in.

## Local angle

The catalog states what happened in the world. How it lands in *this* city is the engine's job
(which districts, which blocs) and the LLM's (`eventProse[].localAngle`). Do not pre-write the local
angle into `headlineBrief` — it is different in every save.

## Coverage

M5 targets 80–120 events, 1990→2025. Track coverage by decade and by theme in `docs/status.md`; the
common failure is a dense 2008–2016 and a thin 1990s.
