# Agora.Core — the political engine (pure C#)

**One rule dominates: this project references nothing from the game.** No `Game.*`, no `Colossal.*`,
no `Unity.*`, no `UnityEngine`. The csproj has no game references and must never gain any. A build
that needs one is a design error in the caller, not a missing reference here.

## Layout

- `Contracts/` — the structs and interfaces crossing the boundary. `CitySnapshot`, `DistrictSnapshot`,
  `SimDate`, `IClock`, `ISnapshotSource`, `IEffectSink`, `IFlavorProvider`. Changes here are contract
  changes: bump `schemaVersion` and run `/schema-change`.
- `Determinism/` — `SeedStreams`, the named-stream RNG. Everything stochastic goes through it.
- `Engine/` — blocs, issue weights, party/faction registry, affinity, turnout, polls, elections,
  coalitions, mandates, derived indices.
- `Events/` — timeline catalog loading/validation, the deterministic scheduler, procedural generation.

## Determinism rules

- Never `System.Random`, `DateTime.Now`, `Guid.NewGuid()`, `Environment.TickCount`.
- Every stochastic draw: `SeedStreams.For(saveGuid, simDate, "stream.name")` → `DeterministicRng`.
  Stream names are stable string constants; renaming one changes history and needs a migration note.
- Never iterate a `Dictionary` or `HashSet` where order affects output. Sort explicitly, or use an
  ordered collection. This is the most common silent determinism bug.
- Prefer `double` over `float`, and sum in a defined order.
- No tuning constants in code. They live in `data/engine_tuning.json` and arrive as parameters.

## Testing

Every engine behaviour gets a test in `tests/Agora.Core.Tests`. The canonical pattern — same seed
twice yields byte-identical output — is in `/write-test`. The whole suite must pass on a machine
with no copy of Cities: Skylines II.
