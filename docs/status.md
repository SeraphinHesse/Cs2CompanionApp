# AGORA — Status

**Current milestone:** M6 · The Spectacle (in progress) — with a **fix-plan pass** (`fixplan.md`)
running ahead of it against defects found in the first real play session.
**Updated:** 2026-08-19

> **The fix-plan pass is code complete across all seven workstreams.** W0–W6 and the backlog are
> merged and independently reviewed; W5's popup lane, the largest remaining piece, was planned in
> `docs/plans/0003-w5-popup-lane.md` and executed across six chunks. Statuses below are keyed to
> artifacts that exist in the tree; where a **gate** has not been re-walked since the code landed, it
> says so rather than claiming a pass.
>
> **What remains is the manual gate, and only the player can walk it** — see "The manual gate" below.
> A green build is not a passed gate: much of this pass is reachable only through `Unity.Entities` /
> `Game.*` and so has manual gates rather than tests, by design rather than by omission.

---

## Where the build actually is

The mod **deploys, loads in-game, ticks the heartbeat, and renders four dashboard panels**
(`council`, `parties`, `stories`, `districts` — see `TAB_ORDER` in `ui/src/shell/state.ts:39`).
The news tab is **struck** as of wave 7's spine; the alert lane it shared is not, because that queue
also carries elections, coalition changes and party founding — see wave 7 below. The engine,
elections, government, flavor, effects and story layers are all implemented in `Agora.Core` /
`Agora.Mod`. **Every claim about what the story layer looks like on screen is a review's reasoning
or a gate row** — see the manual gate ledger.

| Milestone | Code | Gate |
|---|---|---|
| **M0 · Bootstrap** | ✅ | ✅ **passed 2026-07-30** (see `politicsmodplan.md` §11) |
| **M1 · Time & Truth** | ✅ `AgoraTimeService`, `AgoraStartYearSystem`, `StartYearDelivery`, `SimClockMath`, sensors, sidecar IO | ⚠️ save→quit→load ×10 desync check not re-walked since W0's per-save bug was found |
| **M2 · The Engine** | ✅ blocs, affinity, turnout, parties, factions, polling, indices, dashboard | ⚠️ not re-walked |
| **M3 · The Voice** | ✅ `IFlavorProvider`, `ClaudeCliProvider`, `LayeredFlavorProvider`, static pool fallback, prompt builder, schema validation, flavor cache | ⚠️ fail-closed path implemented; **prose quality is a known defect** — see W2/W5 |
| **M4a · Elections** | ✅ `Engine/Elections/Proportional` + `Fptp`, polling, manifestos, fringe ceiling (packet 15) | ⚠️ not re-walked |
| **M4b · Government** | ✅ `Engine/Government/Coalitions` + `Mandates`, party lifecycle | ⚠️ not re-walked |
| **M5 · The World** | ✅ effect palette + dispatcher + resolver + schedule + validation; `Agora.Mod/Effects` ledger and application system; `data/timeline_eu.json`, `timeline_na.json`, `timeline_global.json` | ⚠️ 1990→2008 run not re-walked |
| **M6 · The Spectacle** | 🟡 partial — crosstab explorer, mandate tracker, news archive present; **political map overlay and election-night broadcast mode not built** | ⬜ |

**Test suite.** `tests/Agora.Core.Tests` is at **2182 tests** on `event-system/wave-7` (1319 at the
end of the fix-plan pass, 1033 at its start), spanning determinism, blocs, affinity, turnout,
polling, indices, both electoral systems, coalitions, mandates, factions, party lifecycle,
the fringe ceiling, the
effect palette and application, the per-save reset seam, the scheduler, sim-clock math, start-year
planning, the shipped timeline/tuning catalogs, party identity locks, and the LLM response path —
the CLI reader, the prompt builder, and the schema/numeric validation that enforces
non-negotiable #1. It still runs with **no copy of the game installed** — that constraint is the
test that the Core/Mod split is real.

Build: `dotnet build Agora.sln` · UI: `cd ui && npm run build` ·
Test: `dotnet test tests\Agora.Core.Tests\Agora.Core.Tests.csproj`

---

## Active work — `fixplan.md`

The first play session against a loaded city produced five reported issues, which resolved into
seven workstreams — two of the five were several independent bugs each. `fixplan.md` is the
authority; this is the tracker.

| WS | What | Phase | Status |
|---|---|---|---|
| **W0** | Per-save reset seam — three layers retain the previous city's state across a main-menu round trip. The only *data-corrupting* bug of the seven. | 1 | ✅ code complete, review passed, **merged to `main`** · **ECS half needs the manual walkthrough** |
| **W1** | Readability — four panels each declare their own opacity, lowest 0.62. Shared `_tokens.scss`. | 2 | ✅ code complete, review passed, **merged to `main`** |
| **W2** | Party names lock in — flavor roster is never set before the first prose poll, so parties render as `party-01`. | 2 | ✅ code complete, review passed (one blocking defect found and fixed), **merged to `main`** · **needs the manual walkthrough** |
| **W3** | EU/US theme chosen by the player — `RegionTheme` has no selection surface; always defaults to `Eu`. First-run flag dialog. | 3 | ✅ code complete, review passed (two blocking defects found and fixed, then re-reviewed), **merged to `main`** · **needs the manual walkthrough** |
| **W6** | Parties tab — panel does not exist; `PartyBriefPayload` lacks the fields. | 4 | 🟡 **chunks A–H all merged to `main`** (bindings, tab shell, manifesto/drift, poll trend, history strip, mandate scorecard, coalition relations) · **every chunk reviewed and approved**, H9 after one blocking fix |
| **W4** | Player-owned party identity — inline rename/recolour, with locks that stop flavor clobbering them. | 4 | ✅ **complete.** Lanes A–C and **lane D** all code complete, reviewed, merged · lane D's five text fields are the **first text entry anywhere in `ui/src`** and carry a real manual gate (see below) |
| **W5** | The press — articles lead with what happened, masthead popup, sim pause, Haiku for cost. | 5 | ✅ **code complete.** Prose + model lane, and the whole popup lane (`docs/plans/0003`): C1 binding surface, C3/C4 the two missing feed rows, C2/C5 severity gate + ring + emission, C6/C7 modal + pause + interlock + masthead, C8 join. `PauseOnMajorNews` and `ShowAllReports` now do something. **C0's in-game spike was deliberately not built** — folded into the manual gate · **needs the walkthrough** |
| — | Backlog (correctness + affordance) | 6 | ✅ **all items closed, reviewed, merged to `main`** — envelope unwrap fixed, two raw-id leaks fixed, scrollbar item struck as verified-false, contract drift audited (3 prose defects fixed) · **both owner decisions now resolved** (see below), **the drift re-run is done** (2026-08-09, 44 bindings, shapes clean, six prose defects fixed) |

## Event-system rework — `docs/plans/0004-event-system-rework.md`

Eight sequential waves on `EventSystemRefresh`, each one umbrella branch with parallel disjoint
lanes. `/nextwave` opens a wave, `/commitpushpr` closes it.

