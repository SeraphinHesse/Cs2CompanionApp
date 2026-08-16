# Wave 4 — tick wiring, effects and persistence · lane ownership

Umbrella: `event-system/wave-4`. Spine commit: `wave-4 spine`.

**The law:** every file more than one lane would touch is landed by the orchestrator in the spine,
before any worktree exists. Lanes below own strictly disjoint paths. A merge conflict is a bug in
this table, not something to hand-resolve.

**Base measured, not read:** `EventSystemRefresh` at `1a8b737` builds with 0 errors and the Core
suite is **1978 passed, 0 failed**. Wave 3's PR (#6) is merged.

---

## What the spine changed, and why it is not what the plan said

The plan (`docs/plans/0004-event-system-rework.md` §Wave 4) was written before most of this code was
read. Five of its spine items were already built by waves 2–3, and three things it does not mention
turned out to be load-bearing. Both are recorded here because the lanes code against the result.

### Already built — struck from the spine

| Plan item | Actual state |
|---|---|
| "A story records `DraftMonth` and `ResolveOnMonth`" | `Story.OpenedDate` / `ResolvesDate` / `ResolvedMonth` all exist (wave 2) |
| "`EngineTickInput` gains `PlayerCommands` / `ResolveEarlyRequests`" | `PoliticalState.PlayerCommands` and `Story.ResolveEarlyRequested` exist. Putting them on the *input* would have made the command log a tick argument instead of engine state, which is the opposite of what `PlayerCommand`'s own remarks ratified |
| "Cap story effect breadth against `stories.maxStoryEffectsPerModifier`" | The tuning key exists and is unread. Lane 4c enforces it |
| `PoliticalState` schema bump | **No sidecar version moves this wave.** State 6, settings 4, metric history 1, flavor cache 2 all stand — wave 4 persists no new field. Every field it writes was landed by wave 2 |
| `src/Agora.Mod/Effects/AgoraTreasurySystem.cs` | Struck. There is no `kind: "money"` effect and no `PlayerMoney` debit; the wave-3 handoff records the owner decision that the debt penalty ships as the existing capped palette entry. A treasury system would have nothing to debit |

### Not in the plan, and the wave does not work without them

1. **`Agora.Mod` never loads the civic catalog.** `AgoraRuntime.LoadCatalog()` reads only the three
   `timeline_*.json`; nothing in the whole assembly mentions `CivicEventCatalog`, `StoryAssembler` or
   `PoliticalPower`. Every lane needs it and it lives in `AgoraRuntime.cs`, so it is spine.
2. **There is no story term in `AffinityEngine`.** `PoliticalState.cs:540` asserts stories
   "contribute pressure through their own term with its own budget" — the term does not exist, and
   without it the three authored pressures on all 58 civic events move no votes at all. Owner
   decision 2026-08-16: build it this wave. Spine lands the term and the tuning trio; lane 4c
   produces what it reads.
3. **The 90 wrapped timeline events are inert.** No timeline event authors an `issuePressure` and
   `timeline.schema.json` forbids the key, though `TimelineCatalogLoader` has always read it. Owner
   decision 2026-08-16: fix it this wave. Spine does the schema half; lanes 4f/4g/4h author.

### The one deliberate departure from the plan's lane table

The plan puts the draft/resolve/sweep orchestration in `src/Agora.Mod/Core/AgoraRuntime.Stories.cs`.
**It goes in `Agora.Core` instead**, as `Stories/StoryCycle.cs`, called from `PoliticalEngine`'s new
stage 3b. The reason is the plan's own trap list: `AgoraRuntime` compiles into no test, both of wave
0's blocking defects lived there and passed 1415 tests, and the stranded-story sweep and the
idempotence guards are precisely the arithmetic that has to be tested. What is left on the Mod side
is catalog loading and two field assignments, which is spine.

---

## Schema changes this wave

Run through `/schema-change`. **No sidecar migration exists or is possible** — both files below are
shipped content, not save data.

| Contract | Change | Version |
|---|---|---|
| `engine_tuning` | `affinity.storyPressureWeight` + `…Muted` + `…Loud`, all three **required** | **7 → 8** |
| `timeline` | `data/schemas/timeline.schema.json` gains the optional `issuePressure` property | **stays 1 — deliberately** |

