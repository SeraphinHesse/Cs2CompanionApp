# AGORA — Status

**Current milestone:** M6 · The Spectacle (in progress) — with a **fix-plan pass** (`fixplan.md`)
running ahead of it against defects found in the first real play session.
**Updated:** 2026-08-09

> This file was stale by several milestones until 2026-08-08, and this pass refreshes it again for
> everything that landed on `main` since: W3's fixed/re-reviewed defects, W4 lanes A–C, W5's
> prose+model lane, the backlog, and W6 chunks A–F. Statuses below are keyed to artifacts that exist
> in the tree; where a milestone's **gate** has not been formally re-walked since the code landed, it
> says so rather than claiming a pass. **W6 chunk G is in progress on a live worktree as of this
> update** — the W6 row and the M6 milestone row will need one more pass once it and chunk H land.

---

## Where the build actually is

The mod **deploys, loads in-game, ticks the heartbeat, and renders three dashboard panels**
(`council`, `districts`, `news` — see `TAB_ORDER` in `ui/src/shell/state.ts:21`). The engine,
elections, government, flavor and effects layers are all implemented in `Agora.Core` /
`Agora.Mod`.

| Milestone | Code | Gate |
|---|---|---|
| **M0 · Bootstrap** | ✅ | ✅ **passed 2026-07-30** (see `politicsmodplan.md` §11) |
| **M1 · Time & Truth** | ✅ `AgoraTimeService`, `AgoraStartYearSystem`, `StartYearDelivery`, `SimClockMath`, sensors, sidecar IO | ⚠️ save→quit→load ×10 desync check not re-walked since W0's per-save bug was found |
| **M2 · The Engine** | ✅ blocs, affinity, turnout, parties, factions, polling, indices, dashboard | ⚠️ not re-walked |
| **M3 · The Voice** | ✅ `IFlavorProvider`, `ClaudeCliProvider`, `LayeredFlavorProvider`, static pool fallback, prompt builder, schema validation, flavor cache | ⚠️ fail-closed path implemented; **prose quality is a known defect** — see W2/W5 |
| **M4a · Elections** | ✅ `Engine/Elections/Proportional` + `Fptp`, polling, manifestos | ⚠️ not re-walked |
| **M4b · Government** | ✅ `Engine/Government/Coalitions` + `Mandates`, party lifecycle | ⚠️ not re-walked |
| **M5 · The World** | ✅ effect palette + dispatcher + resolver + schedule + validation; `Agora.Mod/Effects` ledger and application system; `data/timeline_eu.json`, `timeline_na.json`, `timeline_global.json` | ⚠️ 1990→2008 run not re-walked |
| **M6 · The Spectacle** | 🟡 partial — crosstab explorer, mandate tracker, news archive present; **political map overlay and election-night broadcast mode not built** | ⬜ |

**Test suite.** `tests/Agora.Core.Tests` is at **1227 tests** as of W6 chunk H, up from 1033 at the
start of this pass, spanning determinism, blocs, affinity, turnout,
polling, indices, both electoral systems, coalitions, mandates, factions, party lifecycle, the
effect palette and application, the per-save reset seam, the scheduler, sim-clock math, start-year
planning, the shipped timeline/tuning catalogs, party identity locks, and the LLM response path —
the CLI reader, the prompt builder, and the schema/numeric validation that enforces
non-negotiable #1. It still runs with **no copy of the game installed** — that constraint is the
test that the Core/Mod split is real.

Build: `dotnet build Agora.sln` · UI: `cd ui && npm run build` ·
Test: `dotnet test tests\Agora.Core.Tests\Agora.Core.Tests.csproj`

---

## Active work — `fixplan.md`

The first play session against a loaded city produced five reported issues, which resolved into
seven workstreams — two of the five were several independent bugs each. `fixplan.md` is the
authority; this is the tracker.

