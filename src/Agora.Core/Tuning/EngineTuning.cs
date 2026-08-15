using System;
using System.Collections.Generic;
using Agora.Core.Contracts;

namespace Agora.Core.Tuning
{
    /// <summary>
    /// Typed access to <c>data/engine_tuning.json</c> — the only place a coefficient may live.
    ///
    /// <para>
    /// Non-negotiable: no engine packet parses this file itself and no engine packet hardcodes a
    /// number. Take an <see cref="EngineTuning"/> as a parameter and read the section you own. Every
    /// section below corresponds to exactly one top-level key in the JSON file and exactly one
    /// engine packet, so two packets can never fight over the same coefficient.
    /// </para>
    ///
    /// <para>
    /// Reading is total: a missing key yields the documented default and appends a line to
    /// <see cref="Warnings"/>. Only malformed JSON throws, and only from <see cref="FromJson"/>.
    /// </para>
    /// </summary>
    public sealed class EngineTuning
    {
        /// <summary>Bumped whenever a key is added, removed or renamed. Runs through <c>/schema-change</c>.</summary>
        /// <summary>
        /// Must equal <c>data/engine_tuning.json</c>'s own <c>schemaVersion</c>.
        /// <c>ShippedTuningTests.ShippedTuningFile_MatchesBuiltInDefaults</c> pins them together,
        /// because every other test in the suite runs against <see cref="Default"/> rather than the
        /// file — so a value that differs here is a value the shipped engine never verified.
        /// </summary>
        public int SchemaVersion { get; internal set; } = 6;

        public BlocsTuning Blocs { get; internal set; } = new BlocsTuning();
        public PartiesTuning Parties { get; internal set; } = new PartiesTuning();
        public FactionsTuning Factions { get; internal set; } = new FactionsTuning();
        public AffinityTuning Affinity { get; internal set; } = new AffinityTuning();
        public TurnoutTuning Turnout { get; internal set; } = new TurnoutTuning();
        public PollingTuning Polling { get; internal set; } = new PollingTuning();
        public ElectionsPrTuning ElectionsPr { get; internal set; } = new ElectionsPrTuning();
        public ElectionsFptpTuning ElectionsFptp { get; internal set; } = new ElectionsFptpTuning();
        public CoalitionsTuning Coalitions { get; internal set; } = new CoalitionsTuning();
        public MandatesTuning Mandates { get; internal set; } = new MandatesTuning();
        public CatalogTuning Catalog { get; internal set; } = new CatalogTuning();
        public SchedulerTuning Scheduler { get; internal set; } = new SchedulerTuning();
        public IndicesTuning Indices { get; internal set; } = new IndicesTuning();
        public EffectsTuning Effects { get; internal set; } = new EffectsTuning();
        public FringeTuning Fringe { get; internal set; } = new FringeTuning();
        public StoriesTuning Stories { get; internal set; } = new StoriesTuning();
        public PowerTuning Power { get; internal set; } = new PowerTuning();

        /// <summary>
        /// Keys that were missing or the wrong shape, in the order they were read. Empty for a file
        /// that matches this code. Log these on load — a non-empty list means the file and the engine
        /// have drifted.
        /// </summary>
        public IReadOnlyList<string> Warnings { get; internal set; } = new List<string>();

        /// <summary>
        /// The built-in defaults, identical to the values shipped in <c>data/engine_tuning.json</c>.
        /// Use this in unit tests so a test never depends on file IO or on the working directory.
        /// </summary>
        public static EngineTuning Default => new EngineTuning();

        /// <summary>
        /// Parses a tuning document. Throws <see cref="TuningFormatException"/> only when the text is
        /// not valid JSON; unknown or missing keys are recorded in <see cref="Warnings"/>.
        /// </summary>
        public static EngineTuning FromJson(string json)
        {
            JsonNode root = TuningJsonParser.Parse(json);
            var warnings = new List<string>();
            var reader = new TuningReader(root, "", warnings);
            var t = new EngineTuning();
            var d = new EngineTuning(); // built-in defaults, used wherever the file is silent

            t.SchemaVersion = reader.Int("schemaVersion", d.SchemaVersion);
            t.Blocs = BlocsTuning.Read(reader.Section("blocs"), d.Blocs);
            t.Parties = PartiesTuning.Read(reader.Section("parties"), d.Parties);
            t.Factions = FactionsTuning.Read(reader.Section("factions"), d.Factions);
            t.Affinity = AffinityTuning.Read(reader.Section("affinity"), d.Affinity);
            t.Turnout = TurnoutTuning.Read(reader.Section("turnout"), d.Turnout);
            t.Polling = PollingTuning.Read(reader.Section("polling"), d.Polling);
            t.ElectionsPr = ElectionsPrTuning.Read(reader.Section("electionsPr"), d.ElectionsPr);
            t.ElectionsFptp = ElectionsFptpTuning.Read(reader.Section("electionsFptp"), d.ElectionsFptp);
            t.Coalitions = CoalitionsTuning.Read(reader.Section("coalitions"), d.Coalitions);
            t.Mandates = MandatesTuning.Read(reader.Section("mandates"), d.Mandates);
            t.Catalog = CatalogTuning.Read(reader.Section("catalog"), d.Catalog);
            t.Scheduler = SchedulerTuning.Read(reader.Section("scheduler"), d.Scheduler);
            t.Indices = IndicesTuning.Read(reader.Section("indices"), d.Indices);
            t.Effects = EffectsTuning.Read(reader.Section("effects"), d.Effects);
            t.Fringe = FringeTuning.Read(reader.Section("fringe"), d.Fringe);
            t.Stories = StoriesTuning.Read(reader.Section("stories"), d.Stories);
            t.Power = PowerTuning.Read(reader.Section("power"), d.Power);
            t.Warnings = warnings;
            return t;
        }

        /// <summary>
        /// Parses, or returns <see cref="Default"/> with the parse error as the single warning.
        /// The load path uses this: a corrupt tuning file must degrade to defaults, not crash a save.
        /// </summary>
        public static EngineTuning LoadOrDefault(string? json)
        {
            if (string.IsNullOrEmpty(json))
            {
                var empty = Default;
                empty.Warnings = new List<string> { "engine_tuning.json was missing or empty; using built-in defaults." };
                return empty;
            }

            try
            {
                return FromJson(json!);
            }
            catch (TuningFormatException ex)
            {
                var fallback = Default;
                fallback.Warnings = new List<string> { "engine_tuning.json is malformed (" + ex.Message + "); using built-in defaults." };
                return fallback;
            }
        }
    }

    /// <summary>Packet 1 — bloc construction and issue weights. JSON section <c>blocs</c>.</summary>
    public sealed class BlocsTuning
    {
        /// <summary>Quantile cuts of the household wealth distribution, ascending. Two cuts → three tiers.</summary>
        public double[] WealthTierThresholds { get; internal set; } = { 0.33, 0.66 };

        /// <summary>Blocs smaller than this are pruned before affinity runs.</summary>
        public int MinBlocPopulation { get; internal set; } = 25;

        /// <summary>Blocs below this share of the district are pruned even if above the head count.</summary>
        public double MinBlocShare { get; internal set; } = 0.005;

        /// <summary>EMA factor applied to bloc composition month over month. 1.0 = no smoothing.</summary>
        public double CompositionSmoothingAlpha { get; internal set; } = 0.25;

        /// <summary>Base issue weights before composition and lived metrics adjust them.</summary>
        public IssueWeights IssueWeightPriors { get; internal set; } = IssueWeights.Uniform;

        /// <summary>Weight shift per unit of wealth axis (-1 low → +1 high).</summary>
        public IssueWeights WealthWeightSensitivity { get; internal set; } =
            new IssueWeights(-0.35, -0.50, 0.30, -0.25, 0.20, 0.15);

        /// <summary>Weight shift per unit of education axis.</summary>
        public IssueWeights EducationWeightSensitivity { get; internal set; } =
            new IssueWeights(0.10, -0.20, 0.35, 0.25, 0.10, -0.30);

        /// <summary>Weight shift per unit of age axis (-1 child → +1 elderly).</summary>
        public IssueWeights AgeWeightSensitivity { get; internal set; } =
            new IssueWeights(0.25, 0.15, -0.10, -0.15, -0.25, 0.35);

        /// <summary>Ideal-point stance for a perfectly average bloc.</summary>
        public IssuePosition IdealBase { get; internal set; } =
            new IssuePosition(0.20, 0.20, 0.10, 0.10, 0.00, 0.10);

        /// <summary>Ideal-point shift per unit of wealth axis.</summary>
        public IssuePosition WealthIdealSensitivity { get; internal set; } =
            new IssuePosition(-0.30, -0.45, 0.15, -0.20, 0.30, 0.20);

        /// <summary>Ideal-point shift per unit of education axis.</summary>
        public IssuePosition EducationIdealSensitivity { get; internal set; } =
            new IssuePosition(0.15, -0.05, 0.40, 0.30, 0.10, -0.35);

        /// <summary>Ideal-point shift per unit of age axis.</summary>
        public IssuePosition AgeIdealSensitivity { get; internal set; } =
            new IssuePosition(0.20, 0.10, -0.05, -0.10, -0.30, 0.40);

        /// <summary>How strongly a lived metric deficit raises the matching issue weight.</summary>
        public double LivedMetricWeightGain { get; internal set; } = 0.35;

        /// <summary>Cap on the total lived-metric shift for one issue, so no metric can dominate.</summary>
        public double LivedMetricMaxShift { get; internal set; } = 0.50;

        /// <summary>EMA factor on the lived-metric term.</summary>
        public double LivedMetricSmoothingAlpha { get; internal set; } = 0.20;

        public double WeightFloor { get; internal set; } = 0.05;

        public double WeightCeiling { get; internal set; } = 3.00;

        /// <summary>Renormalise weights to mean 1.0 after all adjustments.</summary>
        public bool NormalizeWeights { get; internal set; } = true;

        public double DiscontentHappinessWeight { get; internal set; } = 0.50;
        public double DiscontentServiceWeight { get; internal set; } = 0.30;
        public double DiscontentCostWeight { get; internal set; } = 0.20;

        /// <summary>Happiness (0–100) that counts as neutral. Above raises contentment, below lowers it.</summary>
        public double ReferenceHappiness { get; internal set; } = 50.0;

        internal static BlocsTuning Read(TuningReader r, BlocsTuning d) => new BlocsTuning
        {
            WealthTierThresholds = r.Numbers("wealthTierThresholds", d.WealthTierThresholds),
            MinBlocPopulation = r.Int("minBlocPopulation", d.MinBlocPopulation),
            MinBlocShare = r.Num("minBlocShare", d.MinBlocShare),
            CompositionSmoothingAlpha = r.Num("compositionSmoothingAlpha", d.CompositionSmoothingAlpha),
            IssueWeightPriors = r.Weights("issueWeightPriors", d.IssueWeightPriors),
            WealthWeightSensitivity = r.Weights("wealthWeightSensitivity", d.WealthWeightSensitivity),
            EducationWeightSensitivity = r.Weights("educationWeightSensitivity", d.EducationWeightSensitivity),
            AgeWeightSensitivity = r.Weights("ageWeightSensitivity", d.AgeWeightSensitivity),
            IdealBase = r.Position("idealBase", d.IdealBase),
            WealthIdealSensitivity = r.Position("wealthIdealSensitivity", d.WealthIdealSensitivity),
            EducationIdealSensitivity = r.Position("educationIdealSensitivity", d.EducationIdealSensitivity),
            AgeIdealSensitivity = r.Position("ageIdealSensitivity", d.AgeIdealSensitivity),
            LivedMetricWeightGain = r.Num("livedMetricWeightGain", d.LivedMetricWeightGain),
            LivedMetricMaxShift = r.Num("livedMetricMaxShift", d.LivedMetricMaxShift),
            LivedMetricSmoothingAlpha = r.Num("livedMetricSmoothingAlpha", d.LivedMetricSmoothingAlpha),
            WeightFloor = r.Num("weightFloor", d.WeightFloor),
            WeightCeiling = r.Num("weightCeiling", d.WeightCeiling),
            NormalizeWeights = r.Flag("normalizeWeights", d.NormalizeWeights),
            DiscontentHappinessWeight = r.Num("discontentHappinessWeight", d.DiscontentHappinessWeight),
            DiscontentServiceWeight = r.Num("discontentServiceWeight", d.DiscontentServiceWeight),
            DiscontentCostWeight = r.Num("discontentCostWeight", d.DiscontentCostWeight),
            ReferenceHappiness = r.Num("referenceHappiness", d.ReferenceHappiness)
        };
    }

