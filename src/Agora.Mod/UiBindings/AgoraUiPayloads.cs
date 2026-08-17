using System.Collections.Generic;
using Colossal.UI.Binding;

namespace Agora.Mod.UiBindings
{
    /// <summary>
    /// Wire conventions shared by every payload in this folder, in one place so they cannot drift
    /// between publishers (<c>docs/contracts/ui_bindings.md</c> §2).
    /// </summary>
    internal static class UiJson
    {
        /// <summary>
        /// An absent date is the empty string, never null. That keeps every date field a
        /// <c>string</c> on the TypeScript side; a null would force every consumer to widen its type
        /// and handle a case the contract says does not exist.
        /// </summary>
        internal static void Date(IJsonWriter writer, string name, Agora.Core.Contracts.SimDate? date)
        {
            writer.PropertyName(name);
            writer.Write(date.HasValue ? date.Value.ToString() : "");
        }

        internal static void Date(IJsonWriter writer, string name, Agora.Core.Contracts.SimDate date)
        {
            writer.PropertyName(name);
            writer.Write(date.ToString());
        }

        /// <summary>An absent id is the empty string, never null — same reason as <see cref="Date"/>.</summary>
        internal static void Id(IJsonWriter writer, string name, string id)
        {
            writer.PropertyName(name);
            writer.Write(id ?? "");
        }

        internal static void Text(IJsonWriter writer, string name, string value)
        {
            writer.PropertyName(name);
            writer.Write(value ?? "");
        }

        internal static void Number(IJsonWriter writer, string name, double value)
        {
            writer.PropertyName(name);
            writer.Write(value);
        }

        internal static void Number(IJsonWriter writer, string name, int value)
        {
            writer.PropertyName(name);
            writer.Write(value);
        }

        internal static void Flag(IJsonWriter writer, string name, bool value)
        {
            writer.PropertyName(name);
            writer.Write(value);
        }

        /// <summary>
        /// Enums cross as their C# member name, never as an integer. An integer would silently
        /// re-map the moment anyone reorders an enum, and the TypeScript side declares these as
        /// string unions.
        /// </summary>
        internal static void Enum<T>(IJsonWriter writer, string name, T value) where T : struct
        {
            writer.PropertyName(name);
            writer.Write(value.ToString());
        }

        internal static void Ids(IJsonWriter writer, string name, List<string> ids)
        {
            writer.PropertyName(name);

            if (ids == null)
            {
                writer.ArrayBegin(0);
                writer.ArrayEnd();
                return;
            }

            writer.ArrayBegin((uint)ids.Count);
            for (int i = 0; i < ids.Count; i++) writer.Write(ids[i] ?? "");
            writer.ArrayEnd();
        }

        internal static void Shares(IJsonWriter writer, string name, List<PartySharePayload> shares)
        {
            writer.PropertyName(name);

            if (shares == null)
            {
                writer.ArrayBegin(0);
                writer.ArrayEnd();
                return;
            }

            writer.ArrayBegin((uint)shares.Count);
            for (int i = 0; i < shares.Count; i++) shares[i].Write(writer);
            writer.ArrayEnd();
        }
    }

    // ---------------------------------------------------------------------------- shared

    /// <summary>
    /// One party's slice of a vote. Every list of these is sorted by <c>partyId</c> ordinal
    /// ascending — the engine's contract for <c>List&lt;PartyVoteShare&gt;</c>, which the panel is
    /// forbidden to re-sort.
    /// </summary>
    public sealed class PartySharePayload : IJsonWritable
    {
        public string PartyId;
        public double Share;

        public void Write(IJsonWriter writer)
        {
            writer.TypeBegin("agora.PartyShare");
            UiJson.Id(writer, "partyId", PartyId);
            UiJson.Number(writer, "share", Share);
            writer.TypeEnd();
        }
    }

    // ---------------------------------------------------------------------------- agora.state

    /// <summary>Dashboard chrome: what date it is, which term, and when the next ballot falls.</summary>
    public sealed class StateSummaryPayload : IJsonWritable
    {
        public int SchemaVersion;
        public Agora.Core.Contracts.SimDate? Date;
        public int TermNumber;
        public string System = "Proportional";
        public string Theme = "Eu";
        public Agora.Core.Contracts.SimDate? NextElectionDate;
        public bool IsCampaignSeason;

        /// <summary>-1, not 0, when nothing is scheduled — "this week" must stay distinguishable.</summary>
        public int WeeksToElection = -1;

        public string MayorPartyId = "";

        public void Write(IJsonWriter writer)
        {
            writer.TypeBegin("agora.StateSummary");
            UiJson.Number(writer, "schemaVersion", SchemaVersion);
            UiJson.Date(writer, "date", Date);
            UiJson.Number(writer, "termNumber", TermNumber);
            UiJson.Text(writer, "system", System);
            UiJson.Text(writer, "theme", Theme);
            UiJson.Date(writer, "nextElectionDate", NextElectionDate);
            UiJson.Flag(writer, "isCampaignSeason", IsCampaignSeason);
            UiJson.Number(writer, "weeksToElection", WeeksToElection);
            UiJson.Id(writer, "mayorPartyId", MayorPartyId);
            writer.TypeEnd();
        }
    }

    /// <summary>
    /// The per-save settings document, mirrored for the settings surface and the first-run dialog
    /// (<c>docs/contracts/ui_bindings.md</c> §4.1).
    /// </summary>
    /// <remarks>
    /// Exactly the eight fields plan 0001 §8 fixed, and no more. <c>isFirstRun</c> is deliberately
    /// <b>not</b> among them: it is a lifecycle signal the sidecar never stores, and folding it in
    /// would put a value with no persisted counterpart inside the payload that mirrors the persisted
    /// document. It ships as <c>agora.state.isFirstRun</c> instead.
    /// </remarks>
    public sealed class SettingsPayload : IJsonWritable
    {
        public int SchemaVersion;
        public int StartYear;
        public string Theme = "Eu";
        public string System = "Proportional";
        public bool ThemeLocked;
        public bool PauseOnMajorNews = true;
        public bool ShowAllReports;
        public bool EffectsEnabled = true;

        /// <summary>
        /// The three voter-model levels, as enum <i>names</i>. The panel renders and writes these
        /// names verbatim, so the wire carries "Default" rather than 1 and a member inserted into the
        /// enum cannot silently change what a save means.
        /// </summary>
        public string VoteSharpness = "Default";
        public string NewsInfluence = "Default";
        public string BrandDiscipline = "Default";

        /// <summary>
        /// What each chosen level currently resolves to, for display only. Read from the tuning file
        /// through <c>TuningPresets</c> rather than duplicated here — the panel must never hold its
        /// own copy of a coefficient, or it will eventually disagree with the engine.
        /// </summary>
        public double VoteSharpnessValue;
        public double NewsInfluenceValue;
        public double BrandDisciplineValue;

        /// <summary>
        /// The six story settings, published for the first time in wave 6.
        /// </summary>
        /// <remarks>
        /// They have been in the sidecar since wave 2 and reachable from no surface since — a
        /// per-save setting nothing can read or write is a setting only a text editor can change.
        /// <see cref="StoriesPerCycle"/> and <see cref="EventsPerStory"/> are plain counts rather
        /// than levels because the design document names them as numbers; the other two are levels
        /// because the shipped settings already express intensity that way.
        /// </remarks>
        public bool StoriesEnabled = true;
        public int StoriesPerCycle = 2;
        public int EventsPerStory = 3;
        public bool PoliticalPowerEnabled = true;
        public string PowerIntensity = "Default";
        public string StoryDifficulty = "Default";