| WS | What | Phase | Status |
|---|---|---|---|
| **W0** | Per-save reset seam — three layers retain the previous city's state across a main-menu round trip. The only *data-corrupting* bug of the seven. | 1 | ✅ code complete, review passed, **merged to `main`** · **ECS half needs the manual walkthrough** |
| **W1** | Readability — four panels each declare their own opacity, lowest 0.62. Shared `_tokens.scss`. | 2 | ✅ code complete, review passed, **merged to `main`** |
| **W2** | Party names lock in — flavor roster is never set before the first prose poll, so parties render as `party-01`. | 2 | ✅ code complete, review passed (one blocking defect found and fixed), **merged to `main`** · **needs the manual walkthrough** |
| **W3** | EU/US theme chosen by the player — `RegionTheme` has no selection surface; always defaults to `Eu`. First-run flag dialog. | 3 | ✅ code complete, review passed (two blocking defects found and fixed, then re-reviewed), **merged to `main`** · **needs the manual walkthrough** |
| **W6** | Parties tab — panel does not exist; `PartyBriefPayload` lacks the fields. | 4 | 🟡 **chunks A–H all merged to `main`** (bindings, tab shell, manifesto/drift, poll trend, history strip, mandate scorecard, coalition relations) · **H1–H8 reviewed and approved; H9's review is outstanding** |
| **W4** | Player-owned party identity — inline rename/recolour, with locks that stop flavor clobbering them. | 4 | ✅ **lanes A–C code complete, reviewed, merged to `main`** · **lane D (the UI controls) handed to W6** — they live in `PartyDetailHeader` inside the Parties tab, not started |
| **W5** | The press — articles lead with what happened, masthead popup, sim pause, Haiku for cost. | 5 | 🟡 **prose + model lane complete, reviewed, merged to `main`. Popup lane NOT STARTED.** See below. |
| — | Backlog (correctness + affordance) | 6 | ✅ **all items closed, reviewed, merged to `main`** — envelope unwrap fixed, two raw-id leaks fixed, scrollbar item struck as verified-false, contract drift audited (3 prose defects fixed) · **both owner decisions now resolved** (see below), and the drift re-run must repeat after W6 fully lands |

### W5 — what shipped, and what did not

**Shipped, reviewed, committed** (branch `worktree-agent-a1c4d1450a9355a73`, 4 commits on top of the
inherited pair): article `refs` cross the Core boundary and render as chips; the article instruction
leads with what happened and bans unattributed sourcing; election coverage asks for a party's own
claim and own challenge rather than a winner's and a loser's reaction; the canned pool was rewritten
against the same rule with new election templates and now carries `refs` on every article, which is
what allowed `FilterAgainstCatalog` to start dropping refless ones; `--model` with the alias
`claude-haiku-4-5`; and `byline`/`tags` struck from the article contract.

**Not started: the entire popup lane.** No alert emission, no bindings, no modal, no pause wiring,
no first-run interlock. `PauseOnMajorNews` and `ShowAllReports` remain **two switches that do
nothing**, with hint text promising behaviour that does not exist — that is the most visible loose
end. Three prerequisites are known-missing and are written up in `fixplan.md` §W5: there is no
severity filter anywhere, coalition *formed* produces no feed row, and party founded/dissolved has
neither a feed row nor a tick signal.

**Manual gates outstanding for the shipped half** — none of these can be verified without the game:
prose quality on a real save; the fail-closed path with a bogus `AGORA_CLAUDE_MODEL`; that
`ClaudeCliProvider`'s `ArticlesAllDiscarded` branch keeps last-good rather than blanking the feed
(the branch is untested by construction — the type is game-facing and deliberately unlinked from the
test suite); and that an existing save's `flavor_cache.json` full of refless articles degrades to
canned prose rather than an empty feed.

**W5 deviates from the ratified article count, deliberately.** §11 M3 ratifies 3–5 articles per
wake; an election wake asks for 7 (NA) or 8 (EU) — the ordinary 4 plus one slot per dedicated
election piece — because W5's "elections covered extensively" decision would otherwise buy the
election coverage by cutting general coverage below an ordinary month. Recorded in `politicsmodplan.md`
§11 M3. The extra tokens land on election months only, and elections are 3–4 years apart.

**Phase 1 is code complete and through the checklist gate** (`dotnet build` 0/0 · 1033 tests ·
`npm run check` clean). Nothing is committed. Four review-blocking defects were found and fixed, and
the review passes corrected **eight** places where `fixplan.md` describes code that does not exist —
see `docs/plans/0001-batched-schema-change.md` §9 and `docs/plans/0002-w6-parties-tab.md` for the
list. Two of those change work not yet started: W4's stated enforcement point never writes
`ColorHex`, and W5's article-limit tightening would discard every cached party name unless the cache
load prunes over-length articles only.