| Wave | What | Status |
|---|---|---|
| **0** | Tick correctness prerequisites — the reload double-tick, and trend memory that died at every save boundary. Not story-specific; stands on its own. | ✅ **code complete**, three lanes reviewed and merged, PR open into `EventSystemRefresh` · **five manual gates outstanding**, see below · 1415 → **1442 tests** |
| **1** | Sensors and city statistics — what the game's own statistics screen shows, plus tourism, progression and per-resource taxes. `CitySnapshot` v4. | ✅ **code complete**, five lanes reviewed and merged, PR open into `EventSystemRefresh` · **sixteen manual gates outstanding, none walked**, see below · 1442 → **1469 tests** |
| **2** | Story engine core — the declarative trigger grammar, seeded drafting, the 2-of-3 resolution and the political-power currency. Pure `Agora.Core`. State v6, settings v4, `engine_tuning` v6. | ✅ **code complete**, five lanes reviewed and merged, PR open into `EventSystemRefresh` · **no new manual gates** — all of it is covered by the suite · 1469 → **1703 tests** |
| **3** | Catalog and content — 58 authored civic events, a validating catalog loader, and the timeline adapter. Pure content plus `Agora.Core`. `engine_tuning` v7. | ✅ **code complete**, five lanes reviewed and merged, [PR #6](https://github.com/SeraphinHesse/Cs2CompanionApp/pull/6) open into `EventSystemRefresh` · **no new manual gates of its own** · 1703 → **1978 tests** |
| **4** | Tick wiring, effects and persistence — the cycle runs, effects dispatch, power moves, and stories move votes. `engine_tuning` v8. | ✅ **code complete**, eight lanes reviewed and merged, [PR #7](https://github.com/SeraphinHesse/Cs2CompanionApp/pull/7) open into `EventSystemRefresh` · **fifteen manual gates outstanding, none walked** · 1978 → **2109 tests** |
| **5** | Prose — both writers now produce a headline and an article for every story. The canned pool transcribes from the civic catalog and is the everyday voice; Claude is woken on the story-draft month and its prose is **added beside** the pool's, never over it. `politics_flavor` v3, `engine_tuning` v9, **settings v5 and state v7** — the first real sidecar migration since wave 1. | ✅ **code complete**, four lanes reviewed and merged, [PR #8](https://github.com/SeraphinHesse/Cs2CompanionApp/pull/8) open into `EventSystemRefresh` · **seven manual gates outstanding, none walked** · 2109 → **2178 tests** | 
| **6** | UI — the story system becomes visible. A fifth dashboard tab, a story card that interrupts once per story, a political-power counter, and the five commands wired at last. `ui_bindings.md` v9; **no sidecar schema moved at all.** | ✅ **code complete**, four lanes reviewed and merged, [PR #9](https://github.com/SeraphinHesse/Cs2CompanionApp/pull/9) open into `EventSystemRefresh` · **nineteen manual gates outstanding, none walked** · 2178 → **2178 tests, unchanged and correct** — see below |
| **7** | Retirement, balance and gates — the news feed retires, the two published-but-dead settings get the presets behind them, the column budget is repaired, and the documentation the rework owes lands. `ui_bindings.md` v10, settings v6, state v8, all in the spine. | 🟡 **in progress** — spine landed (`f1259b8`), seven lanes open, ownership in `docs/plans/0004-wave-7-lanes.md` · base measured at **2182 tests, 0 failed** after the spine · **no new manual gates from 7d**; the wave's own rows land with its lanes |

### The manual gate ledger — nothing has been walked, and that is the real state of this rework

This is a section rather than a footnote because it is the single largest outstanding claim in the
project. **The code is reviewed and the game has never been played with it.** The whole story system
— the cycle, the effects, the power economy, the fifth tab, the card that holds the clock — has been
built, reviewed adversarially and typechecked. **No row below has been walked. Not one.**

| Wave | Rows | Where they are written out | Walked |
|---|---|---|---|
| **0** | 5 | `docs/status.md` § "Wave 0's manual gates", below | none |
| **1** | 16 | `docs/plans/0004-wave-1-handoff.md` | none |
| **4** | 15 (rows 0–14) | in full below, § "Wave 4's manual gates" | none |
| **5** | 7 | in full below, § "Wave 5's manual gates" | none |
| **6** | 19 | `docs/plans/0004-wave-6-lanes.md` § "Manual gate rows this wave opens" | none |
| | **62** | | **0** |

**The published figure is fifty-one and the rows add to sixty-two — both are recorded here rather than
one being quietly picked.** `docs/plans/0004-wave-6-handoff.md` states "total outstanding: fifty-one
rows" in the same paragraph that names 5 + 16 + 15 + 7 = 43 from earlier waves, which leaves 8 for a
wave whose own lane document enumerates 19; `docs/plans/0004-wave-7-lanes.md` then carries the 51
forward twice, including into the orchestrator's walkthrough deliverable. **Sixty-two is the count of
rows that actually exist**, and the reconciliation belongs to whoever writes that walkthrough — it is
the document that has to name every row, so it is the one place the discrepancy cannot survive. A
walkthrough written to a target of 51 would silently drop eleven gates, which is why this is here and
not in a footnote.

**Why so many, and why none is a missing test.** `AgoraRuntime`, `AgoraRuntime.StoryCommands.cs`,
everything under `src/Agora.Mod/UiBindings/` and every `GameSystemBase` sensor link into **no test at
all**, by design: `tests/Agora.Core.Tests` must run with no copy of the game installed, and that
constraint is the test that the Core/Mod split is real. A gate row is what evidence looks like on the
far side of that boundary. **No coverage has been manufactured for any of them**, and faking the
runtime to produce a number would be a review-blocking defect rather than a fix.

**The four worth walking first**, because each one decides something rather than confirming it:
wave 0's double-tick (the whole power economy rests on the month running once); wave 1's
`AGORA-STATCOLLECTION` census (it decides whether five metrics mean "this month" or "since founding",
and thresholds were authored without the answer); wave 4's rewound load (no test by construction, and
three lanes were misdiagnosed before it was found); and wave 6's text entry under Gameface (six
textareas per story, and the highest-risk unverified area in the project).

### Wave 3 — the engine now has something to read, and still nothing runs it

`data/events_{global,eu,na}.json` carry **58 authored civic events** (27 global, 15 EU, 16 NA), each
with a declarative trigger, a resolution check, capped effect ids, three issue pressures and seven
prose fields. `CivicEventCatalogLoader` validates them at load. `TimelineEventAdapter` plus
`data/timeline_adaptation.json` express the owner's 25/50/25 split over the 120 shipped timeline
events **without deleting any of them** — the boring quarter is marked `none` and keeps firing as
timeline events exactly as before.

**Nothing calls any of it**, unchanged from wave 2. Wave 3's claim is that the content is authorable,
reachable and honest — not that anything happens.

**Every one of the five lanes was blocked at least once, and every block was a real defect a green
suite had waved through.** The wave's whole defect family had one shape: *a check that reads like a
goal and cannot function as one*. Six became load-time rules (`CatalogIssueCode` 116–121):

- a relative check at district scope → `Unmeasurable` forever, scoring in neither half of the 2-of-3
- an `anyDistrict` check answered by the city's healthiest block, not the one the story is about
- a mirror-negated success pressure that rewards the party which opposed acting
- a check threshold tighter than its trigger, failing the player over a district never mentioned
- a check window outrunning the story's life, deciding half the verdict before the card appeared
- a threshold above what the sensor can ever report

**Three of those classes were the orchestrator's defect, not a lane's.** The sharpest: every content
lane was handed `cycleMonths` (2) as the window a player can influence, when a story actually lives
`cycleMonths - 1` — **one month**. Roughly 40 thresholds across two files had to be re-derived by
hand, because a mechanical `2 → 1` would have silently *doubled* the difficulty on any threshold
sized for the wider span.

**Two sensor ceilings are now published** that nothing had recorded anywhere an author would look:
`serviceCoverage` is the mean of **nine** channels with four hard-zeroed, so it tops out at
**5/9 ≈ 0.5556**; `pollution` tops out at **0.75**. A threshold of 0.45 on service coverage is 81% of
everything attainable, not "a bit over half". See `CivicEventCatalogLoader.AttainableMaximum`.

**Two of the plan's authorable trigger kinds are not authorable.** `CitySnapshot.ActivePolicyIds` is
written by no sensor at all, so a `Policy` trigger can never fire and an `Absent` policy trigger fires
on every city forever — the loader now rejects the kind by name. And `Unlock` ids are raw prefab-name
strings nobody has read, so wave 1's unwalked gate 11 gates that kind entirely.

**The deploying `dotnet build Agora.sln` was run** (0/0), which also closes wave 2's outstanding
"not walked" item retroactively.

Full detail, including the ruling that event pressures are **salience rather than credit** and the
`/schema-change` wave 4 must run *before* its `issuePressure` authoring pass, is in
`docs/plans/0004-wave-3-handoff.md`.

### Wave 2 — built, and not yet reachable by a player

The engine can decide what a city's political stories *are*. **Nothing calls it.** No tick drafts, no
UI renders a story, no effect is dispatched, no power is ever awarded in play, and there is no
catalog for it to read — `data/events_*.json` is wave 3. That is the honest state: wave 2's claim is
that the arithmetic is right, not that anything happens.

It is the first wave of this rework with **no manual gate of its own**, because it contains no
game-facing code. Waves 0 and 1 remain unwalked; that is unchanged and still blocking.

**Twelve rulings were taken mid-wave** and are recorded with their reasoning in
`docs/plans/0004-wave-2-lanes.md`. Wave 3 authors content directly against several. The two that
will bite hardest:

- **`Manual` is a trigger *kind*, not a tier.** A `Manual`-triggered event is never pooled and can
  never produce a story; "mandatory" is a *tier* derived from severity. A mandatory-severity event
  still needs a real trigger. Two lanes read the earlier wording and built opposite things.
- **An `Absent` trigger with a misspelled metric id evaluates `Met` on every city forever.**
  `MetricId` carries three vocabularies and only one is validatable, so wave 3's catalog loader must
  require a non-registry id to appear in an authored id list.

**One verification not run:** the deploying `dotnet build Agora.sln` was blocked by a permission
classifier. Wave 2 adds no ECS code, so no source-generator coverage is missed — recorded as not
walked rather than assumed fine.

**Three of the wave's five defects were found by executing code that had already survived rounds of
careful reading**, including a determinism argument asserted as neutral that changed results in 529
of 4320 configurations. Reviewing on this wave was worth more than reading it.

**Wave 0 also repaired a red base.** `EventSystemRefresh` did not build and had five failing tests
before the wave opened, from three prior commits landing unverified. Fixed as its own commit so the
inherited breakage stays distinguishable from the wave's own work.

**Three findings that contradict the rework plan** and outrank it for later waves, recorded in full
in `docs/plans/0004-wave-0-handoff.md`: `metric_ring.json` was never built because `MetricHistory`
already was one; wave 1's `metric_history` schema bump is probably unnecessary because the file is a
keyed series bag; and `TickPlanner`'s poll cadence had no arithmetic slip to fix, only an intent to
decide — it is now `pollTickIntervalMonths`, default 1, behaviour-identical for every existing save.

### Wave 1 — what it added, and what it corrected

Three new sensor systems read `Game.Simulation.CityStatisticsSystem` — the same source the game's own
city statistics screen reads, which was the owner's stated constraint. `CitySnapshot` is at
**schemaVersion 4**: homelessness, migration, births, deaths, garbage production and uncollected
garbage, tourists, attractiveness, lodging, milestone level, lifetime XP, unlocked feature ids and
per-resource tax rates. The pure half was widened in step, so the new scalars survive a reload
instead of reading as fabricated zeros on the first tick after every load.

**No sidecar or binding version moved.** Only `snapshot` 3 → 4, which is not a sidecar document and
so has no migration to write; its C# and JSON sides are now pinned **to each other** by a
version-relative test rather than to a memorised literal.

`docs/scout/0004-city-statistics.md` is the new authority on what the game exposes, with file and
line numbers. **Anything not marked CONFIRMED there does not get a trigger in wave 3.** Six of the
rework plan's assumptions were wrong and are corrected in full in
`docs/plans/0004-wave-1-handoff.md`; three matter beyond this wave:

- **There is no landmark count** anywhere in `Game.dll`. Shipped as `SignatureBuildingCount`.
- **Garbage "accumulation" is a production rate per day, not a stockpile** — the game's own binding
  calls it `productionRate`. The backlog is a separate field, and it is not the infoview's "stored
  garbage" either, so prose must say *uncollected*, never *landfill*.
- **Birth and death rates are readable, and always were.** The "Known gaps" note below recording
  birth rate as unreachable was about `CityModifierType` — nothing can *modify* it, which is a
  different claim from being unable to *read* it. Corrected inline below.

### Wave 1's manual gates — code built, nothing seen in game

Lanes 1a, 1b and 1c are `GameSystemBase` and compile into **no test at all**, by design. Sixteen gate
rows are listed in full in `docs/plans/0004-wave-1-handoff.md`; **none has been walked.** The one
that blocks later work:

- **`grep AGORA-STATCOLLECTION Agora.log`** must show exactly one census with a non-zero prefab
  count. The `collection=` values for `BirthRate`, `DeathRate`, `CitizensMovedIn`,
  `CitizensMovedAway` and `MovedAwayReason` decide whether those five mean "this month" or "since the
  city was founded" — and **wave 3 cannot author a threshold on any of them until that is answered.**

The rest cluster into units that look plausible either way (a homeless share of `3.0` where `0.03`
was meant; tax rates as `20.0` rather than `0.2`), counts that must move for events and not for
menus (a placement preview must not raise the attraction count), and the per-save reset (load city A
then city B without restarting; B's first snapshot must not carry A's figures).

### Wave 4 — it runs, and nobody has seen it run

The tick now drafts stories on one phase and resolves them on the next; `StoryCycle` sweeps stranded
stories, trims the archive and suspends entirely under replay; `StoryEffects` turns authored effect
ids into capped requests; `PowerLedger` accrues, awards, spends and charges debt; and
`AffinityEngine` gained a **story term that did not previously exist** — so for the first time a
story's issues and its verdict move votes. `AgoraRuntime` also **loads the civic catalog**, which
nothing in the assembly had ever mentioned, and all 90 generically-wrapped timeline events now author
an `issuePressure` where before they were politically inert.

**What has only been built, not seen:** every word of that. No player has viewed a story — there is
no card, no modal and no prose, and the four inbound commands (`SetStoryResponse`,
`DeclareManualOutcome`, `ResolveNow`, `SpendPowerOverride`) compile, are reviewed, and **have no
caller and no binding**. Wave 5 writes the prose; wave 6 builds the surface and owes **four** binding
registrations rather than the three the plan's table lists.

**Every one of the eight lanes was blocked at least once**, and every block was a real defect a green
suite had waved through — but the family was different from wave 3's. Wave 3 produced checks that
read like a goal and could not function as one. Wave 4 produced **derived numbers no green suite can
see**:

- severity clamped to a constant, so a severity-1 minor story did exactly as much damage as a
  severity-5 catastrophe — and the cap test passed *because* all five clamped to the same value
- **102 of 277 authored effect references (36.8%)** silently skipped for want of a district id, and
  47 of 174 effect phases resolving to literally nothing, behind an honest comment
- a breadth cap bounding one story against a ledger limit that 30 cycles of consequences overlapped
- three lanes each appearing to have a rewind defect, all standing on one spine omission: wave 0's
  watermark repair covered one field, and wave 4 added three more

Every one was found by a reviewer probing arithmetic. **None was found by a test.**

### Wave 6 — a player can finally see it, and no player has

There is a fifth dashboard tab (**Stories**, third in the strip), showing live stories with **both**
prose voices, three "Tackle &lt;event&gt;" controls per story expanding into four response options, six
textareas, a **Resolve now** control and the archive below. A story card interrupts on the draft
month — **one card per story, never one per event** — holding the clock only when the engine says the
story is major, and **dismissing it answers nothing**: the story stays live and is tackled from the
panel. A political-power counter sits beside the mod icon and **hides entirely** when the save has
the power layer off, because a zero is a balance and "there is no such currency here" is not one.
`docs/contracts/ui_bindings.md` is at **schemaVersion 9** with a new `agora.stories` group of ten
bindings, five of them inbound.

**What has only been built, not seen: every word of that.** Nobody has run the game. Every claim
above is a review's reasoning or a gate row.

**No sidecar schema moved, and `data/` is byte-identical to the base** — the first wave of this
rework that persists nothing new, and the reason existing saves are entirely unaffected by it.

**The suite is unchanged at 2178, and that is the correct outcome rather than a defect.** Every file
this wave touched is in `UiBindings/`, `AgoraRuntime` or `ui/`, none of which links into the headless
suite by design. No test was deleted (verified against the base) and **no coverage was manufactured**.
The wave's evidence is four adversarial reviews and nineteen gate rows. It is recorded here so that
nobody later closes the gap by faking the runtime.

**Two lanes were blocked; every defect found was found by review, never by a test** — and three of
the four landed in files the lane that found them was forbidden to touch:

- **`SpendPowerOverride` read half the power switch.** It guarded on tuning and never on the per-save
  `PoliticalPowerEnabled`. Latent until this wave, because the method had no caller. On a save with
  the per-save switch off, the counter hides and the projection quotes a cost of 0 — **and the
  purchase would still have been accepted and debited against a balance the player cannot see.**
- **The settings drawer had no height cap and no scroll**, and wave 6's four rows would have taken it
  from ~969rem to ~1770rem — taller than the screen, with every story row below the edge and no
  scrollbar to reach them. Capped at 620rem.
- **`ValueRequired` told story players to press a party-editor button.** One outcome map serves every
  inbound binding, and the most reachable path to that code in the mod is now a story success
  declared with an empty justification.

**Two decisions worth knowing about.** `powerIntensity` and `storyDifficulty` are published but got
**no write key**: `TuningPresets.Apply` reads three levels and there is no preset table behind either,
so a control would persist a value and change no number — the defect W5 closed for
`PauseOnMajorNews`. Wave 7b ships the presets and the keys together. And **nothing lets a player stop
a story card pausing their game**: `pauseOnMajorNews` governs the news lane and was correctly not
repointed, so the answer is a fifth setting, which is a persisted field and therefore 7b's.

Full detail, including the trap that **wave 7a's deletion of `ui/src/panels/News/**` will break
`StoryModal` unless four helpers move first**, is in `docs/plans/0004-wave-6-handoff.md`.

### Wave 5's manual gates — the wake, the migration and the write-back

Wave 5 opens seven. Unlike wave 4's, most of wave 5 **is** covered by tests — `FlavorPromptBuilder`,
`FlavorValidator`, `StaticPoolProvider`, `FlavorCacheMigration` and `StoryProseLedger` are all
`<Compile Link>`-ed into `Agora.Core.Tests`, and the suite went 2109 → 2175. These six are what is
left over: the runtime wiring in `AgoraRuntime.cs`, which compiles into no test, and the two things
only a real save can show. **No coverage was manufactured for any of them.**

Gates 1 and 2 guard an owner decision; gate 3 guards the migration that reaches every existing save;
gate 7 covers the one seam no test in this suite can reach.

1. **The story wake fires, and only when it should.** With `llmWakeOnStoryDraft` true and a CLI
   installed, confirm `Agora flavor: StoryDraft wake requested at <date>` appears on a **draft**
   month and **not** on the month between drafts. At the shipped cadence of 2 that is every other
   month. Then turn `storiesEnabled` **off** for the save and confirm **no story wake at all** for
   at least three cycles — the phase arithmetic still says "draft month", and the gate that stops a
   subprocess starting every two months for stories that will never exist is the one being tested.
2. **The yearly round still writes about stories.** Force a draft on the yearly wake month
   (`llmWakeMonth`, default 1). The round must be labelled `Yearly` **and** still carry story
   sections — the prompt keys on stories being present, not on the wake reason, and a round labelled
   `Yearly` that omitted them would look exactly like the model ignoring an instruction.
3. **An existing save gains the story wake; a customised one does not.** Take a save written before
   this wave (settings v4) whose `wakeCadence` reads `"Yearly, Election, Manual"`, load it, and
   confirm the file now reads `"Yearly, Election, Manual, Story"` at settings v5 / state v7. Then
   take a save whose player had **narrowed** the cadence — e.g. `"Election, Manual"` — load it, and
   confirm it is **unchanged**. Turning a wake back on for someone who turned it off would override
   a decision about how often this mod starts a subprocess.
4. **`localAngle` finally reaches the screen.** After a successful CLI round, confirm a timeline
   event in the News panel shows its local angle. This prose has been parsed, validated, id-checked
   and cached since M3 and written nowhere — the panel published a field no code path ever assigned.
   Confirm too that a later **canned** poll does not overwrite it: the text must not change back.
5. **Claude's story prose is added, not substituted.** Open a story card and read it (canned prose —
   the pool answers immediately). Let a CLI round land for that same story. The text you already
   read must **still be there**, with the model's version alongside it. Nothing a player has read
   may change under them. Then reload: the canned half must come back identical, and the model's
   half must return from `flavor_cache.json`.
