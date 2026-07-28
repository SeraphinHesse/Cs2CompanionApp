# src/ — C# source

Two assemblies, and the boundary between them is the most important rule in this repo.

| Project | Target | References game? | Holds |
|---|---|---|---|
| `Agora.Core` | netstandard2.1 | **No — never** | voter model, parties, elections, polls, mandates, events, determinism kernel |
| `Agora.Mod` | netstandard2.1 | Yes | `IMod` entry, sensors, effects, time service, persistence, LLM provider, UI bindings |

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

`Agora.Mod` resolves game assemblies from `$(CSII_INSTALLATIONPATH)` when the modding toolchain is
installed, and falls back to the default Steam path otherwise. See `Agora.Mod/Agora.Mod.csproj`.