    /// <summary>Packet 2 — party generation and lifecycle. JSON section <c>parties</c>.</summary>
    public sealed class PartiesTuning
    {
        public int TargetCountEu { get; internal set; } = 6;
        public int MinCountEu { get; internal set; } = 4;
        public int MaxCountEu { get; internal set; } = 7;

        /// <summary>Dominant parties in the NA theme. Minor parties are counted separately.</summary>
        public int TargetCountNa { get; internal set; } = 2;

        public int MinorPartyCountNa { get; internal set; } = 2;

        /// <summary>Hard ceiling across both themes, including dissolved brands awaiting revival.</summary>
        public int MaxPartiesTotal { get; internal set; } = 9;

        /// <summary>Sigma of the seeded jitter applied to an archetype's platform at generation.</summary>
        public double ArchetypeSpreadSigma { get; internal set; } = 0.35;

        /// <summary>
        /// The same jitter, for a catalog entry with
        /// <see cref="Engine.Parties.PartyArchetype.IsAnchored"/> set.
        /// </summary>
        /// <remarks>
        /// Far tighter than <see cref="ArchetypeSpreadSigma"/> because the two sigmas answer
        /// different questions. 0.35 on a [-1,+1] axis is the right spread for "generate a plausible
        /// new brand": it is wide enough that two saves do not produce the same party. Applied to a
        /// brand the player already has expectations about, it is wide enough to flip the sign of
        /// every axis except the defining one — a conservative party drew a positive environment
        /// stance about one run in five. 0.08 keeps a stance of ±0.10 on the correct side of centre
        /// at better than 89%, and leaves the defining ±0.80 axes untouched for all practical
        /// purposes.
        /// </remarks>
        public double AnchoredSpreadSigma { get; internal set; } = 0.08;

        /// <summary>
        /// What <c>anchoredSpreadSigma</c> becomes under
        /// <see cref="Contracts.BrandDiscipline.Loose"/> — the generated-brand sigma, i.e. anchoring
        /// switched off in all but name.
        /// </summary>
        public double AnchoredSpreadSigmaLoose { get; internal set; } = 0.35;

        /// <summary>
        /// What it becomes under <see cref="Contracts.BrandDiscipline.Locked"/>: tight enough that
        /// even a ±0.10 lean holds its sign in all but a fraction of a percent of saves, at the cost
        /// of two cities generating near-identical parties.
        /// </summary>
        public double AnchoredSpreadSigmaLocked { get; internal set; } = 0.02;

        /// <summary>Two parties closer than this are nudged apart so the ballot stays legible.</summary>
        public double MinPlatformDistance { get; internal set; } = 0.15;

        public double PlatformDriftPerCycle { get; internal set; } = 0.08;
        public double PlatformDriftCapPerCycle { get; internal set; } = 0.20;

        /// <summary>How far a manifesto moves toward the current top grievance each campaign.</summary>
        public double PlatformGrievanceResponsiveness { get; internal set; } = 0.30;

        public double IncumbencyBonus { get; internal set; } = 0.05;

        /// <summary>
        /// Decay per TERM, not per year — rescaled with the move to 1-year terms so the curve keeps
        /// its old per-year shape rather than decaying several times faster in wall-clock time.
        /// </summary>
        public double IncumbencyDecayPerTerm { get; internal set; } = 0.08;

        /// <summary>
        /// Below this share the party is endangered, then dies (§3). Deliberately below
        /// <c>fringe.baseCeiling</c>: a party the fringe ceiling is holding down has not been
        /// rejected by voters, and must not be killed by its own suppression.
        /// </summary>
        public double DeathVoteShareThreshold { get; internal set; } = 0.01;

        public int DeathConsecutiveElections { get; internal set; } = 2;
        public double EndangeredVoteShareThreshold { get; internal set; } = 0.05;

        public double RevivalGrievanceThreshold { get; internal set; } = 0.35;
        public int RevivalCooldownMonths { get; internal set; } = 36;

        public double SplitTensionThreshold { get; internal set; } = 0.45;
        public double SplitMinVoteShare { get; internal set; } = 0.08;
        public double SplitProbabilityPerCycle { get; internal set; } = 0.10;

        public double MergeAffinityThreshold { get; internal set; } = 0.85;
        public double MergeMaxCombinedVoteShare { get; internal set; } = 0.55;
        public double MergeProbabilityPerCycle { get; internal set; } = 0.10;

        public double NewPartyEntryProbability { get; internal set; } = 0.05;

        public int LifecycleCheckIntervalMonths { get; internal set; } = 12;

        /// <summary>Chart colours, assigned in order so a party keeps its colour across reloads.</summary>
        public string[] ColorPalette { get; internal set; } =
        {
            "#C0392B", "#2E86C1", "#27AE60", "#F1C40F", "#8E44AD",
            "#E67E22", "#16A085", "#7F8C8D", "#D35400"
        };

        internal static PartiesTuning Read(TuningReader r, PartiesTuning d) => new PartiesTuning
        {
            TargetCountEu = r.Int("targetCountEu", d.TargetCountEu),
            MinCountEu = r.Int("minCountEu", d.MinCountEu),
            MaxCountEu = r.Int("maxCountEu", d.MaxCountEu),
            TargetCountNa = r.Int("targetCountNa", d.TargetCountNa),
            MinorPartyCountNa = r.Int("minorPartyCountNa", d.MinorPartyCountNa),
            MaxPartiesTotal = r.Int("maxPartiesTotal", d.MaxPartiesTotal),
            ArchetypeSpreadSigma = r.Num("archetypeSpreadSigma", d.ArchetypeSpreadSigma),
            AnchoredSpreadSigma = r.Num("anchoredSpreadSigma", d.AnchoredSpreadSigma),
            AnchoredSpreadSigmaLoose = r.Num("anchoredSpreadSigmaLoose", d.AnchoredSpreadSigmaLoose),
            AnchoredSpreadSigmaLocked = r.Num("anchoredSpreadSigmaLocked", d.AnchoredSpreadSigmaLocked),
            MinPlatformDistance = r.Num("minPlatformDistance", d.MinPlatformDistance),
            PlatformDriftPerCycle = r.Num("platformDriftPerCycle", d.PlatformDriftPerCycle),
            PlatformDriftCapPerCycle = r.Num("platformDriftCapPerCycle", d.PlatformDriftCapPerCycle),
            PlatformGrievanceResponsiveness = r.Num("platformGrievanceResponsiveness", d.PlatformGrievanceResponsiveness),
            IncumbencyBonus = r.Num("incumbencyBonus", d.IncumbencyBonus),
            IncumbencyDecayPerTerm = r.Num("incumbencyDecayPerTerm", d.IncumbencyDecayPerTerm),
            DeathVoteShareThreshold = r.Num("deathVoteShareThreshold", d.DeathVoteShareThreshold),
            DeathConsecutiveElections = r.Int("deathConsecutiveElections", d.DeathConsecutiveElections),
            EndangeredVoteShareThreshold = r.Num("endangeredVoteShareThreshold", d.EndangeredVoteShareThreshold),
            RevivalGrievanceThreshold = r.Num("revivalGrievanceThreshold", d.RevivalGrievanceThreshold),
            RevivalCooldownMonths = r.Int("revivalCooldownMonths", d.RevivalCooldownMonths),
            SplitTensionThreshold = r.Num("splitTensionThreshold", d.SplitTensionThreshold),
            SplitMinVoteShare = r.Num("splitMinVoteShare", d.SplitMinVoteShare),
            SplitProbabilityPerCycle = r.Num("splitProbabilityPerCycle", d.SplitProbabilityPerCycle),
            MergeAffinityThreshold = r.Num("mergeAffinityThreshold", d.MergeAffinityThreshold),
            MergeMaxCombinedVoteShare = r.Num("mergeMaxCombinedVoteShare", d.MergeMaxCombinedVoteShare),
            MergeProbabilityPerCycle = r.Num("mergeProbabilityPerCycle", d.MergeProbabilityPerCycle),
            NewPartyEntryProbability = r.Num("newPartyEntryProbability", d.NewPartyEntryProbability),
            LifecycleCheckIntervalMonths = r.Int("lifecycleCheckIntervalMonths", d.LifecycleCheckIntervalMonths),
            ColorPalette = r.Strings("colorPalette", d.ColorPalette)
        };
    }

    /// <summary>Packet 3 — faction generation, dominance and lifecycle. JSON section <c>factions</c>.</summary>
    public sealed class FactionsTuning
    {
        public int MinPerParty { get; internal set; } = 2;
        public int MaxPerParty { get; internal set; } = 4;
        public int TargetPerParty { get; internal set; } = 3;

        /// <summary>Internal support needed to write the party platform.</summary>
        public double DominanceThreshold { get; internal set; } = 0.45;

        /// <summary>Extra margin a challenger needs to take dominance, so it does not flip every cycle.</summary>
        public double DominanceHysteresis { get; internal set; } = 0.05;

        public double PlatformWeightDominant { get; internal set; } = 0.60;
        public double PlatformWeightOthers { get; internal set; } = 0.40;

        public double InternalTensionThreshold { get; internal set; } = 0.55;

        public double SupportDriftPerCycle { get; internal set; } = 0.10;
        public double SupportDriftCapPerCycle { get; internal set; } = 0.25;

        public double SplitProbabilityPerCycle { get; internal set; } = 0.12;
        public double MergeAffinityThreshold { get; internal set; } = 0.88;
        public double MergeProbabilityPerCycle { get; internal set; } = 0.10;

        public double DeathSupportThreshold { get; internal set; } = 0.08;
        public int DeathConsecutiveCycles { get; internal set; } = 2;
        public double RevivalGrievanceThreshold { get; internal set; } = 0.40;

        public double LeaderChangeProbabilityPerCycle { get; internal set; } = 0.15;

        public int DemandCountPerFaction { get; internal set; } = 2;

        public int LifecycleCheckIntervalMonths { get; internal set; } = 12;

        /// <summary>NA party-level lifecycle events are possible but extremely unlikely (§3).</summary>
        public double NaPartyLifecycleProbability { get; internal set; } = 0.01;

        public double ArchetypeSpreadSigma { get; internal set; } = 0.30;

        internal static FactionsTuning Read(TuningReader r, FactionsTuning d) => new FactionsTuning
        {
            MinPerParty = r.Int("minPerParty", d.MinPerParty),
            MaxPerParty = r.Int("maxPerParty", d.MaxPerParty),
            TargetPerParty = r.Int("targetPerParty", d.TargetPerParty),
            DominanceThreshold = r.Num("dominanceThreshold", d.DominanceThreshold),
            DominanceHysteresis = r.Num("dominanceHysteresis", d.DominanceHysteresis),
            PlatformWeightDominant = r.Num("platformWeightDominant", d.PlatformWeightDominant),
            PlatformWeightOthers = r.Num("platformWeightOthers", d.PlatformWeightOthers),
            InternalTensionThreshold = r.Num("internalTensionThreshold", d.InternalTensionThreshold),
            SupportDriftPerCycle = r.Num("supportDriftPerCycle", d.SupportDriftPerCycle),
            SupportDriftCapPerCycle = r.Num("supportDriftCapPerCycle", d.SupportDriftCapPerCycle),
            SplitProbabilityPerCycle = r.Num("splitProbabilityPerCycle", d.SplitProbabilityPerCycle),
            MergeAffinityThreshold = r.Num("mergeAffinityThreshold", d.MergeAffinityThreshold),
            MergeProbabilityPerCycle = r.Num("mergeProbabilityPerCycle", d.MergeProbabilityPerCycle),
            DeathSupportThreshold = r.Num("deathSupportThreshold", d.DeathSupportThreshold),
            DeathConsecutiveCycles = r.Int("deathConsecutiveCycles", d.DeathConsecutiveCycles),
            RevivalGrievanceThreshold = r.Num("revivalGrievanceThreshold", d.RevivalGrievanceThreshold),
            LeaderChangeProbabilityPerCycle = r.Num("leaderChangeProbabilityPerCycle", d.LeaderChangeProbabilityPerCycle),
            DemandCountPerFaction = r.Int("demandCountPerFaction", d.DemandCountPerFaction),
            LifecycleCheckIntervalMonths = r.Int("lifecycleCheckIntervalMonths", d.LifecycleCheckIntervalMonths),
            NaPartyLifecycleProbability = r.Num("naPartyLifecycleProbability", d.NaPartyLifecycleProbability),
            ArchetypeSpreadSigma = r.Num("archetypeSpreadSigma", d.ArchetypeSpreadSigma)
        };
    }

