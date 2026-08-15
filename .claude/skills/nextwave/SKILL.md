---
name: nextwave
description: Open a wave of the event-system rework — read the handoff, prove the base green, cut the umbrella branch, land the spine alone, declare disjoint lane ownership, spawn worktrees and dispatch coders. Use as the first action of every wave orchestrator session.
---

# /nextwave

The event-system rework (`docs/plans/0004-event-system-rework.md`) runs as eight sequential waves,
each orchestrated by one session on its own umbrella branch. This skill is how a wave opens. Run it
**before anything else** — the session that designed the structure is gone, and this file is what
keeps it alive across the handoff.

**The one law:** every file that more than one lane would touch is landed by *you*, in the spine
commit, before any worktree exists. Lanes then own strictly disjoint paths. **A merge conflict is a
bug in the wave plan, not something to resolve by hand.**

Wave 0 ran three lanes to completion with zero merge conflicts, so the law works. Everything that
went wrong went wrong somewhere else, and the steps below say where.

## Steps

1. **Read the inputs, in this order.** `docs/plans/0004-event-system-rework.md` (the authority),
   `docs/plans/0004-wave-<N-1>-handoff.md` (what actually happened last wave — it outranks the plan
   wherever the two disagree, because it was written against the code), and `docs/status.md`.
   **Refuse to start if the previous wave's PR is not merged into `EventSystemRefresh`.** Two
   umbrellas open at once is how the disjoint-path guarantee dies.

2. **Prove the base is green before you cut anything.** On `EventSystemRefresh`:
   ```
   dotnet build Agora.sln
   dotnet test tests\Agora.Core.Tests\Agora.Core.Tests.csproj
   ```
   **Record the test count as a number you measured, never one you read in a plan.** Wave 0 inherited
   a base that neither built nor passed — `Agora.Core` had a compile error and five tests were red —
   while the plan claimed 1319 green. It was 1415, and red.

   If the base is red, **repair it as its own commit before the spine**, titled so a reviewer can see
   at a glance what was inherited and what the wave did. You cannot tell your own breakage from
   someone else's on a red floor, and every lane you dispatch will hand you the same five failures
   back. Fix stale *expectations* — do not "fix" a deliberate retune by reverting it; check the
   commit message and the tuning file's own comments for intent first.

   While you are there, make the repaired assertions version-relative rather than literal. Wave 0
   found two tests that had memorised `schemaVersion: 4` as "the future" and a coefficient as `0.25`;
   both go red on the next bump or retune for reasons that have nothing to do with what they guard.

3. **Cut the umbrella.**
   ```
   git checkout EventSystemRefresh && git pull
   git checkout -b event-system/wave-<N>
   ```

4. **Land the spine alone.** Take the wave's spine list from the plan — contracts, schema versions
   and their migration steps, tuning keys, binding-contract rows, `partial` splits. Write it, build
   it, test it, and commit it as **one** commit titled `wave-<N> spine`. No lane exists yet, and none
   may until this is green. A spine that does not compile makes every lane's first build a mystery.

   Three things the spine owes the lanes beyond the plan's own list:

   - **A compiling stub for every seam.** If lane A calls something lane B is writing, the spine
     lands the signature with a trivial body, so both lanes build from commit one. Wave 0 spawned
     worktrees first and had to amend the spine and reset all three. Mark it
     `AGORA-SEAM(wave-<N>/<lane>)` and say in the comment what the real deliverable is, so the stub
     cannot be mistaken for finished work.
   - **Every hand-maintained copy list that mirrors a field you added.** A new property on a contract
     does not fail to compile when a clone method forgets it — it silently arrives at the type
     default. Wave 0 added `PoliticalState.LastCompletedTickMonth` and missed
     `PoliticalEngine.CloneState`, which handed `Retheme` a state claiming no month had ever run.
     Grep for every place the contract is copied field-by-field. `PoliticalEngine.CloneState` and
     `AgoraSettings.Clone()` are the two known ones; both say in their own comments that they are
     hand-maintained, which is a warning, not an excuse.
   - **A test for any invariant your doc comments assert.** If you write "X pins these together",
     write X in the same commit. Wave 0 shipped a comment claiming `SidecarMigrationTests` pinned
     `CurrentStateVersion` to the contract default when no such test existed — and the two constants
     had already drifted once on the strength of that claim.

5. **Declare lane ownership** in `docs/plans/0004-wave-<N>-lanes.md`, one row per lane: branch,
   worktree path, **exclusive path list**, acceptance criteria, and any seam signature other lanes
   code against. **Check that a path appears in exactly one row before spawning anything.** This is
   the cheapest possible moment to catch the collision and the most expensive one to miss it.

   **Publish both ends of every seam, not just the read end.** Wave 0 declared the function that
   *reads* rehydrated snapshots but never the entry point that *records* them, so the test lane spent
   its budget guessing metric names that existed nowhere it could see. If one lane writes data and
   another consumes it, both signatures and the key vocabulary go in the table.

   Also record, per lane, **what it must not test**. Game-facing code (`AgoraRuntime`,
   `UiBindings/**`) is deliberately not linkable into the headless suite; a lane not told so will
   either fake the runtime to manufacture coverage or waste a round trip discovering it cannot.

6. **Spawn worktrees.**
   ```
   git worktree add .claude/worktrees/w<N>-<lane> -b event-system/w<N>-<lane> event-system/wave-<N>
   ```
   Then `npm install` **inside** each worktree's `ui/` that needs it.

