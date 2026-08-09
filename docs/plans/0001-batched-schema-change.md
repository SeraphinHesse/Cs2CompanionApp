# Plan 0001 — Batched `/schema-change`: per-save settings, party overrides, article limits

**Written:** 2026-08-08
**Mandated by:** `fixplan.md` §Sequencing — *"Batch them: **one** `/schema-change` pass covering
per-save settings (`ThemeLocked`, `PauseOnMajorNews`, `ShowAllReports`), `Party.PlayerOverrides`, and
the article length limits, rather than three separate sidecar migrations."*
**Consumers:** W3 (theme dialog), W4 (party editing), W5 (press). None of them can start until this
lands; all three are otherwise blocked on a sidecar shape that does not exist yet.

---

## 0. Scope, and what this pass deliberately does not do

**In scope — the contract half of three workstreams:**

1. `AgoraSettings` gains `ThemeLocked`, `PauseOnMajorNews` (default **on**), `ShowAllReports`
   (default **off**). Per-save, sidecar-backed (non-negotiable #10).
2. `Party` gains `PlayerOverrides`, a `[Flags]` enum carrying `NameLocked`, `DescriptionLocked`,
   `ColorLocked`.
3. `politics_flavor` article limits tighten: `headline` 140 → **90**, `body` 900 → **420**.

**Explicitly out of scope** — these are the consuming workstreams' work, not this pass's:

- The first-run EU/US dialog, the settings panel, `SimulationSystem.selectedSpeed` pausing (W3, W5).
- `ApplyProseNames` honouring the locks; the `agora.parties.rename` / `setDescription` / `setColor`
  bindings; the colour picker (W4).
- The article *prose* instruction rewrite — "lead with what happened", the ban on "residents say",
  `StaticPoolContent` template rewrites, election coverage sets, `--model` (W5). Only the two
  sentences in the prompt that state the new **limits** change here.
- `PartyDetailPayload` and the Parties tab (W6).
- Anything touching `SnapshotRetention` — that is `politicsmodplan.md` §14.3, still open.

**Blocked on nothing.** Every side of this change is Newtonsoft, file IO, JSON Schema and TypeScript
declarations. No game API is touched, so no scout report is required; `docs/scout/0001-api-index.md`
and `0002-modding-toolchain.md` cover nothing relevant to it. No §14 open decision gates any of it —
checked all six on `politicsmodplan.md:326-331`.

### Assumptions stated explicitly

- **`NameLocked` covers `Name` *and* `ShortName`.** They are one identity edit in the UI. Likewise
  **`DescriptionLocked` covers `Description` *and* `Slogan`.** `fixplan.md:208-210` names three
  flags but the LLM writes five fields; this mapping is the only one that leaves no field
  unaccounted for. It must be written into the enum's doc comment, because W4's enforcement point
  will be authored against it.
- **A `[Flags]` enum, not a nested object.** `LlmWakeCadence` (`PoliticalState.cs:67-75`) is the
  existing precedent and `AgoraJson`'s `StringEnumConverter` (`AgoraJson.cs:93`) already writes flags
  as a comma-separated member list. One string on the wire, no new `$defs` block, no null-vs-absent
  question. This is what "cheap to serialise" means here.
- **The tightening prunes, it does not truncate.** See §5.
- `politicsmodplan.md:154` ratifies `body(≤120 words)`. 420 characters is roughly 65–70 words —
  strictly *inside* the ratified bound, so this is not a re-litigation and §6 needs no edit.

---

## 1. Version bumps — current, new, and every place asserted or written

| Constant / literal | File:line | Now | New |
|---|---|---|---|
| `PoliticalState.SchemaVersion` initialiser | `src/Agora.Core/Contracts/PoliticalState.cs:130` | `1` | `2` |
| `AgoraSettings.SchemaVersion` initialiser | `src/Agora.Core/Contracts/PoliticalState.cs:84` | `1` | `2` |
| `SidecarSchema.CurrentStateVersion` | `src/Agora.Mod/Persistence/SidecarSchema.cs:107` | `1` | `2` |
| `SidecarSchema.CurrentSettingsVersion` | `src/Agora.Mod/Persistence/SidecarSchema.cs:108` | `1` | `2` |
| `SidecarSchema.CurrentFlavorCacheVersion` | `src/Agora.Mod/Persistence/SidecarSchema.cs:110` | `1` | `2` |
| `FlavorSchema.SupportedSchemaVersion` | `src/Agora.Mod/Llm/FlavorSchema.cs:29` | `1` | `2` |
| embedded flavor schema `"schemaVersion": { "const": 1 }` | `src/Agora.Mod/Llm/FlavorSchema.cs:47` | `1` | `2` |
| on-disk flavor schema `"schemaVersion": { "const": 1 }` | `data/schemas/politics_flavor.schema.json:13` | `1` | `2` |
| state schema root `"schemaVersion": { "const": 1 }` | `data/schemas/political_state.schema.json:390` | `1` | `2` |
| state schema `$defs/settings.schemaVersion` `const` | `data/schemas/political_state.schema.json:374` | `1` | `2` |
| binding contract header `**schemaVersion: 2**` | `docs/contracts/ui_bindings.md:3` | `2` | `3` |

`CurrentTimelineProgressVersion` (`SidecarSchema.cs:109`) stays at `1` — `timeline_progress.json` is
unchanged.

**Do not touch** the other five `"const": 1` literals in `political_state.schema.json` — lines
`174`, `210`, `237`, `265`, `304`. Those version `PollResult`, `ElectionResult`, `Coalition`,
`Mandate` and `TimelineEvent`, whose C# initialisers (`Elections.cs:42`, `Elections.cs:188`,
`Government.cs:52`, `Government.cs:178`, `TimelineEvent.cs:85`) are unchanged by this pass. Sweeping
them is the easy mistake here.

### Places that *read* a version constant and need no edit

Listed so the coder can confirm rather than search:

- `src/Agora.Mod/Persistence/SidecarStore.cs:383` — stamps `state.SchemaVersion` on write.
- `src/Agora.Mod/Persistence/SidecarStore.cs:385`, `:568` — stamp `settings.SchemaVersion` on write.
- `src/Agora.Mod/Persistence/SidecarStore.cs:58`, `:576` — timeline progress, unchanged.
- `src/Agora.Mod/Llm/FlavorValidator.cs:136-141` — asserts equality with
  `FlavorSchema.SupportedSchemaVersion`. **Stays strict.** A live CLI response must be exactly 2.
