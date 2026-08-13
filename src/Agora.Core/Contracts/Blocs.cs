using System;
using System.Collections.Generic;

namespace Agora.Core.Contracts
{
    /// <summary>
    /// Household wealth tier. Three tiers, cut from the household wealth distribution at the
    /// quantiles in <c>blocs.wealthTierThresholds</c>.
    /// </summary>
    /// <remarks>
    /// Not mirrored from a game enum — Scout 0001 found wealth is a numeric <c>Household</c>
    /// property, not an enum, and the exact field is still open. The sensor bins it; Core only ever
    /// sees the tier.
    /// </remarks>
    public enum WealthTier
    {
        Low = 0,
        Middle = 1,
        High = 2
    }

    /// <summary>
    /// Education tier. Mirrors <c>Game.Citizens.CitizenEducationLevel</c> value-for-value (verified
    /// with <c>tools/api-query.ps1 -Enum</c>), so the sensor's cast is a straight numeric mapping.
    /// </summary>
    public enum EducationTier
    {
        Uneducated = 0,
        PoorlyEducated = 1,
        Educated = 2,
        WellEducated = 3,
        HighlyEducated = 4
    }

    /// <summary>
    /// Age band. Mirrors <c>Game.Citizens.CitizenAge</c> value-for-value (verified).
    /// </summary>
    /// <remarks>
    /// <see cref="Child"/> and <see cref="Teen"/> are not enfranchised. That is expressed as a
    /// turnout multiplier of 0 in <c>turnout.ageBandMultipliers</c>, not as a missing bloc — the
    /// blocs still exist so the dashboard can show the whole population, and so a future setting can
    /// enfranchise 16-year-olds without a schema change.
    /// </remarks>
    public enum AgeBand
    {
        Child = 0,
        Teen = 1,
        Adult = 2,
        Elderly = 3
    }

    /// <summary>
    /// Deterministic iteration helpers for the three bloc axes.
    /// </summary>
    public static class BlocAxes
    {
        private static readonly WealthTier[] WealthArray = { WealthTier.Low, WealthTier.Middle, WealthTier.High };

        private static readonly EducationTier[] EducationArray =
        {
            EducationTier.Uneducated, EducationTier.PoorlyEducated, EducationTier.Educated,
            EducationTier.WellEducated, EducationTier.HighlyEducated
        };

        private static readonly AgeBand[] AgeArray = { AgeBand.Child, AgeBand.Teen, AgeBand.Adult, AgeBand.Elderly };

        public static IReadOnlyList<WealthTier> Wealth => WealthArray;
        public static IReadOnlyList<EducationTier> Education => EducationArray;
        public static IReadOnlyList<AgeBand> Age => AgeArray;

        /// <summary>3 × 5 × 4 = 60 blocs per district before pruning.</summary>
        public const int BlocCount = 3 * 5 * 4;

        /// <summary>
        /// Every bloc key, in a fixed order (wealth outermost, then education, then age).
        /// </summary>
        /// <remarks>
        /// Build bloc lists by walking this, never by enumerating a dictionary. The order is part of
        /// the determinism contract: it fixes the summation order of every district aggregate.
        /// </remarks>
        public static IReadOnlyList<BlocKey> AllKeys => AllKeysArray;

        private static readonly BlocKey[] AllKeysArray = BuildAllKeys();

        private static BlocKey[] BuildAllKeys()
        {
            var keys = new BlocKey[BlocCount];
            int i = 0;
            for (int w = 0; w < WealthArray.Length; w++)
                for (int e = 0; e < EducationArray.Length; e++)
                    for (int a = 0; a < AgeArray.Length; a++)
                        keys[i++] = new BlocKey(WealthArray[w], EducationArray[e], AgeArray[a]);
            return keys;
        }

        /// <summary>Normalised position of a wealth tier on <c>[-1, +1]</c>. Used by bloc weighting.</summary>
        public static double Axis(WealthTier tier) => ((int)tier / 2.0) * 2.0 - 1.0;

        /// <summary>Normalised position of an education tier on <c>[-1, +1]</c>.</summary>
        public static double Axis(EducationTier tier) => ((int)tier / 4.0) * 2.0 - 1.0;

