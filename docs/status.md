# AGORA — Status

**Current milestone:** M6 · The Spectacle (in progress) — with a **fix-plan pass** (`fixplan.md`)
running ahead of it against defects found in the first real play session.
**Updated:** 2026-08-09

> **The fix-plan pass is code complete across all seven workstreams.** W0–W6 and the backlog are
> merged and independently reviewed; W5's popup lane, the largest remaining piece, was planned in
> `docs/plans/0003-w5-popup-lane.md` and executed across six chunks. Statuses below are keyed to
> artifacts that exist in the tree; where a **gate** has not been re-walked since the code landed, it
> says so rather than claiming a pass.
>
> **What remains is the manual gate, and only the player can walk it** — see "The manual gate" below.
> A green build is not a passed gate: much of this pass is reachable only through `Unity.Entities` /
> `Game.*` and so has manual gates rather than tests, by design rather than by omission.

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
| **M4a · Elections** | ✅ `Engine/Elections/Proportional` + `Fptp`, polling, manifestos, fringe ceiling (packet 15) | ⚠️ not re-walked |
| **M4b · Government** | ✅ `Engine/Government/Coalitions` + `Mandates`, party lifecycle | ⚠️ not re-walked |
| **M5 · The World** | ✅ effect palette + dispatcher + resolver + schedule + validation; `Agora.Mod/Effects` ledger and application system; `data/timeline_eu.json`, `timeline_na.json`, `timeline_global.json` | ⚠️ 1990→2008 run not re-walked |
| **M6 · The Spectacle** | 🟡 partial — crosstab explorer, mandate tracker, news archive present; **political map overlay and election-night broadcast mode not built** | ⬜ |

**Test suite.** `tests/Agora.Core.Tests` is at **1319 tests**, up from 1033 at the
start of this pass, spanning determinism, blocs, affinity, turnout,
polling, indices, both electoral systems, coalitions, mandates, factions, party lifecycle,
the fringe ceiling, the
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
| **W6** | Parties tab — panel does not exist; `PartyBriefPayload` lacks the fields. | 4 | 🟡 **chunks A–H all merged to `main`** (bindings, tab shell, manifesto/drift, poll trend, history strip, mandate scorecard, coalition relations) · **every chunk reviewed and approved**, H9 after one blocking fix |
| **W4** | Player-owned party identity — inline rename/recolour, with locks that stop flavor clobbering them. | 4 | ✅ **complete.** Lanes A–C and **lane D** all code complete, reviewed, merged · lane D's five text fields are the **first text entry anywhere in `ui/src`** and carry a real manual gate (see below) |
| **W5** | The press — articles lead with what happened, masthead popup, sim pause, Haiku for cost. | 5 | ✅ **code complete.** Prose + model lane, and the whole popup lane (`docs/plans/0003`): C1 binding surface, C3/C4 the two missing feed rows, C2/C5 severity gate + ring + emission, C6/C7 modal + pause + interlock + masthead, C8 join. `PauseOnMajorNews` and `ShowAllReports` now do something. **C0's in-game spike was deliberately not built** — folded into the manual gate · **needs the walkthrough** |
| — | Backlog (correctness + affordance) | 6 | ✅ **all items closed, reviewed, merged to `main`** — envelope unwrap fixed, two raw-id leaks fixed, scrollbar item struck as verified-false, contract drift audited (3 prose defects fixed) · **both owner decisions now resolved** (see below), **the drift re-run is done** (2026-08-09, 44 bindings, shapes clean, six prose defects fixed) |

### The manual gate — what only the player can verify

Everything below needs the game running. Nothing here has been seen on screen: the code is reviewed,
built and typechecked, and that is a different claim. **Item E is the one exception, and only in
part** — its table was read off a real save's sidecar and log, which is evidence about engine state
and says nothing about what rendered.