6. **A short catalog says so.** Load a save whose `state_*.json` is missing or unreadable while
   `flavor_cache.json` is intact. Confirm the re-validation line reports `0 stories` among its five
   counts **and** a Warn line names the dropped entries. Before this wave the load returned a
   document, logged "restored N entries" at Debug, and was indistinguishable from a clean one.

7. **Story brief fidelity (`AgoraRuntime.BuildStoryBrief`).** Load a save and open a story card for a
   three-slot story whose **major** event's id sorts **last** of the three slot event ids. Confirm
   (a) the headline is the major event's `Name` exactly as authored in the civic catalog — not a
   minor's, not an event id; (b) the article names all three events in the order **major first, then
   the two minors ascending by event id ordinal**, each name followed by that event's `Description`;
   (c) after the story resolves, the card appears under resolutions with a closing lead-in, and each
   `met` / `not met` slot shows its authored `SuccessText` / `FailText` rather than its description.
   **Fails if** the headline names a minor event, any slot is missing from the article, the order
   differs from major-then-ascending-id, or any slot shows a raw event id where a name belongs.
   **Why manual:** `BuildStoryBrief` lives in `AgoraRuntime.cs`, which no `<Compile Link>` line pulls
   into `Agora.Core.Tests`. The automated coverage stops at the `StorySlotBrief` boundary and assumes
   the runtime fills it correctly — every fixture in the suite hand-builds that brief. Wave 5's
   review found the flag half of this was exercised by **nothing** in the repo and added
   `Headline_FollowsTheMajorFlagRatherThanTheSlotPosition` to close the pool's half; this row is the
   runtime half, which no test can reach.

