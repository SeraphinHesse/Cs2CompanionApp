# Plan 0003 — W5: the popup lane

**Written:** 2026-08-09
**Mandated by:** `fixplan.md` §W5, the **Popup** block (`fixplan.md:470-512`) — *"Decisions (owner):
popups for important events and important reports; they pause the game; event-pause can be turned
off; 'show all reports' can be turned on."*
**Status it inherits:** `docs/status.md:62` and `:75-80` — *"Not started: the entire popup lane. No
alert emission, no bindings, no modal, no pause wiring, no first-run interlock."*
**Blocks:** nothing. **Blocked on:** one owner decision, §0.1 below. Everything else is unblocked.

**Prior art this plan leans on and does not repeat:**
`docs/scout/2026-08-09-w5-press.md` (the W5 scout report — §D, §F and §G are the load-bearing parts),
`docs/plans/0002-w6-parties-tab.md` (structure, and the binding-surface merge discipline in its §8).
Every line reference below was re-opened in the **main working tree** rather than taken from the
scout report, whose numbers come from a worktree and have since moved in several files.

**No `politicsmodplan.md` §14 open decision is involved.** All six were checked at
`politicsmodplan.md:326-331`; none touches the press, the UI, or the pause.

---

## 0. Decisions for the owner — read before anything is built

> **MASTER RULINGS, 2026-08-09.** 0.1 and 0.2 are **resolved and no longer block**; 0.3 and 0.4 were
> never decisions. The reasoning below is retained as the record. Also recorded here: one deviation
> from §13's ordering that the master took deliberately.
>
> - **0.1 — take the recommendation. Read `tuning.Catalog.MajorSeverityThreshold`; no literal, ever.**
>   C2's checklist already forbids both literals, so the build is threshold-agnostic by construction
>   and this unblocks with no code consequence either way. **What remains genuinely the owner's is a
>   tuning question, not a code one:** whether `majorSeverityThreshold` should be **4** (shipped, and
>   the default we are keeping) or **3**. Moving it to 3 is a one-line edit to
>   `data/engine_tuning.json` that also loosens the major-event cooldown and makes more events shake
>   a government. **Not to be changed as part of this lane.**
> - **0.2 — take the recommendation. Party lifecycle alerts, and the hint text is amended.**
>   `fixplan.md:489` puts party founded/dissolved in the important set, so the shipped hint is the
>   thing that is wrong, not the scope. Apply the amended wording at C8 item 29.
> - **DEVIATION — C0 is not being built as a throwaway spike.** It requires a running game, which no
>   agent can do, and building code purely to delete it costs a session. Its three questions are
>   instead the **first items of the manual gate**, and they are answered on paper as far as paper
>   allows: (b) and (c) are verified against `refsrc/` in §6, and (a) is readable at
>   `AgoraUISystemBase.OnUpdate:79-82`. **The risk this accepts is stated plainly: if the ack →
>   `_stateVersion` → `Publish` round trip does not work in-game, C6's ack path is wrong and must be
>   revised.** C1 and C6 are written to make that bump explicit and commented so the fix is one line.

### 0.1 BLOCKING — what number is "a serious event"? `fixplan.md` says 3; the engine already says 4.

`fixplan.md:490` defines important as *"timeline events at severity ≥ 3"*. The scout report and this
plan both confirm there is no severity **filter** in the UI path. What neither the fixplan nor the
scout report noticed is that **the engine already has a ratified definition of a major event, and it
is not 3**:

```csharp
// src/Agora.Core/Tuning/EngineTuning.cs:843-844
/// <summary>Severity at or above which an event counts as major.</summary>
public int MajorSeverityThreshold { get; internal set; } = 4;
```

It is loaded from `data/engine_tuning.json:298` (`"majorSeverityThreshold": 4`), constrained to 1–5
by `data/schemas/engine_tuning.schema.json:461`, and it already decides two things in the shipped
build:

- `EventScheduler.IsMajor` (`src/Agora.Core/Events/Scheduler/EventScheduler.cs:377-378`) —
  `ev.Severity >= catalog.MajorSeverityThreshold` — which gates the major-event cooldown.
- `CoalitionStability` (`src/Agora.Core/Engine/Government/Coalitions/CoalitionStability.cs:361`,
  documented at `:30` — *"…shake a government; the rest are noise"*) — which decides whether an
  event destabilises the sitting government at all.

**So "major" is already a defined engine concept with a tuned number.** Shipping a popup that fires
at ≥ 3 would put two disagreeing definitions of "major" in one build: an event that interrupts the
player to announce itself, while the engine's own government-stability model treats it as noise.

**Recommendation: use `tuning.Catalog.MajorSeverityThreshold`, and never a literal.** The popup then
fires on exactly the events the engine considers capable of shaking a government, and a tuning pass
moves both together. Practically this makes the popup rarer than `fixplan.md:490` implies — with
`data/timeline_na.json` the ≥ 4 band is roughly a quarter of the catalog rather than most of it,
which is the correct volume for something that stops the clock.

**This is an owner call because it changes shipped intent, and I will not guess it.** If the owner
wants ≥ 3, the honest implementation is to *lower `majorSeverityThreshold` to 3 in
`data/engine_tuning.json`* and accept the knock-on to coalition stability and the event cooldown —
**not** to hard-code a second threshold in the projection. Two numbers is the outcome to avoid.

### 0.2 BLOCKING (small) — the shipped hint text does not mention parties

`ui/src/shell/SettingsPanel.tsx:221`, already in players' hands:

> *"Stop the clock when an election, a coalition or a serious event is reported."*

`fixplan.md:489` includes **party founded or dissolved** in the important set. The hint does not.
Either the hint gains a clause, or party lifecycle produces a feed row (§4) but never an alert.

**Recommendation: include party lifecycle, and amend the hint** to *"…when an election, a change of
government, a party's founding or collapse, or a serious event is reported."* A party splitting is
the single most legible political event the engine produces and the reason W6 exists; leaving it out
of the alert set while it sits in the feed is arbitrary. But it is copy the owner already approved,
so the change is theirs to make.

### 0.3 NOT a decision — no sidecar or settings schema change is needed. Confirmed.

`PauseOnMajorNews` and `ShowAllReports` exist end to end and were landed by plan 0001. Verified in
the main tree, all seven layers:

| Layer | Location |
|---|---|
| Engine contract | `src/Agora.Core/Contracts/PoliticalState.cs:136` (`= true`), `:142` (`= false`), cloned at `:175-176` |
| Sidecar defaults | `src/Agora.Mod/Persistence/SidecarSchema.cs:177-178` |
| Sidecar migration | `src/Agora.Mod/Persistence/SidecarSchema.cs:251` — *"added themeLocked, pauseOnMajorNews, showAllReports"* |
| State schema | `data/schemas/political_state.schema.json:390-391` |
| Write path | `AgoraRuntime.SetSetting`, `src/Agora.Mod/Core/AgoraRuntime.cs:912-916` |
| Projection → payload | `AgoraUiProjection.cs:63-64` → `AgoraUiPayloads.cs:176-177`, JSON at `:188-189` |
| TS + contract | `ui/src/shell/bindings.ts:20`, `ui/types/bindings.d.ts:3811-3812`, `docs/contracts/ui_bindings.md:190-191`, `:482-483` |

