using System;

namespace Agora.Mod.Llm
{
    /// <summary>
    /// The canned word pools <see cref="StaticPoolProvider"/> draws from.
    ///
    /// <para>
    /// <b>Content, not tuning.</b> Nothing here is a coefficient - no value below can change a vote
    /// share, a seat count or an index, because every one of them ends up as a string in a prose
    /// field. So these do not belong in <c>engine_tuning.json</c>, whose entire contents are numbers
    /// the engine computes with.
    /// </para>
    ///
    /// <para>
    /// <b>AGORA-SEAM(seeds):</b> <c>data/seeds/README.md</c> plans <c>party_names_eu.json</c>,
    /// <c>party_names_na.json</c>, <c>faction_archetypes.json</c> and <c>outlets.json</c> for M3, and
    /// those files do not exist yet. Rather than invent their on-disk shape from inside the Llm
    /// packet and have the M3 author find it already decided, the pools are compiled in here and
    /// <see cref="StaticPoolProvider"/> reads them through this one type - so swapping in a file
    /// loader later is a change to this class and nothing else.
    /// </para>
    ///
    /// <para>English only (non-negotiable #10).</para>
    /// </summary>
    public static class StaticPoolContent
    {
        // ---- party naming ----------------------------------------------------------------------

        /// <summary>Proportional systems: coalition-era European party naming.</summary>
        public static readonly string[] EuPartyAdjectives =
        {
            "Civic", "Social", "Green", "Liberal", "Progressive", "Democratic", "United", "New",
            "People's", "Free", "Common", "Popular", "Independent", "Reform"
        };

        public static readonly string[] EuPartyNouns =
        {
            "Alliance", "Union", "Front", "Movement", "Party", "List", "Forum", "Assembly",
            "Coalition", "League", "Platform", "Circle"
        };

        /// <summary>First-past-the-post systems: broad-church, two-dominant-party naming.</summary>
        public static readonly string[] NaPartyAdjectives =
        {
            "Municipal", "Civic", "Metropolitan", "Working", "Homeowners'", "Neighbourhood",
            "Taxpayers'", "Community", "Riverside", "Northside", "Downtown", "Common Ground"
        };

        public static readonly string[] NaPartyNouns =
        {
            "Ticket", "Slate", "Caucus", "Association", "Party", "Committee", "Coalition", "Bloc"
        };

        /// <summary>Faction names sit inside a party, so they lean personal and factional.</summary>
        public static readonly string[] FactionAdjectives =
        {
            "Old Guard", "Young", "Practical", "Principled", "Grassroots", "Business", "Labour",
            "Reformist", "Traditional", "Insurgent"
        };

        public static readonly string[] FactionNouns =
        {
            "Wing", "Caucus", "Tendency", "Group", "Bloc", "Committee", "Network"
        };

        /// <summary>Leader names. Given and family names are drawn independently.</summary>
        public static readonly string[] LeaderGivenNames =
        {
            "Alex", "Marta", "Daniel", "Priya", "Tomas", "Ines", "Jonas", "Nadia", "Peter", "Ruth",
            "Samir", "Clara", "Viktor", "Helen", "Omar", "Britta", "Lucas", "Amara", "Niels", "Zofia"
        };

        public static readonly string[] LeaderFamilyNames =
        {
            "Vance", "Okonkwo", "Lindqvist", "Marchetti", "Haverson", "Duarte", "Novak", "Bright",
            "Abadi", "Kelleher", "Sorensen", "Pashkov", "Weller", "Adeyemi", "Halloran", "Tanaka",
            "Bergstrom", "Rasmussen", "Ferreira", "Castellan"
        };

        // ---- what a party is about -------------------------------------------------------------

        /// <summary>
        /// One line per <c>Issue</c>, in <c>Issues.All</c> order: Services, CostOfLiving, Environment,
        /// Transit, Growth, HeritageOrder. Indexed by the enum's integer value, so the order is
        /// contractual - see <c>Contracts/Issues.cs</c>.
        /// </summary>
        public static readonly string[] IssueDescriptions =
        {
            "the people who queue for a doctor, a school place or a bin collection",
            "households watching the rent take a bigger bite every year",
            "residents who want clean air and water more than they want another warehouse",
            "everyone who spends an hour a day getting to work and wants that hour back",
            "the builders, the newcomers and anyone who thinks the city should be bigger",
            "neighbours who like their street the way it is and want order kept"
        };

        /// <summary>Slogans, one per issue, same order.</summary>
        public static readonly string[] IssueSlogans =
        {
            "Services that show up.",
            "A city you can afford to live in.",
            "Clean air is not a luxury.",
            "Get the city moving.",
            "Build it, and build it here.",
            "Keep what works."
        };