**The backlog is closed** (2026-08-08, on a branch off `163e6f2`, four commits, each independently
reviewed against the checklist). `dotnet build Agora.sln` 0/0 · **1092 tests** (was 1083; +9, all for
the envelope unwrap) · `npx tsc --noEmit` and `npm run check` clean. Four items:

- **`ClaudeResponseReader` envelope unwrap** — the one real correctness bug left, and it was
  mislabelling itself. Any byte the CLI emitted after the envelope object made the strict parse
  reject it as trailing content, so the unwrap concluded "not an envelope" and the balanced-object
  scan then extracted *the envelope itself*, which reached the validator as unknown fields. A parse
  seam presented to the player as a bad model response. The reviewer reverted the fix and confirmed
  5 of the 9 new tests fail against the pre-fix code.
- **Two raw-id leaks** in News, closing out W2's "never render a raw id" rule.
- **The Gameface scrollbar item — verified false and struck.** `cs2/ui`'s `Scrollable` draws its own
  DOM track and thumb, styled by the game's global CSS, and appends rather than replaces a
  consumer's `className`. Evidence read out of the shipped `index.js`/`index.css`. Cheaper than the
  speculative CSS indicator the item asked for, and the item appears to have been written from a
  general Gameface intuition rather than an observation.
- **Contract-drift audit** over all 26 `agora.*` bindings. Shapes clean; three defects in the prose,
  fixed. **This must be re-run after W4 and W6 merge** — the plan is right that adding bindings is
  when drift appears, and neither workstream was in the tree for this pass.

Two owner decisions came out of it, both recorded in `fixplan.md` § "Decisions for the owner", and
**both are now resolved (2026-08-09):**

- **`NewsArticle` wire fields with no engine source** — `byline` and `tags` were the two that were
  never populated by any layer (`""` and `[]` on every article, permanently) and are now **struck**
  from the payload, the TS type, `ArticleReader`, and the contract doc. The other three id fields
  (`refs` → `EventId`/`DistrictId`/`PartyId`) were kept and populated; they were already
  catalog-validated and in active use.
- **Crosstab's Turnout mode** — **struck.** Both the coder and reviewer who built it found it
  rendered fifteen visually identical tints with real data, conveying no information. Turnout is
  already readable in two other places that are unaffected by this: the district list row text
  (`DistrictList.tsx`) and the district detail Conditions meter + no-data fallback line
  (`DistrictDetail.tsx`). Routed to whichever lane owns `Crosstab.tsx` to remove the mode from the
  selector and its related state, reviewed like any other change.

## W4 — player-owned party identity

**Lanes A–C are code complete and independently reviewed. Lane D is specified and handed to W6.**

The player can own a party's name and short name, its description and slogan, and its colour. Each
of the three groups is a lock in `Party.PlayerOverrides`, and a set lock bars flavor from that group
for good.

**The enforcement point is one function, and it lives in `Agora.Core`.**
`PartyIdentity.ApplyFlavor` is the lock-aware merge, lifted out of `AgoraRuntime.ApplyProseNames`.
That move is the substance of the work rather than tidying: `AgoraRuntime` cannot be loaded by the
headless suite, so the rule deciding whether a player's rename survives a flavor wake was the one
rule in the mod no test could reach. It is now the rule the mod runs *and* the rule the suite tests.

`fixplan.md` §W4 called `ApplyProseNames` "the single enforcement point". It is one of four —
W2 added a second flavor writer of all four prose fields, `EnsureEveryPartyNamed`, and colour has no
flavor path at all. The fourth was a latent bug that "reset name" would have made live: lock the
description, reset the *name*, and `EnsureEveryPartyNamed` fires on the now-empty name and silently
overwrites the locked description. All five corrections to §W4 are recorded there in full.

**Two bugs fixed that the plan did not know about.** `PartyRegistry.IsColorTaken` compared hex
case-sensitively against an uppercase palette, so a player typing `#c0392b` held a colour that never
registered as taken and the next splinter was handed the identical-looking `#C0392B`. And
`StaticPoolProvider` seeded its uniqueness set only from its own draws, so a newly-named party could
land exactly on the name the player had chosen.