**This lane persists nothing new.** The alert queue is deliberately session-scoped (§5.3), so the
sidecar, `political_state.schema.json` and `politics_flavor.schema.json` are all untouched. The only
version number this lane moves is `docs/contracts/ui_bindings.md:3`, currently **6** — read it, write
value + 1, never hard-code (§9, and the same rule 0002 §8 laid down).

**If a coder finds themselves adding a sidecar field, they must stop and escalate.** Nothing in this
plan requires one, and the schema-bump budget for this pass is spent.

### 0.4 NOT a decision — no Harmony patch is needed to pause the sim. Confirmed against `refsrc/`.

See §6. There is a public, refcounted, game-owned surface and `ui/src/shell/pause.ts` already wraps
it. No patch, no `SimulationSystem` write, nothing for the master to rule on.

---

## 1. Where `fixplan.md` §W5 describes code that does not exist, or reasons from a false premise

Every item was opened and read in the main tree. This section is the reason the lane is cheaper than
the fixplan implies in two places and more expensive in one.

**a. "There is no severity filter anywhere" is true; "there is no severity concept" would be false.**
`fixplan.md:491-492` and the task brief both invite the conclusion that severity must be designed
from scratch. **It must not.** `TimelineEvent.Severity` is an `int`, documented *"1–5. Drives effect
scaling"* (`src/Agora.Core/Contracts/TimelineEvent.cs:100-101`); it is authored per catalog entry in
`data/timeline_na.json` / `timeline_eu.json` / `timeline_global.json`, range-validated on load
(`TimelineCatalogLoader.cs:296-303`, `CatalogIssueCode.SeverityOutOfRange`), drawn from a seeded
Gaussian for procedural events (`ProceduralEventGenerator.DrawSeverity`, `:217-227`), and already
crosses the bridge on **both** news payloads — `AgoraUiPayloads.cs:958` and `:1022`, serialised at
`:974` and `:1039`, populated at `AgoraUiProjection.cs:1025` and `:1188`. The UI even renders it
(`SEVERITY_STEPS` dot meters in `NewsFeed.tsx` and `EventList.tsx`).

**What is missing is a consumer that filters on it.** That is one comparison, not a new concept. And
per §0.1 the threshold to compare against already exists too. **New logic in this lane: the gate.
Not the datum, and not the number.**

**a′. Non-negotiable #1 is satisfied by construction, and is worth stating.** Severity's only two
sources are (i) a human-authored catalog file under `data/`, and (ii) a seeded draw in
`Agora.Core`. `data/schemas/politics_flavor.schema.json` declares no `severity` field anywhere — the
model cannot legally emit one, and `JsonSchemaSubsetValidator` plus `NumericFieldScanner` would
reject it if it tried. **No coder on this lane may add one.** An LLM-authored severity would be a
number from Claude output deciding engine-visible behaviour: a direct violation, and the review
should treat any `severity` key appearing in the flavor schema as a blocking defect.

**b. "Coalition formed produces no feed row" — CONFIRMED, and it is structural.**
`AgoraUiProjection.BuildFeed` (`:1212-1231`) skips at `:1215`:

```csharp
if (coalition == null || !coalition.EndedDate.HasValue) continue;
```

`state.CoalitionHistory` only ever receives coalitions that have already ended. The sitting
government lives in `state.Government` (`Coalition`, with `FormedDate` at
`src/Agora.Core/Contracts/Government.cs:57`), which `BuildFeed` never reads — though the projection
reads it elsewhere, at `:348` and `:566-568`, so there is a precedent for the access.

**c. "Party founded/dissolved has neither a feed row nor a tick signal" — HALF WRONG, and this is the
plan's biggest saving.** The *feed row* half is right: `BuildFeed` has no party loop. The *tick
signal* half is beside the point, and `fixplan.md:496-498`'s prescribed fix — *"detection is a
Mod-side diff in `AgoraRuntime.OnMonth` against `tick.KnownPartyIds`"* — is **the wrong design**.

`Party` already carries date-stamped lifecycle facts, persisted in the sidecar:

| Field | Where | Written |
|---|---|---|
| `Party.Status` (`PartyStatus`) | `src/Agora.Core/Contracts/Parties.cs:151`; enum at `:55-71` (`Active`/`Endangered`/`Dissolved`/`Merged`/`Revived`) | `PartyLifecycle.cs:322`, `:371`, `:460` |
| `Party.FoundedDate` | `Parties.cs:153` | `PartyLifecycle.cs:529`, `:592` — both `= input.Date` |
| `Party.DissolvedDate` (`SimDate?`) | `Parties.cs:156` | `PartyLifecycle.cs:323`, `:462` — both `= input.Date` |

So "which parties were founded or dissolved this month" is a **query over persisted state on the tick
date**, not a diff against a cached previous set. That matters for three reasons:

1. A diff needs a new cross-tick static in `AgoraRuntime` and a matching clear in `ResetForNewSave`
   (`AgoraRuntime.cs:535`). A dated query needs neither. One fewer thing to leak across a save
   boundary — which is the exact class of bug W0 existed to fix.
2. A **feed row is history** and must survive a reload. A diff cannot produce one after the fact; a
   dated query reproduces the whole history from `state.Parties` on every publish, which is what the
   election and coalition loops already do.
3. It keeps all of it in `Agora.Mod`'s projection, changing nothing in `Agora.Core`. `fixplan.md` is
   right that no Core change is needed; it is wrong about the mechanism.

**Two false-positive traps a naive `FoundedDate == today` will hit, both verified:**

- **The initial roster.** `PartyRegistry.GenerateInitial` stamps every party with the date it is
  handed (`PartyRegistry.cs:355`). At mint that is the save's start date
  (`PoliticalEngine.cs:83`) — so on a new save's first tick, *every* party is "founded today".
  Six founding alerts before the player has done anything.
- **The empty-registry recovery path.** `PoliticalEngine.cs:319-322` regenerates the whole registry
  with `date` — the *current* tick — and logs a warning. Same explosion, mid-save.

  (A retheme is safe: `PoliticalEngine.cs:175` passes `startDate`, not the retheme date.)

  **Mitigation, both belt and braces:** skip any party whose `FoundedDate` equals the save's start
  date, **and** cap party-lifecycle rows at two per tick (a split yields one; a merge yields one
  dissolution). If the cap trips, log it and emit none — a mass regeneration is a warning in
  `Agora.log`, not news.

**c′. And one hazard neither document mentions: revival erases the record.**
`PartyLifecycle.cs:373` sets `party.DissolvedDate = null` when a brand returns. A dissolution feed
row derived from `DissolvedDate` therefore **vanishes retroactively** when the party revives, and the
player's archive silently rewrites itself. This is acceptable and should be *documented in a comment*
rather than fixed: the alternative is a persisted lifecycle log, which is a sidecar change, which
§0.3 forbids. The alert already fired at the time and is session-scoped, so the player did see it.

