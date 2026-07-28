---
name: review-checklist
description: The blocking review checklist for Agora. Run this on every task before approving. Encodes politicsmodplan.md §2 non-negotiables plus the failure modes that are cheap to introduce and expensive to find later.
---

# Review checklist

Verdict is **approve** or **block with required changes**. There is no "approve with nits" — either
the change is correct or it comes back. Work through every section; a pass that skips sections is
worth nothing.

## 1. LLM is flavor-only (§2.1)

- [ ] No number entering engine state originates from LLM output.
- [ ] Every field on `FlavorPayload` and its members is text, an id, or a date.
- [ ] LLM output is schema-validated before use, and validation failure keeps the last good state.

**Block on:** any parse of a number out of LLM text, however well-guarded.

## 2. No naked randomness (§2.2)

- [ ] No `System.Random`, `Guid.NewGuid()`, `DateTime.Now`, `Environment.TickCount` in `Agora.Core`.
- [ ] Every stochastic draw comes from `SeedStreams.Rng` or `SeedStreams.RngFor`.
- [ ] Stream names are constants in `StreamNames`, not string literals at the call site.
- [ ] Per-entity draws use `RngFor`, not repeated draws from one stream inside a loop.

**Why the loop rule:** drawing repeatedly from a single stream couples each result to iteration
order, so adding one district silently changes every later district's outcome.

## 3. Determinism (§2.3)

- [ ] No iteration over `Dictionary` or `HashSet` where order affects output. Sorted explicitly?
- [ ] No float accumulation across an unordered collection.
- [ ] New engine behaviour has a same-seed-twice test.
- [ ] Any change to `SeedStreams` internals is deliberate, and the golden-value test was updated
      *knowingly* — this rewrites the political history of every existing save.

**This is the most commonly missed section.** Dictionary iteration order is the classic silent
desync: stable within a run, different across runs, invisible in review unless looked for.

## 4. The assembly boundary

- [ ] `Agora.Core` gained no reference to `Game.*`, `Colossal.*`, `Unity.*`, `UnityEngine`.
- [ ] `Agora.Core.Tests` still passes with the game uninstalled.
- [ ] No political computation leaked into `Agora.Mod`.

## 5. Effects (§2.4, §2.5, §7)

- [ ] The effect maps to a `DistrictModifierType` / `CityModifierType` member, or its Harmony
      approach is justified in writing.
- [ ] Scope, magnitude cap, duration cap and fallback are all declared.
- [ ] A cap test exists and actually drives the value past the cap.
- [ ] Nothing creates or modifies districts, zoning, buildings or terrain.

## 6. Persistence (§2.6)

- [ ] Writes are atomic — temp file then rename, never write-in-place.
- [ ] Sidecar is keyed on Agora's own save GUID, not a filename or path.
- [ ] Load handles a missing exact match by reconciling, never by resetting or throwing.

## 7. Schema (§2.9)

- [ ] Contract shape changes bumped `schemaVersion`.
- [ ] Both sides synced: C# structs *and* the LLM prompt *and* `data/schemas/`.
- [ ] Sidecar migration written for existing saves.
- [ ] New UI bindings registered in `docs/contracts/ui_bindings.md` **before** implementation.

## 8. Harmony patches

- [ ] Target list came from a dated scout report, not from inference.
- [ ] Prefix vs postfix choice is explained.
- [ ] Unpatch path proven to restore stock behaviour.
- [ ] Behind a kill-switch if it touches a surface the base game owns.

## 9. Fail-closed behaviour (§2.7)

- [ ] Missing CLI, timeout and malformed JSON each keep last good state, log, and continue.
- [ ] Nothing blocks the simulation thread waiting on the LLM.
- [ ] Systems no-op cleanly when the master toggle is off, rather than unregistering.
