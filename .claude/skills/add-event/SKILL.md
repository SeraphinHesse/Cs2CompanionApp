---
name: add-event
description: Author an event for Agora — a dated timeline event (data/timeline_*.json) or a triggered civic event (data/events_*.json). Entry shape, effect-id checking, the reachability rules prose must obey, and the sensor ceilings thresholds are sized against.
---

# /add-event

There are **two kinds of event and they are not the same job.**

| | **Timeline event** | **Civic event** |
|---|---|---|
| Files | `data/timeline_{global,eu,na}.json` | `data/events_{global,eu,na}.json` |
| Fires on | a **date** — real history, 1990→ | the **state of the city** — a declarative trigger |
| Player can | read about it | *tackle* it: Ignore / Goal / PowerOverride / Manual |
| Prose | one `headlineBrief`, a prompt input | **seven authored fields, published verbatim** |
| Loader | `TimelineCatalogLoader` | `CivicEventCatalogLoader` |
| Contract | `politicsmodplan.md` §6 | `politicsmodplan.md` §15, schema `data/schemas/civic_events.schema.json` |

Both are **content**: they carry no logic, and the scheduler, the story engine and the effect palette
do the work. Part A is the timeline half, Part B the civic half. **Part C applies to both** — it is
the rule that stops the mod lying to the player about their own city, and it is the half of this
skill most likely to be skipped, so read it even if the entry you are writing looks obvious.

---

## Part A — timeline events (dated history)

### Entry shape

```json
{
  "id": "gfc-2008-collapse",
  "dateISO": "2008-09-15",
  "region": "global",
  "title": "Global financial crisis",
  "severity": 4,
  "durationMonths": 24,
  "effects": [
    { "effectId": "city-loan-interest", "scope": "city", "magnitude": 0.30, "durationMonths": 18 }
  ],
  "headlineBrief": "Credit markets seize; lending costs spike and construction stalls.",
  "issuePressure": { "costOfLiving": 0.30, "growth": 0.20 },
  "tags": ["economy", "finance", "recession"]
}
```

A timeline effect carries its **own signed magnitude**, unlike a civic event's bare id list — so a
slump is written `-0.10` here and expressed by *which list* an id sits in there. The cap is on the
magnitude's size, not its sign.

### Rules

1. **Every `effectId` must exist in the palette registry** and every magnitude must sit inside that
   effect's cap. The shipped-catalog test fails the build otherwise — that check is the point, and it
   is cheaper than discovering the drop in a ledger six months of play later.
2. **`headlineBrief` is a prompt input, not published prose.** Keep it factual and terse. The LLM
   writes the article; this tells it what happened. Writing it as finished copy produces articles that
   read like paraphrases of themselves.
3. **Severity 1–5, and be conservative.** Severity drives effect scaling, so a catalog where
   everything is a 4 flattens into no dynamic range at all. Severity 5 should feel rare.
4. **Region routing:** `global` fires everywhere; `eu` and `na` fire only under the matching theme. An
   event with a genuinely different local meaning gets two entries, not one hedged entry.
5. **Real dates.** Whether the scheduler jitters them is an open decision (§14) and not the author's
   call — put the true date in.

### Local angle

The catalog states what happened in the world. How it lands in *this* city is the engine's job (which
districts, which blocs) and the LLM's (`eventProse[].localAngle`). Do not pre-write the local angle
into `headlineBrief` — it is different in every save.

### Adaptation into a story

A timeline event can also become a **mandatory civic event**, through `data/timeline_adaptation.json`
and `TimelineEventAdapter`. Three routes per entry: `none` (it stays a timeline event and nothing
else — this is how the boring quarter is retired without deleting it), the generic wrapper (name ←
`Title`, description ← `HeadlineBrief`, a severity-derived happiness goal), or a hand-authored civic
event, which is Part B and must satisfy every rule there.

---

## Part B — civic events (triggered, tackleable)

### Entry shape