**d. `fixplan.md:483-488` (pause via `SimulationSystem.selectedSpeed`) is already struck** in the
fixplan itself, correctly. §6 confirms it against `refsrc/` and adds the evidence that was missing.

**e. `fixplan.md:511-512`'s `useMapValue` caution is right for the wrong reason.** *"`useMapValue`
throws on an unregistered binding"* — the typing says exactly that
(`ui/types/api.d.ts:44`, and 0002 §2 records the owner verifying it against the bundle). But
`agora.news.article` **is** registered (`AgoraNewsUISystem.cs:40-41`), so the throw is not the
hazard. The real hazard is that `BuildArticle` returns an *empty* payload for an id it does not
recognise (`AgoraUiProjection.cs:1246-1250`), so an Event/Election/Coalition alert that fetched a
body would render a blank masthead rather than throwing. The alert payload must therefore carry
`hasArticle`, exactly as `NewsHeadlinePayload` already does, and the modal must branch on it.

**f. `fixplan.md:499-503` is accurate.** The two settings exist; the work is the consumer. §0.3.

**g. `docs/status.md:22` hard-codes the panel list** and `docs/status.md:62,75-80` describe this lane
as not started. Both need updating at the close (§11).

---

## 2. What the lane can stand on — the surfaces that already work

Stated so nobody rebuilds them.

| Need | Already exists | Where |
|---|---|---|
| Hold the sim paused, refcounted, released on unmount | `useSimulationHeldPaused(active)` | `ui/src/shell/pause.ts:24-55` |
| Overlay the whole HUD from a hook point | `Portal` + a two-div scrim/dialog wrapper | `ui/src/shell/FirstRunDialog.tsx:96-99` |
| Contain a modal's render failure without blanking the HUD | `FirstRunBoundary` (class component, `getDerivedStateFromError` at `:37`) | `ui/src/shell/FirstRunBoundary.tsx:30-77` |
| Mount a modal on its own append | `moduleRegistry.append("GameTopLeft", FirstRunDialog)` | `ui/src/index.tsx:35`, with the rule written at `:30-34` |
| Know it is the first run | `agora.state.isFirstRun` | `ui/src/shell/bindings.ts:49`; C# `AgoraRuntime.IsFirstRun` at `:220` |
| Inbound write that can be refused, with a timeout and English refusals | `requestSetting` / `isAccepted` / `writeMessage` | `ui/src/shell/bindings.ts:93-225` |
| A list `ValueBinding` published on the engine's cadence | `AgoraNewsUISystem` | `src/Agora.Mod/UiBindings/AgoraNewsUISystem.cs:23-67` |
| The feed rows an alert points at | `BuildFeed` | `AgoraUiProjection.cs:1146-1237` |

### 2.1 The publish-cadence trap — the single most likely way this lane ships broken

`AgoraUISystemBase.OnUpdate` (`src/Agora.Mod/UiBindings/AgoraUISystemBase.cs:72-93`) republishes
**only when `AgoraRuntime.StateVersion` changes**:

```csharp
int version = AgoraRuntime.StateVersion;
if (version == _publishedVersion) return;
```

Consequences for this lane, both non-obvious:

1. **The alert queue advances on player input, not on an engine tick.** When the player dismisses an
   alert, the C# handler *must* bump `_stateVersion` or the queue binding never republishes and the
   modal is stuck on a card the engine has already popped. `SetFlag` already does this correctly
   (`AgoraRuntime.cs:964`) and so does `dismissFirstRun` (`:936`) — copy that, and the review must
   check for it explicitly.
2. **The ack handler runs on the UI phase while the sim is paused.** That is fine and is precisely
   why `SetSetting` is synchronous — see its remarks at `AgoraRuntime.cs:878-892`. It also means the
   handler must do **no ECS work**, per `ui_bindings.md` §7 rule 6.

This is also the merge hazard the brief warns about: a `ValueBinding` field merged in without its
`_x.Update(...)` line in `Publish()` compiles, registers, and silently never updates. §12 makes the
`Publish()` body a required review item.

---

## 3. What an alert is

**One type, four kinds, no article body.** An alert is a *pointer at a feed row that already exists*,
plus enough to render a masthead card without a second round trip.

```
NewsAlertPayload
  id          string   the feed-row id it points at: "article:<id>" | "event:<id>"
                       | "election:<id>" | "coalition:<id>" | "party:<id>:founded"
                       | "party:<id>:dissolved"
  kind        string   "Article" | "Event" | "Election" | "Coalition" | "Party"
  date        string   SimDate, from AgoraTimeService via the engine tick (non-negotiable #8)
  headline    string
  summary     string   one line; the same FirstLine(body) the feed row uses
  outletName  string   "" for non-article kinds
  partyId     string   "" when none; the modal resolves the colour through agora.parties.roster
  districtId  string
  eventId     string
  severity    int      0 for non-event kinds
  major       bool     engine's verdict: does this one qualify to hold the clock (§5.2)
  hasArticle  bool     may agora.news.article be fetched for this id
```

`major` is computed in C# and published, rather than recomputed in TS from `severity`, for two
reasons: contract rule 5 (bindings are a view, never a channel — and "is this major" is an engine
judgement, not a rendering decision), and because the threshold lives in `EngineTuning`, which the UI
must not learn. **The UI never compares a severity to a number.** It reads `major`.

`kind` is a closed string vocabulary matching `NewsHeadlinePayload.Kind` (`BuildFeed` sets
`"Article"` / `"Event"` / `"Election"` / `"Coalition"` at `:1161`, `:1185`, `:1203`, `:1221`), plus
`"Party"`. Same convention as `PartyGovernmentRole` in 0002 §3.1: an enum on the C# side, a string
union on the TS side, never an integer across the bridge.

---

## 4. Prerequisite work: three missing feed rows

`fixplan.md:492-495` is right that an alert naming something the player cannot then find in the News
tab is worse than no alert. All three of these land **before** any alert emission.

### 4.1 Severity is already on the wire — only the gate is new (chunk C2)

No projection change is needed to *carry* severity. What chunk C2 adds is `major` on the alert
payload, computed once in the projection from `AgoraRuntime.Tuning.Catalog.MajorSeverityThreshold`
(pending §0.1). **Nothing filters the feed itself** — the News tab keeps showing every fired event at
every severity, which is correct: the feed is an archive, the popup is an interruption, and they
should not have the same admission policy. `fixplan.md:490` conflates the two.

### 4.2 Coalition formed (chunk C3)

In `BuildFeed`, after the existing `CoalitionHistory` loop (`:1212-1231`), add a loop over the
formation side. Two sources, and both are needed or the newest government is missing from its own
feed:

- every `state.CoalitionHistory[i]` with a `FormedDate` — these are past governments, and their
  formation is as much history as their end;
- `state.Government`, when non-null and `EndedDate` is absent — the sitting one, which
  `CoalitionHistory` will not contain until it ends.

Row shape, matching the existing coalition row:

- `Id = "coalition:" + coalition.Id + ":formed"` — **must differ from the ended row's
  `"coalition:" + Id`**, or the two collide in the feed and `CompareFeedRows` (`:1239-1243`) puts two
  rows with the same id adjacent, which reads as a duplicate.
