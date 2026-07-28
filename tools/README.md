# tools/

Dev scripts and the graphify port (owner: Serph).

Nothing here yet. Candidates as the project grows:

- **`decompile.ps1`** — run `ilspycmd` over `Game.dll` and `Colossal.*.dll` into `refsrc/`
  (gitignored). Rerun after each game update; Scout's findings are only as current as this tree.
- **`api-query.ps1`** — the Cecil metadata query used to build `docs/scout/0001-api-index.md`.
  Enumerating types, members, enum values and constructor arity needs no decompiler, because
  `Colossal.Mono.Cecil.dll` ships with the game.
- **graphify port** — knowledge-graph tooling over the repo and the decompiled tree.