        /// <summary>Short names, one per issue, same order. Twelve characters at most (schema).</summary>
        public static readonly string[] IssueShortNames =
        {
            "Services", "Cost", "Green", "Transit", "Growth", "Order"
        };

        // ---- the press -------------------------------------------------------------------------

        public static readonly string[] Outlets =
        {
            "The City Register", "Riverside Echo", "Municipal Review", "The Evening Ledger",
            "Civic Weekly", "The Tribune", "Northgate Herald", "The Daily Standard",
            "Council Watch", "The Broadsheet"
        };

        /// <summary>Must match the schema's <c>tone</c> enum exactly.</summary>
        public static readonly string[] Tones =
        {
            "neutral", "supportive", "critical", "alarmed", "celebratory"
        };

        /// <summary>
        /// Tones that fit each mood band, indexed 0 (furious) to 4 (delighted) to match
        /// <c>FlavorPromptBuilder.HappinessBandIndex</c>.
        /// </summary>
        /// <remarks>
        /// Drawing a tone freely produced a celebratory article about a grumbling city, which reads
        /// as a bug rather than as editorial range. Every row still holds more than one option, so
        /// the press is not uniform - it is just not deranged.
        /// </remarks>
        public static readonly string[][] TonesByMood =
        {
            new[] { "alarmed", "critical", "critical" },
            new[] { "critical", "alarmed", "neutral" },
            new[] { "neutral", "critical", "neutral", "supportive" },
            new[] { "neutral", "supportive", "supportive" },
            new[] { "supportive", "celebratory", "neutral" }
        };

        /// <summary>
        /// City-wide headlines. <c>{mood}</c> is replaced with a qualitative word - never a figure.
        /// </summary>
        public static readonly string[] CityHeadlines =
        {
            "Council leaves the chamber to a city that is {mood}",
            "Another year on, and the mood in the city is {mood}",
            "The view from the ward halls: {mood}",
            "Between the budget and the pavement, a city {mood}",
            "What the council heard this year, and what it did about it",
            "Quiet week at City Hall, louder one on the doorstep"
        };

        public static readonly string[] CityBodies =
        {
            "The council rose this week to the same argument it has been having since the spring. " +
            "Nobody in the chamber disputes the direction; everybody disputes the pace. Outside it, " +
            "residents describe a city that is {mood} - which is not the same as a city that is settled.",

            "Officials point to the plans on the table. Residents point to the street outside. Both " +
            "are describing the same city, and the gap between the two accounts is the story of the " +
            "year. The mood, by most accounts, is {mood}.",

            "It has been a year of small decisions rather than large ones, and the cumulative effect " +
            "is easier to feel than to name. Ask around and you get the same answer in different " +
            "words: the city is {mood}, and waiting to see whether that holds.",

            "There is a version of this city in the council minutes and a version of it on the number " +
            "seven bus, and this week they were further apart than usual. The mood on the bus is {mood}."
        };

        /// <summary>District-level headlines. <c>{district}</c> is the player's own district name.</summary>
        public static readonly string[] DistrictHeadlines =
        {
            "{district} says it has been waiting long enough",
            "In {district}, the argument is about the basics",
            "What {district} wants from the next council",
            "{district}: a neighbourhood taking stock",
            "The long complaint from {district}"
        };

        public static readonly string[] DistrictBodies =
        {
            "Residents of {district} have spent the year making the same case to anyone who will hear " +
            "it. The council says it is listening. The neighbourhood says it has heard that before, " +
            "and would like to see it instead.",

            "Walk {district} on a weekday morning and the argument makes itself. What people there " +
            "want is not complicated, and that is precisely what makes the delay so hard to explain.",

            "{district} is not the loudest part of the city, which may be why it has waited longest. " +
            "That patience is thinner this year than last."
        };

        /// <summary>Prose for an event with no model-written local angle.</summary>
        public static readonly string[] EventAngles =
        {
            "The story arrived here the way most do - late, second-hand, and immediately about " +
            "something local instead. Within a week the council was being asked what it meant for " +
            "this city specifically, and had no ready answer.",

            "Elsewhere it was a headline. Here it was a conversation on the doorstep, and then a line " +
            "in a budget, and then an argument about priorities that had been waiting for a reason.",

            "It landed on a city already arguing about something else, and it did not so much change " +
            "the argument as give both sides a fresh way to make it."
        };

        /// <summary>Deterministic pick: index derived from the caller's seeded stream, never from a hash.</summary>
        public static string Pick(string[] pool, Agora.Core.Determinism.DeterministicRng rng)
        {
            if (pool == null || pool.Length == 0) return string.Empty;
            if (rng == null) return pool[0];
            return pool[rng.NextInt(0, pool.Length)];
        }
    }
}
