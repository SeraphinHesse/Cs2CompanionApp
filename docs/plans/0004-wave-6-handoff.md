# Wave 6 → Wave 7 handoff

Wave 6 (UI) is code complete, reviewed and merged into `event-system/wave-6`. This file is written
for a session that was not here and has none of the context.

---

## Ready-to-paste prompt for the next orchestrator

> You are the orchestrator for **Wave 7 — Retirement, balance and gates** of the AGORA event-system
> rework. It is the **last wave**. **Begin with `/nextwave`.** Read
> `docs/plans/0004-event-system-rework.md` (the plan), `docs/plans/0004-wave-6-handoff.md` (this file
> — it **outranks the plan** wherever the two disagree, because it was written against the code) and
> `docs/status.md`.
>
> Waves 0–5 built a story system that runs. **Wave 6 made it visible: there is now a Stories tab, a
> story card, a political-power counter and five working commands.** Wave 7 retires the news feed
> that stories replaced, balances the economy, and closes the gates.
>
> Confirm wave 6's PR is merged into `EventSystemRefresh` before you cut anything; `/nextwave` step 2
> then has you prove the base builds and tests green and record the count **yourself**.
>
> Before you design the spine, read **"Contradictions"** and **"Traps aimed squarely at wave 7"**
> below. Three will cost you a lane each if found late: **lane 7a's deletion will break `StoryModal`
> unless it moves four helpers first**; **`powerIntensity` and `storyDifficulty` are published but
> drive nothing, and 7b owns both their presets and their write keys**; and **wave 7 is the first
> wave in four that must move a sidecar schema, so `/schema-change` is back in play.**
>
> **Fifty-one manual gate rows are outstanding across waves 0, 1, 4, 5 and 6, and none has been
> walked.** Wave 4's fourteen became walkable this wave. That is the real state of this rework: the
> code is reviewed and the game has never been played with it. Wave 7's own row is "gates", so this
> is yours to confront rather than pass on.

---

## State of the world, in one paragraph

Before this wave the story system drafted, resolved, moved power and moved votes, and both writers
produced prose about it — and **no player had seen a word of any of it.** Now there is a fifth
dashboard tab, third in the strip, showing live stories with both prose voices, three
**"Tackle &lt;event&gt;"** controls per story expanding into four response options, six textareas, a
**Resolve now** control and the archive below. A story card interrupts on the draft month — one card
per story, never one per event — holding the clock only when the engine says the story is major, and
dismissing it answers nothing. A political-power counter sits beside the mod icon and hides entirely
when the save has the power layer off. `docs/contracts/ui_bindings.md` reached **schemaVersion 9**
with a new `agora.stories` group of ten bindings, five of them inbound. **What is still true: nobody
has run the game.** Every claim in this file is a review's reasoning or a gate row.

## PR

**PR:** _(filled in at push — see `docs/status.md` for the link)_
**Merge status: NOT merged.** The owner reviews. Wave 7 must not open its umbrella until it is in.

---

## What actually shipped

**Zero merge conflicts across four lanes**, the seventh wave to prove the spine-first law.

### The spine

| File | Change |
|---|---|
| `docs/contracts/ui_bindings.md` | **schemaVersion 9**; new §4.7 `agora.stories`; §4.1 six settings fields + four write keys; §4.6 three outcome codes; §6 four empty values; §4 preamble recount |
| `UiBindings/AgoraUiPayloads.cs` | seven story payloads; `SettingsPayload` +6 fields |
| `UiBindings/AgoraStoriesUISystem.cs` | **new** — all ten bindings, five command handlers |
| `UiBindings/AgoraUiProjection.Stories.cs` | **new** — seam stubs, filled by 6a |
| `UiBindings/AgoraUiProjection.cs` | `BuildSettings` fills the six story fields |
| `Core/StoryAlert.cs` | **new** — the card record |
| `Core/AgoraRuntime.cs` | the story alert ring, `AckStoryAlert`, `RaiseStoryAlerts`, `MajorSlotOf`; `SetSetting` +4 keys and `SetCount`; ring cleared in `ResetForNewSave` |
| `Core/AgoraRuntime.StoryCommands.cs` | **the per-save power guard** — see "Defects found" |
| `Mod.cs` | `AgoraStoriesUISystem` at `UIUpdate` |
| `ui/types/bindings.d.ts` | the whole mirror; `CommandOutcomeName` +3; stale "schemaVersion 5" comment fixed |
| `ui/src/shell/bindings.ts` | four bindings, five call wrappers, three empty values, three outcome sentences, `ValueRequired` reworded |
| `ui/src/shell/state.ts` · `Dashboard.tsx` · `index.ts` · `ui/src/index.tsx` | the fifth tab and the fifth append |

