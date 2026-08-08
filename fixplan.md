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

Five reported issues resolve into **seven workstreams** (W0–W6). Two of the reported issues (party naming,
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

- [x] Set `_flavor.Pool.Roster` at state-mint time, in `OnSidecarLoaded` immediately after
      `RebuildFlavor()`. **Done** as `AgoraRuntime.SeedFlavorRoster`, also called before each
      `CollectProse` so a party founded mid-game is in the roster the same month. `FillBriefs` needed
      no extraction — it was already a standalone method and only wanted a second call site.
- [x] After any prose collection, sweep `_state.Parties` for an empty `Name` and fill from the pool
      synchronously. **Done** as `EnsureEveryPartyNamed`, called after `Replay(...)` on load (not in
      the mint branch — replay itself can found parties) and after `ApplyProseNames` each month.
- [x] **Reframed.** The stated test — same GUID + same date ⇒ identical names — is tautological and
      already passed. The real defect, which this plan missed: `StaticPoolProvider` seeded each name
      on the *request date*, so **every party was renamed every sim month**. Names now seed on the
      party's `FoundedDate`, and the test that matters is same GUID + *different* dates ⇒ identical
      names. See "names lock in" below.
- [x] ~~Fix cache re-validation to use the union of the current catalog and the previously-seen id
      set.~~ **Struck — false premise.** `RebuildFlavor()` runs *after* the state mint, so
      `BuildFlavorCatalog()` already holds every party and faction id; and `FilterAgainstCatalog`
      drops per-entry rather than failing the document. There is no "fresh save discards its own
      cache" bug of the kind described. Replaced by a diagnostic log of the four catalog counts and a
      `_lastSnapshot` fallback for a load that races the sensors.
- [x] Never render a raw id to the player. **Done** across six sites in Seats, Districts and News.
      Placeholder is **"Unnamed party"**, not "Unnamed list" — "list" is proportional-representation
      vocabulary and reads wrong on a US-theme save, and the theme is not readable from the UI until
      W3.

**Also fixed, beyond the plan:** names now *lock*. `ApplyProseNames` writes `Name`/`ShortName` only
when the current name is empty or the id is provisional (an in-memory set, cleared by
`ResetForNewSave`, holding parties named by the canned pool). A canned name is a stopgap that one
later CLI response may upgrade, exactly once; after that nothing renames a party. `Description` and
`Slogan` keep refreshing — they are prose, not identity.

**Review found one blocking defect, fixed:** the sweep originally generated over *only* the unnamed
parties, but the pool's uniqueness set is per-`Generate`-call, so a subset draw could take a name an
existing party already held — giving either an illegal rename or two parties permanently sharing a
name (low tens of percent over a long NA save; that vocabulary is 96 names). The sweep now always
generates over the full roster and writes only where the name is empty.

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

- [x] Publish `agora.state.settings` — already **reserved** in `docs/contracts/ui_bindings.md` §8
      for exactly this. Read/write per-save settings; sidecar-backed, never global (non-negotiable
      #10).
- [x] New modal `ui/src/shell/FirstRunDialog.tsx`, rendered through `cs2/ui`'s `DialogRenderer` /
      `Portal` (`ui/types/ui.d.ts:78-87, 562-571`). Two large flag choices, each with one line of
      consequence text:
      - **Europe** — proportional list seats, 4–7 parties, coalition governments, 3-year terms.
      - **United States** — first-past-the-post district races, a directly elected mayor, two
        dominant parties with internal factions, 4-year terms.
- [x] Fires once per save, when the sidecar loads with no prior state. Pause the sim while it is
      open (see W5 for the pause helper).
- [x] Add `ThemeLocked: bool` to per-save settings. Set it at the first election — before that the
      player may change their mind from the settings surface; after it the choice is history.
- [x] `System` (`ElectoralSystem`) must be re-derived when `Theme` changes, and the party registry
      regenerated if no election has yet been held.
- [x] Schema: per-save `AgoraSettings` gains `ThemeLocked`, `PauseOnMajorNews`, `ShowAllReports`.
      Bump `schemaVersion` and run `/schema-change` — sidecar migration included. **Landed in the
      batched pass** (`docs/plans/0001-batched-schema-change.md`), not separately.

### What this plan got wrong, and what W3 actually had to fix

- **"`System` must be *re-derived* when `Theme` changes" understates it. `System` was never derived
  from `Theme` on any path, ever.** `CreateInitialState` passed `Theme` to `PartyRegistry.GenerateInitial`
  but never touched `Settings.System`, which sat at its initialiser default `Proportional`. A save
  with `Theme = Na` would have run NA parties through a proportional election with 3-year terms and
  no mayor. A coder implementing only the change-path would have left the mint-path bug in place.
- **`SidecarStore.SaveSettings` had no caller anywhere in the repo.** Settings reached disk only as a
  side effect of `SaveState`, i.e. only when the player saved the game. W3 is its first caller;
  without that, a theme choice was lost to any crash.
- **"Fires once per save, when the sidecar loads with no prior state" is not a sufficient condition.**
  Settings resolve independently of state and fall back to `settings.json`, so "no prior state" and
  "the player has never chosen a theme" are different questions. W3 added a runtime-only
  `SettingsAreDefaults` signal rather than inferring it.
- **The pause design in W5 below is wrong and W3 did not use it.** `SimulationSystem.selectedSpeed`'s
  setter is a **no-op while the game is loading**, and the game re-sets speed once loading completes,
  so a one-shot write is silently discarded. W3 subscribes to the game's own refcounted
  `time.simulationPausedBarrier$` instead, which also makes "a closed dashboard leaves the player
  paused with no way back" structurally impossible — the restore is the game's code.
- **The settings surface had no home.** This plan says the player "may change their mind from the
  settings surface" without saying where that is. W3 built one in the shell; W5 needs the same one.
- **`isFirstRun` is published as its own binding**, not as a field on `SettingsPayload` — it is a
  one-shot lifecycle signal, not part of the persisted settings document.

**Two review-blocking defects were found and fixed, then re-reviewed.** A retheme deleted the flavor
cache *before* disposing the provider, but the CLI worker leaves `Running` before it writes the
cache — so the old theme's document was written back after the delete and read by the new provider,
and because every id in it still validates against the new catalog, nothing was filtered: **EU prose
restored verbatim onto US parties, silently.** And a full ECS sensor sweep was reachable from
`SetSetting` on the UI phase, contradicting a comment three lines above it.

---

## W4 — Player-owned party identity

**Decision (owner):** inline edit in the Parties tab; player edits stop the LLM from making changes.

### The defect

Does not exist. And flavor overwrites the prose fields on every successful generation, so a naive
rename would be silently reverted at the next flavor wake.

> **This section as first written was wrong in five verified places.** It is corrected below rather
> than rewritten, because the corrections are the useful part. Each was checked against source
> before the work started.
>
> 1. **`ApplyProseNames` is not "the single enforcement point" — it is one of four.** It never
>    writes `ColorHex` at all (colour has no flavor path; its only writers are in `Agora.Core`),
>    and W2 added a *second* flavor writer of all four prose fields, `EnsureEveryPartyNamed`.
>    Both write the name/short-name pair and the description/slogan pair, so there are four
>    enforcement points, not one.
> 2. **It does not overwrite unconditionally — not since W2.** Name and short name are already
>    gated by `mayRename`. Description and slogan *were* still unconditional, and that is the write
>    that actually breaks a player's text, on the very next wake.
> 3. **Short name ≤ 12 is not "because the seat chart depends on it."** Every label that renders a
>    short name ellipsises. The real constraint is `data/schemas/political_state.schema.json`'s own
>    `$defs.party.shortName.maxLength: 12` — exceeding it fails the schema the sidecar ships with,
>    which is a load-time failure. The limit stands at 12; only the justification changes.
> 4. **Name ≤ 60 is unsupported anywhere in this repo.** The flavor schema allows 80 and the canned
>    pool caps its own draws at 80. At 60 the generator could produce a name the player is then
>    forbidden to retype — the field would reject the text already sitting in it. **Ruling:
>    `NameMax = 80`.**
> 5. **A party split will not recolour a player-recoloured party.** Both `ColorHex` writes in
>    `PartyLifecycle` are inside `new Party { … }` initialisers, so they can only touch a brand-new
>    brand. Revival and merge mutate in place and touch no prose, colour or `PlayerOverrides`. All
>    four cases are now locked in by tests in `PartyLifecycleTests`, with the conclusion written
>    into a comment there so that nobody "fixes" a bug that is not present.
>
> **And one real hazard this section missed.** `PartyRegistry.IsColorTaken` compared with
> `string.CompareOrdinal` — case-sensitively — against an uppercase palette. A player typing
> `#c0392b` held a colour byte-different from but visually identical to the palette's `#C0392B`,
> so it never registered as taken and `AllocateColor` handed the identical-looking colour to the
> next splinter. Fixed by normalising every hex to uppercase at the C# boundary.

### The fix

- [x] Add `PlayerOverrides` to `Party` — a small flag set (`NameLocked`, `DescriptionLocked`,
      `ColorLocked`) rather than four booleans on the root, so it stays cheap to serialise.
      *Landed ahead of W4 in plan 0001, with migration and `PartyRegistry.Clone` support. The enum's
      doc comment is the authoritative mapping: `NameLocked` covers `Name` **and** `ShortName`;
      `DescriptionLocked` covers `Description` **and** `Slogan`; `ColorLocked` covers `ColorHex`.
      W4 therefore persists no new field and needed no further schema change.*
- [x] **All four** flavor write sites honour the locks, via one shared rule:
      `Agora.Core.Engine.Parties.PartyIdentity.ApplyFlavor`. Lifting the merge into `Agora.Core` is
      what makes it testable — `AgoraRuntime` cannot be loaded by the headless suite, so the rule
      deciding whether a player's rename survives was the one rule in the mod no test could reach.
- [x] New UI→C# bindings under `agora.parties` — **eight, not three**: `rename`, `setDescription`,
      `setColor`, `resetName`, `resetDescription`, `resetColor`, plus two *read* bindings this
      section never mentioned. `colorPalette` publishes the tuning palette, without which the
      picker cannot render the swatches the engine assigns from; `editLimits` publishes the four
      limits and the colour pattern, without which the character counter and the C# rejector are
      two copies of one number that drift silently. `CallBinding`, not `TriggerBinding`, so a
      rejection reaches the player.
- [x] Validate on the C# side: name ≤ **80** (see correction 4), short name ≤ 12 (see correction 3),
      description ≤ 600, slogan ≤ 120, colour must be `#RRGGBB`. **Reject rather than truncate** — a
      silent fix-up is a write the player did not ask for, and the one time it matters (a name cut
      off mid-word) they would have no way to tell.
- [x] The **surface a colour picker needs**: the tuning palette published as
      `agora.parties.colorPalette`, a free hex accepted by `setColor`, and a colour another party
      already wears **accepted with a warning** rather than blocked. The warning crosses as
      `OkColorInUse`, which is an **acceptance**: it must not map to the empty string, or the panel
      cannot show it. **The picker itself is lane D and is not shipped — W6 owns it.**
- [x] The **reset bindings** — `resetName`, `resetDescription`, `resetColor` — each clearing its
      lock and handing the field back to flavor. **Resets are separate bindings and cannot fold into
      the setters**: an empty string is `ValueRequired`, never "reset". A cleared box — a slipped
      keystroke, a paste that did not take — is otherwise indistinguishable from a deliberate
      hand-back, and the two have opposite consequences. **The per-field reset control is lane D and
      is not shipped — W6 owns it.**
- [x] Record every binding in `docs/contracts/ui_bindings.md`, before any UI is written.
- [x] **Not in the original list:** the canned pool's de-duplication. `StaticPoolProvider` seeded
      its `usedNames` set only from its own draws and never read the roster's current names, so a
      newly-named party could land exactly on the name the player had chosen for theirs.

**Out of scope, reported as backlog:** factions have the same flavor-owned fields and no
`PlayerOverrides`. Giving them locks needs a second flags field and its own migration.

**Lane D (the UI controls) is deliberately not W4's.** They live in `PartyDetailHeader` inside the
Parties tab, which W6 owns; building them here would collide on merge. Specified and handed over.

### Manual gates — a human must run these

`AgoraRuntime` and `src/Agora.Mod/UiBindings/**` cannot be loaded by the headless suite, so the six
entry points, the gate locking and the eight binding registrations have **no automated coverage and
cannot be given any** without stubbing `AgoraRuntime`, which would test the stub. These checks are
the substitute. Numbers 5, 6 and 10 are the ones most likely to fail.

1. **Reads land on the first frame.** Load a save, open the binding inspector.
   `agora.parties.colorPalette` is `{colors: [...]}`, non-empty, uppercase, in the same order as
   `parties.colorPalette` in `data/engine_tuning.json`. `agora.parties.editLimits` reads
   `80 / 12 / 600 / 120` and `^#[0-9A-Fa-f]{6}$`.
2. **Roster carries prose.** Before any flavor has generated, every `PartyBrief.description` and
   `.slogan` is `""` — never `null`, never absent. After a prose pass they are populated.
3. **Rename round-trip.** `rename(id, "X", "Y")` returns `""`; the next roster publish shows the new
   name with `nameLocked: true`.
4. **Rejections.** Empty name → `"ValueRequired"`. An 81-character name → `"TooLong"`. An unknown
   party id → `"NotFound"`. Nothing is truncated into shape.
5. **The lock actually holds across a flavor wake.** Rename a party, then force a prose generation
   and let it succeed. The player's name must survive. **This is the whole point of the
   workstream**; if only one gate is run, run this one.
6. **The fourth enforcement point.** Set a description, then reset the *name* on the same party. The
   name re-rolls; the description must be untouched. This is the path that silently overwrote a
   locked description before W4.
7. **`OkColorInUse` is an acceptance.** `setColor` with a hex another party wears returns
   `"OkColorInUse"` **and the colour changes** on the next publish. The panel shows a warning, not a
   failure. If the colour did not change, the acceptance/refusal split is broken.
8. **Case normalisation.** `setColor(id, "#c0392b")` returns `""` and the roster comes back
   `"#C0392B"`.
9. **Resets are idempotent.** `resetName` on a never-renamed party returns `""` and changes nothing;
   twice in a row returns `""` both times.
10. **Per-save isolation.** Rename a party, return to the main menu, load a *different* save. The
    other city's parties must carry none of it — W0's reset seam is what makes this true and W4 adds
    new state to it.
11. **Disabled path.** With the master toggle off, none of the six calls mutates state.

### Lane D — the specification handed to W6

The controls belong in `PartyDetailHeader` inside the Parties tab. W4 ships the whole surface they
need; lane D is presentation only and must add no validation rule of its own.

- **Read the limits from `agora.parties.editLimits`, never from a literal.** That binding exists so
  the character counter and the C# rejector cannot disagree. When they do, the wrong one is always
  the counter: the player finds out by being refused after typing.
- **Read the swatches from `agora.parties.colorPalette`**, in the order published — never re-sorted,
  never de-duplicated. A swatch's position is how a player recognises it between sessions. Offer a
  free hex field beside them; the palette is not a closed set.
- **Test acceptance with `isAccepted`, never with a falsy check on the message.** `OkColorInUse` is
  an accepted write that carries a warning. A panel that reads "has a message" as "was rejected"
  will roll the swatch back to the old colour while the engine keeps the new one, and the two stay
  disagreed until the next republish.
- **Trim before sending.** The C# validators judge the raw input and deliberately do not trim, so
  `" My Party "` is stored with its padding. Trimming is the panel's job.
- **Send both fields of a pair.** `rename` carries the short name and `setDescription` carries the
  slogan. A description editor with no slogan field would take ownership of the slogan and then
  freeze it permanently, because flavor is barred from writing it from that moment.
- **Reset name and reset description behave differently, and the copy must say so.** Reset name
  visibly re-rolls the name on the spot. Reset description changes nothing visible — it hands the
  field back to flavor, which reclaims it at the next wake, possibly months of sim time away. Its
  effect is a promise about the future, and saying that plainly is cheaper than the support question.
- **A locked field is the player's words — never label it as generated.** The three lock booleans
  ride on `PartyBrief`.
- No new binding is needed. If lane D finds it wants one, that is a contract change and goes through
  `/schema-change` first, not a panel-side workaround.

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

- [x] `FlavorPromptBuilder.cs:53` — prompt hard-truncated at 120k chars with the embedded JSON
      schema at the end, so a large city truncates mid-schema and every generation fails
      validation. Truncate the situation block; never the schema. **Done:** only the situation
      block is now trimmed (`TruncateSituation`), against a budget computed from the fixed sections.
- [x] ~~`ClaudeResponseReader.cs:190` — the balanced-object scanner mishandles `\\` before a quote,
      so a slogan containing a backslash truncates the JSON.~~ **Confirmed false report.** Verified
      twice, most recently in the W0 review: the scanner handles an escaped backslash before a
      closing quote correctly, and `ClaudeResponseReaderTests` pins that case. Struck rather than
      deleted so it is not re-reported a third time.
- [x] `ClaudeResponseReader.cs:95` — **the real defect, in the same area.** The envelope unwrap hands
      the CLI's whole stdout to `FlavorJsonReader.Parse`, which rejects trailing content
      (`FlavorJsonReader.cs:81-85`). So any byte the CLI emits after the envelope object — a newline
      of diagnostics, a second JSON line — makes `Parse` return null, `UnwrapEnvelope` concludes
      "not an envelope", and `FirstBalancedObject` then extracts *the envelope itself* instead of the
      flavor document. That reaches the validator as a document full of unknown fields and is
      discarded, so the failure presents as a bad model response rather than as a parse seam that
      never unwrapped. Pre-existing; not fixed in W0. **Done:** envelope location moved into
      `FindEnvelope` — strict whole-text parse first, and only on failure a retry against the first
      balanced object alone. That ordering is load-bearing: parsing the span first would silently
      swallow genuinely glued documents, which is exactly what `Parse`'s trailing-content check
      exists to catch, so that check is untouched. Discrimination is still by a field only the
      *envelope* carries, so a truncated flavor document still cannot be misread as an envelope. A
      diagnostic-only guard in `ExtractFlavorJson` now fails closed with a seam-specific message
      rather than shipping an envelope to the validator, so the log tells the two failures apart.
      `FirstBalancedObject`'s escape handling was not touched. The reviewer reverted the fix and
      confirmed 5 of the 9 new tests fail against the pre-fix code.
- [x] `AgoraRuntime.cs:538` — if `CollectProse` throws, `_lastAttemptDate` is never set and the
      status line misreports. **Done:** `_lastAttemptDate` and the version bump moved into a
      `finally`, so any throw anywhere in the method still moves the status line.

**Readability / affordance**

- [x] `SeatsPanel.tsx:452` — seat count and vote percentage rendered adjacent with no labels;
      "25 / 45%" is ambiguous. **Done:** a `ChipHeader` names the two columns once at the top, at
      the same widths as the rows; the panel is too narrow to repeat units per row.
- [x] `ArticleReader.tsx:69` — no loading state; a not-yet-fetched body is indistinguishable from
      an absent one. **Done, differently than framed:** there is no not-yet-fetched state to render
      — a map binding resolves inside its own subscribe trigger, so the payload is always C#'s final
      answer for that id. The ambiguity was that an absent body rendered as a blank sheet; it now
      falls back to the feed row's summary, or says the full text is unavailable.
- [x] `SeatsPanel.tsx:399` — stability and cohesion meters give no indication which direction is
      good. **Done:** each meter carries a plain-English reading (Fragile / Shaky / Steady / Strong)
      alongside the number.
- [x] `DistrictDetail.tsx:214` — bare `TIE-BREAK` badge with no explanation. **Done:** reads
      "Too close to call - tie-break", with a tooltip saying the tie-break is seeded, not a coin flip.
- [x] `MandateTracker.tsx` — progress bar carries no inline percentage. **Done:** the percentage
      sits on the bar's own row, for progress and salience alike.
- [x] ~~*(unverified)* Scrollable regions may render no visible scrollbar in Gameface.~~
      **Verified false — struck, no change needed.** All five of Agora's scroll regions are `cs2/ui`
      `Scrollable` (`NewsPanel.tsx:202`, `:229`; `ArticleReader.tsx:61`; `DistrictList.tsx:34`;
      `DistrictsPanel.tsx:107`); there are no hand-rolled `overflow: auto` divs anywhere in `ui/src`.
      `Scrollable` renders its **own DOM track and thumb** — read directly out of the shipped bundle
      `Cities2_Data/Content/Game/UI/index.js` (component `iT`) — defaulting to
      `trackVisibility: "scrollable"`, so the track fades in exactly when the content overflows,
      measured by the component's own overflow observer. It is styled by the game's global
      `index.css` (`.track_e3O` / `.thumb_Cib`: a 4rem rail in a 16rem gutter, 60% opacity, with
      hover and pressed states, and the content auto-padded so text never sits under it). The
      consumer's `className` is **appended** via `classnames`, never substituted, so a panel class
      cannot suppress the track. A `::-webkit-scrollbar` rule would have styled nothing — the game
      never uses a native scrollbar, which is precisely why CO drew one in DOM. The item appears to
      have been written from a general Gameface intuition rather than an observation.
      *(Noted, not acted on: Agora sets no `--scrollbarColor`, so the thumb inherits the enclosing
      game panel's. If a future visual pass finds it low-contrast on a dark Agora panel, the fix is
      one line on the `.scroll` class. Hypothetical polish, not this defect.)*
- [x] **Found during W2.** "Never render a raw id" is broader than parties, and two non-party leaks
      remain: `ui/src/panels/News/lookup.ts:74` — `districtLabel` falls back to a district id; and
      `ui/src/panels/News/EventList.tsx:104` — `{event.title || event.archetypeId || event.id}` puts
      an event archetype or instance id in a rendered title. Each needs its own placeholder wording,
      so neither was folded into W2. **Done, with the two wordings decided separately.**
      `districtLabel` now distinguishes **three** states, not two: "Citywide" (no district id),
      "Unknown district" (an id absent from the list — deleted, or the binding has not landed) and
      "Unnamed district" (present in the list, empty name). That is deliberately **asymmetric with
      `partyLabel` in the same module**, which collapses its equivalent pair: an unnamed district is
      something the player can go and fix, where no party state is. The module header says so, to
      stop it being harmonised later.
      For the event title, mapping `archetypeId` to English was considered and **rejected** —
      `ProceduralEventGenerator` sets `Title` and `ArchetypeId` from the same archetype object, so a
      map would only reproduce `title`, and the pool is an injectable parameter so the map would
      silently fall behind. Catalog events are title-validated (`CatalogIssueCode.MissingTitle` drops
      the entry) and carry `ArchetypeId = ""`, so that branch was a pure leak carrying no
      information. Falls back to "Untitled event".
- [x] *(was unverified)* `EventList.tsx:61` — check whether `humanizeEnum` output is genuinely
      player-legible for every `origin` value. **Confirmed a real defect and fixed:** `humanizeEnum`
      splits on inner capitals, which mangles the `origin` values; an explicit `ORIGIN_LABEL` map
      now supplies the English, falling back to the raw value for an origin it has not been taught.

**Housekeeping**

- [x] `docs/status.md` is stale by several milestones. Rewrite against reality. **Done:** rewritten
      2026-08-08 against artifacts in the tree, with a per-milestone split between "code landed" and
      "gate re-walked" rather than one claim standing for both.
- [x] A contract-drift review across the C#/TS binding boundary came back clean. Treat that as weak
      evidence, not proof — re-run it after W4 and W6 add bindings, since that is when drift
      appears. **Partly done — and "came back clean" was too generous.** A field-by-field re-run
      over all **26** registered `agora.*` names confirmed the *shapes* are clean: 20 payload types
      match name-for-name and type-for-type against `bindings.d.ts` (checked against each
      `IJsonWritable.Write` body, not the C# property names), all 15 enum vocabularies match, and no
      name bound in `ui/src` is missing from C# — the phantom-binding class of defect does not exist
      in this tree. The `CommandOutcome` wire form was specifically checked and is **correct on both
      sides**: `CommandOutcomes.ToWire` maps `Ok` to `""`, `CommandOutcomeName` leads with `""`, and
      `bindings.ts`'s `writeMessage` tests `outcome === ""` explicitly rather than by truthiness.
      **Three defects were found in the prose and fixed** — the `CrosstabCell.turnout` claim (see the
      Crosstab note below), a contract row naming a C# type (`AgoraStateSummary`) that does not
      exist, and §1 miscounting its own areas as five when the table below lists six.
      **Still open, escalated as an owner decision** (see "Decisions for the owner"): five
      `NewsArticle` wire fields with no engine source, and whether the Crosstab's Turnout mode should
      exist. **The full re-run still has to happen after W4 and W6 merge** — neither is in this tree,
      and adding bindings is exactly when drift appears.

---

## Decisions for the owner — raised by the backlog pass, not implemented

Neither is a defect fix; both are choices, so nothing was done to the code.

**1. Five `NewsArticle` wire fields have no engine source.** `byline`, `tags`, `partyId`,
`districtId` and `eventId` are declared in `bindings.d.ts`, documented in `ui_bindings.md`, and
*emitted* by the writer — but `AgoraUiProjection.BuildArticle` never assigns them, because
`Agora.Core.Contracts.Article` has exactly five properties (`Id`, `Outlet`, `Headline`, `Body`,
`Tone`). They cross as `""`/`[]` on every fetch, permanently. Consequences today:
`ArticleReader.tsx`'s byline and tag branches are dead code, and `ui_bindings.md` publishes a
**contractual sort key on `article.tags`** — a sort order over a list that is never non-empty.
Either populate them (W5 territory — a byline is exactly the masthead detail W5 wants, and the three
id links are what W5's "require `refs` to be populated" is asking the model for) or strike them from
all three artifacts. Left alone pending your call, since W5 may well want them.

**2. Should the Crosstab's Turnout mode exist?** Its copy now tells the truth, but the mode still
paints all fifteen cells the identical value and the identical tint. A flat wash in a heat grid
reads as a real measurement that happens to be uniform — a different false claim from the one just
removed. Worse, the corrected note renders only in the `readoutHint` branch, so it disappears the
moment the player clicks a cell to investigate. The alternative is to drop the mode and show the
figure once as a scope-level line above the grid, which makes "one number for the whole area"
structural rather than a caption. That is a layout change, so it is yours. Both the coder and the
reviewer read it the same way; I agree, but did not act.

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
