# Wave 5 → Wave 6 handoff

Wave 5 (prose) is code complete, reviewed and merged into `event-system/wave-5`. This file is written
for a session that was not here and has none of the context.

---

## Ready-to-paste prompt for the next orchestrator

> You are the orchestrator for **Wave 6 — UI** of the AGORA event-system rework. **Begin with
> `/nextwave`.** Read `docs/plans/0004-event-system-rework.md` (the plan),
> `docs/plans/0004-wave-5-handoff.md` (this file — it **outranks the plan** wherever the two
> disagree, because it was written against the code) and `docs/status.md`.
>
> Wave 4 made the story system run. Wave 5 gave it words. **Both writers now produce prose for every
> story and nothing renders any of it** — there is still no story card, no modal, and the four
> inbound commands still have no caller and no binding. Wave 6 is the surface.
>
> Confirm wave 5's PR is merged into `EventSystemRefresh` before you cut anything; `/nextwave` step 2
> then has you prove the base builds and tests green and record the count **yourself**.
>
> Before you design the spine, read **"Contradictions with the plan"** and **"Traps aimed squarely at
> wave 6"** below. Three will cost you a lane each if found late: **the story prose seam is a ledger,
> not a payload field**; **`ui/` has never been touched by this rework and its typecheck is a
> separate obligation from `npm run check`**; and **text entry under Gameface is still unproven**.

---

## State of the world, in one paragraph

Before this wave a story could draft, resolve, move power and move votes, and **no writer produced a
word about it**. Now both do. The canned pool transcribes every story from the civic catalog the
moment it opens — headline from the major event's name, article from every slot in order — and it is
the everyday voice, because it answers every poll and always has an answer. The CLI is woken on the
**month a story drafts** (new this wave, an owner decision), so the model writes about roughly six
stories a sim year instead of one, and **its prose is added beside the canned text rather than
replacing it**: nothing a player has read ever changes under them. `politics_flavor` reached
schemaVersion 3 with `stories[]` and `resolutions[]`, article limits tripled, the sidecar migrated
settings 4→5 and state 6→7 so existing saves gain the wake, and `eventProse` — parsed, validated and
cached since M3 and copied nowhere — finally reaches `TimelineEvent.LocalAngle`. **What is still
true: no player has seen any of it.**

## PR

**PR:** _(filled in by `/commitpushpr`)_
**Merge status: NOT merged.** The owner reviews. Wave 6 must not open its umbrella until it is in.

---

## What actually shipped

**Zero merge conflicts across four lanes**, the sixth wave to prove the spine-first law.

### The spine

| File | Change |
|---|---|
| `data/schemas/politics_flavor.schema.json` + `Llm/FlavorSchema.cs` | **schemaVersion 3**, `stories[]` · `resolutions[]`, headline 90→**270**, body 420→**1260** |
| `Llm/FlavorCacheMigration.cs` | routes 1→3 and 2→3; prunes `articles` · `stories` · `resolutions` |
| `Persistence/SidecarSchema.cs` | flavor cache 2→**3**, settings 4→**5**, state 6→**7**, `UpgradeSettingsObjectToV5`, `MigrateStateV6ToV7` |
| `Contracts/PoliticalState.cs` | `LlmWakeCadence.Story`, widened `Default` |
| `Contracts/Boundary.cs` | `StoryProse`, `EventProse`, `ProseSource`; `FlavorPayload` gains all three collections |
| `Tuning/EngineTuning.cs` + `data/engine_tuning.json` + schema | `llmWakeOnStoryDraft`; **engine_tuning 8→9** |
| `Events/Scheduler/TickPlanner.cs` | the story wake clause, gated three ways |
| `Llm/FlavorDocument.cs` · `FlavorCatalog.cs` · `FlavorRequest.cs` | the seam types — **pulled in from their planned lanes** |
| `Stories/Catalog/CivicEventCatalog.cs` | `Find(id)` |
| `Llm/StoryProseLedger.cs` | **new** — the add-don't-replace rule, test-linked |
| `Core/AgoraRuntime.cs` | story briefs, story wake reason, ledger absorb, **`localAngle` write-back**, `Pool.CivicCatalog` wiring, `StoryCount` in the catalog log |
| `Llm/FlavorCache.cs` | discards reported at Warn on the success path |

