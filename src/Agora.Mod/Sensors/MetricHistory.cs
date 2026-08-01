using System;
using System.Collections.Generic;
using Agora.Core.Contracts;

namespace Agora.Mod.Sensors
{
    /// <summary>
    /// A small month-indexed history of one scalar per series, used to compute the two trend fields
    /// the snapshot declares (<c>LandValueTrend</c>, <c>RentTrend</c>).
    ///
    /// <para>
    /// Trends cannot be measured from a single capture, so somewhere has to remember. Keeping it here
    /// rather than in the engine keeps the engine a pure function of its inputs: the sensor reports a
    /// trend as a measurement, exactly like it reports a level.
    /// </para>
    ///
    /// <para>
    /// Pure — <see cref="SimDate"/> in, <c>double</c> out, no game types and no wall clock. Keyed by
    /// series name in a dictionary, but nothing ever iterates that dictionary: every read is a
    /// lookup by an explicit key, so ordering cannot leak into a result.
    /// </para>
    /// </summary>
    public sealed class MetricHistory
    {
        private readonly Dictionary<string, List<Sample>> _series =
            new Dictionary<string, List<Sample>>(StringComparer.Ordinal);

        private readonly int _maxSamplesPerSeries;

        /// <param name="maxSamplesPerSeries">
        /// Retention cap. Sized from the widest trend window the caller asks for; older samples are
        /// dropped oldest-first so a decades-long game does not grow this without bound.
        /// </param>
        public MetricHistory(int maxSamplesPerSeries = 64)
        {
            _maxSamplesPerSeries = Math.Max(2, maxSamplesPerSeries);
        }

        private readonly struct Sample
        {
            public readonly int TotalMonths;
            public readonly double Value;

            public Sample(int totalMonths, double value)
            {
                TotalMonths = totalMonths;
                Value = value;
            }
        }

        /// <summary>
        /// Records <paramref name="value"/> for <paramref name="series"/> at <paramref name="date"/>.
        /// One sample per calendar month: a second record in the same month replaces the first, so
        /// capture cadence cannot change what a trend means.
        /// </summary>
        public void Record(string series, SimDate date, double value)
        {
            if (string.IsNullOrEmpty(series) || double.IsNaN(value) || double.IsInfinity(value)) return;

            List<Sample> samples;
            if (!_series.TryGetValue(series, out samples))
            {
                samples = new List<Sample>();
                _series[series] = samples;
            }

            int months = date.TotalMonths;

            if (samples.Count > 0 && samples[samples.Count - 1].TotalMonths == months)
            {
                samples[samples.Count - 1] = new Sample(months, value);
                return;
            }

            // A capture taken after a load-from-an-earlier-save would otherwise leave the future in
            // the history and make every trend read backwards. Drop anything at or after the new
            // sample's month instead.
            while (samples.Count > 0 && samples[samples.Count - 1].TotalMonths >= months)
            {
                samples.RemoveAt(samples.Count - 1);
            }

            samples.Add(new Sample(months, value));

            while (samples.Count > _maxSamplesPerSeries)
            {
                samples.RemoveAt(0);
            }
        }

        /// <summary>
        /// Fractional change in <paramref name="series"/> over <paramref name="windowMonths"/>,
        /// measured against the oldest sample no newer than the start of the window.
        /// </summary>
        /// <returns>
        /// Null when there is no baseline that old. Null means "not yet measurable" and the caller
        /// must not substitute zero — a brand new city has not held its rents flat, it has no rent
        /// history at all.
        /// </returns>
        public double? TrendOver(string series, SimDate date, int windowMonths)
        {
            if (string.IsNullOrEmpty(series) || windowMonths <= 0) return null;

            List<Sample> samples;
            if (!_series.TryGetValue(series, out samples) || samples.Count < 2) return null;

            int nowMonths = date.TotalMonths;
            int cutoff = nowMonths - windowMonths;

            double present = samples[samples.Count - 1].Value;

            // Newest sample at or before the cutoff. Walking backwards finds it in one step in the
            // steady state, where captures are monthly and the window is fixed.
            for (int i = samples.Count - 1; i >= 0; i--)
            {
                if (samples[i].TotalMonths <= cutoff)
                {
                    return SensorMath.FractionalChange(present, samples[i].Value);
                }
            }

            return null;
        }

        /// <summary>Number of samples held for a series. Diagnostics only.</summary>
        public int SampleCount(string series)
        {
            List<Sample> samples;
            return _series.TryGetValue(series, out samples) ? samples.Count : 0;
        }

        /// <summary>
        /// Forgets everything. Called when a different save is loaded — carrying one city's rent
        /// history into another would be a fabricated trend.
        /// </summary>
        public void Clear() => _series.Clear();

        /// <summary>Series key for a city-wide metric.</summary>
        public static string CityKey(string metric) => "city/" + metric;

        /// <summary>Series key for a district-scoped metric.</summary>
        public static string DistrictKey(string districtId, string metric) => districtId + "/" + metric;

        /// <summary>Metric name for mean land value.</summary>
        public const string LandValue = "landValue";

        /// <summary>Metric name for mean rent.</summary>
        public const string Rent = "rent";
    }
}
