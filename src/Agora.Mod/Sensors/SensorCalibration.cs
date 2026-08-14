using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace Agora.Mod.Sensors
{
    /// <summary>
    /// The unit conversions the sensors need: every constant that turns a game-native quantity into
    /// the range <c>CitySnapshot</c> declares.
    ///
    /// <para>
    /// <b>Why this exists rather than a section on <c>EngineTuning</c>.</b> The engine-tuning
    /// contract was frozen before this packet, and it has no <c>sensors</c> section. These are not
    /// political coefficients — none of them weighs an issue or moves a vote — but they are still
    /// numbers, and scattering them as literals through seven ECS systems is exactly the failure the
    /// no-hardcoded-constants rule exists to prevent. So they live here, named and documented, behind
    /// one injection point.
    /// </para>
    ///
    /// <para>
    /// The intended end state is a <c>sensors</c> section in <c>data/engine_tuning.json</c> read
    /// through <c>EngineTuning</c>. <see cref="FromJson"/> already parses that shape, so the swap is
    /// a one-line change in the loader once the section exists. See the packet report for the exact
    /// key list.
    /// </para>
    /// </summary>
    public sealed class SensorCalibration
    {
        /// <summary>
        /// The calibration every sensor reads. Assigned once during mod load; treated as immutable
        /// afterwards so two captures in the same session cannot disagree.
        /// </summary>
        public static SensorCalibration Active { get; set; } = new SensorCalibration();

        /// <summary>Keys that were missing or the wrong shape, in read order. Empty for a clean load.</summary>
        public IReadOnlyList<string> Warnings => _warnings;

        private readonly List<string> _warnings = new List<string>();

        // --- Pollution -------------------------------------------------------------------------
        // GroundPollution / AirPollution / NoisePollution all store an Int16 in engine units. The
        // reference maxima below are the values treated as "saturated" when normalising to [0, 1].

        /// <summary>Ground pollution reading treated as 1.0.</summary>
        public double GroundPollutionReferenceMax { get; private set; } = 1000.0;

        /// <summary>Air pollution reading treated as 1.0.</summary>
        public double AirPollutionReferenceMax { get; private set; } = 1000.0;

        /// <summary>Noise pollution reading treated as 1.0.</summary>
        public double NoisePollutionReferenceMax { get; private set; } = 1000.0;

        // --- Service coverage ------------------------------------------------------------------

        /// <summary>
        /// Road-edge service coverage reading treated as full coverage. The game's coverage figures
        /// are unbounded above; this is the point past which more coverage stops mattering.
        /// </summary>
        public double ServiceCoverageReferenceMax { get; private set; } = 100.0;

        // --- Crime -----------------------------------------------------------------------------

        /// <summary>
        /// <c>CrimeProducer.m_Crime</c> per building treated as 1.0 when averaging a district's
        /// crime rate.
        /// </summary>
        public double CrimeReferenceMax { get; private set; } = 100.0;

        /// <summary>City-wide <c>StatisticType.CrimeRate</c> reading treated as 1.0.</summary>
        public double CityCrimeStatisticReferenceMax { get; private set; } = 100.0;

        // --- Mobility --------------------------------------------------------------------------

        /// <summary>
        /// <c>TrafficFlowSystem.cityAverageTrafficFlow</c> reading treated as total gridlock. The
        /// property is an int in engine units with no documented ceiling.
        /// </summary>
        public double TrafficFlowReferenceMax { get; private set; } = 100.0;

        /// <summary>
        /// Multiplier turning <c>Worker.m_LastCommuteTime</c> into minutes. The field is a float in
        /// simulation time units; the conversion is unverified in-game (see the packet report).
        /// </summary>
        public double CommuteTimeToMinutes { get; private set; } = 1.0;

        /// <summary>
        /// Daily transit boardings per resident treated as a ridership share of 1.0. Ridership is
        /// reported as a share by the contract but the game only exposes absolute passenger counts.
        /// </summary>
        public double TransitBoardingsPerCapitaAtFullRidership { get; private set; } = 2.0;

        // --- Economy ---------------------------------------------------------------------------

        /// <summary>
        /// Rent is charged per rent period while <c>Household.m_SalaryLastDay</c> is daily. This is
        /// the number of days of salary one rent charge is compared against when computing
        /// rent burden.
        /// </summary>
        public double RentPeriodDays { get; private set; } = 30.0;

        /// <summary>Months of history compared when computing rent and land-value trends.</summary>
        /// <remarks>
        /// Twelve, not twenty-four. Two reasons, and the first is arithmetic: a trend needs a baseline
        /// at or before <c>now - window</c>, so a 24-month window is silent for the first two years of
        /// a save and a 12-month one for the first year. The second is political — a term is a year,
        /// so a one-year window is the span a party is actually judged over, and a rent rise that
        /// began under the previous administration should not still be the incumbent's headline.
        ///
        /// <para>
        /// This is only a defensible number now that <c>metric_history.json</c> exists. While the
        /// history died with the session, <i>every</i> window was effectively infinite: the samples
        /// never survived long enough to satisfy any of them.
        /// </para>
        /// </remarks>
        public int TrendWindowMonths { get; private set; } = 12;

        // --- Sampling --------------------------------------------------------------------------

        /// <summary>
        /// Emergency ceiling on residential buildings walked in one capture, for a city large enough
        /// that a full walk visibly stalls the simulation thread. Above the cap the walk visits a
        /// deterministic subset chosen by entity index, so the choice is reproducible.
        ///
        /// <para>
        /// <b>Defaults to 0, meaning no cap, and should stay there.</b> Subsampling makes the
        /// population and household <i>counts</i> a fraction of the truth — shares and averages
        /// survive it, absolute counts do not. It is a way to keep a huge city playable, not a
        /// tuning knob.
        /// </para>
        /// </summary>
        public int MaxBuildingsPerCapture { get; private set; } = 0;

        /// <summary>
        /// Minimum residents a district needs before its measured values are reported. Below this a
        /// district falls back to the city figure: a two-person district's "average happiness" is
        /// noise, and presenting it as a local fact is the failure §6 warns about.
        /// </summary>
        public int MinDistrictPopulationForLocalValues { get; private set; } = 20;

        /// <summary>
        /// Reads a <c>sensors</c> object. Never throws: a malformed or absent file degrades to the
        /// defaults above and records why in <see cref="Warnings"/>. Sensors must not be able to
        /// take the mod down (the same fail-closed discipline the LLM provider follows).
        /// </summary>
        public static SensorCalibration FromJson(string json)
        {
            var calibration = new SensorCalibration();
            if (string.IsNullOrEmpty(json))
            {
                calibration._warnings.Add("sensors: no calibration supplied; using defaults.");
                return calibration;
            }

            JObject root;
            try
            {
                root = JObject.Parse(json);
            }
            catch (Exception ex)
            {
                calibration._warnings.Add("sensors: unparseable JSON (" + ex.Message + "); using defaults.");
                return calibration;
            }

            JToken sectionToken = root["sensors"] ?? root;
            var section = sectionToken as JObject;
            if (section == null)
            {
                calibration._warnings.Add("sensors: section is not an object; using defaults.");
                return calibration;
            }

            calibration.GroundPollutionReferenceMax =
                calibration.ReadDouble(section, "groundPollutionReferenceMax", calibration.GroundPollutionReferenceMax);
            calibration.AirPollutionReferenceMax =
                calibration.ReadDouble(section, "airPollutionReferenceMax", calibration.AirPollutionReferenceMax);
            calibration.NoisePollutionReferenceMax =
                calibration.ReadDouble(section, "noisePollutionReferenceMax", calibration.NoisePollutionReferenceMax);
            calibration.ServiceCoverageReferenceMax =
                calibration.ReadDouble(section, "serviceCoverageReferenceMax", calibration.ServiceCoverageReferenceMax);
            calibration.CrimeReferenceMax =
                calibration.ReadDouble(section, "crimeReferenceMax", calibration.CrimeReferenceMax);
            calibration.CityCrimeStatisticReferenceMax =
                calibration.ReadDouble(section, "cityCrimeStatisticReferenceMax", calibration.CityCrimeStatisticReferenceMax);
            calibration.TrafficFlowReferenceMax =
                calibration.ReadDouble(section, "trafficFlowReferenceMax", calibration.TrafficFlowReferenceMax);
            calibration.CommuteTimeToMinutes =
                calibration.ReadDouble(section, "commuteTimeToMinutes", calibration.CommuteTimeToMinutes);
            calibration.TransitBoardingsPerCapitaAtFullRidership =
                calibration.ReadDouble(section, "transitBoardingsPerCapitaAtFullRidership", calibration.TransitBoardingsPerCapitaAtFullRidership);
            calibration.RentPeriodDays =
                calibration.ReadDouble(section, "rentPeriodDays", calibration.RentPeriodDays);
            calibration.TrendWindowMonths =
                calibration.ReadInt(section, "trendWindowMonths", calibration.TrendWindowMonths);
            calibration.MaxBuildingsPerCapture =
                calibration.ReadInt(section, "maxBuildingsPerCapture", calibration.MaxBuildingsPerCapture);
            calibration.MinDistrictPopulationForLocalValues =
                calibration.ReadInt(section, "minDistrictPopulationForLocalValues", calibration.MinDistrictPopulationForLocalValues);

            return calibration;
        }

        private double ReadDouble(JObject section, string key, double fallback)
        {
            JToken token = section[key];
            if (token == null)
            {
                _warnings.Add("sensors." + key + ": missing; using " + fallback.ToString(System.Globalization.CultureInfo.InvariantCulture) + ".");
                return fallback;
            }

            if (token.Type != JTokenType.Float && token.Type != JTokenType.Integer)
            {
                _warnings.Add("sensors." + key + ": not a number; using " + fallback.ToString(System.Globalization.CultureInfo.InvariantCulture) + ".");
                return fallback;
            }

            return token.Value<double>();
        }

        private int ReadInt(JObject section, string key, int fallback)
        {
            JToken token = section[key];
            if (token == null)
            {
                _warnings.Add("sensors." + key + ": missing; using " + fallback.ToString(System.Globalization.CultureInfo.InvariantCulture) + ".");
                return fallback;
            }

            if (token.Type != JTokenType.Integer)
            {
                _warnings.Add("sensors." + key + ": not an integer; using " + fallback.ToString(System.Globalization.CultureInfo.InvariantCulture) + ".");
                return fallback;
            }

            return token.Value<int>();
        }
    }
}
