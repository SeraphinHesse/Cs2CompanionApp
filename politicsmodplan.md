# AGORA — Political Layer for Cities: Skylines II
## Project Plan for Claude Code Agents

This file is the canonical plan. The root `CLAUDE.md` routes to per-folder context; this file holds mission, law, architecture, and milestones. Read the section relevant to your current milestone. Do not re-litigate ratified decisions.

> **Revision, 2026-07-28.** Refined against the shipped game assemblies. Sections marked ⟐ changed as a result of Scout report `docs/scout/0001-api-index.md`. The original plan was written without access to the real modding surface; where it guessed, this revision replaces the guess with a verified fact or an explicit open question.
>
> **Revision, 2026-07-30.** M0 is complete and Agora has loaded in-game. Sections marked ⟐⟐ changed as a result of Scout report `docs/scout/0002-modding-toolchain.md`, written after the official modding toolchain was installed. The build now runs through the toolchain's own `Mod.props` / `Mod.targets`, which forced a target-framework change through the whole solution. Where this revision contradicts the 2026-07-28 one, it wins.

---

## 1. Mission

Build a CS2 mod that adds a living political layer on top of the player's city:

- A **deterministic C# political engine** computes parties, factions, vote shares, polls, elections, coalitions, mandates, and events from real city data.
- **Claude (headless CLI)** adds flavor only: names, party/faction descriptions, news articles, event prose. It never computes numbers that affect state.
- A **dashboard UI** renders it: seat charts, per-district vote splits with wealth/education crosstabs, news feed, government breakdown. Later: a political map overlay and an election-night mode.
- The world runs on **real history from 1990 onward**, curated into event catalogs, with sanctioned gameplay effects.

The player is sovereign: never removable, never overridden. Politics applies pressure (happiness, sanctioned effects), never control.

---

## 2. Non-Negotiables (design law — Reviewer blocks violations)

1. **LLM is flavor-only.** No number that enters engine state may originate from Claude output. Claude output is prose fields validated against schema; anything else is discarded.
2. **No naked randomness.** Every stochastic draw uses a named, seeded stream: `seed = Hash(saveGuid, simDate, streamName)`. `System.Random` without a derived seed is a review-blocking defect.
3. **Determinism.** Engine state is a pure function of (city metrics history, prior state, seeds, catalogs, settings). Reloading a save and playing identically must reproduce identical political outcomes. ⟐ **"Desync" is defined precisely:** the SHA-256 of serialized Agora state at sim-date D after a reload must equal the hash taken before it. Vague gates never fail, so they never catch anything.
4. **No map mutation.** Effects never create/modify districts, zoning, buildings, or terrain. The sanctioned effect palette (§7) is a closed registry; events reference effects by ID only.
5. **Effects are capped.** Every effect declares scope (city|district), magnitude cap, duration cap, and a fallback effect (default: happiness modifier). Uncapped effects do not merge.
6. **Sidecar integrity.** Political state writes are atomic (temp file + rename). A snapshot is written on every save-complete callback. Load must never desync (§5).
7. **Fail closed on LLM.** Missing CLI, timeout, malformed JSON → keep last good flavor state, log, continue. The engine never waits on Claude.
8. **Clock unity.** `AgoraTimeService` is the single source of truth for dates. The political calendar and the patched game clock read from it; nothing else computes years.
9. **Schema versioning.** `snapshot.json`, `politics.json`, sidecar state, and catalogs carry `schemaVersion`. Changes go through the `/schema-change` workflow with migration.
10. **English only. Per-save settings.** All configuration lives in the sidecar state, not global config (exception: the master toggle and other settings that must work before a save exists).

---

## 3. Ratified Design Decisions

