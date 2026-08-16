# Wave 3 — lane ownership

Catalog and content. The wave that gives the wave-2 engine something to read.

The spine (`68e5da4`) landed every cross-cutting file before this document was written and before any
worktree existed. Lanes own strictly disjoint paths. **A merge conflict in this wave is a bug in this
document, not something to resolve by hand.**

Branch and worktree names follow waves 0–2: `event-system/w3-<lane>` in `.claude/worktrees/w3-<lane>`,
cut from `event-system/wave-3`.

---

## Owner decisions taken at the top of this wave

Three, all binding, and two of them **narrow what the plan says to build**. They are decisions rather
than discoveries, so no lane may reopen them.

1. **Delta-only on the five census-gated metrics.** `births`, `deaths`, `citizensMovedIn`,
   `citizensMovedAway` and `movedAwayUnhappy` may carry a `delta` spec and never an absolute
   `metric` one. Wave 1's `AGORA-STATCOLLECTION` gate is unwalked, so each is either a
   per-in-game-day rate or a total since the city was founded, and the two differ by orders of
   magnitude. **The loader enforces this** — you cannot author around it. The *threshold values* on
   these five remain provisional until the census is read; say so in `notes`.
2. **The debt penalty uses the shipped `city-service-building-upkeep` entry.** The plan's "new
   `kind: money` effect" route is **not** built: no new effect kind, no `PlayerMoney` debit, no
   `politicsmodplan.md` §7 ratification, no `AgoraTreasurySystem`. Wave 4 wires the existing capped
   entry. This is why there is no effects lane in this wave.
3. **The 25/50/25 timeline split is expressed in a policy file, not by deleting events.**
   `data/timeline_*.json` is **frozen this wave**. The boring quarter is marked `"none"` in
   `data/timeline_adaptation.json` and keeps firing as a timeline event exactly as it does today.

## What the spine already landed — do not rewrite any of it

| File | What it now holds |
|---|---|
| `data/schemas/civic_events.schema.json` | The authored-event shape, `additionalProperties: false` |
| `src/Agora.Core/Stories/Catalog/CivicEventCatalog.cs` | `CivicEventCatalog`, `CivicEventCatalogSource`, `CivicEventCatalogLoadResult` |
| `src/Agora.Core/Stories/Catalog/CivicEventCatalogLoader.cs` | The loader and all four non-schema checks |
| `src/Agora.Core/Events/Catalog/CatalogIssue.cs` | `CatalogIssueCode` 100–115, the civic-event block |
| `data/engine_tuning.json` + `EngineTuning.cs` | Three new palette entries, both hand-maintained copies |
| `data/events_{global,eu,na}.json` | Valid **empty** catalogs, one per content lane |
| `data/schemas/timeline_adaptation.schema.json` + `data/timeline_adaptation.json` | The adaptation policy, empty |
| `tests/…/ShippedCivicEventCatalogTests.cs` | The build-time gate, green on empty and acquiring teeth as content lands |
| `tests/…/EffectPaletteTests.cs` | Golden shape moved 43 → 46 for the three additions |

**No lane edits any of the above** except where a row below names it. If your lane appears to need a
change there, that is an escalation to the orchestrator, not an edit.

---

## Lanes

### 3a — global civic events

| | |
|---|---|
| **Branch** | `event-system/w3-3a` |
| **Worktree** | `.claude/worktrees/w3-3a` |
| **Owns (exclusive)** | `data/events_global.json` |

~25 events: services, pollution, crime, housing, transport, budget. **Every id prefixed `glob-`.**

### 3b — EU civic events

| | |
|---|---|
| **Branch** | `event-system/w3-3b` |
| **Worktree** | `.claude/worktrees/w3-3b` |
| **Owns (exclusive)** | `data/events_eu.json` |

~15 EU-flavoured events. **Every id prefixed `eu-`.**

### 3c — NA civic events

| | |
|---|---|
| **Branch** | `event-system/w3-3c` |
| **Worktree** | `.claude/worktrees/w3-3c` |
| **Owns (exclusive)** | `data/events_na.json` |

~15 NA-flavoured events. **Every id prefixed `na-`.**

### 3d — the timeline adapter