    /// <summary>Packet 4 — bloc→party affinity. JSON section <c>affinity</c>.</summary>
    public sealed class AffinityTuning
    {
        /// <summary>How issue distance becomes a score: <c>linear</c>, <c>quadratic</c> or <c>gaussian</c>.</summary>
        public string DistanceKernel { get; internal set; } = "linear";

        /// <summary>Width of the gaussian kernel. Ignored by the other kernels.</summary>
        public double DistanceKernelSigma { get; internal set; } = 0.60;

        /// <summary>Weight of the issue-proximity term relative to the other terms.</summary>
        public double IssueWeight { get; internal set; } = 1.00;

        /// <summary>Baseline every party starts from before terms are added.</summary>
        public double BaseAffinity { get; internal set; } = 0.50;

        public double IncumbencyBonus { get; internal set; } = 0.05;

        /// <summary>How much district discontent turns the incumbency bonus into a penalty.</summary>
        public double IncumbencyDiscontentPenalty { get; internal set; } = 0.10;

        public double MandatePerformanceWeight { get; internal set; } = 0.15;
        public double MandateFailurePenalty { get; internal set; } = 0.20;

        public double EventModifierWeight { get; internal set; } = 0.18;

        /// <summary>
        /// What <c>eventModifierWeight</c> becomes under <see cref="Contracts.NewsInfluence.Muted"/>.
        /// See <see cref="TuningPresets"/> for why the Default level has no key here.
        /// </summary>
        public double EventModifierWeightMuted { get; internal set; } = 0.10;

        /// <summary>What it becomes under <see cref="Contracts.NewsInfluence.Loud"/>.</summary>
        public double EventModifierWeightLoud { get; internal set; } = 0.30;
        public int EventModifierDecayHalfLifeMonths { get; internal set; } = 9;

        public double LocalGrievanceWeight { get; internal set; } = 0.20;
        public double NationalMoodWeight { get; internal set; } = 0.10;

        /// <summary>
        /// Stickiness to the bloc's previous vote. Cut with the move to 1-year terms: loyalty decays
        /// from the last election at <see cref="LoyaltyDecayPerMonth"/>, so over 12 months it only
        /// falls to ~0.79 of its value instead of ~0.38 over four years.
        /// </summary>
        public double HabitualLoyalty { get; internal set; } = 0.20;

        public double LoyaltyDecayPerMonth { get; internal set; } = 0.02;

        /// <summary>Sigma of the <c>voter.affinity.noise</c> draw.</summary>
        public double NoiseSigma { get; internal set; } = 0.03;

        /// <summary>Absolute clamp on the noise term, so one tail draw cannot decide an election.</summary>
        public double NoiseClamp { get; internal set; } = 0.10;

        /// <summary>Temperature of the softmax that turns affinities into vote shares.</summary>
        public double SoftmaxTemperature { get; internal set; } = 0.15;

        /// <summary>
        /// What <c>softmaxTemperature</c> becomes under
        /// <see cref="Contracts.VoteSharpness.Blurred"/>. This is the pre-2026-08 shipped value, kept
        /// as a level so a player who preferred the flatter electorate can have it back.
        /// </summary>
        public double SoftmaxTemperatureBlurred { get; internal set; } = 0.35;

        /// <summary>
        /// What it becomes under <see cref="Contracts.VoteSharpness.Sharp"/>.
        /// </summary>
        /// <remarks>
        /// 0.10 rather than lower on purpose. On a five-district synthetic city the weakest party
        /// sits at 5.04% at 0.10 and 3.03% at 0.06, and <c>electionsPr.thresholdShare</c> is 5% — so
        /// a fourth, sharper level would predictably wipe small parties off a PR ballot and fire the
        /// <c>affinity.minPartyShare</c> prune path. Sharp is the sharpest level that does not.
        /// </remarks>
        public double SoftmaxTemperatureSharp { get; internal set; } = 0.10;

        /// <summary>Shares below this are zeroed and redistributed, so rounding noise is not reported.</summary>
        public double MinPartyShare { get; internal set; } = 0.001;

        /// <summary>Under FPTP, support this far behind second place migrates tactically.</summary>
        public double TacticalVotingThresholdFptp { get; internal set; } = 0.05;

        internal static AffinityTuning Read(TuningReader r, AffinityTuning d) => new AffinityTuning
        {
            DistanceKernel = r.Text("distanceKernel", d.DistanceKernel),
            DistanceKernelSigma = r.Num("distanceKernelSigma", d.DistanceKernelSigma),
            IssueWeight = r.Num("issueWeight", d.IssueWeight),
            BaseAffinity = r.Num("baseAffinity", d.BaseAffinity),
            IncumbencyBonus = r.Num("incumbencyBonus", d.IncumbencyBonus),
            IncumbencyDiscontentPenalty = r.Num("incumbencyDiscontentPenalty", d.IncumbencyDiscontentPenalty),
            MandatePerformanceWeight = r.Num("mandatePerformanceWeight", d.MandatePerformanceWeight),
            MandateFailurePenalty = r.Num("mandateFailurePenalty", d.MandateFailurePenalty),
            EventModifierWeight = r.Num("eventModifierWeight", d.EventModifierWeight),
            EventModifierWeightMuted = r.Num("eventModifierWeightMuted", d.EventModifierWeightMuted),
            EventModifierWeightLoud = r.Num("eventModifierWeightLoud", d.EventModifierWeightLoud),
            EventModifierDecayHalfLifeMonths = r.Int("eventModifierDecayHalfLifeMonths", d.EventModifierDecayHalfLifeMonths),
            LocalGrievanceWeight = r.Num("localGrievanceWeight", d.LocalGrievanceWeight),
            NationalMoodWeight = r.Num("nationalMoodWeight", d.NationalMoodWeight),
            HabitualLoyalty = r.Num("habitualLoyalty", d.HabitualLoyalty),
            LoyaltyDecayPerMonth = r.Num("loyaltyDecayPerMonth", d.LoyaltyDecayPerMonth),
            NoiseSigma = r.Num("noiseSigma", d.NoiseSigma),
            NoiseClamp = r.Num("noiseClamp", d.NoiseClamp),
            SoftmaxTemperature = r.Num("softmaxTemperature", d.SoftmaxTemperature),
            SoftmaxTemperatureBlurred = r.Num("softmaxTemperatureBlurred", d.SoftmaxTemperatureBlurred),
            SoftmaxTemperatureSharp = r.Num("softmaxTemperatureSharp", d.SoftmaxTemperatureSharp),
            MinPartyShare = r.Num("minPartyShare", d.MinPartyShare),
            TacticalVotingThresholdFptp = r.Num("tacticalVotingThresholdFptp", d.TacticalVotingThresholdFptp)
        };
    }

    /// <summary>Packet 5 — turnout. JSON section <c>turnout</c>.</summary>
    public sealed class TurnoutTuning
    {
        public double Base { get; internal set; } = 0.55;

        /// <summary>Turnout gain per unit of happiness above <see cref="ReferenceHappiness"/>, normalised.</summary>
        public double HappinessCoefficient { get; internal set; } = 0.20;

        public double EducationCoefficient { get; internal set; } = 0.15;
        public double WealthCoefficient { get; internal set; } = 0.08;

        /// <summary>Negative by default: discontent suppresses turnout more often than it mobilises it.</summary>
        public double DiscontentCoefficient { get; internal set; } = -0.10;

        /// <summary>Turnout gain when the race is close, scaled by the top-two margin.</summary>
        public double CompetitivenessCoefficient { get; internal set; } = 0.12;

        public double CampaignIntensityCoefficient { get; internal set; } = 0.05;

        /// <summary>Turnout lost per completed term of the same incumbent.</summary>
        public double IncumbentTermFatigue { get; internal set; } = 0.03;

        /// <summary>
        /// Per-age-band multiplier. Child and teen are 0, which is how minors are disenfranchised —
        /// the blocs still exist, they simply cast no votes.
        /// </summary>
        public AgeBandMultipliers AgeBandMultipliers { get; internal set; } =
            new AgeBandMultipliers(0.0, 0.0, 1.0, 1.10);

        /// <summary>Sigma of the <c>voter.turnout.noise</c> draw.</summary>
        public double NoiseSigma { get; internal set; } = 0.02;

        public double Floor { get; internal set; } = 0.10;
        public double Ceiling { get; internal set; } = 0.95;

        public double ReferenceHappiness { get; internal set; } = 50.0;
        public double ReferenceEducationIndex { get; internal set; } = 0.50;

        /// <summary>Turnout lost at a snap election called mid-term.</summary>
        public double SnapElectionPenalty { get; internal set; } = 0.03;

        internal static TurnoutTuning Read(TuningReader r, TurnoutTuning d) => new TurnoutTuning
        {
            Base = r.Num("base", d.Base),
            HappinessCoefficient = r.Num("happinessCoefficient", d.HappinessCoefficient),
            EducationCoefficient = r.Num("educationCoefficient", d.EducationCoefficient),
            WealthCoefficient = r.Num("wealthCoefficient", d.WealthCoefficient),
            DiscontentCoefficient = r.Num("discontentCoefficient", d.DiscontentCoefficient),
            CompetitivenessCoefficient = r.Num("competitivenessCoefficient", d.CompetitivenessCoefficient),
            CampaignIntensityCoefficient = r.Num("campaignIntensityCoefficient", d.CampaignIntensityCoefficient),
            IncumbentTermFatigue = r.Num("incumbentTermFatigue", d.IncumbentTermFatigue),
            AgeBandMultipliers = r.Ages("ageBandMultipliers", d.AgeBandMultipliers),
            NoiseSigma = r.Num("noiseSigma", d.NoiseSigma),
            Floor = r.Num("floor", d.Floor),
            Ceiling = r.Num("ceiling", d.Ceiling),
            ReferenceHappiness = r.Num("referenceHappiness", d.ReferenceHappiness),
            ReferenceEducationIndex = r.Num("referenceEducationIndex", d.ReferenceEducationIndex),
            SnapElectionPenalty = r.Num("snapElectionPenalty", d.SnapElectionPenalty)
        };
    }

    /// <summary>Packet 6 — published polls and their deliberate error. JSON section <c>polling</c>.</summary>
    public sealed class PollingTuning
    {
        /// <summary>Sigma of the unbiased component of poll error.</summary>
        public double ErrorSigma { get; internal set; } = 0.030;

        /// <summary>
        /// How much low-education districts are under-sampled. Positive by contract — the harness
        /// asserts the *direction*, not the magnitude (§3 Campaigns).
        /// </summary>
        public double EducationUnderSampleBias { get; internal set; } = 0.040;

        /// <summary>How much low-turnout districts are under-sampled. Positive by contract.</summary>
        public double TurnoutUnderSampleBias { get; internal set; } = 0.025;

        /// <summary>Sigma of the per-pollster constant offset, stable across a campaign.</summary>
        public double HouseEffectSigma { get; internal set; } = 0.015;

        public int SampleSizeBase { get; internal set; } = 1000;
        public double SampleSizeVariance { get; internal set; } = 0.20;

        /// <summary>Multiplier on the standard error for the published margin (1.96 ≈ 95%).</summary>
        public double MarginOfErrorMultiplier { get; internal set; } = 1.96;