        /// <summary>Normalised position of an age band on <c>[-1, +1]</c>.</summary>
        public static double Axis(AgeBand band) => ((int)band / 3.0) * 2.0 - 1.0;
    }

    /// <summary>
    /// Identity of one voter bloc: wealth × education × age (<c>politicsmodplan.md</c> §4.3).
    /// </summary>
    /// <remarks>
    /// <see cref="Id"/> is stable across runs and machines and is what goes into
    /// <c>SeedStreams.RngFor</c> as the entity id — never a hash code, never an index.
    /// </remarks>
    public readonly struct BlocKey : IEquatable<BlocKey>, IComparable<BlocKey>
    {
        public WealthTier Wealth { get; }
        public EducationTier Education { get; }
        public AgeBand Age { get; }

        public BlocKey(WealthTier wealth, EducationTier education, AgeBand age)
        {
            Wealth = wealth;
            Education = education;
            Age = age;
        }

        /// <summary>
        /// A dense ordinal in <c>[0, 60)</c> matching <see cref="BlocAxes.AllKeys"/> order.
        /// </summary>
        public int Ordinal => ((int)Wealth * 5 + (int)Education) * 4 + (int)Age;

        /// <summary>
        /// Stable string id, e.g. <c>"middle.educated.adult"</c>. Used in seed sub-streams, in JSON
        /// keys and in UI bindings.
        /// </summary>
        public string Id => WealthKey(Wealth) + "." + EducationKey(Education) + "." + AgeKey(Age);

        public static string WealthKey(WealthTier t)
        {
            switch (t)
            {
                case WealthTier.Low: return "low";
                case WealthTier.Middle: return "middle";
                case WealthTier.High: return "high";
                default: throw new ArgumentOutOfRangeException(nameof(t), t, "Unknown wealth tier.");
            }
        }

        public static string EducationKey(EducationTier t)
        {
            switch (t)
            {
                case EducationTier.Uneducated: return "uneducated";
                case EducationTier.PoorlyEducated: return "poorlyEducated";
                case EducationTier.Educated: return "educated";
                case EducationTier.WellEducated: return "wellEducated";
                case EducationTier.HighlyEducated: return "highlyEducated";
                default: throw new ArgumentOutOfRangeException(nameof(t), t, "Unknown education tier.");
            }
        }

        public static string AgeKey(AgeBand b)
        {
            switch (b)
            {
                case AgeBand.Child: return "child";
                case AgeBand.Teen: return "teen";
                case AgeBand.Adult: return "adult";
                case AgeBand.Elderly: return "elderly";
                default: throw new ArgumentOutOfRangeException(nameof(b), b, "Unknown age band.");
            }
        }

        public bool Equals(BlocKey other) =>
            Wealth == other.Wealth && Education == other.Education && Age == other.Age;

        public override bool Equals(object? obj) => obj is BlocKey other && Equals(other);

        // Not HashCode.Combine — netstandard2.0 does not have it.
        public override int GetHashCode() => Ordinal;

        public int CompareTo(BlocKey other) => Ordinal.CompareTo(other.Ordinal);

        public override string ToString() => Id;

        public static bool operator ==(BlocKey a, BlocKey b) => a.Equals(b);
        public static bool operator !=(BlocKey a, BlocKey b) => !a.Equals(b);
    }

    /// <summary>
    /// One voter bloc inside one district: who they are, what they care about, and where they sit.
    ///
    /// <para>
    /// Blocs are recomputed from the snapshot each engine tick and persisted in
    /// <see cref="PoliticalState"/> so a reload can reconcile without replaying the whole history.
    /// </para>
    /// </summary>
    public sealed class Bloc
    {
        /// <summary>Owning district. Matches <see cref="DistrictSnapshot.Id"/>.</summary>
        public string DistrictId { get; set; } = "";

        public BlocKey Key { get; set; }

        /// <summary>Head count in this bloc. Includes non-voting age bands.</summary>
        public int Population { get; set; }

        /// <summary>Share of the district's population, 0–1.</summary>
        public double PopulationShare { get; set; }

