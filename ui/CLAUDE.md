# ui/ — the dashboard (React + TypeScript on Coherent Gameface)

The CS2 interface is not Unity UI. It is Coherent Gameface — an embedded browser running React with
SCSS, bundled by webpack. This folder is a **separate npm subproject with its own build**, linked to
the C# mod by `mod.json` (`id` must equal `Agora.Mod`'s assembly/target name).

## Gameface constraints — these bite

- **Flexbox only. CSS grid is not supported.** Lay everything out with flex.
- Assume a restricted browser: no arbitrary network access, no DOM APIs beyond what Gameface ships.
- Debug with the game launched using `--uiDeveloperMode`, then open `http://localhost:9444/`.

## Talking to C#

The C# side publishes bindings from a `Game.UI.UISystemBase` in `src/Agora.Mod/UiBindings/`, using
`Colossal.UI.Binding`:

| C# | Purpose | JS side (`cs2/api`) |
|---|---|---|
| `ValueBinding<T>` | push a value the UI reads | `bindValue` + `useValue` |
| `GetterValueBinding<T>` | value recomputed on update | `bindValue` + `useValue` |
| `TriggerBinding` | UI calls into C#, no return | `trigger` |
| `CallBinding<…>` | UI calls C# and awaits a result | `call` |

Complex payloads implement `Colossal.UI.Binding.IJsonWritable` on the C# side.

**Every binding must be recorded in `docs/contracts/ui_bindings.md`.** This contract spans two
languages and two build systems, which makes it the one most likely to drift silently — a renamed
binding fails at runtime with an empty panel, not at compile time.

## Packages

- `cs2/api` — `bindValue`, `useValue`, `trigger`, `call`
- `cs2/bindings` — the game's own bindings, for reading existing state
- `cs2/ui` — the game's component library: buttons, panels, dialogs, scrollables, tooltips

Prefer `cs2/ui` components over hand-rolled markup so the dashboard looks native.

## Build

```
npm install
npm run build     # production bundle
npm run dev       # watch + hot reload (game must run with --uiDeveloperMode)
```

Node: the CS2 UI templates target **Node 20.11**. If the local Node is much newer and the build
misbehaves, pin with nvm rather than fighting webpack.