        public int PublishIntervalDays { get; internal set; } = 7;

        /// <summary>
        /// Length of the polling season. 9 weeks is a shade over the 2-month campaign, so the poll
        /// season opens a few days before the campaign flag does — the same slack the old
        /// 26-weeks-against-6-months pairing had (§3).
        /// </summary>
        public int CampaignWeeks { get; internal set; } = 9;

        public int WeeksBeforeElection { get; internal set; } = 9;

        /// <summary>How strongly pollsters converge on each other near election day.</summary>
        public double HerdingFactor { get; internal set; } = 0.20;

        /// <summary>Fraction of the error that has decayed by election day.</summary>
        public double ErrorDecayTowardElection { get; internal set; } = 0.60;

        public double UndecidedShareBase { get; internal set; } = 0.15;
        public double UndecidedDecayPerWeek { get; internal set; } = 0.08;

        public double MinPublishedShare { get; internal set; } = 0.005;

        /// <summary>Published shares are rounded to this many decimals before storage.</summary>
        public int RoundingDecimals { get; internal set; } = 3;

        public int MaxStoredPolls { get; internal set; } = 60;

        /// <summary>Distinct pollsters, each with its own stable house effect.</summary>
        public int PollsterCount { get; internal set; } = 3;

        internal static PollingTuning Read(TuningReader r, PollingTuning d) => new PollingTuning
        {
            ErrorSigma = r.Num("errorSigma", d.ErrorSigma),
            EducationUnderSampleBias = r.Num("educationUnderSampleBias", d.EducationUnderSampleBias),
            TurnoutUnderSampleBias = r.Num("turnoutUnderSampleBias", d.TurnoutUnderSampleBias),
            HouseEffectSigma = r.Num("houseEffectSigma", d.HouseEffectSigma),
            SampleSizeBase = r.Int("sampleSizeBase", d.SampleSizeBase),
            SampleSizeVariance = r.Num("sampleSizeVariance", d.SampleSizeVariance),
            MarginOfErrorMultiplier = r.Num("marginOfErrorMultiplier", d.MarginOfErrorMultiplier),
            PublishIntervalDays = r.Int("publishIntervalDays", d.PublishIntervalDays),
            CampaignWeeks = r.Int("campaignWeeks", d.CampaignWeeks),
            WeeksBeforeElection = r.Int("weeksBeforeElection", d.WeeksBeforeElection),
            HerdingFactor = r.Num("herdingFactor", d.HerdingFactor),
            ErrorDecayTowardElection = r.Num("errorDecayTowardElection", d.ErrorDecayTowardElection),
            UndecidedShareBase = r.Num("undecidedShareBase", d.UndecidedShareBase),
            UndecidedDecayPerWeek = r.Num("undecidedDecayPerWeek", d.UndecidedDecayPerWeek),
            MinPublishedShare = r.Num("minPublishedShare", d.MinPublishedShare),
            RoundingDecimals = r.Int("roundingDecimals", d.RoundingDecimals),
            MaxStoredPolls = r.Int("maxStoredPolls", d.MaxStoredPolls),
            PollsterCount = r.Int("pollsterCount", d.PollsterCount)
        };
    }

    /// <summary>Packet 7 — proportional elections (EU theme). JSON section <c>electionsPr</c>.</summary>
    public sealed class ElectionsPrTuning
    {
        public int TermYears { get; internal set; } = 1;

        /// <summary>Chamber size when <see cref="SeatsPerPopulation"/> is 0.</summary>
        public int TotalSeats { get; internal set; } = 60;

        /// <summary>Seats per head of population. 0 keeps the chamber fixed at <see cref="TotalSeats"/>.</summary>
        public double SeatsPerPopulation { get; internal set; } = 0.0;

        public int MinSeats { get; internal set; } = 21;
        public int MaxSeats { get; internal set; } = 120;

        /// <summary>Vote share below which a party wins no seats.</summary>
        public double ThresholdShare { get; internal set; } = 0.05;

        /// <summary>Allocation method: <c>sainte-lague</c>, <c>d-hondt</c> or <c>largest-remainder</c>.</summary>
        public string Method { get; internal set; } = "sainte-lague";

        /// <summary>First divisor for modified Sainte-Laguë. 1.0 makes it unmodified.</summary>
        public double FirstDivisor { get; internal set; } = 1.40;

        /// <summary>Fraction of seats awarded in district contests. 0 is a pure list system.</summary>
        public double DistrictSeatShare { get; internal set; } = 0.0;

        public int CampaignMonths { get; internal set; } = 2;

        /// <summary>
        /// A snap election cannot be called until this long after the last one. Must stay well under
        /// <see cref="TermYears"/> × 12 — at 12 months against a 1-year term it equalled a whole
        /// term and made snap elections structurally impossible.
        /// </summary>
        public int SnapElectionMinMonthsSinceLast { get; internal set; } = 4;

        public int SnapElectionDelayMonths { get; internal set; } = 1;

        /// <summary>Seats a party gets once it clears the threshold, before proportional allocation.</summary>
        public int MinSeatsForRepresentation { get; internal set; } = 1;

        internal static ElectionsPrTuning Read(TuningReader r, ElectionsPrTuning d) => new ElectionsPrTuning
        {
            TermYears = r.Int("termYears", d.TermYears),
            TotalSeats = r.Int("totalSeats", d.TotalSeats),
            SeatsPerPopulation = r.Num("seatsPerPopulation", d.SeatsPerPopulation),
            MinSeats = r.Int("minSeats", d.MinSeats),
            MaxSeats = r.Int("maxSeats", d.MaxSeats),
            ThresholdShare = r.Num("thresholdShare", d.ThresholdShare),
            Method = r.Text("method", d.Method),
            FirstDivisor = r.Num("firstDivisor", d.FirstDivisor),
            DistrictSeatShare = r.Num("districtSeatShare", d.DistrictSeatShare),
            CampaignMonths = r.Int("campaignMonths", d.CampaignMonths),
            SnapElectionMinMonthsSinceLast = r.Int("snapElectionMinMonthsSinceLast", d.SnapElectionMinMonthsSinceLast),
            SnapElectionDelayMonths = r.Int("snapElectionDelayMonths", d.SnapElectionDelayMonths),
            MinSeatsForRepresentation = r.Int("minSeatsForRepresentation", d.MinSeatsForRepresentation)
        };
    }

    /// <summary>Packet 8 — FPTP district races and the mayoralty (NA theme). JSON section <c>electionsFptp</c>.</summary>
    public sealed class ElectionsFptpTuning
    {
        public int TermYears { get; internal set; } = 1;
        public int MayorTermYears { get; internal set; } = 1;

        public int CouncilSeatsPerDistrict { get; internal set; } = 1;

        /// <summary>Floor on chamber size when the city has few districts. Extra seats are at-large.</summary>
        public int MinCouncilSeats { get; internal set; } = 9;

        public int MaxCouncilSeats { get; internal set; } = 45;

        public int CampaignMonths { get; internal set; } = 2;

        /// <summary>Wasted-vote squeeze applied to parties running third in a district.</summary>
        public double ThirdPartyPenalty { get; internal set; } = 0.35;

        public double IncumbentMayorBonus { get; internal set; } = 0.04;

        /// <summary>Sigma of the per-district swing draw, on top of the modelled shares.</summary>
        public double DistrictSwingSigma { get; internal set; } = 0.02;

        /// <summary>How much the mayoral result pulls the council races (coattails), 0–1.</summary>
        public double StraightTicketFactor { get; internal set; } = 0.70;

        /// <summary>Mayoral share needed to avoid a runoff. 0 means plurality wins outright.</summary>
        public double MayorRunoffThreshold { get; internal set; } = 0.0;

        /// <summary>Margins below this go to the <c>election.tiebreak</c> stream.</summary>
        public double TieMarginEpsilon { get; internal set; } = 1e-9;

        /// <summary>
        /// AGORA-SEAM(§14.1): NA primaries as full elections is an open decision. Ships false and the
        /// FPTP packet must not implement a primary until it closes; faction dominance stands in.
        /// </summary>
        public bool PrimariesEnabled { get; internal set; } = false;

        internal static ElectionsFptpTuning Read(TuningReader r, ElectionsFptpTuning d) => new ElectionsFptpTuning
        {
            TermYears = r.Int("termYears", d.TermYears),
            MayorTermYears = r.Int("mayorTermYears", d.MayorTermYears),
            CouncilSeatsPerDistrict = r.Int("councilSeatsPerDistrict", d.CouncilSeatsPerDistrict),
            MinCouncilSeats = r.Int("minCouncilSeats", d.MinCouncilSeats),
            MaxCouncilSeats = r.Int("maxCouncilSeats", d.MaxCouncilSeats),
            CampaignMonths = r.Int("campaignMonths", d.CampaignMonths),
            ThirdPartyPenalty = r.Num("thirdPartyPenalty", d.ThirdPartyPenalty),
            IncumbentMayorBonus = r.Num("incumbentMayorBonus", d.IncumbentMayorBonus),
            DistrictSwingSigma = r.Num("districtSwingSigma", d.DistrictSwingSigma),
            StraightTicketFactor = r.Num("straightTicketFactor", d.StraightTicketFactor),
            MayorRunoffThreshold = r.Num("mayorRunoffThreshold", d.MayorRunoffThreshold),
            TieMarginEpsilon = r.Num("tieMarginEpsilon", d.TieMarginEpsilon),
            PrimariesEnabled = r.Flag("primariesEnabled", d.PrimariesEnabled)
        };
    }

    /// <summary>Packet 9 — coalition formation, stability and collapse. JSON section <c>coalitions</c>.</summary>
    public sealed class CoalitionsTuning
    {
        public double MinSeatShareToGovern { get; internal set; } = 0.50;

        public int FormationMaxPartners { get; internal set; } = 4;
        public int FormationAttemptsMax { get; internal set; } = 3;
        public int FormationWindowMonths { get; internal set; } = 3;

        /// <summary>Two parties further apart than this will not sit together.</summary>
        public double IdeologicalDistanceCap { get; internal set; } = 0.55;

        /// <summary>Weight of ideological closeness when ranking candidate coalitions.</summary>
        public double DistanceWeight { get; internal set; } = 0.60;

        /// <summary>Weight of combined seat share when ranking candidate coalitions.</summary>
        public double SizeWeight { get; internal set; } = 0.40;

        /// <summary>Slack granted to a two-largest-parties coalition when nothing else works.</summary>
        public double GrandCoalitionDistanceBonus { get; internal set; } = 0.10;

        public double LeadPartyMinSeatShare { get; internal set; } = 0.20;

        public bool MinorityGovernmentAllowed { get; internal set; } = true;
        public double MinorityGovernmentPenalty { get; internal set; } = 0.25;

        public double CohesionBase { get; internal set; } = 0.75;
        public double CohesionDistancePenalty { get; internal set; } = 0.50;

        public double StabilityInitial { get; internal set; } = 0.80;
        public double StabilityDecayPerMonth { get; internal set; } = 0.01;
        public double StabilityShockPerFailedMandate { get; internal set; } = 0.08;

        /// <summary>Stability lost per point of event severity, for events above the major threshold.</summary>
        public double StabilityShockPerSeverityPoint { get; internal set; } = 0.03;

        public double StabilityRecoveryPerFulfilledMandate { get; internal set; } = 0.05;

        public double CollapseThreshold { get; internal set; } = 0.30;
        public int CollapseCheckIntervalMonths { get; internal set; } = 1;
        public int SnapElectionDelayMonths { get; internal set; } = 1;

