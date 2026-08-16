# Wave 5 — Prose · lane ownership

Umbrella: `event-system/wave-5`. Spine: `1fd2a17 wave-5 spine`.

Base measured before the spine: **build 0 warnings / 0 errors, 2109 tests passed, 0 failed.**
After the spine: **0 / 0, 2124 passed, 0 failed.**

**The one law.** Every file more than one lane would touch was landed in the spine. Lanes own
strictly disjoint paths. A merge conflict is a bug in this table, not something to resolve by hand.

---

## What the spine already landed

Read this before your lane. Several things the plan assigns to a lane are already done.

| Landed | Where |
|---|---|
| `politics_flavor` schemaVersion **3**, `stories[]` + `resolutions[]`, article limits 90→**270** / 420→**1260** | `data/schemas/politics_flavor.schema.json` + `FlavorSchema.EmbeddedJson` |
| Cache migration 1→3 and 2→3, pruning `articles` · `stories` · `resolutions` | `FlavorCacheMigration.cs` |
| Sidecar: flavor cache 2→**3**, settings 4→**5**, state 6→**7** | `SidecarSchema.cs` |
| `LlmWakeCadence.Story`, `llmWakeOnStoryDraft`, engine_tuning 8→**9** | `PoliticalState.cs` · `EngineTuning.cs` · `TickPlanner.cs` |
| `FlavorPayload.Stories` / `.Resolutions` / `.EventProse`, `StoryProse`, `EventProse`, `ProseSource` | `Agora.Core/Contracts/Boundary.cs` |
| `FlavorDocument.Stories` / `.Resolutions` / `.Source`, `StoryProseEntry`, `ToPayload` mapping | `FlavorDocument.cs` |
| `FlavorCatalog.HasStory` / `SortedStoryIds` / `StoryCount`, **5-arg constructor** | `FlavorCatalog.cs` |
| `FlavorRequest.Stories`, `StoryBrief`, `StorySlotBrief`, `FlavorWakeReason.StoryDraft`, `BuildCatalog`, `RosterCopy` | `FlavorRequest.cs` |
| `CivicEventCatalog.Find(id)` | `Agora.Core/Stories/Catalog/CivicEventCatalog.cs` |
| `StoryProseLedger` — the add-don't-replace rule | `Llm/StoryProseLedger.cs` (new, test-linked) |
| Runtime wiring: story briefs, story wake reason, ledger absorb, **`localAngle` write-back** | `AgoraRuntime.cs` |
| `StaticPoolProvider.Generate` stamps `ProseSource.Pool` | `StaticPoolProvider.cs` (one statement — 5c owns the file from here) |

**Pulled into the spine from their planned lanes**, because more than one lane codes against each:
`FlavorDocument.cs`, `FlavorCatalog.cs`, `FlavorRequest.cs`. Lane 5b keeps `FlavorValidator.cs`
alone; lane 5a keeps `FlavorPromptBuilder.cs` alone.

---

## Two owner decisions this wave runs on

1. **A story wake cadence was added.** The CLI is now asked on the month a story drafts — about six
   times a sim year against the yearly wake's one. Gated by `llmWakeOnStoryDraft`, the per-save
   cadence flag, and the story layer.
2. **Claude's prose is ADDED to the canned prose, never substituted for it.** The text a player has
   already read never changes under them. `StoryProseLedger` is the rule; do not write a
   "better source wins" path anywhere.

---

## Lanes

| Lane | Branch | Worktree | Owns (exclusive) | Must not touch |
|---|---|---|---|---|
| **5a** | `event-system/w5-5a` | `.claude/worktrees/w5-5a` | `src/Agora.Mod/Llm/FlavorPromptBuilder.cs`<br>`tests/Agora.Core.Tests/FlavorPromptBuilderTests.cs` | every other file in `Llm/` |
| **5b** | `event-system/w5-5b` | `.claude/worktrees/w5-5b` | `src/Agora.Mod/Llm/FlavorValidator.cs`<br>`tests/Agora.Core.Tests/FlavorValidationTests.cs`<br>`tests/Agora.Core.Tests/FlavorEmptiedRoundTests.cs` | `FlavorCatalog.cs`, `FlavorDocument.cs` — both spine |
| **5c** | `event-system/w5-5c` | `.claude/worktrees/w5-5c` | `src/Agora.Mod/Llm/StaticPoolProvider.cs`<br>`src/Agora.Mod/Llm/StaticPoolContent.cs`<br>`tests/Agora.Core.Tests/StaticPoolPressTests.cs` | `FlavorRequest.cs` — spine |
| **5d** | `event-system/w5-5d` | `.claude/worktrees/w5-5d` | `tests/Agora.Core.Tests/FlavorStoryProseTests.cs` (new)<br>`tests/Agora.Core.Tests/FlavorStoryFallbackTests.cs` (new) | every `src/` file, and every existing test file |