- `src/Agora.Mod/Llm/FlavorPromptBuilder.cs:250` — emits the constant into the prompt.
- `src/Agora.Mod/Llm/StaticPoolProvider.cs:122` — emits the constant into canned output.
- `src/Agora.Mod/Llm/FlavorDocument.cs:126` — `SchemaVersion == 0 ? SupportedSchemaVersion : …`.
- `src/Agora.Mod/Llm/NumericFieldScanner.cs:24` — `AllowedNumericPath = "$.schemaVersion"`.
- `src/Agora.Mod/UiBindings/AgoraUiProjection.cs:34` — copies `state.SchemaVersion` into
  `StateSummaryPayload`. The UI's `StateSummary.schemaVersion` becomes `2` with no code change;
  `EMPTY_STATE_SUMMARY.schemaVersion` stays `0` (`ui_bindings.md:308`) and must not be changed —
  `0` means "nothing published yet".

---

## 2. C# contract changes

### 2.1 `src/Agora.Core/Contracts/Parties.cs`

New type, placed immediately above `public sealed class Party` (currently `:66`):

```csharp
/// <summary>
/// Party fields the player has taken ownership of. A locked field is never rewritten by flavor
/// (fixplan W4): <see cref="IFlavorProvider"/> output for it is discarded, not merged.
///
/// <para>A flag set rather than loose booleans on <see cref="Party"/>: it is one string on the
/// wire, it adds no <c>$defs</c> block to the state schema, and it matches
/// <see cref="LlmWakeCadence"/>, the flags enum already in this contract.</para>
///
/// <para><b>Field mapping — this is the specification W4 enforces against.</b>
/// <see cref="NameLocked"/> covers <see cref="Party.Name"/> AND <see cref="Party.ShortName"/>;
/// <see cref="DescriptionLocked"/> covers <see cref="Party.Description"/> AND
/// <see cref="Party.Slogan"/>; <see cref="ColorLocked"/> covers <see cref="Party.ColorHex"/>.
/// Every flavor-owned string on <see cref="Party"/> is accounted for by exactly one flag.</para>
/// </summary>
[Flags]
public enum PartyOverrides
{
    None = 0,
    NameLocked = 1,
    DescriptionLocked = 2,
    ColorLocked = 4
}
```

`Parties.cs` currently has only `using System.Collections.Generic;` at `:1` — **add `using System;`**
for `[Flags]`.

New property on `Party`, appended after `RevivalCount` (`Parties.cs:143`):

```csharp
/// <summary>
/// Which of this party's flavor-owned fields the player has taken over. Player-owned, not
/// engine-owned and not flavor-owned: nothing in Agora.Core writes it, and flavor must not
/// overwrite a field whose flag is set.
/// </summary>
public PartyOverrides PlayerOverrides { get; set; } = PartyOverrides.None;
```

JSON property name: `playerOverrides`. Wire form: `"None"`, `"NameLocked"`,
`"NameLocked, ColorLocked"` — `AgoraJson.cs:84` camel-cases property names but `:93` adds
`StringEnumConverter` with **no** naming strategy, so member names stay Pascal-cased.

### 2.2 `src/Agora.Core/Engine/Parties/PartyRegistry.cs:154-183` — the trap

`PartyRegistry.Clone` is a hand-written field-by-field copy and is *not* mentioned anywhere in
`fixplan.md`. Adding a field to `Party` without adding it here means every engine pass that clones a
party silently clears the player's locks, and the symptom is "my rename came back a few months
later" — indistinguishable from W4 not working. Add, after `RevivalCount = source.RevivalCount`
(`:181`):

```csharp
                RevivalCount = source.RevivalCount,
                PlayerOverrides = source.PlayerOverrides
```

This is the single highest-risk line in the pass. It gets its own test (§6, test 19).

`PartyLifecycle.cs:525` and `:587` construct *new* `Party` objects for splits and revivals; a new
brand has no player edits, so `PartyOverrides.None` from the initialiser is correct there and no
edit is needed. `PartyRegistry.cs:276` (initial generation) likewise.

### 2.3 `src/Agora.Core/Contracts/PoliticalState.cs` — `AgoraSettings`

Three properties appended after `EffectsEnabled` (`:114`):

```csharp
        /// <summary>
        /// True once the region theme is history. Set at the first election (fixplan W3); before
        /// that the player may still change their mind from the settings surface.
        /// </summary>
        public bool ThemeLocked { get; set; } = false;

        /// <summary>
        /// Pause the sim and raise a modal when a major news item lands — elections, coalition
        /// formation or collapse, party founding or dissolution, timeline events at severity >= 3
        /// (fixplan W5). Default on.
        /// </summary>
        public bool PauseOnMajorNews { get; set; } = true;

        /// <summary>
        /// Raise a modal for *every* report, not just the major ones (fixplan W5). Default off:
        /// on a large city this interrupts constantly.
        /// </summary>
        public bool ShowAllReports { get; set; } = false;
```

