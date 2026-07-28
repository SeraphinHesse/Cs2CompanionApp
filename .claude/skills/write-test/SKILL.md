---
name: write-test
description: Write an Agora test — the determinism pattern, synthetic snapshot fixtures, cap tests, and the golden-value rule. Use whenever adding engine behaviour or an effect.
---

# /write-test

All tests live in `tests/Agora.Core.Tests`, reference **only** `Agora.Core`, and must pass on a
machine with no copy of Cities: Skylines II. If a test needs the game it is not a test — it is a
manual gate item and belongs in `docs/status.md`.

## The determinism pattern

Run twice from identical inputs, compare a hash of the serialized result — not field by field.
Hashing catches the field a hand-written assertion forgot, which is precisely where desyncs hide.

```csharp
[Fact]
public void Run_ProducesIdenticalHashTwice()
{
    Assert.Equal(HashRun(SaveA, Jan1990), HashRun(SaveA, Jan1990));
}
```

Pair it with a negative: a different save or date must produce a *different* hash. Without that, a
function returning a constant passes the determinism test perfectly.

## Golden values

Anything that pins the shape of history — seed derivation above all — gets a test asserting an
exact literal. It exists to fail when someone "harmlessly" refactors `SeedStreams`, because that
silently rewrites the political history of every existing save.

When a golden test fails, do not update the constant to make it pass. Establish first whether the
change was intended.

## Fixtures

Prefer synthetic `CitySnapshot` objects built in the test over recorded JSON. They are readable,
they diff cleanly, and they do not rot when the snapshot schema gains a field.

## Cap tests

Drive the value **past** the cap and assert the clamp. Cover magnitude and duration separately, and
include the negative direction — a cap that only holds for positive magnitudes is not a cap.

## Simulation harness

For lifecycle and multi-year behaviour, run headless over synthetic city data and assert direction
rather than exact values: poll error under-samples low-education districts, turnout rises with
happiness, a defied mandate depresses governing-party support. Direction assertions survive tuning
changes; exact-value assertions turn `engine_tuning.json` into a minefield.

## Running

```
dotnet test tests\Agora.Core.Tests\Agora.Core.Tests.csproj
```

Use the project path, not the solution — `Agora.Mod` needs the game installed, and the whole point
of this suite is that it does not.