### The lanes

| Lane | Delivered | Review |
|---|---|---|
| **5a** `FlavorPromptBuilder.cs` | Story/resolution prompt sections, +12 tests | **Approved first pass** |
| **5b** `FlavorValidator.cs` | Per-entry story id check, +6 tests | **Approved**; found one spine defect |
| **5c** `StaticPoolProvider.cs` · `StaticPoolContent.cs` | The canned story voice, +17 tests | **Blocked once**, 2 findings, both real |
| **5d** two new test files | 16 cross-cutting proofs | Verified on the umbrella |

**The suite went 2109 → 2175 (+66).**

---

## Contradictions with `docs/plans/0004-event-system-rework.md`

**These outrank the plan.** Wave 6 plans against them.

1. **Story prose does not live on `FlavorPayload` alone — it lives in `StoryProseLedger`.** The plan
   implies a payload field the UI reads. It cannot: the canned pool answers *every* poll and the CLI
   answers rarely, so a "latest wins" read would erase the model's prose within a minute of it
   arriving. `AgoraRuntime.StoryProse` is the accessor. Ask it for
   `Get(storyId, StoryProseKind.Opening|Resolution, ProseSource.Pool|Cli)` and **render both when
   both exist** — that is owner decision 2, not an implementation detail.
2. **`FlavorDocument.cs`, `FlavorCatalog.cs` and `FlavorRequest.cs` are spine files now**, not lane
   files. The plan assigned them to 5b/5a; all four lanes coded against them.
3. **The plan's wave 5 lane list is spent, but `FlavorPromptBuilder` is not "5a's" any more** — it is
   merged and shared. Wave 6 should not need to touch `Llm/` at all.
4. **A fourth inbound binding is still required** (carried from wave 4): `setResponse`,
   `declareManual`, `resolveNow` **and** `spendPowerOverride`. The plan's §605 table lists three.
   None is in `docs/contracts/ui_bindings.md` yet, and the method is `declareManual`, not
   `declareOutcome`.
5. **`AgoraTreasurySystem` was never built and must not be** (carried from wave 4).

---

## Traps aimed squarely at wave 6

- **`ui/` has not been touched by this rework at all.** No wave 0–5 file changed under `ui/`. That
  means wave 6 is the first to hit `npm install` in worktrees, and **never junction `ui/node_modules`
  to another checkout** — deleting the junction later follows the link and empties the target,
  silently disarming `tsc` everywhere. `npm install` takes about five seconds.
- **`npm run check` checks less than it sounds like** — design tokens only. `npx tsc --noEmit` is a
  separate obligation, and CSS class-name parity is diffed by hand in review.
- **`npm run build` deploys** into the player's live `…\Mods\Agora.Mod`, and `dotnet build Agora.sln`
  triggers it once `node_modules` exists. Lanes verify with
  `dotnet build src/Agora.Mod/Agora.Mod.csproj -p:UseCsiiToolchain=false`. Only the orchestrator, once
  per wave, runs a deploying build.
- **Text entry is unproven under Gameface** (`docs/status.md` known gap 0). `PartyEditor.tsx` holds
  the only `<input>`/`<textarea>` in `ui/src`, they have never rendered in game, and nothing stops key
  propagation — space, digits, `b`, `p` may reach game hotkeys. This wave adds **two textareas per
  event, six per story**. Copy `PartyEditor`'s pattern *and* add `onKeyDown` propagation stopping, and
  gate it.