### Wave 4's manual gates — the command surface and the watermark repair

Wave 4 is the first wave since 1 to open gates of its own, and it opens them for the reason the
project keeps re-learning: `AgoraRuntime` and `AgoraRuntime.StoryCommands.cs` compile into **no
test**, so every claim about them is reasoning or a gate row. No coverage was manufactured for any of
this, and the wave's two most valuable fixes are both in here.

**Gate 0 is the one that matters most, because it has no test by construction and three separate
lanes were misdiagnosed before it was found.**

0. **The rewound load reconciles EVERY watermark.** Roll a city save back past the oldest retained
   Agora snapshot and load it. Within three sim months, confirm **a story drafts**, **power accrues**
   (the balance moves on the ledger), and the reconciliation line names the story and accrual
   watermarks alongside the tick one. **The failure it guards is silent**: before the fix the tick
   gate opened, so polls ran, elections ran and failure penalties still debited, while no story
   drafted, none resolved and nothing accrued — for every month between the city's date and the
   stale watermark, with no log line at all. Confirm the line appears **once**, and **not at all** on
   an ordinary mid-month reload.

The command surface (`agora.stories.*`) needs a pressable story modal, which is **wave 6**. Rows 1–14
below run when that lane wires them, and they are recorded here rather than in a transcript because
wave 3's handoff records a commit message that described a file it never created.

1. Set `Ignore` on a live slot, quit to menu, reload. The slot reads `Ignore`, and `state_*.json`
   holds **exactly one** `PlayerCommands` row for that `(storyId, eventId)` — not two, not zero.
