using System;
using System.Collections.Generic;
using Agora.Core.Contracts;

namespace Agora.Core.Engine.Government.Mandates
{
    /// <summary>
    /// The bridge between <see cref="MandateMetric"/> and the snapshot. Every member of the enum is
    /// readable here, or explicitly reported as unmeasurable — a mandate that cannot be measured is
    /// held, never failed (§6, <see cref="Mandate.IsMeasurementStalled"/>).
    ///
    /// <para>
    /// Pure and allocation-light: no state, no randomness, no tuning. Reading a metric must never
    /// depend on anything but the snapshot, or monitoring stops being a pure function of its inputs.
    /// </para>
    /// </summary>
    public static class MandateMetrics
    {
        /// <summary>Happiness is expressed on 0–100 by contract; everything else here is 0–1.</summary>
        internal const double HappinessScale = 100.0;

        // ---------------------------------------------------------------------------------------
        // Enumeration order. Never Enum.GetValues — its order is documented as unspecified, and an
        // unspecified order in generation would make the mandate set depend on the runtime.
        // ---------------------------------------------------------------------------------------

        private static readonly MandateMetric[] AllArray =
        {
            MandateMetric.Happiness,
            MandateMetric.Unemployment,
            MandateMetric.AirPollution,
            MandateMetric.GroundPollution,
            MandateMetric.NoisePollution,
            MandateMetric.WaterPollution,
            MandateMetric.CrimeRate,
            MandateMetric.HealthCoverage,
            MandateMetric.EducationCoverage,
            MandateMetric.PoliceCoverage,
            MandateMetric.FireCoverage,
            MandateMetric.GarbageCoverage,
            MandateMetric.TransitCoverage,
            MandateMetric.AverageCommuteMinutes,
            MandateMetric.AverageRent,
            MandateMetric.AverageLandValue,
            MandateMetric.RentBurden,
            MandateMetric.Population,
            MandateMetric.BudgetBalance,
            MandateMetric.Debt
        };

        /// <summary>Every metric, in enum order. Monitoring handles all of them.</summary>
        public static IReadOnlyList<MandateMetric> All => AllArray;

        // Generation is restricted to the metrics that carry their own scale. A promise on rent, land
        // value, commute minutes, population, budget or debt has no unit-free notion of "20% of the
        // deficit" without a per-metric normalising constant, and inventing one in code would be a
        // hardcoded tuning coefficient. Those six stay fully monitorable and scoreable — they are just
        // not generated from a measured deficit until `mandates.metricScales` exists.
        private static readonly MandateMetric[] GeneratableArray =
        {
            MandateMetric.Happiness,
            MandateMetric.Unemployment,
            MandateMetric.AirPollution,
            MandateMetric.GroundPollution,
            MandateMetric.NoisePollution,
            MandateMetric.WaterPollution,
            MandateMetric.CrimeRate,
            MandateMetric.HealthCoverage,
            MandateMetric.EducationCoverage,
            MandateMetric.PoliceCoverage,
            MandateMetric.FireCoverage,
            MandateMetric.GarbageCoverage,
            MandateMetric.TransitCoverage,
            MandateMetric.RentBurden
        };

        /// <summary>
        /// The metrics a mandate may be generated from, in a fixed order. A subset of <see cref="All"/>:
        /// each one is bounded, so a deficit and a target can be expressed without a scale constant.
        /// </summary>
        public static IReadOnlyList<MandateMetric> Generatable => GeneratableArray;

        // ---------------------------------------------------------------------------------------
        // Classification
        // ---------------------------------------------------------------------------------------

