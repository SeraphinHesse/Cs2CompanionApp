# Wave 7 — lane ownership

**Umbrella:** `event-system/wave-7`, cut from `EventSystemRefresh` at `658e853` (wave 6's merged PR
#9). **Spine:** `f1259b8 wave-7 spine`, landed alone before any worktree existed.

**The one law:** every file more than one lane would touch was landed by the orchestrator in the
spine. Lanes below own strictly disjoint paths. **A merge conflict is a bug in this table**, not
something to resolve by hand.

**Base measured, not read:** `dotnet build` 0 warnings 0 errors · **2178 tests, 0 failed** ·
`npx tsc --noEmit` clean. After the spine: **2182 tests, 0 failed**.

---

## Decisions taken before the spine, which outrank the rework plan

The plan's wave-7 section was written before most of this code existed. Six of its instructions were
wrong about the code, and each was resolved by the owner or against the tree before the spine landed.

1. **The news retirement does not retire the alert lane.** The plan lists `agora.news.alerts` and
   `.ackAlert` for removal. That list treats the queue as the feed's popup. It is not: it also
   carries **elections, coalition formation and collapse, and party founding and dissolution** — the
   four things in this mod that happen *to* the player. The story card replaces only the event half.
   Retiring the lane would have meant an election no longer interrupts the player at all, which
   nothing in this rework ever proposed. **Kept.** What is removed is the *article* alert.
2. **`ArticleModal` does not retire either.** It is what renders those cards. The wave-6 handoff
   assumed it went with the panel; it does not, and that assumption is what made the `StoryModal`
   import trap look smaller than it was.
3. **`agora.news.article` stays, narrowed.** The alert cards fetch their bodies from it. The article
   *writer* narrows to the four political kinds rather than retiring, so W5's election coverage
   survives and a card keeps real prose instead of a headline and a one-line summary. **What is
   removed is general monthly coverage**, which existed to fill the feed.
4. **`agora.news.wakeFlavor` stays.** The plan lists it for removal. It is the only manual route to a
   prose refresh anywhere in the mod, and the story system still writes prose. It moves into the
   Stories panel with `flavorStatus` and the mandate tracker.
5. **`AgoraNewsUISystem.cs` is not deleted.** It still publishes five bindings. The plan's row says
   "delete"; it means "reduce".
6. **The mandate tracker goes into the Stories panel**, not its own tab — owner decision. That is
   why lane 7g owns `ui/src/panels/Stories/**` and lane 7e must not touch it.

---

## What the spine already landed — do not redo any of it

| File | What |
|---|---|
| `docs/contracts/ui_bindings.md` | **schemaVersion 10.** §4.5 rewritten; `feed`/`events` and the `NewsHeadline`/`TimelineEventBrief` shapes struck; §4.1 three write keys + `pauseOnMajorStory`; §5 and §6 reconciled |
| `src/Agora.Core/Contracts/PoliticalState.cs` | `AgoraSettings.PauseOnMajorStory` + its `Clone()` line; `AgoraSettings.SchemaVersion` 5 → 6; `PoliticalState.SchemaVersion` 7 → 8 |
| `src/Agora.Mod/Persistence/SidecarSchema.cs` | `CurrentSettingsVersion` 5 → 6, `CurrentStateVersion` 7 → 8; `UpgradeSettingsObjectToV6` called from **both** paths; `MigrateStateV7ToV8`; frozen local constant |
| `src/Agora.Mod/UiBindings/AgoraUiPayloads.cs` | `SettingsPayload.PauseOnMajorStory` + its `Write` line |
| `src/Agora.Mod/UiBindings/AgoraUiProjection.cs` | `BuildSettings` fills it |
| `src/Agora.Mod/Core/AgoraRuntime.cs` | `SetSetting` gains `powerIntensity`, `storyDifficulty`, `pauseOnMajorStory` |
| `tests/…/SidecarMigrationTests.cs` | two fixtures at the old version, the `Strip` list, the reflective helper walk |
| `ui/src/shell/format.ts` · `lookup.ts` | **new** — everything the deleted panel held that outlives it |
| `ui/src/shell/bindings.ts` | `districts$`, `mandates$`, `flavorStatus$`, `article$`, `wakeFlavor`, `EMPTY_NEWS_ARTICLE`, `EMPTY_FLAVOR_STATUS` |
| `ui/src/shell/StoryModal.tsx` | the clock hold gated on `pauseOnMajorStory`; the badge tracks the hold, not the verdict |
| `ui/src/shell/ArticleModal.tsx` | imports repointed to the shell |
| `ui/src/shell/state.ts` · `Dashboard.tsx` | the news tab struck |
| `ui/src/panels/News/**` | **deleted** — see 7g |
| `ui/types/bindings.d.ts` | the mirror, both removals and both additions |
| three panels' `.module.scss` | `max-height: 620rem` → `100%` — the column budget contract |

---

## Lanes

Branch `event-system/w7-<lane>`, worktree `.claude/worktrees/w7-<lane>`, all cut from
`event-system/wave-7` at the spine commit.

### 7a — the C# half of the retirement

| | |
|---|---|
| **Owns, exclusively** | `src/Agora.Mod/UiBindings/AgoraNewsUISystem.cs` · `src/Agora.Mod/UiBindings/AgoraUiProjection.cs` · `src/Agora.Mod/UiBindings/AgoraUiPayloads.cs` · `src/Agora.Mod/Core/AgoraRuntime.cs` · `src/Agora.Mod/Core/NewsAlert.cs` |
| **Must not touch** | anything under `ui/`, `tests/`, `data/`, `docs/` |

Stop publishing `agora.news.feed` and `agora.news.events`; delete `BuildFeed`, `BuildArticle`'s feed
half, the `NewsHeadline` and `TimelineEventBrief` payloads, and the feed/event caps. **Reduce**
`AgoraNewsUISystem` to the five surviving bindings — do not delete the file. Remove
`RaiseArticleAlerts` and the `Article` member of the alert kind union, and re-home
`EMPTY_NEWS_ALERT`'s default kind, which currently reads `"Article"`.

**`BuildArticle` survives, narrowed.** It answers the ids the surviving alerts carry. Do not delete
it — an alert with `hasArticle: true` and no map behind it is a blank masthead with nothing logged,
which is the exact failure §4.5 has warned about since W5.

**Acceptance:** `dotnet build src/Agora.Mod/Agora.Mod.csproj -p:UseCsiiToolchain=false` clean; every
removed binding name absent from the assembly; every surviving one still registered.

### 7g — the UI half, and the re-home

| | |
|---|---|
| **Owns, exclusively** | `ui/src/panels/Stories/**` · `ui/src/shell/ArticleModal.tsx` · `ArticleModal.module.scss` · `ArticleModalBoundary.tsx` |
| **Must not touch** | `ui/src/shell/Shell.module.scss` (7e) · `ui/src/shell/SettingsPanel.*` (7b) · `ui/src/shell/format.ts` / `lookup.ts` / `bindings.ts` / `state.ts` / `Dashboard.tsx` / `StoryModal.tsx` (spine) · any `src/` |

**The panel is already deleted** — the spine did it, because the spine's own contract change is what
stopped it compiling. What is left is the genuinely new work: bring the mandate tracker, the flavor
status line and the manual wake control into the Stories panel.

**`MandateTracker.tsx` was 193 lines and is recoverable verbatim:**
`git show f1259b8^:ui/src/panels/News/MandateTracker.tsx`. Its styles are in the same commit's
`NewsPanel.module.scss`. **Port it rather than rewriting it** — it already gets the one rule that
matters right, that a mandate whose metric is unreadable is *held* and never rendered as behind
schedule. `useLookups` and `formatMonthsRemaining` are in the shell now and need no copy.

The flavor status line and the wake control were in `NewsPanel.tsx` in the same commit. The wake
button must stay disabled while `flavorStatus.pendingWake` is true and must not assume anything
changed as a result — a failed wake keeps the last good flavor by design.

Also fix `ArticleModal.tsx`'s doc comment on `KIND_LABEL`, which compares itself to `NewsFeed`'s map;
that file no longer exists.

**Acceptance:** `npx tsc --noEmit` clean; the mandate tracker renders under Stories with its held
state intact; **CSS class names diffed by hand** — `npm run check` is the design-token guard only and
checks neither this nor types.

### 7b — the balance pass, and the two write keys' other half

| | |
|---|---|
| **Owns, exclusively** | `data/engine_tuning.json` · `data/schemas/engine_tuning.schema.json` · `src/Agora.Core/Tuning/TuningPresets.cs` · `ui/src/shell/SettingsPanel.tsx` · `ui/src/shell/SettingsPanel.module.scss` · `tests/Agora.Core.Tests/StoryPresetTests.cs` (**new file, this name**) |
| **Must not touch** | any other file under `tests/` (7c) · `Shell.module.scss` (7e) · `src/Agora.Mod/**` |

**The spine has already opened `setSetting`'s `powerIntensity` and `storyDifficulty` keys.** Until
this lane merges, both persist a value and change no number — which is the precise defect wave 6
refused to ship. **This lane is therefore not optional and must not be dropped from the wave.** Land
the `PowerIntensity` and `StoryDifficulty` preset tables in `TuningPresets.Apply`, following the
three that already exist: `Default` carries no entry and means "leave the tuning file alone", so a
retune reaches every save that never chose otherwise.

`SettingsPanel.tsx` currently renders both as a read-only line with a comment explaining why. Replace
it with real controls and strike the comment. Add the `pauseOnMajorStory` row — its hint must say it
governs **story** cards, and must not repeat `pauseOnMajorNews`'s news categories.

Then the balance pass: story frequency, effect magnitudes, the power economy. **A story lives
`cycleMonths - 1` months — ONE, not two.** This is the sixth wave to write that sentence down, and
this lane is where it can go wrong again, because every authored threshold in `data/events_*.json`
was sized against that window. Do not retune `cycleMonths` without re-deriving them.

**Acceptance:** each level demonstrably moves a coefficient, pinned by reading `EngineTuning` rather
than asserting a literal; `Default` provably changes nothing; the settings drawer stays inside its
620rem cap with the new rows.

### 7c — the determinism and migration sweep

| | |
|---|---|
| **Owns, exclusively** | `tests/Agora.Core.Tests/**` **except** `StoryPresetTests.cs` (7b) and the eleven flavor files listed under 7f · `data/schemas/political_state.schema.json` |
| **Must not touch** | any `src/` file · any `ui/` file |

The full sweep, plus a fixture built from a **real pre-rework save** at state v4 / settings v3 /
flavor cache v2, proving it loads, upgrades and ticks.

**`data/schemas/political_state.schema.json` is five versions stale and this lane closes it.** It
declares `schemaVersion` `const: 3` (actual 8) and its settings block `const: 2` (actual 6), with
`additionalProperties: false` and none of waves 0–6's fields listed — so as written it would reject
every save this build produces. Nothing validates against it, which is why it drifted unnoticed
through six waves; it is documentation, and documentation that is wrong about the shape of the file
is worse than none. Bring it fully current and **pin it to the C# side with a version-relative test**
rather than a literal, the way wave 1 pinned `snapshot.schema.json`.

**Do not manufacture coverage for `AgoraRuntime` or `UiBindings/**`.** Neither links into this suite
by design, and faking the runtime to produce a number is a review-blocking defect. Write the gate row
instead.

**Acceptance:** count rises and never falls; every schema version this wave moved has a step *and* a
fixture at the old version; `Migrate` proven idempotent over its own output.

### 7d — the documentation the rework owes

| | |
|---|---|
| **Owns, exclusively** | `docs/status.md` · `politicsmodplan.md` · `CLAUDE.md` · `data/CLAUDE.md` · `.claude/skills/add-event/SKILL.md` |
| **Must not touch** | `docs/contracts/ui_bindings.md` (spine) · `docs/plans/**` (orchestrator) · any code |

Ratify §7's new effect kind, add a §15 for the story system, update the routing table, and split
`/add-event` into timeline versus civic-event guidance.

**The `/add-event` split carries wave 3's hardest-won rule:** *an event's prose may only claim what
its effect ids can actually do.* Without it the story system becomes a machine for producing lies
about the city — a headline promising deaths, or a tourism boom, or a prison budget cut, which the
simulation contradicts within the month. The plan's §7 table of what is actually reachable is the
source; carry the specific traps, not a paraphrase.

`docs/status.md` must record the fifty-one outstanding manual gates honestly, not as a footnote.

### 7e — the column budget

| | |
|---|---|
| **Owns, exclusively** | `ui/src/shell/Shell.module.scss` · `ui/src/panels/Seats/**` |
| **Must not touch** | any other panel's `.module.scss` — the spine already set all three to `max-height: 100%` and 7g owns the Stories sheet thereafter |

A ceiling on `.shell`; `.panelSlot` as `flex: 1 1 auto` over `min-height: 0`. **And `SeatsPanel`
gains the scroll region it has never had** — that is the half most likely to be lost if this is filed
as "the settings drawer is too tall". Council overflows on a large chamber with Settings *closed*,
independently of the drawer, and always has.

Two fixes are ruled out with reasons and must not be reopened: collapsing the panel slot
re-litigates the ratified "not a fifth tab" decision, and shrinking Settings below the house 620rem
degrades a correct surface for a spine decision it does not own while leaving Council untouched.

**Flexbox only. Gameface has no CSS grid.** `npx tsc --noEmit` proves nothing about any of this;
state plainly in the report that the evidence is arithmetic over the declared sizes plus a gate row.

### 7f — narrowing the article writer to political events

| | |
|---|---|
| **Owns, exclusively** | `src/Agora.Mod/Llm/FlavorPromptBuilder.cs` · `StaticPoolProvider.cs` · `StaticPoolContent.cs` · `tests/Agora.Core.Tests/FlavorPromptBuilderTests.cs` · `StaticPoolPressTests.cs` · `StaticPoolNamingTests.cs` · `FlavorEmptiedRoundTests.cs` |
| **Must not touch** | `FlavorValidator.cs` · `FlavorDocument.cs` · `FlavorSchema.cs` · `FlavorCacheMigration.cs` · `data/schemas/politics_flavor.schema.json` — **no schema moves in this lane** |

The feed is gone, so general monthly coverage reaches nothing. Both writers stop producing it. What
they keep producing is coverage of **elections, coalition formation and collapse, and party founding
and dissolution** — the four kinds that still raise a card, and the reason W5's election work is not
being discarded.

**This is a prompt-and-pool change, not a contract change.** An article's *shape* is unchanged, so
`politics_flavor` does not move, `FlavorSchema`'s embedded duplicate does not move, and there is no
migration to write. If you find yourself reaching for `/schema-change`, stop: something has been
misread, and the schema files are another lane's anyway.

W5 ratified 7 (NA) / 8 (EU) articles on an election wake against 4 in an ordinary month. With the
ordinary month's four gone, **re-derive the election count rather than subtracting** — the 4 was
general coverage that election coverage was added *beside*, not a floor it was measured from.

**Unlike most of this wave, all three of these files link into the headless suite** (`<Compile Link>`
in the test csproj), so this lane's claims are testable and a green suite is real evidence here.

---

## Merge order

```
7f, 7e, 7d   — no seam with anything; merge as soon as each is reviewed
7a           — the C# retirement
7g           — the UI re-home            (after 7a: reads the surviving binding set)
7b           — presets + write keys      (MUST land; the spine opened keys it backs)
7c           — the sweep                 (last: it tests what the others changed)
```

7f, 7e and 7d share no file and no seam with anything in flight; say so in the merge commit rather
than idling. 7c merges last by dependency, not by ritual — its sweep is over the merged tree.

## Path collision check

Every path above appears in **exactly one** row. The three that were contested and how each was
resolved:

- **`ui/src/panels/Stories/StoriesPanel.module.scss`** — 7g (mandate section) vs 7e (height budget).
  The spine landed the one-line budget change; **7g owns the file thereafter and 7e is barred.**
- **`ui/src/shell/SettingsPanel.*`** — 7b (new rows) vs 7e (column budget). SettingsPanel imports its
  own sheet, not `Shell.module.scss`, so the two never meet. **7b owns both; 7e owns `Shell`.**
- **`tests/Agora.Core.Tests/**`** — 7c (the sweep) vs 7b (preset tests) vs 7f (writer tests). Split by
  **named file**, listed in each row. A lane adding a test file not named in its row is a collision.

## What no lane owns, and why

`docs/contracts/ui_bindings.md`, `SidecarSchema.cs`, `PoliticalState.cs`, `ui/src/shell/format.ts`,
`lookup.ts`, `bindings.ts`, `state.ts`, `Dashboard.tsx`, `StoryModal.tsx`, and the Districts and
Parties style sheets are **spine-only for the rest of the wave**. A lane that needs one of them has
found a bug in this table; report it rather than editing.

## The orchestrator's own deliverable, after every lane merges

A **plain-English walkthrough of all fifty-one outstanding manual gates**, published as an artifact —
owner's request. Not a lane: it can only be written against the finished wave. Every row is currently
spread across five handoff documents in the vocabulary of the code that produced it; the deliverable
is one ordered document a person can follow at the keyboard without reading any of them, sequenced so
that one play session covers as many as possible.
