---
name: schema-change
description: Change an Agora data contract safely — version bump, sidecar migration, both-sides sync between C# and the LLM prompt, doc update. Use whenever a contract's shape changes.
---

# /schema-change

Agora has four contracts, and each has more than one side. Changing one side only is the failure
this workflow exists to prevent.

| Contract | Sides that must agree |
|---|---|
| `snapshot.json` | `CitySnapshot` C# · `data/schemas/` · the LLM prompt |
| `politics_flavor.json` | `FlavorPayload` C# · `data/schemas/` · the LLM prompt |
| `timeline_*.json` | catalog files · `data/schemas/` · effect palette registry |
| ui bindings | `UISystemBase` C# · `ui/src` TSX · `docs/contracts/ui_bindings.md` |

## Steps

1. **Bump `schemaVersion`** on the contract. Every one carries it (§2.9).

2. **Write the migration** for existing sidecars. Loading an older version must upgrade in memory
   and continue — never reset politics, never crash, never silently drop a field. Someone has a
   thirty-year save; it has to keep working.

3. **Sync every side** from the table above in the same change. A snapshot field added in C# but
   missing from the prompt means the LLM writes prose about a city it cannot see.

4. **Update `data/schemas/`** so the schema suite validates the new shape, and confirm the suite
   still rejects the things it is supposed to — particularly a numeric field smuggled into
   `politics_flavor.json`.

5. **Test the migration** with a fixture at the old version. An untested migration is a guess.

## The binding contract is different

UI bindings have no compile-time check in either direction — rename one side and the panel renders
nothing, with no error anywhere. So:

- Register in `docs/contracts/ui_bindings.md` **before** implementing.
- **Never rename in place.** Add the new name, migrate the consumer, remove the old one in a later
  change. Renaming both sides at once works on your machine and breaks anyone mid-update.