        /// <summary>
        /// The issue a promise on this metric belongs to. Drives which blocs reward or punish it, and
        /// therefore the mandate's salience.
        /// </summary>
        public static Issue IssueFor(MandateMetric metric)
        {
            switch (metric)
            {
                case MandateMetric.Happiness: return Issue.Services;
                case MandateMetric.HealthCoverage: return Issue.Services;
                case MandateMetric.EducationCoverage: return Issue.Services;
                case MandateMetric.FireCoverage: return Issue.Services;
                case MandateMetric.GarbageCoverage: return Issue.Services;

                case MandateMetric.AverageRent: return Issue.CostOfLiving;
                case MandateMetric.AverageLandValue: return Issue.CostOfLiving;
                case MandateMetric.RentBurden: return Issue.CostOfLiving;
                case MandateMetric.BudgetBalance: return Issue.CostOfLiving;
                case MandateMetric.Debt: return Issue.CostOfLiving;

                case MandateMetric.AirPollution: return Issue.Environment;
                case MandateMetric.GroundPollution: return Issue.Environment;
                case MandateMetric.NoisePollution: return Issue.Environment;
                case MandateMetric.WaterPollution: return Issue.Environment;

                case MandateMetric.TransitCoverage: return Issue.Transit;
                case MandateMetric.AverageCommuteMinutes: return Issue.Transit;

                case MandateMetric.Unemployment: return Issue.Growth;
                case MandateMetric.Population: return Issue.Growth;

                case MandateMetric.CrimeRate: return Issue.HeritageOrder;
                case MandateMetric.PoliceCoverage: return Issue.HeritageOrder;

                default:
                    throw new ArgumentOutOfRangeException(nameof(metric), metric, "Unknown mandate metric.");
            }
        }

        /// <summary>
        /// Which way "better" runs for this metric. Used by generation to point a target at the good
        /// end; monitoring uses the direction stored on the mandate instead, so an authored promise
        /// running the other way still scores correctly.
        /// </summary>
        public static MandateDirection ImprovementDirection(MandateMetric metric)
        {
            switch (metric)
            {
                case MandateMetric.Happiness:
                case MandateMetric.HealthCoverage:
                case MandateMetric.EducationCoverage:
                case MandateMetric.PoliceCoverage:
                case MandateMetric.FireCoverage:
                case MandateMetric.GarbageCoverage:
                case MandateMetric.TransitCoverage:
                case MandateMetric.Population:
                case MandateMetric.BudgetBalance:
                // Rising land value is treated as improvement (tax base). It is deliberately not
                // generatable, because the affordability reading of the same number is the opposite.
                case MandateMetric.AverageLandValue:
                    return MandateDirection.Increase;

                default:
                    return MandateDirection.Decrease;
            }
        }