- `Date = coalition.FormedDate`, `Kind = "Coalition"`, `PartyId = coalition.LeadPartyId`,
  `HasArticle = false`.
- Headline: distinguish the two cases the engine actually produces — a coalition formed after an
  election vs. one formed mid-term after a collapse. `Coalition.ElectionId`
  (`Government.cs:99`) is empty for the latter, which is the cheap discriminator.
- Summary: the member count and the lead party, from `MemberPartyIds` (`Government.cs:62`). **No raw
  ids in prose** — W2's rule; the panel resolves labels through `agora.parties.roster`, so the
  summary names a count and the payload carries `partyId`.

Skip `CoalitionStatus.Negotiating` (`Government.cs:91`, enum at `:6`): a government still being
formed is not news yet.

### 4.3 Party founded / dissolved (chunk C4)

A new loop in `BuildFeed` over `state.Parties`, producing at most two row kinds per party:

- `FoundedDate != startDate` → `Id = "party:" + p.Id + ":founded"`, `Date = p.FoundedDate`,
  `Kind = "Party"`, `PartyId = p.Id`, `HasArticle = false`.
- `p.DissolvedDate.HasValue` → `Id = "party:" + p.Id + ":dissolved"`, `Date = p.DissolvedDate.Value`.
  Distinguish `PartyStatus.Merged` from `PartyStatus.Dissolved` in the headline — a merge into
  `SuccessorPartyId` is a different story from a party dying below threshold, and the enum already
  separates them (`Parties.cs:66-67`).

`BuildFeed`'s existing `NewsFeedMax = 40` cap (`AgoraUiProjection.cs:23`, applied at `:1235` after
the date-DESC sort) absorbs the extra rows without further work — party rows are dated, so they age
out of the feed like everything else.

`BuildFeed` needs the save's start date to apply the §1c exclusion. It is available as
`state.StartDate`-equivalent through the runtime; the projection already reads `AgoraRuntime` in
`BuildFlavorStatus`, so this introduces no new dependency direction. **Prefer passing it as a
parameter** from `AgoraNewsUISystem.Publish` over reaching into a static from inside a loop — the
projection's other builders take their inputs as arguments and that convention is worth keeping.

---

## 5. Emission, de-duplication, and the two settings

### 5.1 Where an alert is emitted

**In `AgoraRuntime.OnMonth` (`AgoraRuntime.cs:1499-1560`), after `_state = tick.State;` (`:1518`)
and after `CollectProse` (`:1533`)**, in a new private method called from the end of `OnMonth`
alongside `MaybeWakeFlavor` (`:1559`).

Why there and not in the projection: the projection is a **view**, rebuilt from scratch on every
publish (contract rule 5). An alert is an *event* — it happens once. Deriving the queue in the
projection would re-raise every alert on every republish, which is the bug the ring exists to
prevent. `OnMonth` runs exactly once per sim month and is where every other one-shot consequence of a
tick already lives.

Why after `CollectProse`: article alerts need the prose that arrived this tick. `CollectProse` is
also called from `Tick` (`:1460`) when a background CLI generation lands mid-month — **article
alerts must be raised from `CollectProse`'s completion path too**, or a CLI document that arrives on
day 12 produces a feed the player is never told about. Put the article-alert raise inside
`CollectProse`, and the event/election/coalition/party raise in `OnMonth`.

### 5.2 What qualifies

| Kind | Qualifies when | `major` |
|---|---|---|
| Election | always (`tick.Election != null`) | true |
| Coalition | formed, or ended/collapsed | true |
| Party | founded or dissolved this tick, after the §1c exclusions | true *(pending §0.2)* |
| Event | fired this tick **and** `Severity >= MajorSeverityThreshold` *(pending §0.1)* | true |
| Article | only when `ShowAllReports` is on | **false** |

`major` is the field; the tick decides it. An article alert is never major — an ordinary month's
prose must not stop the clock even for a player who asked to see all of it, or `ShowAllReports` on a
yearly wake becomes four consecutive forced pauses.

### 5.3 The ring, and why nothing replays after a reload

```csharp
private static readonly List<NewsAlert> _alerts = new List<NewsAlert>();   // FIFO, bounded
private const int AlertQueueMax = 8;
```

- **In-memory only. Never persisted.** No sidecar field, no `PoliticalState` field. "Do not replay
  on reload" is then **structural**, not a rule someone has to remember: a reloaded save has an empty
  ring because the list is fresh. This is the same reasoning `_provisionalNamePartyIds`
  (`AgoraRuntime.cs:147-174`) is built on, and its comment is worth reading before touching this.
- **Cleared in `ResetForNewSave` (`AgoraRuntime.cs:535`)**, in the prose block beside
  `_flavorPayload = null` (`:554`). Not optional: CS2 reuses the ECS world across quit-to-menu, and
  W0 exists because three layers held city A's state into city B. A queue of city A's alerts popping
  over city B is exactly that bug's shape.
- **Bounded at 8, dropping oldest.** A player who leaves the game running through a decade at speed 3
  with `ShowAllReports` on must not accumulate an unbounded modal queue. When the ring drops, log it.
- **De-duplicated by feed-row id within the session.** A `HashSet<string>` of ids already raised,
  cleared in the same place. This is what stops a re-publish, a settings change, or a second
  `CollectProse` in the same month raising the same article twice. `fixplan.md:508` calls this
  "session-scoped and deliberately do not persist"; it is right, and the set is the mechanism.
- **Emit-time gating, not display-time.** Whether an alert enters the ring is decided once, against
  the settings as they stood when it happened. A player who turns `ShowAllReports` off does not
  retroactively clear a queued article; a player who turns it on does not retroactively gain last
  month's. Both alternatives are worse and the second is impossible anyway.

### 5.4 The two settings, exactly — one popup, two orthogonal questions

This is the brief's question, and the **already-shipped hint text answers it**. Both strings are in
players' hands today (`SettingsPanel.tsx:221` and `:231`) and this plan implements what they promise
rather than inventing a third reading:

| Setting | Question it answers | Effect |
|---|---|---|
| `ShowAllReports` (default **off**) | **What qualifies?** | Off: only `major` items enter the ring. On: ordinary articles enter it too. — *"Off, the press stays in the News tab and only major items interrupt."* (`:231`) |
| `PauseOnMajorNews` (default **on**) | **Does the modal hold the clock?** | On: `useSimulationHeldPaused(true)` while a `major` alert is showing. Off: the modal appears and the game keeps running. — *"Stop the clock when an election, a coalition or a serious event is reported."* (`:221`) |

The two do not overlap and the four combinations are all coherent:

- off / on (default) — major items only, and they stop the clock. The intended experience.
- off / off — major items only, no interruption to the clock. A modal you dismiss at your leisure.
- on / on — everything pops; only the major ones stop the clock. Article alerts never pause (§5.2).
- on / off — everything pops, nothing ever pauses. A news ticker.

**`ShowAllReports` is read in C#, at emit time. `PauseOnMajorNews` is read in TS, at display time.**
That split follows directly from what each one gates, and it means neither is read twice.

