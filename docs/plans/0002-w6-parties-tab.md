# Plan 0002 — W6: the Parties tab

**Written:** 2026-08-08
**Mandated by:** `fixplan.md` §W6 (`fixplan.md:295-334`) — *"Decision (owner): new tab showing name,
description, per-issue priorities, current support/polling, and seats."*
**Blocks:** W4 (inline rename / recolour), which puts its edit controls in this tab's detail-pane
header (`fixplan.md:315`). W6 leaves the seam; it builds none of the controls.
**Blocked on:** the core tab is blocked on nothing. **Chunk H (coalition relations) is blocked on
plan 0001 if the owner takes the persisted design — see §16.**

**Structure.** **Part I (§0–§10) is the core tab and ships on its own.** **Part II (§11–§17)**
covers the five additional-content items the owner accepted on 2026-08-08, each as a self-contained
incremental chunk (D–H) landing after the core. Nothing in Part II changes anything in Part I: the
core can ship first, or the whole thing can ship together.

**Owner decision, 2026-08-08 — against `fixplan.md:320-334`:**

| # | Addition | Verdict | Chunk | Cost |
|---|---|---|---|---|
| 1 | Manifesto vs. current platform | **In** | D (§12) | payload-only; fields already in the core payload |
| 2 | Bloc support breakdown | **Out** | — | — |
| 3 | Poll trend sparkline | **In** | E (§13) | one new map binding, payload-only |
| 4 | Coalition relations | **In** | H (§16) | **not payload-only** — needs `Agora.Core` work or a schema change |
| 5 | Party history strip | **In** | F (§14) | payload-only |
| 6 | Mandate scorecard | **In** | G (§15) | **no new binding at all**; overlaps existing UI — see §15 |

**Explicitly out of scope:** addition 2; W4's edit controls; anything in plan
`0001-batched-schema-change.md` beyond the one item §16 asks it to carry.

No game API is touched anywhere in this plan — every binding uses `Colossal.UI.Binding` types
already in use by `AgoraDistrictsUISystem`, so no scout report is required and neither
`docs/scout/0001-api-index.md` nor `0002-modding-toolchain.md` gates it. No `politicsmodplan.md`
§14 open decision is involved; all six were checked at `politicsmodplan.md:326-331`.

---

# Part I — the core tab

## 0. Assumptions, stated

1. **The core tab does not depend on plan 0001 and 0001 does not depend on W6.**
   `0001-batched-schema-change.md:30` puts *"`PartyDetailPayload` and the Parties tab (W6)"*
   explicitly out of its scope, and the core tab consumes none of the three `PartyBrief` lock
   booleans 0001 adds. §8 lists the files where the two passes touch the same region and how to
   merge them. **Chunk H is the exception** and §16 states the dependency precisely.
2. **A party id never leaves the roster.** `PartyRegistry` marks a party `Dissolved` rather than
   removing it (`PoliticalState.cs:144-145` — *"Includes dissolved brands so they can revive"*), so
   the Parties tab needs no equivalent of the Districts panel's "District gone" branch
   (`DistrictsPanel.tsx:126-142`). It still needs the empty-payload branch, for the frames before
   the engine's first publish.
3. **The detail payload does not repeat what `agora.parties.roster` already carries.**
   `ui_bindings.md:127-129` — *"Every other payload in this contract identifies a party by `partyId`
   only"*. `coreGrievance`, `isIncumbent` and `isInGovernment` are read from the `PartyBrief` the
   panel already holds. `name`, `shortName` and `colorHex` **are** duplicated onto the detail,
   deliberately — they are the pane's own header, and W4 will edit them there; the panel must still
   resolve *other* parties' labels through the roster.
4. **No headless test is possible for the UI-binding surface.** See §7. Chunk H is the one part of
   this plan that *is* headlessly testable, because its engine half lives in `Agora.Core`.

---

## 1. Where the code does not match what `fixplan.md` assumes

Every reference below was opened and read.

**a. `Party` has no seat share.** `fixplan.md:311` lists *"seats, seat share"* and `fixplan.md:302-306`
claims *"`Party` carries everything needed"*. It does not: `Party.SeatsHeld` exists
(`src/Agora.Core/Contracts/Parties.cs:113`) but there is no `SeatShare`. See §3.3 for the derivation.

**b. `Party` has no poll share and no poll delta.** `fixplan.md:311-313`. Both must come from
`PoliticalState.RecentPolls` (`PoliticalState.cs:165`), read newest-published-first exactly as
`AgoraUiProjection.BuildLatestPoll` already does (`AgoraUiProjection.cs:327-353`), against
`Party.LastVoteShare` (`Parties.cs:110`).

**c. "Above/below threshold" is two different facts.** `fixplan.md:313`.
`SeatAllocation.PassedThreshold` — *"False when the party fell below `electionsPr.thresholdShare`"*
(`src/Agora.Core/Contracts/Elections.cs:134-135`) — is an electoral fact about the last count.
`Party.ConsecutiveElectionsBelowThreshold` — *"Reaching `parties.deathConsecutiveElections`
dissolves the party"* (`Parties.cs:122-125`) — is a survival counter. The payload carries both.

**d. "Government role" cannot be read off `Party`.** `fixplan.md:313`. `Party.IsIncumbent`
(`Parties.cs:115-116`) and `Party.IsInGovernment` (`Parties.cs:118-119`) between them cannot
distinguish **opposition** from **not in the chamber**. Only `Coalition.OppositionPartyIds`
(`src/Agora.Core/Contracts/Government.cs:68-69`) can. Derived in the projection (§3.4).

**e. `TAB_ORDER` is one of four edits.** `fixplan.md:314` names only `ui/src/shell/state.ts:21`.
Also required: the `AgoraTab` union at `state.ts:18`, `TAB_LABEL` at `state.ts:23-27`, and
`renderTab` in `ui/src/shell/Dashboard.tsx:35-45`. **The last one fails silently:** the `default:`
branch at `Dashboard.tsx:41-43` falls through to `<SeatsPanel />`, so a tab added to `TAB_ORDER` but
not to `renderTab` renders the Council panel with no error anywhere.

**f. `PartyBriefPayload` really is at `AgoraUiPayloads.cs:165`.** `fixplan.md:306` is correct.

**g. The contract's freeze block forbids this change until amended.**
`docs/contracts/ui_bindings.md:9-11` — *"**Frozen for M4.** … Do not rename, do not add a field, do
not reorder a sort key."* §6 amends it.

**h. `docs/status.md:22` hard-codes the current tab list** and goes stale when this lands.

**i. `fixplan.md:322` is wrong about the additions being free** — *"Everything here is already
computed by the engine; the cost is payload and layout, not simulation."* True for additions 1, 3,
5 and 6. **False for addition 4**, and §16 is the whole response to that.

---

## 2. The stale-frame problem, and the solution

### The constraint

The map-binding round trip is **synchronous and throws on an unresolved key** (owner-verified this
session against the game bundle; the shipped declaration agrees — `ui/types/api.d.ts:44-46` carries
the comment *"throw an error if the binding is not registered on the C# side"* and types the
defined-key overload as returning `V`, not `V | undefined`). Two consequences:

- **Do not design a loading state.** `useMapValue` cannot return `undefined` for a defined key.
  A spinner branch here would be dead code that renders only during the exact bug this section
  eliminates. (`ArticleReader.tsx:69` is on the backlog for the opposite reason — there the fetch
  genuinely can be pending.)
- **A changing key yields one stale frame.** `useMapValue`'s `useState` initialiser does not re-run
  when the key changes, so on the render where the key flips from A to B the hook returns A's value.
  A list rail selecting into a detail pane is precisely this pattern.

### The solution — three parts, all required

**1. Remount on selection: `key={selectedId}` on the detail component.**

```tsx
<PartyDetail key={selectedId} partyId={selectedId} brief={selected} ... />
```

A changed React `key` unmounts the old element and mounts a fresh one, so every `useState`
initialiser — including `useMapValue`'s — runs against the new key. There is no render in which a
mounted component sees a key it was not initialised with. This is the existing, commented precedent
at `ui/src/panels/Districts/DistrictsPanel.tsx:117-125`:

> `// Keyed by district id so switching districts remounts the pane and its two map`
> `// binding subscriptions, rather than re-keying live subscriptions in place.`

Two things that defeat it: do not hoist the `useMapValue` call into `PartiesPanel` (the parent does
not remount, so the key would change under a live hook), and do not memoise the `<PartyDetail>`
element across a selection change.

**This applies to every map binding the pane holds** — including the ones chunks E and H add. All of
them live inside `PartyDetail`, which is the component the key remounts. That is the reason the plan
keeps them there instead of lifting any of them to the panel.

**2. Identity assertion: render numbers only when `detail.id === props.partyId`.**

`PartyDetailPayload.Id` is published by C# and is `""` for an unknown or not-yet-published key
(§3.1). The pane branches on:

```tsx
const published = detail.id === props.partyId;
```

When false the pane renders its header and lifecycle line from `props.brief` — from
`agora.parties.roster`, a pushed `ValueBinding` that is always correct for the selected id — plus a
one-line notice, and renders **no** platform bars, no poll figures, no seat figures. Same shape as
`DistrictDetail.tsx:159` (`const detailPublished = !!detail.id;`) and `:175-182`, tightened from
"non-empty" to "matches the key I was mounted for". It makes rendering another party's numbers under
this party's name structurally impossible regardless of what the hook does.

**3. One source of the key.** `selectedPartyId$` (a `bindLocalValue`, per `ui_bindings.md:81-83`) is
the only thing feeding both the rail's selected state and the pane's `key`/`partyId`. Never key on
an array index — a roster reorder would silently re-point a live subscription.

**Initial selection.** The panel opens on the first row of the published roster (`roster[0].id`)
rather than an empty rail; unlike Districts there is no meaningful "city-wide" landing view.
`selectedPartyId$` starts as `""`; `PartiesPanel` treats `""` as "select the first row" at render
time and does **not** write back from a `useEffect` — a write during render/effect produces exactly
one extra render, which is the failure mode this section is about.

---

## 3. C# side

### 3.1 `src/Agora.Mod/UiBindings/AgoraUiPayloads.cs`

Two additions, in the `// ---- agora.parties` region, placed **after** `FactionBriefPayload` (ends
`:224`) so plan 0001's edits to `PartyBriefPayload` (`:165-193`) do not overlap.

The role enum — declared here rather than in `Agora.Core` because it is a *view* over `Coalition`,
not engine state:

```csharp
    /// <summary>
    /// A party's relationship to the sitting government, as one word. Derived from
    /// <c>PoliticalState.Government</c> in the projection: the engine has no such field, and
    /// <c>Party.IsIncumbent</c> / <c>IsInGovernment</c> between them cannot distinguish opposition
    /// from "not in the chamber at all" (Parties.cs:115-119).
    /// </summary>
    public enum PartyGovernmentRole
    {
        /// <summary>No sitting government, or the party is not named by it.</summary>
        None = 0,

        /// <summary>Holds the leadership: <c>Coalition.LeadPartyId</c>.</summary>
        Lead = 1,

        /// <summary>In government without leading it.</summary>
        Member = 2,

        /// <summary>Named in <c>Coalition.OppositionPartyIds</c>.</summary>
        Opposition = 3
    }
```

Then the payload. **Exact field names and types:**