        internal static CoalitionsTuning Read(TuningReader r, CoalitionsTuning d) => new CoalitionsTuning
        {
            MinSeatShareToGovern = r.Num("minSeatShareToGovern", d.MinSeatShareToGovern),
            FormationMaxPartners = r.Int("formationMaxPartners", d.FormationMaxPartners),
            FormationAttemptsMax = r.Int("formationAttemptsMax", d.FormationAttemptsMax),
            FormationWindowMonths = r.Int("formationWindowMonths", d.FormationWindowMonths),
            IdeologicalDistanceCap = r.Num("ideologicalDistanceCap", d.IdeologicalDistanceCap),
            DistanceWeight = r.Num("distanceWeight", d.DistanceWeight),
            SizeWeight = r.Num("sizeWeight", d.SizeWeight),
            GrandCoalitionDistanceBonus = r.Num("grandCoalitionDistanceBonus", d.GrandCoalitionDistanceBonus),
            LeadPartyMinSeatShare = r.Num("leadPartyMinSeatShare", d.LeadPartyMinSeatShare),
            MinorityGovernmentAllowed = r.Flag("minorityGovernmentAllowed", d.MinorityGovernmentAllowed),
            MinorityGovernmentPenalty = r.Num("minorityGovernmentPenalty", d.MinorityGovernmentPenalty),
            CohesionBase = r.Num("cohesionBase", d.CohesionBase),
            CohesionDistancePenalty = r.Num("cohesionDistancePenalty", d.CohesionDistancePenalty),
            StabilityInitial = r.Num("stabilityInitial", d.StabilityInitial),
            StabilityDecayPerMonth = r.Num("stabilityDecayPerMonth", d.StabilityDecayPerMonth),
            StabilityShockPerFailedMandate = r.Num("stabilityShockPerFailedMandate", d.StabilityShockPerFailedMandate),
            StabilityShockPerSeverityPoint = r.Num("stabilityShockPerSeverityPoint", d.StabilityShockPerSeverityPoint),
            StabilityRecoveryPerFulfilledMandate = r.Num("stabilityRecoveryPerFulfilledMandate", d.StabilityRecoveryPerFulfilledMandate),
            CollapseThreshold = r.Num("collapseThreshold", d.CollapseThreshold),
            CollapseCheckIntervalMonths = r.Int("collapseCheckIntervalMonths", d.CollapseCheckIntervalMonths),
            SnapElectionDelayMonths = r.Int("snapElectionDelayMonths", d.SnapElectionDelayMonths)
        };
    }

    /// <summary>Packet 10 — mandate generation, monitoring and resolution. JSON section <c>mandates</c>.</summary>
    public sealed class MandatesTuning
    {
        public int CountPerTerm { get; internal set; } = 2;
        public int MaxActive { get; internal set; } = 6;

        /// <summary>A metric must be at least this far from its city-wide best to justify a promise.</summary>
        public double MinDeficitToGenerate { get; internal set; } = 0.15;

        /// <summary>Fraction of the deficit the promise commits to closing.</summary>
        public double TargetImprovementFraction { get; internal set; } = 0.20;

        /// <summary>
        /// Must not exceed the term length. A mandate that outlives the government that issued it is
        /// abandoned unscored at the next election, and defiance is the largest single input to the
        /// <c>fringe</c> failure score.
        /// </summary>
        public int HorizonMonths { get; internal set; } = 12;

        /// <summary>Months after issue before monitoring starts scoring.</summary>
        public int GraceMonths { get; internal set; } = 1;

        public int MonitoringIntervalMonths { get; internal set; } = 1;

        /// <summary>Progress at or above this at the deadline counts as partial, not defied.</summary>
        public double PartialCreditThreshold { get; internal set; } = 0.60;

        /// <summary>Fraction of mandates targeted at a single district rather than the whole city.</summary>
        public double DistrictMandateShare { get; internal set; } = 0.50;

        /// <summary>Happiness points granted on fulfilment. Applied through a capped effect.</summary>
        public double FulfilledHappinessBonus { get; internal set; } = 2.0;

        public double DefiedHappinessPenalty { get; internal set; } = 3.0;
        public double PartialHappinessBonus { get; internal set; } = 0.5;

        public double FulfilledLegitimacyBonus { get; internal set; } = 0.05;
        public double DefiedLegitimacyPenalty { get; internal set; } = 0.08;

        /// <summary>Vote share that swings to the opposition when a mandate is defied.</summary>
        public double OppositionSurgeOnDefiance { get; internal set; } = 0.04;

        public double UnrestEventProbabilityOnDefiance { get; internal set; } = 0.25;

        public int ResolutionEffectDurationMonths { get; internal set; } = 12;

        /// <summary>Floor on salience, so an unpopular promise still counts for something.</summary>
        public double SalienceFloor { get; internal set; } = 0.10;

        /// <summary>Months a mandate may sit unmeasurable before it is abandoned rather than failed.</summary>
        public int StalledMetricGraceMonths { get; internal set; } = 3;

        internal static MandatesTuning Read(TuningReader r, MandatesTuning d) => new MandatesTuning
        {
            CountPerTerm = r.Int("countPerTerm", d.CountPerTerm),
            MaxActive = r.Int("maxActive", d.MaxActive),
            MinDeficitToGenerate = r.Num("minDeficitToGenerate", d.MinDeficitToGenerate),
            TargetImprovementFraction = r.Num("targetImprovementFraction", d.TargetImprovementFraction),
            HorizonMonths = r.Int("horizonMonths", d.HorizonMonths),
            GraceMonths = r.Int("graceMonths", d.GraceMonths),
            MonitoringIntervalMonths = r.Int("monitoringIntervalMonths", d.MonitoringIntervalMonths),
            PartialCreditThreshold = r.Num("partialCreditThreshold", d.PartialCreditThreshold),
            DistrictMandateShare = r.Num("districtMandateShare", d.DistrictMandateShare),
            FulfilledHappinessBonus = r.Num("fulfilledHappinessBonus", d.FulfilledHappinessBonus),
            DefiedHappinessPenalty = r.Num("defiedHappinessPenalty", d.DefiedHappinessPenalty),
            PartialHappinessBonus = r.Num("partialHappinessBonus", d.PartialHappinessBonus),
            FulfilledLegitimacyBonus = r.Num("fulfilledLegitimacyBonus", d.FulfilledLegitimacyBonus),
            DefiedLegitimacyPenalty = r.Num("defiedLegitimacyPenalty", d.DefiedLegitimacyPenalty),
            OppositionSurgeOnDefiance = r.Num("oppositionSurgeOnDefiance", d.OppositionSurgeOnDefiance),
            UnrestEventProbabilityOnDefiance = r.Num("unrestEventProbabilityOnDefiance", d.UnrestEventProbabilityOnDefiance),
            ResolutionEffectDurationMonths = r.Int("resolutionEffectDurationMonths", d.ResolutionEffectDurationMonths),
            SalienceFloor = r.Num("salienceFloor", d.SalienceFloor),
            StalledMetricGraceMonths = r.Int("stalledMetricGraceMonths", d.StalledMetricGraceMonths)
        };
    }

    /// <summary>Packet 11 — timeline catalog loading and validation. JSON section <c>catalog</c>.</summary>
    public sealed class CatalogTuning
    {
        public int StartYear { get; internal set; } = 1990;

        /// <summary>Last year the curated catalogs cover. After this the procedural generator runs.</summary>
        public int CatalogEndYear { get; internal set; } = 2026;

        /// <summary>
        /// AGORA-SEAM(§14.2): fixed real dates vs seeded ±6 months is an open decision. Ships false
        /// with a zero window; the <c>event.jitter</c> stream exists but must not be drawn from until
        /// the decision closes.
        /// </summary>
        public bool JitterEnabled { get; internal set; } = false;

        /// <summary>Half-width of the jitter window in months. 0 while <see cref="JitterEnabled"/> is false.</summary>
        public int JitterMonths { get; internal set; } = 0;

        /// <summary>Effect magnitude multiplier per point of severity above 1.</summary>
        public double SeverityEffectScale { get; internal set; } = 0.20;

        public int SeverityMax { get; internal set; } = 5;

        /// <summary>Severity at or above which an event counts as major.</summary>
        public int MajorSeverityThreshold { get; internal set; } = 4;

        public int MinMonthsBetweenMajorEvents { get; internal set; } = 3;

        public int MaxConcurrentEvents { get; internal set; } = 6;

        public bool IncludeGlobal { get; internal set; } = true;
        public double GlobalEventWeight { get; internal set; } = 1.0;
        public double RegionalEventWeight { get; internal set; } = 1.0;

        /// <summary>Validator ceiling: no catalog magnitude may exceed this, before per-effect caps.</summary>
        public double EffectMagnitudeGlobalCap { get; internal set; } = 1.0;

        public int EffectDurationCapMonths { get; internal set; } = 240;

        /// <summary>
        /// AGORA-SEAM(§14.4): the post-2026 authorship split is an open decision. These keys size the
        /// proposed shape (engine picks the archetype, the LLM writes the prose); do not build a
        /// different split until it closes.
        /// </summary>
        public bool ProceduralEnabled { get; internal set; } = true;

        public int ProceduralStartYear { get; internal set; } = 2027;
        public double ProceduralEventsPerYear { get; internal set; } = 2.0;
        public int ProceduralArchetypeCount { get; internal set; } = 12;
        public double ProceduralSeverityMean { get; internal set; } = 2.5;
        public double ProceduralSeveritySigma { get; internal set; } = 0.8;

        internal static CatalogTuning Read(TuningReader r, CatalogTuning d) => new CatalogTuning
        {
            StartYear = r.Int("startYear", d.StartYear),
            CatalogEndYear = r.Int("catalogEndYear", d.CatalogEndYear),
            JitterEnabled = r.Flag("jitterEnabled", d.JitterEnabled),
            JitterMonths = r.Int("jitterMonths", d.JitterMonths),
            SeverityEffectScale = r.Num("severityEffectScale", d.SeverityEffectScale),
            SeverityMax = r.Int("severityMax", d.SeverityMax),
            MajorSeverityThreshold = r.Int("majorSeverityThreshold", d.MajorSeverityThreshold),
            MinMonthsBetweenMajorEvents = r.Int("minMonthsBetweenMajorEvents", d.MinMonthsBetweenMajorEvents),
            MaxConcurrentEvents = r.Int("maxConcurrentEvents", d.MaxConcurrentEvents),
            IncludeGlobal = r.Flag("includeGlobal", d.IncludeGlobal),
            GlobalEventWeight = r.Num("globalEventWeight", d.GlobalEventWeight),
            RegionalEventWeight = r.Num("regionalEventWeight", d.RegionalEventWeight),
            EffectMagnitudeGlobalCap = r.Num("effectMagnitudeGlobalCap", d.EffectMagnitudeGlobalCap),
            EffectDurationCapMonths = r.Int("effectDurationCapMonths", d.EffectDurationCapMonths),
            ProceduralEnabled = r.Flag("proceduralEnabled", d.ProceduralEnabled),
            ProceduralStartYear = r.Int("proceduralStartYear", d.ProceduralStartYear),
            ProceduralEventsPerYear = r.Num("proceduralEventsPerYear", d.ProceduralEventsPerYear),
            ProceduralArchetypeCount = r.Int("proceduralArchetypeCount", d.ProceduralArchetypeCount),
            ProceduralSeverityMean = r.Num("proceduralSeverityMean", d.ProceduralSeverityMean),
            ProceduralSeveritySigma = r.Num("proceduralSeveritySigma", d.ProceduralSeveritySigma)
        };
    }

    /// <summary>Packet 12 — the deterministic tick scheduler. JSON section <c>scheduler</c>.</summary>
    public sealed class SchedulerTuning
    {
        public int TickIntervalMonths { get; internal set; } = 1;
        public int SnapshotIntervalMonths { get; internal set; } = 1;

        /// <summary>
        /// AGORA-SEAM(§14.3): the retention default is proposed, not ratified. Keep the newest N and
        /// nothing cleverer until it closes.
        /// </summary>
        public int SnapshotRetention { get; internal set; } = 25;

        public int EventScanIntervalMonths { get; internal set; } = 1;
        public int MaxEventsPerTick { get; internal set; } = 3;

        public int LifecycleTickMonths { get; internal set; } = 12;
        public int IndicesTickMonths { get; internal set; } = 1;
        public int MandateMonitorIntervalMonths { get; internal set; } = 1;

