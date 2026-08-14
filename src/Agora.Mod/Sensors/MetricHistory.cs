// Compiled into BOTH Agora.Mod and (by <Compile Link>) tests/Agora.Core.Tests: it must stay free of
// every Game.*, Unity.* and Colossal.* type. #nullable disable keeps it warning-clean in the test
// project, which enables nullable, without annotating a file the mod compiles unannotated.
#nullable disable

using System;
using System.Collections.Generic;
using Agora.Core.Contracts;
using Agora.Mod.Persistence;

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
    /// series name in a dictionary, but nothing ever iterates that dictionary <i>unordered</i>:
    /// every read is a lookup by an explicit key, and <see cref="ToFile"/>, the one place that does
    /// enumerate it, sorts before it writes.
    /// </para>
    ///
    /// <para>
    /// <b>It survives a reload.</b> <see cref="ToFile"/> and <see cref="RestoreFrom"/> are the two
    /// ends of <c>metric_history.json</c>. Without them this class was session-scoped, and a trend
    /// window measured in months could never be reached by a player who quits — which made
    /// <c>RentTrend</c> and <c>LandValueTrend</c> permanently unmeasurable and left every district
    /// reporting them as a city fallback.
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

        // ---------------------------------------------------------------- persistence

        /// <summary>
        /// Everything held, as the sidecar document. Series are sorted by key and samples stay in
        /// month order, so two runs over the same history serialize byte-identically — which is what
        /// non-negotiable #3's fingerprint definition requires of anything written to disk.
        /// </summary>
        public MetricHistoryFile ToFile()
        {
            var keys = new List<string>(_series.Keys);
            keys.Sort(StringComparer.Ordinal);

            var file = new MetricHistoryFile();

            for (int i = 0; i < keys.Count; i++)
            {
                List<Sample> samples = _series[keys[i]];
                if (samples == null || samples.Count == 0) continue;

                var series = new MetricSeriesFile { Series = keys[i] };
                for (int s = 0; s < samples.Count; s++)
                {
                    series.Samples.Add(new MetricSampleFile
                    {
                        TotalMonths = samples[s].TotalMonths,
                        Value = samples[s].Value
                    });
                }

                file.Series.Add(series);
            }

            return file;
        }

        /// <summary>
        /// Replaces everything held with what <paramref name="file"/> carries, discarding any sample
        /// dated after <paramref name="asOf"/>.
        /// </summary>
        /// <param name="asOf">
        /// The sim date being loaded into. The trim is the whole reason this takes a date: §5 allows
        /// a load to reconcile onto an <i>earlier</i> snapshot, and a history that still held next
        /// decade's rents would compute a trend against a present that has not happened. Samples in
        /// the same month as <paramref name="asOf"/> are kept — that month is the present, not the
        /// future.
        /// </param>
        /// <remarks>
        /// <para>
        /// <b>Null is a no-op, not an erasure.</b> Null means "there was no file to read" — the save
        /// predates <c>metric_history.json</c>, or it would not parse — and destroying whatever this
        /// session has already collected on the strength of a missing file would turn a benign absence
        /// into data loss. An empty-but-present document is the opposite claim, "this save genuinely
        /// has no history", and that one does clear.
        /// </para>
        /// <para>
        /// Garbage inside the document — a null series, a blank key, a NaN, a sample out of order — is
        /// dropped sample by sample rather than failing the restore, on the same fail-soft rule the
        /// rest of the sidecar follows.
        /// </para>
        /// </remarks>
        public void RestoreFrom(MetricHistoryFile file, SimDate asOf)
        {
            if (file == null) return;

            _series.Clear();
            if (file.Series == null) return;

            int cutoff = asOf.TotalMonths;

            for (int i = 0; i < file.Series.Count; i++)
            {
                MetricSeriesFile series = file.Series[i];
                if (series == null || string.IsNullOrEmpty(series.Series) || series.Samples == null) continue;

                var restored = new List<Sample>();

                for (int s = 0; s < series.Samples.Count; s++)
                {
                    MetricSampleFile sample = series.Samples[s];
                    if (sample == null) continue;
                    if (sample.TotalMonths > cutoff) continue;
                    if (double.IsNaN(sample.Value) || double.IsInfinity(sample.Value)) continue;

                    // Month order is what TrendOver walks backwards over, and a hand-edited file could
                    // arrive out of order. Appending only when the month advances keeps the invariant
                    // Record maintains — strictly ascending, one sample per month — without a sort.
                    if (restored.Count > 0 && sample.TotalMonths <= restored[restored.Count - 1].TotalMonths)
                    {
                        continue;
                    }

                    restored.Add(new Sample(sample.TotalMonths, sample.Value));
                }

                while (restored.Count > _maxSamplesPerSeries)
                {
                    restored.RemoveAt(0);
                }

                if (restored.Count > 0) _series[series.Series] = restored;
            }
        }

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