```csharp
    /// <summary>
    /// The full detail for one party, fetched per key (<c>docs/contracts/ui_bindings.md</c> §4.2).
    /// </summary>
    /// <remarks>
    /// A map binding rather than a field on <see cref="PartyBriefPayload"/>: the roster is pushed to
    /// every panel on every monthly tick, and twelve issue positions plus polling per party is not
    /// something the seat chart or the news feed needs to carry.
    /// <para>
    /// Deliberately absent, because the panel resolves them through the roster (contract §4.2):
    /// <c>coreGrievance</c>, <c>isIncumbent</c>, <c>isInGovernment</c>. <see cref="Name"/>,
    /// <see cref="ShortName"/> and <see cref="ColorHex"/> are the exception — they are this pane's
    /// own header, and fixplan W4 edits them here.
    /// </para>
    /// </remarks>
    public sealed class PartyDetailPayload : IJsonWritable
    {
        public string Id = "";
        public string Name = "";
        public string ShortName = "";
        public string ColorHex = "#808080";
        public string ArchetypeId = "";
        public string Description = "";
        public string Slogan = "";

        public double PlatformServices, PlatformCostOfLiving, PlatformEnvironment,
                      PlatformTransit, PlatformGrowth, PlatformHeritageOrder;

        public double ManifestoServices, ManifestoCostOfLiving, ManifestoEnvironment,
                      ManifestoTransit, ManifestoGrowth, ManifestoHeritageOrder;

        public int Seats;
        public double SeatShare;
        public double LastVoteShare;
        public bool HasContestedElection;
        public bool PassedThreshold;
        public int ConsecutiveElectionsBelowThreshold;

        public double CurrentPollShare;
        public bool HasPoll;
        public Agora.Core.Contracts.SimDate? PollDate;
        public double PollDeltaSinceElection;
        public double CurrentStandingShare;

        public string Status = "Active";
        public Agora.Core.Contracts.SimDate? FoundedDate;
        public Agora.Core.Contracts.SimDate? DissolvedDate;

        public PartyGovernmentRole GovernmentRole = PartyGovernmentRole.None;
        public List<string> FactionIds = new List<string>();

        public void Write(IJsonWriter writer)
        {
            writer.TypeBegin("agora.PartyDetail");
            UiJson.Id(writer, "id", Id);
            UiJson.Text(writer, "name", Name);
            UiJson.Text(writer, "shortName", ShortName);
            UiJson.Text(writer, "colorHex", ColorHex);
            UiJson.Id(writer, "archetypeId", ArchetypeId);
            UiJson.Text(writer, "description", Description);
            UiJson.Text(writer, "slogan", Slogan);

            // One level of nesting is the contract's limit (§2 payload budget) and these two named
            // groups are it — same shape as DistrictDetail's wealth/education/age groups.
            writer.PropertyName("platform");
            writer.TypeBegin("agora.IssuePositionView");
            UiJson.Number(writer, "services", PlatformServices);
            UiJson.Number(writer, "costOfLiving", PlatformCostOfLiving);
            UiJson.Number(writer, "environment", PlatformEnvironment);
            UiJson.Number(writer, "transit", PlatformTransit);
            UiJson.Number(writer, "growth", PlatformGrowth);
            UiJson.Number(writer, "heritageOrder", PlatformHeritageOrder);
            writer.TypeEnd();

            writer.PropertyName("lastManifesto");
            writer.TypeBegin("agora.IssuePositionView");
            UiJson.Number(writer, "services", ManifestoServices);
            UiJson.Number(writer, "costOfLiving", ManifestoCostOfLiving);
            UiJson.Number(writer, "environment", ManifestoEnvironment);
            UiJson.Number(writer, "transit", ManifestoTransit);
            UiJson.Number(writer, "growth", ManifestoGrowth);
            UiJson.Number(writer, "heritageOrder", ManifestoHeritageOrder);
            writer.TypeEnd();

            UiJson.Number(writer, "seats", Seats);
            UiJson.Number(writer, "seatShare", SeatShare);
            UiJson.Number(writer, "lastVoteShare", LastVoteShare);
            UiJson.Flag(writer, "hasContestedElection", HasContestedElection);
            UiJson.Flag(writer, "passedThreshold", PassedThreshold);
            UiJson.Number(writer, "consecutiveElectionsBelowThreshold",
                          ConsecutiveElectionsBelowThreshold);

            UiJson.Number(writer, "currentPollShare", CurrentPollShare);
            UiJson.Flag(writer, "hasPoll", HasPoll);
            UiJson.Date(writer, "pollDate", PollDate);
            UiJson.Number(writer, "pollDeltaSinceElection", PollDeltaSinceElection);
            UiJson.Number(writer, "currentStandingShare", CurrentStandingShare);

            UiJson.Text(writer, "status", Status);
            UiJson.Date(writer, "foundedDate", FoundedDate);
            UiJson.Date(writer, "dissolvedDate", DissolvedDate);

            UiJson.Enum(writer, "governmentRole", GovernmentRole);
            UiJson.Ids(writer, "factionIds", FactionIds);
            writer.TypeEnd();
        }
    }
```

`UiJson.Enum` (`AgoraUiPayloads.cs:65-69`) writes the C# member name, per contract §2 — *"Enums | C#
member name string … Never the integer."*

**An unknown or absent key returns `new PartyDetailPayload()`**, i.e. `id: ""`. It never throws
(§3.4), matching `ui_bindings.md:177-179`.

### 3.2 `src/Agora.Mod/UiBindings/AgoraStateUISystem.cs`

`agora.parties` is published by `AgoraStateUISystem`, not a system of its own — `ui_bindings.md:28`
assigns it there and the rationale is at `AgoraStateUISystem.cs:12-16`. Do not create a new system.

Add a field:

```csharp
        private GetterMapBinding<string, PartyDetailPayload> _partyDetail;
```

In `CreateBindings()`, after the `_factions` registration (`:39-40`):

```csharp
            // AddBinding, not AddUpdateBinding — same reasoning as AgoraDistrictsUISystem.cs:38-41:
            // an update binding re-evaluates every subscribed key every UI tick, and each payload is
            // a freshly built object so the comparer would call it changed every time.
            AddBinding(_partyDetail = new GetterMapBinding<string, PartyDetailPayload>(
                PartiesGroup, "detail", GetPartyDetail));

        /// <summary>
        /// One party's detail. An unknown key returns the empty payload rather than throwing: a map
        /// binding that threw would take the interface down with it.
        /// </summary>
        private static PartyDetailPayload GetPartyDetail(string partyId) =>
            AgoraUiProjection.BuildPartyDetail(AgoraRuntime.State, partyId);
```

In `Publish()`, after `_factions.Update(...)` (`:63`):

```csharp
            // UpdateAll only pushes keys the panel actually has subscribed, so this costs nothing
            // when the Parties tab is closed.
            _partyDetail.UpdateAll();
```

No `valueWriter:` argument — the value is a single `IJsonWritable`, not a `List<T>` (contrast
`AgoraDistrictsUISystem.cs:47-48`, where the crosstab map does need one).

### 3.3 `src/Agora.Mod/UiBindings/AgoraUiProjection.cs`

One new method in the `// ---- agora.parties` section, appended after `CompareFactionRows` (ends
`:137`):

```csharp
        internal static PartyDetailPayload BuildPartyDetail(PoliticalState state, string partyId)
```

Nothing here computes politics; the only arithmetic is one division and one subtraction — the same
class of work as `MayorMargin` (`:226-240`) and `WeeksToElection` (`:54-64`).

| Payload field | Source | Rule |
|---|---|---|
| `Id`…`Slogan` | `Party.Id/.Name/.ShortName/.ColorHex/.ArchetypeId/.Description/.Slogan` | direct copy |
| `Platform*` | `Party.Platform` (`Parties.cs:97`) | six copies off `IssuePosition` |
| `Manifesto*` | `Party.LastManifesto` (`Parties.cs:100`) | six copies |
| `Seats` | `Party.SeatsHeld` (`Parties.cs:113`) | the **live** count, which can differ from the last election's allocation after a lifecycle event |
| `SeatShare` | `TotalSeats(state) > 0 ? (double)party.SeatsHeld / TotalSeats(state) : 0.0` | `TotalSeats` exists at `AgoraUiProjection.cs:174-177` |
| `LastVoteShare` | `Party.LastVoteShare` (`Parties.cs:110`) | 0 before the first election |
| `HasContestedElection` | latest `ElectionResult.PartyIdsOnBallot` (`Elections.cs:204`) contains `partyId` | false when `state.ElectionHistory` is empty |
| `PassedThreshold` | latest `ElectionResult.Seats` entry for `partyId`, `.PassedThreshold` (`Elections.cs:135`) | false when no election or no entry |
| `ConsecutiveElectionsBelowThreshold` | `Party.ConsecutiveElectionsBelowThreshold` (`Parties.cs:125`) | direct copy |
| `CurrentPollShare`, `HasPoll`, `PollDate` | newest `PollResult` with `IsPublished`, its `Shares` entry for `partyId` | scan `state.RecentPolls` backwards, exactly the loop at `:331-350`. `HasPoll` false ⇒ share 0 |
| `PollDeltaSinceElection` | `HasPoll && HasContestedElection ? CurrentPollShare - LastVoteShare : 0.0` | signed; the panel renders the sign |
| `CurrentStandingShare` | `state.CurrentVoteShares` entry for `partyId` (`PoliticalState.cs:157`) | already published city-wide as `agora.seats.voteShares`, so nothing new is exposed |
| `Status` | `Party.Status.ToString()` | matches `PartyBriefPayload` |
| `FoundedDate` / `DissolvedDate` | direct copy | |
| `GovernmentRole` | §3.4 | |
| `FactionIds` | `SortedCopy(party.FactionIds)` | helper at `:1003-1015`; contract requires ascending |

**`PollResult.TrueShares` is never read.** Contract rule 8 (`ui_bindings.md:367-369`) and the remark
at `AgoraUiPayloads.cs:391-396` — reading it here would be a review-blocking defect.

Guards, first lines: `state == null` or `string.IsNullOrEmpty(partyId)` → return
`new PartyDetailPayload()`. Party not found in `state.Parties` → same. This is the
`BuildDistrictDetail` shape (`:410-426`).

### 3.4 Government role, exactly

```csharp
        private static PartyGovernmentRole RoleOf(PoliticalState state, string partyId)
        {
            Coalition government = state.Government;
            if (government == null) return PartyGovernmentRole.None;
            if (string.CompareOrdinal(government.LeadPartyId, partyId) == 0)
                return PartyGovernmentRole.Lead;
            if (government.MemberPartyIds.Contains(partyId)) return PartyGovernmentRole.Member;
            if (government.OppositionPartyIds.Contains(partyId)) return PartyGovernmentRole.Opposition;
            return PartyGovernmentRole.None;
        }
```

Order matters: `MemberPartyIds` *"Always contains `LeadPartyId`"* (`Government.cs:63`), so the lead
test must come first.

---

## 4. The six platform axes — the label mapping

The enum, quoted from source. `src/Agora.Core/Contracts/Issues.cs:16-35`:

```csharp
    public enum Issue
    {
        /// <summary>Health, education, police, fire, garbage, utilities — is the city looked after.</summary>
        Services = 0,                                                            // Issues.cs:19

        /// <summary>Rent, land value, taxes, unemployment — can people afford to live here.</summary>
        CostOfLiving = 1,                                                        // Issues.cs:22

        /// <summary>Air, ground, noise and water pollution; parks and green space.</summary>
        Environment = 2,                                                         // Issues.cs:25

        /// <summary>Commute time, transit coverage, traffic, parking.</summary>
        Transit = 3,                                                             // Issues.cs:28

        /// <summary>Development, jobs, new construction, densification.</summary>
        Growth = 4,                                                              // Issues.cs:31

        /// <summary>Crime, order, stability, and resistance to change.</summary>
        HeritageOrder = 5                                                        // Issues.cs:34
    }
```

The end labels come from the sign convention, which is fixed and not negotiable —
`src/Agora.Core/Contracts/Issues.cs:196-203`:

> *"Sign convention, fixed and not negotiable per issue because affinity depends on it: `+1` means
> "spend/protect/restrict more" and `-1` means "spend/protect/restrict less". Concretely: +Services
> = more public spending; +CostOfLiving = prioritise affordability over revenue; +Environment =
> stricter environmental protection; +Transit = invest in transit over cars; +Growth =
> pro-development; +HeritageOrder = more order and preservation."*

**The mapping to be written into `ui/src/panels/Parties/format.ts`.** Row order is `Issues.All`
order (`Issues.cs:47-58`) — declaration order, which is also the order every engine sum uses.

| # | `Agora.IssueName` | Row label | `-1` end | `+1` end |
|---|---|---|---|---|
| 1 | `Services` | **Public services** | Spend less | Spend more |
| 2 | `CostOfLiving` | **Cost of living** | Revenue first | Affordability first |
| 3 | `Environment` | **Environment** | Fewer restrictions | Stricter protection |
| 4 | `Transit` | **Transit** | Roads and cars | Buses and trains |
| 5 | `Growth` | **Growth** | Restrain building | Build more |
| 6 | `HeritageOrder` | **Heritage and order** | Open to change | Order and preservation |

As TypeScript, verbatim:

