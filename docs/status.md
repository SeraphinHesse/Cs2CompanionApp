# AGORA — Status

**Current milestone:** M0 · Bootstrap
**Updated:** 2026-07-28

---

## M0 · Bootstrap

| Task | Status |
|---|---|
| Repo scaffold + folder structure | ✅ |
| Root router `CLAUDE.md` + per-folder contexts | ✅ |
| Scout report 0001 (API index) | ✅ |
| `Agora.Core` + determinism kernel | ✅ |
| `Agora.Core.Tests` determinism suite | ✅ |
| `Agora.Mod` — `IMod`, settings, day heartbeat | ✅ |
| `Agora.Mod` — UI binding system | ✅ |
| `ui/` React panel — **source only** | 🟨 build config pending toolchain (see `ui/README.md`) |
| Skills (§9) | ✅ |
| Agent definitions (§10) | ✅ |
| **Modding toolchain installed** | ⬜ **blocked on user** |
| Deploy to local `Mods/` folder | ⬜ blocked — folder does not exist until the toolchain installs |
| `refsrc/` decompiled reference tree | ⬜ |
| In-game verification | ⬜ blocked on the three above |

**Verified so far:** `dotnet build Agora.sln` succeeds with 0 warnings and 0 errors.
`dotnet test tests\Agora.Core.Tests\Agora.Core.Tests.csproj` passes 22/22 in ~40 ms, with no game
assemblies involved — which is the check that the Core/Mod split is real. `Agora.Mod.dll` was
confirmed by metadata inspection to expose one `IMod` implementation and two registered systems.

**Not yet verified:** nothing has run inside the game. The UI panel has never rendered.

### M0 gate — manual checklist

Launch with `--developerMode --uiDeveloperMode`, then:

- [ ] Agora appears in the game's mod list
- [ ] Options page renders with readable labels (not raw keys)
- [ ] Master toggle flips without exception
- [ ] `Player.log` shows exactly one Agora heartbeat line per in-game day
- [ ] Debug panel renders and its day counter ticks with the sim clock
- [ ] Toggling the mod off mid-session stops the heartbeat, with no exceptions

`Player.log` lives at
`%USERPROFILE%\AppData\LocalLow\Colossal Order\Cities Skylines II\Player.log`.

---

## Blocked / needs a decision

1. **Modding toolchain not installed.** Game → Options → Modding. Until then the build uses direct
   Managed-DLL references (which works), but there is no UI template, no bundled Harmony, and no
   one-click publish.
2. **Effect palette rescope.** Scout 0001 §3 found no enum support for RCI demand, rent/land value,
   birth rate, or subsidies, and district scope has only 14 modifiers. `politicsmodplan.md` §7 needs
   a pass before M5.
3. **`politicsmodplan.md` §14 open decisions** remain open: NA primaries, timeline jitter, snapshot
   retention, post-2026 authorship, unrest ceiling.

## Next milestone

**M1 · Time & Truth** — `AgoraTimeService`, the clock patch (gated on a complete date-surface
enumeration from Scout 0002), sensor pass 1, sidecar IO, determinism test suite.
