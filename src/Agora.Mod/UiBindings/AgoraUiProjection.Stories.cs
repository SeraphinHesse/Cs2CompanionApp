using System.Collections.Generic;
using Agora.Core.Contracts;
using Agora.Core.Stories;
using Agora.Core.Stories.Catalog;
using Agora.Core.Tuning;
using Agora.Mod.Core;
using Agora.Mod.Llm;

namespace Agora.Mod.UiBindings
{
    /// <summary>
    /// The <c>agora.stories</c> half of the projection: live stories, the archive, a story's prose and
    /// the political-power counter.
    /// </summary>
    /// <remarks>
    /// A new file rather than more of <c>AgoraUiProjection.cs</c>, which is why that class is
    /// <c>partial</c>. Same rules apply here as there: <b>nothing in this file computes politics.</b>
    /// Every number is copied from <see cref="PoliticalState"/>, the civic catalog or the tuning that
    /// priced it, and a tier is the engine's verdict rather than a severity this file compares to a
    /// threshold of its own (<c>docs/contracts/ui_bindings.md</c> §4.5, in bold).
    /// </remarks>
    internal static partial class AgoraUiProjection
    {
        /// <summary>Most archived stories the archive binding will carry.</summary>
        /// <remarks>
        /// A payload bound, not a retention policy: <c>stories.archiveRetention</c> decides what the
        /// engine keeps and this decides how much of it crosses the bridge at once, the same split
        /// <see cref="NewsFeedMax"/> makes against the feed.
        /// </remarks>
        internal const int StoryArchiveMax = 24;

        /// <summary>Most ledger rows the power binding will carry, newest last.</summary>
        internal const int PowerLedgerMax = 24;

        // AGORA-SEAM(wave-6/6a) ------------------------------------------------------------------
        //
        // Every body below is a stub that returns an empty payload of the right shape, so that lanes
        // 6b, 6c and 6d compile and subscribe against a live binding surface from commit one. LANE 6a
        // OWNS THIS FILE and replaces every one of them.
        //
        // What "done" means, per function:
        //
        //  BuildLiveStories  — one StoryPayload per entry in state.LiveStories, IN THE ENGINE'S
        //                      ORDER (LiveStories is sorted by Id ordinal; Slots are sorted major
        //                      first then by event id ordinal). Do not re-sort either. Each slot's
        //                      name, description and the five prose fields come from the civic
        //                      catalog entry for its EventId; a slot whose event the catalog no
        //                      longer explains still ships, with its id NEVER rendered as a name —
        //                      leave Name empty and let the panel say so. `tier` is
        //                      civicEvent.TierUnder(stories.mandatorySeverityThreshold,
        //                      stories.majorSeverityThreshold). `overrideCost` is
        //                      PoliticalPower.OverrideCost(tier, tuning) and `canAfford` is
        //                      PoliticalPower.CanAfford(state.Power, tier, tuning) — BOTH published as
        //                      0/false when power is off, so the card never quotes a live price
        //                      against a balance that cannot move.
        //
        //  BuildStoryArchive — one StoryBriefPayload per entry in state.StoryArchive, in the engine's
        //                      order ((ResolvedMonth desc, Id)), capped at StoryArchiveMax. metCount
        //                      and scoredCount both EXCLUDE Unmeasurable slots, which is the same
        //                      exclusion the 2-of-3 rule makes; slotCount does not.
        //
        //  BuildStoryArticle — the four-by-two prose grid for one story id, read from
        //                      AgoraRuntime.StoryProse. Ask the ledger for BOTH sources of BOTH kinds
        //                      and ship whichever exist; the CLI half is "" in the ordinary case and
        //                      that is not an error. An unknown story id answers an EMPTY payload
        //                      rather than throwing — the same rule BuildArticle follows — but note
        //                      that rule is why an id typo is silent, so the panel is the wrong place
        //                      to diagnose one.
        //
        //  BuildPower        — state.Power straight across, `enabled` from the per-save setting AND
        //                      the tuning switch together (a save may have it on while tuning has it
        //                      off), ledger capped at PowerLedgerMax keeping the NEWEST rows.
        //
        //  BuildStoryAlerts  — AgoraRuntime.StoryAlerts copied across, oldest first, unsorted.
        //
        // Acceptance for all five: none of them may compute a tier, a threshold, a severity
        // comparison, a cost or an affordability verdict of its own. If a number is not already in
        // state, in the catalog or behind a PoliticalPower/EngineTuning call, it does not belong here.

        internal static List<StoryPayload> BuildLiveStories(PoliticalState state,
                                                            CivicEventCatalog catalog,
                                                            EngineTuning tuning)
        {
            return new List<StoryPayload>();
        }

        internal static List<StoryBriefPayload> BuildStoryArchive(PoliticalState state,
                                                                  CivicEventCatalog catalog)
        {
            return new List<StoryBriefPayload>();
        }

        internal static StoryArticlePayload BuildStoryArticle(StoryProseLedger prose, string storyId)
        {
            return new StoryArticlePayload { StoryId = storyId ?? "" };
        }

        internal static PowerPayload BuildPower(PoliticalState state, EngineTuning tuning)
        {
            return new PowerPayload();
        }

        internal static List<StoryAlertPayload> BuildStoryAlerts(IList<StoryAlert> alerts)
        {
            return new List<StoryAlertPayload>();
        }
    }
}
