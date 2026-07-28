# Agora.Mod — game glue

Everything that touches Cities: Skylines II lives here. **No political logic in this project** —
compute nothing that belongs in `Agora.Core`. This assembly reads the game, hands Core plain data,
takes plain data back, and applies it.

## Layout

- `Mod.cs` — `Game.Modding.IMod` entry point. `OnLoad(UpdateSystem)` / `OnDispose(...)`.
- `Core/` — settings (`ModSetting` + `IDictionarySource`), logging, mod lifecycle.
- `Time/` — `AgoraTimeService`, the only source of dates (non-negotiable #8), and the clock patch.
- `Sensors/` — one `GameSystemBase` per metric family. ECS queries → contract structs.
- `Effects/` — sanctioned palette, built on `DistrictModifierType` / `CityModifierType`.
- `Persistence/` — sidecar IO, atomic writes, save GUID component, load reconciliation.
- `Llm/` — `ClaudeCliProvider` implementing `Agora.Core.Contracts.IFlavorProvider`.
- `UiBindings/` — `UISystemBase` subclasses publishing bindings to `ui/`.

## ECS conventions

- Systems derive from `Game.GameSystemBase`, registered in `Mod.OnLoad`:
  `updateSystem.UpdateAt<MySystem>(SystemUpdatePhase.GameSimulation)`.
- Choose the `SystemUpdatePhase` deliberately and comment the reason. Wrong phase means reading
  half-updated state — a silent correctness bug, not a crash.
- Build `EntityQuery`s in `OnCreate`, never per-frame.
- Sensors must tolerate absent data: a metric that only exists city-wide falls back gracefully
  rather than throwing. Mark per-district fields best-effort in the snapshot contract.
- Respect the master toggle: when Agora is disabled, systems must no-op cleanly, not unregister.

## Harmony

Harmony does **not** ship with the game — it comes from the modding toolchain or `Lib.Harmony`.
Before writing any patch, follow `/harmony-patch`: enumerate every target from a dated scout report,
choose prefix vs postfix explicitly, and prove the unpatch path restores stock behaviour. The clock
patch in particular ships behind a kill-switch (see `politicsmodplan.md` §11 M1).

## Key game types

Confirmed against the shipped assemblies — see `docs/scout/0001-api-index.md` before assuming any
hook exists. That report is the authority; this list is a pointer, not a substitute.

## Gotchas

- `Newtonsoft.Json` ships with the game. Reference it from the Managed folder; do not add a package.
- Set `Private="false"` on every game reference — never copy game DLLs into the mod output folder.
- Log via `Colossal.Logging.LogManager.GetLogger("Agora")`, not `UnityEngine.Debug`.