```json
{
  "id": "glob-service-coverage-slump",
  "severity": 3,
  "region": "global",
  "trigger":  { "kind": "metric", "metricId": "serviceCoverage", "comparison": "lt",
                "threshold": 0.30, "scope": "anyDistrict" },
  "check":    { "spec": { "kind": "metric", "metricId": "serviceCoverage", "comparison": "gte",
                          "threshold": 0.30, "scope": "anyDistrict" } },
  "activeEffects":   ["district-wellbeing"],
  "successEffects":  ["city-attractiveness"],
  "failureEffects":  ["district-wellbeing"],
  "activePressure":  { "services": 0.30 },
  "successPressure": { "services": 0.08 },
  "failurePressure": { "services": 0.45 },
  "tags": ["services"],
  "notes": "Expected trigger frequency, and why each threshold is where it is.",
  "name": "…", "description": "…",
  "ignoreText": "…", "goalText": "…", "powerOverrideText": "…",
  "successText": "…", "failText": "…"
}
```

All twelve of `id`, `severity`, `region`, `trigger`, `check` and the seven prose fields are required.
The schema is `additionalProperties: false`: a typo'd key is a rejection, not a silent ignore.

### The rules the loader enforces, and why each exists

Every one of these was a real defect in wave 3 that a green test suite waved through, which is why it
is machine-checked rather than only written down. `CatalogIssueCode` numbers in brackets; the shipped
catalog gate holds `data/events_*.json` to **zero warnings**, so a warning fails the build too.

1. **A story lives `cycleMonths - 1` months — ONE, not two** [120]. `StoryAssembler.NewStory` sets
   `months = stories.CycleMonths - 1`: draft on M, resolve on M+1, next batch at M+2. `cycleMonths`
   (2) is the **cadence**; the story's life is the cadence *minus one*, and the two differ by one. Any
   `windowMonths` above 1 scores the player on months that predate their decision — the further back
   it reads, the smaller their share of the verdict. **Size every threshold against one month of
   influence.** This has been re-explained in five consecutive waves; it is written here so it stops
   being lore and starts being something an author reads before authoring.
2. **Two sensors cannot reach 1.0, and nothing else records it** [121]. `serviceCoverage` is a mean
   over **nine** channels with four hard-zeroed, so it tops out at **5/9 ≈ 0.5556**. `pollution` tops
   out at **0.75**. A threshold of 0.45 on service coverage is therefore **81% of everything
   attainable**, not "a bit over half"; a `gte` above the ceiling can never be met, and a `lt` above
   it is met by every city always. Both say nothing about the city. See
   `CivicEventCatalogLoader.AttainableMaximum`.
3. **A district-scoped check must be bound to its trigger** [117], and must not be **tighter** than
   it [119]. An `allDistricts` check returns `NotMet` the instant one measured district fails, so a
   trigger at `>= 0.40` with a check at `< 0.35` fails the player over a district at 0.37 that
   contributed nothing to the trigger and is never mentioned in the prose. They fix the block the
   story is about and lose anyway, with nothing surfacing why.
4. **A relative (`baseline`) check at district scope is `Unmeasurable` forever** [116], scoring in
   neither half of the 2-of-3.
5. **Pressures are salience, not credit** [118]. All three of `activePressure`, `successPressure` and
   `failurePressure` point the **same way** on the axis and differ only in magnitude — success
   quieter, failure louder. A mirror-negated success pressure does not release the issue: it moves
   voters to the **opposite pole**, rewarding the party that opposed doing anything. Government credit
   and blame are derived by the engine from the slot outcome and its tier, never authored here.
6. **No effect id in both `activeEffects` and `successEffects`.** The palette carries no sign and all
   three story scales are positive, so that shape reapplies the same modifier at twice the magnitude
   and calls it a reward.
7. **`policy` triggers do not exist and are rejected by name.** No sensor writes
   `CitySnapshot.ActivePolicyIds`, so a policy spec can never fire and an `absent` policy spec fires
   on every city forever. `unlock` is gated on wave 1's unwalked gate 11 — its ids are raw prefab-name
   strings nobody has read. `manual` never fires from the city and is never pooled.
8. **Every threshold must be hittable in a normal game**, and the expected trigger frequency goes in
   `notes`. An event nobody can trigger is not content.
