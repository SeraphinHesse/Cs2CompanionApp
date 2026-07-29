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

**Not yet verified: nothing has run inside the game.** Every claim above is about build artifacts. The
UI panel has never rendered, no heartbeat has ever been logged, and the settings page has never been
drawn. The gate below is entirely untested.

Run `.\tools\verify-setup.ps1 -Build` for the current state of all preconditions.

### M0 gate — manual checklist

Launch with `--developerMode --uiDeveloperMode`, then:

Both halves of the mod are built and deployed to `…\Mods\Agora.Mod\`, so every item below is
testable now:

- [ ] Agora appears in the game's mod list
- [ ] Options page renders with readable labels (not raw keys)
- [ ] Master toggle flips without exception
- [ ] `Player.log` shows exactly one Agora heartbeat line per in-game day
- [ ] Debug panel renders and its day counter ticks with the sim clock
- [ ] Toggling the mod off mid-session stops the heartbeat, with no exceptions

A city must be loaded and unpaused — the heartbeat is driven by the sim clock, so nothing is logged
on the main menu.

`Player.log` lives at
`%USERPROFILE%\AppData\LocalLow\Colossal Order\Cities Skylines II\Player.log`.

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