### 5.5 Clock unity (non-negotiable #8)

Every `date` on an alert comes from the `SimDate` the tick was handed — `today` in `OnMonth`, or the
dated field on the state object (`FiredDate`, `FormedDate`, `FoundedDate`, `DissolvedDate`). **No
`DateTime`, no recomputation, and nothing in the UI formats a year from anything but the published
string.** The existing `SimDate` → string convention in `AgoraUiPayloads` is the only route.

---

## 6. The pause — the concrete API, verified against `refsrc/`

**No Harmony patch. No decision for the master.** The evidence, from
`refsrc/Game/Game.UI.InGame/TimeUISystem.cs`:

```csharp
// :65
private EventBinding<bool> m_SimulationPausedBarrierBinding;
// :73
private bool pausedBarrierActive => m_SimulationPausedBarrierBinding.observerCount > 0;
// :93 — registered as a public binding on the "time" group
AddBinding(m_SimulationPausedBarrierBinding = new EventBinding<bool>("time", "simulationPausedBarrier"));
```

and the enforcement, `:119-142`:

```csharp
protected override void OnUpdate()
{
    base.OnUpdate();
    if (m_SimulationSystem.selectedSpeed > 0f)
        m_SpeedBeforePause = m_SimulationSystem.selectedSpeed;

    if (!m_HasFocus || m_SimulationPausedBarrierBinding.observerCount > 0)
    {
        if (!IsPaused()) m_UnpausedBeforeForcedPause = true;
        m_SimulationSystem.selectedSpeed = 0f;
    }
    else
    {
        if (m_UnpausedBeforeForcedPause)
            m_SimulationSystem.selectedSpeed = m_SpeedBeforePause;
        m_UnpausedBeforeForcedPause = false;
    }
}
```

**This closes scout open question Q3, which `pause.ts` asserted in prose and nothing had verified.**
Four properties, now evidenced rather than assumed:

1. **It is genuinely refcounted** — `observerCount > 0`, so N overlapping subscribers behave as one
   and the pause lifts only when the last releases. The article modal may therefore take the barrier
   while the first-run dialog holds it, with no interference.
2. **It is enforced every frame**, not written once. The `selectedSpeed`-is-a-no-op-while-loading
   failure `fixplan.md:486` describes cannot arise.
3. **The restore is the game's**, from `m_SpeedBeforePause`, captured continuously while speed > 0.
   Our code never captures or restores anything.
4. **A player who was already paused stays paused on release** — `m_UnpausedBeforeForcedPause` is
   only set when `!IsPaused()`. Correct behaviour, and worth knowing before anyone "fixes" it.

**One consequence to design around, not a bug:** while the barrier is held the game **forces speed to
zero every frame**, so the player's speed buttons appear dead. A modal that can be left open
indefinitely with `PauseOnMajorNews` on is a game the player cannot un-pause by any means except
dismissing it. The modal must therefore always have a visible, working dismiss — and unlike
`FirstRunDialog` (which deliberately has none, `FirstRunDialog.tsx:25-26`), **this one must never be
undismissable**. The boundary fallback must dismiss too (§7.3).

`ui/src/shell/pause.ts:24` is reused **verbatim**. No new pause code is written in this lane.

---

## 7. The modal

### 7.1 Use `Portal`, not `cs2/ui`'s dialog components. Here is why.

Checked, since the brief asks. `cs2/ui` ships:

- `DialogRenderer` / `DialogStack` / `DialogContext` (`ui/types/ui.d.ts:78-87`) — a host-side stack
  the *game* mounts. `DialogStack.showDialog(dialog: ReactNode)` is a context the game provides; a
  mod appending at a hook point is not inside a provider it controls, and pushing onto the game's own
  dialog stack couples the mod's lifetime to the game's modal state.
- `UITriggeredConfirmationDialog`, exported as `ConfirmationDialog`
  (`ui/types/ui.d.ts:171-186`, alias at `:703`). Its props are `title` / `message` / `details` /
  `confirm` / `cancel` / `onConfirm(dismiss: boolean)`. **It models confirm-or-cancel.** An article
  card is neither. `FirstRunDialog.tsx:24-26` already rejected it for the same reason and wrote the
  reasoning down.
- `Portal` (`ui/types/ui.d.ts:566-571`) — `({ children }) => React.ReactPortal`, **children only**.
  No `className`, no scrim, no centring.

**Decision: reuse the `FirstRunDialog` pattern.** `Portal` + the caller's own two-div wrapper:

```tsx
<Portal>
  <div className={styles.scrim}>
    <div className={styles.card}>
```

exactly as `FirstRunDialog.tsx:96-99`. It is the closest precedent in the repo, it is already proven
in-game, and it is the only one of the three that gives a masthead the layout freedom
`fixplan.md:475-478` asks for. **Do not import `FirstRunDialog.module.scss`** — copy the scrim/dialog
rules into the new module. Sharing them would couple two unrelated modals' visual language, and the
masthead's card is a different shape.

Gameface: **flexbox only, no CSS grid, no `backdrop-filter`** (`ui/CLAUDE.md`). The scrim is a flat
`rgba()` fill; the masthead's rules above and below the nameplate are `border-top` /
`border-bottom`, not pseudo-element tricks; the two-column body `fixplan.md:477` floats is **cut** —
Gameface's multi-column support is not something this plan is willing to assume, and a single column
at the card's width reads fine. Colour and opacity tokens come from `ui/src/shell/_tokens.scss` (W1),
not from literals.

### 7.2 Files

New folder `ui/src/panels/News/` is the wrong home — the modal is shell chrome, not a panel, and it
must render with the dashboard closed. **`ui/src/shell/`**, matching `FirstRunDialog`:

| File | Holds |
|---|---|
| `ui/src/shell/ArticleModal.tsx` | `ArticleModalInner` + the exported `ArticleModal` wrapping it in its boundary. Reads `alerts$`, `settings$`, `isFirstRun$`, `enabled$`, `roster$`; calls `useSimulationHeldPaused`; calls `useMapValue(article$, id)` **only when `current.hasArticle`**. |
| `ui/src/shell/ArticleModal.module.scss` | scrim, card, nameplate, dateline, headline, body, spot rule, counter, actions. |
| `ui/src/shell/ArticleModalBoundary.tsx` | Class boundary, modelled on `FirstRunBoundary.tsx:30-77`. Fallback renders **inline, not through `Portal`** (`FirstRunBoundary.tsx:60-64` explains why) and its action **acks the alert**, so a broken card cannot strand a held barrier. |
| `ui/src/shell/bindings.ts` | Two additions: `alerts$` and `ackAlert`. |
| `ui/src/shell/index.ts` | Re-export; and the comment block at `:2-4` mirrors `index.tsx`'s appends, so it moves too. |
| `ui/src/index.tsx` | A **fourth** `moduleRegistry.append`, its own, with a comment in the register's style. |

**`hasArticle` gates the `useMapValue` call, and a conditional hook is illegal.** The fix is the same
one 0002 §2 used: the fetch lives in a **child component** rendered only for article alerts, and that
child is keyed `key={current.id}` so a changing alert remounts the subscription rather than re-keying
a live one. Do not hoist `useMapValue` into `ArticleModalInner`.

