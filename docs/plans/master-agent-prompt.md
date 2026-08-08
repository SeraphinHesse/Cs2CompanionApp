# Prompt for the `master` agent — execute the remainder of `fixplan.md`

You own the fix-plan pass end to end from here. Phase 1 (**W0**, **W1**, and the phase-1 backlog
items) is done: code complete, checklist-gated, 1033 tests passing, build 0/0, nothing committed.
Your job is everything still open — **W2 through W6, the batched schema change, and the remaining
backlog** — dispatching Scout/Planner/Coder/Reviewer as needed, merging only reviewer-approved work,
and stopping at a clean, fully-reviewed tree ready for the user's own final pass. You never write
feature code yourself.

## Read first, in this order

1. `fixplan.md` — the authority. Do not re-litigate anything it ratifies.
2. `docs/status.md` — current state of the tracker; keep it current as you go.
3. `docs/plans/0001-batched-schema-change.md` — the batched migration plan. **Fully decided, ready
   to execute.**
4. `docs/plans/0002-w6-parties-tab.md` — the Parties tab plan, including Part II (the five accepted
   additions). **Fully decided at §H0** — see "Decisions already locked in" below.
5. Root `CLAUDE.md` non-negotiables and `politicsmodplan.md` where a task's routing table points
   you there.

## Sequencing — do not reorder without a reason you can state

1. **W2** — party names lock in. Do this first: it touches the same runtime W0 just reworked, and
   every later workstream is easier to test once parties render real names instead of `party-01`.
2. **Plan 0001** — the batched schema change (settings, `Party.PlayerOverrides`, article limits).
   Landing this before W3/W6 means those workstreams consume the new contract instead of migrating
   twice.
3. **W3** — EU/US theme, first-run dialog. Gates the electoral system, so it must precede W6.
4. **W6** — Parties tab (core + the five accepted additions, `docs/plans/0002`).
5. **W4** — player-owned party identity (inline rename/recolour). Needs correcting before it's
   built — see below.
6. **W5** — the press (article quality, popup, model pin). Benefits from everything above being in
   place.
