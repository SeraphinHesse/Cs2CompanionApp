---
name: add-effect
description: Add an entry to Agora's sanctioned effect palette — map to a game modifier enum, implement with caps and a fallback, register by ID, prove the cap in a test. Use when an event or mandate needs a new gameplay consequence.
---

# /add-effect

Effects are the only way Agora touches the city. The palette is a closed registry (§7): events and
mandates reference effects by ID and can never reach past them.

## Step 0 — map to the game's own enum, first

Before writing anything, check `docs/scout/0001-api-index.md` §3 for a matching member of
`Game.Areas.DistrictModifierType` (14 members) or `Game.City.CityModifierType` (40 members).

A mapped effect needs no Harmony, is already capped by the simulation, and already serializes with
the save. An unmapped one needs a patch, and patches are where mods break on game updates.

**If there is no matching member,** stop and write down: what the effect needs, why no member fits,
and what the Harmony approach would be. That decision goes to Master, not into the code. §7's gap
list already names the known-unmapped ones — RCI demand, rent/land value, birth rate, subsidies.

## Steps

1. **Declare the entry**: ID, scope (city|district), magnitude cap, duration cap, fallback effect ID.
   Default fallback is a happiness modifier. An effect with no fallback does not ship.

2. **Implement** in `src/Agora.Mod/Effects/`, applying the enum member. The implementation clamps —
   the engine may request any magnitude, and clamping at the sink is what makes the cap unbypassable.

3. **Register by ID** in the palette registry so catalog validation can see it.

4. **Cap test** in `tests/`. Drive the magnitude and duration well past the cap and assert the
   clamped result. A test that only exercises in-range values proves nothing.

5. **Document** one line in `politicsmodplan.md` §7.

## Traps

- **Stacking is unverified.** Whether multiple modifier sources combine additively or
  multiplicatively is Scout 0002 question 7. Until it is answered, assume the worst case and cap
  conservatively.
- **District scope is thin.** 14 members, with no pollution, land value, education, or general
  happiness. Most district-scoped ideas end up expressed through `Wellbeing` and `CrimeAccumulation`.
- **Never cut an event for a missing effect.** Degrade along the fallback chain instead (§13.5).
