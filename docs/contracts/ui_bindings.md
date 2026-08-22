# Contract — C# ↔ UI bindings

**schemaVersion: 10**

The fourth data contract. It spans two languages and two build systems, so nothing checks it at
compile time: rename a binding on one side and the panel silently renders nothing. Every binding
must be listed here, and this file is the authority when the two sides disagree.

**Frozen for M4.** Names and payload shapes in the *Registered bindings* table are law. Do not
rename, do not add a field, do not reorder a sort key. If a panel needs something that is not here,
report it — do not invent a binding name locally.

**Unfrozen four times, on the record.** Plan 0001 (`docs/plans/0001-batched-schema-change.md`) added
three fields to `PartyBrief` on 2026-08-08 under `/schema-change`, which is the only route through the
freeze: a version bump on this file, both sides moved in the same pass, and the reason written down.
W3 then published `agora.state.settings` and `agora.state.isFirstRun` out of §8 and added
`agora.state.setSetting`, the contract's first inbound `CallBinding` — three names that were
**already reserved here**, so nothing was renamed and no existing payload moved.

**W4, on 2026-08-08**, took the same route for the party editors. The reason: player-owned party
identity needs a write surface and two read bindings that **were never reserved** — §8 had reserved
the settings channel in advance but nothing for editing a party, so there was no reserved name to
publish out of. What moved: two fields on `PartyBrief` (`description`, `slogan`), six inbound
`CallBinding`s (`rename`, `setDescription`, `setColor`, `resetName`, `resetDescription`,
`resetColor`), two outbound `ValueBinding`s (`colorPalette`, `editLimits`), and four outcome codes
(`NotFound`, `ValueRequired`, `TooLong`, `OkColorInUse`). **Nothing was renamed and no existing
payload field moved or changed type** — which is the property the freeze actually protects; a purely
additive change cannot break a consumer that has not been updated.

**W6, on 2026-08-08**, unfroze `agora.parties` a second time, under plan
`docs/plans/0002-w6-parties-tab.md`, to add the read side of the Parties tab: three map bindings
(`detail`, `pollTrend`, `electionRecord`) and four new payload shapes — `PartyDetail`,
`IssuePositionView`, `PollTrendPoint` and `PartyElectionRow`. Nothing existing was renamed or
reordered; this too is purely additive.

**Version 6 covers the W4 and W6 work landing together in this pass** — the party editors and the
whole of plan 0002, Part I and Part II, which move this file once rather than once per chunk. Both
branches independently wrote version 5 for their own half; merging them is a further change, so the
merged file reads value + 1. Any later landing that changes a shape here reads the current value and
writes value + 1; the number is read from this file, never hard-coded.

**Version 7 is plan 0002 chunk H**, which adds a fourth `agora.parties` map binding, `relations`, and
one payload shape, `CoalitionOption` — the coalition arithmetic of the open party. Additive again:
nothing was renamed and no existing payload moved. It is the first binding whose value is *computed*
on fetch rather than copied from stored state, which is why §4.2 states plainly that the engine does
the computing (`CoalitionFormation.RankCandidates`, pure and RNG-free) and this bridge only copies —
rule 5 is unmoved.

**Version 8 is W5's popup lane**, under plan `docs/plans/0003-w5-popup-lane.md`, and it unfreezes
`agora.news` for the first time. What moves: one `ValueBinding` (`alerts`), one inbound
`CallBinding` (`ackAlert`), and one payload shape (`NewsAlert`, with its own kind union
`NewsAlertKindName`). Purely additive again — nothing renamed, no existing field moved or retyped,
no sort key reordered, and the feed's own admission policy untouched: the News tab keeps every fired
event at every severity, because the feed is an archive and the popup is an interruption and the two
must not share a policy. No sidecar, settings or flavor schema moves with it; the alert queue is
session state and is deliberately never persisted, which is what makes "an alert does not replay
after a reload" structural rather than a rule.

**Version 9 is wave 6 of the event-system rework** (`docs/plans/0004-event-system-rework.md`), and it
is the largest unfreeze so far: an entire new group, `agora.stories` (§4.7), carrying five outbound
bindings and **five inbound `CallBinding`s** — the first write surface in this contract that a player
uses every month rather than once per save. Purely additive again: nothing renamed, no existing field
moved or retyped, no sort key reordered.

Three things moved outside the new group, and each is here rather than in §4.7 because it changes a
shape that already existed:

- **`SettingsPayload` gains six fields** (§4.1). They have been in the sidecar since wave 2 of the
  rework and reachable from no surface since — a per-save setting nothing can read or write is a
  setting only a text editor can change. Four are writable; `powerIntensity` and `storyDifficulty`
  are **read-only in this build and have no `setSetting` key**, because the presets behind them do
  not exist yet (`TuningPresets.Apply` reads three levels, not five). Publishing a control that
  persists a value and changes no number is the defect `PauseOnMajorNews` and `ShowAllReports` were
  before W5, and it is not being shipped a second time. Wave 7b adds the presets and the write keys
  together.
- **§4.6 gains three outcome codes** — `InsufficientPower`, `AlreadyResolved`, `PowerDisabled`. All
  three landed in the C# enum in wave 4 and reach this contract now, when the story command surface
  first got a caller. The C#-enum-first rule was followed; this is the second step of it, late.
- **`ui/types/bindings.d.ts`'s stale authority comment** said "schemaVersion 5" while this file was
  at 7 and then 8 (`docs/status.md` known gap 2). Corrected to 9 in the same pass, which is what the
  drift audit exists to catch.

**Version 10 is wave 7 of the event-system rework, and it is the first version of this contract that
REMOVES anything.** Every unfreeze before it was purely additive. This one retires the news feed the
story system replaced, which is the "remove the old name in a **later** change" half of §7's
never-rename-in-place rule — wave 6 built the replacement, and this is what makes the removal safe.

**Removed:** `agora.news.feed`, `agora.news.events`, and the payload shapes `NewsHeadline` and
`TimelineEventBrief`. A consumer still reading either binding renders nothing; there are none,
because `ui/src/panels/News/**` is deleted in the same wave.

**Kept, and each for a reason that outlived the panel:**

- **`agora.news.article` stays, narrowed.** It served two callers: the feed's reader, which is gone,
  and the alert card's body fetch, which is not. It now answers only the ids the surviving alerts
  carry — election, coalition and party-lifecycle prose. Wave 7 keeps the article *writer* scoped to
  those same four kinds rather than retiring it, so the cards keep a real body instead of degrading
  to a headline and a one-line summary; W5's election coverage was built deliberately and is not
  discarded here. **What is removed is general monthly coverage**, which existed to fill the feed.
- **`agora.news.alerts` / `agora.news.ackAlert` stay.** The plan's own removal list named them, and
  that list was wrong about what they carry. The alert queue is not the feed's popup — it also
  carries **elections, coalition formation and collapse, and party founding and dissolution**, and
  the story card replaces only the event half of it. Retiring the lane would have meant an election
  no longer interrupts the player at all, which nothing in this rework ever proposed. What *is*
  removed is the **article alert**: with the feed gone there is no general monthly prose to
  interrupt over.
- **`agora.news.mandates` stays**, as the plan says: the mandate tracker is unrelated to stories, is
  consumed by the Parties tab too, and is the one part of the News panel worth keeping. Its
  **renderer** moved into the Stories panel; its **binding name did not move**, because renaming in
  place is what §7 forbids.
- **`agora.news.flavorStatus` and `agora.news.wakeFlavor` stay.** `wakeFlavor` is on the plan's
  removal list and should not have been: it is the only manual route to a prose refresh anywhere in
  the mod, and the story system still writes prose. Both controls moved into the Stories panel with
  the mandate tracker.

So `agora.news` shrinks from eight bindings to six and keeps its name. **The group is now named for
a panel that no longer exists**, which is ugly and is deliberate: renaming it to `agora.stories.*`
would break six live consumers to fix a word, and §7's rule exists precisely to stop that trade.

**Two shapes changed outside the removals:**

- **`SettingsPayload` gains `pauseOnMajorStory`** (§4.1), default true, with a write key. Wave 6
  shipped the story card holding the clock on the engine's `major` verdict alone, with no way to stop
  it short of turning stories off entirely. It is a **new persisted field**: sidecar settings 5 → 6
  and state 7 → 8, migrated through both paths, with a fixture at the old version.
- **`powerIntensity` and `storyDifficulty` gain write keys** (§4.1). Version 9 published them
  read-only and said why — the presets behind them did not exist. Wave 7 lands the preset tables and
  the keys in the same wave, so the setting and its effect reach a player together.

The freeze otherwise stands; these notes exist so the next reviewer reads authorised changes rather
than violations.

---

## 1. Naming

`agora.<area>.<name>` — lowercase group, `camelCase` name. The group prefix `agora` is reserved for
this mod. The JS side addresses a binding as two arguments, `(group, name)`:

```tsx
const seats$ = bindValue<Agora.SeatRow[]>("agora.seats", "allocation", []);
```

Six areas exist, published by five `UISystemBase` subclasses. `agora.state` and `agora.parties`
share `AgoraStateUISystem`, which declares both group constants: the roster and faction tables are
republished on the same monthly tick as the state summary, and splitting them across two systems
would mean two publishers reading the same `PoliticalState` in the same frame. Every other area has
exactly one publisher of its own.

| Area | Owns | Publisher |
|---|---|---|
| `agora.state` | dashboard chrome: is there a political state, what date, what term | `src/Agora.Mod/UiBindings/AgoraStateUISystem.cs` |
| `agora.parties` | the party/faction lookup table every panel renders labels and colours from | `src/Agora.Mod/UiBindings/AgoraStateUISystem.cs` |
| `agora.seats` | seat chart, government breakdown, mayor, last election, latest poll | `src/Agora.Mod/UiBindings/AgoraSeatsUISystem.cs` |
| `agora.districts` | per-district vote splits, wealth × education crosstabs, indices | `src/Agora.Mod/UiBindings/AgoraDistrictsUISystem.cs` |
| `agora.news` | mandate tracker, LLM health, the political-event alert queue. **The feed and the timeline-event list retired in v10**; the group keeps its name because renaming in place is what §7 forbids. | `src/Agora.Mod/UiBindings/AgoraNewsUISystem.cs` |
| `agora.stories` | live stories, the archive, story prose, political power, the story card queue, and the five commands that answer a story | `src/Agora.Mod/UiBindings/AgoraStoriesUISystem.cs` |
| `agora.debug` | M0 pipeline proof — not part of the dashboard, do not extend | `src/Agora.Mod/UiBindings/AgoraDebugUISystem.cs` |

