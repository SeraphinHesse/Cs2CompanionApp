using System;
using System.Collections.Generic;

namespace Agora.Core.Contracts
{
    /// <summary>
    /// The six political issues the voter model runs on (<c>politicsmodplan.md</c> §4.3).
    ///
    /// <para>
    /// The set is closed on purpose. Blocs weight these issues, parties and factions take positions
    /// on these issues, mandates are generated against deficits measured on these issues, and every
    /// tuning coefficient that is "per issue" is keyed by exactly these six names. Adding a seventh
    /// is a schema change (non-negotiable #9), not a code edit.
    /// </para>
    /// </summary>
    public enum Issue
    {
        /// <summary>Health, education, police, fire, garbage, utilities — is the city looked after.</summary>
        Services = 0,

        /// <summary>Rent, land value, taxes, unemployment — can people afford to live here.</summary>
        CostOfLiving = 1,

        /// <summary>Air, ground, noise and water pollution; parks and green space.</summary>
        Environment = 2,

        /// <summary>Commute time, transit coverage, traffic, parking.</summary>
        Transit = 3,

        /// <summary>Development, jobs, new construction, densification.</summary>
        Growth = 4,

        /// <summary>Crime, order, stability, and resistance to change.</summary>
        HeritageOrder = 5
    }

    /// <summary>Deterministic iteration helpers for <see cref="Issue"/>.</summary>
    /// <remarks>
    /// Iterate <see cref="All"/> rather than <c>Enum.GetValues</c>: the framework's ordering is
    /// documented as unspecified, and an unspecified order in the engine is a determinism defect.
    /// </remarks>
    public static class Issues
    {
        /// <summary>Number of issues. Fixed at six; see <see cref="Issue"/>.</summary>
        public const int Count = 6;

        private static readonly Issue[] AllArray =
        {
            Issue.Services,
            Issue.CostOfLiving,
            Issue.Environment,
            Issue.Transit,
            Issue.Growth,
            Issue.HeritageOrder
        };

        /// <summary>All issues in declaration order. Stable, and the order every sum uses.</summary>
        public static IReadOnlyList<Issue> All => AllArray;

        /// <summary>The camelCase key used for this issue in JSON (tuning, schemas, snapshot).</summary>
        public static string ToKey(Issue issue)
        {
            switch (issue)
            {
                case Issue.Services: return "services";
                case Issue.CostOfLiving: return "costOfLiving";
                case Issue.Environment: return "environment";
                case Issue.Transit: return "transit";
                case Issue.Growth: return "growth";
                case Issue.HeritageOrder: return "heritageOrder";
                default: throw new ArgumentOutOfRangeException(nameof(issue), issue, "Unknown issue.");
            }
        }
    }