7. **Remaining backlog** — `ClaudeResponseReader.cs:95` envelope-unwrap defect, the two unverified
   affordance items still open (`docs/status.md`'s backlog row), and anything a workstream's review
   pass surfaces along the way.

## Decisions already locked in — do not re-ask the user for these

- **W6 additional content**: manifesto-vs-platform, poll trend sparkline, coalition relations, party
  history strip, and mandate scorecard are **in**. Bloc support breakdown is **out**.
- **Coalition relations design**: **Design B** — a public, RNG-free `RankCandidates` extracted from
  `Agora.Core`'s `CoalitionFormation.Form`. No schema change, no migration, no save growth; the
  ranking is live and drifts with platforms. Design A (persisting `RankedOptions`) was rejected.
  Plan 0001 does **not** need to carry this.
- **Poll trend**: publish a new party-scoped `agora.parties.pollTrend`, not the reserved
  `agora.seats.pollTrend` (leave that for M6's city-wide chart — the reserved shape would breach the
  UI payload budget if reused here).
- **Mandate scorecard**: division of labour with the existing `MandateTracker` in the News panel, not
  a duplicate view. Parties gets one row per status plus a delivery rate, filtered from the
  already-published `agora.news.mandates`. No new binding.

## Corrections to `fixplan.md` you must apply, not just read

Verified this session against source, not assumed:

- **W4**: `fixplan.md` calls `AgoraRuntime.ApplyProseNames` "the single enforcement point" for name,
  description, *and colour* locks. It is **not** — it never writes `ColorHex`. The only `ColorHex`
  writers are in `Agora.Core` (`PartyRegistry.cs`, `PartyLifecycle.cs`). Design W4's lock enforcement
  to actually cover colour before building it, or a party split will silently recolour a
  player-recoloured party.
- **W5**: tightening article length limits (headline ≤90, body ≤420) is a schema tightening. A
  `maxLength` violation is fatal to the *whole* flavor document (unlike the per-entry drop used
  elsewhere), so shipping the new caps without mitigation would discard every cached flavor entry —
  **including party names** — resurrecting the exact `party-01` bug W2 exists to fix. The fix: prune
  only over-length `articles[]` at cache load; never truncate; never touch `partyFlavor`.
  `StaticPoolProvider` also hardcodes its own 140/900 caps and must move in lockstep or the fallback
  provider breaks on any machine without a `claude` binary.
- **`ClaudeResponseReader.cs:190`** is a confirmed **false report** — already struck from the
  backlog. Do not reopen it. The real defect is the envelope unwrap at `ClaudeResponseReader.cs:95` /
  `FlavorJsonReader.cs:81-85`: trailing content after the envelope makes the reader extract the
  envelope itself instead of the flavor document. It's queued, unfixed — pick it up in the backlog
  phase.
- Treat every `file:line` citation in `fixplan.md` as a claim to verify, not a fact — this session
  found eight places where the plan described code that doesn't exist or reasoned from a false
  premise (see `docs/plans/0001-*.md` §9 and the W6 plan's corrections). Expect more; verify before
  building.

## Operating rules — learned the hard way this session

1. **Split coder lanes so they never contend on a build.** One C#/`dotnet` lane, one UI/`npm` lane,
   working on file-disjoint scope, dispatched in parallel where the workstream allows it. A planner
   or read-only reviewer running alongside either lane must not invoke that lane's build tool.
2. **Every coder result goes to an independent reviewer before you count it done.** Not a rubber
   stamp — reviewers this session caught five review-blocking defects that passed build and tests,
   including one **you** introduced by pushing a correction that carried a valid assumption to a
   call site where it didn't hold. Expect the same risk in your own dispatches; re-review after any
   fix round that changes shared reasoning (a comment, an invariant, a shared helper), not just after
   the first pass.
3. **Pass the review checklist by explicit file path, every time**: `.claude/skills/review-checklist/SKILL.md`.
   Subagents cannot reliably resolve project skills by name — say so anyway in the reviewer's prompt
   is not enough; give the literal path. A reviewer that substitutes the CLAUDE.md non-negotiables
   for the actual checklist still catches real bugs, but not the same coverage — run one checklist
   pass with the explicit path over each workstream's full diff before declaring it merged.
4. **Verify claims against source, not against the plan or a prior agent's report.** The
   `refsrc/Game/...` decompiled tree is the ground truth for any claim about game/ECS ordering or
   semantics — grep it, don't trust a comment that cites it. This session had the same effect-ledger
   comment wrong three times in three different ways (map mutation, entity-version semantics, event
   ordering) before a reviewer actually read the call sequence in `refsrc/`.
5. **No test is better than a tautological one.** If a defect only manifests through `Unity.Entities`
   / `Game.*` state that cannot link into `Agora.Core.Tests`, say so explicitly and record a manual
   gate (a concrete log line or observable behaviour) rather than writing a test that just restates
   the code under test.
6. **`Agora.Core` boundary is sacred.** Never let it, or the test project, gain a reference to
   `Game.*` / `Colossal.*` / `Unity.*`. When a coder needs to link a new file into
   `Agora.Core.Tests`, verify its own dependency chain first — this session found `<Compile Include>`
   of a single file insufficient more than once because of a transitive dependency on a
   `Colossal.Logging`-touching type.
7. **`schemaVersion` bumps are batched, and only one pass may claim a given number.** Plan 0001 lands
   first and takes the version to N+1; plan 0002's Part II moves it once more for all five additions,
   reading whatever 0001 left rather than hard-coding a number.
8. **Keep `docs/status.md` and `fixplan.md`'s checkboxes current** as workstreams complete — don't
   let them drift stale again the way `docs/status.md` did before this pass started.

## Explicitly out of scope — do not attempt

- **The manual A→B→A walkthrough** (`fixplan.md`'s verification gate) needs a human at the keyboard
  with the game running. Do not simulate it, do not claim it passed. State plainly in your final
  report which workstreams still need it.
- **Committing or pushing.** Leave the working tree in a clean, fully-reviewed, uncommitted state.
  If you believe a commit or PR is the right next step, ask the user first — don't take that action
  unprompted.
- **Re-deciding anything in "Decisions already locked in" above.** If new information genuinely
  changes the cost/benefit of one of those, surface it to the user with the specific new fact — don't
  silently reverse it and don't re-ask a question already answered.

## Definition of done

For each workstream: code complete, independently reviewed against the actual checklist file, build
green (`dotnet build Agora.sln`, `dotnet test tests\Agora.Core.Tests\Agora.Core.Tests.csproj`,
`cd ui && npm run check && npm run build`), `fixplan.md` checkboxes and `docs/status.md` updated to
match reality.

When W2 through W5 and the remaining backlog are all in that state, produce one final report:
what shipped, every review-blocking defect found and how it was fixed, every place `fixplan.md` or
your own earlier reasoning was wrong and corrected, current test count and build status, and exactly
what remains for the user — which is at minimum the full manual walkthrough, and anything you
explicitly deferred. Do not report a workstream done if its review is still outstanding.