```ts
/** Issues.All order (Issues.cs:47-58) — declaration order, and the order every engine sum uses. */
export const ISSUE_ORDER: Agora.IssueName[] = [
  "Services", "CostOfLiving", "Environment", "Transit", "Growth", "HeritageOrder",
];

/** Plain English. `HeritageOrder` is an enum member name and must never reach the player. */
export const ISSUE_LABEL: Record<Agora.IssueName, string> = {
  Services: "Public services",
  CostOfLiving: "Cost of living",
  Environment: "Environment",
  Transit: "Transit",
  Growth: "Growth",
  HeritageOrder: "Heritage and order",
};

/**
 * What each end of the axis means, from the sign convention at Issues.cs:196-203. `+1` is
 * "spend/protect/restrict more", `-1` is "less".
 */
export const ISSUE_POLE_LOW: Record<Agora.IssueName, string> = {
  Services: "Spend less",
  CostOfLiving: "Revenue first",
  Environment: "Fewer restrictions",
  Transit: "Roads and cars",
  Growth: "Restrain building",
  HeritageOrder: "Open to change",
};

export const ISSUE_POLE_HIGH: Record<Agora.IssueName, string> = {
  Services: "Spend more",
  CostOfLiving: "Affordability first",
  Environment: "Stricter protection",
  Transit: "Buses and trains",
  Growth: "Build more",
  HeritageOrder: "Order and preservation",
};

/** Payload key for each issue — `IssuePositionView`'s properties are camelCased enum members. */
export const ISSUE_KEY: Record<Agora.IssueName, keyof Agora.IssuePositionView> = {
  Services: "services",
  CostOfLiving: "costOfLiving",
  Environment: "environment",
  Transit: "transit",
  Growth: "growth",
  HeritageOrder: "heritageOrder",
};
```

`ISSUE_KEY` earns its place: it makes `PlatformBars` a loop over `ISSUE_ORDER` instead of six
copy-pasted rows, and it is the compiler's only chance to catch a payload/label mismatch.

---

## 5. TypeScript and UI

### 5.1 `ui/types/bindings.d.ts` — three additions

All inside the existing `declare namespace Agora` block (`:3674`). Add the union next to the other
enum-name unions (after `PartyStatusName`, `:3699`):

```ts
  /** Derived in the UI publisher from PoliticalState.Government; no engine field carries it. */
  type PartyGovernmentRoleName = "None" | "Lead" | "Member" | "Opposition";
```

Add the shared nested group and the detail immediately after `interface PartyBrief` (`:3765-3778`):

```ts
  /**
   * A stance on each issue, each in [-1, +1]. Sign convention (Issues.cs:196-203): +1 is
   * "spend/protect/restrict more", -1 is "less". Never render an enum member name for these —
   * see ISSUE_LABEL in the Parties panel.
   */
  interface IssuePositionView {
    services: number;
    costOfLiving: number;
    environment: number;
    transit: number;
    growth: number;
    heritageOrder: number;
  }

  /**
   * `agora.parties.detail` — a MAP binding keyed by `PartyBrief.id`. An unknown key returns
   * EMPTY_PARTY_DETAIL (`id: ""`), never throws.
   *
   * `name`, `shortName`, `description` and `slogan` are FLAVOR. Everything else is engine-owned.
   * `coreGrievance`, `isIncumbent` and `isInGovernment` are deliberately NOT here — resolve them
   * through `agora.parties.roster`.
   */
  interface PartyDetail {
    id: IdString;
    name: string;
    shortName: string;
    /** "#RRGGBB". Engine-owned, from the tuned palette. */
    colorHex: string;
    archetypeId: IdString;
    description: string;
    slogan: string;
    /** Current stance. */
    platform: IssuePositionView;
    /** The stance it ran on at the last election. Meaningless when `!hasContestedElection`. */
    lastManifesto: IssuePositionView;
    /** Live seat count, which can differ from the last election's allocation. */
    seats: number;
    /** [0,1]. Zero before the first election. */
    seatShare: number;
    /** [0,1]. Zero before the first election. */
    lastVoteShare: number;
    hasContestedElection: boolean;
    /** Cleared the electoral threshold at the last count. False when there has been none. */
    passedThreshold: boolean;
    /** Survival counter toward dissolution — a different fact from `passedThreshold`. */
    consecutiveElectionsBelowThreshold: number;
    /** [0,1] from the newest PUBLISHED poll. Zero when `!hasPoll`. */
    currentPollShare: number;
    hasPoll: boolean;
    /** "" when `!hasPoll`. */
    pollDate: SimDateString;
    /** Signed: `currentPollShare - lastVoteShare`. Zero unless both flags are true. */
    pollDeltaSinceElection: number;
    /** [0,1] city-wide standing, the same figure as `agora.seats.voteShares`. */
    currentStandingShare: number;
    status: PartyStatusName;
    foundedDate: SimDateString;
    /** "" while the party still exists. */
    dissolvedDate: SimDateString;
    governmentRole: PartyGovernmentRoleName;
    /** Ascending. Empty under the EU theme, which models no factions. */
    factionIds: IdString[];
  }
```

### 5.2 New folder `ui/src/panels/Parties/`

Modelled file-for-file on `ui/src/panels/Districts/`, the panel this one most resembles (list rail +
map-bound detail pane).

| File | Contents |
|---|---|
| `index.ts` | `export { PartiesPanel } from "./PartiesPanel";` — a value re-export only; `isolatedModules` is on (`Districts/index.ts:2`) |
| `bindings.ts` | `EMPTY_PARTY_DETAIL`, `enabled$`, `ready$`, `summary$`, `roster$`, `allocation$`, `latestPoll$`, `government$`, `partyDetail$` (`bindMap`), `selectedPartyId$` (`bindLocalValue<string>("")`) |
| `format.ts` | the §4 constants, plus `pct` / `points` / `signedPoints` / `int` / `partyColor` / `hexToRgba` |
| `Boundary.tsx` | class error boundary, copied in shape from `Districts/Boundary.tsx`, message retitled |
| `PartiesPanel.tsx` | shell: header, `!ready` skeleton, `!enabled → null`, the rail + detail flex row, and the `key={selectedId}` mount from §2 |
| `PartyList.tsx` | the rail |
| `PartyDetail.tsx` | the pane; the only file that calls `useMapValue` |
| `PlatformBars.tsx` | the six-row centre-zero bar set |
| `PartiesPanel.module.scss`, `PartyList.module.scss`, `PartyDetail.module.scss`, `PlatformBars.module.scss` | styling, §5.4 |

**`bindings.ts` — the binding declarations, exactly:**

```ts
export const roster$ = bindValue<Agora.PartyBrief[]>("agora.parties", "roster", []);
export const partyDetail$ = bindMap<string, Agora.PartyDetail>("agora.parties", "detail");
export const allocation$ = bindValue<Agora.SeatRow[]>("agora.seats", "allocation", []);
export const latestPoll$ = bindValue<Agora.PollSummary | null>("agora.seats", "latestPoll", null);
export const government$ = bindValue<Agora.GovernmentSummary | null>(
  "agora.seats", "government", null
);
export const selectedPartyId$ = bindLocalValue<string>("");
```

`allocation$` and `latestPoll$` feed the rail's seats and poll-share columns; `government$` gives the
pane's role line a count ("in government with two others"). All three are already published
(`ui_bindings.md:137-146`) and need no change.

`EMPTY_PARTY_DETAIL` is the literal that goes into contract §6 verbatim — every string `""`, every
number `0`, every boolean `false`, both `IssuePositionView`s all-zero, `factionIds: []`,
`colorHex: "#808080"` (matching the C# initialiser), `status: "Active"`, `governmentRole: "None"`.

**`PartyList.tsx` — the rail.** One row per `Agora.PartyBrief` in published order (`id` ordinal
ascending — the panel does not re-sort, contract rule 7). Each row: a colour swatch stripe
(`brief.colorHex`), the **short name** with the full name beneath, seats, and poll share. Seats come
from `allocation$`, poll share from `latestPoll$.shares` — **do not** subscribe the detail map for
every row to fill the rail; that defeats the entire reason `detail` is a map binding. A party with no
allocation row shows `-`, not `0`.

Dissolved parties stay in the roster (§0.2). The rail renders them at reduced emphasis with a
`Dissolved` chip rather than hiding them — a save's dead brands are half the point of the tab — and
the label derives from `brief.status`, never from a name string.

**`PartyDetail.tsx` — the pane.** Sections, top to bottom (chunk letters mark what Part II inserts):

1. **Header** — swatch, name, short name, status/role chips. **This is W4's seam**: keep it as one
   component, `PartyDetailHeader`, taking `{ detail, brief }` and rendering only text. W4 adds the
   rename field, the colour picker and the lock affordances *inside* it, reading `brief.nameLocked` /
   `brief.colorLocked` (plan 0001 §7). W6 adds no button, no input, no `trigger`.
2. **Slogan**, if non-empty — one line, quoted, dimmed. Flavor.
3. **Description**, if non-empty — one paragraph. Flavor. When both are empty the pane says so in one
   sentence ("The press has not written this party up yet"), because that is a real state on a save
   whose flavor provider has never run (`fixplan.md` W2).
4. **Standing** — a four-cell stat row: `Seats` (`seats`, with `pct(seatShare)` beneath), `Last vote`
   (`pct(lastVoteShare)`), `Polling` (`pct(currentPollShare)`), `Since the election`
   (`signedPoints(pollDeltaSinceElection)` tinted `$good`/`$bad` by sign). When
   `!hasContestedElection` the last two cells render `-` and a one-line note — a delta against a vote
   share that does not exist is a lie, not a zero. When `hasContestedElection && !hasPoll`, `Polling`
   falls back to `currentStandingShare` **labelled differently** ("Standing", not "Polling") so a
   modelled figure is never presented as a published poll.
   *(Chunk E appends the sparkline to this row.)*
5. **Threshold** — one line carrying both facts (§1c). Under FPTP
   (`summary.system === "FirstPastThePost"`) the electoral threshold does not apply; the line renders
   the survival counter alone.
6. **Issue priorities** — `<PlatformBars values={detail.platform} />`. §5.3.
   *(Chunk D adds the manifesto marker here.)*
7. **Lifecycle** — founded date, dissolved date when present, government role in words, and the
   faction list resolved through the roster (empty under EU, which the pane states rather than
   rendering an empty box). *(Chunk F expands this into the history strip.)*
   *(Chunks G and H append two further sections after it.)*

Everything from item 4 down sits inside the `published` guard from §2.

### 5.3 `PlatformBars.tsx` — the six-row bar set

Props: `{ values: Agora.IssuePositionView }`. One `<PlatformBar>` per entry of `ISSUE_ORDER`, so
adding or reordering an issue is a constant edit, never a markup edit.

Each row is a flex row of three children:

```
[ label, fixed width ] [ centre-zero track, flex: 1 1 0 ] [ numeric readout, fixed width ]
```

**The track must be centre-zero, not left-anchored.** Positions are in `[-1, +1]`
(`Issues.cs:192-193`) and a left-anchored bar would render "spend less" as a short "spend more".
Flex-only, no grid and no absolute positioning:

- the track is a flex row containing two halves, each `flex: 1 1 0`;
- the left half is `justify-content: flex-end` and holds a fill of width `pct(max(0, -value))`;
- the right half is `justify-content: flex-start` and holds a fill of width `pct(max(0, value))`;
- a 1rem centre rule sits between them as a third, zero-basis child.

Fill colour is the party's `colorHex` at reduced alpha via `hexToRgba` (`Districts/format.ts:77-88` —
copy it into `Parties/format.ts`; there is no shared runtime module, by design, per the contract §6
note).

The row carries a `Tooltip` (`cs2/ui`) giving the two pole labels and the enum's own one-line
description, so the player can learn what "Heritage and order" covers without ever being shown the
identifier. The readout is `value.toFixed(2)` with an explicit sign; within 0.02 of zero it reads
`Centre`.

**Signature note:** take `values` as one prop, not six. That is what lets chunk D become a second
prop rather than a rewrite (§12).

### 5.4 Styling

Every stylesheet opens with `@use "../../shell/tokens" as *;` and declares **none** of the nineteen
reserved names — the guard at `ui/tools/css-presence.js:63-89` fails the build on a local `$surface`,
`$text`, `$line`, `$accent`, `$good`, `$warn`, `$bad`, `$fallback*` or any `$surface-*` / `$text-*` /
`$line-*`. `npm run check` (`ui/package.json:8`) runs it standalone.

- Panel body: `$surface`, matching `DistrictsPanel.module.scss:17`.
- Rail rows: `$surface-inset` at rest, `$surface-hover` on hover, `$line-strong` border when
  selected. Row separator: `$line-soft`.
