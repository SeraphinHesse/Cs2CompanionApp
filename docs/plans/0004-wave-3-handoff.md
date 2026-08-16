# Wave 3 → Wave 4 handoff

Wave 3 (catalog and content) is code complete, reviewed and merged into `event-system/wave-3`.
This file is written for a session that was not here and has none of the context.

---

## Ready-to-paste prompt for the next orchestrator

> You are the orchestrator for **Wave 4 — tick wiring, effects and persistence** of the AGORA
> event-system rework. **Begin with `/nextwave`.** Read `docs/plans/0004-event-system-rework.md`
> (the plan), `docs/plans/0004-wave-3-handoff.md` (this file — it **outranks the plan** wherever the
> two disagree, because it was written against the code) and `docs/status.md`.
>
> Wave 3 gave the wave-2 engine something to read: 58 authored civic events, a validating catalog
> loader, and an adapter for the 120 shipped timeline events. Confirm wave 3's PR is merged into
> `EventSystemRefresh` before you cut anything; `/nextwave` step 2 then has you prove the base builds
> and tests green and record the count **yourself**.
>
> Wave 4 is the wave that makes it run, and it is the highest-risk one in the rework. Before you
> design the spine, read **"Contradictions with the plan"** and **"Traps aimed squarely at wave 4"**
> below. Two items are load-bearing and will cost you a lane each if discovered late: the
> `issuePressure` authoring pass needs a **`/schema-change` first**, and **a story lives one month,
> not `cycleMonths`**.

---

## State of the world, in one paragraph

Before this wave the story engine could decide what a city's political stories *are* and had no
catalog to decide from. It now has one. `data/events_{global,eu,na}.json` carry **58 authored civic
events** — 27 global, 15 EU, 16 NA — each with a declarative trigger, a resolution check, capped
effect ids, three issue pressures and seven prose fields. `CivicEventCatalogLoader` validates them at
load and refuses, by name and with a reason, every shape that would read like a goal and fail to
function as one. `TimelineEventAdapter` plus `data/timeline_adaptation.json` express the owner's
25/50/25 split over the 120 shipped timeline events **without deleting any of them**. Still true:
**nothing calls any of it.** No tick drafts a story, no UI renders one, no effect is dispatched, no
power is awarded in play. Wave 3's claim is that the content is authorable, reachable and honest —
not that anything happens.

## PR

**PR:** (see `docs/status.md` for the link once opened)
**Merge status: NOT merged.** The owner reviews. Wave 4 must not open its umbrella until it is in.

---

## What actually shipped

**Zero merge conflicts across five lanes**, the fourth wave to prove the spine-first law — and the
most demanding test of it so far, since four of the five files were rewritten two or three times
during review.

### The spine

| File | Change |
|---|---|
| `data/schemas/civic_events.schema.json` | **New.** The authored-event shape, `additionalProperties: false` |
| `src/Agora.Core/Stories/Catalog/CivicEventCatalog.cs` | **New.** Catalog, source and load-result types |
| `src/Agora.Core/Stories/Catalog/CivicEventCatalogLoader.cs` | **New.** The loader and every non-schema check |
| `src/Agora.Core/Events/Catalog/CatalogIssue.cs` | `CatalogIssueCode` 100–121, the civic block |
| `data/engine_tuning.json` + schema + `EngineTuning.cs` | 3 palette entries · `stories.wrappedEventHappinessGoalPoints` · **schemaVersion 6 → 7** |
| `data/schemas/timeline_adaptation.schema.json` + `data/timeline_adaptation.json` | **New.** The non-destructive 25/50/25 policy |
| `tests/…/ShippedCivicEventCatalogTests.cs` | **New.** The build-time gate, landed before any content existed |
| `docs/plans/0004-wave-2-handoff.md` | **Reconstructed** — see "What nearly went wrong" |

### The lanes

| Lane | Delivered | Review |
|---|---|---|
| **3a** `events_global.json` | 27 events, 17 metrics, 27 of 46 palette ids | **Blocked once**, 8 findings |
| **3b** `events_eu.json` | 15 events, 14 metrics, 30 distinct effects | **Blocked twice** |
| **3c** `events_na.json` | 16 events, 13 metrics, 22 palette ids | **Blocked twice** |
| **3d** adapter + policy | Wrapper, 60 classified entries, `AdaptationOutcome` | **Blocked twice** |
| **3e** loader tests | 200 executed cases, 105 methods | **Blocked twice** |

**Every lane was blocked at least once, and every block was a real defect a green suite had waved
through.** The suite went 1703 → **1978**.

