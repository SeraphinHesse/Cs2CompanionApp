# tools/

Dev scripts (owner: Serph).

All PowerShell, all ASCII-only on purpose — Windows PowerShell 5.1 reads a BOM-less UTF-8 `.ps1` as
ANSI, so a stray em-dash inside a string literal becomes a parse error.

## `verify-setup.ps1`

Checks every precondition and prints an actionable fix for each failure. Run it first when anything
is off, and again after any toolchain reinstall.

```powershell
.\tools\verify-setup.ps1          # fast checks only
.\tools\verify-setup.ps1 -Build   # also builds and runs the Core suite
```

It reads the `CSII_*` variables from **both** the registry and the current process, because a shell
opened before the toolchain installed will not see them. That mismatch is the most confusing failure
mode in this project: everything is installed correctly and the build still cannot find it.

## `api-query.ps1`

Type, member and enum metadata straight out of the game assemblies. **Reach for this first.**
`Colossal.Mono.Cecil.dll` ships with the game, so names, signatures, enum values and constructor
arity need no decompiler.

```powershell
.\tools\api-query.ps1 -Type TimeSystem
.\tools\api-query.ps1 -Members Game.Simulation.TimeSystem -Public
.\tools\api-query.ps1 -Enum Game.City.CityModifierType
.\tools\api-query.ps1 -Implements GameSystemBase -Assembly Game
```

`-Members` flags public property setters as `SET(public)`. Not cosmetic: a public setter on an engine
type can retire a Harmony patch, which is how `TimeSystem.startingYear` was found.

`-Implements` matches names **exactly**. Substring matching turned a search for `IMod` into a list of
`IModifierType` hits, which is worse than no answer. Use `-Type` when you want fuzzy.

This script produced `docs/scout/0001-api-index.md` and `docs/scout/0002-modding-toolchain.md`.

## `decompile.ps1`

Decompiles `Game.dll` plus the `Colossal.*` assemblies into `refsrc/` (gitignored, ~5,200 `.cs` files,
~2 minutes). Only needed for method **bodies** — what the code does, rather than what it is called.

```powershell
.\tools\decompile.ps1
.\tools\decompile.ps1 -Force -Only Game
```

Requires `ilspycmd`:

```powershell
dotnet tool install -g ilspycmd --version 9.1.0.7988
```

Pin that version. The unpinned `latest` package is currently broken — `DotnetToolSettings.xml` is
missing from the NuGet package and installation fails outright.

**Rerun after every game update.** Scout's findings are only as current as this tree.

## Planned

- **graphify port** — knowledge-graph tooling over the repo and the decompiled tree.