- Section rules: `$line`. Stat chips and the platform track: `$surface-raised` / `$surface-track`.
- Text: `$text`, `$text-dim` for the description, `$text-faint` for dates and unit labels.
- Poll delta: `$good` positive, `$bad` negative, `$text-faint` at zero.
- `$fallback*` is **not used in this panel** — `hasCityFallbacks` is a district concept and no field
  here is a city stand-in.

Geometry: panel `width: 640rem; max-height: 620rem`, matching the Districts panel so the shell slot
does not resize between tabs. Rail `flex: 0 0 190rem`; detail column `flex: 1 1 auto` inside a
`<Scrollable vertical>` from `cs2/ui`. **Flex only** — Gameface has no CSS grid and fails silently on
it (`DistrictsPanel.module.scss:1`).

### 5.5 Shell registration — four edits

`ui/src/shell/state.ts`:

- `:18` — `export type AgoraTab = "council" | "parties" | "districts" | "news";`
- `:21` — `export const TAB_ORDER: AgoraTab[] = ["council", "parties", "districts", "news"];`
- `:23-27` — add `parties: "Parties",` to `TAB_LABEL`.

Parties sits second deliberately: Council answers "who governs", Parties answers "who are they", and
the two are read together. Districts and News are drill-downs.

`ui/src/shell/Dashboard.tsx`:

- add `import { PartiesPanel } from "../panels/Parties";` next to the other three (`:4-6`)
- add `case "parties": return <PartiesPanel />;` to `renderTab` (`:36-44`). **Do not rely on the
  `default:` branch** — §1e; omitting this case silently renders the Council panel.

No change is needed to the `key={tab}` remount at `Dashboard.tsx:97`.

---

## 6. `docs/contracts/ui_bindings.md` — the edits

Six, all required. The contract *is* the registration (rule 1, `:346`: *"Register here first,
implement second."*).

1. **`:3`** — bump `**schemaVersion:**` by one, from whatever value is in the file. **Plan 0001
   lands first and leaves it at `3`, so W6 makes it `4`.** See §8 and checklist step 6.
2. **`:9-11`** — the "Frozen for M4" paragraph. Append: *"fixplan W6 unfroze `agora.parties` on
   2026-08-08 to add `detail`, under plan `docs/plans/0002-w6-parties-tab.md`. The freeze otherwise
   stands."* Add this **beside** plan 0001's own amendment; do not replace it.
3. **§4.2 table (`:120-126`)** — a third row:

   | `agora.parties.detail` | `GetterMapBinding<string,T>` | `PartyDetail` per key | `Agora.PartyDetail` | on demand, per subscribed key | `EMPTY_PARTY_DETAIL` | W6 |

   and, under the table, a paragraph mirroring `:177-179`: *"The map key is the **party id** exactly
   as it appears in `PartyBrief.id`. An unknown key returns the empty value, never throws. Unlike a
   district, a party id is never removed from the roster — a dead party becomes `Dissolved` — so an
   id that resolves once resolves for the life of the save."* Plus the sort key: *"`detail.factionIds`:
   ordinal ascending."*
4. **§5 payload shapes (`:243-289`)** — two entries after the `PartyBrief` line (`:246-247`):

   ```
   IssuePositionView   services, costOfLiving, environment, transit, growth, heritageOrder
   PartyDetail         id, name, shortName, colorHex, archetypeId, description, slogan,
                       platform{…IssuePositionView}, lastManifesto{…IssuePositionView}, seats,
                       seatShare, lastVoteShare, hasContestedElection, passedThreshold,
                       consecutiveElectionsBelowThreshold, currentPollShare, hasPoll, pollDate,
                       pollDeltaSinceElection, currentStandingShare, status, foundedDate,
                       dissolvedDate, governmentRole, factionIds
   ```
5. **§5 "Which fields are flavor" (`:291-295`)** — add `PartyDetail.name`/`shortName`/`description`/
   `slogan` to the flavor list.
6. **§6 (`:296` onward)** — append the `EMPTY_PARTY_DETAIL` literal verbatim, so the panel and the
   contract cannot disagree.

`agora.parties.detail` collides with nothing in §8 Reserved (`:376-387`) — checked all six.

---

## 7. Verification

**There is no headless test for the UI-binding surface, and there cannot be one without a refactor.**
Stating why, so nobody spends a session discovering it: `tests/Agora.Core.Tests/Agora.Core.Tests.csproj`
links four `Agora.Mod` files and nothing from `UiBindings/` (plan 0001 §6.0 / finding j).
`AgoraUiPayloads.cs` imports `Colossal.UI.Binding` and can never be linked into a suite that must run
without the game; `AgoraUiProjection.cs` references `Agora.Mod.Core.AgoraRuntime` in
`BuildFlavorStatus` (`AgoraUiProjection.cs:885-892`), which pulls the same dependency in through the
one file. Making `BuildPartyDetail` testable means splitting the projection into a game-free half —
real work, worth doing, and **not** in W6's budget. **Recommend to Master as a follow-up.**

**Chunk H is the exception**: its engine half lives in `Agora.Core` and gets real tests in the
existing `tests/Agora.Core.Tests/CoalitionsTests.cs` (§16.5).

The gate for Part I:

- `dotnet build Agora.sln` green.
- `dotnet test tests\Agora.Core.Tests\Agora.Core.Tests.csproj` green — unchanged, but must not regress.
- `cd ui && npm run build` green, which includes `npm run check` (the design token guard).
- The manual walkthrough, checklist step 13.

---

## 8. Interaction with plan 0001

**Sequencing: 0001 lands first** (`fixplan.md:384-388` puts the batched schema pass ahead of W6, and
W4 — which consumes both — after it). Files touched by both, in disjoint regions:

| File | 0001 touches | 0002 touches | Merge |
|---|---|---|---|
| `src/Agora.Mod/UiBindings/AgoraUiPayloads.cs` | `PartyBriefPayload`, `:165-193` | new types appended after `FactionBriefPayload` (`:224`) | clean — different hunks |
| `src/Agora.Mod/UiBindings/AgoraUiProjection.cs` | `BuildRoster`, `:84` | new method after `:137` | clean |
| `ui/types/bindings.d.ts` | three fields on `PartyBrief` (`:3765-3778`), new `SettingsPayload` | new types after `:3778`, new union after `:3699` | clean |
| `docs/contracts/ui_bindings.md` | `:3`, `:9-11`, `:126`, `:247`, `:291-295`, `:387` | `:3`, `:9-11`, §4.2 table, §5, §6 | **`:3` and `:9-11` genuinely collide** — §6 items 1–2 |

**The `schemaVersion` collision, explicitly.** `docs/contracts/ui_bindings.md:3` currently reads
`**schemaVersion: 2**`. Plan 0001 §1 takes it to **3**. W6 takes it to **4**. Neither pass may claim
the same number: **the coder opens the file, reads the value, and writes value + 1** — it does not
hard-code `3`. Checklist step 6 says so. If W6 somehow lands first, it writes `3` and 0001's coder
writes `4`; the rule is the same either way.

If chunk H takes the persisted design (§16.2), **0001 must additionally carry one field** — §16.3
spells it out, and that is a hard ordering dependency, not a merge note.

---

## 9. Reserved

*(Was the six-addition survey. Superseded by Part II, which plans the five accepted items.
Addition 2, the bloc support breakdown, was declined by the owner on 2026-08-08 and is not planned.
For the record, had it been accepted it would have needed a new map binding
`agora.parties.blocSupport` rather than a payload field: `Bloc.PreviousVote` (`Blocs.cs:232`) is
persisted and already aggregated in the projection (`AgoraUiProjection.cs:527-542`), so nothing new
would be simulated, but a 60-bloc × N-party table is far too big to ride on `PartyDetail`.)*

---

## 10. Ordered checklist — Part I (core tab)

Riskiest first. Steps 1–4 exist to put the §2 stale-frame question in front of a real game before any
layout work — if the remount does not behave, that is the moment to find out.

### Chunk A — the bridge, end to end (de-risk)

- [ ] **1.** `AgoraUiPayloads.cs`: `PartyGovernmentRole` and `PartyDetailPayload` with its full
      `Write` (§3.1). Compile only; nothing consumes it yet.
- [ ] **2.** `AgoraUiProjection.BuildPartyDetail` + `RoleOf` (§3.3, §3.4). `dotnet build Agora.sln`.
- [ ] **3.** `AgoraStateUISystem.cs`: the field, the `AddBinding` in `CreateBindings`, the
      `UpdateAll()` in `Publish` (§3.2).
- [ ] **4.** **Smoke test, and it is the point of this chunk.** A throwaway `PartiesPanel` that
      renders nothing but the rail (short names only) and a one-line pane showing `selectedId` beside
      `detail.id` and `detail.name`, mounted via §5.5, with the `key={selectedId}` remount from §2 in
      place. Launch with `--uiDeveloperMode`, click rapidly down the rail, and confirm the two ids are
      **never** different on any frame. *If they can differ, stop and report — the whole pane design
      depends on this.* Then temporarily remove the `key` and confirm they **do** differ, so the fix is
      known to be the thing that works rather than assumed to be.

### Chunk B — types and contract

- [ ] **5.** `ui/types/bindings.d.ts`: `PartyGovernmentRoleName`, `IssuePositionView`, `PartyDetail`
      (§5.1).
- [ ] **6.** `docs/contracts/ui_bindings.md`: all six edits in §6. **For edit 1: open the file, read
      the current `schemaVersion` at `:3`, and write that value plus one. Do not hard-code a number —
      plan 0001 is moving the same line (§8).**

### Chunk C — the panel

- [ ] **7.** `Parties/bindings.ts`, `Parties/format.ts` (including the whole §4 label mapping),
      `Parties/index.ts`, `Parties/Boundary.tsx`.
- [ ] **8.** `Parties/PlatformBars.tsx` + `PlatformBars.module.scss` — the centre-zero bar (§5.3).
      Build it before the pane: it is the one piece of layout Gameface can surprise you on, and it is
      the visible half of the tab.
- [ ] **9.** `Parties/PartyList.tsx` + `PartyList.module.scss` (§5.2).
- [ ] **10.** `Parties/PartyDetail.tsx` + `PartyDetail.module.scss`, with `PartyDetailHeader` kept as
      its own component for W4 (§5.2 item 1).
- [ ] **11.** `Parties/PartiesPanel.tsx` + `PartiesPanel.module.scss`, replacing the step-4 throwaway.
      Delete every scrap of the throwaway.

### Close Part I

- [ ] **12.** `dotnet build Agora.sln`; `dotnet test tests\Agora.Core.Tests\Agora.Core.Tests.csproj`;
      `cd ui && npm run build`. All three green.
- [ ] **13.** Manual walkthrough, one session: open the tab on a save with **no** election yet —
      confirm seats/vote/poll cells read `-` with the note, not `0%`. Play to the first election;
      confirm seat share, vote share and `passedThreshold` populate, and that the poll delta appears
      only after the next published poll. Confirm a dissolved party still appears in the rail with its
      chip. Confirm no enum member name (`HeritageOrder`, `CostOfLiving`, `FirstPastThePost`) is
      visible anywhere. Click every party in the rail twice in succession and confirm the pane never
      shows the previous party's numbers.
- [ ] **14.** Update `docs/status.md:22` — the tab list is hard-coded there — and record W6 core
      complete.

**Part I is shippable here.** Part II may follow immediately or later.

---

# Part II — the five accepted additions

## 11. How Part II is organised, and what it costs

Each chunk is independent of the others and depends only on Part I. They may land in any order,
except that H should be last because it is the only one with an engine dependency.

| Chunk | Addition | New binding? | Engine work? | Schema change? |
|---|---|---|---|---|
| D (§12) | Manifesto vs. platform | no — fields already published | no | no |
| E (§13) | Poll trend sparkline | **yes** — `agora.parties.pollTrend` (map) | no | no |
| F (§14) | Party history strip | no — four fields on `PartyDetail` + one list | no | no |
| G (§15) | Mandate scorecard | **no binding at all** — reuses `agora.news.mandates` | no | no |
| H (§16) | Coalition relations | **yes** — `agora.parties.relations` (map) | **yes, `Agora.Core`** | **only under the persisted design — owner choice** |

**Contract `schemaVersion` again:** each chunk that touches `docs/contracts/ui_bindings.md:3`
increments what it finds. If D–H land as one commit with Part I, the line moves **once**, not five
times.

---

## 12. Chunk D — manifesto vs. current platform (addition 1)

`fixplan.md:323-325`: *"`LastManifesto` is already stored separately from `Platform`. Rendering 'ran
on X, now stands at Y' is a betrayal meter for free."* **This one is genuinely free** — verified:
`Party.Platform` (`src/Agora.Core/Contracts/Parties.cs:97`) and `Party.LastManifesto`
(`Parties.cs:100`) are separate persisted properties, and Part I already publishes both as
`platform` and `lastManifesto` (§3.1).

### 12.1 Payload

**No change.** Zero new fields, zero contract edits. This chunk is UI only.

### 12.2 UI

`PlatformBars.tsx` gains one optional prop — which is exactly why §5.3 specified `values` as a single
object prop:

```tsx
export const PlatformBars = (props: {
  values: Agora.IssuePositionView;
  /** Second series drawn as a tick, not a fill: the position this party RAN on. */
  marker?: Agora.IssuePositionView;
  markerLabel?: string;
  color: string;
}): JSX.Element => …
```

Per row, the marker is a 2rem vertical tick positioned in the same centre-zero track as the fill,
using the same two-half flex construction (§5.3) — the tick is the last child of whichever half its
sign puts it in, with `margin-left: auto` (left half) or `margin-right: auto` (right half) to push it
to the correct offset. **No absolute positioning**, so it survives Gameface.

Call site in `PartyDetail.tsx`, section 6:

```tsx
<PlatformBars
  values={detail.platform}
  marker={detail.hasContestedElection ? detail.lastManifesto : undefined}
  markerLabel="Ran on"
  color={detail.colorHex}
/>
```

`marker` is omitted when `!hasContestedElection` — a party that has never stood for election has no
manifesto, and `LastManifesto` defaults to `IssuePosition.Centre` (`Parties.cs:100`), so drawing the
tick would assert "it ran on dead centre", which is false. This is the same honesty rule as the
`Polling` / `Standing` distinction in §5.2 item 4.

### 12.3 The betrayal summary line

Above the bars, one line: *"Moved N points from its manifesto on M of 6 issues."* Both numbers are
computed in the panel from two published vectors, which is re-expression rather than derivation —
the same category as `pct()`. Threshold for "moved": 0.15, declared as a named constant
`MANIFESTO_DRIFT_THRESHOLD` in `format.ts` with a comment saying it is a *display* threshold and
nothing in the engine reads it.

Do **not** call this "betrayal" in the UI. The engine has no betrayal concept, no mandate is being
scored here, and a platform that drifts because the city changed is not a broken promise. The section
heading is **"Manifesto and drift"**.

### 12.4 Contract

One edit: `docs/contracts/ui_bindings.md` §4.2, annotate the `agora.parties.detail` row — *"`platform`
and `lastManifesto` are rendered together as a drift comparison (fixplan W6 addition 1); both were in
the binding from the start."* No version bump on its own account, because no payload changed.

---

## 13. Chunk E — poll trend sparkline (addition 3)

### 13.1 The reservation — verified, and the answer is "do not use it"

`fixplan.md:331` says *"`agora.seats.pollTrend` is already reserved in the contract for M6"*.
**Confirmed:** `docs/contracts/ui_bindings.md:380` lists

| `agora.seats.pollTrend` | trend chart of published poll shares over time | M6 |

under **§8 Reserved — registered as names, not yet published, do not consume**. Contract rule 3
(`:349-351`) forbids consuming it: *"`useValue` on a binding the C# side has not registered returns
the fallback at best and throws at worst. Nothing from §8 Reserved may be consumed."*

So W6 must either publish it or add its own. **Recommendation: add a party-scoped map binding, and
leave `agora.seats.pollTrend` reserved for M6.** Two reasons, the first decisive:

1. **A city-wide trend violates the payload budget.** `agora.seats.pollTrend` as described is a
   series of dates each carrying every party's share — a list of rows that themselves contain lists.
   `ui_bindings.md:60-61` forbids exactly that: *"No payload nests more than one level (a row may
   contain a small named group like `wealth`; it may not contain a list of rows that themselves
   contain lists)."* Publishing it properly means flattening to one row per (date × party), which for
   60 stored polls × 7 parties is 420 rows pushed on every monthly tick — for a sparkline the player
   sees only when one party's pane is open.
