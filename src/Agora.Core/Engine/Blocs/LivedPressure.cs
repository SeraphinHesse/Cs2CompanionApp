using System;
using Agora.Core.Contracts;
using Agora.Core.Tuning;

namespace Agora.Core.Engine.Blocs
{
    /// <summary>
    /// What the city is actually doing to the people who live in one place, expressed as six
    /// per-issue grievance levels in <c>[0, 1]</c> where 0 is "nothing to complain about".
    ///
    /// <para>
    /// This is the input that makes issue weights fall out of lived experience rather than out of
    /// authored personality (<c>politicsmodplan.md</c> §4.3: "high commute time raises transit weight
    /// for commuting blocs"). Nothing here is stochastic and nothing here is authored — every number
    /// is a measurement from <see cref="DistrictSnapshot"/> or <see cref="CitySnapshot"/>.
    /// </para>
    ///
    /// <para>
    /// Every sub-signal is normalised to <c>[0, 1]</c> before it is combined, and signals inside one
    /// issue are combined with an unweighted mean. That is deliberate: a weighted blend would be a
    /// coefficient, and coefficients live in <c>data/engine_tuning.json</c>, not in code. The one
    /// exception is the commute reference below, which is read from tuning.
    /// </para>
    /// </summary>
    /// <remarks>
    /// Fields that a sensor could not resolve per district carry the city value (see
    /// <see cref="DistrictSnapshot.CityFallbackFields"/>). Those still flow in here — there is nothing
    /// better to use — but because the district then holds exactly the city number, the
    /// <em>relative</em> half of the lived shift (<see cref="BlocIssueModel"/>) cancels to zero for
    /// that signal on its own. A fully fallen-back district therefore contributes no false local
    /// grievance, only the city-wide one it genuinely shares.
    /// </remarks>
    public readonly struct LivedPressure
    {
        /// <summary>Service shortfall and unresolved sickness.</summary>
        public double Services { get; }

        /// <summary>Rent burden and unemployment.</summary>
        public double CostOfLiving { get; }

        /// <summary>Pollution and the absence of parks.</summary>
        public double Environment { get; }

        /// <summary>Commute time over the reference, congestion, and transit coverage shortfall.</summary>
        public double Transit { get; }

        /// <summary>Unemployment and rent inflation — the pressure to build and to hire.</summary>
        public double Growth { get; }

        /// <summary>Crime, and the churn of a rapidly revaluing neighbourhood.</summary>
        public double HeritageOrder { get; }

        public LivedPressure(double services, double costOfLiving, double environment,
                             double transit, double growth, double heritageOrder)
        {
            Services = services;
            CostOfLiving = costOfLiving;
            Environment = environment;
            Transit = transit;
            Growth = growth;
            HeritageOrder = heritageOrder;
        }

        /// <summary>No grievance on any issue. The shape of a city that works.</summary>
        public static LivedPressure None
        {
            get { return new LivedPressure(0, 0, 0, 0, 0, 0); }
        }

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

        /// <summary>Grievance measured in one district.</summary>
        public static LivedPressure ForDistrict(DistrictSnapshot district, EngineTuning tuning)
        {
            if (district == null) throw new ArgumentNullException(nameof(district));
            if (tuning == null) throw new ArgumentNullException(nameof(tuning));

            return From(
                district.Services,
                district.Pollution,
                district.Unemployment,
                district.CrimeRate,
                district.SickRate,
                district.RentBurden,
                district.RentTrend,
                district.LandValueTrend,
                district.AverageCommuteMinutes,
                district.TrafficCongestion,
                CommuteReferenceMinutes(tuning));
        }

        /// <summary>
        /// Grievance measured city-wide. This is the baseline the district is compared against, so it
        /// must be computed from exactly the same signal set — otherwise the difference between the
        /// two would measure the formula, not the city.
        /// </summary>
        public static LivedPressure ForCity(CitySnapshot city, EngineTuning tuning)
        {
            if (city == null) throw new ArgumentNullException(nameof(city));
            if (tuning == null) throw new ArgumentNullException(nameof(tuning));

            return From(
                city.Services,
                city.Pollution,
                city.Unemployment,
                city.CrimeRate,
                city.SickRate,
                city.RentBurden,
                city.RentTrend,
                city.LandValueTrend,
                city.AverageCommuteMinutes,
                city.TrafficCongestion,
                CommuteReferenceMinutes(tuning));
        }

        /// <summary>
        /// The commute length that counts as painless. Read from <c>indices.commuteMiseryReferenceMinutes</c>
        /// rather than duplicated into the <c>blocs</c> section: it is the same physical quantity, and
        /// two copies of one reference is how the dashboard's commute misery index and the voter
        /// model's transit salience end up disagreeing about what a bad commute is.
        /// </summary>
        private static double CommuteReferenceMinutes(EngineTuning tuning)
        {
            return tuning.Indices.CommuteMiseryReferenceMinutes;
        }

        private static LivedPressure From(
            ServiceCoverage services,
            PollutionLevels pollution,
            double unemployment,
            double crimeRate,
            double sickRate,
            double rentBurden,
            double rentTrend,
            double landValueTrend,
            double commuteMinutes,
            double trafficCongestion,
            double commuteReferenceMinutes)
        {
            // Services: is the city looked after, and does anyone fix you when you break.
            double serviceGrievance = BlocMath.Mean(
                BlocMath.Clamp01(1.0 - services.Mean()),
                BlocMath.Clamp01(sickRate));

            // Cost of living: rent as a share of income is the plan's named signal; joblessness is
            // the other half of not being able to afford the city.
            double costGrievance = BlocMath.Mean(
                BlocMath.Clamp01(rentBurden),
                BlocMath.Clamp01(unemployment));

            // Environment: what is in the air and ground, and whether there is anywhere green.
            double environmentGrievance = BlocMath.Mean(
                BlocMath.Clamp01(pollution.Mean()),
                BlocMath.Clamp01(1.0 - services.Parks));

            // Transit: how long the trip takes relative to a painless one, how jammed the roads are,
            // and whether there is an alternative to driving.
            double commuteExcess = commuteReferenceMinutes > 0.0
                ? BlocMath.Clamp01((commuteMinutes - commuteReferenceMinutes) / commuteReferenceMinutes)
                : 0.0;
            double transitGrievance = BlocMath.Mean(
                commuteExcess,
                BlocMath.Clamp01(trafficCongestion),
                BlocMath.Clamp01(1.0 - services.Transit));

            // Growth: no jobs, and rents climbing because nothing is being built.
            double growthGrievance = BlocMath.Mean(
                BlocMath.Clamp01(unemployment),
                BlocMath.Clamp01(rentTrend));

            // Heritage and order: crime, plus the churn of a neighbourhood revaluing under you.
            double heritageGrievance = BlocMath.Mean(
                BlocMath.Clamp01(crimeRate),
                BlocMath.Clamp01(landValueTrend));

            return new LivedPressure(
                serviceGrievance,
                costGrievance,
                environmentGrievance,
                transitGrievance,
                growthGrievance,
                heritageGrievance);
        }
    }
}
