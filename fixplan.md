# AGORA — Fix Plan

**Written:** 2026-08-01
**Trigger:** first real play session against a loaded city — five reported issues, plus four
codebase reviews run alongside them.
**Status of `docs/status.md`:** stale. It still reads "M0 · Bootstrap, in-game verification
pending". The mod is deployed, loads, ticks, and renders three panels. Update it as part of W0.

This plan does not re-litigate anything ratified in `politicsmodplan.md`. Where a fix touches a
ratified contract, the change is routed through `/schema-change` and called out below.

---

## 0. Summary of what is actually wrong

Five reported issues resolve into **six workstreams**. Two of the reported issues (party naming,
stale-state-across-cities) are each *several* independent bugs in different layers, which is why
they looked unfixable from the outside.

| # | Reported | Root cause | Workstream |
|---|---|---|---|
| 1 | UI too transparent | Four panels each declare their own opacity, none agree, lowest is 0.62 | W1 |
| 2a | Parties named `party-01` | Flavor roster is never set before the first prose poll | W2 |
| 2b | No US/EU difference | `RegionTheme` has no selection surface anywhere; always defaults to `Eu` | W3 |
| 2c | No rename / recolour | Feature does not exist; `ApplyProseNames` would clobber it if it did | W4 |
| 3 | Articles unclear, no popup | Prompt asks for atmosphere, not news; no popup mechanism exists | W5 |
| 4 | No Parties tab | Panel does not exist; `PartyBriefPayload` lacks the fields it needs | W6 |
| 5 | Old city's values persist | Three separate layers hold per-save state that only resets on process exit | W0 |

W0 goes first. It is the only one that corrupts data rather than merely looking wrong, and every
other workstream is harder to test while a stale save state can leak into a fresh city.

---

## W0 — Per-save reset seam (**blocking, do first**)

### The defect

`AgoraRuntime` is a static class. `Detach()` is called from exactly one place — `Mod.OnDispose`,
i.e. process exit. Quitting to the main menu and loading a different city calls nothing. Three
layers independently retain city A's state:

1. **Runtime prose state** — `AgoraRuntime.OnSidecarLoaded` resets `_state`, `_snapshotHistory`
   and `_saveSettings`, but leaves `_flavorPayload`, `_lastFlavorDate`, `_lastAttemptDate`,
   `_lastFlavorState`, `_lastSnapshot` and `_lastTick` holding the previous city's values.
   *This is the one the player sees:* city B shows city A's articles and party prose.
   `AgoraRuntime.cs:71-83`.

2. **Effect ledger** — `AgoraEffects.Initialize` is called only from `AgoraRuntime.Attach`, which
   early-returns when already attached to the same world (`AgoraRuntime.cs:259`). CS2 reuses the
   ECS world across city loads, so the ledger is **never rebuilt** for city B. City A's live
   modifiers stay in it with their duration counters running. This is the one that mutates the
   player's city.

3. **Heartbeat cadence** — `AgoraHeartbeatSystem._lastTickedDay` / `_hasTicked`
   (`AgoraHeartbeatSystem.cs:29-32`) are instance fields, never reset. Load city B on the same
   in-game date city A last ticked and the gate at `:95` skips the tick entirely; the political
   layer sits idle until the date rolls over.

Also confirmed: `_lastFlavorState` (`AgoraRuntime.cs:78`) surviving the load means that if both
cities leave the provider in the same state, the transition-detection at `:532` never fires and a
completed generation is silently dropped.

`AgoraSidecarSystem._saveGuid` is never reset either, and `EnsureIdentity` early-returns when it is
non-empty (`:331`). If city B carries no Agora block and `SetDefaults` does not fire, city B writes
into **city A's sidecar directory**. Verify against `Agora.log` before assuming; the fix is the same
either way.

### The fix

Do **not** patch each layer separately — that is how this got to three copies. Introduce one seam.

- [ ] Add `AgoraRuntime.ResetForNewSave()`: clears every per-save field, calls
      `AgoraEffects.Shutdown()` then re-`Initialize`s from the loaded tuning, resets the district
      resolver, and disposes the flavor provider. Distinct from `Detach()`, which additionally
      drops the world-level references.
- [ ] Call it from the **first line** of `OnSidecarLoaded`, before any restore work.
- [ ] Add an `OnGamePreload` override to `AgoraHeartbeatSystem` resetting its four cadence fields.
      `GameSystemBase` already subscribes to `onGamePreload`; this needs no new hook.