**Why `timeline` does not bump.** `TimelineCatalogLoader.SupportedSchemaVersion` is a hard equality
(`:181`), so a bump is a rejection, not a migration — a v2 file would fail to load on any build that
has not also moved. The change is purely additive and optional: the loader has read `issuePressure`
since it was written (`:49`, `ReadIssuePressure` at `:427`), so an old build reads a new file
correctly and a new build reads an old file as `IssuePosition.Centre`. Bumping would break
compatibility in the one direction that currently works, in exchange for nothing. Recorded here
rather than silently skipped: the precedent is wave 3, which bumped `engine_tuning` because `stories`
gained a **required** property, and that is the test being applied.

---

## Lanes

Eight. Merge order is the dependency graph, not a ritual — see the bottom of this file.

| Lane | Branch | Worktree | Exclusive paths |
|---|---|---|---|
| **4a** | `event-system/w4-4a` | `.claude/worktrees/w4-4a` | `src/Agora.Core/Stories/StoryCycle.cs` |
| **4b** | `event-system/w4-4b` | `.claude/worktrees/w4-4b` | `src/Agora.Mod/Core/AgoraRuntime.StoryCommands.cs` |
| **4c** | `event-system/w4-4c` | `.claude/worktrees/w4-4c` | `src/Agora.Core/Stories/StoryEffects.cs`, `src/Agora.Core/Stories/StoryPressure.cs` |
| **4d** | `event-system/w4-4d` | `.claude/worktrees/w4-4d` | `src/Agora.Core/Stories/PowerLedger.cs` |
| **4e** | `event-system/w4-4e` | `.claude/worktrees/w4-4e` | `tests/Agora.Core.Tests/StoryCycleTests.cs`, `StoryPersistenceTests.cs` |
| **4f** | `event-system/w4-4f` | `.claude/worktrees/w4-4f` | `data/timeline_global.json`, `tests/Agora.Core.Tests/TimelineGlobalPressureTests.cs` |
| **4g** | `event-system/w4-4g` | `.claude/worktrees/w4-4g` | `data/timeline_eu.json`, `tests/Agora.Core.Tests/TimelineEuPressureTests.cs` |
| **4h** | `event-system/w4-4h` | `.claude/worktrees/w4-4h` | `data/timeline_na.json`, `tests/Agora.Core.Tests/TimelineNaPressureTests.cs` |

Every path above appears in exactly one row. Checked before any worktree was created.

**Why the pressure gate is split three ways.** The natural place for "every wrapped timeline event
authors an `issuePressure`" is one catalog-wide test in the spine. It cannot go there: it is red
until all ninety events are authored, so the spine commit would ship red and every lane's first
build would inherit a failure that is not its own. One gate per file puts each third beside the
content it guards and each lands green with that content. The union of the three is the whole gate;
a fourth `timeline_*.json` would need its own, and there is deliberately no aggregate test that
would have caught the omission, because an aggregate test is exactly the file two lanes would have
to share.

### Files the spine owns — no lane may open these

`EngineTick.cs` · `TickPlanner.cs` · `PoliticalEngine.cs` · `AffinityEngine.cs` ·
`AffinityRequest.cs` · `CommandOutcome.cs` · `EngineTuning.cs` · `TuningPresets.cs` ·
`AgoraRuntime.cs` · `data/engine_tuning.json` · `data/schemas/engine_tuning.schema.json` ·
`data/schemas/timeline.schema.json` · `Stories/StoryCycleTypes.cs` ·
`Stories/StoryPressureContribution.cs` · `Stories/Catalog/CivicEventCatalog.cs` ·
`Contracts/Blocs.cs` · `tests/…/TimelineEventAdapterTests.cs` · `tests/…/StoryCyclePhaseTests.cs`

**Spine verification, measured:** `dotnet build Agora.sln -p:UseCsiiToolchain=false` — 0 errors,
1 warning (the expected fallback-mode "nothing was deployed" notice).
`dotnet test tests\Agora.Core.Tests\Agora.Core.Tests.csproj` — **1992 passed, 0 failed** (from
1978). The spine's own suite caught one defect while it was being written: `EngineTuning`'s
compiled-in `SchemaVersion` default was left at 7 while the file moved to 8, which
`ShippedTuningTests` exists to catch and did.

