# Wave 2 → Wave 3 handoff

> **This file is a reconstruction, written by wave 3's orchestrator, and it is not the artifact the
> `/commitpushpr` step 6 produced.** Commit `e44281e` is titled "Write the wave-2 handoff and record
> its state in the status doc" and its message describes this file's contents in detail — the
> ready-to-paste prompt, the six contradictions, the twelve rulings, the traps. **Its diff touches
> `docs/status.md` and nothing else.** The file was never written.
>
> What follows is assembled from three sources that *do* exist: the commit message itself,
> `docs/plans/0004-wave-2-lanes.md` (which carries all twelve rulings with their full reasoning), and
> the wave-2 section of `docs/status.md`. Where this file states something, it is because one of
> those three states it. **Nothing here is recalled from a session; there was no session to recall.**
>
> The lesson for `/commitpushpr` is narrow and worth stating: a commit message is not evidence that a
> file was written. The step that writes the handoff should be followed by a check that the path
> exists in the tree.

---

## Where wave 2's substance actually lives

| What the missing handoff would have carried | Where it is instead |
|---|---|
| The twelve mid-wave rulings, with reasoning | `docs/plans/0004-wave-2-lanes.md` § "Rulings taken mid-wave" — **complete**, and the authority |
| Lane ownership, seams, merge order | `docs/plans/0004-wave-2-lanes.md` — complete |
| Decisions closed, and where they are closed in the code | `docs/plans/0004-wave-2-lanes.md` § "Decisions already closed" — complete |
| Wave 2's state of the world | `docs/status.md` § "Wave 2 — built, and not yet reachable by a player" |
| The two sharpest traps for wave 3 | `docs/status.md`, same section, and repeated below |
| Test count, verification | `docs/status.md` wave table: 1469 → **1703** |

## PR

**PR #5** — *Wave 2: the story engine core (state v6, settings v4, tuning v6)* — **merged** into
`EventSystemRefresh` on 2026-08-15. Wave 3 was cleared to open on that basis.

## State of the world, in one paragraph

`Agora.Core/Stories/` now holds a complete story engine: a declarative trigger grammar
(`TriggerEvaluator`, `MetricRegistry`), seeded drafting with a pity-weighted pool and a re-use
cooldown (`StoryAssembler`, `EventPoolWeighting`), the 2-of-3 resolution rule with its three-state
`CheckResult` (`StoryResolution`), and the political-power arithmetic (`PoliticalPower`). State moved
to v6, settings to v4, `engine_tuning` to v6. **Nothing calls any of it.** No tick drafts a story, no
UI renders one, no effect is dispatched, no power is awarded in play, and there is no catalog for it
to read — that is wave 3. Wave 2's claim is that the arithmetic is right, not that anything happens.

## The two traps aimed at wave 3

Both are quoted from `docs/status.md`, which is where they survived.

1. **`Manual` is a trigger *kind*, not a tier.** A `Manual`-triggered event is never pooled and can
   never produce a story; "mandatory" is a *tier* derived from severity. A mandatory-severity event
   still needs a real trigger. Two lanes read the earlier wording and built opposite things.
2. **An `Absent` trigger with a misspelled metric id evaluates `Met` on every city forever.**
   `MetricId` carries three vocabularies and only one is validatable, so wave 3's catalog loader must
   require a non-registry id to appear in an authored id list.

## What wave 3 found on top of these

Recorded here because it belongs with trap 2 and was not known when trap 2 was written.

**`CitySnapshot.ActivePolicyIds` is written by nothing.** It is plumbed through `SensorMerge` and
`SnapshotAssembly`, and no sensor populates it — there is no policy sensor. So trap 2 is not only a
*typo* hazard: an entire one of the three vocabularies is empty. A `Policy` spec is permanently
`NotMet`, and an `Absent` policy spec is permanently `Met`. Wave 3's loader rejects `TriggerKind.Policy`
by name, with that reason, rather than letting content be authored against it.

## Verification recorded for wave 2

- `dotnet test tests\Agora.Core.Tests\Agora.Core.Tests.csproj` — **1703 passed, 0 failed** (from 1469).
- `dotnet build Agora.sln` — **not run.** Blocked by a permission classifier and recorded as not
  walked rather than assumed fine. Wave 2 adds no ECS code, so no source-generator coverage was
  missed. **Wave 3 ran it: 0 warnings, 0 errors**, which closes this retroactively.
- No manual gates of wave 2's own — it contains no game-facing code. **Waves 0 and 1 remain unwalked
  and still blocking**, and wave 1's `AGORA-STATCOLLECTION` census gate is the one that constrains
  wave 3's content directly.