2. Answer two different slots of one story in the same sim month. Both rows share a `DecidedMonth`
   and carry `Sequence` 0 and 1 — **not 0 and 0**.
3. Declare a manual success, reload, let the story resolve. The row carries `declaredMet: true`,
   `ManualDeclared` survives the reload, and the award pays at the **minor** rate whatever the
   event's tier — not the mandatory rate, and not zero.
4. Declare a manual **failure** with an empty box: accepted, **exactly one** row with
   `declaredMet: false`. Then declare a **success** with an empty box: `ValueRequired`, **zero** new
   rows, `ManualDeclared` unchanged.
5. Press `Resolve now` five times on one story: success each time, **exactly five** `ResolveNow` rows
   with `Sequence` 0–4, and **no** other story's flag moved.
6. Balance below the mandatory override cost, press the override: `InsufficientPower`, response
   unchanged, **zero** new ledger entries. Set the balance to exactly the cost and repeat: `Ok`,
   balance exactly 0, **exactly one** `OverrideSpend` entry.
7. Balance **−10**, minor override costing 5: `InsufficientPower` — **not** `PowerDisabled`, not
   silent acceptance. Then balance **+60** with debt in the ledger history and a 50-cost override:
   `Ok`, balance 10. Debt is a state, not a bar to play.
8. **The double-charge sequence, three steps, reading the balance at each.** Start at 100, mandatory
   override costing 25. (a) Buy the slot: balance 75, one ledger entry, response `PowerOverride`.
   (b) Press `Goal` on the same slot: `BadValue`, response **still** `PowerOverride`, balance **still
   75**, **no** new `PlayerCommands` row. (c) Press the override again: `Ok` by the already-bought
   guard, balance **still 75**, and **exactly one** `OverrideSpend` entry in total — the balance must
   never read 50.
9. `power.enabled` false, press an override: `PowerDisabled`, **not** `InsufficientPower`, and the
   quoted cost renders 0 rather than a live price against a frozen balance.
10. Hand-edit the minor `overrideCost` to 0 and buy a minor slot: `Ok`, response `PowerOverride`,
    balance unmoved, **zero** ledger entries — and specifically **not** `InsufficientPower`. This is
    the case the old balance-comparison heuristic got wrong.
11. Paste 501 characters into an Ignore box under shipped tuning: `TooLong`, the slot keeps its
    previous text, and nothing was truncated and stored.
12. Pick `Manual`, type a justification, then buy the same slot off: `Ok`, `ManualDeclared` false,
    and `PlayerText` **empty** — the justification must not appear anywhere the panel attributes to
    the purchase.
13. Let a story resolve, then press `Resolve now` from the archive: `AlreadyResolved`, **not**
    `NotFound` — the record exists, the window closed. On an id no story carried: `NotFound`.
14. Declare an outcome on a slot whose response is `Goal`: `BadValue`, response **still** `Goal`, and
    **zero** new rows.

**Wave 6 owes four binding registrations, not three.** None of `setResponse`, `declareManual`,
`resolveNow` or `spendPowerOverride` is in `docs/contracts/ui_bindings.md`; the plan's §605 table
lists three, on the assumption the panel would send `PowerOverride` through `setResponse`. Wave 4
refuses that — it is the free-`Met` hole gate 8 exists for — so a fourth call binding is required.

### Wave 0's manual gates — code built, nothing seen in game

`AgoraRuntime` is not linkable into the headless suite by design, so these are gate rows and no test
was manufactured for them. Gates 2 and 3 exist because review caught the same boundary wrong twice.

1. **The double-tick.** Save mid-month, quit to menu, reload. `Agora.log` and the sidecar must show
   the month running **once** — no duplicate poll, no double-counted `FringeWatch.MonthsObserved`.
   This is the gate the whole political-power economy later rests on.
2. **The clamp must NOT fire on that ordinary reload** — the reconciliation line must be *absent*.
3. **The rewound load.** Roll a city save back past the oldest retained Agora snapshot, load it: the
   reconciliation line appears **once** and the next month boundary actually ticks. A freeze here
   would not show up in gate 1 at all.
4. **Retheme.** Change region mid-month in a month that already ticked; the month must not run twice.
5. **The trend window survives a reload.** Play twelve months, quit to menu, reload, and confirm
   gentrification and brain-drain indices are non-zero on the first tick after the load.

### The manual gate — what only the player can verify

Everything below needs the game running. Nothing here has been seen on screen: the code is reviewed,
built and typechecked, and that is a different claim. **Item E is the one exception, and only in
part** — its table was read off a real save's sidecar and log, which is evidence about engine state
and says nothing about what rendered.