### 7.3 Behaviour

- **One at a time by construction.** The component renders `alerts[0]` or `null`. There is no code
  path that mounts two, which is what `fixplan.md:505-506` asks for and is stronger than a rule.
- **"1 of 3"** from `alerts.length`, shown only when > 1.
- **Two actions: Dismiss, and Dismiss all** when > 1. Both are `ackAlert` calls (§8.2).
- **The pause is per-alert:** `useSimulationHeldPaused(open && current.major && settings.pauseOnMajorNews)`.
  Advancing from a major alert to a non-major one releases the barrier mid-queue, which is correct.
- **The interlock, §7.4.**
- **Master toggle:** every hook above the early return, then `if (!open) return null;` — the ordering
  discipline `FirstRunDialog.tsx:90-94` writes down.

### 7.4 The first-run interlock — what it is, and what W3 gives free

`fixplan.md:510`: *"Gate the whole thing on `!isFirstRun`, or a player meets an article stacked on a
region prompt that has no dismiss."*

**W3 already provides the entire signal.** `agora.state.isFirstRun` is published
(`ui/src/shell/bindings.ts:49`), backed by `AgoraRuntime.IsFirstRun` (`AgoraRuntime.cs:220`), flips
inside the UI tick that answers the prompt (`SetSetting`'s `dismissFirstRun` case, `:930-937`, which
bumps `_stateVersion`), and is cleared per-save in `ResetForNewSave` (`:543`). **The interlock is one
term in a boolean.** No new binding, no new C# state, no coordination protocol:

```tsx
const open = enabled && !isFirstRun && alerts.length > 0;
```

Three properties this buys, each of which would otherwise be work:

1. **The prompts cannot stack.** The region dialog has no dismiss by design
   (`FirstRunDialog.tsx:25-26`); a modal over it would be unanswerable.
2. **The barrier is not double-taken during the first run.** Harmless per §6 point 1, but it means
   the first thing a new player sees is one dialog, not a frozen game behind two.
3. **It self-clears.** The instant the region is chosen, `isFirstRun` goes false and any alert raised
   during the first tick becomes visible. Nothing needs to re-raise it, because the ring holds it.

**One case the interlock does not cover, and must be handled in the same line:** a save whose very
first monthly tick runs *while* the region prompt is up will emit party-founded alerts for the
initial roster — unless §1c's start-date exclusion is in place. It is. That exclusion and this
interlock are the two halves of "a new save is quiet".

---

## 8. C# design

### 8.1 `src/Agora.Mod/UiBindings/AgoraUiPayloads.cs`

One new `IJsonWritable`, `NewsAlertPayload`, with the §3 fields. Place it **immediately after
`NewsHeadlinePayload`** in the `// ---- agora.news` region — the two are siblings and a reader
comparing them should not have to scroll. Field order in `Write` mirrors §3's table so the contract
doc, the payload and the TS interface can be diffed by eye.

### 8.2 `src/Agora.Mod/UiBindings/AgoraNewsUISystem.cs`

Three edits, all small, all in the same file — **this is the shared binding surface and the merge
risk** (§12):

```csharp
// field, beside :16-21
private ValueBinding<List<NewsAlertPayload>> _alerts;

// in CreateBindings, after the wakeFlavor trigger at :45
AddBinding(_alerts = new ValueBinding<List<NewsAlertPayload>>(
    Group, "alerts", new List<NewsAlertPayload>(), ListOf<NewsAlertPayload>()));

AddBinding(new CallBinding<string, string>(Group, "ackAlert", OnAckAlert));

// in Publish, with the others at :61-64 — OMITTING THIS LINE IS THE SILENT FAILURE
_alerts.Update(AgoraUiProjection.BuildAlerts(AgoraRuntime.Alerts));
```

**`CallBinding`, not `TriggerBinding`.** The precedent is `agora.state.setSetting`
(`ui_bindings.md` §4.6 and `AgoraRuntime.SetSetting`): the UI needs to know the ack landed, because
if it did not, the modal must not close over an alert the engine still has. A trigger cannot say so.
The argument is the alert id, or the sentinel `"*"` for dismiss-all; the return is a
`CommandOutcome` name from the existing closed vocabulary, so `writeMessage`
(`ui/src/shell/bindings.ts:217`) already renders every refusal in English with no new copy.

Acking an id the ring no longer holds returns `Ok`, not `NotFound` — a double-click is not an error,
and a refusal there would put a scary sentence in front of a player who did nothing wrong.

### 8.3 `src/Agora.Mod/UiBindings/AgoraUiProjection.cs`

- `BuildFeed` (`:1146`) gains the coalition-formed block (§4.2) and the party block (§4.3), and a
  `startDate` parameter.
- New `internal static List<NewsAlertPayload> BuildAlerts(IList<NewsAlert> alerts)` — a straight
  copy, oldest first, no filtering and no sorting. The ring is already in emission order and
  **re-sorting a queue in the view would change which alert the player sees first**, which is
  contract rule 7's territory.

### 8.4 `src/Agora.Mod/Core/AgoraRuntime.cs`

- `_alerts` + `_raisedAlertIds` statics, beside the prose block at `:141-145`.
- `public static IList<NewsAlert> Alerts` getter beside `State` (`:259`) and `Prose` (`:292`).
- `RaiseAlerts(...)` called from the end of `OnMonth` (`:1559`, beside `MaybeWakeFlavor`), and the
  article-alert raise inside `CollectProse` (§5.1).
- `public static CommandOutcome AckAlert(string id)` — takes `Gate`, mutates the ring, `_stateVersion++`,
  returns `Ok`. Modelled line-for-line on `SetFlag` (`:955-966`) **minus `PersistSettings()`** — there
  is nothing to persist, and calling it would write the sidecar on every dismiss.
- Both collections cleared in `ResetForNewSave` (`:535`), in the prose block at `:554-558`.

**`NewsAlert` is a plain Mod-side class**, not a Core contract. It never enters `PoliticalState`, is
never serialised, and `Agora.Core` has no reason to know a UI queue exists. Putting it in Core would
be the failure mode `src/CLAUDE.md` warns about from the other direction.

---

## 9. `docs/contracts/ui_bindings.md` — the edits

1. **`:3`** — open the file, read the current value (**6** as of this writing), write value + 1. Never
   hard-code. See 0002 §8 for why this rule exists.
2. **The unfreeze note**, after the W6 paragraph at `:30-34`: one paragraph naming this plan, saying
   the change is purely additive — one `ValueBinding`, one `CallBinding`, one new payload shape,
   nothing renamed, no existing field moved or retyped.
3. **§4.5's table (`:404-409`)** — two rows:

   | `agora.news.alerts` | `ValueBinding<T>` | C# → UI | `List<NewsAlert>` | `Agora.NewsAlert[]` | on raise and on ack | `[]` | W5 |
   | `agora.news.ackAlert` | `CallBinding<string,string>` | **UI → C#** | alert id or `"*"` | `(id) => Promise<CommandOutcomeName>` | on dismiss | n/a | W5 |

