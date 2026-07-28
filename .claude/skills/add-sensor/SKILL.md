---
name: add-sensor
description: Add a city or district metric to Agora's snapshot — ECS query, contract field, schema bump, dashboard binding, test. Use when the engine needs a metric it cannot currently see.
---

# /add-sensor

A sensor reads the game and writes a plain number into `CitySnapshot`. It contains no political
logic — if you find yourself weighting or interpreting the value, that belongs in `Agora.Core`.

## Steps

1. **Confirm the source exists.** Grep the newest `docs/scout/` report for the component or system.
   If it is not there, grep `refsrc/`. If it is in neither, stop and write a scout finding first —
   do not infer a component's existence from its name.

2. **Add the contract field** in `src/Agora.Core/Contracts/CitySnapshot.cs`. Document the unit and
   the range in the XML comment. Bump `SchemaVersion` and run `/schema-change` if the shape changed.

3. **Write the sensor** in `src/Agora.Mod/Sensors/`, one `GameSystemBase` per metric *family* — not
   per metric. Build the `EntityQuery` in `OnCreate`, never per-frame. Choose `SystemUpdatePhase`
   deliberately and comment why.

4. **Handle absence.** Per-district metrics are best-effort by design. If the value cannot be
   resolved for a district, fall back to the city figure and set `HasCityFallbacks = true`. Never
   throw, and never silently emit a city number as if it were local.

5. **Bind it to the dashboard** if it should be visible: register in `docs/contracts/ui_bindings.md`
   first, then publish from a `UISystemBase`.

6. **Test it.** Sensors themselves need the game, so the test goes on whatever consumes the value in
   `Agora.Core` — with a synthetic `CitySnapshot` fixture.

## Traps

- **Getters run every UI tick.** Never run an `EntityQuery` inside a `GetterValueBinding`. Cache in
  a simulation system and expose the cached field.
- **District association is not free.** Confirm whether citizens resolve to districts directly or
  need a household → building → district walk. Scout 0002 question 4 tracks this.
- **Ordering matters.** `CitySnapshot.Districts` must be ordered by id. ECS chunk order is not
  stable, and an unstable order makes engine output depend on memory layout.