2. **The Parties tab needs one party's series, not the city's.** A map keyed by party id fetches
   exactly the ~24 points being drawn.

M6's city-wide multi-line chart is a different consumer with a different shape, and it should get the
reserved name when it is built.

### 13.2 New binding — `agora.parties.pollTrend`

`GetterMapBinding<string, List<PollTrendPointPayload>>`, keyed by party id. Registered in
`AgoraStateUISystem` alongside `detail` (§3.2), and — because the value is a `List<T>` — it **does**
need the explicit writer, unlike `detail`:

```csharp
        private GetterMapBinding<string, List<PollTrendPointPayload>> _pollTrend;

            AddBinding(_pollTrend = new GetterMapBinding<string, List<PollTrendPointPayload>>(
                PartiesGroup, "pollTrend", GetPollTrend,
                valueWriter: ListOf<PollTrendPointPayload>()));
```

`ListOf<T>` is `AgoraUISystemBase.ListOf` (`AgoraUISystemBase.cs:112-113`); omitting it throws
`MissingMethodException` on construction — the trap documented at `AgoraUISystemBase.cs:99-110`.
`_pollTrend.UpdateAll();` goes next to `_partyDetail.UpdateAll();` in `Publish()`.

### 13.3 Payload

```csharp
    /// <summary>
    /// One published poll's figure for one party. A flat row on purpose: a series of dates each
    /// carrying every party's shares would be a list of rows containing lists, which the payload
    /// budget (ui_bindings.md:60-61) forbids.
    /// </summary>
    public sealed class PollTrendPointPayload : IJsonWritable
    {
        public Agora.Core.Contracts.SimDate? Date;
        public double Share;
        public double MarginOfError;
        /// <summary>Weeks to the ballot this poll anticipated; -1 when none was scheduled.</summary>
        public int WeeksToElection = -1;

        public void Write(IJsonWriter writer)
        {
            writer.TypeBegin("agora.PollTrendPoint");
            UiJson.Date(writer, "date", Date);
            UiJson.Number(writer, "share", Share);
            UiJson.Number(writer, "marginOfError", MarginOfError);
            UiJson.Number(writer, "weeksToElection", WeeksToElection);
            writer.TypeEnd();
        }
    }
```

TS, in `declare namespace Agora` after `PartyDetail`:

```ts
  /**
   * `agora.parties.pollTrend` — a MAP binding keyed by `PartyBrief.id`. Oldest first, capped at
   * AGORA_POLL_TREND_MAX = 24. An unknown key returns []. Published polls only; the engine's own
   * PollResult.TrueShares never crosses the bridge (contract rule 8).
   */
  interface PollTrendPoint {
    date: SimDateString;
    /** [0,1] as published. */
    share: number;
    /** [0,1], e.g. 0.031 for ±3.1 points. */
    marginOfError: number;
    /** -1 when the poll anticipated no scheduled election. */
    weeksToElection: number;
  }
```

### 13.4 Projection

New method in `AgoraUiProjection.cs`, `agora.parties` section:

```csharp
        internal const int PollTrendMax = 24;

        internal static List<PollTrendPointPayload> BuildPollTrend(PoliticalState state, string partyId)
```