**Eight bindings, not the three the plan listed.** Six writes (`rename`, `setDescription`,
`setColor`, `resetName`, `resetDescription`, `resetColor`) and two reads the plan never mentioned:
`colorPalette`, without which a picker cannot render the swatches the engine assigns from, and
`editLimits`, without which the character counter and the C# rejector are two copies of one number.
Resets are separate bindings because an empty string is `ValueRequired`, never a reset — a cleared
box is a slipped keystroke as often as it is an intention, and the two mean opposite things.
`CommandOutcome` gained four members: `NotFound`, `ValueRequired`, `TooLong` and `OkColorInUse`.
**`OkColorInUse` is an acceptance**, so it does not cross as `""`; both sides now test acceptance
with an `IsAccepted`/`isAccepted` helper rather than against the empty string.

**W4 persists no new field and needed no schema change.** `PlayerOverrides` shipped ahead of it in
plan 0001, with migration.

Three review rounds. The first, over the inherited work, found four blocking defects. The two
sharpest: `PartyBrief` declared `description`/`slogan` in TypeScript and in the contract while the C#
publisher emitted neither — a one-sided schema change that type-checks and hands the panel
`undefined` at runtime — and the UI's outcome map had never learned the four new codes, so an
accepted duplicate colour reached the player as an unexplained failure. The second round, after the
fixes, blocked on two `fixplan.md` checkboxes ticked for UI controls that are lane D and did not
ship; a ticked box hiding unshipped work is precisely what that section is a correction of.

**Not covered by tests, by necessity:** `AgoraRuntime` and `src/Agora.Mod/UiBindings/**` are not
linkable into the headless suite, so the six entry points, the gate locking and the eight binding
registrations have manual gates instead — listed at the end of `fixplan.md` §W4. Nothing was stubbed
to manufacture coverage.

**Out of scope, backlog:** factions have the same flavor-owned fields and no `PlayerOverrides`.
Giving them locks needs a second flags field and its own migration.

**Schema bumps are batched, and the batch has landed.** `docs/plans/0001-batched-schema-change.md`
is complete and reviewed across all three chunks: per-save settings (`ThemeLocked`,
`PauseOnMajorNews`, `ShowAllReports`), `Party.PlayerOverrides`, and the article length limits, in
one sidecar migration rather than three. Sidecar state and settings are now **schemaVersion 2**,
`politics_flavor` is **2**, and the binding contract is **3**.

Two defects in the migration engine were found and fixed that nothing in `fixplan.md` anticipated:
`SidecarSchema.Migrate` stamped the *target* version on an unversioned document without running a
single step — silent, unrepairable data loss the moment a step existed — and the `settings` block
nested inside a state file was never reachable by the settings step table at all. A third, caught in
review, was a one-sided bump of `CurrentFlavorCacheVersion` ahead of the schema it versions.

The article tightening (headline 140→90, body 900→420) ships with `FlavorCacheMigration`, which
prunes only over-length articles at cache load and never truncates. Without it the first reload
after the update would have discarded every cached party name and resurrected the `party-01` bug W2
exists to fix — a consequence `fixplan.md` did not mention.

### The walkthrough that gates the fix plan

> Load city A (EU). Play a year. Rename a party and recolour it. Quit to main menu. Create city B
> and choose US. Confirm: US-flavoured party names, no city A prose anywhere, effects ledger empty,
> heartbeat ticking on day one. Return to city A. Confirm the rename and the colour survived.

Nothing in `fixplan.md` is complete until that passes **without restarting the game**.

---

## Known gaps found this pass, not yet closed

1. **The test suite is insensitive to coalition majority-iteration order.** Found by W6 chunk H's
   review, which injected `majority.Reverse();` after `MajorityOf(candidates)` in
   `CoalitionFormation.Form` and watched **all 1227 tests still pass**. Chunk H's `RankOf` refactor
   was proved correct by a 3000-chamber differential diff against the pre-refactor implementation,
   not by the suite — so the suite would not have caught it had it been wrong. Closing this needs a
   fixture where two majority candidates both have cohesion below 1.0 and the seed makes the first
   walk out, so a reordering changes which government forms. Cheap, and it guards the argument the
   whole refactor rests on.
