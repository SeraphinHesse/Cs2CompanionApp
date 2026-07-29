# Scout 0002 — The modding toolchain build surface

**Date:** 2026-07-30
**Method:** direct inspection of the installed toolchain (`Mod.props`, `Mod.targets`, the `CSII_*`
registry values, and the shipped tool executables). Everything below was read, not inferred.
**Supersedes:** the toolchain assumptions in `politicsmodplan.md` §4.2 and the plan's Setup section.

---

## 1. Environment variables — 15, not 4

The plan named four. The toolchain actually sets fifteen, and `Mod.props` depends on several the
plan never mentions. All values below are this machine's.

| Variable | Value |
|---|---|
| `CSII_INSTALLATIONPATH` | `…\steamapps\common\Cities Skylines II` |
| `CSII_MANAGEDPATH` | `…\Cities Skylines II\Cities2_Data\Managed` |
| `CSII_MSCORLIBPATH` | `…\Managed\mscorlib.dll` |
| `CSII_USERDATAPATH` | `…\LocalLow\Colossal Order\Cities Skylines II` |
| `CSII_TOOLPATH` | `…\Cities Skylines II\.cache\Modding` |
| `CSII_LOCALMODSPATH` | `…\Cities Skylines II\Mods` |
| `CSII_UNITYMODPROJECTPATH` | `…\.cache\Modding\UnityModsProject` |
| `CSII_MODPOSTPROCESSORPATH` | `…\Content\Game\.ModdingToolchain\ModPostProcessor\ModPostProcessor.exe` |
| `CSII_MODPUBLISHERPATH` | `…\Content\Game\.ModdingToolchain\ModPublisher\ModPublisher.exe` |
| `CSII_PDXMODSPATH` | `…\Cities Skylines II\.cache\Mods` |
| `CSII_PDXCACHEPATH` | `…\Cities Skylines II\.pdxsdk` |
| `CSII_ENTITIESVERSION` | `1.3.10` |
| `CSII_UNITYVERSION` | `2022.3.62f2` |
| `CSII_ASSEMBLYSEARCHPATH` | *(empty)* |
| `CSII_PATHSET` | `Build` |

Two notes:

- `CSII_LOCALMODSPATH` is set **before the directory exists**. The toolchain does not create it; the
  first deploy does. Any build step gated on that folder existing will skip forever.
- `CSII_UNITYVERSION` reports `2022.3.62f2`, while the game's own `Cities2_Data` reports `2022.3.71`.
  The toolchain's Unity project and the shipped game are not on the same patch. Harmless for a
  code-and-UI mod, but do not treat either number as authoritative for the other.

**`Mod.props` reads these from the *user* environment via
`[System.Environment]::GetEnvironmentVariable(name, 'EnvironmentVariableTarget.User')`, i.e. straight
from the registry.** So toolchain builds work in a shell that predates the install. Anything of ours
that reads `$(CSII_…)` as an MSBuild property gets the *process* copy and silently sees nothing.

## 2. What `Mod.props` imposes

- `TargetFramework` = **`net48`**. Not netstandard2.1.
- `LangVersion` = 9.0 (we override to `latest` after the import).
- References the game's own `mscorlib.dll` directly.
- Adds `$(ManagedPath)` to `AssemblySearchPaths`, so bare `<Reference Include="Game" />` resolves.
- Registers **12 Unity.Entities source generators** as `<Analyzer>` items from
  `$(UnityModProjectPath)\Library\PackageCache\com.unity.entities@1.3.10\Unity.Entities\SourceGenerators`.
  Confirmed present, 12 DLLs.

**This is the finding that matters for M1.** `SystemAPI`, `IJobEntity` and `EntityQueryBuilder` are
source-generated. Without these analyzers those constructs do not compile. Sensor work therefore has
to build in toolchain mode; fallback mode is a compile check, not a development environment.

### The net48 consequence

.NET Framework cannot reference netstandard2.1. `Agora.Core` was netstandard2.1 and the build failed
with `NU1201`. Core is now **netstandard2.0** — verified to need nothing newer. This is a standing
constraint on Core, recorded in `src/CLAUDE.md`.

