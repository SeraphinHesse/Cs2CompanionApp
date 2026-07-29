# AGORA — Status

**Current milestone:** M0 · Bootstrap
**Updated:** 2026-07-30

---

## M0 · Bootstrap

| Task | Status |
|---|---|
| Repo scaffold + folder structure | ✅ |
| Root router `CLAUDE.md` + per-folder contexts | ✅ |
| Scout report 0001 (API index) | ✅ |
| Scout report 0002 (modding toolchain build surface) | ✅ |
| `tools/` — verify-setup, api-query, decompile | ✅ |
| `Agora.Core` + determinism kernel | ✅ |
| `Agora.Core.Tests` determinism suite | ✅ |
| `Agora.Mod` — `IMod`, settings, day heartbeat | ✅ |
| `Agora.Mod` — UI binding system | ✅ |
| `ui/` React panel — source + build | ✅ bundle builds and deploys |
| Skills (§9) | ✅ |
| Agent definitions (§10) | ✅ |
| **Modding toolchain installed** | ✅ all 15 `CSII_*` set, 12 Entities analyzers present |
| Toolchain build integration (`Mod.props` / `Mod.targets`) | ✅ |
| Deploy to local `Mods/` folder | ✅ `…\Mods\Agora.Mod\` |
| `refsrc/` decompiled reference tree | ✅ 5,209 `.cs` files |
| In-game verification | ⬜ **the only thing left in M0** |

**Verified so far.** A single `dotnet build Agora.sln` succeeds with 0 warnings and 0 errors and
deploys **both halves** of the mod to `…\Mods\Agora.Mod\`: `Agora.Mod.dll`, `Agora.Core.dll`,
`Agora.Mod.mjs`, `Agora.Mod.css`. Fallback mode (`-p:UseCsiiToolchain=false`) also builds clean.
`dotnet test tests\Agora.Core.Tests\Agora.Core.Tests.csproj` passes 22/22 in ~40 ms with no game
assemblies involved — the check that the Core/Mod split is real. The deployed
`Agora.Mod.dll` was confirmed by metadata inspection to be `.NETFramework,v4.8` and to expose
`AgoraMod : IMod`, `AgoraHeartbeatSystem : GameSystemBase`, and `AgoraDebugUISystem : UISystemBase`.

**First in-game evidence, 2026-07-30 01:32.** The game was launched to the settings screen only, with
no city loaded and **without** the dev flags:

```
Modding.log  Loaded Agora.Mod,  Version=1.0.0.0 in 49.8005ms
Modding.log  Loaded Agora.Core, Version=1.0.0.0 in 0.4558ms
Modding.log  Registered UI Module {"m_ModuleId":"Agora.Mod","m_Author":"Serph", …}
             from [assetdb://user/Mods/Agora.Mod/Agora.Mod.mjs]
Agora.log    Agora loading. / Agora asset: …\Mods\Agora.Mod\Agora.Mod.dll / Agora loaded.
Agora.log    Agora unloading.        (clean dispose, both assemblies, no exception)
```

So both halves load, and **neither dev flag is needed for that** — they only add the dev menu and the
UI debugger. Alongside ~20 other installed mods, with no conflict.

**Still unverified:** the heartbeat has never fired, the panel has never rendered, and the toggle has
never been flipped. All three need a loaded, unpaused city.

Run `.\tools\verify-setup.ps1 -Build` for the current state of all preconditions.

### M0 gate — manual checklist

- [x] Agora appears in the game's mod list
- [x] Options page renders with readable labels (not raw keys) — two toggles seen
- [x] UI module registers and the bundle is found by the asset database
- [ ] Master toggle flips without exception
- [ ] `Logs\Agora.log` shows exactly one heartbeat line per in-game day
- [ ] Debug panel renders and its day counter ticks with the sim clock
- [ ] Toggling the mod off mid-session stops the heartbeat, with no exceptions

The last four need a city **loaded and unpaused** — the heartbeat is driven by the sim clock, so
nothing is logged on the main menu.

**Where to look.** `Colossal.Logging` gives every logger its own file, so Agora's output does **not**
go to `Player.log` — grepping that file for "Agora" returns nothing even on a healthy run. Ours is:

```
%USERPROFILE%\AppData\LocalLow\Colossal Order\Cities Skylines II\Logs\Agora.log
```

`Logs\Modding.log` is the other one worth reading: it records assembly load times, UI module
registration, and dispose. A mod that fails to load says so there, not in `Agora.log`.

---

## Blocked / needs a decision

1. **Nothing blocks the M0 gate.** It needs a human at the keyboard: set the Steam launch options to
   `--developerMode --uiDeveloperMode`, load a city, and walk the checklist above.
2. **Effect palette rescope.** Scout 0001 §3 found no enum support for RCI demand, rent/land value,
   birth rate, or subsidies, and district scope has only 14 modifiers. `politicsmodplan.md` §7 needs
   a pass before M5.
3. **`politicsmodplan.md` §14 open decisions** remain open: NA primaries, timeline jitter, snapshot
   retention, post-2026 authorship, unrest ceiling.

## Known toolchain quirks (all worked around; see `docs/scout/0002-modding-toolchain.md`)

- `ModPostProcessor.exe` / `ModPublisher.exe` target **.NET 6, which is EOL and not installed here.**
  `Agora.Mod.csproj` overrides both targets to pass `DOTNET_ROLL_FORWARD=LatestMajor` scoped to the
  `Exec`. Re-sync those overrides if a toolchain update changes them.
- **`Agora.Core` is pinned to netstandard2.0** because toolchain mode builds `Agora.Mod` as `net48`,
  which cannot reference netstandard2.1.
- `CSII_LOCALMODSPATH` is set before the folder exists. Never gate a build step on that folder
  existing — it will skip silently forever.
- A shell opened **before** the toolchain install sees no `CSII_*` variables. `Mod.props` dodges this
  by reading the registry directly; our own scripts check both.

## Next milestone

**M1 · Time & Truth** — `AgoraTimeService`, the clock patch (gated on a complete date-surface
enumeration from Scout 0002), sensor pass 1, sidecar IO, determinism test suite.
