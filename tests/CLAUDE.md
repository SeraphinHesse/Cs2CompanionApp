# tests/ — the determinism and simulation suites

`Agora.Core.Tests` targets `net8.0`, project-references `Agora.Core`, and additionally compiles by
`<Compile Link>` a fixed list of `Agora.Mod` files that name no game type (plus a `Newtonsoft.Json`
package reference pinned to the game's shipped 13.0.2, purely so those files compile here) — see the
comments in `Agora.Core.Tests.csproj` for which and why. The invariant is unchanged and is what
matters: the suite loads no `Game.*`, `Colossal.*` or `Unity.*` assembly and must pass on a machine
with no copy of Cities: Skylines II installed. If a test needs the game, it is not a test — it is a
manual gate item, and belongs in `docs/status.md`.

## Suites

1. **Determinism** — the same seed and inputs twice produce byte-identical output. Runs on every change.
2. **Schema** — every catalog and every sample LLM payload validates; a numeric field smuggled into
   `politics_flavor.json` fails the build.
3. **Simulation harness** — headless multi-year runs on synthetic city data, asserting lifecycle rules,
   poll error direction (low-education districts under-sampled), turnout effects, mandate resolution.
4. **Effect caps** — every palette entry proves its magnitude and duration caps hold under extremes.

## The canonical determinism pattern

Run the unit twice from identical seeds and compare serialized output, not field-by-field. Hash
comparison catches fields a hand-written assertion forgets:

```csharp
var a = Run(seed: fixedSeed);
var b = Run(seed: fixedSeed);
Assert.Equal(Hash(a), Hash(b));
```

"Desync" has a precise definition — the SHA-256 of serialized Agora state at sim-date D after a
reload equals the hash before it. Vague gates never fail, so they never catch anything.

## Writing tests

Use `/write-test`. Prefer synthetic `CitySnapshot` fixtures over recorded ones: they are readable,
they diff cleanly, and they do not rot when the snapshot schema gains a field.