        /// <summary>
        /// Months between poll publications. <c>1</c> means every political tick.
        /// </summary>
        /// <remarks>
        /// Was <c>pollTickIntervalDays</c>, defaulting to 7, and it never did anything: the planner
        /// computed <c>((date.Day - 1) % 7) == 0</c> and <c>SimDate.Day</c> is a literal <c>1</c> on
        /// every date the clock produces, so the expression was unconditionally true. There was no
        /// arithmetic slip to correct — a sim "day" IS a calendar month
        /// (<c>TimeSettingsData.m_DaysPerYear = 12</c>), so a day cadence had nothing to count. The
        /// default of <c>1</c> reproduces the behaviour every existing save has always had while
        /// making the dial mean something for the first time.
        /// </remarks>
        public int PollTickIntervalMonths { get; internal set; } = 1;
        public int CampaignStartMonthsBeforeElection { get; internal set; } = 2;

        public bool LlmWakeYearly { get; internal set; } = true;
        public bool LlmWakeOnElection { get; internal set; } = true;
        public bool LlmWakeManualEnabled { get; internal set; } = true;

        /// <summary>Month (1–12) the yearly LLM wake fires on.</summary>
        public int LlmWakeMonth { get; internal set; } = 1;

        /// <summary>
        /// Cap on deterministic fast-forward during load reconciliation (§5). Beyond this the engine
        /// snaps to current city state and logs, rather than burning minutes replaying.
        /// </summary>
        public int CatchUpMaxMonths { get; internal set; } = 120;

        /// <summary>Months of metric history collected before the first election may be scheduled.</summary>
        public int WarmupMonths { get; internal set; } = 6;

        internal static SchedulerTuning Read(TuningReader r, SchedulerTuning d) => new SchedulerTuning
        {
            TickIntervalMonths = r.Int("tickIntervalMonths", d.TickIntervalMonths),
            SnapshotIntervalMonths = r.Int("snapshotIntervalMonths", d.SnapshotIntervalMonths),
            SnapshotRetention = r.Int("snapshotRetention", d.SnapshotRetention),
            EventScanIntervalMonths = r.Int("eventScanIntervalMonths", d.EventScanIntervalMonths),
            MaxEventsPerTick = r.Int("maxEventsPerTick", d.MaxEventsPerTick),
            LifecycleTickMonths = r.Int("lifecycleTickMonths", d.LifecycleTickMonths),
            IndicesTickMonths = r.Int("indicesTickMonths", d.IndicesTickMonths),
            MandateMonitorIntervalMonths = r.Int("mandateMonitorIntervalMonths", d.MandateMonitorIntervalMonths),
            PollTickIntervalMonths = r.Int("pollTickIntervalMonths", d.PollTickIntervalMonths),
            CampaignStartMonthsBeforeElection = r.Int("campaignStartMonthsBeforeElection", d.CampaignStartMonthsBeforeElection),
            LlmWakeYearly = r.Flag("llmWakeYearly", d.LlmWakeYearly),
            LlmWakeOnElection = r.Flag("llmWakeOnElection", d.LlmWakeOnElection),
            LlmWakeManualEnabled = r.Flag("llmWakeManualEnabled", d.LlmWakeManualEnabled),
            LlmWakeMonth = r.Int("llmWakeMonth", d.LlmWakeMonth),
            CatchUpMaxMonths = r.Int("catchUpMaxMonths", d.CatchUpMaxMonths),
            WarmupMonths = r.Int("warmupMonths", d.WarmupMonths)
        };
    }

    /// <summary>Packet 13 — derived indices. JSON section <c>indices</c>.</summary>
    public sealed class IndicesTuning
    {
        /// <summary>EMA factor applied to every index, so a one-month spike does not read as a trend.</summary>
        public double SmoothingAlpha { get; internal set; } = 0.30;

        /// <summary>Histogram buckets used to approximate the Lorenz curve for Gini.</summary>
        public int GiniSampleBuckets { get; internal set; } = 20;

        public double GentrificationRentWeight { get; internal set; } = 0.50;
        public double GentrificationEducationWeight { get; internal set; } = 0.30;
        public double GentrificationTurnoverWeight { get; internal set; } = 0.20;
        public int GentrificationWindowMonths { get; internal set; } = 24;

        public double BrainDrainEducationWeight { get; internal set; } = 0.60;
        public double BrainDrainOutflowWeight { get; internal set; } = 0.40;
        public int BrainDrainWindowMonths { get; internal set; } = 12;

        public double CommuteMiseryTimeWeight { get; internal set; } = 0.60;
        public double CommuteMiseryCongestionWeight { get; internal set; } = 0.40;

        /// <summary>Commute length that scores 0 misery. Longer commutes scale up from here.</summary>
        public double CommuteMiseryReferenceMinutes { get; internal set; } = 25.0;

        /// <summary>Relative importance of each service when measuring service inequality.</summary>
        public ServiceCoverage ServiceInequalityWeights { get; internal set; } =
            new ServiceCoverage(1.0, 1.0, 1.0, 0.8, 0.8, 1.0, 0.6, 0.6, 0.5);

        public double PolarizationDispersionWeight { get; internal set; } = 1.0;

        public double DiscontentHappinessWeight { get; internal set; } = 0.50;
        public double DiscontentUnemploymentWeight { get; internal set; } = 0.30;
        public double DiscontentServiceWeight { get; internal set; } = 0.20;

        public double LegitimacyTurnoutWeight { get; internal set; } = 0.40;
        public double LegitimacyMandateWeight { get; internal set; } = 0.35;
        public double LegitimacyStabilityWeight { get; internal set; } = 0.25;

        public double ClampMin { get; internal set; } = 0.0;
        public double ClampMax { get; internal set; } = 1.0;

        internal static IndicesTuning Read(TuningReader r, IndicesTuning d) => new IndicesTuning
        {
            SmoothingAlpha = r.Num("smoothingAlpha", d.SmoothingAlpha),
            GiniSampleBuckets = r.Int("giniSampleBuckets", d.GiniSampleBuckets),
            GentrificationRentWeight = r.Num("gentrificationRentWeight", d.GentrificationRentWeight),
            GentrificationEducationWeight = r.Num("gentrificationEducationWeight", d.GentrificationEducationWeight),
            GentrificationTurnoverWeight = r.Num("gentrificationTurnoverWeight", d.GentrificationTurnoverWeight),
            GentrificationWindowMonths = r.Int("gentrificationWindowMonths", d.GentrificationWindowMonths),
            BrainDrainEducationWeight = r.Num("brainDrainEducationWeight", d.BrainDrainEducationWeight),
            BrainDrainOutflowWeight = r.Num("brainDrainOutflowWeight", d.BrainDrainOutflowWeight),
            BrainDrainWindowMonths = r.Int("brainDrainWindowMonths", d.BrainDrainWindowMonths),
            CommuteMiseryTimeWeight = r.Num("commuteMiseryTimeWeight", d.CommuteMiseryTimeWeight),
            CommuteMiseryCongestionWeight = r.Num("commuteMiseryCongestionWeight", d.CommuteMiseryCongestionWeight),
            CommuteMiseryReferenceMinutes = r.Num("commuteMiseryReferenceMinutes", d.CommuteMiseryReferenceMinutes),
            ServiceInequalityWeights = r.Services("serviceInequalityWeights", d.ServiceInequalityWeights),
            PolarizationDispersionWeight = r.Num("polarizationDispersionWeight", d.PolarizationDispersionWeight),
            DiscontentHappinessWeight = r.Num("discontentHappinessWeight", d.DiscontentHappinessWeight),
            DiscontentUnemploymentWeight = r.Num("discontentUnemploymentWeight", d.DiscontentUnemploymentWeight),
            DiscontentServiceWeight = r.Num("discontentServiceWeight", d.DiscontentServiceWeight),
            LegitimacyTurnoutWeight = r.Num("legitimacyTurnoutWeight", d.LegitimacyTurnoutWeight),
            LegitimacyMandateWeight = r.Num("legitimacyMandateWeight", d.LegitimacyMandateWeight),
            LegitimacyStabilityWeight = r.Num("legitimacyStabilityWeight", d.LegitimacyStabilityWeight),
            ClampMin = r.Num("clampMin", d.ClampMin),
            ClampMax = r.Num("clampMax", d.ClampMax)
        };
    }

    /// <summary>
    /// One sanctioned effect's declaration: what it maps to, how far it may go, how long it may last,
    /// and what happens if it cannot be applied (non-negotiable #5).
    /// </summary>
    /// <remarks>
    /// <see cref="Modifier"/> names a member of <c>Game.Areas.DistrictModifierType</c> or
    /// <c>Game.City.CityModifierType</c> (Scout 0001 §3). <c>Agora.Core</c> never resolves it —
    /// it is a string here and an enum lookup in <c>Agora.Mod/Effects</c>, which is what keeps the
    /// palette declarable in data without Core learning that the game exists.
    /// </remarks>
    public readonly struct EffectCap
    {
        public string EffectId { get; }
        public EffectScope Scope { get; }

        /// <summary>Game modifier member name this effect drives.</summary>
        public string Modifier { get; }

        /// <summary>Absolute magnitude ceiling. The sink clamps to ±this; never uncapped.</summary>
        public double MagnitudeCap { get; }

        /// <summary>Duration ceiling in months.</summary>
        public int DurationCapMonths { get; }

        /// <summary>Effect applied instead when this one cannot be. Empty means terminal.</summary>
        public string FallbackEffectId { get; }

        public EffectCap(string effectId, EffectScope scope, string modifier, double magnitudeCap,
                         int durationCapMonths, string fallbackEffectId)
        {
            EffectId = effectId;
            Scope = scope;
            Modifier = modifier;
            MagnitudeCap = magnitudeCap;
            DurationCapMonths = durationCapMonths;
            FallbackEffectId = fallbackEffectId;
        }

        /// <summary>Clamps a requested magnitude into <c>[-MagnitudeCap, +MagnitudeCap]</c>.</summary>
        public double ClampMagnitude(double requested)
        {
            if (double.IsNaN(requested)) return 0.0;
            if (requested > MagnitudeCap) return MagnitudeCap;
            if (requested < -MagnitudeCap) return -MagnitudeCap;
            return requested;
        }

        /// <summary>Clamps a requested duration into <c>[0, DurationCapMonths]</c>.</summary>
        public int ClampDuration(int requested)
        {
            if (requested < 0) return 0;
            return requested > DurationCapMonths ? DurationCapMonths : requested;
        }
    }

    /// <summary>Packet 14 — the sanctioned effect palette. JSON section <c>effects</c>.</summary>
    public sealed class EffectsTuning
    {
        /// <summary>Master switch. False computes politics but applies nothing to the city.</summary>
        public bool Enabled { get; internal set; } = true;

        /// <summary>Ceiling applied on top of every per-effect cap. Belt and braces.</summary>
        public double GlobalMagnitudeCap { get; internal set; } = 1.0;

        public int GlobalDurationCapMonths { get; internal set; } = 120;

        /// <summary>Magnitude multiplier per point of event severity above 1.</summary>
        public double SeverityMagnitudeScale { get; internal set; } = 0.20;

        /// <summary>How an active effect fades: <c>linear</c>, <c>exponential</c> or <c>step</c>.</summary>
        public string DecayCurve { get; internal set; } = "linear";

        public int DecayHalfLifeMonths { get; internal set; } = 6;

        /// <summary>Magnitudes below this are dropped rather than applied as noise.</summary>
        public double MinEffectiveMagnitude { get; internal set; } = 0.001;

        public int ReapplyIntervalMonths { get; internal set; } = 1;

        /// <summary>How overlapping effects on one modifier combine: <c>sum</c> or <c>max</c>.</summary>
        public string StackingMode { get; internal set; } = "sum";

        public int MaxStackedPerModifier { get; internal set; } = 4;

        /// <summary>Terminal fallback for a city-scoped effect that cannot be applied.</summary>
        public string DefaultFallbackCityEffectId { get; internal set; } = "city-tax-happiness";

        /// <summary>Terminal fallback for a district-scoped effect that cannot be applied.</summary>
        public string DefaultFallbackDistrictEffectId { get; internal set; } = "district-wellbeing";

        private Dictionary<string, EffectCap> _perEffect = DefaultPerEffect();
        private List<string> _effectIds = SortedIds(DefaultPerEffect());

