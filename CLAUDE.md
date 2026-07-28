# AGORA — Political layer for Cities: Skylines II

A deterministic C# political engine, LLM-authored flavor prose, and a Gameface dashboard, layered on
the player's city. Canonical design doc: `politicsmodplan.md` — do not re-litigate ratified decisions.

## Non-negotiables (Reviewer blocks violations)

1. **LLM is flavor-only.** No number entering engine state may originate from Claude output.
2. **No naked randomness.** Every draw uses a named seeded stream: `Hash(saveGuid, simDate, streamName)`.
3. **Determinism.** Engine state is a pure function of (metrics history, prior state, seeds, catalogs, settings).
4. **No map mutation.** Never create or modify districts, zoning, buildings, or terrain.
5. **Effects are capped.** Every effect declares scope, magnitude cap, duration cap, and a fallback.
6. **Sidecar integrity.** Atomic writes (temp file + rename). Load must never desync.
7. **Fail closed on LLM.** Missing CLI, timeout, or bad JSON → keep last good flavor, log, continue.
8. **Clock unity.** `AgoraTimeService` is the only source of dates. Nothing else computes years.
9. **Schema versioning.** Every contract carries `schemaVersion`; changes go through `/schema-change`.
10. **English only. Per-save settings**, stored in the sidecar — not global config.

## Routing — read only what your task needs

| Task | Read |
|---|---|
| Engine, voter model, elections, events | `src/CLAUDE.md` + `src/Agora.Core/CLAUDE.md` |
| Sensors, ECS queries, game glue | `src/CLAUDE.md` + `src/Agora.Mod/CLAUDE.md` + newest `docs/scout/` |
| Effects | `src/Agora.Mod/CLAUDE.md` + `politicsmodplan.md` §7 |
| Dashboard UI | `ui/CLAUDE.md` + `docs/contracts/ui_bindings.md` |
| Schemas, catalogs, tuning | `data/CLAUDE.md` + `politicsmodplan.md` §6 |
| Tests | `tests/CLAUDE.md` |
| Milestone or status questions | `docs/status.md` |

## Hard boundaries

- **`Agora.Core` may never reference `Game.*`, `Colossal.*`, or `Unity.*`.** That split is what makes the
  test suite runnable without the game. Breaking it is a review-blocking defect.
- **Do not read `refsrc/` in full** — it is a multi-hundred-MB decompiled reference tree. Grep it.
- Build: `dotnet build Agora.sln` · UI: `cd ui && npm run build`
- Test: `dotnet test tests\Agora.Core.Tests\Agora.Core.Tests.csproj` — the project path, not the
  solution. `Agora.Mod` needs the game installed; this suite must not.