    /// <summary>
    /// How much a bloc cares about each issue. A per-issue importance vector, never a stance.
    ///
    /// <para>
    /// Weights are non-negative and normally normalised to sum to <see cref="Issues.Count"/> (i.e.
    /// mean 1.0), which keeps a bloc's total political energy constant while letting it care much
    /// more about one thing than another. <c>blocs.normalizeWeights</c> controls this.
    /// </para>
    ///
    /// <para>
    /// A separate type from <see cref="IssuePosition"/> on purpose: the two have identical shape and
    /// opposite meaning, and the compiler catching a swapped argument is worth the duplication.
    /// </para>
    /// </summary>
    public readonly struct IssueWeights
    {
        public double Services { get; }
        public double CostOfLiving { get; }
        public double Environment { get; }
        public double Transit { get; }
        public double Growth { get; }
        public double HeritageOrder { get; }

        public IssueWeights(double services, double costOfLiving, double environment,
                               double transit, double growth, double heritageOrder)
        {
            Services = services;
            CostOfLiving = costOfLiving;
            Environment = environment;
            Transit = transit;
            Growth = growth;
            HeritageOrder = heritageOrder;
        }

        /// <summary>Every issue weighted equally at 1.0.</summary>
        public static IssueWeights Uniform => new IssueWeights(1, 1, 1, 1, 1, 1);

        public double this[Issue issue]
        {
            get
            {
                switch (issue)
                {
                    case Issue.Services: return Services;
                    case Issue.CostOfLiving: return CostOfLiving;
                    case Issue.Environment: return Environment;
                    case Issue.Transit: return Transit;
                    case Issue.Growth: return Growth;
                    case Issue.HeritageOrder: return HeritageOrder;
                    default: throw new ArgumentOutOfRangeException(nameof(issue), issue, "Unknown issue.");
                }
            }
        }

        /// <summary>A copy with one issue replaced. Structs are immutable here by design.</summary>
        public IssueWeights With(Issue issue, double value)
        {
            return new IssueWeights(
                issue == Issue.Services ? value : Services,
                issue == Issue.CostOfLiving ? value : CostOfLiving,
                issue == Issue.Environment ? value : Environment,
                issue == Issue.Transit ? value : Transit,
                issue == Issue.Growth ? value : Growth,
                issue == Issue.HeritageOrder ? value : HeritageOrder);
        }

        /// <summary>Componentwise sum.</summary>
        public IssueWeights Add(IssueWeights other) =>
            new IssueWeights(
                Services + other.Services,
                CostOfLiving + other.CostOfLiving,
                Environment + other.Environment,
                Transit + other.Transit,
                Growth + other.Growth,
                HeritageOrder + other.HeritageOrder);

        /// <summary>Componentwise scale.</summary>
        public IssueWeights Scale(double factor) =>
            new IssueWeights(
                Services * factor,
                CostOfLiving * factor,
                Environment * factor,
                Transit * factor,
                Growth * factor,
                HeritageOrder * factor);

        /// <summary>Sum of all six weights. Summed in <see cref="Issues.All"/> order.</summary>
        public double Sum() => Services + CostOfLiving + Environment + Transit + Growth + HeritageOrder;

        /// <summary>
        /// Rescaled so the weights sum to <see cref="Issues.Count"/>. A zero or negative total is
        /// returned as <see cref="Uniform"/> rather than producing NaN.
        /// </summary>
        public IssueWeights Normalized()
        {
            double sum = Sum();
            if (sum <= 0.0 || double.IsNaN(sum) || double.IsInfinity(sum)) return Uniform;
            return Scale(Issues.Count / sum);
        }

        /// <summary>Componentwise clamp. Used to hold weights inside the tuned floor/ceiling.</summary>
        public IssueWeights Clamped(double min, double max)
        {
            return new IssueWeights(
                Clamp(Services, min, max),
                Clamp(CostOfLiving, min, max),
                Clamp(Environment, min, max),
                Clamp(Transit, min, max),
                Clamp(Growth, min, max),
                Clamp(HeritageOrder, min, max));
        }

        // netstandard2.0 has no Math.Clamp.
        private static double Clamp(double v, double min, double max) => v < min ? min : (v > max ? max : v);
    }