---

## Contradictions with `docs/plans/0004-event-system-rework.md`

**These outrank the plan.** Wave 4 plans against them.

1. **`CitySnapshot.ActivePolicyIds` is written by nothing.** It is plumbed through `SensorMerge` and
   `SnapshotAssembly` and populated by **no sensor** — there is no policy sensor. So a `Policy`
   trigger is permanently `NotMet` and an `Absent` policy trigger is permanently `Met`. The loader
   now **rejects `TriggerKind.Policy` by name**. The plan lists `Policy` as an authorable kind; it is
   not, until someone writes the sensor.
2. **`Unlock` triggers are unauthorable in practice.** Feature ids are raw
   `PrefabSystem.GetPrefabName` strings and **nobody has read what they actually are** (wave 1's gate
   11 is unwalked). The loader requires any unlock id to appear in an authored `featureIds`
   allow-list; every shipped catalog leaves that list **empty**.
3. **A story lives `cycleMonths - 1` months — ONE, not two.** `StoryAssembler.NewStory` sets
   `months = stories.CycleMonths - 1`, drafting at M and resolving at M+1. `cycleMonths` is the
   *cadence*, not the story's life, and the two differ by one. **This is the single most expensive
   mistake available to wave 4**, and wave 3 made it: every content lane was handed the cadence as
   though it were the window a player can influence, and authored against it. See the traps below.
4. **The debt penalty ships as `city-service-building-upkeep`** (owner decision). There is no
   `kind: "money"` effect, no `PlayerMoney` debit, no `AgoraTreasurySystem`, and no
   `politicsmodplan.md` §7 ratification. The plan's "primary route" was **not** built. Wave 4 wires
   the existing capped palette entry.
5. **The timeline catalogs were not pruned** (owner decision). `data/timeline_{global,eu,na}.json`
   are untouched; the "boring 25%" is marked `none` in `data/timeline_adaptation.json` and keeps
   firing as timeline events exactly as before.
6. **Event pressures are salience, not credit** (owner ruling). See the next section — this changes
   what wave 4's `StoryPressure.cs` must build.

---

## The pressure ruling — wave 4 owns the other half

The plan says effects "must alienate or enfranchise… a positive outcome moves voters toward the
government and a negative one away". That intent is real and **an `IssuePosition` cannot express
it** — its only consumer, `AffinityEngine.EventTerm` (`:395`), dot-products it against each party's
`Platform` and has no idea who governs. All three content lanes independently invented a
mirror-negating convention on the strength of that sentence, under which *fixing the clinics rewarded
the anti-services party*.

**Ruling: split salience from credit.**

- **Authored** — all three pressures point the **same way** on each axis, differing only in
  magnitude: hot while live, quieter once resolved, loudest on failure. A zero is permitted (the
  issue stopped mattering); a sign flip is not, and is machine-checked as `PressureSignFlip`.
