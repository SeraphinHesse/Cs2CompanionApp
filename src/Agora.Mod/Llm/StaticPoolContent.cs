// Compiled into BOTH Agora.Mod and (by <Compile Link>) tests/Agora.Core.Tests: it must stay free of
// every Game.*, Unity.* and Colossal.* type. #nullable disable keeps it warning-clean in the test
// project, which enables nullable, without annotating a file the mod compiles unannotated.
#nullable disable

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

        // ---- house rules for every template below ------------------------------------------------
        //
        // These are the fallback for the fallback, so they are held to the same four rules the prompt
        // imposes on the model (FlavorPromptBuilder.AppendTask):
        //
        // 1. Lead with what happened, to whom, and why it matters. The concrete thing goes in the
        //    first sentence, not the last.
        // 2. Name a subject - the party or the district the article refs. Every city and election
        //    template carries {party}; every district template carries {district}; the generic pool
        //    carries neither and is only reached when a substituted line cannot fit its cap.
        // 3. No unattributed sourcing. Not "residents say", "officials say", "critics say", "sources
        //    say", "some argue", "many feel", nor any variant. Attribute to the named party or
        //    district, or do not attribute at all. StaticPoolPressTests asserts this over the arrays
        //    themselves, so a template added in breach of it fails the build rather than the reader.
        // 4. Never a figure - no vote share, no seat count, no percentage, no budget number. These
        //    are not LLM output, so non-negotiable #1 does not bite, but the dashboard carries the
        //    figures and the prose does not. Qualitative bands only.
        //
        // Lengths are bounded by FlavorCacheMigration.HeadlineMaxLength / BodyMaxLength AFTER
        // substitution. Party names are drawn from the pools above, so their worst case is
        // computable and pinned by a test; district names are the player's and are not, so
        // StaticPoolProvider drops the placeholder rather than cutting a name in half.

        /// <summary>
        /// City-wide headlines. Every one names <c>{party}</c>, because the article carries that
        /// party's id in <c>refs</c> and a reference the prose never makes is one the reader cannot
        /// check. <c>{mood}</c> is a qualitative word - never a figure.
        /// </summary>
        public static readonly string[] CityHeadlines =
        {
            "{party} takes its case to the ward halls",
            "{party} tables the motion the chamber kept deferring",
            "Council rises without a vote; {party} calls the delay the story",
            "{party} puts its plan on the table before a city that is {mood}",
            "The chamber splits over {party}'s motion and adjourns",
            "{party} spends the week on the doorstep of a city that is {mood}",
            "{party} loses in committee and takes the argument to the floor",
            "Wet Thursday on the doorstep, and {party} keeps knocking"
        };

        public static readonly string[] CityBodies =
        {
            "{party} spent the week putting its plan in front of anyone who would sit still for it: " +
            "two ward halls, a committee room and a draughty church hall off the ring road. The " +
            "chamber has heard the argument before. What has changed is the temper of the city " +
            "outside it, which is {mood}, and which is not the same thing as settled.",

            "The motion {party} tabled did not reach a vote, which is its own kind of answer. It goes " +
            "back to committee next month, by which time the pavement it is about will have had " +
            "another winter. The mood in the city is {mood}, and the chamber knows it.",

            "{party} came to the chamber with a plan and left with a date for another meeting. That " +
            "is not nothing - a date is a commitment of sorts - but it is a long way from the thing " +
            "itself, and a city that is {mood} has grown used to the difference.",

            "There is a version of this city in the council minutes and a version of it on the number " +
            "seven bus. {party} spent the week on the bus. What it brought back is an argument about " +
            "the basics, and a city that is {mood} about how long the basics take.",

            "{party} has been making the same case since the spring, and this week it made it again " +
            "to a half-full committee room. Nobody in the chamber disputes the direction. The dispute " +
            "is about the pace, and the city waiting on it is {mood}.",

            "The plan {party} published this week is short, which is unusual, and specific, which is " +
            "more unusual still. It commits the council to a date. Whether the date survives contact " +
            "with the budget is next year's story; a city that is {mood} will remember either way."
        };

        /// <summary>District-level headlines. <c>{district}</c> is the player's own district name.</summary>
        public static readonly string[] DistrictHeadlines =
        {
            "{district} gets its meeting, and a date for the next one",
            "The bus route through {district} is the argument again",
            "{district} waited a year for a decision and got a review",
            "Council defers the {district} scheme to the spring",
            "What {district} asked for, and what the budget line says",
            "{district} takes its complaint to the committee in person",
            "The long complaint from {district} reaches the chamber"
        };

        public static readonly string[] DistrictBodies =
        {
            "The scheme for {district} went back to committee this week, a year after it first " +
            "arrived there. The council has a date for the next meeting. The neighbourhood has the " +
            "same pavement it had last spring, and a growing sense that the two facts are related.",

            "Walk {district} on a weekday morning and the argument makes itself: the crossing, the " +
            "bins, the bus that comes when it feels like it. None of it is complicated. That is " +
            "precisely what makes the delay so hard to explain from a committee room.",

            "{district} is not the loudest part of the city, which may be why it has waited longest. " +
            "Its case went to the chamber this week in person rather than on paper, and the patience " +
            "it was made with is thinner this year than last.",

            "The council put a line in the budget for {district} and then put the scheme behind a " +
            "review. Both things are on the record. Which one arrives first is the whole question, " +
            "and the neighbourhood has been here before.",

            "A committee room off the market square, a folder of photographs, and the case for " +
            "{district} made in the time the chair allowed. It was heard politely. Being heard " +
            "politely is what the neighbourhood has had for a year."
        };

        /// <summary>
        /// The last resort, for when a substituted line cannot fit its cap - a district name long
        /// enough to push every <c>{district}</c> template past
        /// <c>FlavorCacheMigration.HeadlineMaxLength</c>. Deliberately placeholder-free: a clean
        /// generic headline beats a specific one cut mid-word, which is the same call
        /// <c>FlavorCacheMigration</c> makes when it prunes an over-long cached article rather than
        /// trimming it. Every entry must fit its cap unsubstituted; a test pins that.
        /// </summary>
        public static readonly string[] GenericHeadlines =
        {
            "A quiet week at City Hall, a louder one on the doorstep",
            "Council defers the scheme and sets a date for the next meeting",
            "The chamber splits, adjourns, and leaves the argument where it was",
            "What the council heard this year, and what it did about it"
        };

        /// <inheritdoc cref="GenericHeadlines"/>
        public static readonly string[] GenericBodies =
        {
            "The scheme went back to committee this week, which is where it went last time. The " +
            "council has a date for the next meeting; the street has the same pavement it had in the " +
            "spring. Nobody in the chamber disputes the direction, and everybody disputes the pace.",

            "It has been a year of small decisions rather than large ones, and the cumulative effect " +
            "is easier to feel than to name. The chamber rose on a Thursday with the argument intact " +
            "and a date in the diary, which is roughly where it started.",

            "The motion did not reach a vote, which is its own kind of answer. It returns in the " +
            "spring, by which time the thing it is about will have had another winter."
        };

        // ---- the election round --------------------------------------------------------------------
        //
        // WHAT THESE TEMPLATES KNOW, AND WHAT THEY DO NOT.
        //
        // They know a party exists, what it leads on, and the city's mood band. They do NOT know who
        // won: FlavorRequest and PartyBrief carry no vote share, no seat count and no turnout
        // (deliberately - see the remarks on PartyBrief), and PartyBrief.StatusWord says who governs
        // as things stand, which on the morning after a count is the arrangement the count may have
        // just unseated. So none of the prose below asserts an outcome. The four arrays
        // fill the four slots FlavorPromptBuilder.AppendElectionCoverage asks the model for, but the
        // two reaction sets are written as a party's own claim and a party's own challenge, both of
        // which are true of a party on the morning after a count whichever way the count went.
        // Inventing a winner here would be the same defect as inventing one in the prompt.

        /// <summary>Slot (a): the result piece. The count is over; the arithmetic is not stated.</summary>
        public static readonly string[] ElectionResultHeadlines =
        {
            "The count is done, and {party} spends the morning reading it",
            "Ballots counted overnight, with {party} watching every box",
            "The result is in, and {party} calls the arithmetic close",
            "Count finished before dawn; {party} was in the hall for it",
            "The chamber does its sums again, and {party} is in the room"
        };

        /// <inheritdoc cref="ElectionResultHeadlines"/>
        public static readonly string[] ElectionResultBodies =
        {
            "The last box came in a little after three, and the count went on in a sports hall that " +
            "smelled of floor polish and wet coats. {party} had people at the table throughout. The " +
            "arithmetic is settled now, whatever is said about it, and the council that sits next " +
            "month is the one this hall decided.",

            "{party} spent the night in the hall with everyone else, watching the boxes come up the " +
            "ramp one at a time. There is no drama in a count, only arithmetic and bad coffee. The " +
            "arithmetic is finished. What it means for the chamber is the argument that starts today, " +
            "and it will run longer than the count did.",

            "The result is in. {party} has said what a party says on a morning like this, which is " +
            "that the city has spoken and that it was listening. The count itself was unremarkable - " +
            "a long night, a slow ramp, a returning officer with a cold. The consequences are next " +
            "month's business.",

            "Counting finished in the sports hall shortly before dawn, and {party} was among those " +
            "still there when it did. The figures belong to the returning officer and to the record. " +
            "What belongs to the chamber is the arrangement that follows, and nobody has settled that."
        };

        /// <summary>
        /// Slot (b): a party's own claim on the mandate, written as a claim the party makes rather
        /// than an outcome the pool asserts. A party claims the mandate the morning after a count; whether
        /// the chamber's arithmetic bears the claim out is left to the chamber and to the dashboard.
        /// </summary>
        public static readonly string[] ElectionClaimHeadlines =
        {
            "{party} says the result settles the argument it has been making",
            "{party} thanks its canvassers and claims the mandate",
            "{party} reads the result as vindication and says so early",
            "Vindication, says {party}, and the plan starts on Monday",
            "{party} calls the result a mandate and moves to spend it"
        };

        /// <inheritdoc cref="ElectionClaimHeadlines"/>
        public static readonly string[] ElectionClaimBodies =
        {
            "{party} was out early with its reading of the night: the city was asked a question and " +
            "gave the answer the party had been asking for. Whether the chamber's arithmetic bears " +
            "that out is a matter for the chamber. What is not in doubt is that the claim was made " +
            "first, in a car park, to three cameras and a local radio reporter.",

            "By breakfast {party} had thanked its canvassers, claimed the mandate and named the thing " +
            "it intends to do with it. That order matters: the claim goes out before anyone has " +
            "finished checking the arithmetic, because a claim made first is the one repeated all week.",

            "{party} says the result is an instruction rather than an opinion, and it intends to treat " +
            "it as one. The plan it has been carrying since the spring goes to the chamber next month, " +
            "where it will meet the same committee, the same budget line and the same officers who " +
            "deferred it last time."
        };

        /// <summary>
        /// Slot (c): a party's own challenge to the reading of the count, rather than a defeat the
        /// pool asserts. Same reason as
        /// <see cref="ElectionClaimHeadlines"/>: the pool does not know who lost.
        /// </summary>
        public static readonly string[] ElectionChallengeHeadlines =
        {
            "{party} says the argument does not end with the count",
            "{party} accepts the count and disputes the reading of it",
            "{party} is back on the doorstep the morning after the count",
            "The count is over; {party} says the case for the scheme is not",
            "{party} takes the result as a brief rather than a verdict"
        };

        /// <inheritdoc cref="ElectionChallengeHeadlines"/>
        public static readonly string[] ElectionChallengeBodies =
        {
            "{party} was back at the market square by ten, which is either discipline or stubbornness " +
            "and is probably both. Its reading of the night is that the count settled who sits in the " +
            "chamber and settled nothing about the scheme. That argument now goes to a council freshly " +
            "reminded that the city is watching.",

            "The count is finished and {party} has not changed its case by a word. The party accepts " +
            "the arithmetic - there is nothing else to do with arithmetic - and rejects the conclusion " +
            "drawn from it elsewhere. The chamber will hear the same motion again in the spring.",

            "{party} spent the morning after the count doing what it did the morning before it: " +
            "knocking on doors on the estate behind the depot. The result belongs to the returning " +
            "officer. The argument, the party says, belongs to whoever keeps making it."
        };

        /// <summary>
        /// Slot (d): the coalition outlook, drawn only under <c>RegionTheme.Eu</c>. There is
        /// nothing to have an outlook on under first-past-the-post wards with a directly elected
        /// mayor, which is the same reason <c>FlavorPromptBuilder</c> withholds the piece from an NA
        /// prompt.
        /// </summary>
        public static readonly string[] ElectionCoalitionHeadlines =
        {
            "{party} starts counting friends, and the chamber starts arranging",
            "Who governs with {party}, and on what: the week's real question",
            "{party} opens talks before the hall has been swept",
            "The arithmetic is done; now {party} does the arrangements",
            "{party} names its red line before the first meeting"
        };

        /// <inheritdoc cref="ElectionCoalitionHeadlines"/>
        public static readonly string[] ElectionCoalitionBodies =
        {
            "The count decided who sits in the chamber. It did not decide who governs, and that is the " +
            "week's work. {party} has named the thing it will not trade, which is the usual way of " +
            "starting: a red line stated in public is harder to walk back and easier to sell to your " +
            "own side. Talks begin in a room off the mayor's corridor.",

            "{party} spent the morning on the phone rather than at the microphone, which tells you " +
            "where the week is going. Nobody has the chamber to themselves. The arrangement that " +
            "emerges will be argued over in public and settled in a small room, and it will be settled " +
            "on the budget line rather than on the manifesto.",

            "Coalition talk started before the hall was swept. {party} is one of the parties that has " +
            "to be talked to, and it knows it. What it wants is on the record and has been since the " +
            "spring; what it will settle for is not, and will not be until the room is closed and the " +
            "officers have left."
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