- [ ] Reset `AgoraSidecarSystem._saveGuid` to `Guid.Empty` on game preload so `EnsureIdentity`
      re-mints per city.
- [ ] **Verify during implementation** (flagged by review, not yet confirmed):
      `AgoraEffectApplicationSystem._slots` is a `Dictionary<SlotKey, SlotState>` holding `Entity`
      handles, and `_loggedMissingDistricts` is a never-cleared `HashSet`. Stale `Entity` handles
      pointing into a destroyed city would be the most dangerous form of this bug. Read the file,
      confirm, and hang both off `ResetForNewSave` if so.

### Acceptance

Load city A, let a year pass, quit to main menu, load city B. City B's news feed is empty, its
parties carry city B's names, no modifier from city A is live, and the heartbeat ticks on the first
day. Repeat A→B→A without restarting the process. Add a headless test in
`tests/Agora.Core.Tests` for the state-clearing half; the ECS half needs the manual walkthrough.

---

## W1 — Readability

### The defect

No shared design tokens. Each panel redeclares its own SCSS variables and they disagree:

| Surface | File | Current |
|---|---|---|
| Shell bar | `ui/src/shell/Shell.module.scss:11` | `rgba(8,10,14,0.86)` |
| News | `ui/src/panels/News/NewsPanel.module.scss:21` | `rgba(8,10,14,0.86)` |
| Districts | `ui/src/panels/Districts/DistrictsPanel.module.scss:14` | `rgba(0,0,0,0.72)` |
| Seats | `ui/src/panels/Seats/SeatsPanel.module.scss:16` | `rgba(0,0,0,0.62)` |

Gameface has no `backdrop-filter`, so opacity is the only lever.

### The fix

- [ ] Create `ui/src/shell/_tokens.scss` — the single source for surface, text, line, accent and
      status colours. Import it in all four panels and delete the local `$` declarations.
- [ ] Panel body surface → `rgba(8,10,14,0.94)`. Shell bar → `0.94`. Raised/inset surfaces stay
      relative to the body, not absolute.
- [ ] Raise `$text-dim` from `0.62` to `0.72` and `$text-faint` from `0.4` to `0.55`.
- [ ] `Crosstab.module.scss:116-120` — axis labels are 8rem at 0.5 opacity, effectively illegible.
      → 10rem at 0.75.
- [ ] Add a `css-presence` check (the harness in `ui/tools/css-presence.js` already exists) that
      fails the build if a panel declares a `$surface` of its own.

---

## W2 — Party names lock in

### The defect

`PartyRegistry.GenerateInitial` (`src/Agora.Core/Engine/Parties/PartyRegistry.cs:273`) deliberately
never sets `Name` — names are flavor-owned. The canned pool is perfectly capable of producing them
("Civic Alliance", "Riverside Slate"), but it only learns *which parties exist* from
`StaticPoolProvider.Roster`, set in exactly one place: `LayeredFlavorProvider.RequestFlavor`
(`FlavorProviders.cs:59`).

`AgoraRuntime.OnMonth` calls `CollectProse` **before** `MaybeWakeFlavor`, and `MaybeWakeFlavor`
early-returns unless `tick.DidWork` and a wake cadence fired. So the first poll generates a
document against an empty roster, stamps `_lastGeneratedFor = date`, and no party is ever named.
The UI then falls back to the raw id (`SeatsPanel.tsx:143`: `shortName || name || id`).

Compounding it: on load, the flavor cache is re-validated against a catalog built from current
state (`AgoraRuntime.cs:475`, `ClaudeCliProvider.cs:91`). A fresh save has an empty catalog, so
every cached entry is dropped.

### The fix

- [ ] Set `_flavor.Pool.Roster` at state-mint time, in `OnSidecarLoaded` immediately after
      `RebuildFlavor()`. Build it with the existing `FillBriefs` — extract that call so mint and
      wake share one path.
- [ ] After any prose collection, sweep `_state.Parties` for an empty `Name` and fill from the pool
      synchronously. A party must never reach a binding unnamed.
- [ ] Names persist on `Party.Name` in the sidecar already, so once set they survive reload. Add a
      determinism test: same save GUID + same date ⇒ byte-identical names.
- [ ] Fix cache re-validation to use the union of the current catalog and the previously-seen id
      set, so a fresh save does not discard its own cache.