## 3. What `Mod.targets` provides

| Target | Does |
|---|---|
| `BuildGetFullPaths` | sets `NeedBuild`, resolves full paths, sets `DeployDir` = `$(LocalModsPath)\$(TargetName)` |
| `CheckManagedPath` … `CheckEntityPackagePath` | five path validations that fail the build with actionable text |
| `ClearOutput` | `RemoveDir $(OutDir)` before every build |
| `ModPostProcessorConfig` / `RunModPostProcessor` | builds and runs the post-processor command |
| `DeployWIP` | wipes `$(DeployDir)` and copies all of `$(OutDir)` into it |
| `PublishGetFullPaths` … `RunModPublisher` | the PDX Mods publish path |

Two consequences for us:

- **Our custom `DeployMod` target is redundant in toolchain mode** and is now gated off there.
- Deploy folder is `$(TargetName)`, so **`…\Mods\Agora.Mod\`**, not `…\Mods\Agora\`. This matches
  `ui/mod.json`'s `id`, which is what links the UI mod to the code mod.

`Mod.targets` documents its own override protocol: redefine a target with the same name *after* the
import and yours replaces it. Extra files go in a target `AfterTargets="DeployWIP"` populating
`AdditionalFilesToDeploy` — **that is the hook the built UI bundle will need.**

Its bare `Condition="'$(NeedBuild)'"` is only safe inside `Mod.targets`, where `BuildGetFullPaths`
always sets that property to a literal `true`/`false`. Copy a target out of it into a build where
that target never runs and MSBuild fails with `MSB4113` on the empty string.

## 4. The tools need .NET 6, which is EOL

`ModPostProcessor.deps.json` declares `.NETCoreApp,Version=v6.0`. This machine has 8.0.13, 8.0.21,
9.0.2, 9.0.14 — so the tool aborted the build with *"You must install or update .NET"*.

**Verified fix, no install required:** with `DOTNET_ROLL_FORWARD=LatestMajor`, `ModPostProcessor.exe`
launches and runs correctly on .NET 9. `Agora.Mod.csproj` overrides `RunModPostProcessor` and
`RunModPublisher` to pass that as an `Exec`-scoped environment variable. Scoped deliberately —
setting it globally would change version resolution for every other dotnet app on the machine.

The alternative is installing the out-of-support .NET 6 runtime. Roll-forward was chosen so the repo
does not depend on EOL software.

## 5. Also confirmed

- `Game.dll`, `Colossal.Core.dll` reference `netstandard 2.1.0.0`; none carries a
  `TargetFrameworkAttribute`. The Unity Mono runtime serves both `net48` and netstandard2.1 mods, so
  either target loads — `net48` is simply the sanctioned one.
- `Game.dll` contains **745 `GameSystemBase` subclasses**. That is the sensor surface for M1.
- No type in any shipped assembly implements `Game.Modding.IMod` — mods are strictly external.
- `Game.Simulation.TimeSystem.startingYear` is `get` **+ public `set`**, re-verified independently
  via `tools/api-query.ps1`. `politicsmodplan.md` §13.2's clock-patch risk may be retirable without
  Harmony; M1 must test this before writing a patch.

## 6. Reproducing all of this

```powershell
.\tools\api-query.ps1 -Enum Game.City.CityModifierType
.\tools\api-query.ps1 -Members Game.Simulation.TimeSystem -Public
.\tools\api-query.ps1 -Implements GameSystemBase -Assembly Game
.\tools\decompile.ps1          # refsrc/, ~2 min, 5209 .cs files
.\tools\verify-setup.ps1 -Build
```

`api-query.ps1` needs no decompiler — `Colossal.Mono.Cecil.dll` ships with the game. Use `refsrc/`
only for method *bodies*.

## 7. Still unverified

Nothing in this report has been observed **inside a running game**. It describes the build surface
only. The M0 gate in `docs/status.md` remains entirely unticked, and the UI bundle has never been
built, deployed, or rendered.
