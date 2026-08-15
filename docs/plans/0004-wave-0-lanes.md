# Wave 0 — lane ownership

Umbrella: `event-system/wave-0`. Spine landed as `wave-0 spine`, on top of
`Repair the red baseline on EventSystemRefresh`.

**The one law.** Every file more than one lane would touch is in the spine already. Lanes own
strictly disjoint paths. A merge conflict is a bug in this table, not something to resolve by hand —
stop, fix the table, re-cut the affected lane.

**A path appears in exactly one row.** Checked before any worktree was created.

---

## What the spine already landed

Do not re-edit these. They are done, built and tested.

| File | Change |
|---|---|
| `src/Agora.Core/Contracts/PoliticalState.cs` | `LastCompletedTickMonth` (default `-1`); `SchemaVersion` default reconciled 3 → 5 |
| `src/Agora.Mod/Persistence/SidecarSchema.cs` | `CurrentStateVersion` 4 → 5; `MigrateStateV4ToV5` seeding from the state's own `date`; `TryReadTotalMonths` |
| `src/Agora.Core/Tuning/EngineTuning.cs` | `PollTickIntervalDays` → `PollTickIntervalMonths`, default 1 |
| `src/Agora.Core/Events/Scheduler/TickPlanner.cs` | `IsPollTick` becomes a month cadence gated on `engineTick` |
| `data/engine_tuning.json`, `data/schemas/engine_tuning.schema.json` | `pollTickIntervalDays` → `pollTickIntervalMonths`, value 1 |
| `tests/Agora.Core.Tests/SidecarMigrationTests.cs` | `Strip` extended with `lastCompletedTickMonth` |
| `src/Agora.Mod/Sensors/SnapshotRehydration.cs` | **Stub only.** The seam signature, returning an empty list, so 0a compiles against it while 0b implements it. 0b replaces the body. |
| `tests/Agora.Core.Tests/Agora.Core.Tests.csproj` | The `<Compile Link>` line for the file above |

---

## Lanes

### 0b — the metric ring · `event-system/w0-metrics` · `.claude/worktrees/w0-metrics`

**Merged first**, because 0a codes against its seam.

**Owns, exclusively:**
- `src/Agora.Mod/Sensors/MetricHistory.cs`
- `src/Agora.Mod/Sensors/AgoraSnapshotSystem.cs`
- `src/Agora.Mod/Sensors/SnapshotRehydration.cs` *(the spine left a stub; fill in the body)*

**Task.** `MetricHistory` is already a persisted, bounded, sorted, reload-surviving keyed metric ring
— `Record`, `TrendOver`, `ToFile`/`RestoreFrom`, `CityKey`/`DistrictKey`, routed through
`SidecarSchema.Migrate` and already linked into the test project. **`metric_ring.json` is not built.**
Widen what is recorded and add rehydration:

1. Record, every month, the closed set of fields anything reads off a *historical* snapshot — city
   `Population` and `Education` (all tiers), and per district `Education` (all tiers) and
   `Wealth[WealthTier.Low]`. That set is not a guess: `IndicesEngine.Compute` is the only reader of
   `SnapshotHistory`, and its brain-drain and gentrification legs are the only things it reads off
   the historical object.
2. Record the city and district scalars Wave 2's `metric`/`delta` triggers will name, so the trend
   window is already filling by the time the trigger registry exists.
3. Add `SnapshotRehydration`, which rebuilds `CitySnapshot`s from the stored series.

**Seam other lanes code against — publish it exactly:**

```csharp
// src/Agora.Mod/Sensors/SnapshotRehydration.cs
public static List<CitySnapshot> Restore(MetricHistory history, SimDate asOf, int months);
```

Oldest first, at most `months` entries, none dated after `asOf`, each carrying its own `Date`.

**Acceptance.** `ToFile`/`RestoreFrom` round-trips without loss. Builds with
`dotnet build src/Agora.Mod/Agora.Mod.csproj -p:UseCsiiToolchain=false`.

**The trap to avoid.** A rehydrated `CitySnapshot` leaves every unstored field at `0`, and a zero is
indistinguishable from a measurement. Do not widen the rehydrated object beyond what is actually
recorded, and do not record "everything" to be safe — 0c's golden test is what decides whether the
stored set is sufficient, and a field that is neither stored nor read is just file size.