Source: `PoliticalState.RecentPolls` (`PoliticalState.cs:162-165` — *"Published polls, oldest first,
capped at `polling.maxStoredPolls`"*; `data/engine_tuning.json:196` sets that to **60**). Walk it
forward, skip `!poll.IsPublished` (`Elections.cs:88-89`), take the `Shares` entry for `partyId`
(`Elections.cs:58-59`) — **never `TrueShares`** (`Elections.cs:61-65`, contract rule 8) — and emit
`{ poll.Date, share, poll.MarginOfError, poll.ElectionDate.HasValue ? poll.WeeksToElection : -1 }`.
A poll with no entry for this party contributes **no point**, rather than a zero: a party that did not
exist yet did not poll at 0%.

Cap: keep the **newest** 24, i.e. `if (rows.Count > PollTrendMax) rows.RemoveRange(0, rows.Count - PollTrendMax);`
— note this removes from the *front*, unlike `BuildHistory` (`:290`), because this list is oldest-first
and a trend line reads left-to-right in time. The cap follows the `ElectionHistoryMax` precedent
(`AgoraUiProjection.cs:23`) and becomes contractual (`AGORA_POLL_TREND_MAX = 24`).

`-1` for "no election scheduled" matches the sentinel rule at `ui_bindings.md:342-343`.

### 13.5 UI

`Parties/PollSparkline.tsx` + `PollSparkline.module.scss`. `useMapValue(pollTrend$, props.partyId)`
inside `PartyDetail` — inside the remounted component (§2), never lifted.

**Flexbox sparkline, because Gameface has no SVG guarantee and no grid.** A row of `flex: 1 1 0`
columns, one per point, each an inner bar with `height: pct(share / scaleMax)` and
`align-self: flex-end`. `scaleMax` is the maximum share in the series rounded up to the next 5%, and
is **printed on the axis** — an unlabelled auto-scaled sparkline makes a 2% party look like a 40%
one. The election-day column (`weeksToElection === 0`) is tinted `$accent`. Fill colour is
`hexToRgba(detail.colorHex, …)`.

Fewer than two points: render *"Not enough published polls yet"*, not an empty box. Exactly the
state a new save is in for its first campaign.

Placement: directly beneath the Standing stat row (§5.2 item 4), so the delta figure and the shape it
came from are read together.

### 13.6 Contract

- §4.2 table gains a row: `agora.parties.pollTrend` | `GetterMapBinding<string,T>` |
  `List<PollTrendPoint>` per key | `Agora.PollTrendPoint[]` | on demand, per subscribed key | `[]` | W6.
- Sort key: *"`pollTrend`: `date` ascending (oldest first) — a trend reads left to right in time. This
  is the one list in the contract that is **not** newest-first."* That exception must be written down
  or someone will "fix" it.
- §2 payload budget: add `AGORA_POLL_TREND_MAX = 24` to the cap list at `:63-64`.
- §5: `PollTrendPoint  date, share, marginOfError, weeksToElection`.
- §8: annotate the `agora.seats.pollTrend` row — *"still reserved for M6's city-wide multi-party
  chart. W6's per-party sparkline uses `agora.parties.pollTrend` instead; a city-wide series of
  per-party shares would be a list-of-lists, which §2 forbids."*
- `:3` schemaVersion +1 (once for all of Part II if landed together).

---

## 14. Chunk F — party history strip (addition 5)

### 14.1 What the engine actually retains — verified

`fixplan.md:332-333` asks for *"founded, split from, merged into, revivals, seats per election"*.
All five are persisted and reachable from the projection with **no schema change**:

| Wanted | Field | Persisted? |
|---|---|---|
| founded | `Party.FoundedDate` | yes — `src/Agora.Core/Contracts/Parties.cs:104` |
| dissolved | `Party.DissolvedDate` | yes — `Parties.cs:106-107` |
| split from | `Party.PredecessorPartyId` — *"Party this one split from, if any."* | yes — `Parties.cs:127-128` |
| merged into | `Party.SuccessorPartyId` — *"Party this one merged into, if `Status` is Merged."* | yes — `Parties.cs:130-131` |
| revivals | `Party.RevivalCount` — *"Number of times this brand has revived."* | yes — `Parties.cs:142-143` |
| seats per election | `ElectionResult.Seats` (`List<SeatAllocation>`, `Elections.cs:212-213`) + `ElectionResult.Date`/`TermNumber` (`:193`, `:198`) across `PoliticalState.ElectionHistory` (*"Completed elections, oldest first. Append-only history."*, `PoliticalState.cs:167-168`) | yes |

Two caveats worth writing down rather than discovering in play:

- **`PredecessorPartyId` and `SuccessorPartyId` are nullable `string?`.** The wire rule is `""` for an
  absent id, never null (`ui_bindings.md:47`), so the projection coalesces.
- **A predecessor/successor id may point at a party that is itself dissolved** — which is fine,
  because dissolved brands stay in the roster (§0.2), so the panel can always resolve the label. The
  panel resolves it through `roster$`, never from the detail payload.

### 14.2 Payload — `PartyDetailPayload` gains four fields, plus one list

Four scalars, appended after `DissolvedDate`:

```csharp
        public string PredecessorPartyId = "";
        public string SuccessorPartyId = "";
        public int RevivalCount;
        /// <summary>Party ids this one absorbed, ascending. Derived: every party whose
        /// SuccessorPartyId is this party. Empty for a brand that has absorbed nobody.</summary>
        public List<string> AbsorbedPartyIds = new List<string>();
```

…and in `Write()`:

```csharp
            UiJson.Id(writer, "predecessorPartyId", PredecessorPartyId);
            UiJson.Id(writer, "successorPartyId", SuccessorPartyId);
            UiJson.Number(writer, "revivalCount", RevivalCount);
            UiJson.Ids(writer, "absorbedPartyIds", AbsorbedPartyIds);
```

`AbsorbedPartyIds` is the reverse index of `SuccessorPartyId`: a scan of `state.Parties` in the
projection, `SortedCopy`'d. It is the half of the merge story the forward pointer cannot tell —
without it, a party that absorbed three rivals shows nothing at all.

**The seats-per-election series is a separate list**, because it is a list of rows and the payload
budget forbids nesting one inside `PartyDetail`:

```csharp
    /// <summary>One party's result at one past election. A flat row: seats-per-election cannot nest
    /// inside PartyDetail without breaking the one-level rule (ui_bindings.md:60-61).</summary>
    public sealed class PartyElectionRowPayload : IJsonWritable
    {
        public string ElectionId = "";
        public Agora.Core.Contracts.SimDate? Date;
        public int TermNumber;
        public bool IsSnapElection;
        public int Seats;
        public double SeatShare;
        public double VoteShare;
        public bool PassedThreshold;
        public bool WasOnBallot;

        public void Write(IJsonWriter writer)
        {
            writer.TypeBegin("agora.PartyElectionRow");
            UiJson.Id(writer, "electionId", ElectionId);
            UiJson.Date(writer, "date", Date);
            UiJson.Number(writer, "termNumber", TermNumber);
            UiJson.Flag(writer, "isSnapElection", IsSnapElection);
            UiJson.Number(writer, "seats", Seats);
            UiJson.Number(writer, "seatShare", SeatShare);
            UiJson.Number(writer, "voteShare", VoteShare);
            UiJson.Flag(writer, "passedThreshold", PassedThreshold);
            UiJson.Flag(writer, "wasOnBallot", WasOnBallot);
            writer.TypeEnd();
        }
    }
```

Published as a second map binding, `agora.parties.electionRecord`, keyed by party id — same
registration shape as §13.2 (it is a `List<T>`, so it needs `valueWriter: ListOf<PartyElectionRowPayload>()`).

`WasOnBallot` distinguishes *"stood and won nothing"* from *"did not stand"*: taken from
`ElectionResult.PartyIdsOnBallot` (`Elections.cs:203-204`). A party absent from the ballot contributes
**no row**; `wasOnBallot` false with a row present means it was on the ballot and took no seats. Both
states exist and rendering them the same way would be a lie.

### 14.3 Projection

```csharp
        internal static List<PartyElectionRowPayload> BuildPartyElectionRecord(
            PoliticalState state, string partyId)
```

Walk `state.ElectionHistory` (oldest first, `PoliticalState.cs:167-168`); for each, emit a row when
`PartyIdsOnBallot` contains `partyId`, filling seats/shares from the matching `SeatAllocation`
(`Elections.cs:212-213`) or zeroes when the party stood and took none. Cap at
`ElectionHistoryMax = 12` (`AgoraUiProjection.cs:23`) keeping the **newest** twelve, then render
oldest-first — same reasoning as §13.4.

### 14.4 UI

`Parties/HistoryStrip.tsx`. Two parts, both flex:

1. **Lineage line** — one sentence assembled from the four scalars, with party labels resolved
   through `roster$`: *"Founded March 1994. Split from the Civic Union. Revived twice. Absorbed the
   Greens and the Free List."* Each clause is omitted when its field is empty. `revivalCount` renders
   as "once"/"twice"/"N times", never "1".
2. **Seat bars** — a row of `flex: 1 1 0` columns, one per `PartyElectionRow`, bar height
   `seats / maxSeatsInSeries`, labelled with the election year beneath. Snap elections
   (`isSnapElection`) get a distinguishing tick; rows with `!passedThreshold` render in
   `$text-faint`. Empty series: *"No elections held yet."*

Placement: the Lifecycle section (§5.2 item 7) expands into this; the founded/dissolved lines it
already carried move into the lineage sentence rather than being printed twice.

### 14.5 Contract

- §4.2 table: `agora.parties.electionRecord` | `GetterMapBinding<string,T>` |
  `List<PartyElectionRow>` per key | `Agora.PartyElectionRow[]` | on demand, per subscribed key | `[]` | W6.
- Sort key: *"`electionRecord`: `date` ascending (oldest first), capped at
  `AGORA_ELECTION_HISTORY_MAX = 12`, keeping the newest twelve."*
- §5: append the four new `PartyDetail` fields to its shape entry, and add `PartyElectionRow`.
- §6: update the `EMPTY_PARTY_DETAIL` literal with `predecessorPartyId: ""`, `successorPartyId: ""`,
  `revivalCount: 0`, `absorbedPartyIds: []`.
- `:3` schemaVersion +1 (once for all of Part II if landed together).

---

## 15. Chunk G — mandate scorecard (addition 6), and the overlap with `MandateTracker`

### 15.1 The overlap is real — read it before building anything

`ui/src/panels/News/MandateTracker.tsx` exists and was touched this session (the backlog item
*"`MandateTracker.tsx` — progress bar carries no inline percentage"*, `fixplan.md:365`, is **done**:
the percentage now sits on the bar's own row, `MandateTracker.tsx:113-133`). It already renders, per
mandate: status chip, issue, party chip, district chip, held chip, flavor text, metric name,
direction, a progress bar with inline percentage, baseline/current/target, deadline with overdue and
held handling, and a voter-interest meter (`:90-174`). It consumes `agora.news.mandates`
(`ui/src/panels/News/bindings.ts:67`), a pushed `ValueBinding<List<MandateRow>>`.

**A per-party mandate list in the Parties tab would be a second, worse copy of that.** Do not build
one.

### 15.2 Recommendation: division of labour, not component reuse

**Not reuse.** `MandateTracker` imports `./NewsPanel.module.scss` (`:1`) and `./lookup`'s `Lookups`
(`:2`), so sharing it means either moving the component plus its stylesheet into a shared module —
which the codebase deliberately does not have (`ui_bindings.md:298-300`: *"Each panel declares the
constants it needs module-locally — there is no shared runtime module, by design"*) — or importing
one panel's stylesheet into another, which is worse. Both are more churn than this addition is worth.

**Division of labour:**

| | Shows | Grain |
|---|---|---|
| **News → `MandateTracker`** (unchanged) | every mandate, every party, full metric detail | one row per mandate |
| **Parties → `MandateScorecard`** (new) | one party's *record*: how many it kept | one row per **status** |

The scorecard answers a question the tracker cannot: *"does this party deliver?"* — six counts and a
delivery rate, not a list. There is no duplicated row anywhere, and no player ever sees the same
component twice.

### 15.3 Payload

**None. No new binding, no new field, and nothing changes in C#.** The panel binds the already-published
`agora.news.mandates`:

```ts
export const mandates$ = bindValue<Agora.MandateRow[]>("agora.news", "mandates", []);
```

`MandateRow.partyId` is already published (`AgoraUiPayloads.cs:733-779`, projected at
`AgoraUiProjection.cs:699-721`), so filtering by the open party's id is a `.filter` on data the UI
already holds. This satisfies contract rule 3 — the binding is published, not reserved.

One caveat to record: `agora.news.mandates` is a **pushed** binding carrying *every* mandate, so the
Parties tab pays its bridge cost whenever it is open. That is already the News panel's cost and it is
not per-party, so it does not grow with the roster. Acceptable. Do **not** invent
`agora.parties.mandates` to avoid it — that would publish the same rows twice.

### 15.4 UI

`Parties/MandateScorecard.tsx`. Filter `mandates$` to `m.partyId === props.partyId`, then:

- **Six count chips**, in the tracker's own status-rank order so the two views agree
  (`AgoraUiProjection.StatusRank`, `:739-750`): Active, Pending, Partly met, Fulfilled, Defied,
  Abandoned. Reuse the label map from `MandateTracker.tsx:34-41` by copying it into the Parties
  panel's `format.ts` — six strings, and the codebase's convention is module-local constants.
- **Delivery rate** — `fulfilled / resolved`, where `resolved = fulfilled + partlyMet + defied`.
  Rendered as a single bar with an inline percentage, matching the tracker's own corrected pattern
  (`MandateTracker.tsx:113-133`). **Mandates that are `Active` or `Pending` are excluded from the
  denominator** — a promise not yet due is not a promise broken. When `resolved === 0` the bar is
  replaced by *"Nothing judged yet"*.
- **A held count**, when any of the party's mandates has `isMeasurementStalled` — *"N held: the metric
  is currently unreadable."* `isMeasurementStalled` is a rendering obligation
  (`AgoraUiPayloads.cs:728-732`) and silently folding a held mandate into a failure would break it.
- **One line pointing at News** — *"Full detail in the News tab's mandate tracker."* This is what
  makes the division of labour legible to the player rather than looking like missing information.

Placement: a new section after Lifecycle/History. Renders nothing at all — not an empty box — when the
party has never held a mandate.

### 15.5 Contract

One edit: `docs/contracts/ui_bindings.md` §4.5 (`agora.news`), annotate the `mandates` row —
*"Also consumed by the Parties tab, filtered by `partyId`, as a per-party scorecard (fixplan W6
addition 6). No per-party binding exists or should be added: it would publish the same rows twice."*

No version bump on its own account — no payload changed.

---

## 16. Chunk H — coalition relations (addition 4)

**This is the expensive one, and the owner chose it partly on a false premise.** `fixplan.md:322`
says *"Everything here is already computed by the engine; the cost is payload and layout, not
simulation."* For this item that is not true, and the owner should see the real cost before the
coder starts.

### 16.1 What is actually there — corrected

My earlier survey said `CoalitionMath` is `internal static` and therefore unreachable. That is true
(`src/Agora.Core/Engine/Government/Coalitions/CoalitionMath.cs:37`) **but it is not the binding
constraint**, and the correction matters because it makes a much cheaper design available:

- **`CoalitionCandidate` is `public`** — `CoalitionCandidate.cs:12` — and its own doc comment at
  `:6-11` says it is *"useful to the dashboard ('who was talking to whom')"*. It carries exactly what
  this addition wants: `MemberPartyIds`, `LeadPartyId`, `Seats`, `SeatShare`, `HasMajority`,
  `MeanPairwiseDistance`, `MaxPairwiseDistance`, `DistanceCap`, `Cohesion`, `Score`,
  `IsMinimumWinning`, `IsGrandCoalition` (`:42-80`).
- **`CoalitionFormation` is `public`** — `CoalitionFormation.cs:74` — and `CoalitionFormationResult.RankedCandidates`
  is a public `IReadOnlyList<CoalitionCandidate>` (`:52`).
- **But `RankedCandidates` is never persisted.** It lives on the formation *result*, which is consumed
  on election night and discarded. Nothing on `Coalition` (`Government.cs:50-105`) or
  `PoliticalState` (`PoliticalState.cs:128-211`) retains it. Confirmed by reading both types in full.
- **And the candidate builder is `private`** — `CoalitionFormation.BuildCandidates` (`:251`),
  `Enumerate` (`:267`), `Evaluate` (`:285`). The only public entry point is `Form` (`:94`), which
  **draws from the RNG**: `SeedStreams.RngFor(saveGuid, date, StreamNames.CoalitionFormation, …)` at
  `:162-165`. Calling `Form` from the projection to get a ranking would re-run the negotiation. Even
  though the draw is seeded and therefore reproducible, running formation from a UI publisher is a
  **contract rule 5 violation** (`ui_bindings.md:353-355`: *"Bindings are a view, never a channel for
  engine state. The UI reads politics; it does not compute or mutate it."*) and is not on the table.

### 16.2 Two designs, with real costs. **Recommendation: design B.**

#### Design A — persist the ranking (what `fixplan.md` implies)

Add a trimmed candidate list to `Coalition`, written at formation time from
`CoalitionFormationResult.RankedCandidates`.

- **Cost:** a new contract type and a new list property on `Coalition` → `/schema-change`: contract,
  JSON schema (`data/schemas/political_state.schema.json` `$defs/coalition`, which is
  `additionalProperties: false`), a migration step, sidecar version bumps, and tests. Plan 0001's
  §3–§6 is the shape of that work.
- **What it buys:** the *historical* fact — who was actually talking to whom on election night,
  including the arrangements that were tried and walked out (`Form` at `:158-171` iterates candidates
  and skips the ones whose talks failed). No other design can recover that; the draw that rejected a
  candidate is not re-derivable from state alone without re-running the same seeded stream.
- **What it does not buy:** a *current* answer. The persisted ranking is frozen at the last formation
  and goes stale as platforms drift, so between elections it answers "who could have governed then",
  not "who could govern now".
- **Grows the save.** Up to 98 candidates per government under EU tuning (§16.4), on an append-only
  `CoalitionHistory` (`PoliticalState.cs:173-174`). Would need its own cap, which is another
  contract decision.

#### Design B — expose a pure ranking from `Agora.Core` (recommended)

Add one **public, RNG-free, side-effect-free** method to `CoalitionFormation` that runs the existing
candidate enumeration and ranking *without* the negotiation draw, and have the projection call it.

- **Cost:** ~40 lines in `Agora.Core` (mostly a refactor that extracts a block already inside `Form`),
  one new public payload type, one map binding, and tests in the existing
  `tests/Agora.Core.Tests/CoalitionsTests.cs`. **No schema change. No migration. No sidecar version
  bump. No save growth.**
- **What it buys:** a *live* answer — "who could govern today, given where every party stands now" —
  which recomputes as platforms drift and is arguably the more interesting readout for a Parties tab.
- **What it does not buy:** the historical record of failed talks (design A's unique value).
- **Determinism:** unaffected. The method touches no RNG and writes no state; it is a pure function of
  (last election's seat allocation, current party platforms, tuning). Nothing enters engine state, so
  non-negotiables #2 and #3 are not in play.
- **Boundary rules:** **no breach.** The method lives in `Agora.Core` and references only
  `Agora.Core.Contracts` and `Agora.Core.Tuning` — no `Game.*`, `Colossal.*` or `Unity.*`
  (`src/Agora.Core/CLAUDE.md`, first rule). `Agora.Mod` calling a public `Agora.Core` API is the
  normal direction of flow (`src/CLAUDE.md`: *"Data flows into Core as plain structs and out of Core
  as plain structs"*). And it satisfies contract rule 5 precisely: **the engine computes it, the UI
  reads it.** Making `CoalitionMath` itself public is *not* required and should not be done — its
  `internal` doc comment (`CoalitionMath.cs:32-36`: *"Internal on purpose: the packet's public surface
  is `CoalitionFormation` and `CoalitionStability`, and nothing else"*) is a deliberate boundary, and
  design B respects it by adding to the sanctioned public surface instead.

**Recommendation: B.** It gets the readout the tab wants, it is roughly a fifth of the work, it adds
no migration risk to a sidecar that plan 0001 is already migrating, and it respects the packet's
stated public surface. Design A's unique value — the record of failed talks — is a good idea for a
future "election night" feature (M6's broadcast mode, `docs/status.md`), not for this tab.

**If the owner takes A anyway, it is a hard dependency on plan 0001 — see §16.3.**

### 16.3 If the owner chooses design A: what plan 0001 must additionally carry

Routed into the **batched** pass, not a separate migration, per `fixplan.md:389-392`. 0001 would gain:

1. **`src/Agora.Core/Contracts/Government.cs`** — a new `CoalitionOption` type (member ids, lead id,
   seats, seat share, `hasMajority`, `meanDistance`, `maxDistance`, `distanceCap`, `cohesion`,
   `score`, `isMinimumWinning`, `wasAttempted`, `talksFailed`), and
   `Coalition.RankedOptions : List<CoalitionOption>` **capped at 12**, sorted by
   `CoalitionCandidate.Compare` order (`CoalitionCandidate.cs:92-104`).
2. **The writer** — `CoalitionFormation.Build` (`CoalitionFormation.cs`, the `Build` helper) fills it
   from `RankedCandidates`; `wasAttempted` / `talksFailed` come from the attempt loop at `:158-171`.
3. **`data/schemas/political_state.schema.json`** — a `$defs/coalitionOption` block and a
   `rankedOptions` property on `$defs/coalition`, which declares `additionalProperties: false` — so
   this is not optional. **Not** added to `required`.
4. **The State 1→2 migration step** — `rankedOptions` absent ⇒ `[]` for every coalition in `coalitions`
   *and* in `coalitionHistory`. Slots into `MigrateStateV1ToV2` (0001 §3.3) as three more lines.
5. **A test** — 0001 §6.1 gains: *"`Migrate_StateV1_AddsEmptyRankedOptionsToEveryCoalition`"*, and its
   deep-compare test 5 gains `coalitions[*].rankedOptions` to its known-changed path list.
6. **`PartyRegistry.Clone` is not affected** (it clones `Party`, not `Coalition`), but check whether
   any coalition clone path exists before assuming.

**This is a real addition to a pass that is already large**, and it is the reason the recommendation
is B. Master should decide before 0001's coder starts, because retrofitting it after 0001 lands means
a second sidecar migration — exactly what `fixplan.md:389-392` says to avoid.

### 16.4 Design B in detail

#### 16.4.1 `Agora.Core` — the new public surface

In `src/Agora.Core/Engine/Government/Coalitions/CoalitionFormation.cs`:

```csharp
        /// <summary>
        /// Every viable arrangement the current chamber could form, in formation order, WITHOUT
        /// running the negotiation draw.
        /// </summary>
        /// <remarks>
        /// A read-only view for the dashboard (fixplan W6): it answers "who could govern, given where
        /// the parties stand today", which drifts as platforms drift. <see cref="Form"/> answers a
        /// different question — who actually did — and is the only one that touches the RNG.
        /// <para>
        /// Pure: no seed, no draw, no state written. Callable from a UI publisher without violating
        /// docs/contracts/ui_bindings.md rule 5, because the computation happens here in the engine
        /// and the caller only copies the result.
        /// </para>
        /// </remarks>
        public static IReadOnlyList<CoalitionCandidate> RankCandidates(
            ElectoralSystem system,
            IReadOnlyList<SeatAllocation> seats,
            IReadOnlyList<Party> parties,
            EngineTuning tuning)
```

Behaviour: under `FirstPastThePost`, return an empty list — FPTP forms no coalition
(`CoalitionFormation.cs:129-130` routes to `FormSinglePartyGovernment`), and an empty list is the
honest answer rather than a fabricated one. Otherwise: `CoalitionMath.BuildPool` (`:139`), then the
**existing** build/slack-retry/mark/sort block currently inline in `Form` at `:143-157`.

**Extract that block into a private helper and have both call it.** One implementation, never two —
if the ranking in the dashboard could diverge from the ranking formation actually used, the readout
is worse than not having it:

```csharp
        private static List<CoalitionCandidate> RankOf(
            List<PartySeat> pool, int totalSeats, CoalitionsTuning t, out bool usedSlack)
```

`Form` then reads `candidates = RankOf(pool, totalSeats, t, out usedSlack);` and keeps its own
`MajorityOf` / attempt loop unchanged. This refactor is behaviour-preserving and
`CoalitionsTests.cs` already asserts against `RankedCandidates` (`CoalitionsTests.cs:101-103`), so an
accidental change shows up immediately.

**Cost is bounded**, which matters because this runs on a UI publish: `FormationMaxPartners = 4`
(`src/Agora.Core/Tuning/EngineTuning.cs:686`, `data/engine_tuning.json:237`) and the EU theme runs
4–7 parties, so enumeration tops out at C(7,1)+C(7,2)+C(7,3)+C(7,4) = **98** candidate evaluations,
each a handful of `IssuePosition.Distance` calls. It already runs this on every election night. It is
still cheap enough only because it is behind a **map binding fetched for one open pane** — it must
never be moved into a `GetterValueBinding`, which would re-run it every UI tick (contract rule 6,
`ui_bindings.md:358-361`).

#### 16.4.2 Payload

```csharp
    /// <summary>
    /// One arrangement the chamber could form, from this party's point of view. A live view, not
    /// history: it is recomputed from current platforms, so it drifts between elections.
    /// </summary>
    public sealed class CoalitionOptionPayload : IJsonWritable
    {
        public List<string> MemberPartyIds = new List<string>();
        public string LeadPartyId = "";
        public int Seats;
        public double SeatShare;
        public bool HasMajority;
        /// <summary>Mean platform distance across member pairs, [0,1]. Lower is closer.</summary>
        public double MeanDistance;
        /// <summary>Widest gap between any two members, [0,1] — the figure judged against the cap.</summary>
        public double MaxDistance;
        /// <summary>The cap this set was judged against, including any grand-coalition slack.</summary>
        public double DistanceCap;
        /// <summary>Cohesion this arrangement would have, [0,1]. Also the odds talks succeed.</summary>
        public double Cohesion;
        /// <summary>Ranking score, [0,1].</summary>
        public double Score;
        public bool IsMinimumWinning;
        public bool IsGrandCoalition;
        /// <summary>True when this is the arrangement currently governing.</summary>
        public bool IsCurrentGovernment;

        public void Write(IJsonWriter writer)
        {
            writer.TypeBegin("agora.CoalitionOption");
            UiJson.Ids(writer, "memberPartyIds", MemberPartyIds);
            UiJson.Id(writer, "leadPartyId", LeadPartyId);
            UiJson.Number(writer, "seats", Seats);
            UiJson.Number(writer, "seatShare", SeatShare);
            UiJson.Flag(writer, "hasMajority", HasMajority);
            UiJson.Number(writer, "meanDistance", MeanDistance);
            UiJson.Number(writer, "maxDistance", MaxDistance);
            UiJson.Number(writer, "distanceCap", DistanceCap);
            UiJson.Number(writer, "cohesion", Cohesion);
            UiJson.Number(writer, "score", Score);
            UiJson.Flag(writer, "isMinimumWinning", IsMinimumWinning);
            UiJson.Flag(writer, "isGrandCoalition", IsGrandCoalition);
            UiJson.Flag(writer, "isCurrentGovernment", IsCurrentGovernment);
            writer.TypeEnd();
        }
    }
```

Note what is **not** on it: no per-partner "refusal" flag. A candidate that never appears in the
ranking was rejected by `Evaluate` (`CoalitionFormation.cs:285-300`) for one of two reasons — the lead
is too small (`leadShare < t.LeadPartyMinSeatShare`) or the members are too far apart
(`maxDistance > cap`) — and the pure ranking cannot say which without re-running the evaluation for
rejected sets. The panel expresses refusal as **absence plus one derived pair figure** (§16.4.4)
rather than inventing a reason.

TS:

```ts
  /**
   * `agora.parties.relations` — a MAP binding keyed by `PartyBrief.id`. Every viable arrangement
   * containing this party, best first, capped at AGORA_COALITION_OPTIONS_MAX = 8. Unknown key or
   * FPTP returns []. A LIVE view computed from current platforms — not the historical record of
   * who actually negotiated.
   */
  interface CoalitionOption {
    /** Ascending. Always contains the party this was fetched for. */
    memberPartyIds: IdString[];
    leadPartyId: IdString;
    seats: number;
    /** [0,1]. */
    seatShare: number;
    hasMajority: boolean;
    /** [0,1]. Lower is closer. */
    meanDistance: number;
    /** [0,1]. Judged against `distanceCap`. */
    maxDistance: number;
    /** [0,1]. */
    distanceCap: number;
    /** [0,1]. Also the odds talks succeed. */
    cohesion: number;
    /** [0,1]. */
    score: number;
    isMinimumWinning: boolean;
    isGrandCoalition: boolean;
    isCurrentGovernment: boolean;
  }
```

#### 16.4.3 Projection and binding

```csharp
        internal const int CoalitionOptionsMax = 8;

        internal static List<CoalitionOptionPayload> BuildPartyRelations(
            PoliticalState state, EngineTuning tuning, string partyId)
```

Steps: return empty when `state == null`, `partyId` empty, `state.ElectionHistory.Count == 0`, or
`state.Settings.System == ElectoralSystem.FirstPastThePost`. Otherwise call
`CoalitionFormation.RankCandidates(state.Settings.System, latest.Seats, state.Parties, tuning)`,
keep only candidates whose `MemberPartyIds` contains `partyId`, copy the first
`CoalitionOptionsMax`, and set `IsCurrentGovernment` by comparing the sorted member id list against
`state.Government.MemberPartyIds` (`Government.cs:62-63`, already sorted ascending).

`tuning` is threaded in as an argument rather than read inside the projection, matching
`GetDetail`'s shape in `AgoraDistrictsUISystem.cs:56-57`. The publisher supplies it:

```csharp
        private static List<CoalitionOptionPayload> GetPartyRelations(string partyId) =>
            AgoraUiProjection.BuildPartyRelations(AgoraRuntime.State, AgoraRuntime.Tuning, partyId);
```

`AgoraRuntime.Tuning` is a public static property that never returns null — it falls back to
`EngineTuning.Default` (`src/Agora.Mod/Core/AgoraRuntime.cs:113-116`).

Binding registration mirrors §13.2 (a `List<T>`, so `valueWriter: ListOf<CoalitionOptionPayload>()`),
plus `_relations.UpdateAll();` in `Publish()`.

#### 16.4.4 UI

`Parties/CoalitionRelations.tsx`, a new section after the scorecard. Two blocks:

1. **Possible governments** — up to 8 rows, best first. Each row: the member swatches and short names
   (resolved through `roster$`), seats with `pct(seatShare)`, a majority chip, a `Cohesion` meter, and
   a `Currently governing` chip when `isCurrentGovernment`. `isMinimumWinning` false gets a quiet
   "more partners than it needs" note — that is the concept at `CoalitionCandidate.cs:69-74` and it is
   otherwise invisible.
2. **Who it can work with** — a per-party line derived from the options list: for each other party in
   the roster, whether any listed option contains both. A party sharing no option is shown under
   *"No workable arrangement"* — which is the honest expression of "refusal": the engine did not
   reject them by name, it simply produced no viable set containing both. **Do not label this
   "refuses to govern with"**; nothing in the engine models a refusal, and inventing one puts a
   fabricated political fact on screen.

Empty list: *"No coalition arithmetic yet — the city has not held an election"*, or, under FPTP,
*"Under first past the post the winning party governs alone."* Branch on `summary.system`, never on
the list being empty (contract §4.3's rule about branching on the system rather than sniffing
zeroes).

#### 16.4.5 Contract

- §4.2 table: `agora.parties.relations` | `GetterMapBinding<string,T>` | `List<CoalitionOption>` per
  key | `Agora.CoalitionOption[]` | on demand, per subscribed key | `[]` | W6.
- Sort key: *"`relations`: formation order — `hasMajority` first, then `isMinimumWinning`, then
  `score` descending, then fewer partners, then the joined member-id key
  (`CoalitionCandidate.Compare`, `CoalitionCandidate.cs:92-104`). Capped at
  `AGORA_COALITION_OPTIONS_MAX = 8`. `memberPartyIds` ordinal ascending."*
- §2 payload budget: add the cap.
- §5: `CoalitionOption  memberPartyIds, leadPartyId, seats, seatShare, hasMajority, meanDistance,
  maxDistance, distanceCap, cohesion, score, isMinimumWinning, isGrandCoalition, isCurrentGovernment`.
- A note under §4.2: *"`relations` is a **live** view recomputed from current platforms via
  `CoalitionFormation.RankCandidates`, not the historical record of who negotiated after the last
  election. It answers 'who could govern now'. Empty under FPTP by design."*
- `:3` schemaVersion +1 (once for all of Part II if landed together).

### 16.5 Tests — the one part of this plan that is headlessly testable

`Agora.Core` is game-free, so `RankCandidates` gets real tests in the existing
`tests/Agora.Core.Tests/CoalitionsTests.cs`:

| # | Test | Asserts |
|---|---|---|
| H1 | `RankCandidates_MatchesTheRankingFormationUsed` | For a fixed chamber, `RankCandidates(...)` is element-for-element equal (by `Key`) to `CoalitionFormation.Form(...).RankedCandidates`. **The anti-divergence guard** — the whole reason §16.4.1 extracts one shared helper. |
| H2 | `RankCandidates_DrawsNoRandomness` | Called twice with different `saveGuid`/date context (i.e. via a `Form` run seeded differently), the ranking is identical. Proves the RNG is out of the path. |
| H3 | `RankCandidates_ReturnsEmptyUnderFirstPastThePost` | FPTP ⇒ `Count == 0`. |
| H4 | `RankCandidates_ExcludesDissolvedAndMergedBrands` | A `Dissolved` seat-holder appears in no candidate — the rule at `CoalitionMath.cs:92-93`, now reachable through the new surface. |
| H5 | `RankCandidates_HonoursFormationMaxPartners` | No candidate has more members than `coalitions.formationMaxPartners` (`EngineTuning.cs:686`). |
| H6 | `RankCandidates_IsStableAcrossCallerListOrder` | Shuffling the input `seats` and `parties` lists yields a byte-identical ranking. The determinism rule the packet's own doc comment states at `CoalitionMath.cs:11-15`. |

No test is possible for `BuildPartyRelations` itself (§7), so **H1 is what makes the projection
trustworthy**: it fixes the engine half, and the projection half is a copy loop.

---

## 17. Ordered checklist — Part II

Each chunk is independently landable. **Do D, E, F, G before H** — H is the only one that touches
`Agora.Core` and the only one with an owner decision still open.

### Before starting Part II

- [x] **H0.** ~~Owner decision required: design A or design B for coalition relations (§16.2).~~
      **DECIDED 2026-08-08 by the owner: design B** — the pure RNG-free `RankCandidates` extracted
      into `Agora.Core`'s sanctioned public surface. No schema change, no migration, no save growth;
      `CoalitionMath` stays `internal`. The tab shows a **live** ranking that drifts with platforms,
      and the historical record of failed talks (design A's unique value) is **not** built.
      **Consequence: plan 0001 does NOT carry the §16.3 items.** 0001 is unchanged by this decision.
      §16.3 and design A are retained below as a record of the option not taken — do not implement
      them.

      Also decided in the same pass, per §§14–15 and §17:
      - **Poll trend** — publish a party-scoped `agora.parties.pollTrend`; leave the reserved
        `agora.seats.pollTrend` (`ui_bindings.md:380`) alone for M6's city-wide chart.
      - **Mandate scorecard** — division of labour, not a second list. News keeps one row per
        mandate; Parties gets one row per *status* plus a delivery rate, filtering the
        already-published `agora.news.mandates`. No new binding, no C#.
      - **Bloc support breakdown** — declined; not in scope for W6.

### Chunk D — manifesto vs. platform (§12) — no C#, no contract version bump

- [ ] **D1.** `PlatformBars.tsx`: add the optional `marker` / `markerLabel` props and the centre-zero
      tick (§12.2). No absolute positioning.
- [ ] **D2.** `PartyDetail.tsx`: pass `marker` only when `hasContestedElection`; add the drift summary
      line and the `MANIFESTO_DRIFT_THRESHOLD` constant in `format.ts` (§12.3). Section heading is
      **"Manifesto and drift"**, never "betrayal".
- [ ] **D3.** `ui_bindings.md` §4.2 annotation (§12.4). `cd ui && npm run build`.

### Chunk E — poll trend sparkline (§13)

- [ ] **E1.** `AgoraUiPayloads.cs`: `PollTrendPointPayload` (§13.3).
- [ ] **E2.** `AgoraUiProjection.BuildPollTrend` + `PollTrendMax = 24` (§13.4). **Read `Shares`, never
      `TrueShares`.** Trim from the front, not the back.
- [ ] **E3.** `AgoraStateUISystem.cs`: register the map **with `valueWriter: ListOf<…>()`** and add
      `_pollTrend.UpdateAll()` (§13.2). Omitting the writer throws on construction.
- [ ] **E4.** `ui/types/bindings.d.ts`: `PollTrendPoint`.
- [ ] **E5.** `ui_bindings.md`: §4.2 row, the oldest-first sort-key exception, the §2 cap, the §5 shape,
      the §8 annotation explaining why `agora.seats.pollTrend` stays reserved (§13.6).
- [ ] **E6.** `Parties/PollSparkline.tsx` + scss; wire into `PartyDetail` beneath the Standing row.
      Print the scale maximum on the axis.

### Chunk F — party history strip (§14)

- [ ] **F1.** `AgoraUiPayloads.cs`: four new `PartyDetailPayload` fields + writes; new
      `PartyElectionRowPayload` (§14.2).
- [ ] **F2.** `AgoraUiProjection`: fill the four fields, build `AbsorbedPartyIds` as the reverse index
      of `SuccessorPartyId`, and add `BuildPartyElectionRecord` (§14.3).
- [ ] **F3.** `AgoraStateUISystem.cs`: register `agora.parties.electionRecord` with its list writer;
      `UpdateAll()`.
- [ ] **F4.** `ui/types/bindings.d.ts`: the four `PartyDetail` fields and `PartyElectionRow`.
- [ ] **F5.** `ui_bindings.md`: §4.2 row + sort key, §5 shapes, **and update the `EMPTY_PARTY_DETAIL`
      literal in §6** — a missed field there is a silent runtime hole.
- [ ] **F6.** `Parties/HistoryStrip.tsx` + scss; fold the existing founded/dissolved lines into the
      lineage sentence rather than printing them twice.

### Chunk G — mandate scorecard (§15) — no C# at all

- [ ] **G1.** Read `ui/src/panels/News/MandateTracker.tsx` first (§15.1). If what you are about to
      build is a list of mandate rows, stop — that is the tracker, and this is a scorecard.
- [ ] **G2.** `Parties/bindings.ts`: add `mandates$ = bindValue<Agora.MandateRow[]>("agora.news", "mandates", [])`.
      No new binding is registered anywhere in C#.
- [ ] **G3.** `Parties/MandateScorecard.tsx` + scss (§15.4). Delivery rate excludes `Active` and
      `Pending` from the denominator; held mandates get their own line.
- [ ] **G4.** `ui_bindings.md` §4.5 annotation on the `mandates` row (§15.5).

### Chunk H — coalition relations (§16) — **only after H0**

- [ ] **H1.** `CoalitionFormation.cs`: extract the build/slack/mark/sort block from `Form` (`:143-157`)
      into a private `RankOf` helper, and have `Form` call it. Behaviour-preserving.
      `dotnet test tests\Agora.Core.Tests\Agora.Core.Tests.csproj` must stay green **before** anything
      new is added — that is the proof the refactor changed nothing.
- [ ] **H2.** Add the public `CoalitionFormation.RankCandidates` (§16.4.1). Do **not** make
      `CoalitionMath` public.
- [ ] **H3.** Write tests H1–H6 in `tests/Agora.Core.Tests/CoalitionsTests.cs` (§16.5). Gate: green.
      *Test H1 is the one that matters — it is what stops the dashboard's ranking drifting from the
      engine's.*
- [ ] **H4.** `AgoraUiPayloads.cs`: `CoalitionOptionPayload` (§16.4.2).
- [ ] **H5.** `AgoraUiProjection.BuildPartyRelations` + `CoalitionOptionsMax = 8` (§16.4.3), taking
      `EngineTuning` as an argument.
- [ ] **H6.** `AgoraStateUISystem.cs`: register `agora.parties.relations` with its list writer,
      passing `AgoraRuntime.Tuning`; `UpdateAll()`. **Never a `GetterValueBinding`** — it would
      re-enumerate every UI tick.
- [ ] **H7.** `ui/types/bindings.d.ts`: `CoalitionOption`.
- [ ] **H8.** `ui_bindings.md`: §4.2 row, sort key, cap, §5 shape, and the "live view, not history"
      note (§16.4.5).
- [ ] **H9.** `Parties/CoalitionRelations.tsx` + scss (§16.4.4). Branch on `summary.system` for the
      FPTP message. **Never label an absent pairing a "refusal".**

### Close Part II

- [ ] **P1.** `docs/contracts/ui_bindings.md:3` — bump the `schemaVersion` **once** for the whole of
      Part II, by reading the current value and writing value + 1. Plan 0001 leaves it at `3` and
      Part I takes it to `4`, so Part II lands on `5` if shipped separately, or Part I and II together
      move it once to `4`. **Read, do not hard-code.**
- [ ] **P2.** `dotnet build Agora.sln`; `dotnet test tests\Agora.Core.Tests\Agora.Core.Tests.csproj`;
      `cd ui && npm run build`. All three green.
- [ ] **P3.** Manual walkthrough on a save with at least two elections and one government: confirm the
      manifesto tick is absent for a party that has never stood; the sparkline prints its scale
      maximum; the history strip shows a real split or merge if the save has one; the scorecard's
      delivery rate ignores pending mandates; and the relations list marks the sitting government.
      Confirm no screen anywhere says "betrayal" or "refuses".
- [ ] **P4.** Update `docs/status.md` and record which additions shipped.
- [ ] **P5.** Re-run the C#/TS contract-drift review that `fixplan.md:371-374` asks for after W4 and W6
      add bindings — with five new bindings across two passes, this is now the highest-value item on
      that list.