- [ ] Never render a raw id to the player. Where `name` is genuinely absent, show a themed
      placeholder ("Unnamed list"), not `party-01`.

---

## W3 — EU / US theme, chosen by the player

**Decision (owner):** a first-load prompt showing an EU flag and a US flag.

### The defect

`AgoraSettings.Theme` (`src/Agora.Core/Contracts/PoliticalState.cs:90`) defaults to `Eu` and there
is **no selection surface anywhere**. The doc comment claims it "follows the map theme by default";
that code was never written. The global options page carries only `Enabled` and
`LogDailyHeartbeat`. Theme drives the electoral system (proportional vs FPTP + mayor), the naming
vocabulary, term length, and which timeline catalogs apply — so the whole save is currently EU
whatever map you are on.

### The fix

- [ ] Publish `agora.state.settings` — already **reserved** in `docs/contracts/ui_bindings.md` §8
      for exactly this. Read/write per-save settings; sidecar-backed, never global (non-negotiable
      #10).
- [ ] New modal `ui/src/shell/FirstRunDialog.tsx`, rendered through `cs2/ui`'s `DialogRenderer` /
      `Portal` (`ui/types/ui.d.ts:78-87, 562-571`). Two large flag choices, each with one line of
      consequence text:
      - **Europe** — proportional list seats, 4–7 parties, coalition governments, 3-year terms.
      - **United States** — first-past-the-post district races, a directly elected mayor, two
        dominant parties with internal factions, 4-year terms.
- [ ] Fires once per save, when the sidecar loads with no prior state. Pause the sim while it is
      open (see W5 for the pause helper).
- [ ] Add `ThemeLocked: bool` to per-save settings. Set it at the first election — before that the
      player may change their mind from the settings surface; after it the choice is history.
- [ ] `System` (`ElectoralSystem`) must be re-derived when `Theme` changes, and the party registry
      regenerated if no election has yet been held.
- [ ] Schema: per-save `AgoraSettings` gains `ThemeLocked`, `PauseOnMajorNews`, `ShowAllReports`.
      Bump `schemaVersion` and run `/schema-change` — sidecar migration included.

---

## W4 — Player-owned party identity

**Decision (owner):** inline edit in the Parties tab; player edits stop the LLM from making changes.

### The defect

Does not exist. And `AgoraRuntime.ApplyProseNames` (`:764-783`) overwrites `Name`, `ShortName`,
`Description` and `Slogan` unconditionally on every successful generation, so a naive rename would
be silently reverted at the next flavor wake.

### The fix

- [ ] Add `PlayerOverrides` to `Party` — a small flag set (`NameLocked`, `DescriptionLocked`,
      `ColorLocked`) rather than four booleans on the root, so it stays cheap to serialise. Schema
      bump via `/schema-change`.
- [ ] `ApplyProseNames` skips any field whose lock is set. This is the single enforcement point.
- [ ] New UI→C# bindings under `agora.parties`: `rename`, `setDescription`, `setColor`. Use
      `CallBinding` (not `TriggerBinding`) so the panel can surface a rejection.
- [ ] Validate on the C# side: name ≤ 60 chars, short name ≤ 12 (the seat chart depends on it),
      description ≤ 600, colour must be `#RRGGBB`. Reject rather than truncate.
- [ ] Colour picker offers the tuning palette (`tuning.Parties.ColorPalette`) plus a free hex
      field. Warn — do not block — on a colour already taken; `PartyRegistry.AllocateColor` only
      guarantees uniqueness at generation time.
- [ ] A "reset to generated" control per field that clears the lock and restores flavor ownership.
- [ ] Record every binding in `docs/contracts/ui_bindings.md`. That contract spans two build
      systems and drifts silently.

---

## W5 — The press

**Decisions (owner):** popups for important events *and* important reports; they pause the game;
event-pause can be turned off; "show all reports" can be turned on; elections covered extensively;
masthead-styled; lean articles; Haiku to minimise token spend.

### The defect

Three separate problems.

**Prose is vague by construction.** The canned templates in `StaticPoolContent.cs` are pure
atmosphere — *"In {district}, the argument is about the basics"*, *"The long complaint from
{district}"*. They never name the event, the result, or the party. The LLM prompt is no better;
`FlavorPromptBuilder.cs:241-244` asks only for *"N articles from local outlets covering the city as
described above. Vary the outlets and the tones."* Nothing instructs it to lead with what happened.

**No popup exists.** Articles surface only if the player opens the News tab and clicks a row.

**Model and cost.** The CLI is invoked as a hardcoded bare `-p --output-format json`
(`ClaudeCliRunner.cs:101`) with no model selection, so it runs whatever the CLI defaults to.
`ArticleCount` defaults to 4 (`FlavorRequest.cs:87`), the schema permits a 900-char body, and the
timeout is 120s with one retry.

### The fix

**Prompt and content**

- [ ] Rewrite the article instruction: every article must lead with **what happened, to whom, and
      why it matters**, name at least one party or district by id from the supplied catalog, and
      state the concrete change in the first sentence. Ban the "residents say / officials say"
      construction outright — it is the shape all four current templates share.
- [ ] Tighten the schema: headline ≤ 90 chars (from 140), body ≤ 420 (from 900). Lean is both the
      requested style and the cheaper one. `/schema-change`.
- [ ] Require `refs` to be populated. An article referencing nothing is what produces prose about
      no identifiable subject.
- [ ] Rewrite `StaticPoolContent` headline and body templates against the same rule — the canned
      pool is the fallback for the fallback and must not be the vaguest thing in the build.
- [ ] **Election coverage.** On an election tick, request a dedicated set: a result piece, a
      winner's-reaction piece, a loser's-reaction piece, and — under EU theme — a coalition-outlook
      piece. Raise `ArticleCount` for that wake only.

**Model**

- [ ] Add `Model` to `ClaudeCliOptions`, default `claude-haiku-4-5-20251001`, overridable via
      `AGORA_CLAUDE_MODEL`. Append `--model <model>` to `ClaudeCliRunner.Arguments`.
      *(An earlier review claimed `claude -p` cannot select a model. That is wrong — `claude --help`
      on this machine lists `--model <model>`. Verified 2026-08-01.)*
- [ ] Non-negotiable #1 is unaffected: schema validation and the numeric sweep already stand
      between model output and engine state, so a smaller model cannot corrupt anything. It can
      only write worse prose, which the validator will still reject if malformed.

**Popup**

- [ ] `ui/src/shell/ArticleModal.tsx` — masthead layout: outlet nameplate in a serif face with rules
      above and below, dateline, headline at display size, body in two columns if it fits the
      hook-point width, party colour as a thin spot rule. Rendered through `Portal` so it overlays
      the whole HUD rather than sitting in the top-right corner.
- [ ] Pause via `SimulationSystem.selectedSpeed = 0` — public setter, verified at
      `refsrc/Game/Game.Simulation/SimulationSystem.cs:72`. **No Harmony patch required.** Capture
      the prior speed and restore it on dismiss; restore on panel unmount too, or a closed dashboard
      leaves the player paused with no way back.
- [ ] "Important" = elections (always), coalition formed or collapsed, party founded or dissolved,
      and timeline events at severity ≥ 3.
- [ ] Two per-save settings, both in the W3 schema bump: **Pause on major news** (default on),
      **Show every report as a popup** (default off).
- [ ] Queue, never stack. If two qualifying items land in one tick, show them in sequence with a
      "2 of 3" counter. A modal that can open on top of a modal is how a player gets stuck.

---

## W6 — Parties tab

**Decision (owner):** new tab showing name, description, per-issue priorities, current
support/polling, and seats.

### What already exists

`Party` carries everything needed: `Description`, `Slogan`, `ColorHex`, `Status`, `FoundedDate`,
`CoreGrievance`, `Platform` (the six-issue position vector), `LastManifesto`, `LastVoteShare`,
`SeatsHeld`, `IsIncumbent`, `IsInGovernment`, `FactionIds`. `PartyBriefPayload`
(`AgoraUiPayloads.cs:165`) publishes almost none of it.

### The fix

- [ ] New binding `agora.parties.detail` as a `GetterMapBinding<string, T>` keyed by party id —
      same on-demand pattern as `agora.districts.detail`, so the whole roster's detail is not
      pushed across the bridge every month.
- [ ] New `PartyDetailPayload`: description, slogan, platform vector, last manifesto vector,
      seats, seat share, last vote share, current poll share, poll delta since the election,
      status, founded date, above/below threshold, government role, faction list.
- [ ] `ui/src/panels/Parties/` — list rail of parties (colour swatch, short name, seats, poll
      share) plus a detail pane. Register in `TAB_ORDER` in `ui/src/shell/state.ts`.
- [ ] Issue priorities as a six-row horizontal bar set, labelled in plain English — not
      `HeritageOrder`.
- [ ] Inline edit controls from W4 live in the detail pane header.
- [ ] Update `docs/contracts/ui_bindings.md` §4 with the new sub-namespace.

### Additional content — proposed, decide before building

Listed in the order I would build them. Everything here is already computed by the engine; the cost
is payload and layout, not simulation.

1. **Manifesto vs. current platform** — `LastManifesto` is already stored separately from
   `Platform`. Rendering "ran on X, now stands at Y" is a betrayal meter for free.
2. **Bloc support breakdown** — which voter groups back them. The affinity engine computes this
   every tick.
3. **Poll trend sparkline** — `agora.seats.pollTrend` is already reserved in the contract for M6.
4. **Coalition relations** — likely partners and refusals. `CoalitionMath` already scores this.
5. **Party history strip** — founded, split from, merged into, revivals, seats per election. Makes
   a long save feel like it has a past.
6. **Mandate scorecard** — promises made vs. kept while in government, from `MandateMonitor`.

---

## Backlog — from the codebase reviews

Not part of the five reported issues. Verified unless marked.

**Correctness**

- [ ] `FlavorPromptBuilder.cs:53` — prompt hard-truncated at 120k chars with the embedded JSON
      schema at the end, so a large city truncates mid-schema and every generation fails
      validation. Truncate the situation block; never the schema.
- [ ] `ClaudeResponseReader.cs:190` — the balanced-object scanner mishandles `\\` before a quote, so
      a slogan containing a backslash truncates the JSON.
- [ ] `AgoraRuntime.cs:538` — if `CollectProse` throws, `_lastAttemptDate` is never set and the
      status line misreports.

**Readability / affordance**

- [ ] `SeatsPanel.tsx:452` — seat count and vote percentage rendered adjacent with no labels;
      "25 / 45%" is ambiguous.
- [ ] `ArticleReader.tsx:69` — no loading state; a not-yet-fetched body is indistinguishable from
      an absent one.
- [ ] `SeatsPanel.tsx:399` — stability and cohesion meters give no indication which direction is
      good.
- [ ] `DistrictDetail.tsx:214` — bare `TIE-BREAK` badge with no explanation.
- [ ] `MandateTracker.tsx` — progress bar carries no inline percentage.
- [ ] *(unverified)* Scrollable regions may render no visible scrollbar in Gameface. Confirm
      against `cs2/ui`'s `Scrollable` before adding a CSS indicator.
- [ ] *(unverified)* `EventList.tsx:61` — check whether `humanizeEnum` output is genuinely
      player-legible for every `origin` value.

**Housekeeping**

- [ ] `docs/status.md` is stale by several milestones. Rewrite against reality.
- [ ] A contract-drift review across the C#/TS binding boundary came back clean. Treat that as weak
      evidence, not proof — re-run it after W4 and W6 add bindings, since that is when drift
      appears.

---

## Sequencing

| Phase | Work | Why here |
|---|---|---|
| 1 | **W0** | Blocks reliable testing of everything else, and is the only data-corrupting bug |
| 2 | **W2**, **W1** | Names and legibility — the two things wrong on every single frame |
| 3 | **W3** | Theme gates the electoral system, so it must land before the Parties tab renders seats |
| 4 | **W6**, then **W4** | Build the tab, then add editing inside it |
| 5 | **W5** | Largest surface; benefits from every earlier fix being in place |
| 6 | Backlog | |

Schema bumps cluster in phases 3–5. Batch them: **one** `/schema-change` pass covering per-save
settings (`ThemeLocked`, `PauseOnMajorNews`, `ShowAllReports`), `Party.PlayerOverrides`, and the
article length limits, rather than three separate sidecar migrations.

## Verification gate

Each workstream ships with a `dotnet test tests\Agora.Core.Tests\Agora.Core.Tests.csproj` pass and
a manual in-game walkthrough. The one that matters most:

> Load city A (EU). Play a year. Rename a party and recolour it. Quit to main menu. Create city B
> and choose US. Confirm: US-flavoured party names, no city A prose anywhere, effects ledger empty,
> heartbeat ticking on day one. Return to city A. Confirm the rename and the colour survived.

Nothing in this plan is complete until that walkthrough passes without restarting the game.