### The lanes

| Lane | Delivered | Review |
|---|---|---|
| **6a** `AgoraUiProjection.Stories.cs` | the five projections | **Approved first pass** |
| **6b** `ui/src/panels/Stories/**` (9 files) | the panel | **Approved**; routed two fixes to the spine |
| **6c** counter + settings rows | the counter, four rows, two deliberately absent | **Blocked once**, one real defect |
| **6d** `StoryModal` + boundary + scss | the card | **Approved first pass** |

**The suite is 2178 → 2178 (unchanged), and that is the correct outcome.** Every file this wave
touched is in `UiBindings/`, `AgoraRuntime` or `ui/`, none of which links into the headless suite by
design. **No test was deleted** (verified against the base) and **no coverage was manufactured.** The
wave's evidence is four adversarial reviews and **nineteen manual gate rows**. A flat count is
normally a defect; here it is written down so nobody closes the gap by faking the runtime.

---

## Defects found, all by review, none by a test

Three were in files the lane that found them was forbidden to touch, and were landed on the umbrella
rather than sent back — sending them back would have had a lane edit outside its row.

1. **`SpendPowerOverride` read half the power switch** (`04862e5`). It guarded on
   `tuning.Power.Enabled` and never on the per-save `PoliticalPowerEnabled`. Latent until this wave,
   because the method had no caller. On a save with the per-save switch off and tuning on, the
   counter hides, the projection quotes a cost of 0 and affordability false — **and the purchase
   would still have been accepted and debited against a balance the player cannot see.**
   `StoryCycle.MovePower`'s own remarks predicted wave 6 would need a third copy of this guard; it
   needed a fourth. Found by 6a while writing the cost quote.
2. **The settings drawer had no height cap and no scroll** — 6c's blocking defect, and its own. Four
   new rows took it from ~969rem to ~1770rem, taller than the whole screen, with every story row it
   exists to deliver below the screen edge and no scrollbar to reach them. Capped at 620rem.
3. **`ValueRequired` told story players to press a party-editor button** (`7f7645a`). One outcome map
   serves every inbound binding, and its sentence read *"To hand it back to the generator, use
   reset."* The most reachable path to that code in the whole mod is now declaring a story success
   with an empty justification. Found by 6b's review, in the spine.
4. **§4's preamble still counted nine inbound bindings** (`18ce126`). Fourteen now. Found by 6b.

---

## Contradictions with `docs/plans/0004-event-system-rework.md`

**These outrank the plan. Wave 7 plans against them.**

1. **Five inbound story bindings, not three.** `setResponse`, `declareManual`, `resolveNow`,
   `spendPowerOverride`, `ackAlert`. A purchase arriving through `setResponse` would be a free `Met`
   nobody paid for, so it has its own channel; `ackAlert` is the fifth. Carried from waves 4 and 5,
   now closed and in the contract.
2. **`ui/src/shell/storyPause.ts` does not exist and must never be written.** The plan assigns it to
   6d. `ui/src/shell/pause.ts` already exposes `useSimulationHeldPaused`, and it works by subscribing
   to the game's own refcounted barrier — a second module would be a second refcount on one barrier.
3. **`Core/StoryAlert.cs` and `AgoraStoriesUISystem.cs` are spine files, not lane files.** The first
   is the seam between the publisher and the card; the second is where every binding *name* lives,
   and three lanes coded against those names.
4. **`AgoraUiProjection` and `AgoraRuntime` were already `partial`.** The plan's wave-2 split had
   landed; nothing to do.
5. **`powerIntensity` and `storyDifficulty` drive nothing.** The plan says `PowerIntensity` drives
   "the gain/cost/penalty presets". It does not: `TuningPresets.Apply` reads `VoteSharpness`,
   `NewsInfluence` and `BrandDiscipline` and no fourth or fifth level, and no preset table exists for
   either story enum. Both are persisted, cloned and published, and both are **read-only** — there is
   deliberately no `setSetting` key, because a switch that persists a value and changes no number is
   the defect W5 closed for `PauseOnMajorNews`. **Wave 7b owns the presets and the write keys, and
   they must ship in the same change.**