---

## Seams — both ends, published

A lane codes against these signatures. The spine ships each with a trivial body marked
`AGORA-SEAM(wave-4/<lane>)`, so every lane builds from commit one. **A stub is not finished work**;
the comment on each says what the real deliverable is.

### Written by 4a, read by the spine

```csharp
// src/Agora.Core/Stories/StoryCycle.cs
public static StoryCycleResult Run(StoryCycleInput input);
```

`StoryCycleInput` and `StoryCycleResult` are **spine-owned** (`Stories/StoryCycleTypes.cs`) so
`PoliticalEngine` compiles without 4a. Their fields are the vocabulary of the whole wave:

| `StoryCycleInput` | |
|---|---|
| `PriorState` | the tick's working state — 4a mutates the clone it is handed |
| `Catalog` | `IReadOnlyList<CivicEvent>` — the loaded civic catalog |
| `Context` | `StoryReadContext` — today plus history plus recorded evidence |
| `SaveGuid`, `Today`, `Tuning` | |
| `IsStoryDraft`, `IsStoryResolve` | from `TickPlan` |
| `IsReplay` | true suspends drafting and resolution entirely |
| `GoverningVoteShare` | 0–1, for `PowerLedger.Accrue` |

| `StoryCycleResult` | |
|---|---|
| `DraftedStories`, `ResolvedStories` | sorted by `Id` ordinal |
| `EffectRequests` | already capped by 4c |
| `Pressures` | `List<StoryPressureContribution>` for the affinity stage |
| `PowerDelta` | net signed movement this tick |
| `Warnings` | non-fatal, in emission order |

### Written by 4c, read by 4a and by the spine

```csharp
// src/Agora.Core/Stories/StoryEffects.cs
public static List<EffectRequest> ForActive(IReadOnlyList<Story> live,
                                            IReadOnlyList<CivicEvent> catalog,
                                            EngineTuning tuning);

public static List<EffectRequest> ForResolution(Story story,
                                                IReadOnlyList<SlotOutcome> outcomes,
                                                IReadOnlyList<CivicEvent> catalog,
                                                EngineTuning tuning);

// src/Agora.Core/Stories/StoryPressure.cs
public static List<StoryPressureContribution> For(IReadOnlyList<Story> live,
                                                  IReadOnlyList<Story> justResolved,
                                                  IReadOnlyList<CivicEvent> catalog,
                                                  EngineTuning tuning);
```

`StoryPressureContribution` is **spine-owned** (`Stories/StoryPressureContribution.cs`) because
`AffinityEngine` consumes it. Its fields:

| Field | Meaning |
|---|---|
| `StoryId` | sort key, ordinal |
| `Pressure` | `IssuePosition` — **salience**. Which issues this story makes the city care about, and which way. Dot-producted against each party's platform exactly as `TimelineEvent.IssuePressure` is |
| `GovernmentCredit` | `[-1, +1]` — **credit**, the other half of the wave-3 ruling. Positive pulls voters toward whoever is governing; negative pushes them away. Zero when nobody governs |
| `Severity` | 1–5, scales the term the same way `AffinityEngine.SeverityScale` already does |
| `OpenedDate` | the decay anchor |

### Written by 4d, read by 4a and by 4b

```csharp
// src/Agora.Core/Stories/PowerLedger.cs
public static PoliticalPowerState Accrue(PoliticalPowerState prior, double governingVoteShare,
                                         SimDate today, EngineTuning tuning);

public static PoliticalPowerState AwardForStory(PoliticalPowerState prior, Story story,
                                                IReadOnlyList<CivicEvent> catalog,
                                                SimDate today, EngineTuning tuning);

// AMENDED MID-WAVE — see below. Was: PoliticalPowerState Spend(…)
public static bool TrySpend(PoliticalPowerState prior, string storyId, string eventId,
                            StoryTier tier, SimDate today, EngineTuning tuning,
                            out PoliticalPowerState next);

public static bool TryDebtPenalty(PoliticalPowerState power, EngineTuning tuning,
                                  out EffectRequest request);
```

Every one returns a **new** state; none mutates its argument.