---

## 2. Wire conventions

Identical to the engine's JSON conventions so a payload can be produced straight from a contract
object with no translation table.

| Thing | On the wire | Notes |
|---|---|---|
| Property names | `camelCase` | matches `Agora.Core.Contracts` property names lower-cased |
| `SimDate` | `"YYYY-MM-DD"` string | never a JS `Date`, never a number |
| `SimDate?` | `""` when absent | **never `null`** — an empty string keeps every date field a `string` in TS |
| `string?` (ids) | `""` when absent | **never `null`** — same reason |
| Nullable *object* payloads | `null` | only the four documented ones: `seats.government`, `seats.mayor`, `seats.lastElection`, `seats.latestPoll` |
| Enums | C# member name string | `"Governing"`, `"Proportional"`, `"HeritageOrder"`. Never the integer. |
| Shares, rates, indices, progress | `number` in `[0,1]` | the panel formats as a percentage; C# never pre-multiplies by 100 |
| Happiness | `number` in `[0,100]` | the one exception, because the game's own scale is 0–100 |
| Counts (population, seats, votes) | integer `number` | a one-vote margin must survive the bridge |
| Money | integer `number` | `long` on the C# side; values stay well inside `Number.MAX_SAFE_INTEGER` |
| Lists | JSON array | **every list has a documented sort key below — honour it**, the panel does not re-sort |

### Payload budget

These cross a Gameface bridge on every update. Keep them flat and bounded:

- No payload nests more than one level (a row may contain a small named group like `wealth`; it may
  not contain a list of rows that themselves contain lists).
- Unbounded histories are capped and the cap is part of the contract:
  `AGORA_ELECTION_HISTORY_MAX = 12`, `AGORA_POLL_TREND_MAX = 24`,
  `AGORA_COALITION_OPTIONS_MAX = 8`. (`AGORA_NEWS_FEED_MAX` and `AGORA_EVENTS_MAX` retired with the
  feed in v10.)
- Anything per-district and expensive is a **map binding**, fetched only for the key the panel is
  actually showing — never a city-wide array of every district's full detail.
- Prose bodies never ride in a list payload. A list carries headline + one-line summary; the body
  arrives through a map binding, fetched only when an item is opened — `agora.news.article` for an
  alert card, `agora.stories.article` for a story.

---

## 3. Binding kinds

| Need | C# | JS |
|---|---|---|
| Cheap scalar, recomputed every UI tick | `GetterValueBinding<T>` + `AddUpdateBinding` | `bindValue` + `useValue` |
| Payload pushed when the engine changes it | `ValueBinding<T>` + `AddBinding`, then `.Update(v)` | `bindValue` + `useValue` |
| Per-key detail, fetched on demand | `GetterMapBinding<string,T>` + `AddBinding` | `bindMap` + `useMapValue` |
| UI asks C# to do something, no answer needed | `TriggerBinding` + `AddBinding` | `bindTrigger` |
| UI asks C# to do something **and needs the verdict** | `CallBinding<…,TResult>` + `AddBinding` | `call` |
| UI-only state shared between panels | — | `bindLocalValue` — **never a round trip through C#** |

A `CallBinding` returns a **`CommandOutcome` in wire form** (§4.6), never a payload and never a
`bool`. The rule for choosing between the two inbound kinds: a trigger is right when the only
possible failure is "the engine declined and that is fine" — `agora.news.wakeFlavor`, where a refused
wake looks identical to a successful one that produced nothing. A call is required the moment the
player must be told *why*, because a request that silently does not take is indistinguishable from a
broken panel.

Selection state (which district is open, which party is highlighted) is UI-only. Use
`bindLocalValue`; do not add a binding for it.

---

## 4. Registered bindings

Direction is C# → UI unless a row says otherwise. **Fourteen** bindings run the other way and every
one of them is marked **UI → C#** in its own table: `agora.news.wakeFlavor` (the only trigger),
`agora.news.ackAlert`, `agora.state.setSetting`, the six party editors in §4.2, and the five story
commands in §4.7. "Cadence" is when the publisher calls
`Update`, not how often the UI re-renders. An inbound binding has no cadence and no empty value: its
row reads `n/a` where the table has those columns, and §4.2 drops them entirely.

### 4.0 `agora.debug` — M0 pipeline proof (closed)

| Binding | Kind | C# type | TS type | Cadence | Empty / loading | Since |
|---|---|---|---|---|---|---|
| `agora.debug.simDay` | `GetterValueBinding<int>` | `int` | `number` | UI tick | `0` | M0 |
| `agora.debug.enabled` | `GetterValueBinding<bool>` | `bool` | `boolean` | UI tick | `false` | M0 |
| `agora.debug.simDate` | `GetterValueBinding<string>` | `string` | `string` | UI tick | `""` | M0 |

Consumer: `ui/src/shell/bindings.ts`, read by `ui/src/shell/AgoraButton.tsx`. This area is closed —
it exists to prove the C# → JS pipeline still works, not to carry dashboard data. Dashboard panels
use `agora.state.*`; they must **not** read `agora.debug.enabled` for the master toggle even though
it currently returns the same value, and the button does not.

`simDate` and `simDay` moved here when `ui/src/panels/DebugPanel.tsx` was retired and its readout
folded into the always-mounted toggle button. The area kept its purpose: these are UI-tick getters
straight off the clock, alive from the first frame in a loaded game, whereas `agora.state.summary`
is empty until the engine's first monthly publish. Read the date from `agora.state.summary.date`
for anything that is *about* the political state; read it from here only to prove the bridge works.

### 4.1 `agora.state` — dashboard chrome

| Binding | Kind | Direction | C# type | TS type | Cadence | Empty / loading | Since |
|---|---|---|---|---|---|---|---|
| `agora.state.enabled` | `GetterValueBinding<bool>` | C# → UI | `bool` | `boolean` | UI tick | `false` | M4 |
| `agora.state.ready` | `GetterValueBinding<bool>` | C# → UI | `bool` | `boolean` | UI tick | `false` | M4 |
| `agora.state.summary` | `ValueBinding<T>` | C# → UI | `StateSummaryPayload : IJsonWritable` | `Agora.StateSummary` | monthly + on election | `EMPTY_STATE_SUMMARY` | M4 |
| `agora.state.settings` | `ValueBinding<T>` | C# → UI | `SettingsPayload : IJsonWritable` | `Agora.SettingsPayload` | monthly + on every accepted `setSetting` | `EMPTY_SETTINGS` | W3 |
| `agora.state.isFirstRun` | `GetterValueBinding<bool>` | C# → UI | `bool` | `boolean` | UI tick | `false` | W3 |
| `agora.state.setSetting` | `CallBinding<string,string,string>` | **UI → C#** | `(key, value) => CommandOutcome` | `(key: string, value: string) => Promise<Agora.CommandOutcomeName>` | on click | n/a | W3 |

`enabled` is the master toggle — when false a panel renders `null`, not a disabled shell. `ready` is
true once the engine has published a political state at least once; until then panels render a
skeleton, because every other binding in this contract is still at its empty value.

