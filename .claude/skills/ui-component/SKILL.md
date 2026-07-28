---
name: ui-component
description: Add a dashboard widget to Agora's Gameface UI — binding registration, C# publisher, React consumer, flexbox-only styling. Use when building any dashboard surface.
---

# /ui-component

The CS2 interface is Coherent Gameface — an embedded browser running React. Widgets are real web
components, with two hard constraints that catch people out.

## Constraint 1 — flexbox only

**CSS grid is not supported.** `display: grid` fails silently: no error, no layout. Every layout in
this project is flex.

## Constraint 2 — the binding contract has no compile-time check

Rename a binding on one side and the panel renders nothing, with no error in either build. So the
order is fixed:

1. **Register in `docs/contracts/ui_bindings.md` first** — group, name, C# type, publisher, consumer.
2. **Publish from C#** in `src/Agora.Mod/UiBindings/`:
   ```csharp
   AddUpdateBinding(new GetterValueBinding<int>("agora.<area>", "<name>", Getter));
   ```
3. **Consume in TSX**:
   ```tsx
   const value$ = bindValue<number>("agora.<area>", "<name>", 0);
   const value = useValue(value$);
   ```
   Always pass the fallback — without it the panel renders `undefined` on the first frame.

## Which binding type

| Need | Use |
|---|---|
| Value the UI reads | `GetterValueBinding<T>` |
| Value pushed on change | `ValueBinding<T>` |
| UI calls C#, no return | `TriggerBinding` |
| UI calls C# and awaits a result | `CallBinding<…>` |
| Complex payload | implement `IJsonWritable` — do not hand-serialize to a JSON string |

## Performance

Getters run on **every UI update tick**. Never run an `EntityQuery` inside one. Compute in a
simulation system, cache the result, and have the getter return the cached field.

## Mounting

Prefer `moduleRegistry.append` at a hook point (`GameTopLeft`, `GameTopRight`, `GameBottomRight`,
`UniversalModMenu`) over `override`. Append adds without replacing game code and survives game
updates that would break an override.

Use `cs2/ui` components (buttons, panels, dialogs, scrollables, tooltips) over hand-rolled markup so
the dashboard reads as native rather than as a mod bolted on.

## Debugging

Launch with `--uiDeveloperMode`, open `http://localhost:9444/`. `npm run dev` hot-reloads against
the running game.
