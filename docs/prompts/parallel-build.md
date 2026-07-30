# Prompt — parallel build pass (M1 → M5)

Paste everything below the line into a fresh Claude Code session opened at the repo root.

---

ultracode

Build as much of AGORA as can be **proven correct without launching the game**, using a workflow with
as many parallel subagents as the dependency graph honestly allows. Repo is at `main`, clean, M0
complete and verified in-game.

## Read first, in this order

1. `CLAUDE.md` — the router and the ten non-negotiables. These are design law; the Reviewer blocks
   violations. They override anything you would otherwise consider a sensible default.
2. `politicsmodplan.md` — canonical plan. §2 law, §4 architecture, §5 persistence, §6 contracts,
   §7 effect palette, §11 milestones, §13 risks, §14 open decisions.
3. `docs/status.md` — what is actually done versus claimed.
4. `docs/scout/0001-api-index.md` and `0002-modding-toolchain.md` — verified API and build surface.

**Do not re-litigate ratified decisions** (§3, and the "Closed" lists in §14). If you think one is
wrong, say so in your final report and proceed as ratified anyway.

## What already exists — do not rebuild it

- `Agora.Core` (netstandard2.0, zero game references): `Determinism/` (`SeedStreams`,
  `DeterministicRng` xoshiro256\*\*, 12 stream names), `Contracts/` (`SimDate`, `IClock`,
  `ISnapshotSource`, `IEffectSink`, `IFlavorProvider`, `EffectRequest`, `FlavorPayload`).
- `Agora.Mod` (net48 via toolchain): `IMod` entry, settings + localization, day heartbeat,
  `AgoraTimeService` stub, one `UISystemBase` publishing three bindings.
- `tests/Agora.Core.Tests`: 22 passing, including a **golden-value test pinning seed derivation**.
- `ui/`: React panel, real build config, bundle deploys.
- `tools/`: `verify-setup.ps1`, `api-query.ps1`, `decompile.ps1`.

## Hard constraints — a violation of any of these is a failed task

- **`Agora.Core` may never reference `Game.*`, `Colossal.*`, `Unity.*`, `UnityEngine.*`.** This is what
  lets the suite run with no game installed. It is the single most important rule in the repo.
- **`Agora.Core` is netstandard2.0.** No `Span<T>`, `MathF`, `Math.Clamp`, `HashCode.Combine`, ranges,
  or default interface members. Polyfill privately inside Core. Raising the target breaks the
  toolchain build with `NU1201`, and the error surfaces in `Agora.Mod`, far from the cause.
- **No naked randomness.** Every draw goes through `SeedStreams` with a named stream. No
  `System.Random`, no `Guid.NewGuid()`, no `DateTime.Now`, no `string.GetHashCode()` (randomised per
  process — it would make one save produce different politics every launch).
- **No dictionary/HashSet iteration order dependence** in anything affecting engine state. Sort
  explicitly. This is the most common way determinism dies quietly.
- **No LLM-derived numbers.** `IFlavorProvider` returns prose and IDs only.
- **Every effect declares scope, magnitude cap, duration cap, and a fallback**, and ships with a test
  proving the cap holds.
- **No map mutation**, ever.
- **Do not implement anything in §14 Open Decisions.** Leave a clearly marked seam instead.
- **Verify before you assume.** `.\tools\api-query.ps1 -Members <Type> -Public`,
  `-Enum <Type>`, `-Implements <Type>`. It reads the shipped assemblies via Cecil — no decompiler.
  Only grep `refsrc/` when you need a method *body*. **Never read `refsrc/` in full.**

## Build and test

```
dotnet build Agora.sln
dotnet test tests\Agora.Core.Tests\Agora.Core.Tests.csproj
```

Test **by project path, not by solution** — `dotnet test Agora.sln` drags in `Agora.Mod`, which needs
the game, and destroys the guarantee the suite exists to provide.

Build gotchas that have already bitten this repo:
- `BuildUI` must stay `AfterTargets="DeployWIP"`. `DeployWIP` wipes the deploy folder and webpack
  writes into it. Reordering silently produces a mod with no interface.
- **Any target whose `Condition` can be false must `<Warning>` when it skips.** Two silent-skip bugs
  have already shipped green here.

## Phasing — this is the part that matters

The work is **not** uniformly parallel. Fan out only where the graph allows.

### Phase 1 — SERIAL, exactly one agent. Freeze the contracts.

Nothing else can start until this lands, because ten agents inventing their own `PartyId` type
produces ten things that do not compile together.

Define, in `Agora.Core/Contracts/`, with XML docs and `schemaVersion` where §2.9 applies:
`CitySnapshot`, `DistrictSnapshot` (per-district fields best-effort + `hasCityFallbacks` per §6),
`Bloc` (wealth × education × age), `Party`, `Faction`, `IssuePosition`, `IssueWeights`, `PollResult`,
`ElectionResult`, `SeatAllocation`, `Coalition`, `Mandate`, `TimelineEvent`, `PoliticalState`.

Also: the `data/engine_tuning.json` key set (every coefficient the engine will read — **no tuning
constant may be hardcoded**), and `data/schemas/` updates.

Output a written contract summary the later phases are told to code against.

### Phase 2 — WIDE PARALLEL. Pure engine, in `Agora.Core`. No game needed, fully testable.

This is the bulk of the mod and the whole reason the Core/Mod split exists. Give each packet
**exclusive file ownership** so agents never edit the same file:

