# Contract — C# ↔ UI bindings

**schemaVersion: 5**

The fourth data contract. It spans two languages and two build systems, so nothing checks it at
compile time: rename a binding on one side and the panel silently renders nothing. Every binding
must be listed here, and this file is the authority when the two sides disagree.

**Frozen for M4.** Names and payload shapes in the *Registered bindings* table are law. Do not
rename, do not add a field, do not reorder a sort key. If a panel needs something that is not here,
report it — do not invent a binding name locally.

**Unfrozen three times, on the record.** Plan 0001 (`docs/plans/0001-batched-schema-change.md`) added
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

The freeze otherwise stands; these notes exist so the next reviewer reads authorised changes rather
than violations.

---

## 1. Naming

`agora.<area>.<name>` — lowercase group, `camelCase` name. The group prefix `agora` is reserved for
this mod. The JS side addresses a binding as two arguments, `(group, name)`:

```tsx
const seats$ = bindValue<Agora.SeatRow[]>("agora.seats", "allocation", []);
```

Five areas exist. Each has exactly one publishing `UISystemBase`.

| Area | Owns | Publisher |
|---|---|---|
| `agora.state` | dashboard chrome: is there a political state, what date, what term | `src/Agora.Mod/UiBindings/AgoraStateUISystem.cs` |
| `agora.parties` | the party/faction lookup table every panel renders labels and colours from | `src/Agora.Mod/UiBindings/AgoraStateUISystem.cs` |
| `agora.seats` | seat chart, government breakdown, mayor, last election, latest poll | `src/Agora.Mod/UiBindings/AgoraSeatsUISystem.cs` |
| `agora.districts` | per-district vote splits, wealth × education crosstabs, indices | `src/Agora.Mod/UiBindings/AgoraDistrictsUISystem.cs` |
| `agora.news` | news feed, timeline events, mandate tracker, LLM health | `src/Agora.Mod/UiBindings/AgoraNewsUISystem.cs` |
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
  `AGORA_NEWS_FEED_MAX = 40`, `AGORA_EVENTS_MAX = 25`, `AGORA_ELECTION_HISTORY_MAX = 12`.
- Anything per-district and expensive is a **map binding**, fetched only for the key the panel is
  actually showing — never a city-wide array of every district's full detail.
- Prose bodies never ride in a list payload. The feed carries headline + one-line summary; the body
  arrives through `agora.news.article` only when an item is opened.

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

Direction is C# → UI unless a row says otherwise. Eight bindings run the other way and every one of
them is marked **UI → C#** in its own table: `agora.news.wakeFlavor` (the only trigger),
`agora.state.setSetting`, and the six party editors in §4.2. "Cadence" is when the publisher calls
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
| `agora.state.summary` | `ValueBinding<T>` | C# → UI | `AgoraStateSummary : IJsonWritable` | `Agora.StateSummary` | monthly + on election | `EMPTY_STATE_SUMMARY` | M4 |
| `agora.state.settings` | `ValueBinding<T>` | C# → UI | `SettingsPayload : IJsonWritable` | `Agora.SettingsPayload` | monthly + on every accepted `setSetting` | `EMPTY_SETTINGS` | W3 |
| `agora.state.isFirstRun` | `GetterValueBinding<bool>` | C# → UI | `bool` | `boolean` | UI tick | `false` | W3 |
| `agora.state.setSetting` | `CallBinding<string,string,string>` | **UI → C#** | `(key, value) => CommandOutcome` | `(key: string, value: string) => Promise<Agora.CommandOutcomeName>` | on click | n/a | W3 |

`enabled` is the master toggle — when false a panel renders `null`, not a disabled shell. `ready` is
true once the engine has published a political state at least once; until then panels render a
skeleton, because every other binding in this contract is still at its empty value.

`settings` is the per-save settings document, sidecar-backed and **never global config**
(non-negotiable #10). It is a mirror: the panel renders it and writes through `setSetting`, never the
other way round.

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
| `dismissFirstRun` | ignored | Clears `isFirstRun` without changing a setting. Not persisted. |

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
age); this binding sums the four age bands so 15 rows cross the bridge instead of 60. Turnout in a
cell is the vote-weighted turnout of its blocs, so the disenfranchised child/teen bands drag it down
correctly rather than being dropped.

**`hasCityFallbacks` is a rendering obligation, not decoration.** When it is true, every field named
in `cityFallbackFields` is a city number wearing a district's name. The panel must mark those fields
visually (dimmed + a tooltip) and must never present them as a local fact. This is
`politicsmodplan.md` §6, and the reviewer checks it.

### 4.5 `agora.news` — feed + mandate tracker

| Binding | Kind | Direction | C# type | TS type | Cadence | Empty / loading | Since |
|---|---|---|---|---|---|---|---|
| `agora.news.feed` | `ValueBinding<T>` | C# → UI | `List<NewsHeadline>` | `Agora.NewsHeadline[]` | on flavor publish + on event fire | `[]` | M4 |
| `agora.news.article` | `GetterMapBinding<string,T>` | C# → UI | `NewsArticle` per key | `Agora.NewsArticle` | on demand, per subscribed key | `EMPTY_NEWS_ARTICLE` | M4 |
| `agora.news.events` | `ValueBinding<T>` | C# → UI | `List<TimelineEventBrief>` | `Agora.TimelineEventBrief[]` | on event fire / expire | `[]` | M4 |
| `agora.news.mandates` | `ValueBinding<T>` | C# → UI | `List<MandateRow>` | `Agora.MandateRow[]` | monthly | `[]` | M4 |
| `agora.news.flavorStatus` | `ValueBinding<T>` | C# → UI | `FlavorStatus` | `Agora.FlavorStatus` | on every flavor attempt, success or failure | `EMPTY_FLAVOR_STATUS` | M4 |
| `agora.news.wakeFlavor` | `TriggerBinding` | **UI → C#** | — | `() => void` | on click | n/a | M4 |