        /// <summary>
        /// Whether a major story card holds the clock. New in wave 7; see
        /// <c>AgoraSettings.PauseOnMajorStory</c> for why it is not
        /// <see cref="PauseOnMajorNews"/> under another name.
        /// </summary>
        public bool PauseOnMajorStory = true;

        public void Write(IJsonWriter writer)
        {
            writer.TypeBegin("agora.SettingsPayload");
            UiJson.Number(writer, "schemaVersion", SchemaVersion);
            UiJson.Number(writer, "startYear", StartYear);
            UiJson.Text(writer, "theme", Theme);
            UiJson.Text(writer, "system", System);
            UiJson.Flag(writer, "themeLocked", ThemeLocked);
            UiJson.Flag(writer, "pauseOnMajorNews", PauseOnMajorNews);
            UiJson.Flag(writer, "showAllReports", ShowAllReports);
            UiJson.Flag(writer, "effectsEnabled", EffectsEnabled);
            UiJson.Text(writer, "voteSharpness", VoteSharpness);
            UiJson.Text(writer, "newsInfluence", NewsInfluence);
            UiJson.Text(writer, "brandDiscipline", BrandDiscipline);
            UiJson.Number(writer, "voteSharpnessValue", VoteSharpnessValue);
            UiJson.Number(writer, "newsInfluenceValue", NewsInfluenceValue);
            UiJson.Number(writer, "brandDisciplineValue", BrandDisciplineValue);
            UiJson.Flag(writer, "storiesEnabled", StoriesEnabled);
            UiJson.Number(writer, "storiesPerCycle", StoriesPerCycle);
            UiJson.Number(writer, "eventsPerStory", EventsPerStory);
            UiJson.Flag(writer, "politicalPowerEnabled", PoliticalPowerEnabled);
            UiJson.Text(writer, "powerIntensity", PowerIntensity);
            UiJson.Text(writer, "storyDifficulty", StoryDifficulty);
            UiJson.Flag(writer, "pauseOnMajorStory", PauseOnMajorStory);
            writer.TypeEnd();
        }
    }

    // ---------------------------------------------------------------------------- agora.parties

    /// <summary>
    /// The party lookup table. Every other payload names a party by id alone and resolves its label
    /// and colour here, so two panels cannot disagree about what colour a party is.
    /// </summary>
    public sealed class PartyBriefPayload : IJsonWritable
    {
        public string Id = "";
        public string Name = "";
        public string ShortName = "";
        public string Description = "";
        public string Slogan = "";
        public string ColorHex = "#808080";
        public string Status = "Active";
        public bool IsIncumbent;
        public bool IsInGovernment;
        public string CoreGrievance = "Services";
        public Agora.Core.Contracts.SimDate? FoundedDate;
        public Agora.Core.Contracts.SimDate? DissolvedDate;

        // Projected from Party.PlayerOverrides. A set flag means the field beside it is player-owned
        // rather than flavor-owned, so the UI must never present it as generated text.
        public bool NameLocked;
        public bool DescriptionLocked;
        public bool ColorLocked;

        public void Write(IJsonWriter writer)
        {
            writer.TypeBegin("agora.PartyBrief");
            UiJson.Id(writer, "id", Id);
            UiJson.Text(writer, "name", Name);
            UiJson.Text(writer, "shortName", ShortName);
            UiJson.Text(writer, "description", Description);
            UiJson.Text(writer, "slogan", Slogan);
            UiJson.Text(writer, "colorHex", ColorHex);
            UiJson.Text(writer, "status", Status);
            UiJson.Flag(writer, "isIncumbent", IsIncumbent);
            UiJson.Flag(writer, "isInGovernment", IsInGovernment);
            UiJson.Text(writer, "coreGrievance", CoreGrievance);
            UiJson.Date(writer, "foundedDate", FoundedDate);
            UiJson.Date(writer, "dissolvedDate", DissolvedDate);
            UiJson.Flag(writer, "nameLocked", NameLocked);
            UiJson.Flag(writer, "descriptionLocked", DescriptionLocked);
            UiJson.Flag(writer, "colorLocked", ColorLocked);
            writer.TypeEnd();
        }
    }

    /// <summary>A faction inside a party. Empty under the EU theme, which models no factions.</summary>
    public sealed class FactionBriefPayload : IJsonWritable
    {
        public string Id = "";
        public string PartyId = "";
        public string Name = "";
        public string ShortName = "";
        public string LeaderName = "";
        public double InternalSupport;
        public bool IsDominant;
        public double TensionWithParty;
        public string Status = "Active";
        public string CoreGrievance = "Services";

        public void Write(IJsonWriter writer)
        {
            writer.TypeBegin("agora.FactionBrief");
            UiJson.Id(writer, "id", Id);
            UiJson.Id(writer, "partyId", PartyId);
            UiJson.Text(writer, "name", Name);
            UiJson.Text(writer, "shortName", ShortName);
            UiJson.Text(writer, "leaderName", LeaderName);
            UiJson.Number(writer, "internalSupport", InternalSupport);
            UiJson.Flag(writer, "isDominant", IsDominant);
            UiJson.Number(writer, "tensionWithParty", TensionWithParty);
            UiJson.Text(writer, "status", Status);
            UiJson.Text(writer, "coreGrievance", CoreGrievance);
            writer.TypeEnd();
        }
    }

    /// <summary>
    /// The swatches the colour picker offers, from <c>EngineTuning.Parties.ColorPalette</c>.
    /// </summary>
    /// <remarks>
    /// A binding rather than a constant in the panel: swatches hard-coded in TypeScript drift from
    /// the tuning silently the first time anyone edits the tuning. The order is the tuning's, never
    /// re-sorted — a swatch's position is how a player recognises it between sessions.
    /// </remarks>
    public sealed class PartyPalettePayload : IJsonWritable
    {
        public List<string> Colors = new List<string>();

        public void Write(IJsonWriter writer)
        {
            writer.TypeBegin("agora.PartyPalette");
            UiJson.Ids(writer, "colors", Colors);
            writer.TypeEnd();
        }
    }

    /// <summary>
    /// What the party editors will accept, so the character counter and the rejector are the same
    /// numbers.
    /// </summary>
    /// <remarks>
    /// Every value is read from <see cref="Agora.Core.Engine.Parties.PartyIdentity"/>, which is also
    /// what enforces them. A literal here would make the counter a second copy of a limit, and when
    /// two copies disagree the wrong one is always the counter: the player finds out by being
    /// refused after typing.
    /// </remarks>
    public sealed class PartyEditLimitsPayload : IJsonWritable
    {
        public int NameMax;
        public int ShortNameMax;
        public int DescriptionMax;
        public int SloganMax;
        public string ColorPattern = "";

        public void Write(IJsonWriter writer)
        {
            writer.TypeBegin("agora.PartyEditLimits");
            UiJson.Number(writer, "nameMax", NameMax);
            UiJson.Number(writer, "shortNameMax", ShortNameMax);
            UiJson.Number(writer, "descriptionMax", DescriptionMax);
            UiJson.Number(writer, "sloganMax", SloganMax);
            UiJson.Text(writer, "colorPattern", ColorPattern);
            writer.TypeEnd();
        }
    }

    /// <summary>
    /// A party's relationship to the sitting government, as one word. Derived from
    /// <c>PoliticalState.Government</c> in the projection: the engine has no such field, and
    /// <c>Party.IsIncumbent</c> / <c>IsInGovernment</c> between them cannot distinguish opposition
    /// from "not in the chamber at all" (<c>Parties.cs:165-168</c>).
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
    /// own header.
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

        // Nullable although Party.FoundedDate is not: the empty payload has to write "" rather than
        // a zero date, or an unknown key would render as a party founded in year zero.
        public Agora.Core.Contracts.SimDate? FoundedDate;
        public Agora.Core.Contracts.SimDate? DissolvedDate;