---

### 0a — the tick gate · `event-system/w0-runtime` · `.claude/worktrees/w0-runtime`

**Merged second.**

**Owns, exclusively:** `src/Agora.Mod/Core/AgoraRuntime.cs`

**Task.**
1. Gate `OnMonth` on `today.TotalMonths > _state.LastCompletedTickMonth` (the field the spine added).
   The month may run only when it is strictly newer than the watermark.
2. Write the watermark after a month completes — in `OnMonth` after the engine returns, and for each
   replayed month in `Replay` (`:2712-2760`).
3. Demote `_hasTicked`/`_lastTick` (`:237-238`, `:1848-1851`) to a pure logging latch. They stay for
   the log line; they no longer decide anything, because they are session-local and cleared by
   `ResetForNewSave`, which is exactly why the double-tick existed.
4. Seed `_snapshotHistory` on load from 0b's `SnapshotRehydration.Restore`, at the save-load path
   (`:731-760`), so a delta or window trigger reads the same history whether or not the player quit
   to the menu.

**Acceptance.** Builds with `-p:UseCsiiToolchain=false`. The gate itself is **not unit-testable** —
`AgoraRuntime` is `Agora.Mod` and links no game type into the headless suite by design — so it gets a
manual gate row rather than manufactured coverage. Do not write a test that fakes the runtime to
claim it.

**Do not touch** `AgoraHeartbeatSystem`, `PoliticalEngine`, or anything under `Persistence/`.

---

### 0c — the proofs · `event-system/w0-tests` · `.claude/worktrees/w0-tests`

**Merged last.**

**Owns, exclusively:** `tests/Agora.Core.Tests/**` — new files, plus additions to
`SidecarMigrationTests.cs`, `SchedulerTests.cs` and `MetricHistoryPersistenceTests.cs`. The `.csproj`
already links `SnapshotRehydration.cs`; no lane needs to edit it.

**Task.**
1. **The v4 → v5 fixture.** A hand-authored v4 state document — not a serialized `PoliticalState`
   with fields deleted, per this file's existing fixture discipline — upgrades with
   `lastCompletedTickMonth` seeded from its own `date`. A document with no readable `date` lands at
   `-1`. A document that already carries the property is left alone. Re-running `Migrate` on the
   result changes nothing: idempotency is the property non-negotiable #6 rests on.
2. **The poll cadence.** `pollTickIntervalMonths` now means something. Prove `IsPollTick` follows the
   interval and is false on a month that is not an engine tick.
3. **The golden rehydration test, and it is the load-bearing one.** Do not assert a field list — that
   only ever covers the fields whoever wrote it thought of. Instead: build a full multi-month
   history, round-trip it through record → `ToFile` → `RestoreFrom` → `SnapshotRehydration.Restore`,
   run `IndicesEngine.Compute` against both the original history and the rehydrated one, and assert
   the two `DerivedIndices` are identical. That proves the stored set is sufficient for every reader
   there actually is, and fails loudly the day someone adds a historical read that is not stored.

**Acceptance.** Test count rises from 1415. `dotnet test tests\Agora.Core.Tests\Agora.Core.Tests.csproj`
— the project path, never the solution: `Agora.Mod` needs the game installed and this suite must not.

---

## Notes for every lane

- **`refsrc/` is not needed this wave** — every API touched is Agora's own. If that changes, it lives
  only at `C:\Users\serap\OneDrive\Documents\GitHub\Cs2CompanionApp\refsrc`, never inside a worktree,
  and must be grepped, never read in full.
- **No `ui/` work this wave**, so no `npm install` and no `node_modules` junction.
- **Never run `npm run build`** — it deploys over the player's live `…\Mods\Agora.Mod`. `dotnet build
  Agora.sln` triggers it too once `node_modules` exists. Verify with
  `dotnet build src/Agora.Mod/Agora.Mod.csproj -p:UseCsiiToolchain=false`.
- **`docs/status.md` belongs to no lane.** `/commitpushpr` writes it, including 0a's manual gate row.

## Manual gate this wave produces

Save mid-month, quit to menu, reload. `Agora.log` and the sidecar must show the month running once —
no duplicate poll, no double-counted `FringeWatch.MonthsObserved`. This is the gate the whole
political-power economy later rests on, and it is why Wave 0 leads rather than trails.