Sort keys:

- `feed`: `date` **descending**, then `id` ordinal ascending. Capped at `AGORA_NEWS_FEED_MAX = 40`.
- `events`: `firedDate` **descending**, then `id` ordinal ascending. Capped at `AGORA_EVENTS_MAX = 25`.
- `mandates`: **status rank** ascending — `Active` 0, `Pending` 1, `PartiallyFulfilled` 2,
  `Fulfilled` 3, `Defied` 4, `Abandoned` 5 — then `deadlineDate` ascending, then `id` ordinal
  ascending. So the tracker opens on what is live and closest to its deadline.
- `article.tags`, `events[].tags`, `events[].districtIds`: ordinal ascending.

`agora.news.wakeFlavor` is the manual LLM wake from `politicsmodplan.md` §2. It **requests**; the
engine decides. The panel must disable the control while `flavorStatus.pendingWake` is true and must
not assume the feed changes as a result — a failed wake keeps the last good flavor by design
(non-negotiable #7), and the only visible consequence may be `flavorStatus.lastError`.

`flavorStatus.lastError` is an **engine-authored** short code, never LLM output and never a raw
exception message: `""`, `"CliMissing"`, `"Timeout"`, `"BadJson"`, `"Disabled"`, `"Unknown"`.

A mandate with `isMeasurementStalled === true` is **held, not failing**. Render it as paused; do not
render its progress bar as falling behind, and never show it as `Defied` because the clock ran out
while its metric was unreadable.

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
| `ValueRequired` | The field was left empty. **Empty is never a reset**; resetting is its own binding (§4.2). |
| `TooLong` | Over the limit published by `agora.parties.editLimits`. Separate from `BadValue` so the counter and the rejector say the same thing. |
| `OkColorInUse` | **Accepted, with a warning.** The colour was applied; another party already wears it. |

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
                    showAllReports, effectsEnabled
PartyBrief          id, name, shortName, description, slogan, colorHex, status, isIncumbent,
                    isInGovernment, coreGrievance, foundedDate, dissolvedDate, nameLocked,
                    descriptionLocked, colorLocked
PartyPalette        colors  — "#RRGGBB", UPPERCASE, in EngineTuning.Parties.ColorPalette order
PartyEditLimits     nameMax, shortNameMax, descriptionMax, sloganMax, colorPattern
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
NewsHeadline        id, date, kind, headline, summary, outletId, outletName, severity, partyId,
                    districtId, eventId, hasArticle
NewsArticle         id, date, headline, byline, body, tone, outletId, outletName, tags, partyId,
                    districtId, eventId
TimelineEventBrief  id, date, title, region, origin, severity, durationMonths, firedDate,
                    expiresDate, archetypeId, localAngle, tags, districtIds
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
`FactionBrief.name`/`shortName`/`leaderName`, `MayorSummary.name`, `PollSummary.pollsterName`,
`NewsHeadline.headline`/`summary`/`outletName`, `NewsArticle.*` prose,
`TimelineEventBrief.localAngle` and `MandateRow.text`. Render them; never parse them, never sort by
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
constants it needs module-locally — there is no shared runtime module, by design, because three
panels are authored in parallel.

```tsx
const EMPTY_STATE_SUMMARY: Agora.StateSummary = {
  schemaVersion: 0, date: "", termNumber: 0, system: "Proportional", theme: "Eu",
  nextElectionDate: "", isCampaignSeason: false, weeksToElection: -1, mayorPartyId: "",
};

const EMPTY_SETTINGS: Agora.SettingsPayload = {
  schemaVersion: 0, startYear: 1990, theme: "Eu", system: "Proportional",
  themeLocked: false, pauseOnMajorNews: true, showAllReports: false, effectsEnabled: true,
};

const EMPTY_PARTY_PALETTE: Agora.PartyPalette = { colors: [] };

const EMPTY_PARTY_EDIT_LIMITS: Agora.PartyEditLimits = {
  nameMax: 0, shortNameMax: 0, descriptionMax: 0, sloganMax: 0, colorPattern: "",
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
  id: "", date: "", headline: "", byline: "", body: "", tone: "", outletId: "", outletName: "",
  tags: [], partyId: "", districtId: "", eventId: "",
};

const EMPTY_FLAVOR_STATUS: Agora.FlavorStatus = {
  lastFlavorDate: "", lastAttemptDate: "", isStale: false, providerAvailable: false,
  pendingWake: false, lastError: "", articleCount: 0,
};
```

Array bindings take `[]`. `agora.seats.government`, `agora.seats.mayor`, `agora.seats.lastElection`
and `agora.seats.latestPoll` take `null`.

`weeksToElection` is `-1` — not `0` — when no election is scheduled, so "the election is this week"
stays distinguishable from "there is no election".

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
| `agora.seats.pollTrend` | trend chart of published poll shares over time | M6 |
| `agora.districts.overlay` | political map overlay tint data per district | M6 |
| `agora.districts.blocs` | the full 60-bloc breakdown behind a crosstab cell | M6 |
| `agora.news.archive` | paged news archive beyond the 40-item feed | M6 |
| `agora.news.markRead` | UI → C# read-state persistence | M6 |

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
