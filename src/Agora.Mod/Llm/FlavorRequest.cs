// Compiled into BOTH Agora.Mod and (by <Compile Link>) tests/Agora.Core.Tests: it must stay free of
// every Game.*, Unity.* and Colossal.* type. #nullable disable keeps it warning-clean in the test
// project, which enables nullable, without annotating a file the mod compiles unannotated.
#nullable disable

using System;
using System.Collections.Generic;
using Agora.Core.Contracts;

namespace Agora.Mod.Llm
{
    /// <summary>Why the flavor provider was woken. §3 ratified these three cadences.</summary>
    public enum FlavorWakeReason
    {
        Yearly = 0,
        Election = 1,
        Manual = 2
    }

    /// <summary>
    /// One party, as the prompt sees it.
    /// </summary>
    /// <remarks>
    /// Engine-owned identity only: an ID, an archetype, and the issue the party exists to shout
    /// about. Deliberately no vote share, no seat count, no platform coordinates. Feeding the model
    /// numbers is not itself a #1 violation - the rule is about numbers coming back - but a prompt
    /// that recites poll numbers invites prose that recites them back slightly wrong, and the
    /// dashboard shows the real ones two panels away.
    /// </remarks>
    public sealed class PartyBrief
    {
        public string PartyId = string.Empty;
        public string ArchetypeId = string.Empty;

        /// <summary>The issue this party leads on.</summary>
        public Issue CoreGrievance;

        /// <summary>
        /// Where the party stands as things are: <c>leads the government</c>, <c>in government</c>,
        /// <c>in opposition</c>, or the lifecycle word when that is the whole story - a phrase, never
        /// a figure. Built by <see cref="StandingWord"/>; see its remarks for what it is not.
        /// </summary>
        public string StatusWord = string.Empty;

        /// <summary>
        /// The standing phrase for <paramref name="party"/>.
        /// </summary>
        /// <remarks>
        /// <para>
        /// This used to be <c>Status.ToString()</c>, which is a lifecycle word - Active, Endangered,
        /// Dissolved, Merged, Revived - and says nothing at all about who governs. The prompt's
        /// election block meanwhile told the model this word was the whole of the outcome it might
        /// write from, so between them they invited a result to be written out of nothing, which is
        /// the invention non-negotiable #1 exists to stop. A governing word is the smallest honest
        /// thing to send instead.
        /// </para>
        /// <para>
        /// It is still not an election result. <c>IsInGovernment</c> and <c>IsIncumbent</c> describe
        /// the arrangement standing at the moment the brief is built, and on the morning after a
        /// count that is usually the arrangement the count has just unseated - government formation
        /// has not run yet. <c>FlavorPromptBuilder.AppendElectionCoverage</c> says exactly that to the
        /// model rather than overstating it; keep the two in step.
        /// </para>
        /// <para>
        /// Dissolved and merged override the role, because a party that is off the ballot is not in
        /// opposition, it is gone, and the engine leaves both flags false on one anyway. Endangered
        /// and revived qualify it, because both are worth writing about and neither states a figure.
        /// </para>
        /// <para>
        /// The endangered qualifier reads as lifecycle, not as a count. It used to say <c>losing
        /// ground</c>, one adjective off <c>lost ground</c> - an exemplar the election block dropped
        /// for being imitable loser-naming - and the roster sits a section above <c>Do not name a
        /// winner or a loser</c>. <c>At risk of folding</c> says the same engine fact (below the
        /// threshold once, one more result and the party dies) with no electoral reading.
        /// </para>
        /// </remarks>
        public static string StandingWord(Party party)
        {
            if (party == null) return string.Empty;

            switch (party.Status)
            {
                case PartyStatus.Dissolved: return "dissolved, off the ballot";
                case PartyStatus.Merged: return "merged into another party";
            }

            string role = party.IsIncumbent
                ? "leads the government"
                : party.IsInGovernment ? "in government" : "in opposition";

            switch (party.Status)
            {
                case PartyStatus.Endangered: return role + ", at risk of folding";
                case PartyStatus.Revived: return role + ", recently revived";
                default: return role;
            }
        }