**`Spend` → `TrySpend`, amended after both ends had shipped.** The original returned the state
unchanged when `CanAfford` refused *and* when the cost was legitimately zero, writing no ledger entry
either way — so the two were indistinguishable, and lane 4b was left inferring failure from an
unmoved balance. Under a hand-edited `power.overrideCost.minor = 0` that inference reports
`InsufficientPower` for an override that succeeded and was correctly free.

**Two independent reviewers found this in two different lanes before either had merged**, which is
the argument for publishing both ends of a seam rather than only the read end: the gap was invisible
from inside either lane and obvious from either review. `TrySpend` returns true when the override is
granted — including a free one — and false only when `CanAfford` refuses; `next` is always a valid
non-null state, the debited clone on success and the untouched prior clone on a refusal.

### Written by the spine, read by everyone

- `TickPlan.IsStoryDraft` / `TickPlan.IsStoryResolve`
- `EngineTickInput.CivicCatalog` / `.IsReplay`
- `EngineTickResult.DraftedStories` / `.ResolvedStories` / `.PowerDelta`
- `AgoraRuntime.CivicCatalog` (property, `IReadOnlyList<CivicEvent>`, never null)
- `CommandOutcome.InsufficientPower = 11` / `.AlreadyResolved = 12` / `.PowerDisabled = 13`
- `affinity.storyPressureWeight` / `…Muted` / `…Loud`
- `PlayerCommand.DeclaredMet` and `PlayerCommandLog.Append` — both added mid-wave, see below

### Two spine additions made mid-wave, after review

- **`PlayerCommand.DeclaredMet`.** Without it the log could not tell a declared success from a
  declared failure: the two commands appended rows differing in **no field**, while the flag they set
  is what `PoliticalPower.AwardFor`'s `manualDeclared` parameter reads. `PlayerCommand`'s contract
  says the log is replayed rather than re-solicited, so a replay would have scored a different award
  from the one the player earned. Additive and optional, so **no schema version moves** — wave 4 is
  the first wave that writes any story command, so no older save carries a row for the default to be
  wrong about.
- **`PlayerCommandLog.Append`.** The command log's ordering rule, in `Agora.Core` because deciding
  where a record sorts in engine state is computing rather than glue. Lane 4b had implemented it in
  `Agora.Mod`, which `src/Agora.Mod/CLAUDE.md` forbids and which would have left one documented rule
  with two implementations on opposite sides of the assembly boundary.

### And one root-cause fix to `AgoraRuntime.ClampWatermarkToClock`

**Three reviewers independently reported what looked like a rewind defect in three different lanes;
all three were one spine omission.** Wave 0 wrote that repair when there was exactly one watermark.
Wave 4 added three more — `LastStoryDraftMonth`, `LastStoryResolveMonth` and
`PoliticalPowerState.LastAccrualMonth` — and each gates its own subsystem behind the same "have we
already run this month" question. The repair covered one and left three, so a save rolled back
further than the snapshot retention ticked its polls and elections and charged its failure penalties
while drafting no story, resolving none, and accruing nothing — silently, because the guards return
above their own warnings. It now reconciles all four.

---

## What each lane must NOT test

`AgoraRuntime` and everything under `src/Agora.Mod/UiBindings/` are **deliberately not linkable into
the headless suite** — the Core suite must pass with no copy of the game installed, and that
constraint is the test that the Core/Mod split is real. Lane 4b's file is game-facing and therefore
has **no test**, by design and not by omission. Do not fake the runtime to manufacture coverage;
that is itself a review-blocking defect. Write a manual gate row instead, specific enough to fail.

Lanes 4a, 4c, 4d are pure `Agora.Core` and carry the full testing obligation.

---

## Merge order

```
4c ─┐
4d ─┼─► 4a ─► 4e
    │
4b ─┘   4f ┐
        4g ┼─ independent of everything above
        4h ┘
```

- **4c and 4d first** — 4a calls both seams, so merging them first means 4a is verified against real
  bodies rather than stubs.
- **4a next**, then **4e**, whose tests drive 4a's code. 4e **cannot build in its own worktree** and
  that is correct rather than a defect: merge it into the umbrella to verify it, and review it there.
- **4b** shares no file and no seam with 4a/4c/4e and may merge as soon as it is reviewed.
- **4f, 4g, 4h** touch only content and share nothing with anyone. They may merge the moment they
  are green, in any order. Say so in the merge commit rather than idling.