        /// <summary>True when the metric has a fixed scale, so a deficit can be stated as a fraction.</summary>
        public static bool IsBounded(MandateMetric metric)
        {
            switch (metric)
            {
                case MandateMetric.Happiness:
                case MandateMetric.Unemployment:
                case MandateMetric.AirPollution:
                case MandateMetric.GroundPollution:
                case MandateMetric.NoisePollution:
                case MandateMetric.WaterPollution:
                case MandateMetric.CrimeRate:
                case MandateMetric.HealthCoverage:
                case MandateMetric.EducationCoverage:
                case MandateMetric.PoliceCoverage:
                case MandateMetric.FireCoverage:
                case MandateMetric.GarbageCoverage:
                case MandateMetric.TransitCoverage:
                case MandateMetric.RentBurden:
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>
        /// How bad a measured value is, on <c>[0, 1]</c> where 0 is perfect. Defined only for bounded
        /// metrics; the unbounded ones return false rather than guessing a scale.
        /// </summary>
        public static bool TryBadness(MandateMetric metric, double value, out double badness)
        {
            badness = 0.0;
            if (double.IsNaN(value) || double.IsInfinity(value)) return false;

            switch (metric)
            {
                case MandateMetric.Happiness:
                    badness = 1.0 - Clamp01(value / HappinessScale);
                    return true;

                case MandateMetric.Unemployment:
                case MandateMetric.AirPollution:
                case MandateMetric.GroundPollution:
                case MandateMetric.NoisePollution:
                case MandateMetric.WaterPollution:
                case MandateMetric.CrimeRate:
                // RentBurden may exceed 1 in a badly squeezed city; clamped, because a deficit above
                // "everything" carries no extra information.
                case MandateMetric.RentBurden:
                    badness = Clamp01(value);
                    return true;

                case MandateMetric.HealthCoverage:
                case MandateMetric.EducationCoverage:
                case MandateMetric.PoliceCoverage:
                case MandateMetric.FireCoverage:
                case MandateMetric.GarbageCoverage:
                case MandateMetric.TransitCoverage:
                    badness = 1.0 - Clamp01(value);
                    return true;

                default:
                    return false;
            }
        }

        /// <summary>
        /// The value, in the metric's own units, at which badness is zero. Only meaningful for bounded
        /// metrics; it is the reference a city-wide target is measured against.
        /// </summary>
        public static bool TryIdealValue(MandateMetric metric, out double ideal)
        {
            switch (metric)
            {
                case MandateMetric.Happiness:
                    ideal = HappinessScale;
                    return true;

                case MandateMetric.HealthCoverage:
                case MandateMetric.EducationCoverage:
                case MandateMetric.PoliceCoverage:
                case MandateMetric.FireCoverage:
                case MandateMetric.GarbageCoverage:
                case MandateMetric.TransitCoverage:
                    ideal = 1.0;
                    return true;

                case MandateMetric.Unemployment:
                case MandateMetric.AirPollution:
                case MandateMetric.GroundPollution:
                case MandateMetric.NoisePollution:
                case MandateMetric.WaterPollution:
                case MandateMetric.CrimeRate:
                case MandateMetric.RentBurden:
                    ideal = 0.0;
                    return true;

                default:
                    ideal = 0.0;
                    return false;
            }
        }

        // ---------------------------------------------------------------------------------------
        // Reading
        // ---------------------------------------------------------------------------------------

        /// <summary>
        /// Reads a metric for a mandate's scope. <paramref name="districtId"/> null means city-wide.
        /// Returns false when the value cannot be trusted this tick — the district is gone, the metric
        /// has no per-district reading, or the sensor fell back to the city value for that field. The
        /// caller holds the mandate rather than failing it.
        /// </summary>
        public static bool TryRead(CitySnapshot? snapshot, string? districtId, MandateMetric metric, out double value)
        {
            value = 0.0;
            if (snapshot == null) return false;

            if (districtId == null) return TryReadCity(snapshot, metric, out value);

            DistrictSnapshot? district = FindDistrict(snapshot, districtId);
            if (district == null) return false;

            return TryReadDistrict(district, metric, out value);
        }

        /// <summary>Locates a district by id. Linear over a list the contract keeps sorted; never a dictionary.</summary>
        public static DistrictSnapshot? FindDistrict(CitySnapshot? snapshot, string? districtId)
        {
            if (snapshot == null || districtId == null) return null;

            List<DistrictSnapshot> districts = snapshot.Districts;
            if (districts == null) return null;

            for (int i = 0; i < districts.Count; i++)
            {
                DistrictSnapshot d = districts[i];
                if (d != null && string.Equals(d.Id, districtId, StringComparison.Ordinal)) return d;
            }

            return null;
        }

        public static bool TryReadCity(CitySnapshot? snapshot, MandateMetric metric, out double value)
        {
            value = 0.0;
            if (snapshot == null) return false;

            switch (metric)
            {
                case MandateMetric.Happiness: value = snapshot.Happiness; break;
                case MandateMetric.Unemployment: value = snapshot.Unemployment; break;
                case MandateMetric.AirPollution: value = snapshot.Pollution.Air; break;
                case MandateMetric.GroundPollution: value = snapshot.Pollution.Ground; break;
                case MandateMetric.NoisePollution: value = snapshot.Pollution.Noise; break;
                case MandateMetric.WaterPollution: value = snapshot.Pollution.Water; break;
                case MandateMetric.CrimeRate: value = snapshot.CrimeRate; break;
                case MandateMetric.HealthCoverage: value = snapshot.Services.Health; break;
                case MandateMetric.EducationCoverage: value = snapshot.Services.Education; break;
                case MandateMetric.PoliceCoverage: value = snapshot.Services.Police; break;
                case MandateMetric.FireCoverage: value = snapshot.Services.Fire; break;
                case MandateMetric.GarbageCoverage: value = snapshot.Services.Garbage; break;
                case MandateMetric.TransitCoverage: value = snapshot.Services.Transit; break;
                case MandateMetric.AverageCommuteMinutes: value = snapshot.AverageCommuteMinutes; break;
                case MandateMetric.AverageRent: value = snapshot.AverageRent; break;
                case MandateMetric.AverageLandValue: value = snapshot.AverageLandValue; break;
                case MandateMetric.RentBurden: value = snapshot.RentBurden; break;
                case MandateMetric.Population: value = snapshot.Population; break;
                case MandateMetric.BudgetBalance: value = snapshot.BudgetBalance; break;
                case MandateMetric.Debt: value = snapshot.Debt; break;
                default: return false;
            }

            return !double.IsNaN(value) && !double.IsInfinity(value);
        }

        /// <summary>
        /// Reads a district metric. Returns false for a field the sensor filled from the city value —
        /// non-negotiable per §6: never score a mandate against a fallen-back field.
        /// </summary>
        public static bool TryReadDistrict(DistrictSnapshot? district, MandateMetric metric, out double value)
        {
            value = 0.0;
            if (district == null) return false;
            if (IsFallenBack(district, metric)) return false;

            switch (metric)
            {
                case MandateMetric.Happiness: value = district.Happiness; break;
                case MandateMetric.Unemployment: value = district.Unemployment; break;
                case MandateMetric.AirPollution: value = district.Pollution.Air; break;
                case MandateMetric.GroundPollution: value = district.Pollution.Ground; break;
                case MandateMetric.NoisePollution: value = district.Pollution.Noise; break;
                case MandateMetric.WaterPollution: value = district.Pollution.Water; break;
                case MandateMetric.CrimeRate: value = district.CrimeRate; break;
                case MandateMetric.HealthCoverage: value = district.Services.Health; break;
                case MandateMetric.EducationCoverage: value = district.Services.Education; break;
                case MandateMetric.PoliceCoverage: value = district.Services.Police; break;
                case MandateMetric.FireCoverage: value = district.Services.Fire; break;
                case MandateMetric.GarbageCoverage: value = district.Services.Garbage; break;
                case MandateMetric.TransitCoverage: value = district.Services.Transit; break;
                case MandateMetric.AverageCommuteMinutes: value = district.AverageCommuteMinutes; break;
                case MandateMetric.AverageRent: value = district.AverageRent; break;
                case MandateMetric.AverageLandValue: value = district.AverageLandValue; break;
                case MandateMetric.RentBurden: value = district.RentBurden; break;
                case MandateMetric.Population: value = district.Population; break;

                // The budget is a city fact. There is no district ledger, so a district-scoped promise
                // on either of these is permanently unmeasurable — held, then abandoned, never failed.
                case MandateMetric.BudgetBalance:
                case MandateMetric.Debt:
                    return false;

                default: return false;
            }

            return !double.IsNaN(value) && !double.IsInfinity(value);
        }

        /// <summary>
        /// True when this district's reading for the metric is really the city value in disguise.
        /// </summary>
        public static bool IsFallenBack(DistrictSnapshot? district, MandateMetric metric)
        {
            if (district == null || !district.HasCityFallbacks) return false;

            List<string> fields = district.CityFallbackFields;
            if (fields == null || fields.Count == 0) return false;

            string[] names = FallbackFieldNames(metric);

            // Membership test over a sorted list: order-independent by construction.
            for (int i = 0; i < fields.Count; i++)
            {
                string field = fields[i];
                if (field == null) continue;

                for (int j = 0; j < names.Length; j++)
                {
                    if (string.Equals(field, names[j], StringComparison.Ordinal)) return true;
                }
            }

            return false;
        }

        // The sensor records the property it fell back on. Struct-valued properties may be reported
        // either whole ("Pollution") or per component ("Pollution.Air"), so each metric accepts both
        // spellings plus its own flat name. Being generous here is the safe direction: an extra match
        // holds a mandate, a missed match would score it against a city number pretending to be local.
        private static readonly string[] NHappiness = { "Happiness" };
        private static readonly string[] NUnemployment = { "Unemployment" };
        private static readonly string[] NAir = { "Pollution", "Pollution.Air", "AirPollution" };
        private static readonly string[] NGround = { "Pollution", "Pollution.Ground", "GroundPollution" };
        private static readonly string[] NNoise = { "Pollution", "Pollution.Noise", "NoisePollution" };
        private static readonly string[] NWater = { "Pollution", "Pollution.Water", "WaterPollution" };
        private static readonly string[] NCrime = { "CrimeRate" };
        private static readonly string[] NHealth = { "Services", "Services.Health", "HealthCoverage" };
        private static readonly string[] NEducation = { "Services", "Services.Education", "EducationCoverage" };
        private static readonly string[] NPolice = { "Services", "Services.Police", "PoliceCoverage" };
        private static readonly string[] NFire = { "Services", "Services.Fire", "FireCoverage" };
        private static readonly string[] NGarbage = { "Services", "Services.Garbage", "GarbageCoverage" };
        private static readonly string[] NTransit = { "Services", "Services.Transit", "TransitCoverage" };
        private static readonly string[] NCommute = { "AverageCommuteMinutes" };
        private static readonly string[] NRent = { "AverageRent" };
        private static readonly string[] NLandValue = { "AverageLandValue" };
        private static readonly string[] NRentBurden = { "RentBurden" };
        private static readonly string[] NPopulation = { "Population" };
        private static readonly string[] NBudget = { "BudgetBalance" };
        private static readonly string[] NDebt = { "Debt" };

        /// <summary>
        /// Property names on <see cref="DistrictSnapshot"/> whose presence in
        /// <see cref="DistrictSnapshot.CityFallbackFields"/> invalidates this metric.
        /// </summary>
        public static string[] FallbackFieldNames(MandateMetric metric)
        {
            switch (metric)
            {
                case MandateMetric.Happiness: return NHappiness;
                case MandateMetric.Unemployment: return NUnemployment;
                case MandateMetric.AirPollution: return NAir;
                case MandateMetric.GroundPollution: return NGround;
                case MandateMetric.NoisePollution: return NNoise;
                case MandateMetric.WaterPollution: return NWater;
                case MandateMetric.CrimeRate: return NCrime;
                case MandateMetric.HealthCoverage: return NHealth;
                case MandateMetric.EducationCoverage: return NEducation;
                case MandateMetric.PoliceCoverage: return NPolice;
                case MandateMetric.FireCoverage: return NFire;
                case MandateMetric.GarbageCoverage: return NGarbage;
                case MandateMetric.TransitCoverage: return NTransit;
                case MandateMetric.AverageCommuteMinutes: return NCommute;
                case MandateMetric.AverageRent: return NRent;
                case MandateMetric.AverageLandValue: return NLandValue;
                case MandateMetric.RentBurden: return NRentBurden;
                case MandateMetric.Population: return NPopulation;
                case MandateMetric.BudgetBalance: return NBudget;
                case MandateMetric.Debt: return NDebt;
                default:
                    throw new ArgumentOutOfRangeException(nameof(metric), metric, "Unknown mandate metric.");
            }
        }

        /// <summary>The stable string form of a metric, for ids and seed entity keys.</summary>
        public static string ToKey(MandateMetric metric)
        {
            switch (metric)
            {
                case MandateMetric.Happiness: return "happiness";
                case MandateMetric.Unemployment: return "unemployment";
                case MandateMetric.AirPollution: return "airPollution";
                case MandateMetric.GroundPollution: return "groundPollution";
                case MandateMetric.NoisePollution: return "noisePollution";
                case MandateMetric.WaterPollution: return "waterPollution";
                case MandateMetric.CrimeRate: return "crimeRate";
                case MandateMetric.HealthCoverage: return "healthCoverage";
                case MandateMetric.EducationCoverage: return "educationCoverage";
                case MandateMetric.PoliceCoverage: return "policeCoverage";
                case MandateMetric.FireCoverage: return "fireCoverage";
                case MandateMetric.GarbageCoverage: return "garbageCoverage";
                case MandateMetric.TransitCoverage: return "transitCoverage";
                case MandateMetric.AverageCommuteMinutes: return "averageCommuteMinutes";
                case MandateMetric.AverageRent: return "averageRent";
                case MandateMetric.AverageLandValue: return "averageLandValue";
                case MandateMetric.RentBurden: return "rentBurden";
                case MandateMetric.Population: return "population";
                case MandateMetric.BudgetBalance: return "budgetBalance";
                case MandateMetric.Debt: return "debt";
                default:
                    throw new ArgumentOutOfRangeException(nameof(metric), metric, "Unknown mandate metric.");
            }
        }

        // netstandard2.0 has no Math.Clamp.
        private static double Clamp01(double v) => v < 0.0 ? 0.0 : (v > 1.0 ? 1.0 : v);
    }
}
