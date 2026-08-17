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

        // ------------------------------------------------------------------ agora.stories

        /// <summary>
        /// The live stories, in the engine's order, with each slot's text resolved through the civic
        /// catalog.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <b>Nothing here is sorted.</b> <see cref="PoliticalState.LiveStories"/> is already sorted by
        /// id ordinal and <see cref="Story.Slots"/> major-first then by event id ordinal — both are
        /// declared total orders the engine writes down (<c>Story.Slots</c>' own remarks). Re-sorting
        /// them here would be the determinism bug <c>Agora.Core/CLAUDE.md</c> calls the most common
        /// one, and it would additionally make the panel disagree with the prose, which is generated
        /// against the same order.
        /// </para>
        /// <para>
        /// A null state or a null tuning publishes nothing rather than throwing: this runs on the UI
        /// thread, where an escaping exception costs far more than an empty panel. A null catalog does
        /// the same — every text field on a slot, and the severity a tier is projected from, is the
        /// catalog's, so without it there is no story to render, only a list of ids.
        /// </para>
        /// </remarks>
        internal static List<StoryPayload> BuildLiveStories(PoliticalState state,
                                                            CivicEventCatalog catalog,
                                                            EngineTuning tuning)
        {
            var rows = new List<StoryPayload>();
            if (state == null || catalog == null || tuning == null) return rows;

            List<Story> stories = state.LiveStories;
            if (stories == null) return rows;

            StoriesTuning storiesTuning = tuning.Stories;

            // Asked once per republish rather than once per slot: it is a property of the save and the
            // tuning, not of the event being priced.
            bool powerOn = PowerIsOn(state, tuning);

            for (int i = 0; i < stories.Count; i++)
            {
                Story story = stories[i];
                if (story == null || string.IsNullOrEmpty(story.Id)) continue;

                var payload = new StoryPayload
                {
                    Id = story.Id,
                    OpenedDate = story.OpenedDate,
                    ResolvesDate = story.ResolvesDate,
                    IsMandatory = story.IsMandatory,
                    Outcome = story.Outcome.ToString(),
                    ResolveEarlyRequested = story.ResolveEarlyRequested,

                    // The canned headline, which always exists. The model's version, if one arrived,
                    // rides on agora.stories.article beside the pool's rather than instead of it.
                    Headline = story.HeadlineFallback ?? ""
                };

                List<StorySlot> slots = story.Slots;
                if (slots != null)
                {
                    for (int s = 0; s < slots.Count; s++)
                    {
                        StorySlot slot = slots[s];
                        if (slot == null) continue;

                        payload.Slots.Add(BuildStorySlot(slot, catalog.Find(slot.EventId),
                                                         storiesTuning, powerOn, state.Power, tuning));
                    }
                }

                rows.Add(payload);
            }

            return rows;
        }

        /// <summary>
        /// One slot: the player's own state from <paramref name="slot"/>, everything else from the
        /// catalog entry that explains it.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <b>A slot the catalog no longer explains still ships, and its <c>name</c> stays empty.</b>
        /// The story is real, still on screen and still owed a verdict (<c>CivicEventCatalog.Find</c>'s
        /// own remarks), so dropping it would lose the player a decision. Substituting the event id
        /// for the missing name would look like it had worked — a raw id where a name belongs is a
        /// defect this repo has fixed twice — so the field is left empty and the panel says so in
        /// words.
        /// </para>
        /// <para>
        /// Such a slot is also unpriced: <see cref="StoryTier"/> is projected from the catalog's
        /// severity, and there is no severity to project. It quotes 0 and false, which is exactly what
        /// the command surface does with it — <c>AgoraRuntime.SpendPowerOverride</c> answers
        /// <c>Failed</c> for a slot it cannot price.
        /// </para>
        /// </remarks>
        private static StorySlotPayload BuildStorySlot(StorySlot slot, CivicEvent civicEvent,
                                                       StoriesTuning storiesTuning, bool powerOn,
                                                       PoliticalPowerState power, EngineTuning tuning)
        {
            var payload = new StorySlotPayload
            {
                EventId = slot.EventId ?? "",
                Role = slot.Role.ToString(),
                Response = slot.Response.ToString(),
                PlayerText = slot.PlayerText ?? "",
                Outcome = slot.SlotOutcome.ToString(),
                ManualDeclared = slot.ManualDeclared
            };

            if (civicEvent == null) return payload;

            payload.Name = civicEvent.Name ?? "";
            payload.Description = civicEvent.Description ?? "";
            payload.IgnoreText = civicEvent.IgnoreText ?? "";
            payload.GoalText = civicEvent.GoalText ?? "";
            payload.PowerOverrideText = civicEvent.PowerOverrideText ?? "";
            payload.SuccessText = civicEvent.SuccessText ?? "";
            payload.FailText = civicEvent.FailText ?? "";
            payload.Severity = civicEvent.Severity;

            // The engine's verdict, through the one implementation of it. No severity is compared to
            // anything in this file (docs/contracts/ui_bindings.md §4.7, in bold).
            StoryTier tier = civicEvent.TierUnder(storiesTuning.MandatorySeverityThreshold,
                                                  storiesTuning.MajorSeverityThreshold);
            payload.Tier = tier.ToString();

            // With the layer off the price is 0 and nothing is affordable — "there is no such currency
            // here", not "you cannot afford it yet". PoliticalPower already answers that way for the
            // tuning switch; the per-save setting is the half it cannot see, and PowerIsOn is where the
            // two are put together.
            payload.OverrideCost = powerOn ? PoliticalPower.OverrideCost(tier, tuning) : 0;
            payload.CanAfford = powerOn && PoliticalPower.CanAfford(power, tier, tuning);
            return payload;
        }

        /// <summary>
        /// Whether this save has a political-power economy at all: the per-save setting <b>and</b> the
        /// tuning switch, which have to agree.
        /// </summary>
        /// <remarks>
        /// Two switches because they answer different questions — the player's answer and the content
        /// pack's — and non-negotiable #10 puts the player's above the default. <c>PoliticalPower</c>
        /// reads the tuning half on every figure it produces and cannot read the other: it is handed
        /// <see cref="EngineTuning"/> and never <see cref="AgoraSettings"/>, which
        /// <c>StoryCycle.MovePower</c> records as a deficient seam every caller has to remember. This
        /// is the quote's copy of that guard, and it goes when the seam takes the setting.
        /// </remarks>
        private static bool PowerIsOn(PoliticalState state, EngineTuning tuning)
        {
            // Fully qualified: Agora.Mod.Core has a settings type of its own, and this is the per-save
            // one. Same qualification BuildSettings makes, one file over, for the same collision.
            Agora.Core.Contracts.AgoraSettings settings =
                state.Settings ?? new Agora.Core.Contracts.AgoraSettings();
            return settings.PoliticalPowerEnabled && tuning.Power.Enabled;
        }

        /// <summary>
        /// The archive, in the engine's order — <c>(ResolvedMonth</c> descending<c>, Id)</c> — capped
        /// at <see cref="StoryArchiveMax"/>.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <b><see cref="StoryBriefPayload.MetCount"/> and <see cref="StoryBriefPayload.ScoredCount"/>
        /// both exclude <see cref="SlotOutcome.Unmeasurable"/> slots</b>, counted exactly as
        /// <c>StoryResolution.Resolve</c> counts them: met into both, not-met into the denominator
        /// alone, and a slot the engine could not read into neither. So a three-slot story can
        /// legitimately read "1 of 2", and <see cref="StoryBriefPayload.SlotCount"/> — which is the
        /// full complement, holes included — is what says the third slot has not gone missing.
        /// </para>
        /// <para>
        /// The <paramref name="catalog"/> is unused and the parameter is the publisher's, not this
        /// row's: a brief carries no event text. A null one therefore does not empty the archive, the
        /// way it empties <see cref="BuildLiveStories"/> — the guard belongs on what is actually read.
        /// </para>
        /// </remarks>
        internal static List<StoryBriefPayload> BuildStoryArchive(PoliticalState state,
                                                                  CivicEventCatalog catalog)
        {
            var rows = new List<StoryBriefPayload>();
            if (state == null || state.StoryArchive == null) return rows;

            List<Story> archive = state.StoryArchive;
            for (int i = 0; i < archive.Count && rows.Count < StoryArchiveMax; i++)
            {
                Story story = archive[i];
                if (story == null || string.IsNullOrEmpty(story.Id)) continue;

                var row = new StoryBriefPayload
                {
                    Id = story.Id,
                    OpenedDate = story.OpenedDate,
                    ResolvesDate = story.ResolvesDate,
                    Outcome = story.Outcome.ToString(),
                    Headline = story.HeadlineFallback ?? ""
                };

                List<StorySlot> slots = story.Slots ?? new List<StorySlot>();
                row.SlotCount = slots.Count;

                for (int s = 0; s < slots.Count; s++)
                {
                    StorySlot slot = slots[s];
                    if (slot == null) continue;

                    if (slot.SlotOutcome == SlotOutcome.Met) row.MetCount++;
                    if (slot.SlotOutcome == SlotOutcome.Met ||
                        slot.SlotOutcome == SlotOutcome.NotMet) row.ScoredCount++;
                }

                rows.Add(row);
            }

            return rows;
        }

        /// <summary>
        /// One story's prose, in both voices and at both ends, or an empty payload for a story the
        /// ledger has never heard of.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <b>Both sources of both kinds are asked for, and an empty CLI half is the ordinary
        /// case.</b> The pool answers every poll and the model answers rarely, so a missing CLI half is
        /// what the panel sees nearly always — it is not an error and nothing here logs one.
        /// </para>
        /// <para>
        /// An unknown story id answers an empty payload rather than throwing, the same rule
        /// <see cref="BuildArticle"/> follows. That rule is also why an id typo is silent, so a blank
        /// card is not diagnosable from the panel — the ledger is where to look.
        /// </para>
        /// </remarks>
        internal static StoryArticlePayload BuildStoryArticle(StoryProseLedger prose, string storyId)
        {
            var payload = new StoryArticlePayload { StoryId = storyId ?? "" };
            if (prose == null || string.IsNullOrEmpty(storyId)) return payload;

            StoryProse poolOpening = prose.Get(storyId, StoryProseKind.Opening, ProseSource.Pool);
            StoryProse cliOpening = prose.Get(storyId, StoryProseKind.Opening, ProseSource.Cli);
            StoryProse poolClosing = prose.Get(storyId, StoryProseKind.Resolution, ProseSource.Pool);
            StoryProse cliClosing = prose.Get(storyId, StoryProseKind.Resolution, ProseSource.Cli);

            if (poolOpening != null)
            {
                payload.PoolHeadline = poolOpening.Headline ?? "";
                payload.PoolArticle = poolOpening.Article ?? "";
            }

            if (cliOpening != null)
            {
                payload.CliHeadline = cliOpening.Headline ?? "";
                payload.CliArticle = cliOpening.Article ?? "";
            }

            if (poolClosing != null)
            {
                payload.PoolResolutionHeadline = poolClosing.Headline ?? "";
                payload.PoolResolutionArticle = poolClosing.Article ?? "";
            }

            if (cliClosing != null)
            {
                payload.CliResolutionHeadline = cliClosing.Headline ?? "";
                payload.CliResolutionArticle = cliClosing.Article ?? "";
            }

            return payload;
        }

        /// <summary>
        /// The power counter: the balance straight across, with the ledger's newest
        /// <see cref="PowerLedgerMax"/> rows.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <see cref="PowerPayload.InDebt"/> is <see cref="PoliticalPower.IsInDebt"/>'s answer rather
        /// than a sign test of our own: what counts as debt is the engine's rule and the consequence
        /// attached to it is a capped, tuned effect, so a second definition here would drift from the
        /// one that charges for it.
        /// </para>
        /// <para>
        /// The balance is published whether or not the layer is on. <c>enabled</c> is what the counter
        /// branches on — false hides it entirely rather than rendering a zero, because a zero is a
        /// balance and "this save has no such currency" is not one.
        /// </para>
        /// </remarks>
        internal static PowerPayload BuildPower(PoliticalState state, EngineTuning tuning)
        {
            var payload = new PowerPayload();
            if (state == null || tuning == null) return payload;

            payload.Enabled = PowerIsOn(state, tuning);

            PoliticalPowerState power = state.Power;
            if (power == null) return payload;

            payload.Balance = power.Balance;
            payload.LifetimeEarned = power.LifetimeEarned;
            payload.LifetimeSpent = power.LifetimeSpent;
            payload.InDebt = PoliticalPower.IsInDebt(power);

            List<PowerLedgerEntry> ledger = power.Ledger;
            if (ledger == null) return payload;

            // Newest last and already sorted by (Month, Sequence), so the cap drops from the FRONT:
            // the rows a counter is asked about are the recent ones, and taking the first 24 would
            // pin the strip to the opening months of the save forever.
            int from = ledger.Count > PowerLedgerMax ? ledger.Count - PowerLedgerMax : 0;
            for (int i = from; i < ledger.Count; i++)
            {
                PowerLedgerEntry entry = ledger[i];
                if (entry == null) continue;

                payload.Ledger.Add(new PowerLedgerRowPayload
                {
                    Month = entry.Month,
                    Sequence = entry.Sequence,
                    Reason = entry.Reason.ToString(),
                    Delta = entry.Delta,
                    StoryId = entry.StoryId ?? "",
                    EventId = entry.EventId ?? ""
                });
            }

            return payload;
        }

        /// <summary>
        /// The story card queue, oldest first: a straight copy of the ring, with no filtering and no
        /// sorting of its own.
        /// </summary>
        /// <remarks>
        /// Same copy-rather-than-project rule as <see cref="BuildAlerts"/>, and for the same reason:
        /// the ring is in the order the cards were raised, which is the order the player is asked to
        /// answer them in. <c>major</c> is the verdict recorded when the alert was raised and is never
        /// recomputed here.
        /// </remarks>
        internal static List<StoryAlertPayload> BuildStoryAlerts(IList<StoryAlert> alerts)
        {
            var rows = new List<StoryAlertPayload>();
            if (alerts == null) return rows;

            for (int i = 0; i < alerts.Count; i++)
            {
                StoryAlert alert = alerts[i];
                if (alert == null || string.IsNullOrEmpty(alert.Id)) continue;

                rows.Add(new StoryAlertPayload
                {
                    Id = alert.Id,
                    Date = alert.Date,
                    Headline = alert.Headline ?? "",
                    Summary = alert.Summary ?? "",
                    SlotCount = alert.SlotCount,
                    Major = alert.Major
                });
            }

            return rows;
        }
    }
}