**Path collision check: performed.** No path appears in two rows. Every file the spine touched that a
lane also owns (`StaticPoolProvider.cs`, `FlavorValidationTests.cs`, `FlavorEmptiedRoundTests.cs`,
`StaticPoolPressTests.cs`) is owned by exactly one lane afterwards.

**5d builds in its own worktree** — every signature it needs exists from the spine. Its tests will
*fail* until 5b and 5c merge, which is expected: verify it on the umbrella after those two land.

---

## Seams — both ends, published

Lanes code against these and must not change them.

```csharp
// Agora.Core.Contracts — what crosses the boundary
enum ProseSource { Pool = 0, Cli = 1 }
class StoryProse   { string StoryId; string Headline; string Article; ProseSource Source; }
class EventProse   { string EventId; string LocalAngle; ProseSource Source; }
class FlavorPayload { …; List<StoryProse> Stories; List<StoryProse> Resolutions; List<EventProse> EventProse; }

// Agora.Mod.Llm — the wire shape
class StoryProseEntry { string StoryId; string Headline; string Article;
                        StoryProse ToContract(ProseSource source); }
class FlavorDocument  { …; List<StoryProseEntry> Stories; List<StoryProseEntry> Resolutions;
                        ProseSource Source; }          // defaults to Cli; the pool overwrites it

// Agora.Mod.Llm — what a request tells the writers
class StorySlotBrief { string EventId; bool IsMajor; string Title; string HeadlineBrief;
                       string OutcomeWord; }           // "met" | "not met" | "unmeasurable" | ""
class StoryBrief     { string StoryId; List<StorySlotBrief> Slots; bool IsResolved;
                       string OutcomeWord; }           // "success" | "failure" | "abandoned" | ""
class FlavorRequest  { …; List<StoryBrief> Stories; }

// The id check 5b implements, 5a and 5c must satisfy
bool FlavorCatalog.HasStory(string id);
IReadOnlyList<string> FlavorCatalog.SortedStoryIds();

// The lookup from a slot's event id to its authored text
CivicEvent? CivicEventCatalog.Find(string id);
```

**Key vocabulary.** The JSON keys are `stories` / `resolutions`, each entry `{ storyId, headline,
article }` — note `article`, **not** `body`; `body` is the news-article key and the two are separate
declarations in the schema. Limits are `FlavorCacheMigration.StoryHeadlineMaxLength` (270) and
`StoryArticleMaxLength` (1260) — read the constants, never the numbers.

**The `CivicEvent` fields the fallback is written from:** `Name`, `Description`, `IgnoreText`,
`GoalText`, `PowerOverrideText`, `SuccessText`, and the failure equivalent. Reach them through
`CivicEventCatalog.Find`.

---

## What no lane may do

- **Put a number in `politics_flavor`.** Non-negotiable #1, and this is the wave where the temptation
  is highest. A numeric field anywhere in that schema or in either copy is a review-blocking defect.
- **Write a "best source wins" or "latest wins" path.** See owner decision 2.
- **Touch `AgoraRuntime.cs` or `UiBindings/**`.** Neither compiles into the test suite. Anything
  there gets a manual gate row, never a test, and faking the runtime to manufacture coverage is
  itself a review-blocking defect. The spine already did this wave's runtime work.
- **Run `npm run build` or a bare `dotnet build Agora.sln`.** Both deploy to the player's live
  `…\Mods\Agora.Mod`. Verify with
  `dotnet build src/Agora.Mod/Agora.Mod.csproj -p:UseCsiiToolchain=false` and
  `dotnet test tests\Agora.Core.Tests\Agora.Core.Tests.csproj`.
- **Bump any schemaVersion.** The spine moved every one this wave needs.

---

## Merge order

`5a` and `5c` share no file and no seam and may merge in either order, or early. `5b` after them
(its validator drops entries the other two produce). `5d` last, verified on the umbrella.
