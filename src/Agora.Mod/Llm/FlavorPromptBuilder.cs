using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Agora.Core.Contracts;

namespace Agora.Mod.Llm
{
    /// <summary>
    /// Assembles the prompt sent to the Claude CLI.
    ///
    /// <para>
    /// <b>The city is described in words, not figures.</b> Happiness 62.4 becomes "content";
    /// unemployment 0.11 becomes "high". That is not decoration. The single failure this whole
    /// packet exists to prevent is a model-authored number reaching engine state, and the cheapest
    /// structural defence is to never put a number in front of the model in the first place - a
    /// model that was never told the unemployment rate cannot write an article quoting it slightly
    /// wrong, and a reader who never sees a figure in the prose cannot mistake one for engine truth.
    /// The dashboard shows the real values, from the real snapshot, one panel away.
    /// </para>
    ///
    /// <para>
    /// The band thresholds below are prose vocabulary, not tuning: they choose an adjective and
    /// touch no engine state, so moving one cannot change a vote. They are still called out in the
    /// report as candidates for a future <c>llm</c> section in <c>engine_tuning.json</c>.
    /// </para>
    ///
    /// <para>Pure and deterministic: same request in, same prompt out. No I/O, no clock, no game types.</para>
    /// </summary>
    public static class FlavorPromptBuilder
    {
        /// <summary>Hard cap on prompt size. A prompt past this has a bug upstream, not a big city.</summary>
        public const int MaxPromptCharacters = 120_000;

        /// <summary>Districts named in the prompt, worst-off first. Whole-city prose does not need 60.</summary>
        public const int MaxDistrictsInPrompt = 12;