**A. The C0 questions** (the de-risk spike that was deliberately not built — answer these first,
because a "no" means C6's ack path needs revising, which is a one-line fix by construction):
1. Does an alert card **disappear** when Dismiss is pressed? This proves the ack → `_stateVersion` →
   `Publish` round trip. `AgoraUISystemBase.OnUpdate:79-82` gates publishing on `StateVersion`, and
   `AgoraRuntime.AckAlert` bumps it with a comment forbidding the line's removal.
2. Does the clock stop while a **major** card is up, and **return to the prior speed** when it
   closes? An article card must **never** stop the clock, even with both settings on.
3. On a first-run save, does the article modal stay out of the way until the region prompt is
   dismissed? Two pause barriers must coexist and the clock resume only after **both** are gone.

**B. Text entry — the highest-risk unverified area.** W4 lane D's five fields are the first
`<input>`/`<textarea>` anywhere in `ui/src`; `cs2/ui` exports no text-input component. Beyond "do
characters appear": **focus a field and press space, digits, `b`, `p`** — keys bound to game hotkeys
— and confirm the sim does not pause, change speed, or open bulldoze. Nothing in the component stops
key propagation, because there was no pattern in the repo to copy. `<textarea>` is the higher risk.
Then: type past the published limit (counter reddens, engine returns the `TooLong` sentence); pick a
colour another party already wears (amber "already wears this colour", **and the swatch keeps the
new colour** — that is `OkColorInUse` being read as an acceptance).

**C. The fix plan's own walkthrough**, unchanged and still the gate on everything:
> Load city A (EU). Play a year. Rename a party and recolour it. Quit to main menu. Create city B
> and choose US. Confirm: US-flavoured party names, no city A prose anywhere, effects ledger empty,
> heartbeat ticking on day one. Return to city A. Confirm the rename and the colour survived.

**Watch specifically for an alert from city A popping over city B** — the ring is cleared in
`ResetForNewSave`, and that clear is the W0 bug class.

**D. Gameface rendering** that no static check can reach: that the masthead's serif stack resolves to
an actual serif; that a long article body scrolls rather than pushing the buttons off-screen; that
`Portal` overlays the HUD for the modal's subtree as it already does for `FirstRunDialog`.

**E. The parties-tab report** — *"the parties tab isn't showing anything; the US/EU choice doesn't
apply, it's locked to EU; there are no coalitions or factions."*

Three of those four claims were **read off disk and disproved**, which is the first part of this gate
that has evidence behind it rather than only a review. `Agora.log` for the reported session carries no
error, no `could not register its bindings` and no publisher failure, and the save's own sidecar
(`ModsData/Agora/725366ab-…/state_1990_08.json`) says:

| Claim | What the sidecar says |
|---|---|
| "locked to EU" | `theme: "Na"`, `system: "FirstPastThePost"`. The choice **applied**; `themeLocked` is still false, so it was also still changeable. |
| "no factions" | **12 factions** across 4 parties, generated at frame zero as `FactionModel.AppliesTo` requires. |
| "no coalitions" | Correct, and **by design**: coalitions are a proportional feature and this save is FPTP. `electionHistory: 0` and `recentPolls: 0` besides. |
| "shows nothing" | `parties: 4`, all named. The register was there to be shown. |

So all four symptoms are downstream of one bug — a Parties tab that rendered nothing — and the tab was
the only place any of those facts were visible. The prime suspect is a **stale deployed bundle** (the
Parties tab is recent; `ui/npm run build` deploys to `…\Mods\Agora.Mod`). Both halves have now been
rebuilt and redeployed, and the deployed `Agora.Mod.mjs` was grepped for the new strings. **Staleness
cannot be proven retroactively — the rebuild overwrote the evidence — so this is the live hypothesis,
not a confirmed root cause.** What is confirmed is that causes 2 and 3 of that report are ruled out.

Still needing the screen, and nothing below is claimed as walked:
1. New city → the region prompt appears and holds the clock. Choose **United States** → US party
   names, FPTP, factions in party detail. Choose **Europe** → proportional, and coalition arithmetic
   in party detail from the **first published poll** rather than the first election.
2. The **region chip** in the dashboard bar (new): present while `themeLocked` is false, absent after
   the first election, and pressing it opens Settings on the theme picker. This is the standing second
   route to the choice, for the case where the first-run prompt never rendered.
3. `Agora.log` should now carry a `save active at …; theme … (…), N parties, M factions, themeLocked=…`
   line on every load, and a `setTheme("…") requested` line on every press — the two lines that would
   have answered this report without a sidecar read.

**Found while walking this, and *not* fixed — it is a contract change and out of the chosen scope:**
faction **names are generated and then dropped**. `StaticPoolProvider.BuildFactions` names every
faction, `FlavorDocument` parses them into `FactionFlavor` — and `ToPayload` has nowhere to put them,
because `FlavorPayload` (the frozen boundary contract) has no `Factions` collection. Its own remark
says so and says adding one "is a contract change and is reported rather than made here". The
consequence on disk: all 12 factions carry `name: ""` after a completed prose wake
(`lastFlavorDate: 1990-08-01`). The pane counts them and lists no names, which is the honest
rendering of the state, but the state is wrong. **Fix belongs behind `/schema-change`.**

### W5 — what shipped, and what did not

**Shipped, reviewed, committed** (branch `worktree-agent-a1c4d1450a9355a73`, 4 commits on top of the
inherited pair): article `refs` cross the Core boundary and render as chips; the article instruction
leads with what happened and bans unattributed sourcing; election coverage asks for a party's own
claim and own challenge rather than a winner's and a loser's reaction; the canned pool was rewritten
against the same rule with new election templates and now carries `refs` on every article, which is
what allowed `FilterAgainstCatalog` to start dropping refless ones; `--model` with the alias
`claude-haiku-4-5`; and `byline`/`tags` struck from the article contract.

> **Superseded 2026-08-09 — the popup lane is now built.** The paragraph below describes the state
> before `docs/plans/0003-w5-popup-lane.md` was written and executed. Kept as the record of what the
> gap was. All three prerequisites it names were built: a severity gate reading the engine's own
> `MajorSeverityThreshold`, a coalition-formed feed row, and party founded/dissolved rows plus a
> Mod-side detection query extracted into `Agora.Core`.

**Not started: the entire popup lane.** No alert emission, no bindings, no modal, no pause wiring,
no first-run interlock. `PauseOnMajorNews` and `ShowAllReports` remain **two switches that do
nothing**, with hint text promising behaviour that does not exist — that is the most visible loose
end. Three prerequisites are known-missing and are written up in `fixplan.md` §W5: there is no
severity filter anywhere, coalition *formed* produces no feed row, and party founded/dissolved has
neither a feed row nor a tick signal.

**Manual gates outstanding for the shipped half** — none of these can be verified without the game:
prose quality on a real save; the fail-closed path with a bogus `AGORA_CLAUDE_MODEL`; that
`ClaudeCliProvider`'s `ArticlesAllDiscarded` branch keeps last-good rather than blanking the feed
(the branch is untested by construction — the type is game-facing and deliberately unlinked from the
test suite); and that an existing save's `flavor_cache.json` full of refless articles degrades to
canned prose rather than an empty feed.

**W5 deviates from the ratified article count, deliberately.** §11 M3 ratifies 3–5 articles per
wake; an election wake asks for 7 (NA) or 8 (EU) — the ordinary 4 plus one slot per dedicated
election piece — because W5's "elections covered extensively" decision would otherwise buy the
election coverage by cutting general coverage below an ordinary month. Recorded in `politicsmodplan.md`
§11 M3. The extra tokens land on election months only, and elections are 3–4 years apart.

**Phase 1 is code complete and through the checklist gate** (`dotnet build` 0/0 · 1033 tests ·
`npm run check` clean). Nothing is committed. Four review-blocking defects were found and fixed, and
the review passes corrected **eight** places where `fixplan.md` describes code that does not exist —
see `docs/plans/0001-batched-schema-change.md` §9 and `docs/plans/0002-w6-parties-tab.md` for the
list. Two of those change work not yet started: W4's stated enforcement point never writes
`ColorHex`, and W5's article-limit tightening would discard every cached party name unless the cache
load prunes over-length articles only.

**The backlog is closed** (2026-08-08, on a branch off `163e6f2`, four commits, each independently
reviewed against the checklist). `dotnet build Agora.sln` 0/0 · **1092 tests** (was 1083; +9, all for
the envelope unwrap) · `npx tsc --noEmit` and `npm run check` clean. Four items:

- **`ClaudeResponseReader` envelope unwrap** — the one real correctness bug left, and it was
  mislabelling itself. Any byte the CLI emitted after the envelope object made the strict parse
  reject it as trailing content, so the unwrap concluded "not an envelope" and the balanced-object
  scan then extracted *the envelope itself*, which reached the validator as unknown fields. A parse
  seam presented to the player as a bad model response. The reviewer reverted the fix and confirmed
  5 of the 9 new tests fail against the pre-fix code.
- **Two raw-id leaks** in News, closing out W2's "never render a raw id" rule.
- **The Gameface scrollbar item — verified false and struck.** `cs2/ui`'s `Scrollable` draws its own
  DOM track and thumb, styled by the game's global CSS, and appends rather than replaces a
  consumer's `className`. Evidence read out of the shipped `index.js`/`index.css`. Cheaper than the
  speculative CSS indicator the item asked for, and the item appears to have been written from a
  general Gameface intuition rather than an observation.
- **Contract-drift audit** over all 26 `agora.*` bindings. Shapes clean; three defects in the prose,
  fixed. **This must be re-run after W4 and W6 merge** — the plan is right that adding bindings is
  when drift appears, and neither workstream was in the tree for this pass.

Two owner decisions came out of it, both recorded in `fixplan.md` § "Decisions for the owner", and
**both are now resolved (2026-08-09):**

- **`NewsArticle` wire fields with no engine source** — `byline` and `tags` were the two that were
  never populated by any layer (`""` and `[]` on every article, permanently) and are now **struck**
  from the payload, the TS type, `ArticleReader`, and the contract doc. The other three id fields
  (`refs` → `EventId`/`DistrictId`/`PartyId`) were kept and populated; they were already
  catalog-validated and in active use.
- **Crosstab's Turnout mode** — **struck.** Both the coder and reviewer who built it found it
  rendered fifteen visually identical tints with real data, conveying no information. Turnout is
  already readable in two other places that are unaffected by this: the district list row text
  (`DistrictList.tsx`) and the district detail Conditions meter + no-data fallback line
  (`DistrictDetail.tsx`). Routed to whichever lane owns `Crosstab.tsx` to remove the mode from the
  selector and its related state, reviewed like any other change.

## W4 — player-owned party identity

**Lanes A–C are code complete and independently reviewed. Lane D is specified and handed to W6.**

The player can own a party's name and short name, its description and slogan, and its colour. Each
of the three groups is a lock in `Party.PlayerOverrides`, and a set lock bars flavor from that group
for good.

**The enforcement point is one function, and it lives in `Agora.Core`.**
`PartyIdentity.ApplyFlavor` is the lock-aware merge, lifted out of `AgoraRuntime.ApplyProseNames`.
That move is the substance of the work rather than tidying: `AgoraRuntime` cannot be loaded by the
headless suite, so the rule deciding whether a player's rename survives a flavor wake was the one
rule in the mod no test could reach. It is now the rule the mod runs *and* the rule the suite tests.

`fixplan.md` §W4 called `ApplyProseNames` "the single enforcement point". It is one of four —
W2 added a second flavor writer of all four prose fields, `EnsureEveryPartyNamed`, and colour has no
flavor path at all. The fourth was a latent bug that "reset name" would have made live: lock the
description, reset the *name*, and `EnsureEveryPartyNamed` fires on the now-empty name and silently
overwrites the locked description. All five corrections to §W4 are recorded there in full.

**Two bugs fixed that the plan did not know about.** `PartyRegistry.IsColorTaken` compared hex
case-sensitively against an uppercase palette, so a player typing `#c0392b` held a colour that never
registered as taken and the next splinter was handed the identical-looking `#C0392B`. And
`StaticPoolProvider` seeded its uniqueness set only from its own draws, so a newly-named party could
land exactly on the name the player had chosen.

**Eight bindings, not the three the plan listed.** Six writes (`rename`, `setDescription`,
`setColor`, `resetName`, `resetDescription`, `resetColor`) and two reads the plan never mentioned:
`colorPalette`, without which a picker cannot render the swatches the engine assigns from, and
`editLimits`, without which the character counter and the C# rejector are two copies of one number.
Resets are separate bindings because an empty string is `ValueRequired`, never a reset — a cleared
box is a slipped keystroke as often as it is an intention, and the two mean opposite things.
`CommandOutcome` gained four members: `NotFound`, `ValueRequired`, `TooLong` and `OkColorInUse`.
**`OkColorInUse` is an acceptance**, so it does not cross as `""`; both sides now test acceptance
with an `IsAccepted`/`isAccepted` helper rather than against the empty string.

**W4 persists no new field and needed no schema change.** `PlayerOverrides` shipped ahead of it in
plan 0001, with migration.

Three review rounds. The first, over the inherited work, found four blocking defects. The two
sharpest: `PartyBrief` declared `description`/`slogan` in TypeScript and in the contract while the C#
publisher emitted neither — a one-sided schema change that type-checks and hands the panel
`undefined` at runtime — and the UI's outcome map had never learned the four new codes, so an
accepted duplicate colour reached the player as an unexplained failure. The second round, after the
fixes, blocked on two `fixplan.md` checkboxes ticked for UI controls that are lane D and did not
ship; a ticked box hiding unshipped work is precisely what that section is a correction of.

**Not covered by tests, by necessity:** `AgoraRuntime` and `src/Agora.Mod/UiBindings/**` are not
linkable into the headless suite, so the six entry points, the gate locking and the eight binding
registrations have manual gates instead — listed at the end of `fixplan.md` §W4. Nothing was stubbed
to manufacture coverage.

**Out of scope, backlog:** factions have the same flavor-owned fields and no `PlayerOverrides`.
Giving them locks needs a second flags field and its own migration.

**Schema bumps are batched, and the batch has landed.** `docs/plans/0001-batched-schema-change.md`
is complete and reviewed across all three chunks: per-save settings (`ThemeLocked`,
`PauseOnMajorNews`, `ShowAllReports`), `Party.PlayerOverrides`, and the article length limits, in
one sidecar migration rather than three. Sidecar state and settings are now **schemaVersion 2**,
`politics_flavor` is **2**, and the binding contract is **3**.

Two defects in the migration engine were found and fixed that nothing in `fixplan.md` anticipated:
`SidecarSchema.Migrate` stamped the *target* version on an unversioned document without running a
single step — silent, unrepairable data loss the moment a step existed — and the `settings` block
nested inside a state file was never reachable by the settings step table at all. A third, caught in
review, was a one-sided bump of `CurrentFlavorCacheVersion` ahead of the schema it versions.

The article tightening (headline 140→90, body 900→420) ships with `FlavorCacheMigration`, which
prunes only over-length articles at cache load and never truncates. Without it the first reload
after the update would have discarded every cached party name and resurrected the `party-01` bug W2
exists to fix — a consequence `fixplan.md` did not mention.

### The walkthrough that gates the fix plan

> Load city A (EU). Play a year. Rename a party and recolour it. Quit to main menu. Create city B
> and choose US. Confirm: US-flavoured party names, no city A prose anywhere, effects ledger empty,
> heartbeat ticking on day one. Return to city A. Confirm the rename and the colour survived.

Nothing in `fixplan.md` is complete until that passes **without restarting the game**.

---

## Fringe ceiling and 1-year terms (packet 15)

Terms are now **1 year in both themes**, and the NA theme enforces the ratified "two dominant
parties + weak third parties" rule through a new `fringe` tuning packet.

**What was wrong.** Nothing in the voter model converted major-party failure into minor-party gain:
the incumbency and mandate terms are party-scoped and can only *subtract* from the government. A
fringe party's support was platform proximity plus habitual loyalty and nothing else, so minor
parties took 20%+ of an NA ballot for no reason, and no amount of good government pushed them back.

**What it does.** A minor party is pinned at `fringe.baseCeiling` (3%) until the majors have failed
`unlockConsecutiveTerms` (3) terms running; the ceiling then opens toward `maxCeiling` (40%) scaled
by how badly they failed, how long the failure has run, and how aggrieved the city is on that
party's own `CoreGrievance`. One good term resets the streak outright.

- Enforced in **affinity space**, in `AffinityEngine.Compute`'s bloc loop, as an additive shift on
  `BlocAffinity.Affinity`. That one hook covers city standings, published polls **and** election day,
  because `FptpElection` re-softmaxes the same affinities rather than reading the standings — so the
  election packet needs no knowledge of ceilings. `FringeCeiling.cs` / `FringeFailure.cs`.
- **FPTP only.** A proportional save is bit-identical with the packet on and off; the failure ledger
  is gated on the system, not just the master switch, so that claim is testable.
- `parties.deathVoteShareThreshold` dropped 0.03 → **0.01** so the ceiling cannot dissolve the
  parties it suppresses before the unlock can fire. This key is shared with EU, so EU parties now die
  only below 1%.
- `PartyPlatform.RefreshManifesto`, which had been called from nothing but its own tests, is now
  wired at campaign open (edge-triggered). Without it the ceiling is a ratchet — grievance opens it
  and nothing an establishment party can do closes it again.
- `MandateResolution.OppositionSurge` finally has a reader: it is the defiance signal, and it arrives
  salience-weighted at source.

**Known cosmetic consequence.** A capped party settles at `PartyStatus.Endangered` rather than
`Active`, because 3% sits under `parties.endangeredVoteShareThreshold` (5%). Harmless mechanically —
the death counter only starts below 1% — but the Parties tab will show a permanently "endangered"
party that is in fact being held there on purpose. Worth a UI distinction later.

Schema: `engine_tuning` and `political_state` both went **2 → 3**; the v2→v3 sidecar migration
reconstructs `parties[].isMajor` from id order rather than defaulting it, since defaulting to false
would tell the ceiling an existing NA save has no majors and pin its whole ballot.

## Known gaps found this pass, not yet closed

-1. **`SimDate.ToString()` is culture-invariant by accident, not by declaration.** A hygiene gap, not
   a determinism hole — the distinction matters and an earlier draft of this entry got it wrong.
   `src/Agora.Core/Contracts/SimDate.cs:57` is `$"{Year:D4}-{Month:D2}-{Day:D2}"`, and an
   interpolated string formats under `CurrentCulture`.
   **`SeedStreams.Derive` is already immune** and says so: it folds `Year`/`Month`/`Day` in as `int`s
   via `MixInt32` *"so the seed never depends on formatting behaviour"*. The primary seed path was
   never at risk. The real exposure is one level out — `StaticPoolProvider.cs:377` builds an article
   id as `"static-" + request.Date.ToString() + "-" + (i + 1)`, and that id becomes the sub-stream
   key at `:401` which `RngFor` concatenates into the hashed stream name. It is also a **persisted
   article id and part of sidecar filenames** (`AgoraJson.cs:226-228`), which is the bigger of the
   two consequences.
   Safe today: `D4`/`D2` on a non-negative `int` emits ASCII digits under every culture .NET
   supports, and `NegativeSign` is the only culture-sensitive element. `Month` and `Day` are
   constructor-validated; `Year` is not range-checked, but a negative year is a far larger failure
   than a formatting one. `CoalitionFormation` already formats its attempt number with an explicit
   `InvariantCulture` and a comment saying why, so the codebase knows the rule and this site simply
   predates it. **When closing it:** describe the fix as protecting *sub-stream keys, ids and
   filenames* — not "seed derivation", which is already safe by construction — and pin it with a test
   that sets `CurrentCulture` to a hostile culture (`ar-SA`, `sv-SE`) around `SimDate.ToString()`
   itself, not around `Derive`.

0. **Text entry has never been rendered under Gameface.** W4 lane D's five fields
   (`PartyEditor.tsx:253, 266, 338, 436` and the `<textarea>` at `:325`) are the **first
   `<input>`/`<textarea>` anywhere in `ui/src`**. `cs2/ui` exports no text-input component — the only
   trace is a `focusInputField` sound enum in `types/ui.d.ts:195`, i.e. the game has internal fields
   it does not expose — so there was no in-repo pattern to copy and `refsrc/` is C#-only. Beyond "do
   characters arrive", **the test that matters is key propagation**: focus a field and press space,
   digits, `b`, `p`, and confirm the sim does not pause, change speed, or open bulldoze. Nothing in
   the component stops propagation, because there was no established pattern to copy. If it fails,
   the fix is `onKeyDown` stopping propagation or a `FOCUS_DISABLED` scope — a follow-up, not a
   defect in what shipped. **`<textarea>` is the higher risk** of the two.

1. **The test suite is insensitive to coalition majority-iteration order.** Found by W6 chunk H's
   review, which injected `majority.Reverse();` after `MajorityOf(candidates)` in
   `CoalitionFormation.Form` and watched **all 1227 tests still pass**. Chunk H's `RankOf` refactor
   was proved correct by a 3000-chamber differential diff against the pre-refactor implementation,
   not by the suite — so the suite would not have caught it had it been wrong. Closing this needs a
   fixture where two majority candidates both have cohesion below 1.0 and the seed makes the first
   walk out, so a reordering changes which government forms. Cheap, and it guards the argument the
   whole refactor rests on.
2. ~~**`ui/types/bindings.d.ts` still says "schemaVersion 5"**~~ — **closed by wave 6 of the event
   system rework, 2026-08-17.** The mirror's authority comment now reads 9, matching
   `docs/contracts/ui_bindings.md`. It had drifted three versions, which is the case for the drift
   audit being a scheduled re-run rather than something done when someone happens to notice.
   **The audit itself is still owed**: the last one covered 26 `agora.*` bindings on 2026-08-09, and
   waves 0–6 have since added the whole `agora.stories` group. Adding bindings is exactly when drift
   appears, so the re-run belongs in wave 7.

## Blocked / needs a decision

1. **M6 scope.** The political map overlay and election-night broadcast mode are the two remaining
   M6 tasks and neither is started. The overlay's fallback (a stylized district map inside the
   dashboard) has not been chosen against yet.