| | |
|---|---|
| **Branch** | `event-system/w3-3d` |
| **Worktree** | `.claude/worktrees/w3-3d` |
| **Owns (exclusive)** | `src/Agora.Core/Stories/Catalog/TimelineEventAdapter.cs` (new), `data/timeline_adaptation.json`, `tests/Agora.Core.Tests/TimelineEventAdapterTests.cs` (new) |

The generic wrapper — `Name` ← `Title`, `Description` ← `HeadlineBrief`, a severity-derived check —
plus the 25/50/25 classification expressed in the policy file. **`data/timeline_*.json` is frozen and
this lane does not open it.**

### 3e — the loader's negative-path suite

| | |
|---|---|
| **Branch** | `event-system/w3-3e` |
| **Worktree** | `.claude/worktrees/w3-3e` |
| **Owns (exclusive)** | `tests/Agora.Core.Tests/CivicEventCatalogLoaderTests.cs` (new) |

The spine's gate proves the *shipped* catalogs load. This lane proves the loader **rejects what it
claims to reject** — every `CatalogIssueCode` in the 100–115 block reachable by a fixture, plus the
degradation contract: a bad entry rejects that entry and not the document, a corrupt document
contributes nothing and does not throw, and the valid subset survives alongside the errors.

---

## Path disjointness — checked before any worktree was created

Every path below appears in **exactly one** row.

```
data/events_global.json                                    3a
data/events_eu.json                                        3b
data/events_na.json                                        3c
src/Agora.Core/Stories/Catalog/TimelineEventAdapter.cs     3d
data/timeline_adaptation.json                              3d
tests/Agora.Core.Tests/TimelineEventAdapterTests.cs        3d
tests/Agora.Core.Tests/CivicEventCatalogLoaderTests.cs     3e
```

`data/timeline_{global,eu,na}.json` appear in **no** row: frozen by owner decision 3.
Everything in the spine table appears in no row.

**The id-prefix convention is what makes three blind content lanes collision-proof.** `glob-`, `eu-`,
`na-`. The loader rejects a duplicate id across documents, and three lanes that cannot see each other
would otherwise both reach for `housing-crisis`.

---

## Seams — both ends, published here

| Seam | Signature | Written by | Read by |
|---|---|---|---|
| Catalog load | `CivicEventCatalogLoadResult CivicEventCatalogLoader.Load(IEnumerable<CivicEventCatalogSource>, EngineTuning)` | spine | 3d, 3e |
| Single-document load | `CivicEventCatalogLoadResult CivicEventCatalogLoader.Load(string sourceName, string json, EngineTuning)` | spine | 3e |
| Census gate | `IReadOnlyList<string> CivicEventCatalogLoader.CensusGatedMetricIds` | spine | 3a–3c, 3e |
| Theme filter | `IReadOnlyList<CivicEvent> CivicEventCatalog.ForTheme(EventRegion)` | spine | 3d |
| Declared features | `IReadOnlyList<string> CivicEventCatalog.DeclaredFeatureIds` | spine | 3e |
| Metric validity | `bool MetricRegistry.IsKnown(string metricId, TriggerScope)` | wave 2 | 3a–3d |
| Adaptation | **Amended mid-wave.** Not static: an instance method on an adapter built from the parsed `TimelineAdaptationPolicy`, because a static call cannot consult a policy. Lane 3d owns the exact shape and must return a **discriminated** result rather than a bare `null` — `null` currently means `none`, `authored` *and* null-input, and a wave-4 caller treating it as "drop it" would silently lose every authored event. | 3d | wave 4 |

### The metric vocabulary content is authored against

**36 city-scope ids, 19 district-scope.** `MetricRegistry.CityMetricIds` / `DistrictMetricIds` are the
authority and both are sorted ordinal. The district list is the city list minus the city-only
fields — notably `commuteMinutes` and `trafficCongestion` are **city-only**, and so are every
`CityStatistics`, `TourismLevels` and `ProgressionState` scalar except `uncollectedGarbage`,
`attractionCount` and `signatureBuildingCount` (wave 1 ruling 3: no statistic is per-district at all).

**A name may be added but never renamed** — the sidecar fingerprint is taken over them sorted.

---

## What no lane may do

- **No `Policy` trigger, ever.** Nothing writes `CitySnapshot.ActivePolicyIds`, so a policy spec is
  permanently `NotMet` and an `Absent` policy spec is permanently `Met`. The loader rejects the kind
  by name. This is not a gap to work around; it is a missing sensor, and it belongs to a later wave.