4. **`:134`** — *"the only trigger"* prose about `wakeFlavor` stays true (`ackAlert` is a call, not a
   trigger), but the sentence listing the inbound bindings needs `ackAlert` added.
5. **§6's empty-value block** — `EMPTY_NEWS_ALERT`, copied literally, matching `ui/src/shell/bindings.ts`.
6. **The payload-shape list around `:482`** — the `NewsAlert` field list.
7. **`:707-708`, the reserved table** — `agora.news.markRead` is still unimplemented and stays
   reserved. **Do not repurpose it for the ack.** It means read-state *persistence*, which this lane
   explicitly does not do (§5.3), and quietly redefining a reserved name is worse than adding one.

---

## 10. What is deliberately not in this lane

- **Persisted read-state.** `agora.news.markRead` stays reserved for M6.
- **Any change to the feed's admission policy.** The News tab keeps every fired event at every
  severity (§4.1).
- **Two-column article body** (`fixplan.md:477`) — cut, §7.1.
- **A dedicated article RNG stream** — scout Q7, an engine-owner determinism question, unrelated.
- **The canned pool's own "residents say" violations** — scout §B4's irony, a prose-lane item.

---

## 11. Verification

**There is no headless test for any of this, and the reason is structural.** Stating it so nobody
spends a session finding out:

- `AgoraUiPayloads.cs` imports `Colossal.UI.Binding` and can never be linked into a suite that must
  run with no game installed; `AgoraUiProjection.cs` reaches `AgoraRuntime` and pulls the same
  dependency through. 0002 §7 covers the same ground and recommends the projection split as a
  follow-up — this lane does not fund it either.
- `AgoraRuntime` is `Agora.Mod` and unlinkable for the same reason.
- The modal, the pause and the interlock are React over game bindings.

**A test asserting `4 >= 4` would be tautological and must not be written.** The severity gate's only
interesting property — that it reads the tuning value rather than a literal — is a **code-review
item**, not a test.

**One genuinely testable thing exists, and it is not in this lane's critical path.** If chunk C4's
"which parties changed state on date D" query is extracted as a pure static over `IList<Party>` in
`Agora.Core` (it needs nothing else), it gets real tests in `tests/Agora.Core.Tests` covering the
initial-roster exclusion, the merge-vs-dissolve split, and the revival erasure of §1c′. **Recommended,
and cheap — roughly a third of a session.** It is the only part of the lane a machine can check.

### Build gate — every chunk

- `dotnet build Agora.sln` green.
- `dotnet test tests\Agora.Core.Tests\Agora.Core.Tests.csproj` green — must not regress.
- `cd ui && npm run build` green (includes `npm run check`, the design-token guard).

### Manual gates — the only real verification, one session with `--uiDeveloperMode`

- **M1.** New save, region prompt up: **no alert modal appears behind or over it**, and no
  party-founded popups for the initial roster. Choose a region; the prompt closes and the clock runs.
- **M2.** Play to the first election. One modal, masthead-styled, clock stopped. Dismiss it; the
  clock returns to the speed it was at, not to 1×.
- **M3.** With the game *already paused* by the player, trigger an alert and dismiss it. The game
  stays paused (§6 point 4) rather than starting to run.
- **M4.** Turn `PauseOnMajorNews` off. Next major alert: modal appears, **clock keeps running**.
- **M5.** Turn `ShowAllReports` on. Next flavor wake: an article modal appears with a body, and it
  **does not** stop the clock. Turn it off; ordinary articles stop popping.
- **M6.** Force two alerts in one tick. Counter reads "1 of 2"; Dismiss advances; Dismiss-all clears.
  **Never two cards at once.**
- **M7.** Dismiss an alert, then save, quit to menu, reload. **No alert replays.** Then load a
  *different* city: no alert from the first one appears (the `ResetForNewSave` clear).
- **M8.** With an alert open, quit to the main menu without dismissing. Re-enter a save and confirm
  the clock is not stuck at zero — the barrier was released by unmount.
- **M9.** Every alert the modal shows is findable in the News tab afterwards. Specifically check a
  coalition **formation** and a party founding, which are the rows §4 adds.