`settings` is the per-save settings document, sidecar-backed and **never global config**
(non-negotiable #10). It is a mirror: the panel renders it and writes through `setSetting`, never the
other way round.

The three `*Value` fields are **display only**. They carry the coefficient each chosen level
currently resolves to, read from `engine_tuning.json` through `Agora.Core.Tuning.TuningPresets`, so
the panel can show what a level means without holding its own copy of a number the engine owns. They
are not settable and have no `setSetting` key. A tuning file that failed to load publishes them as
zero, which the panel renders as absent rather than as a real value.

`isFirstRun` is a **one-shot lifecycle signal** — this save has never chosen a region theme. True
only when the sidecar carried neither a political state nor a settings document; it goes false the
moment the theme is chosen or the dialog is dismissed, and it is never persisted. It is a getter, not
a pushed value, because it must flip inside the same UI tick that answered the prompt — the sim is
paused while the dialog is open and there is no engine tick to push on. It is deliberately **not** a
field of `SettingsPayload`: putting a value the sidecar never stores inside the payload that mirrors
the sidecar is how the two come to disagree.

`setSetting` is the contract's first write channel. Keys, and their legal values:

| Key | Value | Effect |
|---|---|---|
| `theme` | `"Eu"` \| `"Na"` | Regenerates the party registry, factions, standings, polls, government and mandates at the save's start date. Rejected with `ThemeLocked` once an election has been held; `Busy` while a prose generation is in flight. |
| `pauseOnMajorNews` | `"true"` \| `"false"` | Per-save (W5). |
| `showAllReports` | `"true"` \| `"false"` | Per-save (W5). |
| `effectsEnabled` | `"true"` \| `"false"` | The per-save effect kill switch. |
| `voteSharpness` | `"Blurred"` \| `"Default"` \| `"Sharp"` | How decisively blocs convert preference into votes (`affinity.softmaxTemperature`). Enum **name**, case-sensitive; an all-digit value is `BadValue`. Takes effect at the next engine tick. |
| `newsInfluence` | `"Muted"` \| `"Default"` \| `"Loud"` | How far a live event can move a bloc (`affinity.eventModifierWeight`). Same parsing rule. |
| `brandDiscipline` | `"Loose"` \| `"Default"` \| `"Locked"` | How tightly fixed brands hold their archetype (`parties.anchoredSpreadSigma`). Read **only at party generation**, so an accepted write changes nothing visible until the registry is regenerated. |
| `storiesEnabled` | `"true"` \| `"false"` | The per-save story kill switch (W6). Off stops the **next** draft and strands nothing: a story already live still resolves on its own month. Nothing is ever retro-generated. |
| `storiesPerCycle` | `"0"`–`"5"` | Stories drafted per cycle. **`"0"` means unset** — the engine falls back to `stories.storiesPerCycle`, which is how a player hands the decision back to tuning. Outside the range is `BadValue`. Parsed invariant; a non-decimal value is `BadValue`. |
| `eventsPerStory` | `"0"`–`"5"` | Events bundled into one story, same unset rule and same range. |
| `politicalPowerEnabled` | `"true"` \| `"false"` | The per-save power kill switch (W6). Off means overrides answer `PowerDisabled` and no debt penalty can arise; stories still draft and resolve. |
| `powerIntensity` | `"Lenient"` \| `"Default"` \| `"Harsh"` | How punishing the power economy is — the gain, cost and penalty presets (wave 7). Enum **name**, case-sensitive, same parsing rule as `voteSharpness`. `"Default"` carries no preset entry and means "leave the tuning file alone", so a retune reaches every save that never chose otherwise. |
| `storyDifficulty` | `"Forgiving"` \| `"Default"` \| `"Demanding"` | How hard story goals are to meet — the `stories` check-scaling presets (wave 7). Same parsing and same `"Default"` rule. |
| `pauseOnMajorStory` | `"true"` \| `"false"` | Whether a **story** card the engine has judged major holds the clock. Default true (wave 7). Governs only whether the sim stops — the card appears either way and is always dismissable. |
| `dismissFirstRun` | ignored | Clears `isFirstRun` without changing a setting. Not persisted. |

**`powerIntensity` and `storyDifficulty` were deliberately keyless in v9**, and the reason is worth
keeping because it is the rule, not the episode: both were published on `SettingsPayload` and both
drove nothing — `TuningPresets.Apply` read three levels and no preset table stood behind these two —
so a write would have persisted a value, republished it, and changed no number in the engine. A
switch that does nothing, with hint text promising behaviour there is none of, is exactly what
`PauseOnMajorNews` and `ShowAllReports` were before W5. **The rule this encodes: a write key and the
thing it drives ship in the same change.** Wave 7 landed the preset tables, so the keys open here.

**`pauseOnMajorStory` is not `pauseOnMajorNews` under another name**, and a panel must never write
one when the player asked for the other. `pauseOnMajorNews`'s hint enumerates elections, governments,
party lifecycle and serious events — all *news* — so neither of its positions is an answer about
stories. Repointing it would have silently redefined a choice existing saves already made about
something else; a fifth setting was the honest route and it costs a sidecar migration, which v10 pays.

The upper bound of 5 on the two counts is a bound on what a settings control may ask for, not a
balance number: wave 2's concurrency retune sized the story effect budget and its non-saturation
claim around 2 stories × 3 events, and asking for an order of magnitude more is a rebalance, which
belongs in `engine_tuning.json` where the effect scales and the pool size move with it.

Anything else returns `UnknownKey`. A theme change **destroys** the political state built under the
old theme — party ids are reused across themes with different meanings, so nothing keyed to one can
survive. That is why the guard is the election history rather than a flag, and why the panel must
confirm before calling.

Outcome codes are a closed set — see §4.6.

### 4.2 `agora.parties` — shared lookup table + the party editors

Reads — every row here is C# → UI:

| Binding | Kind | C# type | TS type | Cadence | Empty / loading | Since |
|---|---|---|---|---|---|---|
| `agora.parties.roster` | `ValueBinding<T>` | `List<PartyBrief>` | `Agora.PartyBrief[]` | on party lifecycle change (rare) + monthly | `[]` | M4 |
| `agora.parties.factions` | `ValueBinding<T>` | `List<FactionBrief>` | `Agora.FactionBrief[]` | on faction lifecycle change (rare) + monthly | `[]` | M4 |
| `agora.parties.colorPalette` | `ValueBinding<T>` | `PartyPalette : IJsonWritable` | `Agora.PartyPalette` | with the roster, on the same version-gated tick | `EMPTY_PARTY_PALETTE` | W4 |
| `agora.parties.editLimits` | `ValueBinding<T>` | `PartyEditLimits : IJsonWritable` | `Agora.PartyEditLimits` | with the roster, on the same version-gated tick | `EMPTY_PARTY_EDIT_LIMITS` | W4 |
| `agora.parties.detail` | `GetterMapBinding<string,T>` | `PartyDetail` per key | `Agora.PartyDetail` | on demand, per subscribed key | `EMPTY_PARTY_DETAIL` | W6 |
| `agora.parties.pollTrend` | `GetterMapBinding<string,T>` | `List<PollTrendPoint>` per key | `Agora.PollTrendPoint[]` | on demand, per subscribed key | `[]` | W6 |
| `agora.parties.electionRecord` | `GetterMapBinding<string,T>` | `List<PartyElectionRow>` per key | `Agora.PartyElectionRow[]` | on demand, per subscribed key | `[]` | W6 |
| `agora.parties.relations` | `GetterMapBinding<string,T>` | `List<CoalitionOption>` per key | `Agora.CoalitionOption[]` | on demand, per subscribed key | `[]` | W6 |

The map key is the **party id** exactly as it appears in `PartyBrief.id`. An unknown key returns the
empty value, never throws. Unlike a district, a party id is never removed from the roster — a dead
party becomes `Dissolved` — so an id that resolves once resolves for the life of the save.

`detail` is a map rather than fields on `PartyBrief` for the reason every map binding here exists:
the roster is pushed to every panel on every monthly tick, and twelve issue positions plus polling
per party is not something the seat chart or the news feed needs to carry.

On `agora.parties.detail`: `platform` and `lastManifesto` are rendered together as a drift comparison
(fixplan W6 addition 1); both were in the binding from the start. No payload changed and no version
moved on its account — the pane draws the manifesto as a tick over the current-platform bars and
suppresses both the tick and the drift line when `hasContestedElection` is false, because
`lastManifesto` defaults to dead centre.

`pollTrend` is the detail pane's sparkline: one party's published poll shares over time, fetched for
the open party alone. It is party-scoped rather than city-wide because a city-wide series of
per-party shares is a list of rows containing lists, which §2 forbids, and flattening it to one row
per (date × party) would push hundreds of rows every monthly tick for a chart the player sees only
when one pane is open. `agora.seats.pollTrend` stays reserved (§8) for M6's city-wide chart. Points
carry the **published** share only — `PollResult.TrueShares` never crosses the bridge (rule 8) — and
a poll with no entry for the party contributes no point rather than a zero, because a party that did
not exist yet did not poll at 0%.

`electionRecord` is the detail pane's history strip: one party's result at each election it took part
in, fetched for the open party alone. It is a separate list rather than a field on `PartyDetail`
because seats-per-election is a list of rows and §2 allows one level of nesting, not two. An election
the party had **no part in — absent from the ballot and absent from the seat table — contributes no
row**, so the series is not a calendar and a gap in it is not a wipeout. `wasOnBallot` is the ballot
list's own answer and separates *stood and took nothing* from *did not stand*; a row carrying it
**false** is the rare case where the seat table names a party the ballot list does not, and the row is
published rather than dropped, because the seats are real. `hasSeatRecord` is the converse and says
whether the seat table produced a row at all: a party that stood while the count allocated nothing —
FPTP's empty result for a city with no districts — has `passedThreshold`, `seats`, `seatShare` and
`voteShare` at their unset defaults, so the strip shows the contest but states no threshold verdict.
`relations` is a **live** view recomputed from current platforms via
`CoalitionFormation.RankCandidates`, not the historical record of who negotiated after the last
election. It answers "who could govern now". **Empty under FPTP by design** — the winning party
governs alone and there is no coalition arithmetic to report — so the panel branches on
`summary.system` and never on the list being empty (§4.3's rule). Every arrangement containing the
open party is listed, best first; one that never appears was not refused by name, it simply was not
viable, and there is no per-partner refusal field because the ranking cannot say which of the two
rejection rules a set it never built would have failed. Chamber seats come from the latest election,
so a city between a collapse and a new formation still has an answer.

**Before the first election the chamber is projected, not counted.** With `ElectionHistory` empty,
`BuildPartyRelations` ranks off `ProvisionalChamber.Project` — the chamber the save's latest
**published** poll would seat, allocated by the same `ProportionalAllocator` a real ballot uses. It
is pure (no state written, no election recorded, no naked randomness: the only draw reachable is the
allocator's named `election.tiebreak` stream), it lives in `Agora.Core` like the ranking, and it
adds **no persisted field and no schema change** — which is what the live-view design was chosen
for. The projection is never used to fill in for an election whose seat list came back empty: a real
chamber is never overwritten by a hypothetical one. It reads the published poll and never
`state.CurrentVoteShares` or `PollResult.TrueShares`, for the same reason `BuildLatestPoll` refuses
them — the dashboard shows what is publicly known. **A panel rendering projected seats must say they
are projected.**

**FPTP is not the only empty case, and the panel must not imply it is.** `BuildPartyRelations`
returns `[]` on **four** paths: FPTP, no chamber to read (neither an election nor a published poll),
a latest election that is null, and *no ranked candidate contains this party*. The second covers a
save's opening months, before the first poll publishes; the last is ordinary in an established city.

"Empty" must still never be rendered as an **inference** about elections having happened —
`state.Government` is read independently and a null government is equally "never voted" and "between
a collapse and a new formation" (W6 chunk H9; a first draft asserted the city had never voted and
was blocked in review). What the panel *may* now say is what a published binding states outright:
`agora.seats.allocation` is non-empty **iff** this city has a counted chamber, and
`agora.seats.latestPoll` is non-null **iff** a poll has published. Those two are facts on the wire,
not deductions from a list's length, and they are what separates "coalition options appear once the
first poll is published" from "no arrangement is viable as things stand" — two different states that
one sentence used to cover. Branch on `summary.system` for the FPTP sentence, on those two bindings
for the rest, and never on `relations.length`.

Enumeration is bounded by
`coalitions.formationMaxPartners`, which is why this is a map binding fetched for one open pane and
must never become a `GetterValueBinding` re-running it every UI tick (rule 6).

`PartyDetail` carries the lineage scalars
that go beside `electionRecord` — `predecessorPartyId`, `successorPartyId`, `revivalCount` and `absorbedPartyIds`,
the last being the reverse index of `successorPartyId`, since the forward pointer alone cannot tell a
party that absorbed three rivals that it did.

Writes — **UI → C#**, all six. Every one returns a `CommandOutcome` in wire form (§4.6); none has a
cadence or an empty value:

| Binding | C# type | Args | TS signature | Since |
|---|---|---|---|---|
| `agora.parties.rename` | `CallBinding<string,string,string,string>` | `(partyId, name, shortName)` | `(partyId: string, name: string, shortName: string) => Promise<Agora.CommandOutcomeName>` | W4 |
| `agora.parties.setDescription` | `CallBinding<string,string,string,string>` | `(partyId, description, slogan)` | `(partyId: string, description: string, slogan: string) => Promise<Agora.CommandOutcomeName>` | W4 |
| `agora.parties.setColor` | `CallBinding<string,string,string>` | `(partyId, colorHex)` | `(partyId: string, colorHex: string) => Promise<Agora.CommandOutcomeName>` | W4 |
| `agora.parties.resetName` | `CallBinding<string,string>` | `(partyId)` | `(partyId: string) => Promise<Agora.CommandOutcomeName>` | W4 |
| `agora.parties.resetDescription` | `CallBinding<string,string>` | `(partyId)` | `(partyId: string) => Promise<Agora.CommandOutcomeName>` | W4 |
| `agora.parties.resetColor` | `CallBinding<string,string>` | `(partyId)` | `(partyId: string) => Promise<Agora.CommandOutcomeName>` | W4 |

Two tables rather than one with a `Direction` column, unlike §4.1 and §4.5. Those two carry a single
inbound row each, where one extra column is cheaper than a heading. Here the inbound rows are the
majority and share none of the read table's shape: a call has no cadence, no empty value and no TS
*payload* type, and it has an argument list, which a read binding does not. Folded together, six of
eight rows would read `n/a` in three columns and the argument lists would have nowhere to live.

**`rename` carries the short name and `setDescription` carries the slogan** — not an oversight, a
requirement. `NameLocked` covers `Name` **and** `ShortName`, and `DescriptionLocked` covers
`Description` **and** `Slogan` (`PartyOverrides` in `src/Agora.Core/Contracts/Parties.cs`). A rename
that could not also set the short name would take ownership of the short name and freeze it
permanently: flavor is barred from writing it from that moment, and nothing else could. Both fields
of a pair are required and must be non-empty.

**An empty string is rejected with `ValueRequired`; it is never read as "reset".** That is why the
three resets are their own bindings rather than a setter called with `""`. A cleared text box — a
slipped keystroke, a paste that did not take, a focus change mid-edit — is otherwise
indistinguishable from a deliberate hand-back, and the two have opposite consequences. A player who
wants the generated name back has to say so.

**A reset on a field that is not locked is a no-op returning `Ok`.** The resets are idempotent: the
player asked for the state the save is already in, and nothing needed to happen. Do not return
`BadValue`, and do not have the panel suppress the call to avoid one.

`setColor` takes `#RRGGBB` and is normalised to upper case on the C# side, so the value that comes
back on the next roster publish may differ in case from what was sent. A colour another party
already wears is **accepted** with `OkColorInUse` — a warning to render, not a refusal. `resetColor`
clears `ColorLocked` and hands the party back a palette colour.

`PartyBrief` gained `nameLocked`, `descriptionLocked` and `colorLocked` in plan 0001, ahead of W4's
party editing, and `description`/`slogan` in W4. The publisher fills the locks from
`Party.PlayerOverrides`. **W4's party editors are the consumer**; the earlier note that no panel read
them is obsolete.

`description` and `slogan` ride on the roster rather than in a new per-party detail payload because
of the rule below: every other payload identifies a party by `partyId` alone and party metadata is
looked up here. A description editor cannot show the text it is editing if the text is published
nowhere, and adding a second place to look up a party's own fields is exactly the duplication the
rule exists to prevent.

`colorPalette` and `editLimits` are bindings rather than constants in the panel for the same reason,
one step further out. The palette lives in `EngineTuning.Parties.ColorPalette`; a picker that
hard-coded the swatches would drift from the tuning silently the first time it was edited. The limits
are enforced by `PartyIdentity` in C#; a hard-coded character counter and the rejector would be two
copies of one number, and when they disagree the wrong one is the counter — the player finds out by
being refused after typing.

**Every other payload in this contract identifies a party by `partyId` only.** Name, short name and
colour are looked up here. Do not duplicate party metadata into seat rows, district rows or news
items — that is how the colour of one party ends up different in two panels.

Sort: `roster` by `id` ordinal ascending. `factions` by `partyId` ordinal ascending, then
`internalSupport` **descending**, then `id` ordinal ascending. `colorPalette.colors` is in
**tuning order** — the order the engine assigns from — and is never re-sorted; a swatch's position
is how a player recognises it between sessions. `editLimits` is a flat object with no list in it.
`detail.factionIds`: ordinal ascending. `pollTrend`: `date` ascending (oldest first) — a trend reads
left to right in time. This is the one list in the contract that is **not** newest-first, and it is
capped by dropping from the **front**, so the newest `AGORA_POLL_TREND_MAX = 24` points survive.
`electionRecord`: `date` ascending (oldest first), capped at `AGORA_ELECTION_HISTORY_MAX = 12`,
keeping the newest twelve. `detail.absorbedPartyIds`: ordinal ascending. `relations`: formation
order — `hasMajority` first, then `isMinimumWinning`, then `score` descending, then fewer partners,
then the joined member-id key (`CoalitionCandidate.Compare`,
`src/Agora.Core/Engine/Government/Coalitions/CoalitionCandidate.cs`). Capped at
`AGORA_COALITION_OPTIONS_MAX = 8`, keeping the **best** eight, so this cap drops from the back.
`relations[*].memberPartyIds`: ordinal ascending.

### 4.3 `agora.seats` — seat chart + government breakdown

| Binding | Kind | C# type | TS type | Cadence | Empty / loading | Since |
|---|---|---|---|---|---|---|
| `agora.seats.total` | `GetterValueBinding<int>` | `int` | `number` | UI tick (reads a cached field) | `0` | M4 |
| `agora.seats.allocation` | `ValueBinding<T>` | `List<SeatRow>` | `Agora.SeatRow[]` | on election + on monthly reprojection | `[]` | M4 |
| `agora.seats.voteShares` | `ValueBinding<T>` | `List<PartyShare>` | `Agora.PartyShare[]` | monthly | `[]` | M4 |
| `agora.seats.government` | `ValueBinding<T>` | `GovernmentSummary?` | `Agora.GovernmentSummary \| null` | on coalition form / collapse / stability tick | `null` | M4 |
| `agora.seats.mayor` | `ValueBinding<T>` | `MayorSummary?` | `Agora.MayorSummary \| null` | on election + on mayor change | `null` | M4 |
| `agora.seats.lastElection` | `ValueBinding<T>` | `ElectionSummary?` | `Agora.ElectionSummary \| null` | on election | `null` | M4 |
| `agora.seats.latestPoll` | `ValueBinding<T>` | `PollSummary?` | `Agora.PollSummary \| null` | on poll publication | `null` | M4 |
| `agora.seats.history` | `ValueBinding<T>` | `List<ElectionHistoryRow>` | `Agora.ElectionHistoryRow[]` | on election | `[]` | M4 |

Sort keys — contractual:

- `allocation`: `seats` **descending**, then `partyId` ordinal ascending. Ties are broken by id so
  the chart does not reshuffle between updates.
- `voteShares`, and every `PartyShare[]` nested anywhere: `partyId` ordinal ascending. This is the
  engine contract for `List<PartyVoteShare>`; do not re-sort in the panel.
- `history`: `date` **descending**, then `id` ascending. Capped at `AGORA_ELECTION_HISTORY_MAX = 12`.
- `government.memberPartyIds` / `oppositionPartyIds`: ordinal ascending.

**`PollResult.TrueShares` is never published.** It is model truth used to generate the published
number; putting it on the bridge would leak the answer into the UI. `PollSummary.shares` is the
published figure only. A publisher that writes `TrueShares` is a review-blocking defect.

Under FPTP the winning party plus mayor is still modelled as a `Coalition`, so
`agora.seats.government` is populated under both electoral systems and the panel needs one code
path. `listSeats` is `0` for every row under FPTP; `districtSeats` is `0` for every row under a pure
list system. The panel decides what to show from `state.summary.system`, not by sniffing zeroes.

### 4.4 `agora.districts` — vote splits + wealth × education crosstabs

| Binding | Kind | C# type | TS type | Cadence | Empty / loading | Since |
|---|---|---|---|---|---|---|
| `agora.districts.list` | `ValueBinding<T>` | `List<DistrictBrief>` | `Agora.DistrictBrief[]` | monthly + on election | `[]` | M4 |
| `agora.districts.detail` | `GetterMapBinding<string,T>` | `DistrictDetail` per key | `Agora.DistrictDetail` | on demand, per subscribed key | `EMPTY_DISTRICT_DETAIL` | M4 |
| `agora.districts.crosstab` | `GetterMapBinding<string,T>` | `List<CrosstabCell>` per key | `Agora.CrosstabCell[]` | on demand, per subscribed key | `[]` | M4 |
| `agora.districts.cityCrosstab` | `ValueBinding<T>` | `List<CrosstabCell>` | `Agora.CrosstabCell[]` | monthly | `[]` | M4 |
| `agora.districts.cityIndices` | `ValueBinding<T>` | `CityIndices` | `Agora.CityIndices` | monthly | `EMPTY_CITY_INDICES` | M4 |

The map key is the **district id** exactly as it appears in `DistrictBrief.id`. An unknown key
returns the empty value, never throws — a district can be deleted by the player while its detail
panel is open.

Sort keys:

- `list`: `id` ordinal ascending (matches `CitySnapshot.Districts`).
- `crosstab` / `cityCrosstab`: `wealth` ascending (`Low`, `Middle`, `High`) then `education`
  ascending (`Uneducated`, `PoorlyEducated`, `Educated`, `WellEducated`, `HighlyEducated`). Always
  exactly 15 cells, in that order, even when a cell has zero population — the panel renders a fixed
  3 × 5 grid and must not have to handle holes.
- `detail.shares`: `partyId` ordinal ascending.
- `detail.cityFallbackFields`: property name ascending.

**Crosstab cells collapse the age axis.** The engine models 60 blocs (3 wealth × 5 education × 4
age); this binding sums the four age bands so 15 rows cross the bridge instead of 60.

**`CrosstabCell.turnout` is not a per-cell figure.** It is the **district-wide** realised turnout —
city-wide for `cityCrosstab` — written unchanged into **every one of the 15 cells**. The only
variation is that a cell with no eligible voters carries `0`. Do not render it as if it varied by
cell: a per-cell turnout heatmap built on this value is a flat fill telling the player something
untrue. Per-bloc turnout is computed each tick but is not persisted on `Bloc`, so the publisher
cannot reach it; publishing the district figure is honest about the granularity available, where
interpolating a per-cell rate would invent one. The reasoning is on the `BuildCrosstab` `<remarks>`
in `src/Agora.Mod/UiBindings/AgoraUiProjection.cs`. Closing the gap means adding a field to `Bloc`,
which is a contract change and goes through `/schema-change`.

**`hasCityFallbacks` is a rendering obligation, not decoration.** When it is true, every field named
in `cityFallbackFields` is a city number wearing a district's name. The panel must mark those fields
visually (dimmed + a tooltip) and must never present them as a local fact. This is
`politicsmodplan.md` §6, and the reviewer checks it.

#### `DistrictDetail.budget` — the household ledger (M4, extended)

A named group on `agora.districts.detail`, alongside `wealth` / `education` / `age` / `indices` and
under the same one-level nesting limit:

| Property | Meaning | Units |
|---|---|---|
| `averageRent` | mean rent charged | currency, per rent period (30 days) |
| `rentBurden` | rent as a share of income over that period | share, 0–1+ |
| `averageHouseholdUpkeep` | mean spend keeping the home standing | currency **per day** |
| `averageHouseholdResourceSpend` | mean spend on goods | currency **per day** |
| `averageHouseholdFees` | mean utility bill at the player's own fee rates | currency **per day** |
| `disposableMargin` | what is left of a day's income after all four | share, **signed and uncapped** |

Mirrors `CitySnapshot` v3 one-for-one; the C# publisher copies `DistrictSnapshot` without deriving
anything, so the panel and the engine cannot come to disagree about a household's budget.

**The fallback field names are the snapshot's property names, camelCased** — `averageRent`,
`rentBurden`, `averageHouseholdUpkeep`, `averageHouseholdResourceSpend`, `averageHouseholdFees`,
`disposableMargin`. That is not a coincidence to be tidied: `cityFallbackFields` carries the C#
property names, and `makeFallbackSet` matches them case-insensitively against the `field` prop each
cell passes. Renaming a payload property away from its snapshot property silently stops that cell
being dimmed, with nothing failing anywhere.

**`disposableMargin` is signed and may exceed 1.** A meter fill must clamp it; the *label* must not.
A district at −0.12 is drawing down savings, and rendering that as an empty bar reading "0%" would be
the panel inventing a floor the engine deliberately refused to impose.

**Mixed periods are contractual, not an oversight.** Rent is billed per rent period and the other
three per day, exactly as the game bills them. The panel labels the periods rather than converting,
because a converted figure would not match the number the player sees in the game's own district
panel — which is the whole point of showing it.

### 4.5 `agora.news` — mandate tracker + the political-event alert queue

**The feed is gone (v10).** `agora.news.feed` and `agora.news.events` were removed with
`ui/src/panels/News/**`; the story system replaced what they were for. The group keeps its name
because renaming a live binding in place is what §7 forbids, and six consumers still read it — see
the version-10 note at the top of this file for the full reasoning on each survivor.

| Binding | Kind | Direction | C# type | TS type | Cadence | Empty / loading | Since |
|---|---|---|---|---|---|---|---|
| `agora.news.mandates` | `ValueBinding<T>` | C# → UI | `List<MandateRow>` | `Agora.MandateRow[]` | monthly | `[]` | M4 |
| `agora.news.article` | `GetterMapBinding<string,T>` | C# → UI | `NewsArticle` per key | `Agora.NewsArticle` | on demand, per subscribed key | `EMPTY_NEWS_ARTICLE` | M4 |
| `agora.news.flavorStatus` | `ValueBinding<T>` | C# → UI | `FlavorStatus` | `Agora.FlavorStatus` | on every flavor attempt, success or failure | `EMPTY_FLAVOR_STATUS` | M4 |
| `agora.news.wakeFlavor` | `TriggerBinding` | **UI → C#** | — | `() => void` | on click | n/a | M4 |
| `agora.news.alerts` | `ValueBinding<T>` | C# → UI | `List<NewsAlert>` | `Agora.NewsAlert[]` | on raise and on ack | `[]` | W5 |
| `agora.news.ackAlert` | `CallBinding<string,string>` | **UI → C#** | alert id or `"*"` | `(id) => Promise<CommandOutcomeName>` | on dismiss | n/a | W5 |

**`agora.news.mandates` row:** also consumed by the Parties tab, filtered by `partyId`, as a per-party
scorecard (fixplan W6 addition 6). No per-party binding exists or should be added: it would publish
the same rows twice.

Sort keys:

- `mandates`: **status rank** ascending — `Active` 0, `Pending` 1, `PartiallyFulfilled` 2,
  `Fulfilled` 3, `Defied` 4, `Abandoned` 5 — then `deadlineDate` ascending, then `id` ordinal
  ascending. So the tracker opens on what is live and closest to its deadline.
- `alerts`: **not sorted.** Emission order, oldest first — the order the alerts happened in, which is
  the order the player is asked to answer them in. A view that re-sorted it would change which card
  comes up first, which is §7 rule 7's territory. Bounded by the engine, which drops the oldest and
  logs when it does.

`agora.news.wakeFlavor` is the manual LLM wake from `politicsmodplan.md` §2. It **requests**; the
engine decides. The panel must disable the control while `flavorStatus.pendingWake` is true and must
not assume the feed changes as a result — a failed wake keeps the last good flavor by design
(non-negotiable #7), and the only visible consequence may be `flavorStatus.lastError`.

`flavorStatus.lastError` is an **engine-authored** short code, never LLM output and never a raw
exception message: `""`, `"CliMissing"`, `"Timeout"`, `"BadJson"`, `"Disabled"`, `"Unknown"`.

**`agora.news.alerts` is the political-event alert queue, and as of v10 that is all it is.** It
carries elections, coalition formation and collapse, and party founding and dissolution — the four
things in this mod that happen *to* the player rather than being decided by them, and that nothing
else announces. It no longer carries **article alerts** (there is no general monthly prose to
interrupt over) and it never carried stories, which have their own lane and their own modal for the
three reasons §4.7 gives.

**A body is still fetched from `agora.news.article` under the alert's own `id`, and still only when
`hasArticle` is true.** The map answers an id it does not know with `EMPTY_NEWS_ARTICLE` rather than
throwing, so a fetch for an alert the writer produced no prose for renders a blank masthead instead
of failing loudly. **Branch on `hasArticle`, never on `kind`** — unchanged since W5, and it matters
more in v10 than before, because the writer now covers four kinds rather than every item and
`hasArticle` is the only honest answer to "is there a body".

What changed underneath is which ids can be present: the feed is gone, so an id is no longer "a feed
row's id" but simply the alert's own. Nothing a consumer does had to change.

`alerts[].major` is the **engine's** verdict on whether an item is grave enough to hold the clock,
decided once when the alert is raised. The UI must never compare `severity` to a threshold of its
own: the number lives in `EngineTuning` and a copy of it in a panel is a second definition of "major"
that drifts on the next tuning pass (§7 rule 5). Whether the clock is actually held is the separate
question `settings.pauseOnMajorNews` answers **for this lane**. The story lane asks
`settings.pauseOnMajorStory` instead, and the two must never be crossed: each control's hint text
names the categories it governs, and reading the other one enforces a choice the player made about
something else.

**Every alert `id` is prefixed, and as of v10 there is no longer an exception.** `event:`,
`election:`, `coalition:` (an ending), `coalition:…:formed`, `party:…:founded` / `:dissolved`.

The exception that existed until v10 is worth recording, because it is the shape of a whole class of
bug this repo keeps meeting: an **article alert carried the bare article id, unprefixed**, because
that same string doubled as the `agora.news.article` map key. Prefixing it broke the fetch
**silently** — `BuildArticle` answered an unknown key with an empty payload rather than throwing, so
the player saw a blank masthead and nothing was logged. Both the article alert and the map are gone,
so the asymmetry is gone with them. **Do not reintroduce an id that doubles as a lookup key** without
reading this paragraph first.

`agora.news.ackAlert` takes the alert's id, or the sentinel `"*"` for dismiss-all, and answers a
`CommandOutcomeName` like every other inbound call. Acking an id the engine no longer holds is
accepted (`""`), **not** `NotFound`: a double-click, or a dismiss racing a republish, is not
something the player did wrong. **The one rejection is an empty or null id, which answers
`BadValue`** (`AgoraRuntime.AckAlert`) — that is a caller bug, not a stale id, and the two are
deliberately distinguished. The panel must send it with a deadline — while a major alert is up
the game forces the speed to zero every frame, so a call that never answers leaves a card that
cannot be closed and a clock that cannot be started.

A mandate with `isMeasurementStalled === true` is **held, not failing**. Render it as paused; do not
render its progress bar as falling behind, and never show it as `Defied` because the clock ran out
while its metric was unreadable.

### 4.7 `agora.stories` — the stories, the power currency, and the five commands

| Binding | Kind | Direction | C# | TS | Cadence | Empty | Since |
|---|---|---|---|---|---|---|---|
| `agora.stories.live` | `ValueBinding<T>` | C# → UI | `List<StoryPayload>` | `Agora.Story[]` | on `StateVersion` — draft, resolve, and every accepted command | `[]` | W6 |
| `agora.stories.archive` | `ValueBinding<T>` | C# → UI | `List<StoryBriefPayload>` | `Agora.StoryBrief[]` | on `StateVersion` | `[]` | W6 |
| `agora.stories.article` | `GetterMapBinding<string,T>` | C# → UI | `StoryArticlePayload` per key | `Agora.StoryArticle` | on demand, per subscribed key | `EMPTY_STORY_ARTICLE` | W6 |
| `agora.stories.power` | `ValueBinding<T>` | C# → UI | `PowerPayload` | `Agora.Power` | on `StateVersion` | `EMPTY_POWER` | W6 |
| `agora.stories.alerts` | `ValueBinding<T>` | C# → UI | `List<StoryAlertPayload>` | `Agora.StoryAlert[]` | on raise and on ack | `[]` | W6 |
| `agora.stories.setResponse` | `CallBinding<string,string,string,string,string>` | **UI → C#** | `(storyId, eventId, mode, text) => CommandOutcome` | `(storyId, eventId, mode: SlotResponseName, text: string) => Promise<CommandOutcomeName>` | on click | n/a | W6 |
| `agora.stories.declareManual` | `CallBinding<string,string,bool,string,string>` | **UI → C#** | `(storyId, eventId, met, text) => CommandOutcome` | `(storyId, eventId, met: boolean, text: string) => Promise<CommandOutcomeName>` | on click | n/a | W6 |
| `agora.stories.resolveNow` | `CallBinding<string,string>` | **UI → C#** | `(storyId) => CommandOutcome` | `(storyId: string) => Promise<CommandOutcomeName>` | on click | n/a | W6 |
| `agora.stories.spendPowerOverride` | `CallBinding<string,string,string>` | **UI → C#** | `(storyId, eventId) => CommandOutcome` | `(storyId, eventId) => Promise<CommandOutcomeName>` | on click | n/a | W6 |
| `agora.stories.ackAlert` | `CallBinding<string,string>` | **UI → C#** | story id or `"*"` | `(id: string) => Promise<CommandOutcomeName>` | on dismiss | n/a | W6 |

**Sort keys.** `live` by `id` ordinal ascending. A story's `slots` **major first, then minors
ascending by `eventId` ordinal** — a declared total order the engine writes and the panel must not
re-sort. `archive` by `(resolvedMonth` descending`, id)`. `power.ledger` by `(month, sequence)`,
newest last. `alerts` **oldest first**, the order the player answers them in.

**Payload caps.** `archive` at 24 rows; `power.ledger` at 24 rows, keeping the newest;
`live` is bounded by the engine at `storiesPerCycle` plus mandatory stories, not by this contract.
Bodies are never on `live` — a story carries two articles in up to two voices at 1260 characters
each, so they are fetched per story from `article`, the same split the news feed makes.

**Five inbound bindings, not the three the rework plan's §605 table lists.** The plan assumed a
purchase would travel as an ordinary response through `setResponse`. Wave 4 refuses that: a
`PowerOverride` arriving as a response would be a free `Met` nobody paid for, so the purchase has its
own channel that charges for it. The fifth is the card dismissal. `setResponse` answers `BadValue` if
asked for `"PowerOverride"`, and it is the panel's job to route the button to the right call rather
than to the one whose name looks closest.

**A tier is the engine's verdict and the UI never derives one.** `slot.tier` is Mandatory / Major /
Minor, projected from the 1–5 `slot.severity` through `stories.mandatorySeverityThreshold` and
`stories.majorSeverityThreshold`. `severity` ships alongside for display only. This is the same rule
§4.5 states in bold for news, and the reason is the same: a fourth vocabulary drifts on the next
tuning pass, and here it would drift into disagreeing with the price the engine charges.

**`overrideCost` and `canAfford` are what the button LOOKS like, never whether the purchase
happens.** With the power layer off both are published as `0` / `false` — the card must not quote a
live price against a balance that cannot move, or a player will save up for something the engine
refuses with `PowerDisabled` whatever they do. Whether a purchase goes through is
`spendPowerOverride`'s answer, read at the moment of the press; a panel that checks affordability
itself and declines to send is computing a rejection the engine did not return (rule 5).

**`"Unaddressed"` is silence and `"Ignore"` is a decision.** They score identically — both resolve
not-met — and they read completely differently in the prose and in the command log. The panel must be
able to show that a slot has not been answered; collapsing the two loses the only signal that the
player has work outstanding.

**A slot outcome of `"Unmeasurable"` is held, not failing** — the same rule as a stalled mandate,
one paragraph up. It means the engine could not read the city, it is excluded from both halves of the
success ratio, and it costs the player nothing. Never render it as a failure, and never render it as
"the player did not respond", which is `"NotMet"`.

**A story's prose has two voices and both render when both exist.** The canned pool answers every
poll and always has an answer; the CLI answers rarely. Showing only the newest would erase the
model's prose within a minute of it arriving and — worse — would change text the player had already
read. The pool half is always shown; the CLI half appears **beside** it, never instead of it. This is
an owner decision from wave 5, not an implementation detail.

**Story cards are their own queue and their own lane, deliberately not `agora.news.alerts`.** Three
reasons, all from this contract: every news alert `id` is a feed row's id whose body is fetched from
`agora.news.article` under that same id, and a story id is neither — `BuildArticle` answers an unknown
key with an empty payload rather than throwing, so the failure would be a blank masthead with nothing
logged; `ArticleModal` renders `alerts[0]` or nothing and holds the pause barrier while it is up, so
two lanes sharing it would serialise; and the news queue drops its oldest when full, which on that
lane is a missed headline and on this one would be **a decision the player never got to make**.

**One card per story, never one per event.** All of a story's slots render inside the one card, which
is why `StoryAlert` carries a `slotCount` and no event id. At the shipped cadence that is two
interruptions per cycle rather than six. **Dismissing a card answers nothing** — it closes the
interruption; the story stays live and is answered from the Stories panel.

`alert.major` is the engine's verdict on whether the card holds the clock, decided once when the
alert is raised from the story's own major slot against the tuned threshold. The UI never recomputes
it, for the same reason it never derives a tier.

**Every one of the five commands must be sent with a deadline.** A story card may hold the pause
barrier, and while it is held the game forces the speed to zero every frame — so a call that never
answers leaves a player with a card they cannot close and a clock they cannot start. `ui/src/shell/
bindings.ts` wraps all five in `withDeadline`; use those wrappers rather than calling `call` directly.

---

### 4.6 Outcome codes — the closed set

Every inbound `CallBinding` in this contract returns one of these, and only these. Mirrors
`Agora.Core.Contracts.CommandOutcome`; the TypeScript union is `Agora.CommandOutcomeName`. A new
reason is added to the **C# enum first**, then here, then to the `.d.ts` — W4's party editors
extended this set rather than starting a parallel one, adding the last four rows below.

| Wire | Meaning |
|---|---|
| `""` | Accepted, or already true — nothing needed to happen. `Ok` crosses as the empty string, not as `"Ok"`. |
| `NoActiveSave` | No save is loaded, or the political layer never came up for this one. |
| `UnknownKey` | This build does not recognise the setting or field name. |
| `BadValue` | The name was recognised; the value was not legal for it. |
| `ThemeLocked` | The save has held an election; the region theme is history. |
| `Busy` | Something the request would tear down is in flight. Retry shortly. |
| `Failed` | It failed for a reason the player cannot act on. The detail is in `Agora.log`. |
| `NotFound` | No party in this save carries that id. Unlike `UnknownKey`, the *field* was fine — the *subject* was not. |
| `ValueRequired` | The field was left empty. **Empty is never a reset**; where a reset exists it is its own binding (§4.2). Now reached from two surfaces — a cleared party name, and a self-declared story success with no justification (§4.7) — so its player-facing sentence must not name a control that exists on only one of them. |
| `TooLong` | Over the limit published by `agora.parties.editLimits`. Separate from `BadValue` so the counter and the rejector say the same thing. |
| `OkColorInUse` | **Accepted, with a warning.** The colour was applied; another party already wears it. |
| `InsufficientPower` | The balance does not cover this override's cost. A statement about *now*, not about the save. |
| `AlreadyResolved` | The story exists; its window has closed. Unlike `NotFound`, the *record* was found — the *moment* had passed. |
| `PowerDisabled` | This save runs with the political-power layer switched off. Nothing can be bought off, ever, here. |

**`InsufficientPower` and `PowerDisabled` must not be collapsed.** One says "not yet", the other says
"not in this save". Telling a player to save up for a purchase the engine will never permit is worse
than saying nothing at all, and it is the exact confusion the two separate codes exist to prevent.
The same distinction holds between `AlreadyResolved` and `NotFound` for the identical reason §4.6
already gives for `NotFound` versus `UnknownKey`: the field was fine, the subject was not.

**Two of these are acceptances: `""` and `OkColorInUse`. Everything else is a rejection.** Test with
`CommandOutcomes.IsAccepted` on the C# side, and with the same two-value check on the panel's —
**never with `result === ""`**. An empty-string test reads the accepted-with-warning case as a
failure, so the panel would revert the swatch to the old colour while the engine kept the new one,
and the two would disagree until the next republish. `OkColorInUse` deliberately does *not* cross as
`""`: the empty string means "nothing to tell the player", and here there is something to tell them.

**Engine-authored, always** — never an exception message, never `ex.Message`, never model output.
Same rule and same reason as `flavorStatus.lastError` (§4.5): the panel switches on the value, and a
string that varies with the machine cannot be switched on.

---

## 5. Payload shapes

Authoritative declarations are in `ui/types/bindings.d.ts` under `declare namespace Agora`. They are
global — panels reference `Agora.SeatRow` with **no import statement**. Do not `import` from a
module path; there is no runtime module behind these types and `isolatedModules` would turn the
import into a webpack resolution error.

Summarised here so a C# publisher author does not have to read TypeScript:

```
StateSummary        schemaVersion, date, termNumber, system, theme, nextElectionDate,
                    isCampaignSeason, weeksToElection, mayorPartyId
SettingsPayload     schemaVersion, startYear, theme, system, themeLocked, pauseOnMajorNews,
                    showAllReports, effectsEnabled, voteSharpness, newsInfluence,
                    brandDiscipline, voteSharpnessValue, newsInfluenceValue,
                    brandDisciplineValue, storiesEnabled, storiesPerCycle, eventsPerStory,
                    politicalPowerEnabled, powerIntensity, storyDifficulty,
                    pauseOnMajorStory
PartyBrief          id, name, shortName, description, slogan, colorHex, status, isIncumbent,
                    isInGovernment, coreGrievance, foundedDate, dissolvedDate, nameLocked,
                    descriptionLocked, colorLocked
PartyPalette        colors  — "#RRGGBB", UPPERCASE, in EngineTuning.Parties.ColorPalette order
PartyEditLimits     nameMax, shortNameMax, descriptionMax, sloganMax, colorPattern
IssuePositionView   services, costOfLiving, environment, transit, growth, heritageOrder
PartyDetail         id, name, shortName, colorHex, archetypeId, description, slogan,
                    platform{…IssuePositionView}, lastManifesto{…IssuePositionView}, seats,
                    seatShare, lastVoteShare, hasContestedElection, passedThreshold,
                    consecutiveElectionsBelowThreshold, currentPollShare, hasPoll, pollDate,
                    pollDeltaSinceElection, currentStandingShare, status, foundedDate,
                    dissolvedDate, predecessorPartyId, successorPartyId, revivalCount,
                    absorbedPartyIds, governmentRole, factionIds
PollTrendPoint      date, share, marginOfError, weeksToElection
PartyElectionRow    electionId, date, termNumber, isSnapElection, seats, seatShare, voteShare,
                    passedThreshold, wasOnBallot, hasSeatRecord
CoalitionOption     memberPartyIds, leadPartyId, seats, seatShare, hasMajority, meanDistance,
                    maxDistance, distanceCap, cohesion, score, isMinimumWinning, isGrandCoalition,
                    isCurrentGovernment
FactionBrief        id, partyId, name, shortName, leaderName, internalSupport, isDominant,
                    tensionWithParty, status, coreGrievance
PartyShare          partyId, share
SeatRow             partyId, seats, seatShare, voteShare, districtSeats, listSeats, passedThreshold
GovernmentSummary   id, status, leadPartyId, memberPartyIds, oppositionPartyIds, seats, seatShare,
                    hasMajority, cohesion, stability, collapseReason, formedDate, endedDate,
                    formationAttempts, electionId, mandateIds
MayorSummary        partyId, name, electionId, sinceDate, margin, voteShares
ElectionSummary     id, date, system, termNumber, isSnapElection, turnout, totalSeats,
                    totalVotesCast, totalEligibleVoters, finalPollDeviation, nextElectionDate,
                    cityVoteShares
ElectionHistoryRow  id, date, termNumber, isSnapElection, turnout, winningPartyId, mayorPartyId,
                    totalSeats
PollSummary         id, date, pollsterId, pollsterName, sampleSize, marginOfError, undecidedShare,
                    projectedTurnout, weeksToElection, electionDate, shares
DistrictBrief       id, name, population, eligibleVoters, leadingPartyId, leadingShare,
                    runnerUpPartyId, margin, turnout, happiness, discontent, hasCityFallbacks
DistrictDetail      id, name, population, households, eligibleVoters, votesCast, turnout, happiness,
                    unemployment, winningPartyId, margin, seats, decidedByTieBreak, shares,
                    wealth{low,middle,high}, education{uneducated,poorlyEducated,educated,
                    wellEducated,highlyEducated}, age{child,teen,adult,elderly},
                    indices{gentrification,commuteMisery,serviceCoverage,discontent,gini},
                    hasCityFallbacks, cityFallbackFields
CrosstabCell        wealth, education, population, populationShare, eligibleVoters, turnout,
                    leadingPartyId, leadingShare, happiness, discontent
CityIndices         gini, brainDrain, serviceInequality, commuteMisery, polarization, legitimacy,
                    discontent
NewsAlert           id, kind, date, headline, summary, outletName, partyId, districtId, eventId,
                    severity, major, hasArticle
NewsArticle         id, date, headline, body, tone, outletId, outletName, partyId,
                    districtId, eventId
MandateRow          id, partyId, coalitionId, districtId, issue, metric, direction, baselineValue,
                    targetValue, currentValue, progress, issuedDate, deadlineDate, resolvedDate,
                    status, salience, text, isMeasurementStalled, monthsRemaining
FlavorStatus        lastFlavorDate, lastAttemptDate, isStale, providerAvailable, pendingWake,
                    lastError, articleCount
```

The two W4 payloads cross as `agora.PartyPalette` and `agora.PartyEditLimits` (the writer's
`TypeBegin` name, matching `agora.PartyBrief`). `PartyEditLimits` carries the limits `PartyIdentity`
enforces in `src/Agora.Core/Engine/Parties/PartyIdentity.cs` — at time of writing `nameMax` 80,
`shortNameMax` 12, `descriptionMax` 600, `sloganMax` 120, `colorPattern` `^#[0-9A-Fa-f]{6}$`. Those
numbers are the payload's *values*, not the contract: read them from the binding, do not copy them
into a panel.

### Which fields are flavor

Three ownerships, not two, and the difference decides what the UI may offer to do with a field.

**Flavor-owned** — `PartyBrief.name`/`shortName`/`description`/`slogan`,
`PartyDetail.name`/`shortName`/`description`/`slogan`,
`FactionBrief.name`/`shortName`/`leaderName`, `MayorSummary.name`, `PollSummary.pollsterName`,
`NewsAlert.headline`/`summary`/`outletName`, `NewsArticle.*` prose and `MandateRow.text`. Render
them; never parse them, never sort by
them, never derive a number from them.

**Engine-owned** — everything else, including **`PartyBrief.colorHex`**. Colour has never been
flavor-owned and cannot become so: `PartyFlavor` in `src/Agora.Core/Contracts/Boundary.cs` has no
colour field, so there is nothing for a model to write. It is assigned from the tuned palette
(`agora.parties.colorPalette`) in order, so a party keeps its colour across reloads.

**Player-owned, where the player has taken over.** A `PartyBrief` field whose lock is set is the
player's, whatever it was before — `nameLocked` for `name` **and** `shortName`, `descriptionLocked`
for `description` **and** `slogan`, `colorLocked` for `colorHex`. Flavor output for a locked field is
discarded, not merged. The UI must not offer to regenerate a locked field without offering "reset to
generated" (`agora.parties.resetName` / `resetDescription` / `resetColor`), and must never present it
as LLM output. A player who names their party and then sees it described as generated text has been
told the mod is going to overwrite it.

---

## 6. Empty / loading values

Copy these literally. `bindValue`'s third argument is the value rendered before C# publishes
anything; omit it and the panel renders `undefined` on the first frame. Each panel declares the
constants it needs module-locally — there is no shared runtime module **between panels**, by design,
because panels are authored in parallel.

**`shell/` is the one exception, and the rule is about what kind of thing is being shared.** Panels
have always depended on the shell for design tokens (`@use "../../shell/tokens"`, W1); W4 lane D
extended that to TypeScript, importing `isAccepted` / `writeMessage` from `ui/src/shell/bindings.ts`
rather than copying them. The distinction to hold:

- **Presentation helpers** — a percentage formatter, a label fallback — may be copied panel to panel.
  Two copies that drift produce two slightly different renderings of the same number, which is
  cosmetic.
- **A rule that decides whether a write took must never be copied.** `CommandOutcome.OkColorInUse`
  is an **accepted** write that carries a warning, so "has a message" is not "was rejected". A panel
  holding its own copy of that test, drifting from the shell's, rolls its control back to the old
  value while the engine keeps the new one, and the two stay disagreed until the next republish.
  Import it. Do not reimplement it, and do not infer acceptance from the message being non-empty.

```tsx
const EMPTY_STATE_SUMMARY: Agora.StateSummary = {
  schemaVersion: 0, date: "", termNumber: 0, system: "Proportional", theme: "Eu",
  nextElectionDate: "", isCampaignSeason: false, weeksToElection: -1, mayorPartyId: "",
};

const EMPTY_SETTINGS: Agora.SettingsPayload = {
  schemaVersion: 0, startYear: 1990, theme: "Eu", system: "Proportional",
  themeLocked: false, pauseOnMajorNews: true, showAllReports: false, effectsEnabled: true,
  voteSharpness: "Default", newsInfluence: "Default", brandDiscipline: "Default",
  voteSharpnessValue: 0, newsInfluenceValue: 0, brandDisciplineValue: 0,
  storiesEnabled: true, storiesPerCycle: 2, eventsPerStory: 3,
  politicalPowerEnabled: true, powerIntensity: "Default", storyDifficulty: "Default",
  pauseOnMajorStory: true,
};

// `enabled: false` is the one field here worth arguing about, and it is deliberate. Before the
// engine publishes we do not know whether this save runs the power layer, and the counter's rule is
// "hide when off" — so a `true` here would flash a balance of 0 on every load of a save that has the
// layer switched off, which reads as "you have no power" rather than "there is no such currency".
const EMPTY_POWER: Agora.Power = {
  enabled: false, balance: 0, lifetimeEarned: 0, lifetimeSpent: 0, inDebt: false, ledger: [],
};

const EMPTY_STORY_ARTICLE: Agora.StoryArticle = {
  storyId: "", poolHeadline: "", poolArticle: "", cliHeadline: "", cliArticle: "",
  poolResolutionHeadline: "", poolResolutionArticle: "",
  cliResolutionHeadline: "", cliResolutionArticle: "",
};

// Not wired into a binding — `agora.stories.alerts` is an array and takes `[]`. This is the guard a
// card substitutes for a queue index the engine no longer holds, so a render racing an ack cannot
// read a field off `undefined`. `major` is false: an empty card must never take the pause barrier.
const EMPTY_STORY_ALERT: Agora.StoryAlert = {
  id: "", date: "", headline: "", summary: "", slotCount: 0, major: false,
};

const EMPTY_PARTY_PALETTE: Agora.PartyPalette = { colors: [] };

const EMPTY_PARTY_EDIT_LIMITS: Agora.PartyEditLimits = {
  nameMax: 0, shortNameMax: 0, descriptionMax: 0, sloganMax: 0, colorPattern: "",
};

const EMPTY_PARTY_DETAIL: Agora.PartyDetail = {
  id: "", name: "", shortName: "", colorHex: "#808080", archetypeId: "", description: "",
  slogan: "",
  platform: {
    services: 0, costOfLiving: 0, environment: 0, transit: 0, growth: 0, heritageOrder: 0,
  },
  lastManifesto: {
    services: 0, costOfLiving: 0, environment: 0, transit: 0, growth: 0, heritageOrder: 0,
  },
  seats: 0, seatShare: 0, lastVoteShare: 0, hasContestedElection: false, passedThreshold: false,
  consecutiveElectionsBelowThreshold: 0, currentPollShare: 0, hasPoll: false, pollDate: "",
  pollDeltaSinceElection: 0, currentStandingShare: 0, status: "Active", foundedDate: "",
  dissolvedDate: "", predecessorPartyId: "", successorPartyId: "", revivalCount: 0,
  absorbedPartyIds: [], governmentRole: "None", factionIds: [],
};

const EMPTY_CITY_INDICES: Agora.CityIndices = {
  gini: 0, brainDrain: 0, serviceInequality: 0, commuteMisery: 0,
  polarization: 0, legitimacy: 0, discontent: 0,
};

const EMPTY_DISTRICT_DETAIL: Agora.DistrictDetail = {
  id: "", name: "", population: 0, households: 0, eligibleVoters: 0, votesCast: 0,
  turnout: 0, happiness: 0, unemployment: 0, winningPartyId: "", margin: 0, seats: 0,
  decidedByTieBreak: false, shares: [],
  wealth: { low: 0, middle: 0, high: 0 },
  education: { uneducated: 0, poorlyEducated: 0, educated: 0, wellEducated: 0, highlyEducated: 0 },
  age: { child: 0, teen: 0, adult: 0, elderly: 0 },
  indices: { gentrification: 0, commuteMisery: 0, serviceCoverage: 0, discontent: 0, gini: 0 },
  hasCityFallbacks: false, cityFallbackFields: [],
};

const EMPTY_NEWS_ARTICLE: Agora.NewsArticle = {
  id: "", date: "", headline: "", body: "", tone: "", outletId: "", outletName: "",
  partyId: "", districtId: "", eventId: "",
};

const EMPTY_NEWS_ALERT: Agora.NewsAlert = {
  id: "", kind: "Article", date: "", headline: "", summary: "", outletName: "",
  partyId: "", districtId: "", eventId: "", severity: 0, major: false, hasArticle: false,
};

const EMPTY_FLAVOR_STATUS: Agora.FlavorStatus = {
  lastFlavorDate: "", lastAttemptDate: "", isStale: false, providerAvailable: false,
  pendingWake: false, lastError: "", articleCount: 0,
};
```

Array bindings take `[]`. `agora.seats.government`, `agora.seats.mayor`, `agora.seats.lastElection`
and `agora.seats.latestPoll` take `null`.

**Map bindings are the exception: `bindMap` takes no fallback argument**, and
`useMapValue(binding, key)` cannot return `undefined` for a key the panel has asked for. So
`EMPTY_DISTRICT_DETAIL`, `EMPTY_NEWS_ARTICLE`, `EMPTY_STORY_ARTICLE` and `EMPTY_PARTY_DETAIL` are
not wired into a binding
declaration — they are module-local guard constants the panel substitutes for whatever `useMapValue`
hands back, `const detail = raw || EMPTY_PARTY_DETAIL`, covering the frame before the getter has run
and any nested group the payload did not write. The C# side answers an unknown key with its own
empty payload, so the two must agree field for field; the literal above is why this section exists.
`EMPTY_PARTY_DETAIL.colorHex` is `"#808080"` rather than `""` for that reason — it matches the C#
initialiser, and a swatch with no colour renders as a hole rather than as grey.

`weeksToElection` is `-1` — not `0` — when no election is scheduled, so "the election is this week"
stays distinguishable from "there is no election".

`EMPTY_NEWS_ALERT` is a guard of the same kind, for a different reason: `agora.news.alerts` is an
array binding and takes `[]`, so this is what a card substitutes for an index the queue no longer
holds — the frame between a dismiss and the republish that removes it. Its `major` is `false`
deliberately. An empty alert must never be the thing that takes the pause barrier.

`EMPTY_PARTY_EDIT_LIMITS` is all zeroes, which is not a usable limit: a counter reading `nameMax: 0`
would declare every keystroke too long. Gate the editors on `state.ready` (§4.1) like every other
panel, rather than treating the empty value as a real ceiling. The same is true of an empty palette —
render no swatches, not a picker with nothing in it.

---

## 7. Rules

1. **Register here first, implement second.** A binding not in this table does not exist.
2. **Never rename in place.** Add the new name, migrate the consumer, then remove the old one in a
   later change. Renaming both sides in one commit works locally and breaks anyone mid-update.
3. **Never consume an unpublished binding.** `useValue` on a binding the C# side has not registered
   returns the fallback at best and throws at worst. Nothing from §8 *Reserved* may be consumed.
   Always pass the fallback argument, without exception.
4. **Complex payloads implement `IJsonWritable`.** Do not hand-serialize to a JSON string and parse
   it on the JS side — that defeats the binding layer's change tracking.
5. **Bindings are a view, never a channel for engine state.** The UI reads politics; it does not
   compute or mutate it. No panel recomputes a share, a seat count or an index — if a number is
   needed, it is published.
   **A write binding does not weaken this, because a call *requests*.** The engine validates,
   decides, and says so in the return code (§4.6). Nothing the panel sends is state until the engine
   has made it state, and **no panel may compute a rejection the C# side did not return** — greying a
   control out because the panel believes the theme is locked is the same defect as recomputing a
   seat count, and it will be wrong on the tick the two disagree. Disable a control from a *published*
   value (`settings.themeLocked`); report a refusal from the *returned* code.
6. **Update cost matters.** `GetterValueBinding` re-evaluates on the UI update tick — keep getters
   cheap, and never run an `EntityQuery` inside one. Cache in a simulation system, expose the cached
   field. Only scalars are getters; every payload is a pushed `ValueBinding` or an on-demand map.
7. **Honour the documented sort key.** Sorting is done once, in C#, against a stable key. A panel
   that re-sorts by a flavor string reintroduces the ordering nondeterminism the engine spent effort
   removing.
8. **Never publish model-internal truth.** `PollResult.TrueShares`, seed values, RNG state, raw
   snapshot history and unclamped intermediate scores stay on the C# side.
9. **`hasCityFallbacks` must change the rendering.** See §4.4.
10. **Cadence is per change, not per frame.** A `ValueBinding.Update` call with an unchanged payload
    still costs a bridge crossing on some versions — publish on the engine's monthly/election tick,
    not from `OnUpdate`.

---

## 8. Reserved — registered as names, **not yet published, do not consume**

Listed so a later milestone does not pick a colliding name. A panel that binds one of these today
gets an empty panel and no error.

| Binding | Intended for | Milestone |
|---|---|---|
| `agora.seats.pollTrend` | city-wide multi-party trend chart of published poll shares over time | M6 |
| `agora.districts.overlay` | political map overlay tint data per district | M6 |
| `agora.districts.blocs` | the full 60-bloc breakdown behind a crosstab cell | M6 |
| `agora.news.archive` | paged news archive beyond the 40-item feed | M6 |
| `agora.news.markRead` | UI → C# read-state persistence | M6 |

**`agora.news.markRead` stays reserved too, and W5 deliberately did not take it.** The alert ack is
`agora.news.ackAlert` (§4.5), a new name, because the two mean different things: `markRead` is
read-state *persistence*, and the alert queue is session state that is never written to disk.
Repurposing a reserved name for something narrower is worse than adding one — the next reader would
find `markRead` published and conclude read-state survives a reload, which it does not.

**`agora.seats.pollTrend` stays reserved, on purpose.** W6 needed a poll trend and did **not** take
this name: the Parties tab draws one party's series, and a city-wide series of per-party shares is a
list of rows containing lists, which §2 forbids. It published the party-scoped map
`agora.parties.pollTrend` (§4.2) instead. This name is still M6's, for the city-wide multi-party
chart that is a different consumer with a different shape.

**Moved out in W3, on 2026-08-08.** `agora.state.settings`, `agora.state.setSetting` and
`agora.state.isFirstRun` were reserved here with `SettingsPayload`'s eight-field shape fixed in
advance. All three are now **published** and live in §4.1; the shape they shipped with is the shape
reserved here, unchanged. Reserving first and implementing second is what rule 1 asks for, and this
is what it looks like when it works.

---

## 9. File hazard

`ui/types/bindings.d.ts` is regenerated by `npx create-csii-ui-mod update` (`npm run update`). The
`declare namespace Agora` block appended to it would be lost. It lives there because that is the
only types file this contract owns; when the toolchain is next updated, move the block to
`ui/types/agora.d.ts` (no other change needed — it is a global declaration file either way) and
update this note.
