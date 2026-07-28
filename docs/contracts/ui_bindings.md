# Contract — C# ↔ UI bindings

**schemaVersion: 1**

The fourth data contract. It spans two languages and two build systems, so nothing checks it at
compile time: rename a binding on one side and the panel silently renders nothing. Every binding
must be listed here, and this file is the authority when the two sides disagree.

## Naming

`agora.<area>.<name>` — lowercase, dot-separated. The group prefix `agora` is reserved for this mod.

## Registered bindings

| Binding | Kind | C# type | Publisher | Consumer | Since |
|---|---|---|---|---|---|
| `agora.debug.simDay` | `GetterValueBinding<int>` | `int` | `UiBindings/AgoraDebugUISystem.cs` | `ui/src/panels/DebugPanel.tsx` | M0 |
| `agora.debug.enabled` | `GetterValueBinding<bool>` | `bool` | `UiBindings/AgoraDebugUISystem.cs` | `ui/src/panels/DebugPanel.tsx` | M0 |

## Rules

1. **Register here first, implement second.** A binding not in this table does not exist.
2. **Never rename in place.** Add the new name, migrate the consumer, then remove the old one in a
   later change. Renaming both sides in one commit works locally and breaks anyone mid-update.
3. **Complex payloads implement `IJsonWritable`.** Do not hand-serialize to a JSON string and parse it
   on the JS side — that defeats the binding layer's change tracking.
4. **Bindings are a view, never a channel for engine state.** The UI reads politics; it does not
   compute or mutate it. `TriggerBinding` may request an action (e.g. "wake the LLM now"), but the
   engine decides what happens.
5. **Update cost matters.** `GetterValueBinding` re-evaluates on the UI update tick — keep getters
   cheap, and never run an `EntityQuery` inside one. Cache in a system, expose the cached value.
