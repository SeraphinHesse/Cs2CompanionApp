# Wave 6 — UI · lane ownership

Umbrella: `event-system/wave-6`, cut from `EventSystemRefresh` at `1bb6fac` (PR #8 merged).
Spine: `17e6cc6 wave-6 spine`.

**Base measured before anything was cut**, not read from a plan: `dotnet build Agora.sln` 0 warnings
/ 0 errors, `dotnet test tests\Agora.Core.Tests\Agora.Core.Tests.csproj` **2178 passed, 0 failed**,
`npx tsc --noEmit` clean. The base was green; no repair commit was needed, which is the first time in
this rework that has been true.

**The one law:** every file more than one lane would touch was landed in the spine. Lanes own
strictly disjoint paths. **A merge conflict is a bug in this table, not something to resolve by
hand.**

---

## What the spine already did, so no lane repeats it

| File | What landed |
|---|---|
| `docs/contracts/ui_bindings.md` | **schemaVersion 9**; new §4.7 `agora.stories`; §4.1 six settings fields + four write keys; §4.6 three outcome codes; §6 four empty values |
| `UiBindings/AgoraUiPayloads.cs` | `StorySlotPayload` · `StoryPayload` · `StoryBriefPayload` · `StoryArticlePayload` · `PowerLedgerRowPayload` · `PowerPayload` · `StoryAlertPayload`; `SettingsPayload` +6 fields |
| `UiBindings/AgoraStoriesUISystem.cs` | **complete** — all ten bindings registered, five command handlers wired to `AgoraRuntime` |
| `UiBindings/AgoraUiProjection.Stories.cs` | **AGORA-SEAM(wave-6/6a)** — five stubs returning empty payloads |
| `UiBindings/AgoraUiProjection.cs` | `BuildSettings` fills the six story fields |
| `Core/StoryAlert.cs` | the card record |
| `Core/AgoraRuntime.cs` | `_storyAlerts` ring + `StoryAlerts` + `AckStoryAlert` + `RaiseStoryAlerts` + `MajorSlotOf`; `SetSetting` +4 keys and `SetCount`; `ResetForNewSave` clears the ring |
| `Mod.cs` | `AgoraStoriesUISystem` at `SystemUpdatePhase.UIUpdate` |
| `ui/types/bindings.d.ts` | the whole mirror; `CommandOutcomeName` +3; stale "schemaVersion 5" comment fixed |
| `ui/src/shell/bindings.ts` | `stories$` · `storyArchive$` · `power$` · `storyAlerts$`; five call wrappers with deadlines; `EMPTY_POWER` / `EMPTY_STORY_ALERT` / `EMPTY_STORY_ARTICLE`; `EMPTY_SETTINGS` widened; three outcome sentences |
| `ui/src/shell/state.ts` | `"stories"` in `AgoraTab`, `TAB_ORDER`, `TAB_LABEL` |
| `ui/src/shell/Dashboard.tsx` | `case "stories"` in `renderTab` |
| `ui/src/shell/index.ts` · `ui/src/index.tsx` | the fifth append |
| `ui/src/panels/Stories/{index.ts,StoriesPanel.tsx}` | **AGORA-SEAM(wave-6/6b)** stub |
| `ui/src/shell/StoryModal.tsx` | **AGORA-SEAM(wave-6/6d)** stub |

**No schema version moved except this contract's.** Nothing in wave 6 is persisted; there is no
sidecar migration to write and no fixture to add. That is unusual for this rework and is worth
saying, so nobody goes looking for the migration step that is not there.

---

## Lanes

### 6a — the projections

| | |
|---|---|
| Branch | `event-system/w6-a` |
| Worktree | `.claude/worktrees/w6-a` |
| **Owns, exclusively** | `src/Agora.Mod/UiBindings/AgoraUiProjection.Stories.cs` |

Replace all five AGORA-SEAM stubs. The file's own header block states the acceptance criteria per
function; they are the contract, not a summary of it.

**Acceptance**

1. `BuildLiveStories` returns one `StoryPayload` per `state.LiveStories` entry **in the engine's
   order**, slots **in the engine's order** (major first, then minors ascending by event id ordinal).
   Neither is re-sorted here.
2. A slot's `name` / `description` / five prose fields come from the civic catalog entry for its
   `EventId`. **A slot whose event the catalog no longer explains ships with `name` empty — never
   with the event id in the name field.** A raw id on screen is a defect this repo has fixed twice.
3. `tier` is `civicEvent.TierUnder(stories.mandatorySeverityThreshold, stories.majorSeverityThreshold)`.
   **No severity comparison is written in this file.**
4. `overrideCost` is `PoliticalPower.OverrideCost(tier, tuning)` and `canAfford` is
   `PoliticalPower.CanAfford(state.Power, tier, tuning)` — and **both are `0` / `false` when the
   power layer is off**, on the per-save setting and the tuning switch together.
5. `BuildStoryArchive`: `metCount` and `scoredCount` both **exclude `Unmeasurable`** slots, the same
   exclusion the 2-of-3 rule makes. `slotCount` does not. Capped at `StoryArchiveMax`.
6. `BuildStoryArticle` asks `StoryProseLedger` for **both sources of both kinds** and ships whichever
   exist. An unknown story id answers an empty payload rather than throwing.
7. `BuildPower` copies `state.Power` across, ledger capped at `PowerLedgerMax` **keeping the newest**.
8. `BuildStoryAlerts` copies the queue across, **oldest first, unsorted**.

**Must not test.** This file is in `UiBindings/` and links into no test by design. Do not add a
`<Compile Link>` line for it, and **do not stub `Colossal.UI.Binding` to manufacture coverage** —
that is itself a review-blocking defect. Reason about the arithmetic; the gate rows below are where
these claims are settled.

**Seams it codes against:** everything in the spine table. It writes nothing outside its own file.

---

### 6b — the Stories panel

| | |
|---|---|
| Branch | `event-system/w6-b` |
| Worktree | `.claude/worktrees/w6-b` |
| **Owns, exclusively** | `ui/src/panels/Stories/**` (the two stub files, plus every new file it needs there) |

**Acceptance**

1. Live stories from `stories$`, in the order received. Headline from `story.headline`; body fetched
   per story from `agora.stories.article`. **Render both prose voices when both exist** — the pool's
   text is what is always shown, the model's appears beside it and never instead of it.
2. Three **"Tackle &lt;event name&gt;"** controls per story, one per slot, expanding into the four
   response options. A slot whose `name` is `""` says so in words; it never renders `eventId`.
3. Textareas for Ignore and Manual — **six per story**. Copy `PartyEditor.tsx`'s pattern **and** stop
   key propagation in `onKeyDown`. Without that, space, digits, `b` and `p` reach the game's hotkeys
   and the player pauses the sim by typing.
4. A **Resolve now** control, and the archive from `storyArchive$` below the live stories.
5. Route the buttons to the right call: `PowerOverride` goes through `spendPowerOverride`, **not**
   `setResponse` — `setResponse("PowerOverride")` answers `BadValue` by design.
6. Render the engine's verdict, never one of your own. Use `isAccepted` / `writeMessage` from
   `shell/bindings.ts`; do not reimplement either, and do not decline to send an override because
   `canAfford` is false — that field decides what the button looks like, not whether the call happens.
7. A `PanelBoundary` on the pattern of `ui/src/panels/Parties/Boundary.tsx`.
8. **Flexbox only.** Gameface has no CSS grid.
9. `npx tsc --noEmit` clean, and CSS class names diffed by hand against the `.tsx` — `npm run check`
   runs only the design-token guard and does neither.

**Do not touch** `ui/src/shell/**` (6c and 6d and the spine own it) or `ui/types/bindings.d.ts`
(spine). If a binding you need is missing, that is a spine bug — report it, do not add one locally.

---

### 6c — the power counter and the settings rows

| | |
|---|---|
| Branch | `event-system/w6-c` |
| Worktree | `.claude/worktrees/w6-c` |
| **Owns, exclusively** | `ui/src/shell/AgoraButton.tsx` · `ui/src/shell/Shell.module.scss` · `ui/src/shell/SettingsPanel.tsx` · `ui/src/shell/SettingsPanel.module.scss` |

**Acceptance**

1. The political-power counter **next to the mod icon, top left**. `AgoraButton` is already appended
   to `GameTopLeft` and already renders a dot, a label and a date, so this is an added element in an
   existing control, not a new surface.
2. **`power.enabled === false` hides the counter.** It does not render a zero — a zero is a balance,
   and "this save has no such currency" is not one.
3. Debt reads from `power.inDebt`, **not** from `balance < 0`. What counts as debt is the engine's
   rule and the consequence attached to it is a capped, tuned effect.
4. Settings rows for the **four writable** story settings: `storiesEnabled`, `storiesPerCycle`
   (0–5), `eventsPerStory` (0–5), `politicalPowerEnabled`. **0 means "use the tuned default"** and
   the control must say so — it is not "no stories".
5. **Render no control for `powerIntensity` or `storyDifficulty`.** They are published and read-only;
   there is no `setSetting` key and a write answers `UnknownKey`. The presets behind them do not
   exist until wave 7b, and a control that persists a value and changes no number is the defect W5
   closed for `PauseOnMajorNews`. If you want to show them, show them as text with a note that they
   arrive in a later pass — do not wire a control to a key that is not there.

**Do not touch** `ui/src/shell/bindings.ts` or `state.ts` or `Dashboard.tsx` — all spine. Note that
`Shell.module.scss` is yours alone: the spine's tab-strip change reuses the existing `styles.tab`
class and adds no CSS.

---

### 6d — the story card

| | |
|---|---|
| Branch | `event-system/w6-d` |
| Worktree | `.claude/worktrees/w6-d` |
| **Owns, exclusively** | `ui/src/shell/StoryModal.tsx` · `ui/src/shell/StoryModal.module.scss` · `ui/src/shell/StoryModalBoundary.tsx` |

**Acceptance**

1. Renders `storyAlerts$[0]` **or nothing** — one card at a time by construction, exactly as
   `ArticleModal` does it. The queue arrives oldest-first and is never re-sorted here.
2. **One card per story, never one per event.** All of a story's slots render inside the one card.
   Two stories in a cycle mean two cards, not six. This is manual gate 3b and the easiest thing in
   this lane to get wrong.
3. The pause barrier through `useSimulationHeldPaused(active)` from `./pause`, taken **only when the
   card's own `major` flag says so**. `alert.major` is the engine's verdict; never compare a severity
   to a threshold here.
4. **`storyPause.ts` is not to be written.** `pause.ts` already exposes exactly this hook, and a
   second copy would be a second refcount on one barrier. The rework plan names a `storyPause.ts`
   because it was written before `pause.ts` landed — see "Contradictions" below.
5. **Always dismissable**, through `ackStoryAlert`, including from the boundary's fallback. While the
   barrier is held the game forces the speed to zero every frame, so a card with no working way out
   is a game the player cannot un-pause by any means at all.
6. Rendered through `Portal`, on its own error boundary, on the pattern of `ArticleModalBoundary`.
7. Renders `null` while the region prompt is up (`isFirstRun$`), with an empty queue, and with the
   mod switched off — the same three conditions `ArticleModal` honours.

**The card is a notification, not a form.** Dismissing answers nothing; the story stays live and is
tackled from the Stories panel. If this lane finds itself putting the four response buttons on the
card, that is 6b's job and the interruption budget is the reason.

**Do not touch** `ui/src/index.tsx` (spine already appends `StoryModal`), `ui/src/shell/index.ts`
(spine already exports it), `ArticleModal.tsx` or `pause.ts`.

---

## Path disjointness check

| Path | Lane |
|---|---|
| `src/Agora.Mod/UiBindings/AgoraUiProjection.Stories.cs` | 6a |
| `ui/src/panels/Stories/**` | 6b |
| `ui/src/shell/AgoraButton.tsx` | 6c |
| `ui/src/shell/Shell.module.scss` | 6c |
| `ui/src/shell/SettingsPanel.tsx` · `SettingsPanel.module.scss` | 6c |
| `ui/src/shell/StoryModal.tsx` · `StoryModal.module.scss` · `StoryModalBoundary.tsx` | 6d |

**Every path appears in exactly one row. No path is shared, and no lane may create a file outside
its own row.** Everything else in the tree is spine or untouched.

---

## Merge order

`6a → 6c → 6b → 6d`, building and testing after each.

The order is a dependency graph and not a ritual. **6a merges first** because it is the only lane
whose output the other three can actually see: until the projections are real, every UI lane is
rendering empty payloads and cannot tell a correct render from a blank one. **6c is second** and
shares no file and no seam with 6b or 6d — it may merge as soon as it is reviewed, ahead of 6a if 6a
blocks, and saying so in the merge commit is better than idling. **6b before 6d** so that the panel
the card points the player at exists first.

None of these lanes drives another's code from a test, so all four build in their own worktrees.

---

## Seam vocabulary, published for every lane

Binding group `agora.stories`. Names verbatim, from §4.7:

```
live · archive · article · power · alerts
setResponse · declareManual · resolveNow · spendPowerOverride · ackAlert
```

TS symbols, all exported from `ui/src/shell/bindings.ts`:

```ts
stories$        // Agora.Story[]        sorted by id ordinal
storyArchive$   // Agora.StoryBrief[]   newest first
power$          // Agora.Power          EMPTY_POWER
storyAlerts$    // Agora.StoryAlert[]   oldest first

setStoryResponse(storyId, eventId, mode: Agora.SlotResponseName, text): Promise<WriteOutcome>
declareManualOutcome(storyId, eventId, met: boolean, text): Promise<WriteOutcome>
resolveStoryNow(storyId): Promise<WriteOutcome>
spendPowerOverride(storyId, eventId): Promise<WriteOutcome>
ackStoryAlert(id): Promise<WriteOutcome>          // "*" dismisses all

isAccepted(result) · writeMessage(result)          // do not reimplement either
EMPTY_POWER · EMPTY_STORY_ALERT · EMPTY_STORY_ARTICLE
```

C# symbols 6a codes against:

```csharp
AgoraRuntime.State            // PoliticalState — .LiveStories, .StoryArchive, .Power
AgoraRuntime.CivicCatalog     // CivicEventCatalog — .Find(id), .Events
AgoraRuntime.Tuning           // EngineTuning — .Stories, .Power
AgoraRuntime.StoryProse       // StoryProseLedger — Get(storyId, StoryProseKind, ProseSource)
AgoraRuntime.StoryAlerts      // IList<StoryAlert>

CivicEvent.TierUnder(mandatoryThreshold, majorThreshold) -> StoryTier
PoliticalPower.OverrideCost(tier, tuning) -> int
PoliticalPower.CanAfford(powerState, tier, tuning) -> bool

AgoraUiProjection.StoryArchiveMax = 24 · PowerLedgerMax = 24
```

Enum member names on the wire (C# names, never integers):

```
SlotRole        Major · Minor
StoryTier       Mandatory · Major · Minor
SlotResponse    Unaddressed · Ignore · Goal · PowerOverride · Manual
SlotOutcome     Pending · Met · NotMet · Unmeasurable
StoryOutcome    Pending · Success · Failure · Abandoned
PowerLedgerReason  Accrual · SuccessAward · FailurePenalty · OverrideSpend · ManualAward
PowerIntensity  Lenient · Default · Harsh          (read-only this wave)
StoryDifficulty Forgiving · Default · Demanding    (read-only this wave)
```

---

## What no lane may test

`AgoraRuntime`, `AgoraRuntime.*.cs` and everything under `src/Agora.Mod/UiBindings/` are **not
linkable into the headless suite, by design** — that split is what lets the determinism suite run
without the game installed. No lane may add a `<Compile Link>` line for any of them, and **faking the
runtime or stubbing `Colossal.UI.Binding` to manufacture coverage is a review-blocking defect.**

`ui/**` has no test harness at all. Its obligations are `npx tsc --noEmit` — a **separate obligation**
from `npm run check`, which runs only the design-token guard — and a by-hand diff of CSS class names
against the `.tsx` that reference them, done in review.

The suite is therefore expected to finish this wave at **2178**, unchanged. **A count that has not
risen is normally a defect; here it is the correct outcome**, and it is written down so that nobody
closes the gap by inventing coverage for code the suite cannot reach.

---

## Contradictions with `docs/plans/0004-event-system-rework.md`

These outrank the plan. Found while landing the spine.

1. **Five inbound bindings, not three.** The plan's §605 table lists `setResponse`, `declareManual`
   and `resolveNow`. Wave 4 refuses a purchase arriving through `setResponse` — it would be a free
   `Met` nobody paid for — so `spendPowerOverride` is a fourth, and `ackAlert` is a fifth. Carried
   from waves 4 and 5, now closed.
2. **`storyPause.ts` is not needed and must not be written.** The plan assigns lane 6d a
   `ui/src/shell/storyPause.ts`. `ui/src/shell/pause.ts` already exposes
   `useSimulationHeldPaused(active)`, which is exactly the hook, and it is the game's own refcounted
   barrier — a second module would be a second refcount on one barrier.
3. **`src/Agora.Mod/Core/StoryAlert.cs` is a spine file, not lane 6d's.** It is the seam between the
   publisher (spine) and the card (6d), and the queue that fills it lives in `AgoraRuntime`, which no
   lane may own.
4. **`AgoraStoriesUISystem.cs` is a spine file, not lane 6a's.** It is where every binding *name*
   lives, and three lanes code against those names. 6a owns only the projections behind it.
5. **`AgoraUiProjection` was already `partial`** and `AgoraRuntime` already had `.StoryCommands.cs`,
   so the plan's wave-2 `partial` split had already landed. Nothing to do.
6. **`powerIntensity` and `storyDifficulty` drive nothing.** The plan's wave-2 section says
   `PowerIntensity` drives "the gain/cost/penalty presets". It does not: `TuningPresets.Apply` reads
   `VoteSharpness`, `NewsInfluence` and `BrandDiscipline` and no fourth or fifth level, and no preset
   table exists for either story enum. Both are persisted, cloned and now published, and both are
   **read-only in this build**. Wave 7b's row already owns building them; the write key ships with
   the preset table, not before it.
7. **`AgoraTreasurySystem` was never built and must not be** (carried from wave 4).

---

## Findings for later waves, recorded here rather than acted on

### Wave 7a will break `StoryModal` unless it moves four helpers first

`ui/src/shell/StoryModal.tsx` imports `cx`, `formatSimDate`, `splitParagraphs` and `SEVERITY_STEPS`
from `ui/src/panels/News/format`. **Wave 7a's row is "`ui/src/panels/News/**` (delete)."**

This is not a wave-6 defect. Lane 6d was told to model itself on `ArticleModal.tsx`, which imports
from exactly the same three News modules (`bindings`, `format`, `lookup`) and has done since W5 — so
the lane followed the established precedent, and doing anything else would have been a second copy of
four presentation helpers. But `ArticleModal` is retired *with* the news lane and `StoryModal` is
not, so wave 7a inherits a dangling import that `npx tsc --noEmit` will catch and nothing else will.

**What wave 7a must do before deleting the directory:** move `cx`, `formatSimDate`,
`splitParagraphs` and `SEVERITY_STEPS` somewhere the shell owns — `ui/src/shell/format.ts` is the
obvious home, and lane 6b independently wrote its own copies of the first three inside
`ui/src/panels/Stories/format.ts`, so there will be two copies to reconcile at the same time. Doing
the move first and the delete second keeps it a one-commit refactor instead of a broken build.

### `pauseOnMajorNews` does not govern story cards, and nothing else does either

Raised by lane 6d and confirmed by its review. The card holds the clock on the engine's `major`
verdict alone. A player who has turned **off** "Pause on major news" — whose hint text enumerates
elections, governments, party lifecycle and serious events, all news — still gets force-paused by a
major story, and **has no way to prevent it** short of `storiesEnabled: false`, which turns the whole
feature off.

**6d's choice is correct and is not the thing to change.** Repointing `pauseOnMajorNews` would make a
control whose own hint enumerates news categories silently govern a different surface, and the
symmetric error is just as bad: a player who leaves it *on* has not consented to story pauses either.
Neither position of that toggle is a statement about stories, so neither reading of it is honest.

**The fix is a fifth story setting, `pauseOnMajorStory`, default true, with its own row** — a genuine
setting rather than a repointed one. It is not taken in wave 6 because it is a new persisted field:
settings schemaVersion 5 → 6, a migration step called from **both** the nested-in-state and standalone
paths, and a fixture. That is `/schema-change` work, and wave 6 has moved no sidecar schema at all.
It belongs with wave 7b, which is already opening the settings surface for the power presets.

**Until then it is manual gate row 12 below**, which is what decides whether it is urgent. Note the
mitigation: the card is always dismissable and dismissing releases the barrier, so this is a forced
pause with a working exit, not a freeze.

---

## Manual gate rows this wave opens

Every one of these is here because the code behind it links into no test. **No coverage was
manufactured for any of it.** Wave 4's rows 1–14, which have been waiting for a pressable surface
since that wave closed, become walkable the moment 6b and 6d merge — expect to walk those fourteen
as well as these.

1. **The tab exists and renders the right panel.** Open the dashboard: a fifth tab, **Stories**,
   sits third in the strip. Press it and the Stories panel renders. **Fails if** it renders the
   Council seat chart — `Dashboard.renderTab`'s `default:` falls through to `SeatsPanel`, so a
   missing `case` is the wrong panel silently, with no error anywhere.
2. **A story card appears on a draft month, and exactly one per story.** Let a draft month tick with
   two stories drafting. **Confirm two cards, not six** — one per story, with all three events inside
   each. Confirm a card holds the clock only when its own major slot cleared the threshold, and that
   an ordinary card does not stop the sim at all.
3. **Dismissing a card answers nothing.** Dismiss a card, then open the Stories panel: the story is
   still live, its slots still read `Unaddressed`, and no `PlayerCommands` row was written. Then
   reload: the story is still there and **the card does not come back** — the queue is session state
   and the story is not.
4. **A card from city A never pops over city B.** Load city A, let a story draft, dismiss nothing,
   quit to main menu, load city B. **Confirm no story card from A appears** and that
   `agora.stories.article` is never asked for an id B has never heard of. This is the W0 bug class
   and the ring is cleared in `ResetForNewSave` beside the news one.
5. **Text entry under Gameface — the highest-risk unverified area, now six textareas per story.**
   Focus an Ignore box and press space, digits, `b`, `p`. **The sim must not pause, change speed, or
   open bulldoze.** `PartyEditor.tsx` holds the only other text entry in `ui/src` and it has never
   been rendered in game either, so a failure here is a finding about both.
6. **The power counter renders, and hides when it should.** Confirm the counter sits next to the mod
   icon top left and tracks the ledger. Then set `politicalPowerEnabled` false: **the counter
   disappears** rather than rendering 0. Drive the balance negative and confirm it reads as debt.
7. **An override the player cannot afford is refused legibly.** With the balance below a mandatory
   override's cost, press it: the panel shows the `InsufficientPower` sentence, not a silent no-op
   and not a generic failure. Then with `politicalPowerEnabled` false: **`PowerDisabled`, a different
   sentence**, and the quoted cost renders 0 rather than a live price.
8. **The four settings rows take, and the two read-only levels have no control.** Set
   `storiesPerCycle` to 3 and confirm the next cycle drafts three. Set it to 0 and confirm the
   tuned default returns. **Confirm there is no control for `powerIntensity` or `storyDifficulty`** —
   a control there would be a switch that does nothing.
9. **Both prose voices render, and neither replaces the other.** Open a story card and read it
   (canned prose — the pool answers immediately). Let a CLI round land for that story. **The text
   already read must still be there**, with the model's version alongside. Reload: the canned half
   comes back identical and the model's returns from `flavor_cache.json`.
10. **A slot whose event the catalog no longer explains shows no raw id.** Hand-edit a live story's
    slot to name an event id the catalog does not carry, reload, open the panel. The slot says the
    event is unknown; **it does not print the id where a name belongs.**
11. **A goal whose metric is unreadable shows as held, not failed.** Carried from the plan's gate 5b,
    now walkable: an `Unmeasurable` slot renders as held, costs no power, and is excluded from the
    archive row's `scoredCount`.
12. **The pause-setting gap, walked.** Turn **off** "Pause on major news", then let a major story
    draft. Confirm the clock stops anyway, and that no control in the settings panel prevents it.
    **This row decides whether `pauseOnMajorStory` is urgent or can wait for wave 7b** — walk it
    early. Then dismiss the card and confirm the clock returns to the speed it was on, which is the
    mitigation that makes this a gap rather than a defect.
13. **Two barriers coexist without deadlocking the clock.** Get a major news alert and a major story
    card up at the same time. Dismiss the story card: **the clock must stay held** by the news card,
    and release only when that one goes too. The two queues are independent by contract and neither
    is entitled to sit over the other; this row is where that stops being an assertion.
14. **The mod switched off mid-card.** With a major story card up and the clock held, toggle the mod
    off. The card must vanish **and the clock must come back.** The release is React running an
    effect cleanup, which no static check can confirm.
15. **The boundary's exit is reachable.** Force a render failure inside the story card. Confirm the
    inline notice appears with a pressable control, **the clock is already back before it is
    pressed**, and no further story card pops that session.