7. **Dispatch coders in parallel** — one `coder` subagent per lane, in a single message so they run
   concurrently. Give each only its own row plus the `CLAUDE.md` files its paths route to. A coder
   handed the whole plan will wander outside its lane.

   **Hand a lane the invariant in prose, never a literal comparison.** A coder implements what you
   wrote, exactly, including the off-by-one. Wave 0's orchestrator specified a clamp as
   "when `today <= watermark`"; the correct boundary was `<`, because equality is the ordinary
   mid-month reload — and the lane shipped a faithful implementation of the wrong operator, which
   re-armed the very double-tick the wave existed to remove. Say what must be true and why, name the
   case that must *not* trigger, and let the lane derive the operator.

   Tell each lane which decisions are already closed and where they are closed *in the code*, so it
   does not stall on a question the repo has answered. A doc comment on the function it is about to
   change is usually the authority.

8. **Review before merging.** One `reviewer` subagent per lane against `/review-checklist`. Merging
   an unreviewed lane means the next lane builds on it. Reviewing paid for itself in wave 0: it
   blocked two real defects that a green build and a full test suite had both waved through.

   **Write the brief adversarially — name the specific failure mode to hunt.** "Review this lane"
   finds style; "check whether this clamp fires on the ordinary mid-month reload, which is the case
   it must never fire on" finds the bug. Ask for the sequence of player actions that breaks it.

   **For any file not linkable into the test suite, instruct the reviewer to reason on the
   arithmetic and the control flow, and to state plainly that green tells it nothing.** Both of wave
   0's blocking defects lived in `AgoraRuntime`, passed the build, and passed 1415 tests.

   When a review blocks, work out whose defect it is before dispatching the fix. Wave 0's first
   block was half the orchestrator's (a spine omission) and half the lane's; sending the whole thing
   back would have had the lane edit a file it does not own.

9. **Merge in dependency order**, building and testing after each. The order in the lane table is a
   dependency graph, not a ritual: a lane that shares no file and no seam with an in-flight lane may
   merge early, and saying so in the merge commit is better than idling. On a conflict: stop, fix the
   wave plan, and re-cut the affected lane. Do not hand-resolve — a hand-resolved conflict silently
   erases one lane's half of a shared file.

   A lane whose tests drive another lane's code **cannot build in its own worktree**, and that is
   correct rather than a defect. Merge it into the umbrella to verify it, and review it there.

10. **Hand off to `/commitpushpr`** once every lane is merged and green.

## Traps

- **Never junction `ui/node_modules`** to another checkout. Deleting the junction later follows the
  link and empties the target, silently disarming `tsc` for every other lane and for the main
  checkout. This has already happened once and cost a real verification gap. `npm install` in the
  worktree takes about five seconds.
- **`npm run build` deploys** into the player's live `…\Mods\Agora.Mod`, and `dotnet build Agora.sln`
  triggers it too once `node_modules` exists. Lanes verify with
  `dotnet build src/Agora.Mod/Agora.Mod.csproj -p:UseCsiiToolchain=false`. Only you, once per wave,
  run a deploying build.
- **`dotnet test Agora.sln` pulls in `Agora.Mod`**, which needs the game installed. Always test by
  project path: `dotnet test tests\Agora.Core.Tests\Agora.Core.Tests.csproj`.
- **A green suite is not evidence for game-facing code.** `AgoraRuntime` and `UiBindings/**` compile
  into no test. Every claim about them is reasoning or a manual gate row, and manufacturing coverage
  by faking the runtime is itself a review-blocking defect. Write the gate row instead, and make it
  specific enough to fail: "confirm the reconciliation line appears once after a rewound load **and
  not at all** on an ordinary reload" catches what "confirm it still works" never will.
- **`npm run check` checks less than it sounds like.** It runs only the design-token guard — no
  typecheck, no CSS class parity. `npx tsc --noEmit` is a separate obligation, and class names are
  diffed by hand in review.
- **`refsrc/` does not exist inside a worktree.** It is gitignored and lives only in the main
  checkout at `C:\Users\serap\OneDrive\Documents\GitHub\Cs2CompanionApp\refsrc`. Hand any lane that
  needs it that absolute path, and tell it to **grep, never read in full** — the tree is hundreds of
  MB. A lane that greps `./refsrc` locally gets zero hits and quietly concludes the API is absent.
- **Shared files belong in the spine, never in a lane.** If two lanes both need a file and you are
  tempted to "let them coordinate", the wave plan is wrong. Move it to the spine or split the wave.
- **Check whether the thing the plan asks you to build already exists.** Wave 0 was specified to add
  a persisted metric ring as a new sidecar document; `MetricHistory` and `MetricHistoryFile` already
  were one — bounded, sorted, migrated, reload-surviving — recording two series instead of twenty.
  The wave widened them and struck the new document from the plan. The rework plan was written
  before most of this code was read, so treat its file lists as intent and grep before you create.
- **A guard over a hand-maintained list should be reflective, and must throw on what it does not
  understand.** Wave 0's `CloneStateCoverageTests` enumerates every property, seeds each by type, and
  throws on an unrecognised type rather than skipping it — a silent skip shrinks the guard back to
  whatever it happened to cover and it stops failing without anyone noticing. Assert only that a
  value was carried, not how: some copies are deliberately shallow.
- **Prefer reading a value from tuning over asserting a literal.** A test that memorises a
  coefficient goes red on the next balance pass for a reason unrelated to what it guards. Read
  `EngineTuning.Default`, and keep the assertion about the *shape* of the relationship.
