---
name: commitpushpr
description: Close a wave of the event-system rework — prove it green, verify every migration, clean up worktrees, push the umbrella, open the PR into EventSystemRefresh and write the next orchestrator's handoff prompt. Use once all lanes are merged into the umbrella.
---

# /commitpushpr

The other half of `/nextwave`. Fired at the wave orchestrator once every lane is merged into
`event-system/wave-<N>` and the umbrella is ready to go back to `EventSystemRefresh`.

**Step 6 is the load-bearing one.** The PR is for the owner; the handoff is for the next session,
which will have none of your context. A wave that ships perfect code and a vague handoff has failed.

## Steps

1. **Prove it green.** All three, and record the numbers:
   ```
   dotnet build Agora.sln
   dotnet test tests\Agora.Core.Tests\Agora.Core.Tests.csproj
   cd ui && npx tsc --noEmit
   ```
   The test count must **rise** every wave. A drop is a deleted test, i.e. a defect, not noise —
   find it before you push.

2. **Verify every migration.** For each schema version this wave bumped, confirm there is a
   migration step *and* a fixture test at the old version. An untested migration is a guess, and
   someone has a thirty-year save. The six documents and their version fields:

   | Document | Version constant |
   |---|---|
   | `state_*.json` | `SidecarSchema.CurrentStateVersion` |
   | `settings.json` (standalone **and** nested in state) | `CurrentSettingsVersion` |
   | `metric_history.json` · `metric_ring.json` | `CurrentMetricHistoryVersion` · the ring's own |
   | `flavor_cache.json` | `CurrentFlavorCacheVersion` + `FlavorSchema.SupportedSchemaVersion` |
   | `snapshot` / `politics_flavor` / `timeline` / `civic_events` | `data/schemas/*.schema.json` |
   | ui bindings | the `schemaVersion:` line in `docs/contracts/ui_bindings.md` |

   Two rules that have already caused real bugs: a **nested** settings block is never reached by the
   settings step table, so the upgrade helper must be called from the state step too; and a migration
   step uses **frozen local constants**, never a live tuning read.

3. **Clean up.** `git worktree remove` each lane, then `git worktree prune`, then delete the merged
   lane branches. A stale worktree is the next wave's mystery conflict.

4. **Commit and push the umbrella** with the co-author trailer. Never force-push it.

5. **Open the PR.**
   ```
   gh pr create --base EventSystemRefresh --head event-system/wave-<N>
   ```
   Body covers, in this order: what shipped · which schema versions moved and why · **what is not
   done** · manual gates only the player can walk · the test delta. Ends with the Claude Code
   generation footer. **Do not merge it yourself** — the owner reviews.

6. **Write `docs/plans/0004-wave-<N>-handoff.md`.** It must contain a **ready-to-paste prompt for the
   next orchestrator**, and it must stand alone:
   - the next wave's number, and the instruction to **begin with `/nextwave`**
   - one paragraph on the state of the world, written for someone who was not here
   - the PR link and its merge status
   - the spine file list you actually landed, which may differ from the plan
   - the lane table and what each lane really delivered
   - **anything discovered that contradicts `docs/plans/0004-event-system-rework.md`** — this
     outranks the plan for the next wave, so say it plainly rather than burying it
   - every manual gate opened and not yet walked

7. **Update `docs/status.md`** with the wave's row. Say what was built *and* what has only been
   built rather than seen — those are different claims.

## Traps

- **A green build is not a passed gate.** `AgoraRuntime` and `src/Agora.Mod/UiBindings/**` are not
  linkable into the headless suite by design, so their coverage is manual gate items in
  `docs/status.md`, not tests. Never stub a game type to manufacture coverage; record the gate.
- **Never force-push the umbrella** and never merge the PR yourself.
- **Do not tick a checkbox for work that did not ship.** A ticked box hiding unshipped work is the
  single failure this workflow has already had to correct once.
- **Report the count honestly.** If a lane was cut, say which and why. Scaling the wave down is the
  owner's call, not yours.