- **Runtime:** personal Claude CLI (headless `claude -p --output-format json`). Portfolio project; long-term (post-v3) LLM is phased toward pregenerated content pools — keep the flavor provider behind an interface (`IFlavorProvider`) from day one.
- **Cadence:** Claude wakes (a) yearly, (b) at each election, (c) on manual trigger from the dashboard (spawns fresh articles + a random event roll). Cadence configurable per save.
- **Start year:** default 1990, configurable at save creation, locked afterward. The game clock is patched to display it (mandatory, not optional). ⟐ See §11 M1 — this may not require Harmony at all.
- **History:** full real history, 1990 → mid-2020s including the 2025 Iran conflict oil shock. Target 80–120 events across `timeline_eu.json`, `timeline_na.json`, `timeline_global.json`. After the catalog ends: procedural mode (engine picks seeded archetypes, Claude writes prose).
- **Systems:** EU theme = proportional, 4–7 parties, coalitions, 1-year terms. NA theme = FPTP, two dominant parties + weak third parties, directly elected mayor, 1-year terms. Theme follows the map theme choice; overridable in settings. *(Term lengths amended from 3/4 years to 1 year; the "weak third parties" clause is enforced by the `fringe` packet — see §6.)*
- **Campaigns:** final 6 months of each term. Weekly published polls with **turnout- and education-weighted error** (polls under-sample low-education, low-turnout districts). Turnout scales with happiness and education and can flip close races. Manifestos refresh against current grievances each campaign.
- **Party lifecycle:** EU parties split / merge / die (<3% two consecutive elections) / revive (dead brand returns if its core grievance resurges). NA: party-level lifecycle events are possible but extremely unlikely; instead each party contains 2–4 **factions** with their own demographic support, demands, and leaders — faction split/merge/die/revive is common, and the dominant faction writes the party platform each cycle.
- **Mandates:** winning platform generates measurable mandates from real deficits ("cut District X ground pollution 20% in 2 years"). Engine monitors actual game state monthly. Fulfilled → happiness up + governing credit; defied → happiness down + opposition surge + possible unrest event. Player is never punished beyond sanctioned effects.
- **Save-scumming is supported and expected.** Determinism (§2.2–2.3) makes replays converge. Only Claude prose may differ across replays.
- **Persistence:** sidecar (§5). Revisit save-embedding only if the mod is ever published broadly.
- **Election night:** placeholder results screen until M6; broadcast-style live count is late scope.
- **Excluded forever:** auto-creating districts or zoning (player choice), player removal, uncapped effects.

### ⟐ Ratified 2026-07-28