        public static string Build(FlavorRequest request)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));

            var sb = new StringBuilder(8192);

            AppendRole(sb, request);
            AppendRules(sb);
            AppendSituation(sb, request);
            AppendRoster(sb, request);
            AppendEvents(sb, request);
            AppendTask(sb, request);
            AppendSchema(sb);

            string prompt = sb.ToString();
            if (prompt.Length > MaxPromptCharacters)
            {
                prompt = prompt.Substring(0, MaxPromptCharacters);
            }
            return prompt;
        }

        private static void AppendRole(StringBuilder sb, FlavorRequest request)
        {
            sb.Append("You are the writers' room for a city's political press. ");
            sb.Append("The city is simulated; a separate engine already decided every fact, every number ");
            sb.Append("and every outcome. You supply only the words.\n\n");

            sb.Append("Date: ").Append(request.Date.ToString()).Append('\n');
            sb.Append("Political system: ").Append(SystemDescription(request.Theme)).Append('\n');
            sb.Append("Occasion: ").Append(ReasonDescription(request.Reason)).Append("\n\n");
        }

        private static void AppendRules(StringBuilder sb)
        {
            sb.Append("RULES - a response breaking any of these is discarded unread:\n");
            sb.Append("1. Output one JSON object and nothing else. No preamble, no explanation, no markdown fence.\n");
            sb.Append("2. Prose, IDs and dates only. Do not write a single number, percentage, poll figure, ");
            sb.Append("seat count, budget figure or year-over-year change anywhere in any field - not in a name, ");
            sb.Append("not in a headline, not in a body. Say \"support slipped\", never a figure.\n");
            sb.Append("3. Use only the IDs listed below, spelled exactly. Do not invent a party, faction, ");
            sb.Append("district or event. An entry referencing an unknown ID is dropped.\n");
            sb.Append("4. English only.\n");
            sb.Append("5. Do not predict results, announce winners, or state what the city government will do next. ");
            sb.Append("The engine decides that and your text is written before it does.\n");
            sb.Append("6. Keep every field inside its length limit in the schema at the end of this prompt.\n");
            sb.Append("7. Invent no real-world people, brands or organisations.\n\n");
        }

        private static void AppendSituation(StringBuilder sb, FlavorRequest request)
        {
            var snapshot = request.Snapshot;
            if (snapshot == null)
            {
                sb.Append("THE CITY: no measurements are available this cycle. Write generally.\n\n");
                return;
            }

            sb.Append("THE CITY (qualitative only - the engine holds the figures):\n");
            sb.Append("- size: ").Append(PopulationBand(snapshot.Population)).Append('\n');
            sb.Append("- mood: ").Append(HappinessBand(snapshot.Happiness)).Append('\n');
            sb.Append("- work: ").Append(UnemploymentBand(snapshot.Unemployment)).Append('\n');
            sb.Append("- cost of living: ").Append(RentBurdenBand(snapshot.RentBurden)).Append('\n');
            sb.Append("- pollution: ").Append(UnitBand(snapshot.Pollution.Mean(), "clean", "mostly clean", "noticeable", "bad", "choking")).Append('\n');
            sb.Append("- public services: ").Append(CoverageBand(snapshot.Services.Mean())).Append('\n');
            sb.Append("- crime: ").Append(UnitBand(Normalise01(snapshot.CrimeRate), "rare", "low", "a live issue", "serious", "out of hand")).Append('\n');
            sb.Append("- commuting: ").Append(CommuteBand(snapshot.AverageCommuteMinutes)).Append('\n');
            sb.Append("- public finances: ").Append(BudgetBand(snapshot.BudgetBalance, snapshot.Debt)).Append('\n');

            var indices = snapshot.Indices;
            if (indices != null)
            {
                sb.Append("- inequality: ").Append(UnitBand(indices.GiniCoefficient, "flat", "mild", "visible", "stark", "two cities")).Append('\n');
                sb.Append("- service fairness: ").Append(UnitBand(1.0 - indices.ServiceInequalityIndex, "wildly uneven", "uneven", "patchy", "fairly even", "even")).Append('\n');
                sb.Append("- political temperature: ").Append(UnitBand(indices.PolarizationIndex, "calm", "settled", "restive", "polarised", "at each other's throats")).Append('\n');
                sb.Append("- faith in the council: ").Append(UnitBand(indices.LegitimacyIndex, "gone", "thin", "grudging", "solid", "strong")).Append('\n');
            }

            AppendDistricts(sb, snapshot);
            sb.Append('\n');
        }

        private static void AppendDistricts(StringBuilder sb, CitySnapshot snapshot)
        {
            if (snapshot.Districts == null || snapshot.Districts.Count == 0) return;

            // Unhappiest first, so a limited prompt spends its budget on the districts with a story.
            // Ties broken on Id, because a sort whose result depends on the input order makes two
            // otherwise identical prompts impossible to diff.
            var districts = new List<DistrictSnapshot>();
            for (int i = 0; i < snapshot.Districts.Count; i++)
            {
                if (snapshot.Districts[i] != null) districts.Add(snapshot.Districts[i]);
            }
            districts.Sort((a, b) =>
            {
                int byHappiness = a.Happiness.CompareTo(b.Happiness);
                if (byHappiness != 0) return byHappiness;
                return string.CompareOrdinal(a.Id, b.Id);
            });

            int count = districts.Count < MaxDistrictsInPrompt ? districts.Count : MaxDistrictsInPrompt;
            if (count == 0) return;

            sb.Append("\nDISTRICTS (id | name | mood | note):\n");
            for (int i = 0; i < count; i++)
            {
                var district = districts[i];
                sb.Append("- ").Append(district.Id)
                  .Append(" | ").Append(Sanitize(district.Name))
                  .Append(" | ").Append(HappinessBand(district.Happiness));

                // Non-negotiable: a city figure standing in for a local one must never be presented
                // as a local fact (§6). Telling the model keeps it out of the prose in the first place.
                if (district.HasCityFallbacks)
                {
                    sb.Append(" | some local measurements are unavailable; do not write about local specifics here");
                }
                sb.Append('\n');
            }

            if (districts.Count > count)
            {
                sb.Append("- (").Append((districts.Count - count).ToString(CultureInfo.InvariantCulture))
                  .Append(" further districts omitted; do not reference them)\n");
            }
        }

        private static void AppendRoster(StringBuilder sb, FlavorRequest request)
        {
            if (request.Parties.Count > 0)
            {
                sb.Append("PARTIES (partyId | archetype | leads on | standing | current name):\n");
                var parties = new List<PartyBrief>(request.Parties);
                parties.Sort((a, b) => string.CompareOrdinal(a.PartyId, b.PartyId));
                for (int i = 0; i < parties.Count; i++)
                {
                    var p = parties[i];
                    sb.Append("- ").Append(p.PartyId)
                      .Append(" | ").Append(Sanitize(p.ArchetypeId))
                      .Append(" | ").Append(Issues.ToKey(p.CoreGrievance))
                      .Append(" | ").Append(Sanitize(p.StatusWord))
                      .Append(" | ").Append(string.IsNullOrEmpty(p.CurrentName) ? "(unnamed)" : Sanitize(p.CurrentName))
                      .Append('\n');
                }
                sb.Append('\n');
            }

            if (request.Factions.Count > 0)
            {
                sb.Append("FACTIONS (factionId | partyId | archetype | leads on | standing | current name):\n");
                var factions = new List<FactionBrief>(request.Factions);
                factions.Sort((a, b) => string.CompareOrdinal(a.FactionId, b.FactionId));
                for (int i = 0; i < factions.Count; i++)
                {
                    var f = factions[i];
                    sb.Append("- ").Append(f.FactionId)
                      .Append(" | ").Append(f.PartyId)
                      .Append(" | ").Append(Sanitize(f.ArchetypeId))
                      .Append(" | ").Append(Issues.ToKey(f.CoreGrievance))
                      .Append(" | ").Append(Sanitize(f.StatusWord))
                      .Append(" | ").Append(string.IsNullOrEmpty(f.CurrentName) ? "(unnamed)" : Sanitize(f.CurrentName))
                      .Append('\n');
                }
                sb.Append('\n');
            }
        }

        private static void AppendEvents(StringBuilder sb, FlavorRequest request)
        {
            if (request.Events.Count == 0) return;

            sb.Append("EVENTS IN PLAY (eventId | title | factual brief):\n");
            var events = new List<EventBrief>(request.Events);
            events.Sort((a, b) => string.CompareOrdinal(a.EventId, b.EventId));
            for (int i = 0; i < events.Count; i++)
            {
                var e = events[i];
                sb.Append("- ").Append(e.EventId)
                  .Append(" | ").Append(Sanitize(e.Title))
                  .Append(" | ").Append(Sanitize(e.HeadlineBrief))
                  .Append('\n');
            }
            sb.Append('\n');
        }

        private static void AppendTask(StringBuilder sb, FlavorRequest request)
        {
            int articles = request.ArticleCount;
            if (articles < 0) articles = 0;
            if (articles > 12) articles = 12;

            sb.Append("WRITE:\n");
            if (request.Parties.Count > 0)
            {
                sb.Append("- partyFlavor for every party listed, with a name, a short name, ");
                sb.Append("a description of who it speaks for, and a slogan. Keep an existing name unless ");
                sb.Append("the party's standing says it has just been founded, merged or revived.\n");
            }
            if (request.Factions.Count > 0)
            {
                sb.Append("- factionFlavor for every faction listed, including a leader's name.\n");
            }
            sb.Append("- ").Append(articles.ToString(CultureInfo.InvariantCulture));
            sb.Append(" articles from local outlets covering the city as described above. ");
            sb.Append("Vary the outlets and the tones. Each article's id must be unique and kebab-case. ");
            sb.Append("Set refs only to IDs from the lists above.\n");
            if (request.Events.Count > 0)
            {
                sb.Append("- eventProse for every event listed: how that event lands in THIS city specifically.\n");
            }
            sb.Append("- generatedAtSimDate exactly \"").Append(request.Date.ToString()).Append("\".\n");
            sb.Append("- schemaVersion exactly ").Append(FlavorSchema.SupportedSchemaVersion.ToString(CultureInfo.InvariantCulture));
            sb.Append(" (the only number allowed in your entire response).\n\n");
        }

        private static void AppendSchema(StringBuilder sb)
        {
            sb.Append("SCHEMA - your output is validated against this and silently discarded if it fails:\n");
            sb.Append(FlavorSchema.EmbeddedJson);
            sb.Append('\n');
        }

        // ---- banding -------------------------------------------------------------------------
        //
        // Every threshold below picks an adjective. None of them feeds engine state, so none of them
        // is a tuning coefficient in the sense of non-negotiable "no hardcoded tuning constants" -
        // changing one changes a word in a prompt and nothing else.

        private static string SystemDescription(RegionTheme theme) =>
            theme == RegionTheme.Na
                ? "first-past-the-post wards with a directly elected mayor; two dominant parties, each with internal factions"
                : "proportional representation with coalition government; several parties, none usually near a majority";

        private static string ReasonDescription(FlavorWakeReason reason)
        {
            switch (reason)
            {
                case FlavorWakeReason.Election: return "an election has just been decided";
                case FlavorWakeReason.Manual: return "a mid-cycle news round, requested by the desk";
                default: return "the annual round-up";
            }
        }

        private static string PopulationBand(int population)
        {
            if (population < 2_000) return "a village";
            if (population < 10_000) return "a small town";
            if (population < 50_000) return "a town";
            if (population < 200_000) return "a city";
            if (population < 750_000) return "a large city";
            return "a metropolis";
        }

        /// <summary>
        /// Happiness arrives 0-100 (see CitySnapshot). Public because
        /// <see cref="StaticPoolProvider"/> fills the same <c>{mood}</c> slot in its canned prose and
        /// the two must agree - a fallback article calling the city "content" while the prompt calls
        /// it "furious" would read as a bug the moment the CLI came back.
        /// </summary>
        public static string HappinessBand(double happiness) =>
            UnitBand(Clamp01(happiness / 100.0), "furious", "unhappy", "grumbling", "content", "delighted");

        /// <summary>
        /// The same banding as <see cref="HappinessBand"/>, as an index 0 (furious) to 4 (delighted).
        /// <see cref="StaticPoolProvider"/> uses it to pick a tone that matches the mood, so a canned
        /// article does not report a furious city in a celebratory voice.
        /// </summary>
        public static int HappinessBandIndex(double happiness)
        {
            double v = Clamp01(happiness / 100.0);
            if (v < 0.20) return 0;
            if (v < 0.40) return 1;
            if (v < 0.60) return 2;
            if (v < 0.80) return 3;
            return 4;
        }

        /// <summary>Unemployment arrives 0-1.</summary>
        private static string UnemploymentBand(double unemployment) =>
            UnitBand(Clamp01(1.0 - unemployment * 5.0), "mass unemployment", "high unemployment",
                     "noticeable unemployment", "most people working", "near full employment");

        private static string RentBurdenBand(double rentBurden) =>
            UnitBand(Clamp01(1.0 - rentBurden), "rents are crushing", "rents bite hard",
                     "rents are a common complaint", "rents are manageable", "rents are easy");

        private static string CoverageBand(double coverage) =>
            UnitBand(Clamp01(coverage), "absent", "thin", "patchy", "decent", "excellent");

        private static string CommuteBand(double minutes)
        {
            if (minutes <= 0.0) return "unmeasured";
            if (minutes < 12.0) return "short";
            if (minutes < 25.0) return "reasonable";
            if (minutes < 40.0) return "long";
            if (minutes < 60.0) return "punishing";
            return "an ordeal";
        }

        private static string BudgetBand(long balance, long debt)
        {
            if (balance < 0 && debt > 0) return "in deficit and carrying debt";
            if (balance < 0) return "spending more than it takes in";
            if (debt > 0) return "balanced, but servicing debt";
            return "in the black";
        }

        /// <summary>Maps a [0,1] value onto five adjectives, lowest first.</summary>
        private static string UnitBand(double value, string veryLow, string low, string middle, string high, string veryHigh)
        {
            double v = Clamp01(value);
            if (v < 0.20) return veryLow;
            if (v < 0.40) return low;
            if (v < 0.60) return middle;
            if (v < 0.80) return high;
            return veryHigh;
        }

        /// <summary>
        /// CrimeRate has no documented unit on the snapshot contract, so treat anything above 1 as a
        /// 0-100 scale and anything else as already normalised. A wrong guess costs one adjective.
        /// </summary>
        private static double Normalise01(double value) => Clamp01(value > 1.0 ? value / 100.0 : value);

        private static double Clamp01(double value)
        {
            if (double.IsNaN(value)) return 0.0;
            if (value < 0.0) return 0.0;
            if (value > 1.0) return 1.0;
            return value;
        }

        /// <summary>
        /// Flattens player-authored text (district names) for the prompt: newlines and pipes would
        /// break the line-per-record layout the model is reading, and a name is not trusted input.
        /// </summary>
        private static string Sanitize(string text)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;

            var sb = new StringBuilder(text.Length);
            for (int i = 0; i < text.Length && sb.Length < 120; i++)
            {
                char c = text[i];
                if (c == '\r' || c == '\n' || c == '|') { sb.Append(' '); continue; }
                if (char.IsControl(c)) continue;
                sb.Append(c);
            }
            return sb.ToString().Trim();
        }
    }
}