        /// <summary>
        /// Every palette entry, sorted by effect id ascending. This is the closed registry
        /// (non-negotiable #4): an effect id not in this list does not exist.
        /// </summary>
        public IReadOnlyList<string> EffectIds => _effectIds;

        public bool TryGetEffect(string effectId, out EffectCap cap) => _perEffect.TryGetValue(effectId, out cap);

        /// <summary>
        /// The declaration for an effect, or a conservative default at the global caps when the id is
        /// unknown. Never returns an uncapped result.
        /// </summary>
        public EffectCap CapFor(string effectId, EffectScope scope)
        {
            if (_perEffect.TryGetValue(effectId, out EffectCap cap)) return cap;

            string fallback = scope == EffectScope.District
                ? DefaultFallbackDistrictEffectId
                : DefaultFallbackCityEffectId;

            return new EffectCap(effectId, scope, "", GlobalMagnitudeCap, GlobalDurationCapMonths, fallback);
        }

        internal static EffectsTuning Read(TuningReader r, EffectsTuning d)
        {
            var t = new EffectsTuning
            {
                Enabled = r.Flag("enabled", d.Enabled),
                GlobalMagnitudeCap = r.Num("globalMagnitudeCap", d.GlobalMagnitudeCap),
                GlobalDurationCapMonths = r.Int("globalDurationCapMonths", d.GlobalDurationCapMonths),
                SeverityMagnitudeScale = r.Num("severityMagnitudeScale", d.SeverityMagnitudeScale),
                DecayCurve = r.Text("decayCurve", d.DecayCurve),
                DecayHalfLifeMonths = r.Int("decayHalfLifeMonths", d.DecayHalfLifeMonths),
                MinEffectiveMagnitude = r.Num("minEffectiveMagnitude", d.MinEffectiveMagnitude),
                ReapplyIntervalMonths = r.Int("reapplyIntervalMonths", d.ReapplyIntervalMonths),
                StackingMode = r.Text("stackingMode", d.StackingMode),
                MaxStackedPerModifier = r.Int("maxStackedPerModifier", d.MaxStackedPerModifier),
                DefaultFallbackCityEffectId = r.Text("defaultFallbackCityEffectId", d.DefaultFallbackCityEffectId),
                DefaultFallbackDistrictEffectId = r.Text("defaultFallbackDistrictEffectId", d.DefaultFallbackDistrictEffectId)
            };

            TuningReader per = r.Section("perEffect");
            IReadOnlyList<string> ids = per.ChildKeys();

            if (ids.Count == 0)
            {
                t._perEffect = DefaultPerEffect();
                t._effectIds = SortedIds(t._perEffect);
                return t;
            }

            var map = new Dictionary<string, EffectCap>(StringComparer.Ordinal);
            for (int i = 0; i < ids.Count; i++)
            {
                string id = ids[i];
                TuningReader e = per.Section(id);

                string scopeText = e.Text("scope", "city");
                EffectScope scope = string.Equals(scopeText, "district", StringComparison.Ordinal)
                    ? EffectScope.District
                    : EffectScope.City;

                string defaultFallback = scope == EffectScope.District
                    ? t.DefaultFallbackDistrictEffectId
                    : t.DefaultFallbackCityEffectId;

                // A terminal fallback must not point at itself, or the sink loops forever.
                if (string.Equals(id, defaultFallback, StringComparison.Ordinal)) defaultFallback = "";

                map[id] = new EffectCap(
                    id,
                    scope,
                    e.Text("modifier", ""),
                    e.Num("magnitudeCap", t.GlobalMagnitudeCap),
                    e.Int("durationCapMonths", t.GlobalDurationCapMonths),
                    e.Text("fallbackEffectId", defaultFallback));
            }

            t._perEffect = map;
            t._effectIds = SortedIds(map);
            return t;
        }

        private static List<string> SortedIds(Dictionary<string, EffectCap> map)
        {
            var ids = new List<string>(map.Count);
            foreach (var kv in map) ids.Add(kv.Key);
            ids.Sort(StringComparer.Ordinal);
            return ids;
        }

        /// <summary>
        /// The shipped palette, mirroring <c>data/engine_tuning.json</c>. Present so tests and a
        /// missing-file load both see the same registry the game would.
        /// </summary>
        private static Dictionary<string, EffectCap> DefaultPerEffect()
        {
            var m = new Dictionary<string, EffectCap>(StringComparer.Ordinal);

            void District(string id, string modifier, double cap, int months, string fallback) =>
                m[id] = new EffectCap(id, EffectScope.District, modifier, cap, months, fallback);

            void City(string id, string modifier, double cap, int months, string fallback) =>
                m[id] = new EffectCap(id, EffectScope.City, modifier, cap, months, fallback);

            const string DistrictFallback = "district-wellbeing";
            const string CityFallback = "city-tax-happiness";

            // Game.Areas.DistrictModifierType — 14 members, the scope the design cares most about.
            District("district-wellbeing", "Wellbeing", 0.15, 60, "");
            District("district-crime-accumulation", "CrimeAccumulation", 0.25, 48, DistrictFallback);
            District("district-garbage-production", "GarbageProduction", 0.20, 36, DistrictFallback);
            District("district-building-upkeep", "BuildingUpkeep", 0.20, 36, DistrictFallback);
            District("district-parking-fee", "ParkingFee", 0.30, 36, DistrictFallback);
            District("district-low-commercial-tax", "LowCommercialTax", 0.20, 36, DistrictFallback);
            District("district-energy-awareness", "EnergyConsumptionAwareness", 0.20, 36, DistrictFallback);
            District("district-product-consumption", "ProductConsumption", 0.15, 36, DistrictFallback);
            District("district-street-traffic-safety", "StreetTrafficSafety", 0.20, 36, DistrictFallback);
            District("district-building-fire-hazard", "BuildingFireHazard", 0.20, 24, DistrictFallback);
            District("district-bike-probability", "BikeProbability", 0.25, 36, DistrictFallback);
            District("district-car-reserve-probability", "CarReserveProbability", 0.20, 36, DistrictFallback);

            // Game.City.CityModifierType — 40 members.
            City("city-tax-happiness", "TaxHappiness", 0.15, 60, "");
            City("city-attractiveness", "Attractiveness", 0.25, 60, CityFallback);
            City("city-entertainment", "Entertainment", 0.20, 36, CityFallback);
            City("city-park-entertainment", "ParkEntertainment", 0.20, 36, CityFallback);
            City("city-crime-accumulation", "CrimeAccumulation", 0.25, 48, CityFallback);
            City("city-crime-probability", "CrimeProbability", 0.25, 48, CityFallback);
            City("city-crime-response-time", "CrimeResponseTime", 0.25, 36, CityFallback);
            City("city-criminal-monitor", "CriminalMonitorProbability", 0.25, 36, CityFallback);
            City("city-prison-time", "PrisonTime", 0.25, 36, CityFallback);
            City("city-disease-probability", "DiseaseProbability", 0.20, 36, CityFallback);
            City("city-pollution-health-affect", "PollutionHealthAffect", 0.20, 36, CityFallback);
            City("city-hospital-efficiency", "HospitalEfficiency", 0.20, 36, CityFallback);
            City("city-college-graduation", "CollegeGraduation", 0.20, 60, CityFallback);
            City("city-university-graduation", "UniversityGraduation", 0.20, 60, CityFallback);
            City("city-university-interest", "UniversityInterest", 0.20, 60, CityFallback);
            City("city-industrial-garbage", "IndustrialGarbage", 0.20, 36, CityFallback);
            City("city-industrial-air-pollution", "IndustrialAirPollution", 0.25, 48, CityFallback);
            City("city-industrial-ground-pollution", "IndustrialGroundPollution", 0.25, 48, CityFallback);
            City("city-service-building-upkeep", "CityServiceBuildingBaseUpkeepCost", 0.20, 36, CityFallback);
            City("city-loan-interest", "LoanInterest", 0.30, 60, CityFallback);
            City("city-import-cost", "ImportCost", 0.30, 48, CityFallback);
            City("city-export-cost", "ExportCost", 0.30, 48, CityFallback);
            City("city-service-import-cost", "CityServiceImportCost", 0.25, 48, CityFallback);
            City("city-industrial-efficiency", "IndustrialEfficiency", 0.30, 24, CityFallback);
            City("city-office-efficiency", "OfficeEfficiency", 0.30, 24, CityFallback);
            City("city-telecom-capacity", "TelecomCapacity", 0.20, 36, CityFallback);
            City("city-disaster-warning-time", "DisasterWarningTime", 0.25, 36, CityFallback);
            City("city-disaster-damage-rate", "DisasterDamageRate", 0.20, 36, CityFallback);
            City("city-taxi-starting-fee", "TaxiStartingFee", 0.25, 36, CityFallback);
            City("city-oil-resource-amount", "OilResourceAmount", 0.20, 60, CityFallback);
            City("city-ore-resource-amount", "OreResourceAmount", 0.20, 60, CityFallback);

            return m;
        }
    }

    /// <summary>
    /// Packet 15 — the fringe-party ceiling. JSON section <c>fringe</c>.
    ///
    /// <para>
    /// NA/FPTP only, and the enforcement point checks the system before any of this is read. It exists
    /// because the two-party system was not actually behaving like one: a bloc's support is a softmax
    /// over affinity, affinity differences between parties are small next to
    /// <c>affinity.softmaxTemperature</c>, and neither the incumbency nor the mandate term can hand a
    /// fringe party anything — both are party-scoped and can only subtract from the incumbent. So a
    /// minor party collected a large share simply for existing, and no amount of good government
    /// pushed it back down.
    /// </para>
    ///
    /// <para>
    /// The fix is a ceiling that starts shut and opens only on a record of failure, so a third party
    /// becomes viable the way it does in reality — because the establishment earned it.
    /// </para>
    /// </summary>
    public sealed class FringeTuning
    {
        /// <summary>Master switch. False makes the whole packet inert and is the control in tests.</summary>
        public bool Enabled { get; internal set; } = true;

        /// <summary>
        /// Share a minor party is pinned at while the ceiling is shut. Must stay above
        /// <c>parties.deathVoteShareThreshold</c>, or the suppression kills the parties it suppresses.
        /// </summary>
        public double BaseCeiling { get; internal set; } = 0.03;

        /// <summary>Ceiling at full unlock. Far enough to displace a major, which is the point.</summary>
        public double MaxCeiling { get; internal set; } = 0.40;

        /// <summary>Consecutive failure terms before the ceiling moves off <see cref="BaseCeiling"/> at all.</summary>
        public int UnlockConsecutiveTerms { get; internal set; } = 3;

        /// <summary>Consecutive failure terms at which the streak factor reaches 1.</summary>
        public int FullUnlockTerms { get; internal set; } = 6;

        /// <summary>Failure score at or above which a closed term counts as a failure term.</summary>
        public double FailureTermScoreThreshold { get; internal set; } = 0.50;

        /// <summary>Weight of defied major-party mandates in the failure score.</summary>
        public double DefianceWeight { get; internal set; } = 0.40;

        /// <summary>Weight of sustained city discontent in the failure score.</summary>
        public double DiscontentWeight { get; internal set; } = 0.35;

        /// <summary>Weight of government and mayoral turnover in the failure score.</summary>
        public double ChurnWeight { get; internal set; } = 0.25;

        /// <summary>
        /// Summed opposition surge that saturates the defiance signal. 0.08 is two full-salience
        /// defiances at <c>mandates.oppositionSurgeOnDefiance</c>.
        /// </summary>
        public double DefianceSurgeForFullSignal { get; internal set; } = 0.08;

        /// <summary>Mean discontent below which the discontent signal reads zero.</summary>
        public double DiscontentFloor { get; internal set; } = 0.50;

        /// <summary>Collapses plus mayoral changes in one term that saturate the churn signal.</summary>
        public int ChurnEventsForFullSignal { get; internal set; } = 2;

        /// <summary>
        /// City grievance on a fringe party's own core issue below which its ceiling stays shut however
        /// badly the majors governed. Deliberately equal to <c>parties.revivalGrievanceThreshold</c>:
        /// "aggrieved enough to revive a dead brand" and "aggrieved enough to lift a ceiling" should be
        /// the same bar.
        /// </summary>
        public double GrievanceFloor { get; internal set; } = 0.35;