JSON property names: `themeLocked`, `pauseOnMajorNews`, `showAllReports`. All three are per-save and
live only in the sidecar — nothing goes near `src/Agora.Mod/Core/` mod settings (non-negotiable #10).

Also update the `SchemaVersion` initialiser at `:84` to `2` and at `:130` to `2`.

---

## 3. The sidecar migration

### 3.1 A defect in the migration engine that must be fixed first

`SidecarSchema.Migrate` at `src/Agora.Mod/Persistence/SidecarSchema.cs:177-185`:

```csharp
            if (!hadVersion)
            {
                root[VersionProperty] = target;
                return new MigrationResult(MigrationOutcome.AssumedVersionOne, 1, target,
                    "No schemaVersion; assumed 1 and stamped as " + Format(target) + ".");
            }
```

It stamps the **target** version and returns without running a single step. Harmless while every
step table is empty (`:131-134`, and the class doc at `:94` says so). The moment this pass adds a
step it becomes silent data loss: an unversioned sidecar is labelled v2 while carrying v1 content,
so no party gets `playerOverrides` and no settings block gets the three new fields — and it can
never be repaired, because the file now claims to be current.

Fix: fall through to the chain instead of short-circuiting.

```csharp
            bool assumed = !hadVersion;
            if (assumed) version = 1;   // the shape that predates the field, by definition
```

…then run the existing `version == target` / `version > target` / step-loop logic unchanged, and at
the two success returns report `MigrationOutcome.AssumedVersionOne` when `assumed` is true (both the
"already current" return at `:189` and the "upgraded" return at `:229`). The generosity the original
comment describes is preserved; what changes is that the generosity now includes actually migrating
the thing.

Also rewrite the class doc paragraph at `:91-95` — "Currently every document is at version 1, so the
step tables are empty by design" becomes false in this pass.

### 3.2 A structural gap: the nested `settings` object is never migrated

`state_*.json` embeds the whole settings document at `#/settings` (`political_state.schema.json:397`,
`PoliticalState.cs:142`), and that nested object carries its **own** `schemaVersion`
(`political_state.schema.json:374`). But `SidecarSchema.Migrate` only ever reads and stamps the
**root** version (`:175`, `:226`). `SidecarStore.cs:385` repairs the nested version — but only on
*write*, after the object has already been materialised.

So the settings step table cannot be relied on to reach settings that arrive inside a state file.
The State 1→2 step must upgrade the nested object itself, by calling the same helper the standalone
Settings step uses. One helper, two call sites; never two implementations.

### 3.3 The steps, field by field

Add to `SidecarSchema.cs`, above the step tables:

```csharp
        /// <summary>
        /// Brings one settings object — standalone <c>settings.json</c> or the block nested in a
        /// state file — from v1 to v2. Idempotent: a property already present is left alone, so
        /// running it twice cannot change a value the player set.
        /// </summary>
        internal static void UpgradeSettingsObjectToV2(JObject settings)
        {
            if (settings == null) return;

            if (settings["themeLocked"] == null)       settings["themeLocked"] = false;
            if (settings["pauseOnMajorNews"] == null)  settings["pauseOnMajorNews"] = true;
            if (settings["showAllReports"] == null)    settings["showAllReports"] = false;

            settings[VersionProperty] = CurrentSettingsVersion;
        }
```

`SettingsSteps` (`:132`) gains exactly one entry:

```csharp
        private static readonly List<MigrationStep> SettingsSteps = new List<MigrationStep>
        {
            new MigrationStep(1, "added themeLocked, pauseOnMajorNews, showAllReports",
                root => UpgradeSettingsObjectToV2(root))
        };
```

`StateSteps` (`:131`) gains exactly one entry:

```csharp
        private static readonly List<MigrationStep> StateSteps = new List<MigrationStep>
        {
            new MigrationStep(1, "added party playerOverrides and the three per-save UI settings",
                MigrateStateV1ToV2)
        };

        private static void MigrateStateV1ToV2(JObject root)
        {
            // Parties: absent playerOverrides means "the player has taken nothing over".
            var parties = root["parties"] as JArray;
            if (parties != null)
            {
                foreach (JToken token in parties)
                {
                    var party = token as JObject;
                    if (party == null) continue;
                    if (party["playerOverrides"] == null) party["playerOverrides"] = "None";
                }
            }

            // Settings: the nested block carries its own version and the State chain is the only
            // thing that will ever reach it (see 3.2).
            var settings = root["settings"] as JObject;
            if (settings == null)
            {
                // A state file with no settings block. SidecarStore.ResolveSettings would fall back
                // to defaults anyway (SidecarStore.cs:339-355); writing them here makes the file
                // self-describing instead of relying on that fallback.
                settings = new JObject();
                root["settings"] = settings;
                settings["startYear"] = 1990;
                settings["theme"] = "Eu";
                settings["system"] = "Proportional";
                settings["wakeCadence"] = "Yearly, Election, Manual";
                settings["snapshotRetention"] = 25;
                settings["enabled"] = true;
                settings["effectsEnabled"] = true;
            }

            UpgradeSettingsObjectToV2(settings);

            // ThemeLocked refinement, available only in this document: W3 locks the theme at the
            // first election, and a save that has already held one is past that point. The
            // standalone settings.json step cannot make this call — it cannot see election history —
            // so it leaves themeLocked false and the runtime re-locks on the next election check.
            var history = root["electionHistory"] as JArray;
            if (history != null && history.Count > 0) settings["themeLocked"] = true;
        }
```

### 3.4 What an absent field defaults to on read of an old sidecar

| Document | Field | Absent in v1 → v2 value | Why |
|---|---|---|---|
| state | `parties[].playerOverrides` | `"None"` | The player cannot have locked a field on a build with no lock UI. |
| state / settings | `settings.themeLocked` | `false`, **or `true` if `electionHistory` is non-empty** | W3's rule is "locked at the first election". A save past its first election is past that point. |
| state / settings | `settings.pauseOnMajorNews` | `true` | Owner decision, `fixplan.md:289`. |
| state / settings | `settings.showAllReports` | `false` | Owner decision, `fixplan.md:289`. |
| state | `settings` (whole block absent) | full v2 default object | Matches `new AgoraSettings()`; §3.3 spells the values. |
| state | `settings.schemaVersion` | `2` | Set by `UpgradeSettingsObjectToV2`. |
| state | everything else | untouched | Test 5 in §6 asserts this by deep comparison. |

**Nothing is removed and nothing is renamed.** A v1 property this build does not know about survives
the DOM rewrite untouched — `Migrate` works on the `JObject`, and `AgoraJson.cs:69` sets
`MissingMemberHandling.Ignore` so materialising ignores it rather than throwing. That is the
"never silently drop a field" guarantee in the `/schema-change` skill, step 2.

**Never desync (non-negotiable #6).** Migration necessarily changes the SHA-256 of the serialized
state once — that is what a version bump *is*. The invariant that must hold afterwards is
idempotency: `Migrate(Migrate(d)) == Migrate(d)` byte for byte, so the fingerprint at sim date D is
stable across every subsequent reload. Every helper above is written to be idempotent (it only fills
absent properties) and test 6 in §6 asserts it. `Migrate` is also non-destructive on refusal:
`TooNew` and `NoPathForward` return before touching the DOM, and `SidecarStore.cs:281-288` already
declines to quarantine such a file.

---

## 4. Schema files (`data/schemas/`)

### 4.1 `data/schemas/political_state.schema.json`

Three edits, all inside objects that declare `additionalProperties: false` — so these are not
optional. A migrated file that carries `playerOverrides` while the schema does not declare it fails
validation.

- **`:390`** root `"schemaVersion": { "type": "integer", "const": 1 }` → `"const": 2`.
- **`$defs/party`, properties block `:83-105`** — add after `"revivalCount"` (`:105`):

  ```json
        "revivalCount": { "type": "integer", "minimum": 0 },
        "playerOverrides": {
          "type": "string",
          "$comment": "Flags enum, comma-separated member list. 'None' | any combination of 'NameLocked', 'DescriptionLocked', 'ColorLocked'. Player-owned: neither the engine nor the LLM writes it."
        }
  ```

  Do **not** add it to `required` (`:82`) — an absent `playerOverrides` must stay a legal document,
  or a pre-migration fixture becomes undescribable and the migration test cannot be written.

- **`$defs/settings` `:368-386`** — bump `:374` to `"const": 2` and add three properties after
  `"effectsEnabled"` (`:384`):

  ```json
        "effectsEnabled": { "type": "boolean" },
        "themeLocked": { "type": "boolean" },
        "pauseOnMajorNews": { "type": "boolean" },
        "showAllReports": { "type": "boolean" }
  ```

  Leave `required` (`:371`) as `["schemaVersion", "startYear", "theme", "system"]`. The three new
  booleans have defaults; requiring them would reject a hand-written fixture for no benefit.

### 4.2 `data/schemas/politics_flavor.schema.json`

- **`:13`** `"schemaVersion": { "type": "integer", "const": 1 }` → `"const": 2`.
- **`:58`** `"headline": { "type": "string", "maxLength": 140 }` → `"maxLength": 90`.
- **`:59`** `"body": { "type": "string", "maxLength": 900 }` → `"maxLength": 420`.

Unchanged, and worth stating so the coder does not tidy them: `outlet` 60 (`:57`), `partyFlavor.name`
80, `shortName` 12, `description` 600, `slogan` 120 (`:24-27`), `eventProse.localAngle` 900 (`:82`).
The tightening applies to articles only.

### 4.3 `src/Agora.Mod/Llm/FlavorSchema.cs:37-121` — the embedded copy

`data/` is not deployed (`FlavorSchema.cs:9-17`), so the embedded literal is the runtime authority.
Make the identical three edits at `:47`, `:92`, `:93`. `MatchesFile` (`:170`) compares meaning, not
whitespace, so formatting need not match byte for byte — but the constraints must.

---

## 5. The flavor tightening: what happens to already-cached entries

This is the part `fixplan.md:255-256` under-specifies, and getting it wrong re-creates the W2 bug it
is meant to fix.

### The failure if nothing else is done

`FileFlavorCache.Load` (`src/Agora.Mod/Llm/FileFlavorCache.cs`… actually `FlavorCache.cs:76-104`)
re-validates the raw cached bytes through the same `FlavorValidator` as a live response
(`FlavorCache.cs:87`). And `FlavorValidator.ValidateCore` treats a schema error as **fatal to the
whole document**:

```csharp
// src/Agora.Mod/Llm/FlavorValidator.cs:129-132
            if (errors.Count > 0)
            {
                return FlavorValidationResult.Failed(errors);
            }
```

Unlike the catalog check at `:164-251`, which drops individual entries precisely so that *"losing
one article to a hallucinated district is much better than losing a whole year of prose"*
(`:55-58`). So one 500-character body in an existing `flavor_cache.json` discards the entire file —
including every `partyFlavor` entry, i.e. every party **name**. The player reloads and sees
`party-01` again. That is exactly the defect W2 exists to kill.

### The resolution

**Prune the over-length articles at cache load; never truncate; never touch party flavor.**

New file `src/Agora.Mod/Llm/FlavorCacheMigration.cs`:

```csharp
/// <summary>
/// Brings a cached politics_flavor document up to the current schema version before it is
/// re-validated. Cache only — a live CLI response still fails closed (non-negotiable #7 and
/// FlavorValidator.cs:136), because the model must learn the constraint rather than have it
/// papered over.
/// </summary>
public static class FlavorCacheMigration
{
    public const int HeadlineMaxLength = 90;
    public const int BodyMaxLength = 420;

    /// <summary>
    /// Returns the migrated JSON, or <paramref name="json"/> unchanged when nothing applies.
    /// Never throws.
    /// </summary>
    public static string UpgradeToCurrent(string json, IFlavorLog log,
                                          out int fromVersion, out int prunedArticles);
}
```

Behaviour, v1 → v2:

1. Parse with `FlavorJsonReader.ParseObject`. Unparseable → return the input unchanged with
   `fromVersion = 0`; the validator will reject it and log, as today.
2. `schemaVersion == 2` → return unchanged, `prunedArticles = 0`.
3. `schemaVersion == 1` → remove every element of `articles[]` whose `headline` exceeds 90
   characters **or** whose `body` exceeds 420. Count them. Leave `partyFlavor`, `factionFlavor`,
   `eventProse` and `generatedAtSimDate` untouched. Set `schemaVersion = 2`. Re-serialise.
4. Anything else (a version this build cannot reach) → return unchanged; the validator's
   `:136` check rejects it and the session starts with no cached prose, which is the honest outcome.

Wire it in at `FlavorCache.cs:85-87`, between the read and the validate:

```csharp
                string json = File.ReadAllText(path, new UTF8Encoding(false));
                int fromVersion, pruned;
                json = FlavorCacheMigration.UpgradeToCurrent(json, _log, out fromVersion, out pruned);
                if (pruned > 0)
                {
                    _log.Warn("cached flavor upgraded from schemaVersion " + fromVersion +
                              "; dropped " + pruned + " article(s) longer than the new limits");
                }
                var date = FlavorDocument.ParseSimDate(PeekDate(json));
                var result = _validator.Validate(json, _catalog, date ?? default(SimDate));
```

### The answer to "what happens to already-cached entries that exceed the new limits"

- **Over-length articles are dropped individually, at load, and permanently** — once the next
  successful generation calls `FlavorCache.Save` (`FlavorCache.cs:106`), the pruned form is what is
  on disk. They were prose; no engine number ever depended on them (non-negotiable #1), so nothing
  desyncs.
- **They are never truncated.** A body cut at character 420 ends mid-sentence and would be published
  to the player as if it had been written that way. One fewer article is better than one bad one,
  and article count is a prompt instruction, not engine state (`FlavorRequest.cs:86-87`).
- **Party, faction and event prose survive intact.** None of their limits moved. This is the whole
  point: party names are the load-bearing content of the cache, and losing them is a visible
  regression to `party-01`.
- **A cache whose every article is over-length still loads**, with an empty `articles[]` and its
  party names intact. The news feed is empty for one session until the next wake.
- **An already-running session is unaffected** — `ClaudeCliProvider._lastGoodDocument` is in memory
  and the tightening bites at the next load.
- **`FlavorSchema.SupportedSchemaVersion` moving to 2 does not by itself break the cache**, because
  the migration restamps before validation. Without the migration it would, via `:136`.

### `StaticPoolProvider` — the fail-closed-on-fail-closed case

`StaticPoolProvider` validates its own canned output through the same validator
(`src/Agora.Mod/Llm/StaticPoolProvider.cs:102`) and hardcodes the old limits:

```
src/Agora.Mod/Llm/StaticPoolProvider.cs:277   Cap(…, 140)   → 90
src/Agora.Mod/Llm/StaticPoolProvider.cs:279   Cap(…, 900)   → 420
src/Agora.Mod/Llm/StaticPoolProvider.cs:291   Cap(…, 140)   → 90
src/Agora.Mod/Llm/StaticPoolProvider.cs:293   Cap(…, 900)   → 420
src/Agora.Mod/Llm/StaticPoolProvider.cs:322   Cap(…, 900)   — localAngle, UNCHANGED
```

`fixplan.md` mentions none of these. Leaving them means the **fallback provider** — the thing that
exists so a missing CLI still produces prose — fails its own schema and returns nothing. Every party
is unnamed on a machine with no `claude` binary. These four numbers must change in the same commit
as §4.2 and §4.3.

Prefer routing them through `FlavorCacheMigration.HeadlineMaxLength` / `BodyMaxLength` rather than
retyping the literals, so there is one place the pair is spelled.

### The prompt

`src/Agora.Mod/Llm/FlavorPromptBuilder.cs`, in `AppendTask` (`:224-252`). One sentence appended to
the article instruction at `:244`, nothing else:

```csharp
            sb.Append("Set refs only to IDs from the lists above. ");
            sb.Append("Headlines are at most 90 characters and bodies at most 420 - a longer one ");
            sb.Append("fails validation and the whole response is discarded.\n");
```

The prose-quality rewrite (lead with what happened, name a party or district, ban "residents say")
is W5 and is deliberately not in this pass. Note for W5's planner: `FlavorPromptBuilder.cs:53`
hard-truncates the prompt at 120k characters with the schema appended last
(`AppendSchema`, `:254-259`), so a large city truncates mid-schema — the backlog item at
`fixplan.md:345-347` should land before the prompt grows.

---

## 6. Tests

**The suite cannot currently see any of this code.** `tests/Agora.Core.Tests/Agora.Core.Tests.csproj`
links exactly four `Agora.Mod` files (`ModifierDelta.cs`, `EffectLedger.cs`, `SimClockMath.cs`,
`StartYearDelivery.cs`) and none of `Persistence/` or `Llm/`. Skill step 5 — *"an untested migration
is a guess"* — therefore requires csproj work before a single test can be written. This is step 0 of
the checklist for exactly that reason.

### 6.0 Harness

Add to `tests/Agora.Core.Tests/Agora.Core.Tests.csproj`:

```xml
    <PackageReference Include="Newtonsoft.Json" Version="13.0.3" />
```

and, in the existing link `ItemGroup` (`:44-49`):

```xml
    <Compile Include="..\..\src\Agora.Mod\Persistence\SidecarSchema.cs"      Link="ModPersistence\SidecarSchema.cs" />
    <Compile Include="..\..\src\Agora.Mod\Persistence\AgoraJson.cs"          Link="ModPersistence\AgoraJson.cs" />
    <Compile Include="..\..\src\Agora.Mod\Persistence\AtomicFile.cs"         Link="ModPersistence\AtomicFile.cs" />
    <Compile Include="..\..\src\Agora.Mod\Persistence\SidecarPaths.cs"       Link="ModPersistence\SidecarPaths.cs" />
    <Compile Include="..\..\src\Agora.Mod\Persistence\LoadReconciliation.cs" Link="ModPersistence\LoadReconciliation.cs" />
    <Compile Include="..\..\src\Agora.Mod\Persistence\SidecarStore.cs"       Link="ModPersistence\SidecarStore.cs" />
    <Compile Include="..\..\src\Agora.Mod\Llm\FlavorJsonReader.cs"           Link="ModLlm\FlavorJsonReader.cs" />
    <Compile Include="..\..\src\Agora.Mod\Llm\FlavorLog.cs"                  Link="ModLlm\FlavorLog.cs" />
    <Compile Include="..\..\src\Agora.Mod\Llm\FlavorSchema.cs"               Link="ModLlm\FlavorSchema.cs" />
    <Compile Include="..\..\src\Agora.Mod\Llm\FlavorCacheMigration.cs"       Link="ModLlm\FlavorCacheMigration.cs" />
```

Verified game-free: every file in `src/Agora.Mod/Persistence/` except `AgoraSidecarSystem.cs` uses
only `System.*`, `Newtonsoft.*` and `Agora.Core.Contracts`. Confirm the same for the four `Llm` files
with a grep for `Game\.|Colossal|Unity` before adding them; if `FlavorLog.cs` carries
`ColossalFlavorLog`, split that class out or use `NullFlavorLog` and drop the link. **Write
`FlavorCacheMigration.cs` against `System.*` + `Newtonsoft.*` only**, so it links cleanly — that is a
design constraint on the new file, not an accident.

Adding a `Newtonsoft.Json` **package** reference to the test project does not violate
`src/CLAUDE.md`'s "Newtonsoft ships with the game — do not add a package": that rule protects
`Agora.Mod`'s deployed output. The test project is never deployed and the linked code uses only
`JObject` / `JsonConvert` basics, stable across 13.x.

### 6.1 `tests/Agora.Core.Tests/SidecarMigrationTests.cs` (new)

| # | Test | Asserts |
|---|---|---|
| 1 | `Migrate_StampsAbsentVersionAndStillRunsEveryStep` | A state fixture with **no** `schemaVersion`: outcome `AssumedVersionOne`, `IsLoadable`, root version `2`, every party has `playerOverrides == "None"`, `settings.pauseOnMajorNews == true`. This is the §3.1 defect; without the fix it fails on the last three clauses. |
| 2 | `Migrate_StateV1_AddsPlayerOverridesToEveryParty` | Fixture with three parties, none carrying the field → all three become `"None"`. A fourth already carrying `"NameLocked"` is left alone. |
| 3 | `Migrate_StateV1_AddsSettingsFieldsWithTheDocumentedDefaults` | `themeLocked == false`, `pauseOnMajorNews == true`, `showAllReports == false`, nested `settings.schemaVersion == 2`. The `true` is the one a careless implementation gets wrong. |
| 4 | `Migrate_StateV1_LocksTheThemeWhenAnElectionHasBeenHeld` | Two fixtures: `electionHistory: []` → `themeLocked == false`; one entry → `themeLocked == true`. |
| 5 | `Migrate_StateV1_ChangesNothingElse` | Deep-compare the v1 fixture against the migrated DOM after removing the six known-changed paths (`schemaVersion`, `settings.schemaVersion`, the three settings booleans, every `parties[*].playerOverrides`). `JToken.DeepEquals` must be true. The "never silently drop a field" check. |
| 6 | `Migrate_IsIdempotent` | `Migrate` the migrated DOM: outcome `Current`, and `AgoraJson.Serialize` of the result is string-equal to the first pass. Determinism / no fingerprint churn on reload. |
| 7 | `Migrate_RefusesAStateFileFromTheFuture` | `schemaVersion: 3` → `TooNew`, `IsLoadable == false`, DOM byte-identical to the input. |
| 8 | `Migrate_SettingsFileV1_UpgradesStandalone` | A bare `settings.json` at v1 through `SidecarDocument.Settings` → v2, three fields present, `themeLocked == false` (it cannot see election history — §3.3). |
| 9 | `SidecarStore_RoundTripsAnOldVersionStateFile` | **The round-trip the skill demands.** Write a hand-authored v1 `state_1994_03.json` into a temp directory keyed by a fixed GUID; `new SidecarStore(root, log).Load(guid, new SimDate(1994,3,1))`; assert `HasState`, `Warnings.Count == 0`, `Settings.PauseOnMajorNews == true`, `Settings.ShowAllReports == false`, `State.Parties[0].PlayerOverrides == PartyOverrides.None`, and that `State.Parties[0].Name` is unchanged from the fixture. Then `SaveState`, `Load` again, and assert `AgoraJson.Fingerprint` is equal across that second round trip. |
| 10 | `Party_PlayerOverrides_SerializesAsMemberNames` | `PartyOverrides.NameLocked \| PartyOverrides.ColorLocked` round-trips through `AgoraJson` as the string `"NameLocked, ColorLocked"`, and `None` as `"None"`. Guards the wire form the JSON schema declares. |

### 6.2 `tests/Agora.Core.Tests/FlavorCacheMigrationTests.cs` (new)

| # | Test | Asserts |
|---|---|---|
| 11 | `Upgrade_DropsOnlyTheOverLengthArticles` | Three articles — one with a 200-char headline, one with a 700-char body, one inside both limits. One survives; `prunedArticles == 2`. |
| 12 | `Upgrade_KeepsPartyFlavorWhenEveryArticleIsDropped` | `partyFlavor` is byte-identical after every article is pruned. **This is the W2-regression guard** and the most important test in the pass. |
| 13 | `Upgrade_StampsSchemaVersionTwo` | `schemaVersion == 2` on the output. |
| 14 | `Upgrade_NeverTruncates` | A surviving article's `body` is character-identical to the input. |
| 15 | `Upgrade_LeavesACurrentDocumentUntouched` | v2 in → string-identical out, `prunedArticles == 0`. |
| 16 | `Upgrade_LeavesEventProseAlone` | An 800-char `localAngle` survives — the 900 limit did not move. |

### 6.3 `tests/Agora.Core.Tests/FlavorSchemaDriftTests.cs` (new)

| # | Test | Asserts |
|---|---|---|
| 17 | `EmbeddedSchema_MatchesTheFileOnDisk` | `FlavorSchema.MatchesFile(<repoRoot>/data/schemas/politics_flavor.schema.json)` is true. `FlavorSchema.cs:170` documents this gate at `:20-24` and **nothing calls it** — a grep for `MatchesFile` across the repo returns only the definition. This pass is when it starts earning its keep. Use the `RepoRoot()` helper pattern already in `ShippedTimelineCatalogTests.cs:465`. |
| 18 | `EmbeddedSchema_DeclaresTheTightenedArticleLimits` | Reads the embedded schema and asserts `articles.items.properties.headline.maxLength == 90` and `body.maxLength == 420` literally. A future edit to one side alone fails here rather than in-game. |

### 6.4 `tests/Agora.Core.Tests/PartyLifecycleTests.cs` (existing file, one addition)

| # | Test | Asserts |
|---|---|---|
| 19 | `Clone_PreservesPlayerOverrides` | `PartyRegistry.Clone` of a party with `NameLocked \| ColorLocked` returns a party with the same value. Guards §2.2 — the field-by-field copy that will otherwise silently drop it. |

---

## 7. Both-sides sync — the four sides, listed

The `/schema-change` table names three sides per contract; the binding contract has three of its
own. Every one below must move in this pass or the contract has drifted.

### `political_state.json` / sidecar

| Side | File | Change |
|---|---|---|
| C# contract | `src/Agora.Core/Contracts/Parties.cs` | `PartyOverrides` enum + `Party.PlayerOverrides` (§2.1) |
| C# contract | `src/Agora.Core/Contracts/PoliticalState.cs:84,114,130` | three settings fields, two version bumps (§2.3) |
| C# clone | `src/Agora.Core/Engine/Parties/PartyRegistry.cs:181` | copy the new field (§2.2) |
| JSON schema | `data/schemas/political_state.schema.json:105,374,384,390` | four edits (§4.1) |
| Migration | `src/Agora.Mod/Persistence/SidecarSchema.cs:107,108,110,131,132,177-185` | versions, two steps, the fall-through fix (§3) |
| LLM prompt | — | **no change.** The prompt describes the *snapshot*, not the state file; neither `playerOverrides` nor the UI settings are prompt inputs. Confirmed against `FlavorPromptBuilder.cs:224-259`. |

### `politics_flavor.json`

| Side | File | Change |
|---|---|---|
| C# contract | `src/Agora.Mod/Llm/FlavorSchema.cs:29,47,92,93` | version + two limits (§4.3) |
| JSON schema | `data/schemas/politics_flavor.schema.json:13,58,59` | version + two limits (§4.2) |
| LLM prompt | `src/Agora.Mod/Llm/FlavorPromptBuilder.cs:244` | one sentence stating the limits (§5) |
| Fallback producer | `src/Agora.Mod/Llm/StaticPoolProvider.cs:277,279,291,293` | four caps (§5) |
| Cache reader | `src/Agora.Mod/Llm/FlavorCache.cs:85-87` + new `FlavorCacheMigration.cs` | prune-on-load (§5) |

### ui bindings

| Side | File | Change |
|---|---|---|
| C# publisher | `src/Agora.Mod/UiBindings/AgoraUiPayloads.cs:165-193` | `PartyBriefPayload` gains three bools + three `UiJson.Flag` writes |
| C# projection | `src/Agora.Mod/UiBindings/AgoraUiProjection.cs:84` | populate them from `party.PlayerOverrides` |
| TS types | `ui/types/bindings.d.ts:3765-3778` | `PartyBrief` gains `nameLocked`, `descriptionLocked`, `colorLocked` |
| TS types | `ui/types/bindings.d.ts` (near `:3754`) | new `interface SettingsPayload` (shape below) |
| Doc | `docs/contracts/ui_bindings.md:3,9-11,126,247,291-295,380-387` | six edits (below) |

Payload additions, exactly:

```csharp
// AgoraUiPayloads.cs, on PartyBriefPayload after DissolvedDate (:176)
        public bool NameLocked;
        public bool DescriptionLocked;
        public bool ColorLocked;

// …and in Write(), after UiJson.Date(writer, "dissolvedDate", DissolvedDate); (:190)
            UiJson.Flag(writer, "nameLocked", NameLocked);
            UiJson.Flag(writer, "descriptionLocked", DescriptionLocked);
            UiJson.Flag(writer, "colorLocked", ColorLocked);
```

```ts
// ui/types/bindings.d.ts, inside declare namespace Agora, on interface PartyBrief
    /** Player has renamed this party. `name`/`shortName` are player-owned, not flavor-owned. */
    nameLocked: boolean;
    /** Player has rewritten `description`/`slogan`. */
    descriptionLocked: boolean;
    /** Player has recoloured this party; `colorHex` is player-owned. */
    colorLocked: boolean;
```

```ts
/**
 * `agora.state.settings` — RESERVED, registered but not yet published (W3). Do not consume:
 * per contract rule 3, binding an unpublished name yields the fallback at best.
 * Per-save only; never global config (non-negotiable #10).
 */
interface SettingsPayload {
  schemaVersion: number;
  startYear: number;
  theme: RegionThemeName;
  system: ElectoralSystemName;
  /** True once the theme is history — set at the first election. */
  themeLocked: boolean;
  pauseOnMajorNews: boolean;
  showAllReports: boolean;
  effectsEnabled: boolean;
}
```

`docs/contracts/ui_bindings.md` edits:

1. `:3` header `**schemaVersion: 2**` → `**schemaVersion: 3**`.
2. `:9-11` — the "Frozen for M4" block says *"do not add a field"*. Amend it to record that plan 0001
   unfroze `PartyBrief` under `/schema-change` on 2026-08-08. Leaving it unamended makes the change
   look like a violation to the next reviewer.
3. `:126` — annotate the `agora.parties.roster` row: gained `nameLocked` / `descriptionLocked` /
   `colorLocked` in W4's schema pass; publisher fills them, no panel consumes them yet.
4. `:247-248` §5 summary — append the three names to the `PartyBrief` line.
5. `:291-295` "Which fields are flavor" — add: *a `PartyBrief` field whose lock is set is
   **player-owned**, not flavor-owned. The UI must not offer to regenerate it without first offering
   "reset to generated", and must never present it as LLM output.*
6. `:387` §8 Reserved — keep `agora.state.settings` **in** §8 (rule 3 forbids consuming an
   unpublished binding, and W3 is what publishes it), but replace the one-line entry with the
   `SettingsPayload` shape above, so W3's coder implements against a fixed contract rather than
   inventing one. Note the companion writer binding W3 will need — a `CallBinding` so the panel can
   surface a rejection, per `fixplan.md:213` — and reserve the name `agora.state.setSetting`.

**No panel changes in this pass.** The three booleans render nothing until W4. A coder who finds
themselves editing `ui/src/` has left the plan.

`ui/types/bindings.d.ts` is regenerated by `npm run update` (`ui_bindings.md:391-397`). Both
additions go inside the existing `declare namespace Agora` block so they move together when that
block is relocated to `ui/types/agora.d.ts`.

---

## 8. Ordered checklist

Riskiest first, and the harness first of all, because steps 1–4 are unverifiable without it and the
harness itself may not compile. **A/B/C are separately committable**; B and C touch files disjoint
from A once step 0 has landed.

### Chunk A — sidecar

- [x] **0.** Add the `Newtonsoft.Json` package reference and the six `Persistence` link entries to
      `tests/Agora.Core.Tests/Agora.Core.Tests.csproj` (§6.0). Grep each linked file for
      `Game\.|Colossal|Unity` first. Confirm `dotnet test tests\Agora.Core.Tests\Agora.Core.Tests.csproj`
      still passes green with no new tests. *If this step fails, stop and report — everything below
      depends on it.*
- [x] **1.** Fix the no-version fall-through in `SidecarSchema.Migrate` (`:177-185`) and rewrite the
      stale class-doc paragraph at `:91-95` (§3.1). Add test 1 — it will fail until step 3.
- [x] **2.** `PartyOverrides` enum + `Party.PlayerOverrides` (§2.1), `using System;` in `Parties.cs`,
      and the `PartyRegistry.Clone` line at `:181` (§2.2). Add test 19.
- [x] **3.** Three `AgoraSettings` fields; version initialisers at `PoliticalState.cs:84` and `:130`
      → 2 (§2.3).
- [x] **4.** Version constants at `SidecarSchema.cs:107,108,110` → 2, plus the pointer comment on
      `CurrentFlavorCacheVersion` (§9 finding d). Add `UpgradeSettingsObjectToV2`,
      `MigrateStateV1ToV2` and the two step-table entries (§3.3).
- [x] **5.** `data/schemas/political_state.schema.json` — the four edits (§4.1).
- [x] **6.** Write `SidecarMigrationTests.cs`, tests 1–10 (§6.1). Gate: all green.

### Chunk B — flavor

- [x] **7.** In one commit: `data/schemas/politics_flavor.schema.json:13,58,59`,
      `FlavorSchema.cs:29,47,92,93`, `StaticPoolProvider.cs:277,279,291,293`, and the prompt sentence
      at `FlavorPromptBuilder.cs:244` (§4.2, §4.3, §5). Splitting these breaks the fallback provider
      in the intermediate commit.
- [x] **8.** New `src/Agora.Mod/Llm/FlavorCacheMigration.cs`, `System.*` + `Newtonsoft.*` only (§5).
- [x] **9.** Wire it into `FlavorCache.cs:85-87` with the Warn line (§5).
- [x] **10.** Add the four `Llm` link entries to the test csproj; write
      `FlavorCacheMigrationTests.cs` (tests 11–16) and `FlavorSchemaDriftTests.cs` (17–18). Gate.

### Chunk C — UI contract

- [x] **11.** `AgoraUiPayloads.cs` `PartyBriefPayload` fields + writes; `AgoraUiProjection.cs:84`
      populates them from `party.PlayerOverrides` (§7).
- [x] **12.** `ui/types/bindings.d.ts` — `PartyBrief` fields and `SettingsPayload` (§7).
- [x] **13.** `docs/contracts/ui_bindings.md` — the six edits (§7).

### Close

- [x] **14.** **Gated without the solution build** — `dotnet build Agora.sln` triggers `npm run build`
      and deploys into the player's live Mods folder. Gate actually run:
      `dotnet build src/Agora.Mod/Agora.Mod.csproj -p:UseCsiiToolchain=false`, the test project, and
      `tsc --noEmit`. ~~`dotnet build Agora.sln`, `dotnet test tests\Agora.Core.Tests\Agora.Core.Tests.csproj`,
      `cd ui && npm run build`. All three green.
- [ ] **15.** Manual: load a save created before this change. Confirm in `Agora.log` a line of the
      form `Upgraded schemaVersion 1 -> 2: added party playerOverrides and the three per-save UI
      settings`, that party names are unchanged in the seat chart, and that the news feed still has
      its articles. Then save, quit to menu, reload: the second load must log `Current`, not another
      upgrade — that is idempotency proven against a real file rather than a fixture.
- [x] **16.** Update `docs/status.md` with the new sidecar version and this plan's completion.

---

## 9. Where the code does not match what `fixplan.md` assumes

Every line reference below was opened and read, not inferred.

**a. `AgoraRuntime.cs` is not where `fixplan.md` says it is.** The file is
`src/Agora.Mod/Core/AgoraRuntime.cs`. `fixplan.md` cites it bare — `AgoraRuntime.cs:71-83`,
`:259`, `:475`, `:532`, `:538`, `:764-783` — with no `Core/` segment, in W0, W2, W4 and the backlog.
Not load-bearing for this pass, but it will cost every workstream a minute.

**b. `ApplyProseNames` is at `:769-787`, not `:764-783`** (`fixplan.md:202`). Off by five.

**c. `ApplyProseNames` never writes `ColorHex`.** `fixplan.md:211` calls it *"the single enforcement
point"* for all three locks, but the method only touches `Name`, `ShortName`, `Description` and
`Slogan` (`src/Agora.Mod/Core/AgoraRuntime.cs:780-783`). Colour has no flavor path at all. The only
writers of `ColorHex` are `PartyRegistry.cs:276` (initial generation), `PartyLifecycle.cs:525`
(split) and `:587` (revival) — all in `Agora.Core`. **`ColorLocked` therefore cannot be enforced at
W4's stated enforcement point**, and a party split will recolour a player-recoloured party unless
`PartyLifecycle` honours the flag. The contract field belongs in this pass regardless; W4's plan
needs correcting before it is written. **For Master.**

**d. `SidecarSchema.CurrentFlavorCacheVersion` (`:110`) and `SidecarDocument.FlavorCache` (`:14`) are
dead code.** `FileFlavorCache` never calls `SidecarSchema.Migrate` — it validates through
`FlavorValidator` against `FlavorSchema.SupportedSchemaVersion` (`FlavorCache.cs:87`,
`FlavorValidator.cs:136`). Two constants version one file, and only one of them is consulted. This
plan bumps both to 2 and adds a pointer comment rather than deleting either mid-pass; **recommend a
follow-up to delete `CurrentFlavorCacheVersion` and the `FlavorCache` enum member outright.**

**e. `FlavorSchema.MatchesFile` (`FlavorSchema.cs:170`) is called by nothing.** A repo-wide grep for
`MatchesFile` returns the definition and its own doc comment. The anti-drift gate the class describes
at `:20-24` — *"exists so a test or a gate can assert the two are still equivalent"* — does not
exist. Test 17 creates it.

**f. `SidecarSchema.Migrate` silently skips the step chain for an unversioned document**
(`:177-185`). Latent today because every step table is empty; a data-loss bug the moment this pass
adds one. Fixed in step 1. Nothing in `fixplan.md` anticipates it.

**g. The `settings` block nested inside `state_*.json` is never migrated.** `Migrate` reads and
stamps only the root `schemaVersion` (`:175`, `:226`); `SidecarStore.cs:385` repairs the nested
version only on *write*. So `SettingsSteps` alone would never reach the settings of a save that has
a state file. Handled in §3.2/§3.3 by having the State step call the shared helper.

**h. A `maxLength` violation is fatal to the whole flavor document.** `FlavorValidator.cs:129-132`
returns `Failed` on any schema error, in deliberate contrast to the per-entry catalog drop at
`:164-251`. `fixplan.md:255-256` — *"Tighten the schema: headline ≤ 90, body ≤ 420"* — does not
mention that this discards every existing `flavor_cache.json` in full, party names included, which
re-creates the W2 `party-01` defect on the first reload after the update. §5 is the entire response
to this.

**i. `StaticPoolProvider` validates its own canned output** against the same schema
(`StaticPoolProvider.cs:102`) and hardcodes 140/900 at `:277`, `:279`, `:291`, `:293`.
`fixplan.md` mentions none of them. Missing these four numbers disables the fallback provider —
the thing that exists so a machine with no `claude` binary still gets prose.

**j. No test project can see `Agora.Mod/Persistence` or `Agora.Mod/Llm`.**
`tests/Agora.Core.Tests/Agora.Core.Tests.csproj:44-49` links four Mod files and none of them are
persistence or LLM. `/schema-change` step 5 is therefore not satisfiable without csproj work, and
`fixplan.md`'s verification gate (*"Each workstream ships with a `dotnet test …` pass"*) currently
cannot cover any of the migration surface. Step 0 fixes it, and the fix benefits W0's headless
state-clearing test too.

**k. Minor, no action.** `politicsmodplan.md:154` ratifies `body(≤120 words)`; 420 characters is
~65–70 words, strictly inside that bound, so no ratified decision is being re-litigated and §6 needs
no edit. Recorded so a reviewer does not have to re-derive it.