- **No `Unlock` trigger, and no `featureIds` entries.** Feature ids are raw
  `PrefabSystem.GetPrefabName` strings (wave 1 ruling 4) and **nobody has read what they actually
  are** — wave 1's manual gate 11 is unwalked. Authoring one means guessing a string that must match
  the game exactly, and a wrong guess reads as "never unlocked" forever. Leave `featureIds` empty.
- **No `Manual` trigger on an authored event.** A `Manual` event is never pooled and can never
  produce a story. Mandatory is a *tier* derived from severity; it is not a trigger kind. Two wave-2
  lanes read the earlier wording and built opposite things.
- **No lane tests `AgoraRuntime` or `UiBindings/**`.** Deliberately not linkable into the headless
  suite; faking the runtime to manufacture coverage is itself a review-blocking defect. Nothing in
  wave 3 touches either.
- **No lane runs `npm run build` or a bare `dotnet build Agora.sln`.** Both deploy into the player's
  live `…\Mods\Agora.Mod`. Verify with
  `dotnet build src/Agora.Mod/Agora.Mod.csproj -p:UseCsiiToolchain=false`.
- **No lane runs `dotnet test Agora.sln`** — it pulls in `Agora.Mod`, which needs the game installed.
  Always `dotnet test tests\Agora.Core.Tests\Agora.Core.Tests.csproj`.
- **No lane touches `ui/`.** No worktree needs `npm install` this wave.
- **`refsrc/` does not exist inside a worktree.** It lives only at
  `C:\Users\serap\OneDrive\Documents\GitHub\Cs2CompanionApp\refsrc`. Grep it, never read it in full.

---

## Content rules for lanes 3a–3c

- **Every threshold must be hittable in a normal game**, and `notes` must say roughly how often you
  expect it to fire and why. The design document says this twice. An event that can never trigger is
  indistinguishable from one that was never written.
- **Effects must alienate or enfranchise.** Each of active / success / failure carries an
  `issuePressure`, so a positive outcome moves voters toward the government and a negative one away.
  That is the mechanism the whole rework exists for. An event with no pressure anywhere is scenery.
- **District dependence is the point.** Prefer `anyDistrict` triggers and `district-*` effects so the
  same month reads differently block to block.
- **Severity is conservative.** `5` is mandatory and should feel rare; a catalog where everything is
  a 4 has no dynamic range. `AffinityEngine.EventTerm` scales by `severity/5`.
- **An event's prose may only claim what its effect ids can actually do.** This is the rule the
  mapping table in `docs/plans/0004-event-system-rework.md` § "The two known palette gaps" exists to
  enforce, and it is the one a reviewer will check hardest. The specific traps recorded there:
  - **There is no tourism modifier.** Use `city-attractiveness` and say *attractiveness*, not
    *tourism*.
  - **Nothing kills citizens**, and nothing may. Re-specify a disaster as `crime-accumulation` +
    `disease-probability` with heavy `issuePressure`.
  - **`city-prison-time` is sentence length, not prison cost.** Do not call it a budget.
  - **Agricultural output is unreachable** — `IndustrialEfficiency` is all-industry. Re-specify a
    farming event around trade cost and taxes.
  - **Garbage "production rate" is not a stockpile.** `uncollectedGarbage` is the backlog, and it is
    not the infoview's "stored garbage" either — say *uncollected*, never *landfill*.
  - **RCI demand, rent, land value and birth rate cannot be modified** (scout 0001 §3). Birth and
    death rates *can* be read — that correction is wave 1's, and it is about reading, not modifying.
- **The 46-entry palette is closed.** `data/engine_tuning.json` `effects.perEffect` is the authority.
  Authoring against an id that is not there fails the gate test and wastes the lane's budget.

---

## Merge order

`3a → 3b → 3c → 3d → 3e`, building and testing after each.

**The order is nominal rather than a dependency graph this wave.** 3a, 3b and 3c share no file and no
seam with each other or with 3d/3e — they are three disjoint data files — and 3d and 3e depend only
on the spine. **Any lane may merge as soon as it is reviewed**, in any order; say so in the merge
commit. The one real interaction is the shipped-catalog gate, which only sees an id collision once
two content lanes are on the same branch — and the `glob-`/`eu-`/`na-` prefix convention is what
makes that collision impossible rather than merely unlikely.