6. **`AgoraTreasurySystem` was never built and must not be** (carried from wave 4).

---

## Traps aimed squarely at wave 7

- **Lane 7a's deletion breaks `StoryModal`.** `ui/src/shell/StoryModal.tsx` imports `cx`,
  `formatSimDate`, `splitParagraphs` and `SEVERITY_STEPS` from `ui/src/panels/News/format`, and 7a's
  row is "`ui/src/panels/News/**` (delete)". Lane 6d followed `ArticleModal`'s precedent exactly —
  it imports from the same three News modules and has since W5 — but **`ArticleModal` retires with
  that lane and `StoryModal` does not.** Move the four helpers to a shell-owned module *first*, and
  reconcile them with the copies lane 6b independently wrote in
  `ui/src/panels/Stories/format.ts`. Move first, delete second: one refactor instead of a broken
  build. `npx tsc --noEmit` catches it; nothing else will.
- **Wave 7 is the first wave in four that must move a sidecar schema**, so `/schema-change` is back
  in play with all its rules — a step *and* a fixture at the old version, frozen local constants
  never a live tuning read, and the settings upgrade helper called from **both** the standalone path
  and the state step, because a nested settings block is never reached by the settings step table.
  Waves 2 and 5 both had to learn that second one.
- **`npm run check` checks less than it sounds like** — design tokens only. `npx tsc --noEmit` is a
  separate obligation and CSS class names are diffed by hand in review. Two lanes this wave reported
  self-checked parity figures; **one of them did not survive checking** (6b said 66/66, it was
  64/64 — parity held, the count was wrong). Do not take a lane's number.
- **`npm run build` deploys** into the player's live `…\Mods\Agora.Mod`, and `dotnet build Agora.sln`
  triggers it once `node_modules` exists. Lanes verify with
  `dotnet build src/Agora.Mod/Agora.Mod.csproj -p:UseCsiiToolchain=false`. Only the orchestrator,
  once per wave, runs a deploying build.
- **Never junction `ui/node_modules`.** `npm install` in the worktree takes about five seconds.
- **A story lives `cycleMonths - 1` months — ONE, not two.** Repeated for a fifth wave. 6b got it
  right; the balance pass in 7b is where it can go wrong again, because every authored threshold is
  sized against that window.
- **`AgoraRuntime` and `UiBindings/**` compile into no test.** Anything there gets a gate row, and
  **faking the runtime to manufacture coverage is a review-blocking defect.**

---

## Known gaps, recorded rather than closed

- **Nothing lets a player stop a story card pausing their game.** The card holds the clock on the
  engine's `major` verdict alone. A player who turned **off** "Pause on major news" — whose hint text
  enumerates elections, governments, party lifecycle and serious events, all *news* — still gets
  force-paused by a major story, with no way to prevent it short of `storiesEnabled: false`, which
  turns the whole feature off. **6d's choice not to repoint `pauseOnMajorNews` is correct and is not
  the thing to change**: that control's hint enumerates news categories, and neither position of it
  is a statement about stories. The fix is a fifth setting, `pauseOnMajorStory`, default true — a new
  persisted field, so settings 5→6 with a migration through both paths and a fixture. **It belongs
  with 7b, which is already opening this surface.** Mitigation: the card is always dismissable and
  dismissing releases the barrier, so this is a forced pause with a working exit, not a freeze.
  **Gate row 12 decides whether that is too late.**
- **The dashboard column overflows when Settings is open — and wave 6 improved it by two thirds.**
  With Settings open and a panel showing, the column wants ~1296rem of a 1080rem screen; it wanted
  ~1646rem before this wave. Panel content inside a `Scrollable` is still reachable because it
  scrolls up into view, so the only strictly unreachable element is anything pinned *outside* a
  scroll region at a panel's bottom. **And `SeatsPanel` has no `max-height` and no scroll region at
  all**, independently of any of this — Council overflows on a large chamber with Settings closed
  too. That is the part most likely to be lost if this is filed as "the settings drawer is too tall".
  Two fixes ruled out with reasons: collapsing the panel slot re-litigates the ratified "not a fifth
  tab" decision, and shrinking Settings below the house 620rem degrades a correct surface for a spine
  decision it does not own while leaving Council untouched. **The fix is a shell-owned column
  budget** — a ceiling on `.shell`, `.panelSlot` as `flex: 1 1 auto` over `min-height: 0`, each
  panel's fixed `max-height` becoming `max-height: 100%`, and `SeatsPanel` gaining the scroll region
  it never had. Spine plus all four panels: **a wave-7 lane of its own.**
