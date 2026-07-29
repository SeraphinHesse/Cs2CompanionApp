# src/ — C# source

Two assemblies, and the boundary between them is the most important rule in this repo.

| Project | Target | References game? | Holds |
|---|---|---|---|
| `Agora.Core` | netstandard2.0 | **No — never** | voter model, parties, elections, polls, mandates, events, determinism kernel |
| `Agora.Mod` | net48 (toolchain) / netstandard2.1 (fallback) | Yes | `IMod` entry, sensors, effects, time service, persistence, LLM provider, UI bindings |

**Core is netstandard2.0 and must stay there.** The modding toolchain builds `Agora.Mod` as `net48`,
and .NET Framework cannot reference netstandard2.1 — raising Core's target fails with `NU1201`, and
the error surfaces in `Agora.Mod`, far from the cause. So no `Span<T>`, `MathF`, `Math.Clamp`,
`HashCode.Combine`, or default interface members in Core. Polyfill inside Core instead.

`Agora.Mod` reads the game and feeds `Agora.Core` through interfaces defined in
`Agora.Core/Contracts/`. Data flows **into** Core as plain structs and **out of** Core as plain
structs. Core never learns that Unity exists.

## Why the split is non-negotiable

`Game.dll` cannot be loaded outside the Unity runtime, so anything referencing it is untestable
under `dotnet test`. Keeping the engine pure is what lets the determinism suite and the headless
multi-year simulation harness run in milliseconds on CI. If you find yourself wanting a game type
in Core, add a field to a contract struct instead.

## Conventions

- **Determinism:** no `System.Random`, no `DateTime.Now`, no `Guid.NewGuid()`, no dictionary
  iteration order dependence, no floating-point accumulation across unordered collections in Core.
  Use `Agora.Core.Determinism.SeedStreams`.
- **ECS (Mod only):** systems derive from `Game.GameSystemBase`; register in `Mod.OnLoad` via
  `UpdateSystem.UpdateAt<T>(SystemUpdatePhase…)`. Pick the phase deliberately and comment why.
- **Harmony (Mod only):** see `/harmony-patch`. Every patch needs an enumerated target list from a
  scout report, and an unpatch path proven to restore stock behaviour.
- **Logging:** `Colossal.Logging.LogManager.GetLogger("Agora")`. One logger, prefixed messages.
- **JSON:** `Newtonsoft.Json` ships with the game — do not add a JSON dependency.

## Build

```
dotnet build Agora.sln
dotnet test tests\Agora.Core.Tests\Agora.Core.Tests.csproj
```

Test by project path, not by solution: `dotnet test Agora.sln` would also build `Agora.Mod`, which
needs the game. The Core suite must pass on a machine with no copy of Cities: Skylines II.

Check that a contributor without the toolchain can still compile:

```
dotnet build src\Agora.Mod\Agora.Mod.csproj -p:UseCsiiToolchain=false
```

### Two build modes

`Agora.Mod` picks a mode automatically:

- **Toolchain mode** (when `CSII_TOOLPATH\Mod.props` exists) imports the toolchain's `Mod.props` and
  `Mod.targets`. That supplies `net48`, the game's own `mscorlib`, the **Unity.Entities source
  generators** (required for `SystemAPI` / `IJobEntity` codegen), `ModPostProcessor`, the deploy step,
  and the PDX Mods publish path. Deploys to `…\Mods\Agora.Mod\`.
- **Fallback mode** targets netstandard2.1 and references the Managed folder directly. Compiles, but
  no post-processing, no source generators, no publishing.

`Mod.props` reads the `CSII_*` variables from the **user** environment (the registry), not the
process environment — so toolchain mode works even in a shell opened before the toolchain installed.

`ModPostProcessor.exe` and `ModPublisher.exe` target .NET 6, which is out of support. Rather than
installing an EOL runtime, `Agora.Mod.csproj` overrides those two targets to pass
`DOTNET_ROLL_FORWARD=LatestMajor` as an `Exec`-scoped variable. Re-sync those overrides if a toolchain
update changes them. See `docs/scout/0002-modding-toolchain.md`.

Verify the whole environment with `.\tools\verify-setup.ps1 -Build`.
