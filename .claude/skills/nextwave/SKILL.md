---
name: nextwave
description: Open a wave of the event-system rework — read the handoff, cut the umbrella branch, land the spine alone, declare disjoint lane ownership, spawn worktrees and dispatch coders. Use as the first action of every wave orchestrator session.
---

# /nextwave

The event-system rework (`docs/plans/0004-event-system-rework.md`) runs as eight sequential waves,
each orchestrated by one session on its own umbrella branch. This skill is how a wave opens. Run it
**before anything else** — the session that designed the structure is gone, and this file is what
keeps it alive across the handoff.

**The one law:** every file that more than one lane would touch is landed by *you*, in the spine
commit, before any worktree exists. Lanes then own strictly disjoint paths. **A merge conflict is a
bug in the wave plan, not something to resolve by hand.**

## Steps

1. **Read the inputs, in this order.** `docs/plans/0004-event-system-rework.md` (the authority),
   `docs/plans/0004-wave-<N-1>-handoff.md` (what actually happened last wave — it outranks the plan
   wherever the two disagree, because it was written against the code), and `docs/status.md`.
   **Refuse to start if the previous wave's PR is not merged into `EventSystemRefresh`.** Two
   umbrellas open at once is how the disjoint-path guarantee dies.

2. **Cut the umbrella.**
   ```
   git checkout EventSystemRefresh && git pull
   git checkout -b event-system/wave-<N>
   ```

3. **Land the spine alone.** Take the wave's spine list from the plan — contracts, schema versions
   and their migration steps, tuning keys, binding-contract rows, `partial` splits. Write it, build
   it, test it, and commit it as **one** commit titled `wave-<N> spine`. No lane exists yet, and none
   may until this is green. A spine that does not compile makes every lane's first build a mystery.

4. **Declare lane ownership** in `docs/plans/0004-wave-<N>-lanes.md`, one row per lane: branch,
   worktree path, **exclusive path list**, acceptance criteria, and any seam signature other lanes
   code against. **Check that a path appears in exactly one row before spawning anything.** This is
   the cheapest possible moment to catch the collision and the most expensive one to miss it.

5. **Spawn worktrees.**
   ```
   git worktree add .claude/worktrees/w<N>-<lane> -b event-system/w<N>-<lane> event-system/wave-<N>
   ```
   Then `npm install` **inside** each worktree's `ui/` that needs it.

6. **Dispatch coders in parallel** — one `coder` subagent per lane, in a single message so they run
   concurrently. Give each only its own row plus the `CLAUDE.md` files its paths route to. A coder
   handed the whole plan will wander outside its lane.

7. **Review before merging.** One `reviewer` subagent per lane against `/review-checklist`. Merging
   an unreviewed lane means the next lane builds on it.

8. **Merge in the declared order**, building and testing after each. On a conflict: stop, fix the
   wave plan, and re-cut the affected lane. Do not hand-resolve — a hand-resolved conflict silently
   erases one lane's half of a shared file.

9. **Hand off to `/commitpushpr`** once every lane is merged and green.

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
- **`npm run check` checks less than it sounds like.** It runs only the design-token guard — no
  typecheck, no CSS class parity. `npx tsc --noEmit` is a separate obligation, and class names are
  diffed by hand in review.
- **`refsrc/` does not exist inside a worktree.** It is gitignored and lives only in the main
  checkout at `C:\Users\serap\OneDrive\Documents\GitHub\Cs2CompanionApp\refsrc`. Hand any lane that
  needs it that absolute path, and tell it to **grep, never read in full** — the tree is hundreds of
  MB. A lane that greps `./refsrc` locally gets zero hits and quietly concludes the API is absent.
- **Shared files belong in the spine, never in a lane.** If two lanes both need a file and you are
  tempted to "let them coordinate", the wave plan is wrong. Move it to the spine or split the wave.
