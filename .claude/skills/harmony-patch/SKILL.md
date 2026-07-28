---
name: harmony-patch
description: Write a Harmony patch for Agora — enumerate targets from a scout report, choose prefix vs postfix, add a kill-switch, prove the unpatch path. Use only after confirming no non-patch approach exists.
---

# /harmony-patch

Patches are the most fragile thing in a mod: they break on game updates, they leak into surfaces you
did not intend, and they are the usual cause of "works alone, breaks with other mods."

**Harmony does not ship with the game.** It comes from the modding toolchain or `Lib.Harmony`, and
must be shipped alongside the mod.

## Step 0 — is a patch actually needed?

Check for a public API first. This is not a formality: Scout 0001 found `TimeSystem.startingYear`
has a **public setter**, which may deliver the entire 1990 start year with no patch at all — the
plan had assumed a mandatory patch across every date surface.

Grep `refsrc/` for a public member before concluding a patch is required. Write down what you
checked; "no API exists" is a claim that needs evidence.

## Steps

1. **Enumerate every target** from a dated `docs/scout/` report. Every call site, not just the one
   you noticed. A patch applied to three of five date surfaces is worse than no patch — it produces
   a UI that disagrees with itself.

2. **Choose prefix vs postfix and say why.** Postfix to adjust a result. Prefix to skip or replace,
   which is far more invasive. If you need a prefix returning `false`, justify it explicitly.

3. **Kill-switch.** Any patch touching a base-game surface reads a setting and passes through
   unchanged when disabled. The player must be able to turn Agora off and get their game back.

4. **Prove the unpatch path.** `OnDispose` unpatches, and toggling off mid-session restores stock
   behaviour. Verify in-game — not by reading the code.

5. **Leak checklist.**
   - Does the patch fire in the main menu, the editor, or map view? Should it?
   - Does it fire for non-player entities?
   - Does it allocate per-call in a hot path?
   - Does it hold a reference that survives a save load?
   - Does it assume a loaded city, and what happens when there is not one?

6. **Document** in the patch class's XML comment: target, why a patch was necessary, what was ruled
   out, and which scout report enumerated the targets.

## Conventions

Patches live in the folder of the concern they serve (`Time/` for the clock patch), not in a
`Patches/` bucket — grouping by mechanism instead of by purpose scatters related code.