- **Derived, by wave 4** — government credit and blame come from the slot's own outcome and tier
  through **`stories.enfranchisementWeight` and `stories.alienationWeight`**, which already exist and
  are already documented as exactly that ("how far a met outcome pulls voters toward the
  government"). **Nothing in the catalogs expresses this. Wave 4 must build it, or the rework's
  central mechanism does not exist.**

Full reasoning is on `CivicEvent.ActivePressure`'s remarks, which is where a content author meets it.

---

## Traps aimed squarely at wave 4

- **A story lives one month.** Repeated because it is the costliest. Wave 4 owns `TickPlanner`'s
  draft/resolve phases and will be reasoning about `cycleMonths` constantly. Any window a player is
  scored over is `cycleMonths - 1`. Wave 3 mechanised the catalog half (`CheckWindowOutrunsStoryLife`)
  but **only a human re-deriving each threshold catches the second-order damage**: when the rule
  fired, lane 3a found four checks whose thresholds were sized for a two-month span, so a mechanical
  `2 → 1` would have silently *doubled* the difficulty. Roughly 40 thresholds across two files had to
  be re-derived by hand.
- **The `issuePressure` authoring pass needs a `/schema-change` FIRST.** Every wrapped timeline event
  is currently **inert**: no shipped timeline event authors an `issuePressure`, so all 90 generic
  wrappers carry zero pressure and empty effects. Fixing that is not an authoring pass alone —
  `TimelineCatalogLoader` reads a key `timeline.schema.json` **forbids**
  (`additionalProperties: false`, `issuePressure` undeclared), so authoring first walks straight into
  a red `ShippedTimelineCatalogTests`. A tripwire
  (`ShippedTimelineEvents_AuthorNoPressure_SoWrappedEventsAreInertForNow`) goes **red** when the pass
  lands, which is deliberate.
- **The wrapper's equal active/success/failure magnitudes are a placeholder ratio, not a settled
  shape.** The ruling fixed direction only. Wave 4 decides whether the volume knob is a tuning ratio
  or three authored magnitudes per event.
- **`TimelineEventAdapter.Adapt` is an instance method, not static**, and returns a discriminated
  `AdaptationOutcome { Kind, CivicEvent?, AuthoredCivicEventId }` over
  `{ NoEvent, Dropped, Wrapped, Authored }` — **not** a bare `null`. The published seam in
  `0004-wave-3-lanes.md` was amended mid-wave. Treating a null as "drop it" would have silently lost
  every authored event.
- **No adapted event can currently reach a story by any path.** A wrapped event carries
  `TriggerKind.Manual`, which `StoryAssembler` deliberately excludes from the pool
  (`StoryAssembler.cs:138`) because delivery is the introducing system's job — and **that system does
  not exist**. Wave 4 builds it or the timeline half of the catalog is dead weight.
- **Two metrics cannot reach 1.0 and nothing said so before this wave.** `serviceCoverage` is the
  mean of **nine** channels with garbage, transit, water and electricity hard-zeroed (the game has no
  coverage concept for them), so it tops out at **5/9 ≈ 0.5556**. `pollution` is the mean of four with
  water hard-zeroed: **0.75**. `CivicEventCatalogLoader.AttainableMaximum` publishes both. A
  threshold of 0.45 on `serviceCoverage` is 81% of everything attainable, not "a bit over half".
- **`StorySlot` records no district id.** So a district-scoped `relativeToBaseline` check has no
  baseline (`StoryAssembler.Baseline` returns null for any non-city scope) and resolves
  `Unmeasurable` forever; and an `anyDistrict` check is answered by the city's *healthiest* block
  rather than the one the story is about. Both are now load-time failures. **If wave 4 adds a
  district id to `StorySlot`, both rules should be revisited** — they exist because the information
  is absent, not because the shapes are inherently wrong.

---

## The six load-time rules wave 3 added, and why

Every one ends a failure mode that was **silent**: a check that reads like a goal and cannot function
as one. All were found by review, none by the suite. Wave 4 should expect the same class in the tick
layer.

| Code | Rule | The silent failure |
|---|---|---|
| 116 | `BaselineCheckAtDistrictScope` | Relative check at district scope → `Unmeasurable` forever, scores in neither half of the 2-of-3 |
| 117 | `DistrictCheckNotBoundToTrigger` | Answered by the healthiest district, not the one the story is about |
| 118 | `PressureSignFlip` | Success rewards the party that opposed acting |
| 119 | `CheckThresholdLeavesTrapBand` | Player fixes the right district, loses over one never mentioned |
| 120 | `CheckWindowOutrunsStoryLife` | Half the verdict decided before the card appeared |
| 121 | `ThresholdAboveAttainableMaximum` | A threshold above what the sensor can ever report |

**Rule 121 initially had an `absent`-shaped hole of its own**, found by lane 3e: scoped to `metric`,
it let `absent serviceCoverage gte 0.9` through, whose inner condition can never be met and therefore
negates to `Met` on every city forever. The generalisable lesson, now in the code: **a ceiling must be
checked wherever a threshold is read, not only where it is read positively.**

---

## What nearly went wrong

- **The wave-2 handoff was never written.** Commit `e44281e` is titled "Write the wave-2 handoff" and
  describes the file in detail; its diff touches only `docs/status.md`. Wave 3 reconstructed it as
  `docs/plans/0004-wave-2-handoff.md`, labelled as a reconstruction. **A commit message is not
  evidence that a file exists** — `/commitpushpr` step 6 should be followed by a check that the path
  is in the tree.
- **Three of the wave's defect classes were the orchestrator's, not the lanes'.** The story-life
  literal (above); the two unscoreable check shapes, discoverable from neither the schema nor the
  loader; and a spine positive-test fixture that itself contained a trap band, caught by the rule the
  moment it was written. **A positive fixture is the easiest place for this defect family to hide**,
  because nobody scrutinises a test that passes — two of lane 3e's `AssertClean` fixtures had the
  same problem.
- **A spine commit went out with the suite red.** The ceiling check was placed inside
  `ValidateMetricSpec`, which runs *before* the threshold is parsed, so it compared against a default
  zero and never fired. Caught by the test written in the same commit; fixed in `e29b36f`.
- **Three lanes read one brief sentence identically and all landed wrong** on the pressure
  convention. That is evidence about the brief, not three independent lapses — which is why the
  ruling is written on the contract type and machine-checked rather than restated in a lane brief.
- **A reviewer claimed the census question is now statically resolvable from `refsrc`. It is not.**
  `m_CollectionType` is a field on `StatisticsPrefab`/`StatisticsData` — authored asset data — and
  `refsrc` is decompiled C# only. Wave 1's runtime census remains the only answer.

---

## Manual gates opened by wave 3

**None of wave 3's own code is game-facing**, so it opens no new gate of its own. But it makes two
existing gates *blocking on content correctness* rather than merely outstanding:

1. **The `AGORA-STATCOLLECTION` census (wave 1, gate 1) now gates real authored numbers.** Five
   metrics — `births`, `deaths`, `citizensMovedIn`, `citizensMovedAway`, `movedAwayUnhappy` — carry
   `delta`-only triggers by owner decision, and the loader enforces that. But **their threshold
   magnitudes remain provisional**: a delta survives the units ambiguity in direction, not in
   magnitude. Two shipped events (`glob-population-exodus`, `glob-unhappy-departures`) state
   PROVISIONAL in their `notes` and name the reading they assume.
2. **Wave 1 gate 11 (`unlockedFeatureIds`) gates the entire `Unlock` trigger kind.** Until someone
   reads real feature names off a save, no event can use one.

**Still outstanding and still blocking:** all five of wave 0's gates and all sixteen of wave 1's.
None has been walked.

---

## Known gaps, recorded rather than closed

- **`districtAffinity` is empty on every authored event, and nothing reads the field.** There is no
  district-archetype vocabulary anywhere in `Agora.Core` — `FactionArchetypes` is issue×direction and
  `Parties.ArchetypeId` is party brands, neither is a district taxonomy. Empty means "evenly", which
  is the honest answer until such a vocabulary exists. A decision for a later wave, not a defect.
- **Two ungroundable thresholds**, both flagged in-file for wave 7: `rent` (game currency, no
  reference max — `PropertyUtils.GetRentPricePerRenter` derives it from `EconomyParameterData` prefab
  values absent from `refsrc`) and `uncollectedGarbage` (raw kilograms, no documented ceiling). Lanes
  3a and 3b initially disagreed by 12× on the rent figure; they now agree on order of magnitude,
  which is the best available without a save reading.
- **`commuteMinutes` is unvalidated.** `SensorCalibration.CommuteTimeToMinutes` defaults to `1.0`
  and its own doc comment says the conversion is unverified in-game, so the metric is
  `Worker.m_LastCommuteTime` in raw simulation units under a name claiming minutes.
  `na-transit-referendum` triggers on it.
- **`trafficCongestion` may be sign-inverted.** `AgoraMobilitySensorSystem` normalises
  `TrafficFlowSystem.cityAverageTrafficFlow`, which is a *flow* figure and plausibly higher-is-better.
  If so, an event triggering on congestion fires on free-flowing cities. **Not resolved** — it belongs
  to whoever owns the mobility sensor, and `na-highway-widening-fight` rides directly on it.
- **Three `notes` in `events_eu.json` still assert a bar sits above ordinary drift** without evidence,
  and one justifies a threshold as "a quarter of what the severity-4 events ask" when the ratio no
  longer holds after the story-life rescale. `notes` is authoring metadata, not player-facing prose;
  routed to wave 7's calibration pass.
- **`data/CLAUDE.md`'s file list does not mention `events_*.json` or `timeline_adaptation.json`.**
  Wave 7d owns that doc; a stale list actively misleads a content author, so it is named here.

## Verification recorded

- `dotnet build Agora.sln` — **0 warnings, 0 errors**, toolchain mode. Run once, at the end; **this
  build deploys** to the player's live `…\Mods\Agora.Mod`. It also retroactively closes wave 2's
  "deploying build not walked" item.
- `dotnet test tests\Agora.Core.Tests\Agora.Core.Tests.csproj` — **1978 passed, 0 failed**
  (from 1703; **+275**).
- `cd ui && npx tsc --noEmit` — clean. **No `ui/` file changed this wave.**
- **Schema versions: only `engine_tuning` moved, 6 → 7**, because `stories` gained a required
  property. No sidecar version moved — state 6, settings 4, metric history 1, flavor cache 2 — because
  wave 3 persisted no new field. Civic events and the adaptation policy are shipped content, not save
  data, so **there is no migration to write and none possible**.