    /// <summary>
    /// A stance on each issue, in <c>[-1, +1]</c>. The same shape for a party platform, a faction
    /// platform, and a bloc's ideal point — which is what makes affinity a distance computation.
    ///
    /// <para>
    /// Sign convention, fixed and not negotiable per issue because affinity depends on it:
    /// <c>+1</c> means "spend/protect/restrict more" and <c>-1</c> means "spend/protect/restrict
    /// less". Concretely: +Services = more public spending; +CostOfLiving = prioritise
    /// affordability over revenue; +Environment = stricter environmental protection;
    /// +Transit = invest in transit over cars; +Growth = pro-development; +HeritageOrder = more
    /// order and preservation.
    /// </para>
    /// </summary>
    public readonly struct IssuePosition
    {
        public double Services { get; }
        public double CostOfLiving { get; }
        public double Environment { get; }
        public double Transit { get; }
        public double Growth { get; }
        public double HeritageOrder { get; }

        public IssuePosition(double services, double costOfLiving, double environment,
                             double transit, double growth, double heritageOrder)
        {
            Services = services;
            CostOfLiving = costOfLiving;
            Environment = environment;
            Transit = transit;
            Growth = growth;
            HeritageOrder = heritageOrder;
        }

        /// <summary>Dead centre on every issue.</summary>
        public static IssuePosition Centre => new IssuePosition(0, 0, 0, 0, 0, 0);

        public double this[Issue issue]
        {
            get
            {
                switch (issue)
                {
                    case Issue.Services: return Services;
                    case Issue.CostOfLiving: return CostOfLiving;
                    case Issue.Environment: return Environment;
                    case Issue.Transit: return Transit;
                    case Issue.Growth: return Growth;
                    case Issue.HeritageOrder: return HeritageOrder;
                    default: throw new ArgumentOutOfRangeException(nameof(issue), issue, "Unknown issue.");
                }
            }
        }

        public IssuePosition With(Issue issue, double value)
        {
            return new IssuePosition(
                issue == Issue.Services ? value : Services,
                issue == Issue.CostOfLiving ? value : CostOfLiving,
                issue == Issue.Environment ? value : Environment,
                issue == Issue.Transit ? value : Transit,
                issue == Issue.Growth ? value : Growth,
                issue == Issue.HeritageOrder ? value : HeritageOrder);
        }

        public IssuePosition Add(IssuePosition other) =>
            new IssuePosition(
                Services + other.Services,
                CostOfLiving + other.CostOfLiving,
                Environment + other.Environment,
                Transit + other.Transit,
                Growth + other.Growth,
                HeritageOrder + other.HeritageOrder);

        public IssuePosition Scale(double factor) =>
            new IssuePosition(
                Services * factor,
                CostOfLiving * factor,
                Environment * factor,
                Transit * factor,
                Growth * factor,
                HeritageOrder * factor);

        /// <summary>Componentwise clamp back into <c>[-1, +1]</c>.</summary>
        public IssuePosition Clamped() => Clamped(-1.0, 1.0);

        public IssuePosition Clamped(double min, double max)
        {
            return new IssuePosition(
                Clamp(Services, min, max),
                Clamp(CostOfLiving, min, max),
                Clamp(Environment, min, max),
                Clamp(Transit, min, max),
                Clamp(Growth, min, max),
                Clamp(HeritageOrder, min, max));
        }

        /// <summary>
        /// Issue-weighted L1 distance to another position, normalised to <c>[0, 1]</c>.
        /// </summary>
        /// <remarks>
        /// This is the affinity kernel's input. Terms are summed in <see cref="Issues.All"/> order so
        /// the floating-point result is bit-stable regardless of how the caller stores its issues.
        /// A zero or negative weight total returns 0 rather than producing NaN.
        /// </remarks>
        public double WeightedDistance(IssuePosition other, IssueWeights weights)
        {
            double weighted = 0.0;
            double totalWeight = 0.0;

            for (int i = 0; i < Issues.All.Count; i++)
            {
                Issue issue = Issues.All[i];
                double w = weights[issue];
                weighted += w * Math.Abs(this[issue] - other[issue]);
                totalWeight += w;
            }

            if (totalWeight <= 0.0) return 0.0;

            // Per-issue distance maxes at 2.0 (from -1 to +1); divide it out so the result is [0,1].
            return weighted / (totalWeight * 2.0);
        }

        /// <summary>Unweighted mean absolute distance, normalised to <c>[0, 1]</c>.</summary>
        /// <remarks>Used by the party-lifecycle and coalition packets to measure ideological gap.</remarks>
        public double Distance(IssuePosition other) => WeightedDistance(other, IssueWeights.Uniform);

        // netstandard2.0 has no Math.Clamp.
        private static double Clamp(double v, double min, double max) => v < min ? min : (v > max ? max : v);
    }
}