        // Party.PredecessorPartyId and Party.SuccessorPartyId are nullable string?; the wire rule is
        // "" for an absent id, never null (contract §2), so the projection coalesces into these.
        public string PredecessorPartyId = "";
        public string SuccessorPartyId = "";
        public int RevivalCount;

        /// <summary>
        /// Party ids this one absorbed, ascending. Derived: every party whose SuccessorPartyId is this
        /// party. Empty for a brand that has absorbed nobody. It is the half of the merge story the
        /// forward pointer cannot tell — without it a party that absorbed three rivals shows nothing.
        /// </summary>
        public List<string> AbsorbedPartyIds = new List<string>();

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

            UiJson.Id(writer, "predecessorPartyId", PredecessorPartyId);
            UiJson.Id(writer, "successorPartyId", SuccessorPartyId);
            UiJson.Number(writer, "revivalCount", RevivalCount);
            UiJson.Ids(writer, "absorbedPartyIds", AbsorbedPartyIds);

            UiJson.Enum(writer, "governmentRole", GovernmentRole);
            UiJson.Ids(writer, "factionIds", FactionIds);
            writer.TypeEnd();
        }
    }

    /// <summary>
    /// One published poll's figure for one party. A flat row on purpose: a series of dates each
    /// carrying every party's shares would be a list of rows containing lists, which the payload
    /// budget (<c>docs/contracts/ui_bindings.md</c> §2) forbids.
    /// </summary>
    /// <remarks>
    /// The share is the <b>published</b> one. <c>PollResult.TrueShares</c> is the model's own answer
    /// and never crosses the bridge (contract rule 8) — a sparkline drawn from it would be the engine
    /// showing the player its own working.
    /// </remarks>
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

    /// <summary>
    /// One party's result at one past election. A flat row: seats-per-election cannot nest inside
    /// <see cref="PartyDetailPayload"/> without breaking the payload budget's one-level rule
    /// (<c>docs/contracts/ui_bindings.md</c> §2).
    /// </summary>
    /// <remarks>
    /// <see cref="WasOnBallot"/> separates <i>stood and won nothing</i> from <i>did not stand</i>. A
    /// party missing from <c>ElectionResult.PartyIdsOnBallot</c> and from its seat table contributes
    /// no row at all; a row with the flag false is the one case where the two disagree — seats
    /// recorded against a party the ballot list does not name — and the pane says so rather than
    /// silently presenting it as an ordinary contest.
    /// <para>
    /// <see cref="HasSeatRecord"/> says whether <see cref="PassedThreshold"/> means anything.
    /// <c>SeatAllocation</c> is a readonly struct, so a row built for a party that stood with no
    /// matching allocation carries <c>PassedThreshold = false</c> because nobody set it, not because
    /// the count judged the party short. Without this flag the pane cannot tell the two apart and
    /// would attribute a verdict the engine never gave.
    /// </para>
    /// </remarks>
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
        public bool HasSeatRecord;

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
            UiJson.Flag(writer, "hasSeatRecord", HasSeatRecord);
            writer.TypeEnd();
        }
    }

    /// <summary>
    /// One arrangement the chamber could form, from this party's point of view. A live view, not
    /// history: it is recomputed from current platforms, so it drifts between elections.
    /// </summary>
    /// <remarks>
    /// There is deliberately no per-partner "refusal" flag. A set that never reaches the ranking was
    /// rejected because its lead is too small or its members too far apart, and the pure ranking
    /// cannot say which without re-running the evaluation for sets it never built. Absence is the
    /// honest expression; a named refusal would be a political fact the engine does not model.
    /// </remarks>
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

    // ---------------------------------------------------------------------------- agora.seats

    /// <summary>
    /// One row of the seat chart. <c>listSeats</c> is 0 under FPTP and <c>districtSeats</c> is 0
    /// under a pure list system; the panel branches on the electoral system, never on which field
    /// happens to be zero.
    /// </summary>
    public sealed class SeatRowPayload : IJsonWritable
    {
        public string PartyId = "";
        public int Seats;
        public double SeatShare;
        public double VoteShare;
        public int DistrictSeats;
        public int ListSeats;
        public bool PassedThreshold;

        public void Write(IJsonWriter writer)
        {
            writer.TypeBegin("agora.SeatRow");
            UiJson.Id(writer, "partyId", PartyId);
            UiJson.Number(writer, "seats", Seats);
            UiJson.Number(writer, "seatShare", SeatShare);
            UiJson.Number(writer, "voteShare", VoteShare);
            UiJson.Number(writer, "districtSeats", DistrictSeats);
            UiJson.Number(writer, "listSeats", ListSeats);
            UiJson.Flag(writer, "passedThreshold", PassedThreshold);
            writer.TypeEnd();
        }
    }

    /// <summary>
    /// The sitting government. Under FPTP the winning party plus mayor is modelled as a one-member
    /// coalition, so this is populated under both systems and the panel needs one code path.
    /// </summary>
    public sealed class GovernmentSummaryPayload : IJsonWritable
    {
        public string Id = "";
        public string Status = "Negotiating";
        public string LeadPartyId = "";
        public List<string> MemberPartyIds = new List<string>();
        public List<string> OppositionPartyIds = new List<string>();
        public int Seats;
        public double SeatShare;
        public bool HasMajority;
        public double Cohesion;
        public double Stability;
        public string CollapseReason = "None";
        public Agora.Core.Contracts.SimDate? FormedDate;
        public Agora.Core.Contracts.SimDate? EndedDate;
        public int FormationAttempts;
        public string ElectionId = "";
        public List<string> MandateIds = new List<string>();

        public void Write(IJsonWriter writer)
        {
            writer.TypeBegin("agora.GovernmentSummary");
            UiJson.Id(writer, "id", Id);
            UiJson.Text(writer, "status", Status);
            UiJson.Id(writer, "leadPartyId", LeadPartyId);
            UiJson.Ids(writer, "memberPartyIds", MemberPartyIds);
            UiJson.Ids(writer, "oppositionPartyIds", OppositionPartyIds);
            UiJson.Number(writer, "seats", Seats);
            UiJson.Number(writer, "seatShare", SeatShare);
            UiJson.Flag(writer, "hasMajority", HasMajority);
            UiJson.Number(writer, "cohesion", Cohesion);
            UiJson.Number(writer, "stability", Stability);
            UiJson.Text(writer, "collapseReason", CollapseReason);
            UiJson.Date(writer, "formedDate", FormedDate);
            UiJson.Date(writer, "endedDate", EndedDate);
            UiJson.Number(writer, "formationAttempts", FormationAttempts);
            UiJson.Id(writer, "electionId", ElectionId);
            UiJson.Ids(writer, "mandateIds", MandateIds);
            writer.TypeEnd();
        }
    }

    /// <summary>The sitting mayor. Null under a pure list system, which elects no mayor.</summary>
    public sealed class MayorSummaryPayload : IJsonWritable
    {
        public string PartyId = "";
        public string Name = "";
        public string ElectionId = "";
        public Agora.Core.Contracts.SimDate? SinceDate;
        public double Margin;
        public List<PartySharePayload> VoteShares = new List<PartySharePayload>();

        public void Write(IJsonWriter writer)
        {
            writer.TypeBegin("agora.MayorSummary");
            UiJson.Id(writer, "partyId", PartyId);
            UiJson.Text(writer, "name", Name);
            UiJson.Id(writer, "electionId", ElectionId);
            UiJson.Date(writer, "sinceDate", SinceDate);
            UiJson.Number(writer, "margin", Margin);
            UiJson.Shares(writer, "voteShares", VoteShares);
            writer.TypeEnd();
        }
    }

    /// <summary>The most recent completed election.</summary>
    public sealed class ElectionSummaryPayload : IJsonWritable
    {
        public string Id = "";
        public Agora.Core.Contracts.SimDate? Date;
        public string System = "Proportional";
        public int TermNumber;
        public bool IsSnapElection;
        public double Turnout;
        public int TotalSeats;
        public int TotalVotesCast;
        public int TotalEligibleVoters;
        public double FinalPollDeviation;
        public Agora.Core.Contracts.SimDate? NextElectionDate;
        public List<PartySharePayload> CityVoteShares = new List<PartySharePayload>();

        public void Write(IJsonWriter writer)
        {
            writer.TypeBegin("agora.ElectionSummary");
            UiJson.Id(writer, "id", Id);
            UiJson.Date(writer, "date", Date);
            UiJson.Text(writer, "system", System);
            UiJson.Number(writer, "termNumber", TermNumber);
            UiJson.Flag(writer, "isSnapElection", IsSnapElection);
            UiJson.Number(writer, "turnout", Turnout);
            UiJson.Number(writer, "totalSeats", TotalSeats);
            UiJson.Number(writer, "totalVotesCast", TotalVotesCast);
            UiJson.Number(writer, "totalEligibleVoters", TotalEligibleVoters);
            UiJson.Number(writer, "finalPollDeviation", FinalPollDeviation);
            UiJson.Date(writer, "nextElectionDate", NextElectionDate);
            UiJson.Shares(writer, "cityVoteShares", CityVoteShares);
            writer.TypeEnd();
        }
    }

    /// <summary>One row of the election history list, newest first and capped at 12.</summary>
    public sealed class ElectionHistoryRowPayload : IJsonWritable
    {
        public string Id = "";
        public Agora.Core.Contracts.SimDate? Date;
        public int TermNumber;
        public bool IsSnapElection;
        public double Turnout;
        public string WinningPartyId = "";
        public string MayorPartyId = "";
        public int TotalSeats;

        public void Write(IJsonWriter writer)
        {
            writer.TypeBegin("agora.ElectionHistoryRow");
            UiJson.Id(writer, "id", Id);
            UiJson.Date(writer, "date", Date);
            UiJson.Number(writer, "termNumber", TermNumber);
            UiJson.Flag(writer, "isSnapElection", IsSnapElection);
            UiJson.Number(writer, "turnout", Turnout);
            UiJson.Id(writer, "winningPartyId", WinningPartyId);
            UiJson.Id(writer, "mayorPartyId", MayorPartyId);
            UiJson.Number(writer, "totalSeats", TotalSeats);
            writer.TypeEnd();
        }
    }

    /// <summary>
    /// The most recently published poll.
    /// </summary>
    /// <remarks>
    /// <see cref="Shares"/> is the <i>published</i> figure. <c>PollResult.TrueShares</c> is the
    /// model's own answer and is deliberately absent from this type: putting it on the bridge would
    /// leak the result the poll is supposed to be a noisy estimate of, and the contract calls a
    /// publisher that writes it a review-blocking defect.
    /// </remarks>
    public sealed class PollSummaryPayload : IJsonWritable
    {
        public string Id = "";
        public Agora.Core.Contracts.SimDate? Date;
        public string PollsterId = "";
        public string PollsterName = "";
        public int SampleSize;
        public double MarginOfError;
        public double UndecidedShare;
        public double ProjectedTurnout;
        public int WeeksToElection = -1;
        public Agora.Core.Contracts.SimDate? ElectionDate;
        public List<PartySharePayload> Shares = new List<PartySharePayload>();

        public void Write(IJsonWriter writer)
        {
            writer.TypeBegin("agora.PollSummary");
            UiJson.Id(writer, "id", Id);
            UiJson.Date(writer, "date", Date);
            UiJson.Id(writer, "pollsterId", PollsterId);
            UiJson.Text(writer, "pollsterName", PollsterName);
            UiJson.Number(writer, "sampleSize", SampleSize);
            UiJson.Number(writer, "marginOfError", MarginOfError);
            UiJson.Number(writer, "undecidedShare", UndecidedShare);
            UiJson.Number(writer, "projectedTurnout", ProjectedTurnout);
            UiJson.Number(writer, "weeksToElection", WeeksToElection);
            UiJson.Date(writer, "electionDate", ElectionDate);
            UiJson.Shares(writer, "shares", Shares);
            writer.TypeEnd();
        }
    }

    // ---------------------------------------------------------------------------- agora.districts

    /// <summary>One district as the list view shows it.</summary>
    public sealed class DistrictBriefPayload : IJsonWritable
    {
        public string Id = "";
        public string Name = "";
        public int Population;
        public int EligibleVoters;
        public string LeadingPartyId = "";
        public double LeadingShare;
        public string RunnerUpPartyId = "";
        public double Margin;
        public double Turnout;
        public double Happiness;
        public double Discontent;
        public bool HasCityFallbacks;

        public void Write(IJsonWriter writer)
        {
            writer.TypeBegin("agora.DistrictBrief");
            UiJson.Id(writer, "id", Id);
            UiJson.Text(writer, "name", Name);
            UiJson.Number(writer, "population", Population);
            UiJson.Number(writer, "eligibleVoters", EligibleVoters);
            UiJson.Id(writer, "leadingPartyId", LeadingPartyId);
            UiJson.Number(writer, "leadingShare", LeadingShare);
            UiJson.Id(writer, "runnerUpPartyId", RunnerUpPartyId);
            UiJson.Number(writer, "margin", Margin);
            UiJson.Number(writer, "turnout", Turnout);
            UiJson.Number(writer, "happiness", Happiness);
            UiJson.Number(writer, "discontent", Discontent);
            UiJson.Flag(writer, "hasCityFallbacks", HasCityFallbacks);
            writer.TypeEnd();
        }
    }

    /// <summary>
    /// The full detail for one district, fetched per key.
    /// </summary>
    /// <remarks>
    /// <see cref="HasCityFallbacks"/> is a rendering obligation, not decoration: when it is true,
    /// every property named in <see cref="CityFallbackFields"/> is a city-wide number wearing this
    /// district's name, and the panel must mark it rather than present it as a local fact.
    /// </remarks>
    public sealed class DistrictDetailPayload : IJsonWritable
    {
        public string Id = "";
        public string Name = "";
        public int Population;
        public int Households;
        public int EligibleVoters;
        public int VotesCast;
        public double Turnout;
        public double Happiness;
        public double Unemployment;
        public string WinningPartyId = "";
        public double Margin;
        public int Seats;
        public bool DecidedByTieBreak;
        public List<PartySharePayload> Shares = new List<PartySharePayload>();

        public double WealthLow, WealthMiddle, WealthHigh;
        public double EduUneducated, EduPoorly, EduEducated, EduWell, EduHighly;
        public double AgeChild, AgeTeen, AgeAdult, AgeElderly;
        public double IdxGentrification, IdxCommuteMisery, IdxServiceCoverage, IdxDiscontent, IdxGini;

        /// <summary>
        /// The household ledger, copied from <c>DistrictSnapshot</c> without deriving anything.
        /// </summary>
        /// <remarks>
        /// Rent is per rent period; upkeep, goods and fees are per day, matching how the game bills
        /// each of them. <see cref="BudgetDisposableMargin"/> is signed and uncapped — a district
        /// drawing down savings reads negative, and the panel clamps only the meter fill, never the
        /// number.
        /// </remarks>
        public double BudgetAverageRent, BudgetRentBurden;
        public double BudgetUpkeep, BudgetResourceSpend, BudgetFees, BudgetDisposableMargin;

        public bool HasCityFallbacks;
        public List<string> CityFallbackFields = new List<string>();

        public void Write(IJsonWriter writer)
        {
            writer.TypeBegin("agora.DistrictDetail");
            UiJson.Id(writer, "id", Id);
            UiJson.Text(writer, "name", Name);
            UiJson.Number(writer, "population", Population);
            UiJson.Number(writer, "households", Households);
            UiJson.Number(writer, "eligibleVoters", EligibleVoters);
            UiJson.Number(writer, "votesCast", VotesCast);
            UiJson.Number(writer, "turnout", Turnout);
            UiJson.Number(writer, "happiness", Happiness);
            UiJson.Number(writer, "unemployment", Unemployment);
            UiJson.Id(writer, "winningPartyId", WinningPartyId);
            UiJson.Number(writer, "margin", Margin);
            UiJson.Number(writer, "seats", Seats);
            UiJson.Flag(writer, "decidedByTieBreak", DecidedByTieBreak);
            UiJson.Shares(writer, "shares", Shares);

            // One level of nesting is the contract's limit, and these named groups are it.
            writer.PropertyName("wealth");
            writer.TypeBegin("agora.WealthSplit");
            UiJson.Number(writer, "low", WealthLow);
            UiJson.Number(writer, "middle", WealthMiddle);
            UiJson.Number(writer, "high", WealthHigh);
            writer.TypeEnd();

            writer.PropertyName("education");
            writer.TypeBegin("agora.EducationSplit");
            UiJson.Number(writer, "uneducated", EduUneducated);
            UiJson.Number(writer, "poorlyEducated", EduPoorly);
            UiJson.Number(writer, "educated", EduEducated);
            UiJson.Number(writer, "wellEducated", EduWell);
            UiJson.Number(writer, "highlyEducated", EduHighly);
            writer.TypeEnd();

            writer.PropertyName("age");
            writer.TypeBegin("agora.AgeSplit");
            UiJson.Number(writer, "child", AgeChild);
            UiJson.Number(writer, "teen", AgeTeen);
            UiJson.Number(writer, "adult", AgeAdult);
            UiJson.Number(writer, "elderly", AgeElderly);
            writer.TypeEnd();

            writer.PropertyName("indices");
            writer.TypeBegin("agora.DistrictIndicesView");
            UiJson.Number(writer, "gentrification", IdxGentrification);
            UiJson.Number(writer, "commuteMisery", IdxCommuteMisery);
            UiJson.Number(writer, "serviceCoverage", IdxServiceCoverage);
            UiJson.Number(writer, "discontent", IdxDiscontent);
            UiJson.Number(writer, "gini", IdxGini);
            writer.TypeEnd();

            // Property names are the snapshot's own, camelCased, and must stay that way: they are
            // what arrives in cityFallbackFields, and the panel matches the two to decide whether a
            // cell is a local fact or a city number wearing this district's name. A rename here
            // stops a cell being dimmed and fails nowhere.
            writer.PropertyName("budget");
            writer.TypeBegin("agora.DistrictBudgetView");
            UiJson.Number(writer, "averageRent", BudgetAverageRent);
            UiJson.Number(writer, "rentBurden", BudgetRentBurden);
            UiJson.Number(writer, "averageHouseholdUpkeep", BudgetUpkeep);
            UiJson.Number(writer, "averageHouseholdResourceSpend", BudgetResourceSpend);
            UiJson.Number(writer, "averageHouseholdFees", BudgetFees);
            UiJson.Number(writer, "disposableMargin", BudgetDisposableMargin);
            writer.TypeEnd();

            UiJson.Flag(writer, "hasCityFallbacks", HasCityFallbacks);
            UiJson.Ids(writer, "cityFallbackFields", CityFallbackFields);
            writer.TypeEnd();
        }
    }

    /// <summary>
    /// One cell of a wealth × education crosstab. The engine models 60 blocs; this collapses the
    /// four age bands so 15 rows cross the bridge instead of 60.
    /// </summary>
    public sealed class CrosstabCellPayload : IJsonWritable
    {
        public string Wealth = "Low";
        public string Education = "Uneducated";
        public int Population;
        public double PopulationShare;
        public int EligibleVoters;
        public double Turnout;
        public string LeadingPartyId = "";
        public double LeadingShare;
        public double Happiness;
        public double Discontent;

        public void Write(IJsonWriter writer)
        {
            writer.TypeBegin("agora.CrosstabCell");
            UiJson.Text(writer, "wealth", Wealth);
            UiJson.Text(writer, "education", Education);
            UiJson.Number(writer, "population", Population);
            UiJson.Number(writer, "populationShare", PopulationShare);
            UiJson.Number(writer, "eligibleVoters", EligibleVoters);
            UiJson.Number(writer, "turnout", Turnout);
            UiJson.Id(writer, "leadingPartyId", LeadingPartyId);
            UiJson.Number(writer, "leadingShare", LeadingShare);
            UiJson.Number(writer, "happiness", Happiness);
            UiJson.Number(writer, "discontent", Discontent);
            writer.TypeEnd();
        }
    }

    /// <summary>City-wide derived indices, all in [0,1].</summary>
    public sealed class CityIndicesPayload : IJsonWritable
    {
        public double Gini;
        public double BrainDrain;
        public double ServiceInequality;
        public double CommuteMisery;
        public double Polarization;
        public double Legitimacy;
        public double Discontent;

        public void Write(IJsonWriter writer)
        {
            writer.TypeBegin("agora.CityIndices");
            UiJson.Number(writer, "gini", Gini);
            UiJson.Number(writer, "brainDrain", BrainDrain);
            UiJson.Number(writer, "serviceInequality", ServiceInequality);
            UiJson.Number(writer, "commuteMisery", CommuteMisery);
            UiJson.Number(writer, "polarization", Polarization);
            UiJson.Number(writer, "legitimacy", Legitimacy);
            UiJson.Number(writer, "discontent", Discontent);
            writer.TypeEnd();
        }
    }

    // ---------------------------------------------------------------------------- agora.news

    /// <summary>
    /// One feed item. Prose bodies deliberately do not ride here — the body is fetched from
    /// <c>agora.news.article</c> only when the item is opened.
    /// </summary>
    public sealed class NewsHeadlinePayload : IJsonWritable
    {
        public string Id = "";
        public Agora.Core.Contracts.SimDate? Date;
        public string Kind = "Article";
        public string Headline = "";
        public string Summary = "";
        public string OutletId = "";
        public string OutletName = "";
        public int Severity;
        public string PartyId = "";
        public string DistrictId = "";
        public string EventId = "";
        public bool HasArticle;

        public void Write(IJsonWriter writer)
        {
            writer.TypeBegin("agora.NewsHeadline");
            UiJson.Id(writer, "id", Id);
            UiJson.Date(writer, "date", Date);
            UiJson.Text(writer, "kind", Kind);
            UiJson.Text(writer, "headline", Headline);
            UiJson.Text(writer, "summary", Summary);
            UiJson.Id(writer, "outletId", OutletId);
            UiJson.Text(writer, "outletName", OutletName);
            UiJson.Number(writer, "severity", Severity);
            UiJson.Id(writer, "partyId", PartyId);
            UiJson.Id(writer, "districtId", DistrictId);
            UiJson.Id(writer, "eventId", EventId);
            UiJson.Flag(writer, "hasArticle", HasArticle);
            writer.TypeEnd();
        }
    }

    /// <summary>
    /// One queued interruption: a pointer at a feed row that already exists, plus enough to render a
    /// masthead card without a second round trip.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Sibling of <see cref="NewsHeadlinePayload"/> and kept next to it deliberately — the two are
    /// read side by side, and the field order below mirrors it so a drift between them is visible
    /// without scrolling. What it adds is <c>major</c>; what it drops is <c>outletId</c>, which no
    /// card renders.
    /// </para>
    /// <para>
    /// <b><c>major</c> is published rather than recomputed in TypeScript from <c>severity</c>.</b>
    /// Whether an item is grave enough to hold the clock is an engine judgement, not a rendering
    /// decision (<c>docs/contracts/ui_bindings.md</c> §7 rule 5), and the threshold it is decided
    /// against lives in <c>EngineTuning</c> — a number the dashboard must not learn, because a copy
    /// of it in the UI is a second definition of "major" that drifts on the next tuning pass. The
    /// UI never compares a severity to a number; it reads this flag.
    /// </para>
    /// </remarks>
    public sealed class NewsAlertPayload : IJsonWritable
    {
        public string Id = "";
        public string Kind = "Article";
        public Agora.Core.Contracts.SimDate? Date;
        public string Headline = "";
        public string Summary = "";
        public string OutletName = "";
        public string PartyId = "";
        public string DistrictId = "";
        public string EventId = "";
        public int Severity;
        public bool Major;
        public bool HasArticle;

        public void Write(IJsonWriter writer)
        {
            writer.TypeBegin("agora.NewsAlert");
            UiJson.Id(writer, "id", Id);
            UiJson.Text(writer, "kind", Kind);
            UiJson.Date(writer, "date", Date);
            UiJson.Text(writer, "headline", Headline);
            UiJson.Text(writer, "summary", Summary);
            UiJson.Text(writer, "outletName", OutletName);
            UiJson.Id(writer, "partyId", PartyId);
            UiJson.Id(writer, "districtId", DistrictId);
            UiJson.Id(writer, "eventId", EventId);
            UiJson.Number(writer, "severity", Severity);
            UiJson.Flag(writer, "major", Major);
            UiJson.Flag(writer, "hasArticle", HasArticle);
            writer.TypeEnd();
        }
    }

    /// <summary>A full article body, fetched per key. Every field is flavor; parse none of it.</summary>
    public sealed class NewsArticlePayload : IJsonWritable
    {
        public string Id = "";
        public Agora.Core.Contracts.SimDate? Date;
        public string Headline = "";
        public string Body = "";
        public string Tone = "";
        public string OutletId = "";
        public string OutletName = "";
        public string PartyId = "";
        public string DistrictId = "";
        public string EventId = "";

        public void Write(IJsonWriter writer)
        {
            writer.TypeBegin("agora.NewsArticle");
            UiJson.Id(writer, "id", Id);
            UiJson.Date(writer, "date", Date);
            UiJson.Text(writer, "headline", Headline);
            UiJson.Text(writer, "body", Body);
            UiJson.Text(writer, "tone", Tone);
            UiJson.Id(writer, "outletId", OutletId);
            UiJson.Text(writer, "outletName", OutletName);
            UiJson.Id(writer, "partyId", PartyId);
            UiJson.Id(writer, "districtId", DistrictId);
            UiJson.Id(writer, "eventId", EventId);
            writer.TypeEnd();
        }
    }

    /// <summary>One live timeline event.</summary>
    public sealed class TimelineEventBriefPayload : IJsonWritable
    {
        public string Id = "";
        public Agora.Core.Contracts.SimDate? Date;
        public string Title = "";
        public string Region = "Global";
        public string Origin = "Catalog";
        public int Severity;
        public int DurationMonths;
        public Agora.Core.Contracts.SimDate? FiredDate;
        public Agora.Core.Contracts.SimDate? ExpiresDate;
        public string ArchetypeId = "";
        public string LocalAngle = "";
        public List<string> Tags = new List<string>();
        public List<string> DistrictIds = new List<string>();

        public void Write(IJsonWriter writer)
        {
            writer.TypeBegin("agora.TimelineEventBrief");
            UiJson.Id(writer, "id", Id);
            UiJson.Date(writer, "date", Date);
            UiJson.Text(writer, "title", Title);
            UiJson.Text(writer, "region", Region);
            UiJson.Text(writer, "origin", Origin);
            UiJson.Number(writer, "severity", Severity);
            UiJson.Number(writer, "durationMonths", DurationMonths);
            UiJson.Date(writer, "firedDate", FiredDate);
            UiJson.Date(writer, "expiresDate", ExpiresDate);
            UiJson.Id(writer, "archetypeId", ArchetypeId);
            UiJson.Text(writer, "localAngle", LocalAngle);
            UiJson.Ids(writer, "tags", Tags);
            UiJson.Ids(writer, "districtIds", DistrictIds);
            writer.TypeEnd();
        }
    }

    /// <summary>
    /// One promise in the mandate tracker. A row with <see cref="IsMeasurementStalled"/> set is
    /// <i>held</i>, not failing: its metric is currently unreadable, it accrues no progress, and the
    /// clock cannot defy it.
    /// </summary>
    public sealed class MandateRowPayload : IJsonWritable
    {
        public string Id = "";
        public string PartyId = "";
        public string CoalitionId = "";
        public string DistrictId = "";
        public string Issue = "Services";
        public string Metric = "Happiness";
        public string Direction = "Increase";
        public double BaselineValue;
        public double TargetValue;
        public double CurrentValue;
        public double Progress;
        public Agora.Core.Contracts.SimDate? IssuedDate;
        public Agora.Core.Contracts.SimDate? DeadlineDate;
        public Agora.Core.Contracts.SimDate? ResolvedDate;
        public string Status = "Pending";
        public double Salience;
        public string Text = "";
        public bool IsMeasurementStalled;
        public int MonthsRemaining;

        public void Write(IJsonWriter writer)
        {
            writer.TypeBegin("agora.MandateRow");
            UiJson.Id(writer, "id", Id);
            UiJson.Id(writer, "partyId", PartyId);
            UiJson.Id(writer, "coalitionId", CoalitionId);
            UiJson.Id(writer, "districtId", DistrictId);
            UiJson.Text(writer, "issue", Issue);
            UiJson.Text(writer, "metric", Metric);
            UiJson.Text(writer, "direction", Direction);
            UiJson.Number(writer, "baselineValue", BaselineValue);
            UiJson.Number(writer, "targetValue", TargetValue);
            UiJson.Number(writer, "currentValue", CurrentValue);
            UiJson.Number(writer, "progress", Progress);
            UiJson.Date(writer, "issuedDate", IssuedDate);
            UiJson.Date(writer, "deadlineDate", DeadlineDate);
            UiJson.Date(writer, "resolvedDate", ResolvedDate);
            UiJson.Text(writer, "status", Status);
            UiJson.Number(writer, "salience", Salience);
            UiJson.Text(writer, "text", Text);
            UiJson.Flag(writer, "isMeasurementStalled", IsMeasurementStalled);
            UiJson.Number(writer, "monthsRemaining", MonthsRemaining);
            writer.TypeEnd();
        }
    }

    /// <summary>
    /// LLM health for the status line. <see cref="LastError"/> is an engine-authored short code from
    /// a fixed vocabulary — never model output, never a raw exception message, because the panel
    /// switches on it.
    /// </summary>
    public sealed class FlavorStatusPayload : IJsonWritable
    {
        public Agora.Core.Contracts.SimDate? LastFlavorDate;
        public Agora.Core.Contracts.SimDate? LastAttemptDate;
        public bool IsStale;
        public bool ProviderAvailable;
        public bool PendingWake;
        public string LastError = "";
        public int ArticleCount;

        public void Write(IJsonWriter writer)
        {
            writer.TypeBegin("agora.FlavorStatus");
            UiJson.Date(writer, "lastFlavorDate", LastFlavorDate);
            UiJson.Date(writer, "lastAttemptDate", LastAttemptDate);
            UiJson.Flag(writer, "isStale", IsStale);
            UiJson.Flag(writer, "providerAvailable", ProviderAvailable);
            UiJson.Flag(writer, "pendingWake", PendingWake);
            UiJson.Text(writer, "lastError", LastError);
            UiJson.Number(writer, "articleCount", ArticleCount);
            writer.TypeEnd();
        }
    }

    // ---------------------------------------------------------------------------- agora.stories

    /// <summary>
    /// One event inside a story: what it is, how the player answered it, and how it came out.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b><see cref="Tier"/> is the engine's verdict and the panel may not recompute it.</b> Mandatory
    /// / Major / Minor is a projection of the 1–5 <see cref="Severity"/> through
    /// <c>stories.mandatorySeverityThreshold</c> and <c>stories.majorSeverityThreshold</c>, and
    /// <c>docs/contracts/ui_bindings.md</c> §4.5 already states in bold that the UI must never
    /// re-derive a tier from a severity. <see cref="Severity"/> ships alongside for display only.
    /// </para>
    /// <para>
    /// <see cref="OverrideCost"/> and <see cref="CanAfford"/> are likewise the engine's, priced from
    /// this slot's own tier against the live balance. With <c>power.enabled</c> off the cost is
    /// published as <b>0</b> and <see cref="CanAfford"/> as false — quoting a live price against a
    /// balance that cannot move would invite the player to save up for a purchase the engine will
    /// refuse with <c>PowerDisabled</c> whatever they do.
    /// </para>
    /// </remarks>
    public sealed class StorySlotPayload : IJsonWritable
    {
        public string EventId = "";

        /// <summary><c>"Major"</c> or <c>"Minor"</c> — <c>SlotRole</c>'s member name.</summary>
        public string Role = "Minor";

        /// <summary>The catalog's authored name. Never an id — a raw id on screen is a defect.</summary>
        public string Name = "";

        public string Description = "";

        /// <summary>The four response blurbs, authored in the civic catalog.</summary>
        public string IgnoreText = "";
        public string GoalText = "";
        public string PowerOverrideText = "";

        /// <summary>
        /// The authored aftermath lines. Only one of the two is true once the slot resolves; both
        /// ship so the card can show the stakes before it does.
        /// </summary>
        public string SuccessText = "";
        public string FailText = "";

        /// <summary>The catalog's 1–5 integer. Display only — see the remark on tier.</summary>
        public int Severity;

        /// <summary><c>"Mandatory"</c>, <c>"Major"</c> or <c>"Minor"</c>. The engine's, not the UI's.</summary>
        public string Tier = "Minor";

        /// <summary>
        /// <c>SlotResponse</c>'s member name. <c>"Unaddressed"</c> is silence and <c>"Ignore"</c> is a
        /// decision; they score the same and read completely differently, so do not merge them.
        /// </summary>
        public string Response = "Unaddressed";

        /// <summary>The player's own words, for Ignore and Manual. Prose — never parse it.</summary>
        public string PlayerText = "";

        /// <summary><c>SlotOutcome</c>'s member name: Pending, Met, NotMet or Unmeasurable.</summary>
        public string Outcome = "Pending";

        /// <summary>True once a Manual slot's outcome has been declared.</summary>
        public bool ManualDeclared;

        /// <summary>Price of buying this slot off, or 0 when the power layer is off.</summary>
        public int OverrideCost;

        /// <summary>Whether the current balance covers <see cref="OverrideCost"/>.</summary>
        public bool CanAfford;

        public void Write(IJsonWriter writer)
        {
            writer.TypeBegin("agora.StorySlot");
            UiJson.Id(writer, "eventId", EventId);
            UiJson.Text(writer, "role", Role);
            UiJson.Text(writer, "name", Name);
            UiJson.Text(writer, "description", Description);
            UiJson.Text(writer, "ignoreText", IgnoreText);
            UiJson.Text(writer, "goalText", GoalText);
            UiJson.Text(writer, "powerOverrideText", PowerOverrideText);
            UiJson.Text(writer, "successText", SuccessText);
            UiJson.Text(writer, "failText", FailText);
            UiJson.Number(writer, "severity", Severity);
            UiJson.Text(writer, "tier", Tier);
            UiJson.Text(writer, "response", Response);
            UiJson.Text(writer, "playerText", PlayerText);
            UiJson.Text(writer, "outcome", Outcome);
            UiJson.Flag(writer, "manualDeclared", ManualDeclared);
            UiJson.Number(writer, "overrideCost", OverrideCost);
            UiJson.Flag(writer, "canAfford", CanAfford);
            writer.TypeEnd();
        }
    }

    /// <summary>
    /// One live story: its slots, its clock and the headline that names it.
    /// </summary>
    /// <remarks>
    /// The bodies are deliberately absent. A story's two articles run to 1260 characters each and
    /// exist in up to two voices, so shipping them on the list binding would push the largest thing
    /// on this bridge across it every republish to render one of them. Fetch them per story from
    /// <c>agora.stories.article</c>, exactly as the news feed fetches an article body.
    /// </remarks>
    public sealed class StoryPayload : IJsonWritable
    {
        public string Id = "";

        public Agora.Core.Contracts.SimDate? OpenedDate;

        /// <summary>The month it resolves on by itself, absent a <c>Resolve now</c>.</summary>
        public Agora.Core.Contracts.SimDate? ResolvesDate;

        public bool IsMandatory;

        /// <summary><c>StoryOutcome</c>'s member name: Pending, Success, Failure or Abandoned.</summary>
        public string Outcome = "Pending";

        /// <summary>True once the player has asked for an early close and it has not yet run.</summary>
        public bool ResolveEarlyRequested;

        /// <summary>
        /// The headline to put on the card. The pool's, which always exists — the model's version, if
        /// one arrived, is fetched with the body and rendered beside it rather than instead of it.
        /// </summary>
        public string Headline = "";

        /// <summary>
        /// The slots, in the engine's order: <b>major first, then minors ascending by event id
        /// ordinal.</b> A declared total order — do not re-sort it in the panel.
        /// </summary>
        public List<StorySlotPayload> Slots = new List<StorySlotPayload>();

        public void Write(IJsonWriter writer)
        {
            writer.TypeBegin("agora.Story");
            UiJson.Id(writer, "id", Id);
            UiJson.Date(writer, "openedDate", OpenedDate);
            UiJson.Date(writer, "resolvesDate", ResolvesDate);
            UiJson.Flag(writer, "isMandatory", IsMandatory);
            UiJson.Text(writer, "outcome", Outcome);
            UiJson.Flag(writer, "resolveEarlyRequested", ResolveEarlyRequested);
            UiJson.Text(writer, "headline", Headline);

            writer.PropertyName("slots");
            List<StorySlotPayload> slots = Slots ?? new List<StorySlotPayload>();
            writer.ArrayBegin((uint)slots.Count);
            for (int i = 0; i < slots.Count; i++) slots[i].Write(writer);
            writer.ArrayEnd();

            writer.TypeEnd();
        }
    }

    /// <summary>One archived story, as a row: enough to list it and to fetch its articles again.</summary>
    public sealed class StoryBriefPayload : IJsonWritable
    {
        public string Id = "";
        public Agora.Core.Contracts.SimDate? OpenedDate;
        public Agora.Core.Contracts.SimDate? ResolvesDate;
        public string Outcome = "Pending";
        public string Headline = "";

        /// <summary>Slots that resolved <c>Met</c>, and how many were scored at all.</summary>
        /// <remarks>
        /// <see cref="ScoredCount"/> excludes <c>Unmeasurable</c> slots from both halves of the ratio,
        /// which is the same exclusion the 2-of-3 rule makes. A card reading "1 of 2" on a
        /// three-slot story is therefore correct rather than a missing slot, and the panel must not
        /// substitute <see cref="SlotCount"/> to make the arithmetic look tidier.
        /// </remarks>
        public int MetCount;
        public int ScoredCount;
        public int SlotCount;

        public void Write(IJsonWriter writer)
        {
            writer.TypeBegin("agora.StoryBrief");
            UiJson.Id(writer, "id", Id);
            UiJson.Date(writer, "openedDate", OpenedDate);
            UiJson.Date(writer, "resolvesDate", ResolvesDate);
            UiJson.Text(writer, "outcome", Outcome);
            UiJson.Text(writer, "headline", Headline);
            UiJson.Number(writer, "metCount", MetCount);
            UiJson.Number(writer, "scoredCount", ScoredCount);
            UiJson.Number(writer, "slotCount", SlotCount);
            writer.TypeEnd();
        }
    }

    /// <summary>
    /// A story's prose, fetched per story id. Every field is flavor; parse none of it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Two voices, and both are rendered when both exist</b> — owner decision 2 of wave 5, not an
    /// implementation detail. The canned pool answers every poll and always has an answer; the CLI
    /// answers rarely. A "latest wins" read would erase the model's prose within a minute of it
    /// arriving, and worse, would change text the player had already read. The pool half is therefore
    /// the text that is always shown, and the CLI half is shown <i>beside</i> it when present.
    /// </para>
    /// <para>
    /// <see cref="CliHeadline"/> / <see cref="CliArticle"/> are <c>""</c> when the model has not
    /// written about this story — which is the ordinary case, not an error.
    /// </para>
    /// </remarks>
    public sealed class StoryArticlePayload : IJsonWritable
    {
        public string StoryId = "";

        /// <summary>The opening article, always present once the story has drafted.</summary>
        public string PoolHeadline = "";
        public string PoolArticle = "";
        public string CliHeadline = "";
        public string CliArticle = "";

        /// <summary>
        /// The resolution article. All four are <c>""</c> until the story resolves.
        /// </summary>
        public string PoolResolutionHeadline = "";
        public string PoolResolutionArticle = "";
        public string CliResolutionHeadline = "";
        public string CliResolutionArticle = "";

        public void Write(IJsonWriter writer)
        {
            writer.TypeBegin("agora.StoryArticle");
            UiJson.Id(writer, "storyId", StoryId);
            UiJson.Text(writer, "poolHeadline", PoolHeadline);
            UiJson.Text(writer, "poolArticle", PoolArticle);
            UiJson.Text(writer, "cliHeadline", CliHeadline);
            UiJson.Text(writer, "cliArticle", CliArticle);
            UiJson.Text(writer, "poolResolutionHeadline", PoolResolutionHeadline);
            UiJson.Text(writer, "poolResolutionArticle", PoolResolutionArticle);
            UiJson.Text(writer, "cliResolutionHeadline", CliResolutionHeadline);
            UiJson.Text(writer, "cliResolutionArticle", CliResolutionArticle);
            writer.TypeEnd();
        }
    }

    /// <summary>One movement of the political-power balance, for the ledger strip.</summary>
    public sealed class PowerLedgerRowPayload : IJsonWritable
    {
        /// <summary>Month of the movement, as <c>SimDate.TotalMonths</c>.</summary>
        public int Month;

        /// <summary>Ordinal within the month. <c>(month, sequence)</c> is the sort key.</summary>
        public int Sequence;

        /// <summary>
        /// <c>PowerLedgerReason</c>'s member name: Accrual, SuccessAward, FailurePenalty,
        /// OverrideSpend or ManualAward.
        /// </summary>
        public string Reason = "Accrual";

        /// <summary>Signed. Negative for a spend or a penalty.</summary>
        public int Delta;

        public string StoryId = "";
        public string EventId = "";

        public void Write(IJsonWriter writer)
        {
            writer.TypeBegin("agora.PowerLedgerRow");
            UiJson.Number(writer, "month", Month);
            UiJson.Number(writer, "sequence", Sequence);
            UiJson.Text(writer, "reason", Reason);
            UiJson.Number(writer, "delta", Delta);
            UiJson.Id(writer, "storyId", StoryId);
            UiJson.Id(writer, "eventId", EventId);
            writer.TypeEnd();
        }
    }

    /// <summary>The political-power currency, for the counter beside the mod icon.</summary>
    /// <remarks>
    /// <see cref="Balance"/> is signed and debt is a state rather than a refusal. <see cref="InDebt"/>
    /// ships as the engine's own reading rather than leaving the counter to test <c>balance &lt; 0</c>,
    /// because what counts as debt is the engine's rule and the consequence attached to it is a
    /// capped, tuned effect — not a sign test the UI happens to agree with today.
    /// </remarks>
    public sealed class PowerPayload : IJsonWritable
    {
        /// <summary>False when the save has the power layer switched off. The counter hides.</summary>
        public bool Enabled;

        public int Balance;
        public int LifetimeEarned;
        public int LifetimeSpent;
        public bool InDebt;

        /// <summary>
        /// Newest last, bounded by <c>power.ledgerRetention</c>, sorted by <c>(month, sequence)</c>.
        /// </summary>
        public List<PowerLedgerRowPayload> Ledger = new List<PowerLedgerRowPayload>();

        public void Write(IJsonWriter writer)
        {
            writer.TypeBegin("agora.Power");
            UiJson.Flag(writer, "enabled", Enabled);
            UiJson.Number(writer, "balance", Balance);
            UiJson.Number(writer, "lifetimeEarned", LifetimeEarned);
            UiJson.Number(writer, "lifetimeSpent", LifetimeSpent);
            UiJson.Flag(writer, "inDebt", InDebt);

            writer.PropertyName("ledger");
            List<PowerLedgerRowPayload> rows = Ledger ?? new List<PowerLedgerRowPayload>();
            writer.ArrayBegin((uint)rows.Count);
            for (int i = 0; i < rows.Count; i++) rows[i].Write(writer);
            writer.ArrayEnd();

            writer.TypeEnd();
        }
    }

    /// <summary>
    /// One story card the player has not answered yet.
    /// </summary>
    /// <remarks>
    /// <b>One card per story, never one per event.</b> All three events render inside this one card,
    /// which is why the payload carries a <see cref="SlotCount"/> rather than an event id: at two
    /// stories per cycle that is two interruptions, and six would be six serialised forced pauses on
    /// the first frame of the month.
    /// </remarks>
    public sealed class StoryAlertPayload : IJsonWritable
    {
        /// <summary>The story id, and the ack key. Also the <c>agora.stories.article</c> map key.</summary>
        public string Id = "";

        public Agora.Core.Contracts.SimDate? Date;

        public string Headline = "";

        /// <summary>One line — the major event's description.</summary>
        public string Summary = "";

        public int SlotCount;

        /// <summary>
        /// The engine's verdict on whether this card holds the clock, decided once when the alert is
        /// raised. The UI never compares a severity to a threshold of its own.
        /// </summary>
        public bool Major;

        public void Write(IJsonWriter writer)
        {
            writer.TypeBegin("agora.StoryAlert");
            UiJson.Id(writer, "id", Id);
            UiJson.Date(writer, "date", Date);
            UiJson.Text(writer, "headline", Headline);
            UiJson.Text(writer, "summary", Summary);
            UiJson.Number(writer, "slotCount", SlotCount);
            UiJson.Flag(writer, "major", Major);
            writer.TypeEnd();
        }
    }
}
