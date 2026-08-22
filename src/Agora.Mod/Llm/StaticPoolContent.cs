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
        // 2. Name a subject - the party the article refs. Every election template carries {party};
        //    the generic pool carries no placeholder at all and is only reached when a substituted
        //    line cannot fit its cap.
        // 3. No unattributed sourcing. Not "residents say", "officials say", "critics say", "sources
        //    say", "some argue", "many feel", nor any variant. Attribute to the named party, or do
        //    not attribute at all. StaticPoolPressTests asserts this over the arrays themselves, so a
        //    template added in breach of it fails the build rather than the reader.
        // 4. Never a figure - no vote share, no seat count, no percentage, no budget number. These
        //    are not LLM output, so non-negotiable #1 does not bite, but the dashboard carries the
        //    figures and the prose does not. Qualitative bands only.
        //
        // THE GENERAL POOLS ARE GONE. CityHeadlines, CityBodies, DistrictHeadlines and DistrictBodies
        // wrote the ordinary month for the news feed; v10 of docs/contracts/ui_bindings.md retired the
        // feed, so both writers stopped producing that coverage and the four arrays went with it. The
        // {mood} placeholder went too - it appeared in the city pool alone. Do not restore one without
        // a surface that renders what it writes.
        //
        // Lengths are bounded by FlavorCacheMigration.HeadlineMaxLength / BodyMaxLength AFTER
        // substitution. Party names are drawn from the pools above, so their worst case is computable
        // and pinned by a test.

        /// <summary>
        /// The last resort, for when a substituted line cannot fit its cap - a party name long enough
        /// to push every <c>{party}</c> template past
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

        // ---- stories ---------------------------------------------------------------------------
        //
        // WHY THERE ARE SO FEW LINES DOWN HERE.
        //
        // A story's canned prose is a transcription, not a draw: the headline is the major event's
        // authored name and the article is the authored text of its slots, in the story's own order.
        // That is content the catalog already wrote, checked by the same tests that check the catalog,
        // and it is about this city's story rather than about stories in general. The three pools below
        // are only the floor under it - what a card opens with when the authored text will not fit its
        // cap whole, which the house rule (prune, never truncate) says is the one case where a generic
        // whole line beats a specific cut one. The four closing lines after them are not a floor: a
        // resolution carries one every time, because saying that the story closed is the one thing a
        // closing card owes the player and the only part of it the catalog cannot supply.

        /// <summary>
        /// Headlines for a live story whose own major event name will not fit
        /// <c>FlavorCacheMigration.StoryHeadlineMaxLength</c>. Placeholder-free and inside the cap
        /// unsubstituted; a test pins both.
        /// </summary>
        public static readonly string[] StoryHeadlines =
        {
            "The council takes something on, and the city waits to see",
            "A file opens at City Hall, and the clock starts with it",
            "The chamber picks up a question it has been walking past"
        };

        /// <inheritdoc cref="StoryHeadlines"/>
        public static readonly string[] StoryArticles =
        {
            "The council has taken this one on, which is the first thing that has happened to it in a " +
            "while. What follows is committee work: a report, a budget line, an evening of deputations " +
            "and a vote that will be reported in one sentence. The city will know how it went long " +
            "before the chamber says so.",

            "It is on the agenda now, and being on the agenda is not the same as being decided. The " +
            "administration has a plan, the objections have a hearing date, and between the two there " +
            "is a winter to get through.",

            "The file is open. Officers are costing it, the committee has an evening set aside for it, " +
            "and everyone involved has said in public that they want the same thing, which is how these " +
            "start and rarely how they end."
        };

        /// <summary>
        /// Headlines for a resolved story whose own major event name will not fit. Closing rather than
        /// opening, because a resolution card is the last thing a player reads about a story.
        /// </summary>
        public static readonly string[] ResolutionHeadlines =
        {
            "The file closes, and the city gets the outcome it was given",
            "That is the end of that one, whatever the chamber says next",
            "The council draws a line under it and moves down the agenda"
        };

        /// <summary>
        /// How a resolution card opens: one line saying that the story closed and how it went.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <b>Constants selected by <c>StoryBrief.OutcomeWord</c>, never drawn.</b> A closing card
        /// whose only difference from the opening card was the tense of three catalog paragraphs is
        /// not a closing card, and there are three shipped cases where even that difference vanishes:
        /// an abandoned story leaves every slot <c>Pending</c> and so every slot word empty, a slot
        /// that came out <c>unmeasurable</c> has no authored outcome text to switch to, and a save
        /// whose civic catalog has not reached the pool resolves nothing at all. The lead-in is the
        /// one part of a resolution that needs neither the catalog nor a slot word, so it is what
        /// carries the news that the story is over.
        /// </para>
        /// <para>
        /// Four words, four lines, and the fourth is not decoration: <c>OutcomeWord</c> is empty while
        /// a story is open, and a brief that arrives resolved with no word is a caller bug that should
        /// still produce a whole card rather than a card that says nothing.
        /// </para>
        /// </remarks>
        public const string ResolutionSuccessLead =
            "The file is closed, and it closed the way the council was working for.";

        /// <inheritdoc cref="ResolutionSuccessLead"/>
        public const string ResolutionFailureLead =
            "The file is closed, and it did not close the way the council was working for.";

        /// <inheritdoc cref="ResolutionSuccessLead"/>
        public const string ResolutionAbandonedLead =
            "The council let this one go, and the file closed with it unfinished.";

        /// <inheritdoc cref="ResolutionSuccessLead"/>
        public const string ResolutionClosedLead = "The file is closed.";

        /// <summary>Deterministic pick: index derived from the caller's seeded stream, never from a hash.</summary>
        public static string Pick(string[] pool, Agora.Core.Determinism.DeterministicRng rng)
        {
            if (pool == null || pool.Length == 0) return string.Empty;
            if (rng == null) return pool[0];
            return pool[rng.NextInt(0, pool.Length)];
        }
    }
}