2. ~~**W6 additional content**~~ — **decided 2026-08-08.** Five of the six are in: manifesto-vs-platform,
   poll trend sparkline, coalition relations, party history strip, mandate scorecard. Bloc support
   breakdown declined. Coalition relations uses the **live-ranking** design (a public RNG-free
   `RankCandidates` in `Agora.Core`) — no schema change, no save growth; `fixplan.md:322`'s claim
   that it was "already computed" was wrong. See `docs/plans/0002-w6-parties-tab.md` §H0.
3. **Effect palette rescope.** Scout 0001 §3 found no enum support for RCI demand, rent/land value,
   birth rate, or subsidies, and district scope has only 14 modifiers. The palette shipped against
   that gap list; `politicsmodplan.md` §7 still reflects the pre-rescope intent.
   **Corrected 2026-08-15 (wave 1):** that list is about what can be **modified**, and it was read
   for years as though it were about what can be **read**. Birth and death rates are fully readable
   — `StatisticType.BirthRate` and `DeathRate` have been public throughout, and wave 1 now records
   both (scout 0004 §4). Nothing can *change* them, which is the only thing scout 0001 ever claimed.
   The distinction matters because content authors were about to skip a working trigger on the
   strength of this line.
4. **`politicsmodplan.md` §14 open decisions** remain open: NA primaries, timeline jitter, snapshot
   retention, post-2026 authorship, unrest ceiling.