- **`Dashboard.renderTab`'s `default:` falls through to `SeatsPanel`.** A missing `case` renders the
  wrong panel silently. Add `"stories"` to `TAB_ORDER` **and** to the switch.
- **Gameface has no CSS grid.** Flexbox only.
- **A story lives `cycleMonths - 1` months — ONE, not two.** Repeated for a fourth wave because it is
  still the costliest available mistake.
- **`AgoraRuntime` and `UiBindings/**` compile into no test.** Anything there gets a gate row, never
  a test, and **faking the runtime to manufacture coverage is a review-blocking defect**.

---

## Known gaps, recorded rather than closed

- **The `IsMajor` / slot-order contract is asserted only against lane 5d's own fixture**, never
  against what `AgoraRuntime.BuildStoryBrief` produces. If the runtime sorted slots differently the
  golden test would keep passing. Closing it needs `BuildStoryBrief` reachable from the suite, which
  is a Core/Mod split question rather than a test one.
- **`StoriesAllDiscarded` deliberately does not exist**, unlike `ArticlesAllDiscarded`. The reasoning
  is on `FlavorValidator.FilterStoryProse` and rests on the canned pool always having written the
  story. That premise is now true — 5c landed — but **if a future change makes pool story prose
  conditional, this decision must be revisited before any UI reads `FlavorPayload.Stories`.**
- **`data/schemas/political_state.schema.json` is stale.** Its nested settings block still declares
  `schemaVersion const 2` while settings are at 5, and it never gained wave 4's story fields. It is
  not validated against in any test, which is why it has drifted twice. Not widened here.
- **`SidecarSchema.CurrentFlavorCacheVersion` still versions a file nothing routes through
  `Migrate`.** Two constants version `flavor_cache.json` and only `FlavorSchema`'s is read. The
  honest fix is deleting it and `SidecarDocument.FlavorCache`; carried, not done.
- **Everything wave 3 and wave 4 recorded is still open**, including `districtAffinity` empty on
  every event, the two unreconciled severity ceilings, the drifting district target, and
  `PoliticalPowerEnabled` guarded at call sites rather than in the seam. Wave 6's cost quote would be
  that guard's **third** copy.

---

## Manual gates opened by wave 5 — six, none walked

Full text in `docs/status.md` § "Wave 5's manual gates".

Unlike wave 4, **most of wave 5 is genuinely tested** — every file the lanes touched is
`<Compile Link>`-ed into `Agora.Core.Tests`. The six gates are what is left: the `AgoraRuntime`
wiring, and the two things only a real save shows. Gates 1–3 are the sharpest: the wake firing on
draft months and **not** on a stories-off save, the yearly round still carrying story sections, and
the settings migration reaching an untouched save while leaving a customised one alone.

**Still outstanding from earlier waves:** all five of wave 0's, all sixteen of wave 1's, and all
fifteen of wave 4's. None has been walked. Wave 4's rows 1–14 **cannot** run until wave 6 wires a
pressable modal — which is this wave. Expect to walk twenty-one rows, not six.

---

## Verification recorded

- `dotnet build Agora.sln` — **0 warnings, 0 errors**, toolchain mode. Run once at the end; **this
  build deploys** to the player's live `…\Mods\Agora.Mod`.
- `dotnet test tests\Agora.Core.Tests\Agora.Core.Tests.csproj` — **2175 passed, 0 failed**
  (from 2109; **+66**).
- `cd ui && npx tsc --noEmit` — **not run, and not required: no `ui/` file changed this wave.**
  Wave 6 owes it.
- **Schema versions moved: `politics_flavor` 2→3, `engine_tuning` 8→9, flavor cache 2→3, settings
  4→5, state 6→7.** The last two are a real migration reaching every existing save — the first since
  wave 1. `timeline` and `civic_events` did not move. `PoliticalEngine.CloneState` and
  `AgoraSettings.Clone()` needed no change: no new *persisted* field was added, only a new enum
  member on a field both already copy.
