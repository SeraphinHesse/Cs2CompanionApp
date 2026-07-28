# data/seeds/

Name pools, faction archetypes and outlet names.

These are the **fallback flavor** used when the LLM is unavailable (non-negotiable #7: fail closed,
keep going). They are also what the post-v3 `StaticPoolProvider` draws from once the project moves
away from a live LLM (§3).

Selection from these pools is a stochastic draw like any other and goes through
`StreamNames.NameSelection` — never `System.Random`. Two players with the same save GUID and date
must get the same fallback names.

Populated in M3, when `IFlavorProvider` and its fallback path land.

Planned files:

- `party_names_eu.json` — name fragments for proportional-system parties
- `party_names_na.json` — name fragments for two-party-dominant systems
- `faction_archetypes.json` — the 2–4 factions inside an NA party: demographic base, demands, leader-name pools
- `outlets.json` — news outlet names and their tonal leanings