        internal static FringeTuning Read(TuningReader r, FringeTuning d) => new FringeTuning
        {
            Enabled = r.Flag("enabled", d.Enabled),
            BaseCeiling = r.Num("baseCeiling", d.BaseCeiling),
            MaxCeiling = r.Num("maxCeiling", d.MaxCeiling),
            UnlockConsecutiveTerms = r.Int("unlockConsecutiveTerms", d.UnlockConsecutiveTerms),
            FullUnlockTerms = r.Int("fullUnlockTerms", d.FullUnlockTerms),
            FailureTermScoreThreshold = r.Num("failureTermScoreThreshold", d.FailureTermScoreThreshold),
            DefianceWeight = r.Num("defianceWeight", d.DefianceWeight),
            DiscontentWeight = r.Num("discontentWeight", d.DiscontentWeight),
            ChurnWeight = r.Num("churnWeight", d.ChurnWeight),
            DefianceSurgeForFullSignal = r.Num("defianceSurgeForFullSignal", d.DefianceSurgeForFullSignal),
            DiscontentFloor = r.Num("discontentFloor", d.DiscontentFloor),
            ChurnEventsForFullSignal = r.Int("churnEventsForFullSignal", d.ChurnEventsForFullSignal),
            GrievanceFloor = r.Num("grievanceFloor", d.GrievanceFloor)
        };
    }

    /// <summary>Packet 16 — the story cycle. JSON section <c>stories</c>.</summary>
    public sealed class StoriesTuning
    {
        /// <summary>Master switch. False makes the whole packet inert and is the control in tests.</summary>
        public bool Enabled { get; internal set; } = true;

        /// <summary>Stories drafted per cycle.</summary>
        public int StoriesPerCycle { get; internal set; } = 2;

        /// <summary>Events bundled into one story: one major and two minors.</summary>
        public int EventsPerStory { get; internal set; } = 3;

        /// <summary>
        /// The length of one full cycle in months. 2 means "draft on M, resolve on M+1, next batch
        /// at M+2" — so the draft-to-resolution gap is <c>CycleMonths - 1</c>, not
        /// <see cref="CycleMonths"/>.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The summary of this field previously read "months from draft to resolution", which
        /// contradicted its own worked example by exactly one month. The example is the authority and
        /// always was: the cycle is the *period*, and a resolution lands one month after the draft.
        /// </para>
        /// <remarks>
        /// <b>Not a day count, and there is no day-15 alternative.</b> CS2 ships
        /// <c>m_DaysPerYear = 12</c>, so one in-game day is one calendar month and
        /// <c>SimClockMath.ToSimDate</c> returns a literal <c>Day = 1</c>. A mid-month read would
        /// hand back the byte-identical snapshot taken at month start, making every metric and delta
        /// check provably unmeasurable; forcing a fresh sample instead would make the reading depend
        /// on which 128-frame tick crossed the threshold, which is a non-deterministic input and so
        /// forbidden by non-negotiable #3. The full argument is in the rework plan.
        /// </remarks>
        public int CycleMonths { get; internal set; } = 2;

        /// <summary>Slots that must be met for a full story to succeed — the "2 of 3" rule.</summary>
        public int SuccessThreshold { get; internal set; } = 2;

        /// <summary>
        /// Severity at or above which an event is Mandatory. Inclusive lower bound.
        /// </summary>
        /// <remarks>
        /// Deliberately the top of the 1–5 range: mandatory should feel rare (<c>/add-event</c>).
        /// </remarks>
        public int MandatorySeverityThreshold { get; internal set; } = 5;

        /// <summary>
        /// Severity at or above which an event is Major. Inclusive lower bound.
        /// </summary>
        /// <remarks>
        /// Deliberately equal to <c>catalog.majorSeverityThreshold</c>: "major" already has exactly
        /// one definition, shared by <c>EventScheduler.IsMajor</c>, <c>CoalitionStability</c> and the
        /// alert lane, and a second number for the same concept would drift on the next tuning pass.
        /// </remarks>
        public int MajorSeverityThreshold { get; internal set; } = 4;

        /// <summary>Weight added per cycle an eligible entry goes undrawn — the pity term.</summary>
        public double MissStreakWeightStep { get; internal set; } = 0.25;

        /// <summary>Cap on the pity term, so an ancient entry cannot crowd out everything forever.</summary>
        public int MaxMissStreak { get; internal set; } = 8;

        /// <summary>Entries the pool may hold. Beyond this the lowest-weighted are dropped.</summary>
        public int PoolMaxSize { get; internal set; } = 60;

        /// <summary>Resolved stories kept in <c>PoliticalState.StoryArchive</c>.</summary>
        public int ArchiveRetention { get; internal set; } = 40;

        /// <summary>
        /// Whether a minor may be promoted to fill a story with no major left in the pool. False
        /// makes the cycle draft fewer stories instead.
        /// </summary>
        public bool MinorPromotionEnabled { get; internal set; } = true;

        /// <summary>
        /// Story effects allowed to target one modifier in a cycle.
        /// </summary>
        /// <remarks>
        /// <c>effects.stackingMode</c> is <c>sum</c> with <c>maxStackedPerModifier</c> 4, so six story
        /// events several of which share a modifier would hit that limit and <b>silently drop the
        /// fifth</b>. Capping breadth at draft time enforces the constraint where it can be reasoned
        /// about rather than discovered in the ledger. Kept below the effects cap on purpose.
        /// </remarks>
        public int MaxStoryEffectsPerModifier { get; internal set; } = 3;

        /// <summary>Magnitude scale for effects applied while a story is live.</summary>
        public double ActiveEffectScale { get; internal set; } = 0.5;

        /// <summary>Magnitude scale for effects applied on a met slot.</summary>
        public double SuccessEffectScale { get; internal set; } = 1.0;

        /// <summary>Magnitude scale for effects applied on a not-met slot.</summary>
        public double FailureEffectScale { get; internal set; } = 1.0;

        /// <summary>How far a failed outcome pushes voters away from the government.</summary>
        public double AlienationWeight { get; internal set; } = 1.0;

        /// <summary>How far a met outcome pulls voters toward the government.</summary>
        public double EnfranchisementWeight { get; internal set; } = 1.0;

        /// <summary>
        /// Characters the player may write in an Ignore or Manual box. Over-length input is rejected
        /// with the existing <c>CommandOutcome.TooLong</c>.
        /// </summary>
        public int FreeTextMaxLength { get; internal set; } = 500;

        internal static StoriesTuning Read(TuningReader r, StoriesTuning d) => new StoriesTuning
        {
            Enabled = r.Flag("enabled", d.Enabled),
            StoriesPerCycle = r.Int("storiesPerCycle", d.StoriesPerCycle),
            EventsPerStory = r.Int("eventsPerStory", d.EventsPerStory),
            CycleMonths = r.Int("cycleMonths", d.CycleMonths),
            SuccessThreshold = r.Int("successThreshold", d.SuccessThreshold),
            MandatorySeverityThreshold = r.Int("mandatorySeverityThreshold", d.MandatorySeverityThreshold),
            MajorSeverityThreshold = r.Int("majorSeverityThreshold", d.MajorSeverityThreshold),
            MissStreakWeightStep = r.Num("missStreakWeightStep", d.MissStreakWeightStep),
            MaxMissStreak = r.Int("maxMissStreak", d.MaxMissStreak),
            PoolMaxSize = r.Int("poolMaxSize", d.PoolMaxSize),
            ArchiveRetention = r.Int("archiveRetention", d.ArchiveRetention),
            MinorPromotionEnabled = r.Flag("minorPromotionEnabled", d.MinorPromotionEnabled),
            MaxStoryEffectsPerModifier = r.Int("maxStoryEffectsPerModifier", d.MaxStoryEffectsPerModifier),
            ActiveEffectScale = r.Num("activeEffectScale", d.ActiveEffectScale),
            SuccessEffectScale = r.Num("successEffectScale", d.SuccessEffectScale),
            FailureEffectScale = r.Num("failureEffectScale", d.FailureEffectScale),
            AlienationWeight = r.Num("alienationWeight", d.AlienationWeight),
            EnfranchisementWeight = r.Num("enfranchisementWeight", d.EnfranchisementWeight),
            FreeTextMaxLength = r.Int("freeTextMaxLength", d.FreeTextMaxLength)
        };
    }

    /// <summary>An amount per story tier. Used for both award and cost schedules.</summary>
    public sealed class PowerTierAmounts
    {
        public PowerTierAmounts() { }

        public PowerTierAmounts(int minor, int major, int mandatory)
        {
            Minor = minor;
            Major = major;
            Mandatory = mandatory;
        }

        public int Minor { get; internal set; }
        public int Major { get; internal set; }
        public int Mandatory { get; internal set; }

        internal static PowerTierAmounts Read(TuningReader r, PowerTierAmounts d) => new PowerTierAmounts
        {
            Minor = r.Int("minor", d.Minor),
            Major = r.Int("major", d.Major),
            Mandatory = r.Int("mandatory", d.Mandatory)
        };
    }

    /// <summary>Packet 17 — the political-power economy. JSON section <c>power</c>.</summary>
    public sealed class PowerTuning
    {
        /// <summary>Master switch. False makes the whole packet inert and is the control in tests.</summary>
        public bool Enabled { get; internal set; } = true;

        /// <summary>Ceiling on one month's accrual, before any tier or outcome award.</summary>
        public int MaxMonthlyGain { get; internal set; } = 5;

        /// <summary>
        /// Exponent shaping how the government's vote share scales the monthly accrual. 1 is linear;
        /// above 1 makes a weak government earn disproportionately little.
        /// </summary>
        public double GainPopularityCurve { get; internal set; } = 1.0;

        /// <summary>Power awarded per met slot, by tier.</summary>
        public PowerTierAmounts SuccessGain { get; internal set; } = new PowerTierAmounts(10, 20, 50);

        /// <summary>
        /// Fraction of the tier's <see cref="SuccessGain"/> lost on a not-met slot. Below 1 so that
        /// failing costs less than succeeding pays — the economy should reward engagement.
        /// </summary>
        public double FailureLossRatio { get; internal set; } = 0.5;

        /// <summary>Power required to buy a slot off, by tier.</summary>
        public PowerTierAmounts OverrideCost { get; internal set; } = new PowerTierAmounts(50, 100, 500);

        /// <summary>
        /// Fraction of city income debited per month while the balance is negative. Also the
        /// <c>magnitudeCap</c> on the debt effect, which is not a <c>CityModifier</c> and so gets no
        /// cap from <c>EffectDispatcher</c>.
        /// </summary>
        public double DebtRevenuePenalty { get; internal set; } = 0.20;

        /// <summary>Absolute ceiling on one month's debt debit, whatever the city earns.</summary>
        public int DebtPenaltyCapPerMonth { get; internal set; } = 50000;

        /// <summary>Ledger entries kept for the UI.</summary>
        public int LedgerRetention { get; internal set; } = 50;

        internal static PowerTuning Read(TuningReader r, PowerTuning d) => new PowerTuning
        {
            Enabled = r.Flag("enabled", d.Enabled),
            MaxMonthlyGain = r.Int("maxMonthlyGain", d.MaxMonthlyGain),
            GainPopularityCurve = r.Num("gainPopularityCurve", d.GainPopularityCurve),
            SuccessGain = PowerTierAmounts.Read(r.Section("successGain"), d.SuccessGain),
            FailureLossRatio = r.Num("failureLossRatio", d.FailureLossRatio),
            OverrideCost = PowerTierAmounts.Read(r.Section("overrideCost"), d.OverrideCost),
            DebtRevenuePenalty = r.Num("debtRevenuePenalty", d.DebtRevenuePenalty),
            DebtPenaltyCapPerMonth = r.Int("debtPenaltyCapPerMonth", d.DebtPenaltyCapPerMonth),
            LedgerRetention = r.Int("ledgerRetention", d.LedgerRetention)
        };
    }
}