2. **`ui/types/bindings.d.ts:3652` still says "schemaVersion 5"** in its authority comment while
   `docs/contracts/ui_bindings.md:3` is at **7**. Contract drift in the mirror rather than in a
   payload, but it is exactly what the drift audit exists to catch. Fold into the re-run.

## Blocked / needs a decision

1. **M6 scope.** The political map overlay and election-night broadcast mode are the two remaining
   M6 tasks and neither is started. The overlay's fallback (a stylized district map inside the
   dashboard) has not been chosen against yet.
2. ~~**W6 additional content**~~ — **decided 2026-08-08.** Five of the six are in: manifesto-vs-platform,
   poll trend sparkline, coalition relations, party history strip, mandate scorecard. Bloc support
   breakdown declined. Coalition relations uses the **live-ranking** design (a public RNG-free
   `RankCandidates` in `Agora.Core`) — no schema change, no save growth; `fixplan.md:322`'s claim
   that it was "already computed" was wrong. See `docs/plans/0002-w6-parties-tab.md` §H0.
3. **Effect palette rescope.** Scout 0001 §3 found no enum support for RCI demand, rent/land value,
   birth rate, or subsidies, and district scope has only 14 modifiers. The palette shipped against
   that gap list; `politicsmodplan.md` §7 still reflects the pre-rescope intent.
4. **`politicsmodplan.md` §14 open decisions** remain open: NA primaries, timeline jitter, snapshot
   retention, post-2026 authorship, unrest ceiling.

---

## Where to look when something breaks

`Colossal.Logging` gives every logger its own file, so Agora's output does **not** go to
`Player.log` — grepping that file for "Agora" returns nothing even on a healthy run. Ours is:

```
%USERPROFILE%\AppData\LocalLow\Colossal Order\Cities Skylines II\Logs\Agora.log
```

`Logs\Modding.log` is the other one worth reading: assembly load times, UI module registration, and
dispose. A mod that fails to load says so there, not in `Agora.log`.

Run `.\tools\verify-setup.ps1 -Build` for the current state of all build preconditions.

## Known toolchain quirks (all worked around; see `docs/scout/0002-modding-toolchain.md`)

- `ModPostProcessor.exe` / `ModPublisher.exe` target **.NET 6, which is EOL and not installed here.**
  `Agora.Mod.csproj` overrides both targets to pass `DOTNET_ROLL_FORWARD=LatestMajor` scoped to the
  `Exec`. Re-sync those overrides if a toolchain update changes them.
- **`Agora.Core` is pinned to netstandard2.0** because toolchain mode builds `Agora.Mod` as `net48`,
  which cannot reference netstandard2.1.
- `CSII_LOCALMODSPATH` is set before the folder exists. Never gate a build step on that folder
  existing — it will skip silently forever.
- A shell opened **before** the toolchain install sees no `CSII_*` variables. `Mod.props` dodges this
  by reading the registry directly; our own scripts check both.
- Gameface has **no `backdrop-filter`** — panel opacity is the only legibility lever (W1).
- **`npm run check` is misnamed and checks less than it sounds like it does.** `ui/package.json:8`
  maps it to `node tools/css-presence.js`, whose standalone entry point
  (`ui/tools/css-presence.js:158-170`) runs **only the design-token guard**. It does *not* diff CSS
  class names against the `.tsx` that reference them — `CSSPresencePlugin` is a webpack `hasCSS`
  injector, not a parity check. **And neither `npm run check` nor `npm run build` typechecks**;
  webpack is transpile-only. A green `check` + `build` is therefore *not* evidence of either class
  parity or type safety. Run **`npx tsc --noEmit`** separately, and diff class names by hand in
  review. Found during W6 chunk G's review, 2026-08-09.
- **`npm run build` deploys.** It writes into the player's live `…\Mods\Agora.Mod` folder, and
  `dotnet build Agora.sln` triggers it too once `node_modules` is installed. Use
  `dotnet build src/Agora.Mod/Agora.Mod.csproj -p:UseCsiiToolchain=false` for a compile check that
  does not clobber the deployed mod mid-session.
