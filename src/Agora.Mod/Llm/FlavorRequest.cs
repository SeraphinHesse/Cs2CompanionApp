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

        /// <summary>Governing, in opposition, newly founded, dissolving - a word, not a number.</summary>
        public string StatusWord = string.Empty;

        /// <summary>Existing name, when there is one worth keeping continuity with.</summary>
        public string CurrentName = string.Empty;
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

        /// <summary>How many articles to ask for. A prompt instruction, not engine state.</summary>
        public int ArticleCount { get; set; } = 4;

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