**A. The C0 questions** (the de-risk spike that was deliberately not built — answer these first,
because a "no" means C6's ack path needs revising, which is a one-line fix by construction):
1. Does an alert card **disappear** when Dismiss is pressed? This proves the ack → `_stateVersion` →
   `Publish` round trip. `AgoraUISystemBase.OnUpdate:79-82` gates publishing on `StateVersion`, and
   `AgoraRuntime.AckAlert` bumps it with a comment forbidding the line's removal.
2. Does the clock stop while a **major** card is up, and **return to the prior speed** when it
   closes? An article card must **never** stop the clock, even with both settings on.
3. On a first-run save, does the article modal stay out of the way until the region prompt is
   dismissed? Two pause barriers must coexist and the clock resume only after **both** are gone.

**B. Text entry — the highest-risk unverified area.** W4 lane D's five fields are the first
`<input>`/`<textarea>` anywhere in `ui/src`; `cs2/ui` exports no text-input component. Beyond "do
characters appear": **focus a field and press space, digits, `b`, `p`** — keys bound to game hotkeys
— and confirm the sim does not pause, change speed, or open bulldoze. Nothing in the component stops
key propagation, because there was no pattern in the repo to copy. `<textarea>` is the higher risk.
Then: type past the published limit (counter reddens, engine returns the `TooLong` sentence); pick a
colour another party already wears (amber "already wears this colour", **and the swatch keeps the
new colour** — that is `OkColorInUse` being read as an acceptance).

**C. The fix plan's own walkthrough**, unchanged and still the gate on everything:
> Load city A (EU). Play a year. Rename a party and recolour it. Quit to main menu. Create city B
> and choose US. Confirm: US-flavoured party names, no city A prose anywhere, effects ledger empty,
> heartbeat ticking on day one. Return to city A. Confirm the rename and the colour survived.

**Watch specifically for an alert from city A popping over city B** — the ring is cleared in
`ResetForNewSave`, and that clear is the W0 bug class.

**D. Gameface rendering** that no static check can reach: that the masthead's serif stack resolves to
an actual serif; that a long article body scrolls rather than pushing the buttons off-screen; that
`Portal` overlays the HUD for the modal's subtree as it already does for `FirstRunDialog`.

**E. The parties-tab report** — *"the parties tab isn't showing anything; the US/EU choice doesn't
apply, it's locked to EU; there are no coalitions or factions."*

Three of those four claims were **read off disk and disproved**, which is the first part of this gate
that has evidence behind it rather than only a review. `Agora.log` for the reported session carries no
error, no `could not register its bindings` and no publisher failure, and the save's own sidecar
(`ModsData/Agora/725366ab-…/state_1990_08.json`) says:

| Claim | What the sidecar says |
|---|---|
| "locked to EU" | `theme: "Na"`, `system: "FirstPastThePost"`. The choice **applied**; `themeLocked` is still false, so it was also still changeable. |
| "no factions" | **12 factions** across 4 parties, generated at frame zero as `FactionModel.AppliesTo` requires. |
| "no coalitions" | Correct, and **by design**: coalitions are a proportional feature and this save is FPTP. `electionHistory: 0` and `recentPolls: 0` besides. |
| "shows nothing" | `parties: 4`, all named. The register was there to be shown. |

So all four symptoms are downstream of one bug — a Parties tab that rendered nothing — and the tab was
the only place any of those facts were visible. The prime suspect is a **stale deployed bundle** (the
Parties tab is recent; `ui/npm run build` deploys to `…\Mods\Agora.Mod`). Both halves have now been
rebuilt and redeployed, and the deployed `Agora.Mod.mjs` was grepped for the new strings. **Staleness
cannot be proven retroactively — the rebuild overwrote the evidence — so this is the live hypothesis,
not a confirmed root cause.** What is confirmed is that causes 2 and 3 of that report are ruled out.

Still needing the screen, and nothing below is claimed as walked:
1. New city → the region prompt appears and holds the clock. Choose **United States** → US party
   names, FPTP, factions in party detail. Choose **Europe** → proportional, and coalition arithmetic
   in party detail from the **first published poll** rather than the first election.
2. The **region chip** in the dashboard bar (new): present while `themeLocked` is false, absent after
   the first election, and pressing it opens Settings on the theme picker. This is the standing second
   route to the choice, for the case where the first-run prompt never rendered.
3. `Agora.log` should now carry a `save active at …; theme … (…), N parties, M factions, themeLocked=…`
   line on every load, and a `setTheme("…") requested` line on every press — the two lines that would
   have answered this report without a sidecar read.

**Found while walking this, and *not* fixed — it is a contract change and out of the chosen scope:**
faction **names are generated and then dropped**. `StaticPoolProvider.BuildFactions` names every
faction, `FlavorDocument` parses them into `FactionFlavor` — and `ToPayload` has nowhere to put them,
because `FlavorPayload` (the frozen boundary contract) has no `Factions` collection. Its own remark
says so and says adding one "is a contract change and is reported rather than made here". The
consequence on disk: all 12 factions carry `name: ""` after a completed prose wake
(`lastFlavorDate: 1990-08-01`). The pane counts them and lists no names, which is the honest
rendering of the state, but the state is wrong. **Fix belongs behind `/schema-change`.**

### W5 — what shipped, and what did not

**Shipped, reviewed, committed** (branch `worktree-agent-a1c4d1450a9355a73`, 4 commits on top of the
inherited pair): article `refs` cross the Core boundary and render as chips; the article instruction
leads with what happened and bans unattributed sourcing; election coverage asks for a party's own
claim and own challenge rather than a winner's and a loser's reaction; the canned pool was rewritten
against the same rule with new election templates and now carries `refs` on every article, which is
what allowed `FilterAgainstCatalog` to start dropping refless ones; `--model` with the alias
`claude-haiku-4-5`; and `byline`/`tags` struck from the article contract.

> **Superseded 2026-08-09 — the popup lane is now built.** The paragraph below describes the state
> before `docs/plans/0003-w5-popup-lane.md` was written and executed. Kept as the record of what the
> gap was. All three prerequisites it names were built: a severity gate reading the engine's own
> `MajorSeverityThreshold`, a coalition-formed feed row, and party founded/dissolved rows plus a
> Mod-side detection query extracted into `Agora.Core`.

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

## Fringe ceiling and 1-year terms (packet 15)

Terms are now **1 year in both themes**, and the NA theme enforces the ratified "two dominant
parties + weak third parties" rule through a new `fringe` tuning packet.

**What was wrong.** Nothing in the voter model converted major-party failure into minor-party gain:
the incumbency and mandate terms are party-scoped and can only *subtract* from the government. A
fringe party's support was platform proximity plus habitual loyalty and nothing else, so minor
parties took 20%+ of an NA ballot for no reason, and no amount of good government pushed them back.

**What it does.** A minor party is pinned at `fringe.baseCeiling` (3%) until the majors have failed
`unlockConsecutiveTerms` (3) terms running; the ceiling then opens toward `maxCeiling` (40%) scaled
by how badly they failed, how long the failure has run, and how aggrieved the city is on that
party's own `CoreGrievance`. One good term resets the streak outright.

- Enforced in **affinity space**, in `AffinityEngine.Compute`'s bloc loop, as an additive shift on
  `BlocAffinity.Affinity`. That one hook covers city standings, published polls **and** election day,
  because `FptpElection` re-softmaxes the same affinities rather than reading the standings — so the
  election packet needs no knowledge of ceilings. `FringeCeiling.cs` / `FringeFailure.cs`.
- **FPTP only.** A proportional save is bit-identical with the packet on and off; the failure ledger
  is gated on the system, not just the master switch, so that claim is testable.
- `parties.deathVoteShareThreshold` dropped 0.03 → **0.01** so the ceiling cannot dissolve the
  parties it suppresses before the unlock can fire. This key is shared with EU, so EU parties now die
  only below 1%.
- `PartyPlatform.RefreshManifesto`, which had been called from nothing but its own tests, is now
  wired at campaign open (edge-triggered). Without it the ceiling is a ratchet — grievance opens it
  and nothing an establishment party can do closes it again.
- `MandateResolution.OppositionSurge` finally has a reader: it is the defiance signal, and it arrives
  salience-weighted at source.

**Known cosmetic consequence.** A capped party settles at `PartyStatus.Endangered` rather than
`Active`, because 3% sits under `parties.endangeredVoteShareThreshold` (5%). Harmless mechanically —
the death counter only starts below 1% — but the Parties tab will show a permanently "endangered"
party that is in fact being held there on purpose. Worth a UI distinction later.

Schema: `engine_tuning` and `political_state` both went **2 → 3**; the v2→v3 sidecar migration
reconstructs `parties[].isMajor` from id order rather than defaulting it, since defaulting to false
would tell the ceiling an existing NA save has no majors and pin its whole ballot.

## Known gaps found this pass, not yet closed

-1. **`SimDate.ToString()` is culture-invariant by accident, not by declaration.** A hygiene gap, not
   a determinism hole — the distinction matters and an earlier draft of this entry got it wrong.
   `src/Agora.Core/Contracts/SimDate.cs:57` is `$"{Year:D4}-{Month:D2}-{Day:D2}"`, and an
   interpolated string formats under `CurrentCulture`.
   **`SeedStreams.Derive` is already immune** and says so: it folds `Year`/`Month`/`Day` in as `int`s
   via `MixInt32` *"so the seed never depends on formatting behaviour"*. The primary seed path was
   never at risk. The real exposure is one level out — `StaticPoolProvider.cs:377` builds an article
   id as `"static-" + request.Date.ToString() + "-" + (i + 1)`, and that id becomes the sub-stream
   key at `:401` which `RngFor` concatenates into the hashed stream name. It is also a **persisted
   article id and part of sidecar filenames** (`AgoraJson.cs:226-228`), which is the bigger of the
   two consequences.
   Safe today: `D4`/`D2` on a non-negative `int` emits ASCII digits under every culture .NET
   supports, and `NegativeSign` is the only culture-sensitive element. `Month` and `Day` are
   constructor-validated; `Year` is not range-checked, but a negative year is a far larger failure
   than a formatting one. `CoalitionFormation` already formats its attempt number with an explicit
   `InvariantCulture` and a comment saying why, so the codebase knows the rule and this site simply
   predates it. **When closing it:** describe the fix as protecting *sub-stream keys, ids and
   filenames* — not "seed derivation", which is already safe by construction — and pin it with a test
   that sets `CurrentCulture` to a hostile culture (`ar-SA`, `sv-SE`) around `SimDate.ToString()`
   itself, not around `Derive`.

0. **Text entry has never been rendered under Gameface.** W4 lane D's five fields
   (`PartyEditor.tsx:253, 266, 338, 436` and the `<textarea>` at `:325`) are the **first
   `<input>`/`<textarea>` anywhere in `ui/src`**. `cs2/ui` exports no text-input component — the only
   trace is a `focusInputField` sound enum in `types/ui.d.ts:195`, i.e. the game has internal fields
   it does not expose — so there was no in-repo pattern to copy and `refsrc/` is C#-only. Beyond "do
   characters arrive", **the test that matters is key propagation**: focus a field and press space,
   digits, `b`, `p`, and confirm the sim does not pause, change speed, or open bulldoze. Nothing in
   the component stops propagation, because there was no established pattern to copy. If it fails,
   the fix is `onKeyDown` stopping propagation or a `FOCUS_DISABLED` scope — a follow-up, not a
   defect in what shipped. **`<textarea>` is the higher risk** of the two.

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
- **Never junction a worktree's `ui/node_modules` to another checkout's install.** `ui/node_modules`
  is gitignored, so a fresh worktree has none and `tsc` is unavailable there. Junctioning to a
  sibling install looks like the cheap fix and is a trap: deleting the junction afterwards with a
  recursive delete follows the link and **empties the target**, silently disarming `tsc` for every
  other lane and for the main checkout. This happened on 2026-08-09 and cost a real verification
  gap — two lanes reported clean typechecks against an install a third lane then destroyed. Run
  `npm install` inside the worktree instead; it takes about five seconds.
- **`npm run build` deploys.** It writes into the player's live `…\Mods\Agora.Mod` folder, and
  `dotnet build Agora.sln` triggers it too once `node_modules` is installed. Use
  `dotnet build src/Agora.Mod/Agora.Mod.csproj -p:UseCsiiToolchain=false` for a compile check that
  does not clobber the deployed mod mid-session.