        /// <summary>Existing name, when there is one worth keeping continuity with.</summary>
        public string CurrentName = string.Empty;

        /// <summary>
        /// When the party came into being. Not prompt material — the canned pool keys its name draw
        /// on this rather than on the request date, so a party's name is a fixed function of its own
        /// founding and does not change every time prose is regenerated.
        /// </summary>
        public SimDate FoundedDate;
    }

    /// <summary>One faction inside a party, as the prompt sees it.</summary>
    public sealed class FactionBrief
    {
        public string FactionId = string.Empty;
        public string PartyId = string.Empty;
        public string ArchetypeId = string.Empty;
        public Issue CoreGrievance;
        public string StatusWord = string.Empty;
        public string CurrentName = string.Empty;

        /// <summary>When the faction formed. Keys the canned pool's name draw; see <see cref="PartyBrief.FoundedDate"/>.</summary>
        public SimDate FoundedDate;
    }

    /// <summary>One timeline event needing a local angle.</summary>
    public sealed class EventBrief
    {
        public string EventId = string.Empty;

        /// <summary>Engine-authored title. Not prose to publish; context to write from.</summary>
        public string Title = string.Empty;

        /// <summary>The catalog's factual one-liner (§6). A prompt input, never published as-is.</summary>
        public string HeadlineBrief = string.Empty;

        public List<string> Tags = new List<string>();
    }

    /// <summary>
    /// Everything one generation needs. Assembled by the caller on the sim thread, then handed to the
    /// worker; treat it as immutable once <c>RequestFlavor</c> has been called with it.
    /// </summary>
    public sealed class FlavorRequest
    {
        /// <summary>The sim date this generation belongs to. From <c>AgoraTimeService</c> only (#8).</summary>
        public SimDate Date { get; set; }

        public FlavorWakeReason Reason { get; set; }

        /// <summary>EU or NA. Drives the political vocabulary the prose uses, nothing else.</summary>
        public RegionTheme Theme { get; set; }

        /// <summary>The measured city. May be null; the prompt then omits the situation block.</summary>
        public CitySnapshot Snapshot { get; set; }

        public List<PartyBrief> Parties { get; set; } = new List<PartyBrief>();
        public List<FactionBrief> Factions { get; set; } = new List<FactionBrief>();
        public List<EventBrief> Events { get; set; } = new List<EventBrief>();

        /// <summary>The ordinary round's article count. A prompt instruction, not engine state.</summary>
        public const int DefaultArticleCount = 4;

        /// <summary>
        /// An election round under NA rules: the ordinary count plus one each for the result, the
        /// winner's reaction and the loser's reaction that <c>FlavorPromptBuilder</c> asks for on top.
        /// </summary>
        /// <remarks>
        /// Like <see cref="ElectionArticleCountEu"/>, this exceeds the "3–5 articles per wake" figure
        /// §11 M3 ratifies; §11 M3 is where the deviation is recorded.
        /// </remarks>
        public const int ElectionArticleCountNa = DefaultArticleCount + 3;

        /// <summary>
        /// An election round under EU rules: the NA set plus the coalition-outlook piece.
        /// </summary>
        /// <remarks>
        /// <para>
        /// A raised count is a prompt instruction to the model and nothing else — the canned pool is
        /// handed <see cref="RosterCopy"/>, which carries <see cref="DefaultArticleCount"/>, so it is
        /// never asked to fill eight slots out of template lists that hold three.
        /// <c>FlavorPromptBuilder.AppendTask</c> clamps at twelve, so neither value is touched by it.
        /// </para>
        /// <para>
        /// This is a deviation from the "3–5 articles per wake" figure §11 M3 ratifies, and §11 M3 is
        /// where it is recorded — read the authority there, not just the rationale here.
        /// </para>
        /// </remarks>
        // StaticPoolProvider.BuildArticles clamps on this same constant rather than a literal, so the
        // pool can never be handed a count it caps back down.
        public const int ElectionArticleCountEu = ElectionArticleCountNa + 1;