---

## Where to look when something breaks

`Colossal.Logging` gives every logger its own file, so Agora's output does **not** go to
`Player.log` — grepping that file for "Agora" returns nothing even on a healthy run. Ours is:

```
%USERPROFILE%\AppData\LocalLow\Colossal Order\Cities Skylines II\Logs\Agora.log
```

`Logs\Modding.log` is the other one worth reading: assembly load times, UI module registration, and
dispose. A mod that fails to load says so there, not in `Agora.log`.

Run `.\tools\verify-setup.ps1 -Build` for the current state of all build preconditions.

## Known toolchain quirks (all worked around; see `docs/scout/0002-modding-toolchain.md`)

- `ModPostProcessor.exe` / `ModPublisher.exe` target **.NET 6, which is EOL and not installed here.**
  `Agora.Mod.csproj` overrides both targets to pass `DOTNET_ROLL_FORWARD=LatestMajor` scoped to the
  `Exec`. Re-sync those overrides if a toolchain update changes them.
- **`Agora.Core` is pinned to netstandard2.0** because toolchain mode builds `Agora.Mod` as `net48`,
  which cannot reference netstandard2.1.
- `CSII_LOCALMODSPATH` is set before the folder exists. Never gate a build step on that folder
  existing — it will skip silently forever.
- A shell opened **before** the toolchain install sees no `CSII_*` variables. `Mod.props` dodges this
  by reading the registry directly; our own scripts check both.
- Gameface has **no `backdrop-filter`** — panel opacity is the only legibility lever (W1).
- **`npm run check` is misnamed and checks less than it sounds like it does.** `ui/package.json:8`
  maps it to `node tools/css-presence.js`, whose standalone entry point
  (`ui/tools/css-presence.js:158-170`) runs **only the design-token guard**. It does *not* diff CSS
  class names against the `.tsx` that reference them — `CSSPresencePlugin` is a webpack `hasCSS`
  injector, not a parity check. **And neither `npm run check` nor `npm run build` typechecks**;
  webpack is transpile-only. A green `check` + `build` is therefore *not* evidence of either class
  parity or type safety. Run **`npx tsc --noEmit`** separately, and diff class names by hand in
  review. Found during W6 chunk G's review, 2026-08-09.
- **Never junction a worktree's `ui/node_modules` to another checkout's install.** `ui/node_modules`
  is gitignored, so a fresh worktree has none and `tsc` is unavailable there. Junctioning to a
  sibling install looks like the cheap fix and is a trap: deleting the junction afterwards with a
  recursive delete follows the link and **empties the target**, silently disarming `tsc` for every
  other lane and for the main checkout. This happened on 2026-08-09 and cost a real verification
  gap — two lanes reported clean typechecks against an install a third lane then destroyed. Run
  `npm install` inside the worktree instead; it takes about five seconds.
- **`npm run build` deploys.** It writes into the player's live `…\Mods\Agora.Mod` folder, and
  `dotnet build Agora.sln` triggers it too once `node_modules` is installed. Use
  `dotnet build src/Agora.Mod/Agora.Mod.csproj -p:UseCsiiToolchain=false` for a compile check that
  does not clobber the deployed mod mid-session.