        /// <summary>
        /// Head count old enough to vote. Zero for <see cref="AgeBand.Child"/> and
        /// <see cref="AgeBand.Teen"/> under the shipped tuning.
        /// </summary>
        public int EligibleVoters { get; set; }

        /// <summary>How much this bloc cares about each issue, after lived metrics.</summary>
        public IssueWeights Weights { get; set; } = IssueWeights.Uniform;

        /// <summary>Where this bloc would place policy if it could. Affinity measures distance to it.</summary>
        public IssuePosition Ideal { get; set; } = IssuePosition.Centre;

        /// <summary>0–100, mirroring <see cref="CitySnapshot.Happiness"/>.</summary>
        public double Happiness { get; set; }

        /// <summary>0–1. Aggregate dissatisfaction; drives turnout and opposition swing.</summary>
        public double Discontent { get; set; }

        /// <summary>
        /// How this bloc voted at the last election, ordered by <see cref="PartyVoteShare.PartyId"/>.
        /// Empty before the first election. Feeds <c>affinity.habitualLoyalty</c>.
        /// </summary>
        public List<PartyVoteShare> PreviousVote { get; set; } = new List<PartyVoteShare>();

        /// <summary>True when any contributing metric fell back to a city-wide value.</summary>
        public bool HasCityFallbacks { get; set; }
    }

    /// <summary>
    /// One bloc's affinity for one party at one moment. Output of the affinity packet, input to
    /// turnout and vote-share aggregation.
    /// </summary>
    /// <remarks>
    /// Affinity is a pre-normalisation score, not a probability. The vote-share step turns a
    /// district's affinity set into shares with a softmax at <c>affinity.softmaxTemperature</c>.
    /// </remarks>
    public sealed class BlocAffinity
    {
        public string DistrictId { get; set; } = "";
        public BlocKey Bloc { get; set; }
        public string PartyId { get; set; } = "";

        /// <summary>Composite score. Higher is better; typical range 0–2 but not clamped by contract.</summary>
        public double Affinity { get; set; }

        /// <summary>Issue-proximity component, before bonuses and noise. Kept for the dashboard.</summary>
        public double IssueComponent { get; set; }

        /// <summary>Incumbency term, positive or negative.</summary>
        public double IncumbencyComponent { get; set; }

        /// <summary>Mandate-performance term, positive or negative.</summary>
        public double MandateComponent { get; set; }

        /// <summary>Sum of active event modifiers.</summary>
        public double EventComponent { get; set; }

        /// <summary>Habitual-loyalty term from <see cref="Bloc.PreviousVote"/>.</summary>
        public double LoyaltyComponent { get; set; }

        /// <summary>The seeded noise draw, from <c>StreamNames.AffinityNoise</c>.</summary>
        public double NoiseComponent { get; set; }

        /// <summary>
        /// Suppression applied by the fringe ceiling (<c>fringe</c> packet), always zero or negative.
        /// Zero for majors, for every party in the EU theme, and whenever the ceiling is not binding.
        ///
        /// <para>Unlike the other components this is not a term the scorer added up — it is a
        /// correction applied to the finished row, because a ceiling is a statement about a party's
        /// <i>share</i> and share only exists once the whole row is known. It is recorded so that
        /// <see cref="Affinity"/> still equals the sum of its parts, and so the dashboard can say a
        /// party is being held down rather than merely unpopular.</para>
        /// </summary>
        public double CeilingComponent { get; set; }
    }

    /// <summary>One bloc's projected turnout in one district. Output of the turnout packet.</summary>
    public sealed class BlocTurnout
    {
        public string DistrictId { get; set; } = "";
        public BlocKey Bloc { get; set; }

        /// <summary>Fraction of <see cref="EligibleVoters"/> that votes, 0–1, after floor/ceiling.</summary>
        public double Turnout { get; set; }

        public int EligibleVoters { get; set; }

        /// <summary>Rounded head count. The election packet counts these, not fractional shares.</summary>
        public int ProjectedVotes { get; set; }

        /// <summary>The seeded noise draw, from <c>StreamNames.TurnoutNoise</c>.</summary>
        public double NoiseComponent { get; set; }
    }
}