- **M10.** No raw id is visible anywhere on the card (W2's rule) — party names resolve through
  `agora.parties.roster`.

---

## 12. Parallel lanes, and the one file both must not fight over

**Chunk C1 is a hard join point and must be done alone, by one coder, before either lane starts.** It
registers the binding on both sides and moves the contract doc. After it lands:

| Lane | Chunks | Files it owns |
|---|---|---|
| **Lane A — C# emission** | C2, C3, C4, C5 | `AgoraUiProjection.cs`, `AgoraRuntime.cs`, `AgoraUiPayloads.cs` (the payload body only) |
| **Lane B — UI modal** | C6, C7 | `ui/src/shell/ArticleModal*.tsx`, `ArticleModal.module.scss`, `ArticleModalBoundary.tsx`, `ui/src/index.tsx`, `ui/src/shell/index.ts` |

**Disjoint.** The two lanes share no file after C1. Lane B develops against the empty list C1
publishes and a temporary local stub, which is genuinely useful: a modal that renders correctly from
an empty queue is a modal that will not crash on the frame before the first alert.

### The shared binding surface, spelled out

Three files carry both sides of the contract and are where every merge conflict in this pass has
actually happened:

| File | Region | Rule |
|---|---|---|
| `src/Agora.Mod/UiBindings/AgoraNewsUISystem.cs` | `CreateBindings` **and** `Publish` | C1 only. **A merge that keeps the `AddBinding` and drops the `_alerts.Update(...)` line in `Publish` compiles, registers, and silently never updates.** §2.1. The reviewer must open `Publish` and count: one `Update` per `ValueBinding` field, plus `_article.UpdateAll()`. |
| `ui/src/shell/bindings.ts` | module scope | C1 only. Every binding declared at module scope with a mandatory fallback. |
| `docs/contracts/ui_bindings.md` | `:3` and §4.5 | C1 only, and `:3` is **read then incremented**, never assigned a literal. |

If anything forces C1 to be split, the invariant to preserve is: **the `AddBinding` line and the
`Update` line land in the same commit.** They are one change.

---

## 13. Ordered checklist

Riskiest first. C0 exists because the publish-cadence question (§2.1) and the second-barrier question
(§6) are the two things that could invalidate the design, and both are answerable in an hour against
a running game — before any masthead is styled or any feed row is written.

**Chunk sizes:** C0 ≈ half a session · C1 ≈ half · C2 ≈ half · C3 ≈ one · C4 ≈ one (plus a third for
the optional Core extraction, §11) · C5 ≈ one · C6 ≈ one · C7 ≈ one · C8 ≈ half.
**Total: 9 chunks, ≈ 7 sessions**, of which C2–C5 and C6–C7 run concurrently.

### C0 — de-risk spike. Throwaway. Delete it entirely in C8.

- [ ] **1.** In `AgoraNewsUISystem`, register a stub `_alerts` `ValueBinding` returning one hard-coded
      `NewsAlertPayload`, and an `ackAlert` `CallBinding` that clears it and **bumps `_stateVersion`**.
- [ ] **2.** A throwaway `ArticleModal` — `Portal` + scrim + a div with the headline and a Dismiss
      button — appended in `index.tsx`, calling `useSimulationHeldPaused(true)`.
- [ ] **3.** **Launch with `--uiDeveloperMode` and answer three questions.** *Any "no" stops the lane
      and gets reported before further work.*
      - Does the card **disappear** when Dismiss is pressed? (Proves the ack → `_stateVersion` →
        `Publish` round trip. **Then deliberately remove the `_stateVersion++` and confirm it stops
        working**, so the fix is known to be the thing that works rather than assumed to be.)
      - Does the clock stop while it is up and **return to the prior speed** when it closes?
      - On a first-run save, mount it *alongside* `FirstRunDialog` and confirm both barriers coexist
        and the clock resumes only after **both** are gone (§6 point 1, in the game rather than in
        `refsrc`).

### C1 — the shared binding surface. **One coder, alone. Nothing else in this commit.**

- [x] **4.** `AgoraUiPayloads.cs`: real `NewsAlertPayload` with its full `Write` (§8.1).
- [x] **5.** `AgoraUiProjection.BuildAlerts` (§8.3) — returns empty for now.
- [x] **6.** `AgoraNewsUISystem`: field, `AddBinding` ×2, **and the `_alerts.Update(...)` line in
      `Publish`** (§8.2). `AgoraRuntime.AckAlert` + `Alerts` as stubs over an empty ring.
- [x] **7.** `ui/types/bindings.d.ts`: `Agora.NewsAlert`, `Agora.NewsAlertKind`.
- [x] **8.** `ui/src/shell/bindings.ts`: `alerts$`, `EMPTY_NEWS_ALERT`, `ackAlert` wrapper — reuse
      `requestSetting`'s timeout/`WriteOutcome` shape (`:105-156`) rather than a bare `call`.
- [x] **9.** `docs/contracts/ui_bindings.md`: all seven edits in §9. **Read `:3`, write value + 1.**
- [x] **10.** ~~`dotnet build Agora.sln` · `cd ui && npm run build`. Both green.~~ **Gated differently,
      deliberately.** `dotnet build Agora.sln` triggers `npm run build`, which **deploys into the
      player's live Mods folder** — not acceptable mid-session with lanes in flight. C1 was gated on
      `dotnet build src/Agora.Mod/Agora.Mod.csproj -p:UseCsiiToolchain=false` (0 errors),
      `dotnet test tests\Agora.Core.Tests\Agora.Core.Tests.csproj` (**1227 passing**), and
      `ui\node_modules\.bin\tsc --noEmit` (clean). Note `npm run check` is **not** a typecheck and
      **not** a class-parity check — it is only a design-token guard, despite the name. Use
      `tsc --noEmit`, and never `npx tsc`, which fetches a decoy package.

**C1 is the join. C2–C5 and C6–C7 may now run in parallel.**

---

### Lane A — C# emission

#### C2 — the severity gate *(blocked on §0.1)*

- [ ] **11.** Add `major` to the alert construction, reading
      `AgoraRuntime.Tuning.Catalog.MajorSeverityThreshold`. **No literal `3`, no literal `4`, anywhere.**
- [ ] **12.** A comment at the comparison naming `EngineTuning.cs:844` and `EventScheduler.cs:378` as
      the other two consumers of the same number, so the next reader knows it is shared.

#### C3 — coalition formed (§4.2)

- [ ] **13.** The formation loop in `BuildFeed`, over `CoalitionHistory` **and** `state.Government`.
- [ ] **14.** `":formed"` id suffix; skip `Negotiating`; headline discriminated on `ElectionId`.
- [ ] **15.** Build. Manually confirm in the News tab that a formation and its later ending are two
      distinct rows with distinct ids.

#### C4 — party founded / dissolved (§4.3)

- [ ] **16.** *(Recommended, §11)* Extract the "which parties changed state on date D" query as a
      pure static in `Agora.Core`, and test it: initial-roster exclusion, merge vs. dissolve, and the
      revival erasure of §1c′.
- [ ] **17.** The party loop in `BuildFeed`, with the `startDate` parameter, the start-date exclusion,
      and the two-per-tick cap. **A comment recording §1c′** — that a revival erases its own
      dissolution row, and that this is accepted rather than overlooked.

#### C5 — the ring and real emission (§5)

- [ ] **18.** `NewsAlert`, `_alerts`, `_raisedAlertIds`, the `Alerts` getter, `AckAlert` (§8.4).
- [ ] **19.** **The clears in `ResetForNewSave` (`AgoraRuntime.cs:535`), in the prose block at `:554`.**
      Not last, not optional — this is the W0 bug class.
- [ ] **20.** `RaiseAlerts` from `OnMonth`; the article raise from `CollectProse` (§5.1). Read
      `ShowAllReports` at emit time (§5.4). Cap at 8, log on drop.
- [ ] **21.** Build + test. Both green.

---

### Lane B — the UI modal *(starts after C1; does not wait for lane A)*

#### C6 — the modal shell, the pause and the interlock

- [x] **22.** `ArticleModalBoundary.tsx`, modelled on `FirstRunBoundary.tsx:30-77`. Fallback inline
      (**not** through `Portal`), and its action **acks**.
- [x] **23.** `ArticleModal.tsx`: `Portal` + scrim + card; `alerts[0]` or `null`; counter; Dismiss and
      Dismiss-all; `useSimulationHeldPaused(open && current.major && settings.pauseOnMajorNews)`; the
      interlock `enabled && !isFirstRun` (§7.4). Every hook above the early return.
- [x] **24.** The keyed child that calls `useMapValue(article$, id)`, mounted **only** when
      `current.hasArticle` (§7.2).
- [x] **25.** `ui/src/index.tsx`: a fourth `moduleRegistry.append`, its own, with a comment in the
      register's style. Update the *"Three appends"* prose at `:11` and the mirror at
      `ui/src/shell/index.ts:2-4`.

#### C7 — the masthead

- [x] **26.** `ArticleModal.module.scss`: nameplate with rules above and below, dateline, display
      headline, single-column body, party colour as a thin spot rule, scrim. **Flexbox only. No CSS
      grid. No `backdrop-filter`.** Tokens from `_tokens.scss`, no literal colours.
- [x] **27.** `npm run build` green, including `npm run check`.

---

### C8 — join and close

- [ ] **28.** **Delete every scrap of the C0 throwaway.** Grep for the stub payload's literal text
      before declaring this done.
- [ ] **29.** Read the shipped hint text against the built behaviour, one at a time
      (`SettingsPanel.tsx:219-237`), and apply §0.2's amendment if the owner took it.
- [ ] **30.** All three build gates green.
- [ ] **31.** The full manual walkthrough, M1–M10 (§11), in one session.
- [ ] **32.** Update `docs/status.md` — `:62` (the W5 row), `:75-80` (the "not started" paragraph),
      and `:22` if the panel list moved. Re-run the contract-drift audit `docs/status.md:120` says
      must repeat after new bindings land.