        /// <summary>How many articles to ask for. A prompt instruction, not engine state.</summary>
        public int ArticleCount { get; set; } = DefaultArticleCount;

        /// <summary>
        /// The count an election wake asks for under <paramref name="theme"/>. Per-request: the caller
        /// sets it on a request it just constructed, so a raised count cannot leak into a later round.
        /// </summary>
        public static int ElectionArticleCount(RegionTheme theme) =>
            theme == RegionTheme.Eu ? ElectionArticleCountEu : ElectionArticleCountNa;

        /// <summary>
        /// A copy of this request for <c>StaticPoolProvider.Roster</c>: the same cast of parties,
        /// factions and events, but the ordinary <see cref="DefaultArticleCount"/>.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The pool's roster answers "who exists", not "what was asked for this round". A CLI request
        /// is the only thing that ever carries a raised <see cref="ArticleCount"/>, and the pool must
        /// not inherit it: its templates are a fixed, small set, so an eight-article round exhausts
        /// <c>UniqueLine</c>'s bounded retry and files the same body twice. The roster also outlives
        /// the request — it is only rebuilt at the next month boundary, and the dashboard polls the
        /// pool throughout that window — so an inherited count would not be a one-round mistake.
        /// </para>
        /// <para>
        /// A copy rather than the request itself for a second reason: the pool writes
        /// <see cref="Date"/> and <see cref="Theme"/> onto its roster on every poll — and
        /// <see cref="Snapshot"/> on the first poll after a roster that has none — from the sim
        /// thread, while the CLI worker may be reading the request it was handed.
        /// The lists are shared, because both sides only ever read them, and the briefs inside are
        /// immutable once <c>FillBriefs</c> has built them.
        /// </para>
        /// </remarks>
        public FlavorRequest RosterCopy()
        {
            return new FlavorRequest
            {
                Date = Date,
                Reason = Reason,
                Theme = Theme,
                Snapshot = Snapshot,
                Parties = Parties,
                Factions = Factions,
                Events = Events,
                Catalog = Catalog,
                ArticleCount = DefaultArticleCount
            };
        }

        /// <summary>
        /// The IDs the response may reference. Built from the briefs and the snapshot's districts by
        /// <see cref="BuildCatalog"/> unless the caller supplies its own.
        /// </summary>
        public FlavorCatalog Catalog { get; set; }

        /// <summary>
        /// Derives the legal-ID set from this request. Called automatically when
        /// <see cref="Catalog"/> is null.
        /// </summary>
        public FlavorCatalog BuildCatalog()
        {
            var partyIds = new List<string>();
            for (int i = 0; i < Parties.Count; i++) partyIds.Add(Parties[i].PartyId);

            var factionIds = new List<string>();
            for (int i = 0; i < Factions.Count; i++) factionIds.Add(Factions[i].FactionId);

            var eventIds = new List<string>();
            for (int i = 0; i < Events.Count; i++) eventIds.Add(Events[i].EventId);

            var districtIds = new List<string>();
            if (Snapshot != null && Snapshot.Districts != null)
            {
                for (int i = 0; i < Snapshot.Districts.Count; i++)
                {
                    var district = Snapshot.Districts[i];
                    if (district != null) districtIds.Add(district.Id);
                }
            }

            return new FlavorCatalog(partyIds, factionIds, districtIds, eventIds);
        }

        /// <summary>The catalog to validate against, building one if the caller did not.</summary>
        public FlavorCatalog EffectiveCatalog() => Catalog ?? BuildCatalog();
    }
}