- **Repository:** `Cs2CompanionApp` (existing repo, reused). Not a new sibling repo.
- **Assembly split:** two projects — `Agora.Core` (pure C#, zero game references) and `Agora.Mod` (game glue). This is what makes §12's test strategy executable; `Game.dll` cannot be loaded outside the Unity runtime, so an engine that references it has nowhere to run its tests. Breaking the split is a review-blocking defect.
- **Toolchain:** the official in-game modding toolchain is the supported path. Until it is installed, `Agora.Mod` references the game's `Managed` assemblies directly and both approaches coexist in one csproj.
- **Clock patch mitigation:** the patch remains mandatory (see M1), but ships behind a kill-switch and is gated on a complete date-surface enumeration.

### ⟐⟐ Ratified 2026-07-30 (toolchain installed)

- **Toolchain mode is the build.** `Agora.Mod.csproj` imports the toolchain's `Mod.props` / `Mod.targets` when `CSII_TOOLPATH\Mod.props` exists, and falls back to direct `Managed` references otherwise (`-p:UseCsiiToolchain=false`). Toolchain mode is what M1 onward develops against, because the **Unity.Entities source generators arrive only through it** — `SystemAPI` and `IJobEntity` do not compile without them. Fallback mode is a compile check for contributors without the game, not a development environment.
- **Target frameworks, forced by the above.** `Mod.props` sets `net48`. .NET Framework cannot reference netstandard2.1, so **`Agora.Core` is pinned to netstandard2.0**. No `Span<T>`, `MathF`, `Math.Clamp`, `HashCode.Combine`, or default interface members in Core — polyfill inside Core rather than raising the target, because raising it fails with `NU1201` in `Agora.Mod`, far from the cause.
- **Deploy identity.** The toolchain deploys to `$(LocalModsPath)\$(TargetName)`, i.e. `…\Mods\Agora.Mod\`. That name, the assembly name, and `ui/mod.json`'s `id` are the same string by necessity, not by convention.
- **The UI template ships with the game**, at `…\Content\Game\.ModdingToolchain\npx-create-csii-ui-mod\template`. `npx create-csii-ui-mod` only copies that folder. Diff against it after a game update rather than regenerating.
- **`refsrc/` is generated by `tools/decompile.ps1`** (5,209 `.cs` files). `tools/api-query.ps1` reads type/member/enum metadata with no decompiler at all, because `Colossal.Mono.Cecil.dll` ships with the game. **Reach for `api-query` first; drop to `refsrc/` only for method bodies.**

---

## 4. Architecture

### 4.1 The loop

```
[Sensors (ECS queries)] → snapshot.json (monthly)
        ↓
[Engine tick (monthly)] — voter model, polls, mandates, event scheduler
        ↓ (yearly / election / manual)
[LLM wake] claude -p  ← prompt = plan excerpt + sidecar state + snapshot
        ↓
politics_flavor.json — schema-validated prose only
        ↓
[UI] dashboard renders engine state + flavor   [Effects] apply sanctioned modifiers
```

### 4.2 ⟐ Components (two assemblies)

**`src/Agora.Core/` — pure C#, no game references, ⟐⟐ netstandard2.0**

- **Contracts/** — the boundary types: `SimDate`, `CitySnapshot`, `DistrictSnapshot`, `EffectRequest`, `FlavorPayload`, and the interfaces `IClock`, `ISnapshotSource`, `IEffectSink`, `IFlavorProvider`.
- **Determinism/** — `SeedStreams` (named seeded streams), `DeterministicRng` (xoshiro256\*\*, fixed algorithm), `StreamNames`.
- **Engine/** — voter model, party & faction registry, lifecycle rules, polls (weighted error), turnout, elections (PR seat allocation / FPTP district races + mayor), coalition formation & collapse, mandates, derived indices.
- **Events/** — timeline catalog loader/validator, deterministic scheduler, procedural post-catalog generator.

**`src/Agora.Mod/` — game glue, references `Game.dll` et al, ⟐⟐ net48 (toolchain) / netstandard2.1 (fallback)**

- **Mod.cs** — `Game.Modding.IMod` entry point.
- **Core/** — settings (`ModSetting` + `IDictionarySource`), logging, mod lifecycle.
- **Time/** — `AgoraTimeService`, the single date authority; the clock patch.
- **Sensors/** — one `GameSystemBase` per metric family. ECS queries → contract structs.
- **Effects/** — sanctioned palette implementations, each with caps + fallback, registered by ID.
- **Persistence/** — sidecar IO, save GUID component, load reconciliation.
- **Llm/** — `ClaudeCliProvider` (process spawn, timeout, JSON parse, schema validation, retry-once); `StaticPoolProvider` stub for the post-v3 future; prompt assembly.
- **UiBindings/** — `UISystemBase` subclasses publishing bindings to `ui/`.

**`ui/` — React + TypeScript on Coherent Gameface, separate npm build.** ⟐ Not optional and not a subfolder of `src/`: the CS2 interface is an embedded browser, so the dashboard is a real web app with its own toolchain, linked to the code mod by `mod.json`'s `id`.

⟐⟐ **The two builds share one output folder, and the order matters.** webpack writes straight to `…\Mods\Agora.Mod\`; the toolchain's `DeployWIP` target begins by `RemoveDir`-ing that same folder. A C# build therefore deletes the UI bundle. `Agora.Mod.csproj` runs its `BuildUI` target `AfterTargets="DeployWIP"` so one `dotnet build Agora.sln` produces both halves, and a sibling target warns aloud when `ui/node_modules` is missing rather than skipping in silence. Never reorder these. The failure is invisible: the mod loads, the panel is simply absent, and nothing logs an error.

### 4.3 Voter model (M2 spec sketch)

- Citizens aggregate into **blocs** per district: (wealth tier × education tier × age band).
- Each bloc holds **issue weights** (services, cost of living, environment, transit, growth, heritage/order) derived from its composition and updated by lived metrics (e.g., high commute time raises transit weight for commuting blocs).
- Parties/factions hold **issue positions**. Affinity = weighted dot product + incumbency term + mandate performance term + event modifiers + seeded noise stream.
- Vote share per district = turnout-weighted bloc affinities, normalized. City result = aggregation per electoral rules.
- All coefficients live in `data/engine_tuning.json` — never hardcode tuning constants.

⟐ **Confirmed available:** `Game.Citizens.CitizenAge`, `CitizenEducationLevel`, `CitizenHappiness` are real components, so the age × education axes are directly buildable. **Open:** the exact household wealth field, and whether citizens resolve to districts reliably (Scout 0002 questions 3–4).

---

## 5. Persistence & Determinism

### Sidecar layout

```
ModsData/Agora/<saveGuid>/
  state_<simYear>_<simMonth>.json   # full political state at each save point
  timeline_progress.json            # fired event IDs
  settings.json                     # per-save settings
  flavor_cache.json                 # last good Claude prose
  metric_history.json               # sensor memory for the rent and land-value trends
```

### Rules

- ⟐ **Agora owns the save identity.** Do not assume the engine exposes a stable save GUID. Instead write a GUID into the save itself via the serialization hooks (`Game.Serialization.IPreSerialize` / `IPostDeserialize`, both confirmed present) and key the sidecar on that. It survives renames and copies, it cannot collide with a filename, and it retires risk §13.1 outright.
- Write `state_*.json` inside the save callback, atomically (temp file + rename). ⟐ `GameSystemBase` exposes `OnGameLoaded(Context)` and `OnGameLoadingComplete(Purpose, GameMode)`; the equivalent post-*save* hook still needs confirmation (Scout 0002 question 2). If no post-save hook exists, serialize at save-start with the sim paused.
- On load: match GUID + sim date exactly → load that snapshot. Missing exact match → reconcile: nearest earlier snapshot + fast-forward the engine deterministically using current city state; log a warning; never crash, never reset politics.
- Retention: prune to last N snapshots per save (default 25, setting). (Pending confirmation.)
- Sim-date-seeded streams guarantee reloaded replays converge (§2.2).

---

## 6. Data Contracts (schemas live in `data/schemas/`)

### snapshot.json (engine → LLM, also debugging artifact)
City block + `districts[]`, each district: id, name, population, wealth distribution, education distribution, age bands, happiness, unemployment, rent/land value + trend, pollution (air/ground/noise/water), crime, health, service coverage set, transit metrics, and v2/v3 sensor fields as milestones land. Plus derived indices (Gini, gentrification per district, brain drain, commute misery, service inequality). Plus active policies, tax rates, budget balance, debt, recent disasters, in-progress mandates.

⟐ Per-district fields are **best-effort**: some metrics exist only city-wide. A sensor that cannot resolve a district value falls back to the city value and sets `hasCityFallbacks`, so the dashboard never presents a city number as a local fact.

### politics_flavor.json (LLM → game; prose only)
`schemaVersion, generatedAtSimDate, partyFlavor[{partyId, name, shortName, description, slogan}], factionFlavor[...], articles[{id, outlet, headline, body(≤120 words), tone, refs{eventId?, districtId?, partyId?}}], eventProse[{eventId, localAngle}]`. Reviewer rule: any numeric field here beyond IDs/dates is a schema violation.

### timeline_*.json (curated catalogs)
`events[{id, dateISO, region: eu|na|global, title, severity: 1..5, durationMonths, effects[{effectId, scope, magnitude, durationMonths}], headlineBrief, tags[]}]`. Validation: every `effectId` must exist in the palette registry; magnitudes within caps.

### ⟐ ui_bindings (C# ↔ JS) — `docs/contracts/ui_bindings.md`
The fourth contract, and the one most likely to drift silently: it spans two languages and two build systems, so nothing checks it at compile time. A renamed binding produces an empty panel at runtime, not a build error. Every binding is registered in that file before it is implemented, with its group, name, C# type, publisher and consumer.

---

## 7. ⟐ Sanctioned Effect Palette (closed registry)

**This section was rebuilt against the game's own modifier enums.** `Game.Areas.DistrictModifierType` (14 members) and `Game.City.CityModifierType` (40 members) are first-class, already capped, already serialized, and already respected by the simulation. Building the palette on them instead of inventing one means most effects need no Harmony at all. Full enum listings are in `docs/scout/0001-api-index.md` §3.

**Directly supported by an enum member — implement these first.**
Crime (`CrimeAccumulation` at both scopes, plus `CrimeProbability`, `CriminalMonitorProbability`, `CrimeResponseTime`, `PrisonTime`) · district unrest (`District.CrimeAccumulation` + `Wellbeing`, an almost exact fit for the design intent) · district happiness (`Wellbeing`) · health (`DiseaseProbability`, `PollutionHealthAffect`, `HospitalEfficiency`) · education (`CollegeGraduation`, `UniversityGraduation`, `UniversityInterest`) · garbage (`GarbageProduction`, `IndustrialGarbage`) · building upkeep (`BuildingUpkeep`, `CityServiceBuildingBaseUpkeepCost`) · loan interest (`LoanInterest`) · trade costs (`ImportCost`, `ExportCost`, `CityServiceImportCost`) · sector productivity for strikes (`IndustrialEfficiency`, `OfficeEfficiency`) · immigration and tourism attractiveness (`Attractiveness`, `Entertainment`, `ParkEntertainment`) · utility consumption (`EnergyConsumptionAwareness`, `ProductConsumption`) · industrial pollution (`IndustrialAirPollution`, `IndustrialGroundPollution`) · disasters (`DisasterWarningTime`, `DisasterDamageRate`) · transit and parking (`ParkingFee`, `TaxiStartingFee`) · tax sentiment (`TaxHappiness`, `LowCommercialTax`).

**No enum support — needs a decision before M5, not an assumption.**
RCI demand shifts (only the narrow `OfficeSoftwareDemand` / `IndustrialElectronicsDemand` exist) · rent and land value nudges · birth rate · one-off subsidies and fines (would require direct `PlayerMoney` manipulation) · pollution *decay* rate (the industrial members reduce production, not decay) · general transit fare income.

**The structural constraint.** District scope has 14 modifiers and **no pollution, no land value, no education, and no happiness beyond `Wellbeing`**; city scope has 40. Agora's whole premise is per-district politics, so the effect layer is most constrained at exactly the scope the design cares about most. M5 scoping must confront this before any effect is written: either district effects lean heavily on `Wellbeing` + `CrimeAccumulation`, or Harmony work is accepted for the rest.

**FORBIDDEN:** anything creating/modifying districts, zoning, buildings, terrain; anything uncapped; anything targeting the player's authority.

Each implementation ships with: scope, magnitude cap, duration cap, fallback effect, and a unit test proving the cap holds.

---

## 8. ⟐ Repository & Context Routing

```
Cs2CompanionApp/
├ CLAUDE.md                # ROUTER ONLY: mission, non-negotiables, routing table
├ politicsmodplan.md       # this file
├ Agora.sln
├ src/
│ ├ CLAUDE.md              # the Core/Mod boundary, conventions, build commands
│ ├ Agora.Core/            # PURE C# — no game references, ever
│ │ └ CLAUDE.md  Contracts/ Determinism/ Engine/ Events/
│ └ Agora.Mod/             # game glue
│   └ CLAUDE.md  Mod.cs Core/ Time/ Sensors/ Effects/ Persistence/ Llm/ UiBindings/
├ ui/                      # React+TS Gameface dashboard, own npm build
│ └ CLAUDE.md  README.md  mod.json  package.json  webpack.config.js
│   src/  types/           # types/ = the cs2/* .d.ts, from the shipped template
├ data/
│ └ CLAUDE.md  schemas/  timeline_*.json  engine_tuning.json  seeds/
├ tests/
│ └ CLAUDE.md  Agora.Core.Tests/
├ tools/                   # ⟐⟐ verify-setup.ps1  api-query.ps1  decompile.ps1
│                          #    (+ graphify port, owner: Serph, outstanding)
├ docs/
│ ├ status.md              # milestone tracker + manual gate checklists
│ ├ scout/                 # 0001-api-index.md, 0002-modding-toolchain.md
│ └ contracts/ui_bindings.md
└ refsrc/                  # gitignored: decompiled game source, grep-only
```

**Routing policy:** the root `CLAUDE.md` stays under ~40 lines. Every folder `CLAUDE.md` states: what lives here, local conventions, what to read next, what NOT to load. An agent tasked in `Agora.Core/` reads root + `src/CLAUDE.md` + `src/Agora.Core/CLAUDE.md` — nothing else.

⟐ **`refsrc/` is grep-only.** It is a multi-hundred-megabyte decompiled tree. Reading it wholesale burns a context window for nothing; searching it is what turns Scout's findings from guesses into facts.

---

## 9. Skills / Standardized Workflows

- `/add-sensor` — ECS query → metric struct → snapshot field → schema bump if needed → dashboard binding → test.
- `/add-effect` — map to an enum member (or justify Harmony) → implementation → caps + fallback → registry entry → cap test → doc line in palette.
- `/add-event` — catalog entry → schema validation → effect ID check → headlineBrief.
- `/schema-change` — version bump, migration for existing sidecars, contract doc update, both-sides (C# + prompt) sync.
- `/harmony-patch` — patch class conventions, prefix/postfix choice, target enumeration from scout report, leak checklist, uninstall safety.
- `/ui-component` — dashboard widget conventions, binding registration, flexbox-only styling.
- `/write-test` — determinism test pattern (same seed twice → identical output), simulation harness usage.
- `review-checklist` (skill, not command) — non-negotiables §2 as a checklist, run by Reviewer on every task.

---

## 10. Agent Organization

- **Master** — owns the current milestone. Dispatches subagents, merges approved work, updates `docs/status.md` and checks the milestone gate. Never writes feature code.
- **Scout** — runs FIRST each milestone. Verifies feasibility against the actual game assemblies. Output: dated report in `docs/scout/` with concrete type/member names. Planner may not assume any hook Scout hasn't confirmed.
- **Planner** — converts the milestone into tasks: each with acceptance criteria, file-level scope, and referenced scout findings. No task larger than one Coder session.
- **Coder** — implements exactly one task. Runs build + tests. Widening scope = stop and report back to Master.
- **Reviewer** — applies `review-checklist`. Special attention: naked RNG, LLM-derived numbers, uncapped effects, schema drift, patch leaks. Verdict: approve or block with required changes.

Loop: Scout → Plan → (Code → Review)\* → Master merges → gate check → next milestone.

---

## 11. Milestones & Gates

### M0 · Bootstrap ⟐⟐ — **COMPLETE 2026-07-30**
Tasks: repo scaffold; router + folder CLAUDE.mds; skills (§9); agent definitions; two-assembly build; `Agora.Core` determinism kernel + test suite; loadable mod with options page and per-day heartbeat; **UI pipeline smoke test** (one `GetterValueBinding` rendered by a React panel); Scout reports #1 and #2; `refsrc/` decompiled reference tree; `tools/` scripts; graphify port (Serph — still outstanding).

**Gate:** mod appears in game options, toggles cleanly on/off, logs one line per in-game day, and the debug panel's counter ticks with the sim clock.

**Evidence.** One `dotnet build Agora.sln` → 0 warnings, 0 errors, both halves deployed. `dotnet test tests\Agora.Core.Tests` → 22/22, ~40 ms, no game assemblies loaded. In-game (`Logs\Modding.log`): `Loaded Agora.Mod … in 49.8005ms`, `Loaded Agora.Core … in 0.4558ms`, `Registered UI Module {"m_ModuleId":"Agora.Mod", …} from [assetdb://user/Mods/Agora.Mod/Agora.Mod.mjs]`, clean dispose of both, no exception, alongside ~20 other installed mods. Settings page renders both toggles as readable labels.

⟐⟐ **Where the logs are.** `Colossal.Logging` gives each logger its own file. Agora writes to `…\Cities Skylines II\Logs\Agora.log` — **not** `Player.log`, which contains no Agora lines even on a healthy run. `Logs\Modding.log` carries assembly load, UI module registration and dispose; a mod that fails to load reports it there, not in its own log.

*Rationale for the UI smoke test: the Gameface pipeline was the largest unknown in the stack, and under the original plan nothing touched it until M2's gate depended on it. It paid for itself immediately — it is what exposed the `DeployWIP` ordering hazard in §4.2, which would otherwise have surfaced in M2 as a panel that mysteriously stopped appearing.*

### M1 · Time & Truth ⟐
Tasks: `AgoraTimeService`; **start-year delivery**; sensor pass 1 (v1 metric set, city + district) → `snapshot.json`; sidecar IO with atomic writes + self-owned save GUID + load reconciliation; determinism kernel wired to real saves; determinism test suite.

**Start-year delivery — verify before patching.** Scout 0001 found `TimeSystem.startingYear` has a **public setter**. If the game's date surfaces derive from `TimeSystem`, setting it delivers 1990 with no Harmony at all, retiring risk §13.2 entirely. M1's first task is to test that. Only if it fails does the Harmony patch proceed, and then: a complete enumeration of date-display surfaces from Scout 0002 comes first, and the patch ships behind a kill-switch.

**Gate:** save → quit → load ×10 with zero desync, where desync means the SHA-256 of serialized Agora state at sim-date D differs across the reload; HUD and every scouted surface show 1990; disabling Agora mid-save reverts every date surface to stock; same-seed test suite green.

### M2 · The Engine
Tasks: bloc construction; issue weights; party generation (EU) and party+faction generation (NA) with placeholder names; affinity + turnout model; monthly vote-share computation per district; derived indices; basic dashboard (seat chart, district splits, wealth×education crosstabs) reading engine state.

**Gate:** an election simulated from a fixed save reproduces identical results ×3.

### M3 · The Voice
Tasks: `IFlavorProvider` + `ClaudeCliProvider`; prompt assembly (plan excerpt + sidecar + snapshot); schema validation + retry-once + fail-closed fallback; wake cadence (yearly, election, manual trigger button); flavor cache; parties/factions get real names, descriptions, slogans; 3–5 articles per wake in the news feed (an election wake is the one exception: the ordinary count plus one slot for each dedicated election piece — 7 under NA rules, 8 under EU, per the W5 "elections covered extensively" decision).

**Gate:** disable network/CLI mid-year — engine and dashboard continue perfectly, prose stays at last good state.

### ⟐ M4a · The Cycle — Elections
Tasks: term calendar (3y EU / 4y NA); campaign season; weekly polls with turnout+education-weighted error; manifesto refresh; election execution (PR seats / FPTP districts + mayor); placeholder election results screen.

**Gate:** two full terms simulated on a test city produce two clean elections, and poll error runs in the correct direction (low-education districts under-sampled).

### ⟐ M4b · The Cycle — Government
Tasks: coalition formation and collapse with snap elections (EU); faction dominance mechanics + platform writing (NA); mandate generation, monitoring, resolution with happiness stakes; party/faction lifecycle incl. revival.

**Gate:** at least one coalition collapse (EU test) and one faction takeover (NA test) occur under forced conditions and resolve correctly.

*M4 was split because as written it contained terms, campaigns, polls, two electoral systems, coalitions, factions, mandates, lifecycle and a results screen — four milestones of work behind a single gate, which means no feedback until all of it is done.*

### M5 · The World
Tasks: **effect palette rescope against §7's gap list** (do this first); author timeline catalogs (80–120 events, 1990→2025, incl. Gulf War, reunification, Maastricht/NAFTA, dot-com, 9/11, euro cash, enlargement, GFC, eurozone austerity, shale boom, 2015 migration wave, 2016 populism/Brexit, tariffs, COVID, supply-chain crunch, Ukraine energy crisis, inflation/rate hikes, AI boom, 2025 Iran oil shock); scheduler; effect palette implementation with caps + fallback chains; procedural post-catalog generator; strikes/unrest/subsidies wired to news.

**Gate:** a 1990-start test city reaches 2008 and the crash measurably hits loans, land value, and headlines.

### M6 · The Spectacle
Tasks: political map overlay (districts tinted by leading party, toggleable like info views — per scout findings; fallback: stylized district map inside dashboard); election night broadcast mode with live district calls replacing the placeholder; dashboard polish (trend charts, crosstab explorer, news archive, mandate tracker).

**Gate:** overlay screenshot reads as an election broadcast of the player's own city.

---

## 12. Testing Strategy

- **Determinism suite** (runs on every change): fixed snapshot + fixed seeds → byte-identical engine output, twice. ⟐ Runs under plain `dotnet test` against `Agora.Core` alone, on a machine with **no copy of the game installed**. That constraint is the test — if the suite ever needs the game, the Core/Mod split has been breached.
- **Schema suite:** all catalogs and sample LLM outputs validate; a numeric field smuggled into flavor JSON fails the build.
- **Engine simulation harness:** headless multi-year runs on synthetic city data; asserts lifecycle rules, poll error direction, turnout effects, mandate resolution.
- **Effect cap tests:** every palette entry proves magnitude/duration caps.
- **Manual in-game checklist per milestone gate**, maintained in `docs/status.md`.

⟐ **Golden-value tests on the determinism kernel.** Seed derivation is pinned to a known hash, so an innocent-looking refactor of `SeedStreams` — swapping in `string.GetHashCode`, reordering the mix, changing the encoding — fails loudly instead of silently rewriting every existing save's political history. (`string.GetHashCode` is randomised per process on .NET Core; using it would make the same save produce different politics every launch.)

---

## 13. Known Risks (Scout priorities)

1. ⟐ **Save-complete callback availability** → mitigated by Agora owning its own save GUID (§5). The remaining question is only *when* to write, not *how* to identify.
2. ⟐ **Clock patch leak surfaces** → possibly eliminated entirely; `TimeSystem.startingYear` is publicly settable. Verify in M1 before writing any patch.
3. **District metric granularity** — some metrics may only exist city-wide → sensor spec marks per-district fields best-effort with city-level fallback.
4. **Map overlay paintability of district polygons** → dashboard-map fallback pre-approved.
5. ⟐ **Effect palette gaps** — RCI demand, rent/land value, birth rate and subsidies have no enum support, and district scope has only 14 modifiers. This is now a known quantity rather than a risk; it needs a scoping decision before M5.
6. **LLM latency on wake** — always async, never blocks sim; manual trigger shows a running state in UI.
7. ⟐ **Harmony does not ship with the game.** It comes from the modding toolchain or `Lib.Harmony`, and must be shipped with the mod.
8. ~~Node version drift.~~ ⟐⟐ **Retired — the claim was wrong.** The template's `package.json` declares `"node": ">=18"`, so Node 24 is fine. The "20.11" figure came from the plan, not from the template. `tools/verify-setup.ps1` now reads `engines.node` from `ui/package.json` instead of asserting a version from memory.
9. ⟐⟐ **The toolchain's own tools target .NET 6, which is EOL.** `ModPostProcessor.exe` and `ModPublisher.exe` abort the build on a machine without it. Worked around by overriding both targets to pass `DOTNET_ROLL_FORWARD=LatestMajor` scoped to the `Exec` — verified running on .NET 9. **Re-sync those overrides if a toolchain update changes `Mod.targets`.** They are copies of CO's target bodies plus one environment variable.
10. ⟐⟐ **Silent-skip build targets.** Already bitten this project twice: a deploy target gated on a folder nothing creates, and a UI bundle deleted by `DeployWIP`. Both failed green. Any new target whose `Condition` can be false must say so out loud (`<Warning>`), never skip quietly.
11. ⟐⟐ **A shell opened before the toolchain install sees no `CSII_*` variables.** `Mod.props` dodges this by reading the registry; webpack does **not** — it reads `process.env.CSII_USERDATAPATH` and throws. Anything shelling out to npm must pass it explicitly.

---

## 14. Open Decisions (do not implement until closed)

- NA primaries as full elections vs faction-dominance only.
- Timeline jitter: fixed real dates vs seeded ±6 months.
- Snapshot retention default (proposed 25).
- Post-2026 authorship split (proposed: engine picks archetype, Claude writes prose).
- Unrest ceiling confirmation: statistical only, no visual destruction.
- ⟐ **Effect palette gap resolution** (§7): accept the district-scope constraint, or take on Harmony work for rent/land value and RCI demand.

### ⟐ Closed 2026-07-28

- ~~Repository location~~ → reuse `Cs2CompanionApp`.
- ~~Single assembly vs split~~ → split (`Agora.Core` + `Agora.Mod`).
- ~~Clock patch scheduling~~ → stays mandatory in M1, with a kill-switch and an enumeration gate; verify `TimeSystem.startingYear` first.

### ⟐⟐ Closed 2026-07-30

- ~~Toolchain vs direct `Managed` references~~ → toolchain mode is the build; fallback retained behind `-p:UseCsiiToolchain=false` as a contributor compile check.
- ~~`Agora.Core` target framework~~ → netstandard2.0, forced by `net48`. Not revisitable without leaving toolchain mode.
- ~~How the UI bundle reaches the game~~ → webpack writes directly into the deploy folder; `BuildUI` runs after `DeployWIP`.