9. **Severity 1–5, conservative.** Mandatory (5) should feel rare: it is the tier that can hold the
   player's clock and costs 500 power to override.

---

## Part C — the prose rule, and the traps behind it

> **An event's prose may only claim what its effect ids can actually do.**

The palette is a **closed registry** — `politicsmodplan.md` §7, which carries this rule too. A civic event's
seven prose fields are published to the player *verbatim*, beside a simulation that is running the
effect ids — so a headline promising something the palette cannot deliver is contradicted by the
player's own city within the month. That is not a flavour problem. It is the mod telling the player
something false about the simulation it is running, and once one event does it no number the
dashboard shows is trustworthy either.

Check every noun in the prose against the effect ids in the same entry. The traps, specifically:

- **No modifier kills citizens.** "20–100 cims die" is **not doable and not wanted** — killing
  citizens is entity mutation and sits outside §7. `city-disease-probability`,
  `city-pollution-health-affect` and `city-hospital-efficiency` change **illness and treatment, not
  mortality**; nothing kills. (`RecoveryFailChange` is the only member that plausibly moves death
  outcomes; it is **not** in the palette, and adding it is an `/add-effect` decision this plan has not
  taken.) Re-specify as a crime-plus-disease spike carrying heavy `IssuePressure`: the political shock
  without the forbidden mechanism.
- **There is no tourism modifier.** `Game.City.Tourism` is a **read** component — which is why you may
  *trigger* on the `tourists` metric and may never *affect* it. Use `city-attractiveness`,
  `city-entertainment` and `city-park-entertainment`, and **say "attractiveness" in the prose, not
  "tourism"**, or the effect and the headline disagree in the one place the player can check.
- **`city-prison-time` is sentence length, not cost.** It maps to `PrisonTime`. The cost proxy is
  `city-service-building-upkeep` → `CityServiceBuildingBaseUpkeepCost`, which is **city-wide across
  every service building**. **Never write prose calling either one "the prison budget"** — the player
  who goes looking will find their libraries got more expensive too.
- **Agricultural output is unreachable.** `city-industrial-efficiency` is **all-industry**; the
  fish-specific and ore/oil members are the wrong resources or not in the palette. Re-specify a
  farmers-versus-tech-subsidies event around **taxes and trade cost** (`city-import-cost`,
  `city-export-cost`, `city-service-import-cost`, `district-low-commercial-tax`), which is where the
  player's lever actually is.
- **`ServiceFee` and `TaxRates` are forbidden and are not in the palette.** They are the player's own
  sliders. Writing them is "targeting the player's authority" in the plainest sense of §7's FORBIDDEN
  list, and the player would watch their own settings move without having touched them.
- **RCI demand, rent, land value and birth rate are unreachable as *effects*** — several are readable
  as metrics (`rent`, `landValue`, `births`), which makes them fine in a trigger and impossible in an
  effect list, and that asymmetry is exactly where prose goes wrong. Events reaching for them land
  instead on `district-wellbeing`, `city-attractiveness`, `city-tax-happiness`,
  `district-crime-accumulation` or `city-college-graduation` / `city-university-graduation` /
  `city-university-interest`. **Do not author against a modifier that does not exist**: the catalog
  test fails and the entry is wasted work.
- **There is no commute modifier.** Commute misery is `district-street-speed-limit`,
  `district-street-traffic-safety`, `city-highway-traffic-safety` and `district-wellbeing`.
- **Party polarisation on an axis is `IssuePressure` only** — a voter effect, not a city effect, and
  it needs no palette entry at all.

The authority for what exists is `data/engine_tuning.json` → `effects.perEffect` (46 entries, each
with its scope, modifier, `magnitudeCap` and `durationCapMonths`). Read it, do not remember it.

---

## Coverage

Timeline: M5 targets 80–120 events, 1990→2025 — track by decade and by theme in `docs/status.md`;
the common failure is a dense 2008–2016 and a thin 1990s. Civic: 58 ship today (27 global, 15 EU,
16 NA). The failure mode there is a **thin severity spread** and a pool crowded onto two metrics, so
the same three stories recur; `stories.reuseCooldownMonths` hides that for about six months and then
stops hiding it.