| # | Packet | Owns |
|---|---|---|
| 1 | Bloc construction + issue weights from lived metrics | `Engine/Blocs/` |
| 2 | Party registry + EU lifecycle (split/merge/die <3%×2/revive) | `Engine/Parties/` |
| 3 | Faction model + NA dominance & platform authorship | `Engine/Factions/` |
| 4 | Affinity model (weighted dot product + incumbency + mandate + events + seeded noise) | `Engine/Affinity/` |
| 5 | Turnout model (happiness × education weighted) | `Engine/Turnout/` |
| 6 | Polls with **turnout- and education-weighted error** (under-sample low-education districts) | `Engine/Polling/` |
| 7 | PR seat allocation (EU) | `Engine/Elections/Proportional/` |
| 8 | FPTP districts + directly elected mayor (NA) | `Engine/Elections/Fptp/` |
| 9 | Coalition formation and collapse → snap elections | `Engine/Government/Coalitions/` |
| 10 | Mandate generation, monthly monitoring, resolution | `Engine/Government/Mandates/` |
| 11 | Timeline catalog loader + validator (every `effectId` in the palette; magnitudes within caps) | `Events/Catalog/` |
| 12 | Deterministic event scheduler + procedural post-catalog generator | `Events/Scheduler/` |
| 13 | Derived indices (Gini, gentrification, brain drain, commute misery, service inequality) | `Engine/Indices/` |
| 14 | Effect palette registry, Core side — IDs, scopes, caps, fallback chains | `Engine/Effects/` |

Every packet ships with tests in `tests/Agora.Core.Tests/`, in its **own file**, following
`/write-test`: same seed twice → identical output, plus behavioural assertions (poll error runs in the
correct direction; a party under 3% twice dies; a cap actually clamps).

### Phase 3 — PARALLEL, data authoring. Also pure.

| # | Packet | Owns |
|---|---|---|
| 15 | `timeline_eu.json` — reunification, Maastricht, euro cash, enlargement, eurozone austerity, 2015 migration, Brexit | `data/timeline_eu.json` |
| 16 | `timeline_na.json` — NAFTA, dot-com, 9/11, shale boom, 2016 populism, tariffs | `data/timeline_na.json` |
| 17 | `timeline_global.json` — Gulf War, GFC, COVID, supply chain, Ukraine energy, inflation/rates, AI boom, 2025 Iran oil shock | `data/timeline_global.json` |

Target 80–120 events total, 1990 → mid-2020s. Every `effectId` must exist in the Phase 2 packet-14
registry. Schema-validate. `headlineBrief` is a *brief for Claude*, not finished prose.

### Phase 4 — PARALLEL, `Agora.Mod` glue. Compiles and unit-tests, but **cannot be verified in-game**.

| # | Packet | Owns |
|---|---|---|
| 18 | `AgoraTimeService` + start-year delivery. **Test `TimeSystem.startingYear`'s public setter FIRST** — if date surfaces derive from it, no Harmony is needed and §13.2 dies. Only if it fails: enumerate every date surface, then patch behind a kill-switch. | `Agora.Mod/Time/` |
| 19 | Sensor pass 1 → `CitySnapshot`. One `GameSystemBase` per metric family, queries built in `OnCreate`, per-district best-effort with city fallback. | `Agora.Mod/Sensors/` |
| 20 | Sidecar IO: atomic temp-file+rename, **self-owned save GUID** via `IPreSerialize`/`IPostDeserialize`, load reconciliation that never crashes and never resets politics. | `Agora.Mod/Persistence/` |
| 21 | `ClaudeCliProvider` — process spawn, timeout, JSON parse, schema validation, retry-once, **fail closed to last good flavor**. Plus `StaticPoolProvider` stub. | `Agora.Mod/Llm/` |
| 22 | Effect application — Core `EffectRequest` → `DistrictModifierType` / `CityModifierType`. §7 is the closed registry; anything unmapped is reported, not invented. | `Agora.Mod/Effects/` |

### Phase 5 — PARALLEL, `ui/`. Flexbox only — **Gameface has no CSS grid.**

| # | Packet | Owns |
|---|---|---|
| 23 | Seat chart + government breakdown | `ui/src/panels/Seats/` |
| 24 | Per-district vote splits + wealth × education crosstabs | `ui/src/panels/Districts/` |
| 25 | News feed + mandate tracker | `ui/src/panels/News/` |

**Every binding must be registered in `docs/contracts/ui_bindings.md` before it is implemented.** That
contract spans two languages and two build systems; nothing checks it at compile time, and a renamed
binding yields an empty panel at runtime rather than a build error.

### Phase 6 — SERIAL. Integrate and prove.

Full build both modes (`dotnet build Agora.sln`, then `-p:UseCsiiToolchain=false`), full test suite,
then run the `review-checklist` skill over the diff as an adversarial pass. Update `docs/status.md`
honestly — **separate what is proven from what merely compiles.** Write `docs/scout/0003-*.md` for
anything newly discovered about the game API.

## Reporting

State plainly, at the end:
- what is **proven** (built + tested), versus what merely compiles, versus what is stubbed;
- every §14 open decision you hit, and the seam you left;
- anything where you could not verify a game API and guessed.

**Do not report M1–M5 as complete.** None of their gates can be met without a human loading a city.
The honest ceiling for this pass is: the entire pure engine built and tested, the glue compiled, the
UI bundled — with in-game verification outstanding.