- **`CivicEvent` has no `manualText`.** Three of the four response options render an authored,
  per-event blurb; the fourth renders fixed panel copy, because the seven prose fields ratified in
  wave 2 and authored across 58 events in wave 3 include no Manual blurb. A player reading four
  options sees three that talk about *this event* and one that talks about the system. Adding it is a
  `StorySlot` change across C#, the catalog, the prompt and the `.d.ts` — `/schema-change` work, and
  a re-authoring pass over 58 events. Reported by 6b, correctly not worked around.
- **Key propagation is implemented as well as it can be statically, and is unproven.** `swallowKeys`
  is on both textareas, on `onKeyDown` **and** `onKeyUp`, and correctly does not call
  `preventDefault`. React's `stopPropagation` stops a document-level listener in the **bubble** phase;
  a **capture**-phase listener above the React root would not be stopped, and nothing in this repo
  settles which CS2 uses. Gate row 5.
- **`placeholder` on `<textarea>` has no precedent anywhere in `ui/src`.** If Gameface ignores it the
  boxes lose hint text only — the one genuinely mandatory field states its requirement in a real
  element, not a placeholder. Degrades, does not break.
- **Everything waves 3, 4 and 5 recorded is still open**, including `districtAffinity` empty on every
  event, the two unreconciled severity ceilings, the stale
  `data/schemas/political_state.schema.json`, and `CurrentFlavorCacheVersion` versioning a file
  nothing routes through `Migrate`.

---

## Manual gates — nineteen opened, none walked

Full text in `docs/plans/0004-wave-6-lanes.md` § "Manual gate rows this wave opens". The sharpest
five, in the order worth walking them:

1. **The tab renders the right panel.** `Dashboard.renderTab`'s `default:` falls through to
   `SeatsPanel`, so a missing `case` is the wrong panel silently, with no error anywhere.
2. **Two stories drafting means two cards, not six.** One card per story is the interruption budget
   and the easiest thing in the wave to get wrong.
3. **Text entry under Gameface** — six textareas per story. Focus one and press space, digits, `b`,
   `p`: the sim must not pause, change speed, or open bulldoze. Highest-risk unverified area in the
   project, and now multiplied.
4. **The pause-setting gap** (row 12) — decides whether `pauseOnMajorStory` is urgent or can wait.
5. **A card from city A must never pop over city B** (row 4) — the W0 bug class; the ring is cleared
   in `ResetForNewSave` beside the news one.

**Still outstanding from earlier waves:** all five of wave 0's, all sixteen of wave 1's, all fifteen
of wave 4's, and all seven of wave 5's. **Wave 4's rows 1–14 are now walkable for the first time**,
because they needed a pressable modal and this wave built it. **Total outstanding: fifty-one rows,
none walked.**

---

## Verification recorded

- `dotnet build Agora.sln` — **0 warnings, 0 errors**, toolchain mode. Run once at the end; **this
  build deploys** to the player's live `…\Mods\Agora.Mod`.
- `dotnet test tests\Agora.Core.Tests\Agora.Core.Tests.csproj` — **2178 passed, 0 failed**
  (from 2178; **unchanged, and correct** — see above).
- `cd ui && npx tsc --noEmit` — **clean.** Also `npm run check` — design token guard clean, reported
  as the separate and much weaker obligation it is.
- **Schema versions moved: `ui_bindings.md` 8 → 9, and nothing else.** No sidecar document, no
  `data/` schema, no tuning file. `data/` is byte-identical to the base. **Wave 6 persists nothing
  new**, so there is no migration to write and no fixture to add — the first wave of this rework
  for which that is true, and the reason existing saves are entirely unaffected by it.
  `PoliticalEngine.CloneState` and `AgoraSettings.Clone()` needed no change: the six story settings
  have been in `Clone()` since wave 2, and the story alert ring is session state that is never
  serialised.
