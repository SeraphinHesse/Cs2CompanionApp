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
    /// A month-indexed history of one scalar per series. It computes the two trend fields the snapshot
    /// declares (<c>LandValueTrend</c>, <c>RentTrend</c>), and it is the store
    /// <see cref="SnapshotRehydration"/> rebuilds past snapshots from.
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

        /// <summary>
        /// Files one assembled snapshot: every metric in the vocabulary below, at city scope and at
        /// each district's scope, under <paramref name="snapshot"/>'s own date.
        ///
        /// <para>
        /// It takes the assembled <see cref="CitySnapshot"/> rather than the raw sensor readings on
        /// purpose. A district that could not measure its own education carries the city's, and the
        /// snapshot is where that resolution happens — recording the reading instead would leave the
        /// series empty for exactly that district and rebuild a past snapshot that disagrees with the
        /// one the engine actually saw. What is stored is what the engine was told, which is the only
        /// definition of "faithful" <see cref="SnapshotRehydration"/> can be held to.
        /// </para>
        ///
        /// <para>
        /// <b>Rent and land value are deliberately not recorded here.</b> The snapshot system records
        /// those itself, and only where they were genuinely measured; re-recording them from the
        /// snapshot would overwrite a fallback district's empty series with the city's rent and turn
        /// every later <c>RentTrend</c> for that district into a trend in someone else's rents.
        /// </para>
        ///
        /// <para>
        /// Lives here, on the pure type, rather than on the sensor system, so the headless suite can
        /// exercise the real recorder against the real rehydrator instead of a reimplementation of it
        /// that would agree with itself by construction.
        /// </para>
        /// </summary>
        public void RecordSnapshot(CitySnapshot snapshot)
        {
            if (snapshot == null) return;

            SimDate date = snapshot.Date;

            RecordScope(CityScope, date, snapshot.Population, snapshot.Happiness, snapshot.Unemployment,
                snapshot.CrimeRate, snapshot.Education, snapshot.Wealth, snapshot.Pollution,
                snapshot.Services, snapshot.UncollectedGarbage, snapshot.AttractionCount,
                snapshot.SignatureBuildingCount);

            // City scope only. Neither figure has a per-district sensor — the mobility family reports
            // nothing district-scoped at all — so every district's commute and congestion is a copy of
            // the city's, and storing it per district would store one number once per district under a
            // name claiming it is local.
            Record(CityKey(CommuteMinutes), date, snapshot.AverageCommuteMinutes);
            Record(CityKey(TrafficCongestion), date, snapshot.TrafficCongestion);

            // The city-statistics pass. City scope only, and not for want of a district query: the
            // game's statistics system is keyed by (StatisticType, parameter) with no district
            // dimension at all, so these are city-only at source (scout 0004 §1.4). Recording them
            // per district would file the city's number under a district's name.
            CityStatistics statistics = snapshot.Statistics;
            Record(CityKey(Homeless), date, statistics.Homeless);
            Record(CityKey(HomelessShare), date, statistics.HomelessShare);
            Record(CityKey(CitizensMovedIn), date, statistics.CitizensMovedIn);
            Record(CityKey(CitizensMovedAway), date, statistics.CitizensMovedAway);
            Record(CityKey(MovedAwayUnhappy), date, statistics.MovedAwayUnhappy);
            Record(CityKey(Births), date, statistics.Births);
            Record(CityKey(Deaths), date, statistics.Deaths);
            Record(CityKey(GarbageProductionRate), date, statistics.GarbageProductionRate);

            TourismLevels tourism = snapshot.Tourism;
            Record(CityKey(Tourists), date, tourism.Tourists);
            Record(CityKey(Attractiveness), date, tourism.Attractiveness);
            Record(CityKey(LodgingUsed), date, tourism.LodgingUsed);
            Record(CityKey(LodgingTotal), date, tourism.LodgingTotal);

            ProgressionState progression = snapshot.Progression;
            Record(CityKey(MilestoneLevel), date, progression.MilestoneLevel);
            Record(CityKey(Experience), date, progression.Experience);
            Record(CityKey(MilestoneProgress), date, progression.MilestoneProgress);

            List<DistrictSnapshot> districts = snapshot.Districts;
            if (districts == null) return;

            // Contractually sorted by id already, and Record appends to a per-series list, so nothing
            // here depends on the order — but it is a fixed order regardless.
            for (int i = 0; i < districts.Count; i++)
            {
                DistrictSnapshot d = districts[i];
                if (d == null || string.IsNullOrEmpty(d.Id)) continue;

                // The three counts are passed as null — "not measured", the same convention the sensor
                // readings use — for any field this district fell back on. See the vocabulary
                // declaration below for why an absent sample beats a recorded zero.
                RecordScope(d.Id, date, d.Population, d.Happiness, d.Unemployment, d.CrimeRate,
                    d.Education, d.Wealth, d.Pollution, d.Services,
                    Measured(d, SnapshotAssembly.FieldUncollectedGarbage, d.UncollectedGarbage),
                    Measured(d, SnapshotAssembly.FieldAttractionCount, d.AttractionCount),
                    Measured(d, SnapshotAssembly.FieldSignatureBuildingCount, d.SignatureBuildingCount));
            }
        }

        /// <summary>
        /// <paramref name="value"/> if <paramref name="district"/> really measured
        /// <paramref name="fieldName"/>, or null if the field is named in its
        /// <see cref="DistrictSnapshot.CityFallbackFields"/>.
        /// </summary>
        private static T? Measured<T>(DistrictSnapshot district, string fieldName, T value)
            where T : struct
        {
            List<string> fallbacks = district.CityFallbackFields;
            if (fallbacks == null) return value;

            for (int i = 0; i < fallbacks.Count; i++)
            {
                if (string.Equals(fallbacks[i], fieldName, StringComparison.Ordinal)) return null;
            }

            return value;
        }

        /// <summary>
        /// The metrics recorded identically at city and district scope. One method for both so the
        /// two scopes cannot drift into different vocabularies.
        ///
        /// <para>
        /// The last three arrive nullable so that one scope may have measured them and the other not.
        /// That is not a second vocabulary — the metric names are the same at both scopes — it is the
        /// difference between a series with no sample this month and a series with a fabricated one.
        /// The city never passes null: it has nowhere to fall back to and so is never fallback-marked.
        /// </para>
        /// </summary>
        private void RecordScope(string scope, SimDate date, int population, double happiness,
                                 double unemployment, double crimeRate, EducationDistribution education,
                                 WealthDistribution wealth, PollutionLevels pollution,
                                 ServiceCoverage services, double? uncollectedGarbage,
                                 int? attractionCount, int? signatureBuildingCount)
        {
            Record(ScopedKey(scope, Population), date, population);
            Record(ScopedKey(scope, Happiness), date, happiness);
            Record(ScopedKey(scope, Unemployment), date, unemployment);
            Record(ScopedKey(scope, CrimeRate), date, crimeRate);

            Record(ScopedKey(scope, EducationUneducated), date, education.UneducatedShare);
            Record(ScopedKey(scope, EducationPoorlyEducated), date, education.PoorlyEducatedShare);
            Record(ScopedKey(scope, EducationEducated), date, education.EducatedShare);
            Record(ScopedKey(scope, EducationWellEducated), date, education.WellEducatedShare);
            Record(ScopedKey(scope, EducationHighlyEducated), date, education.HighlyEducatedShare);

            Record(ScopedKey(scope, WealthLow), date, wealth.LowShare);
            Record(ScopedKey(scope, WealthMiddle), date, wealth.MiddleShare);
            Record(ScopedKey(scope, WealthHigh), date, wealth.HighShare);

            // Stored as their means, not channel by channel. A trigger asks whether a place is
            // polluted or underserved, not which of four channels moved, and nine service figures per
            // district per month for a decade is a sidecar nobody wanted.
            Record(ScopedKey(scope, PollutionMean), date, pollution.Mean());
            Record(ScopedKey(scope, ServiceCoverageMean), date, services.Mean());

            // The three v4 figures the game resolves per district as well as city-wide, so they are
            // the only part of the city-statistics pass that belongs in this shared method — and the
            // only three recorded conditionally. A null writes nothing at all, which TryValueAt
            // already reports as "not measured" and SnapshotRehydration already leaves at the
            // contract default.
            if (uncollectedGarbage.HasValue)
            {
                Record(ScopedKey(scope, UncollectedGarbage), date, uncollectedGarbage.Value);
            }

            if (attractionCount.HasValue)
            {
                Record(ScopedKey(scope, AttractionCount), date, attractionCount.Value);
            }

            if (signatureBuildingCount.HasValue)
            {
                Record(ScopedKey(scope, SignatureBuildingCount), date, signatureBuildingCount.Value);
            }
        }

        /// <summary>Number of samples held for a series. Diagnostics only.</summary>
        public int SampleCount(string series)
        {
            List<Sample> samples;
            return _series.TryGetValue(series, out samples) ? samples.Count : 0;
        }

        /// <summary>
        /// The value recorded for <paramref name="series"/> in the month <paramref name="totalMonths"/>
        /// names, exact month only.
        /// </summary>
        /// <returns>
        /// False when that series holds no sample in that month. The caller must treat false as "not
        /// measured", never as zero — the whole point of the out-parameter is that a recorded 0.0 and
        /// an absent sample are different facts.
        /// </returns>
        public bool TryValueAt(string series, int totalMonths, out double value)
        {
            value = 0.0;
            if (string.IsNullOrEmpty(series)) return false;

            List<Sample> samples;
            if (!_series.TryGetValue(series, out samples)) return false;

            // Samples are strictly ascending by month, so a backward walk stops as soon as it passes
            // the month asked for. Monthly reconstruction reads the newest months first in practice.
            for (int i = samples.Count - 1; i >= 0; i--)
            {
                if (samples[i].TotalMonths < totalMonths) return false;
                if (samples[i].TotalMonths == totalMonths)
                {
                    value = samples[i].Value;
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Every series key held, sorted ordinal ascending. Sorted rather than enumerated raw because
        /// a caller that walks the keys is walking a dictionary, and dictionary order is exactly the
        /// determinism bug non-negotiable #3 names.
        /// </summary>
        public List<string> SeriesKeys()
        {
            var keys = new List<string>(_series.Keys);
            keys.Sort(StringComparer.Ordinal);
            return keys;
        }

        /// <summary>
        /// The union of the months any series holds a sample in, ascending, discarding anything after
        /// <paramref name="asOf"/>. The months a snapshot can be rebuilt for.
        /// </summary>
        public List<int> RecordedMonths(SimDate asOf)
        {
            int cutoff = asOf.TotalMonths;
            var seen = new HashSet<int>();
            var months = new List<int>();

            List<string> keys = SeriesKeys();
            for (int k = 0; k < keys.Count; k++)
            {
                List<Sample> samples = _series[keys[k]];
                for (int s = 0; s < samples.Count; s++)
                {
                    int month = samples[s].TotalMonths;
                    if (month > cutoff) break;
                    if (seen.Add(month)) months.Add(month);
                }
            }

            months.Sort();
            return months;
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

        // ------------------------------------------------------------- the key vocabulary

        // A series key is "<scope>/<metric>": the scope is either CityScope or a district id, and the
        // metric is one of the constants below. Ids are "d" + eight digits (DistrictIdentityMap) and
        // metric names are lower camel case with '.' for a tier, so '/' appears exactly once and the
        // split is unambiguous.
        //
        // This vocabulary is a contract, not an implementation detail. Wave 2's trigger registry names
        // these strings, SnapshotRehydration reads them back into a CitySnapshot, and the sidecar's
        // fingerprint is taken over them sorted — so a metric name may be added but never renamed
        // without a migration, on the same rule that governs a seed stream name.

        /// <summary>The scope segment meaning "the whole city".</summary>
        public const string CityScope = "city";

        /// <summary>Series key for a city-wide metric.</summary>
        public static string CityKey(string metric) => CityScope + "/" + metric;

        /// <summary>Series key for a district-scoped metric.</summary>
        public static string DistrictKey(string districtId, string metric) => districtId + "/" + metric;

        /// <summary>
        /// Series key for <paramref name="metric"/> in <paramref name="scope"/>, where the scope is
        /// either <see cref="CityScope"/> or a district id. The one place the two key forms are
        /// chosen between, so a caller that holds a scope string does not have to branch.
        /// </summary>
        public static string ScopedKey(string scope, string metric) =>
            string.Equals(scope, CityScope, StringComparison.Ordinal)
                ? CityKey(metric)
                : DistrictKey(scope, metric);

        /// <summary>
        /// The scope segment of <paramref name="series"/> — <see cref="CityScope"/> or a district id
        /// — or null when the key carries no separator and is therefore not one this class wrote.
        /// </summary>
        public static string ScopeOf(string series)
        {
            if (string.IsNullOrEmpty(series)) return null;
            int slash = series.IndexOf('/');
            return slash <= 0 ? null : series.Substring(0, slash);
        }

        // --- Metric names --------------------------------------------------------------------
        //
        // Two groups, and the distinction matters. The first group is what a *historical* snapshot is
        // read for: IndicesEngine.Compute is the only reader of the snapshot history, and its
        // brain-drain leg takes Population and Education off the city while its gentrification leg
        // takes Education and Wealth off each district. Those must be recorded or rehydration lies.
        // The second group is the scalar vocabulary a metric/delta trigger will name; it is recorded
        // so the trend window is already filling by the time there is a registry to read it, and it
        // is deliberately short of exhaustive — a metric that is neither stored nor read is file size.

        /// <summary>Metric name for mean land value.</summary>
        public const string LandValue = "landValue";

        /// <summary>Metric name for mean rent.</summary>
        public const string Rent = "rent";

        /// <summary>Head count. Recorded because brain drain is measured per skilled resident.</summary>
        public const string Population = "population";

        /// <summary>Share of residents at each education tier. The five sum to 1 within rounding.</summary>
        public const string EducationUneducated = "education.uneducated";
        public const string EducationPoorlyEducated = "education.poorlyEducated";
        public const string EducationEducated = "education.educated";
        public const string EducationWellEducated = "education.wellEducated";
        public const string EducationHighlyEducated = "education.highlyEducated";

        /// <summary>
        /// Share of residents in each wealth tier. All three, not just the low tier gentrification
        /// reads: a distribution stored in part cannot be reconstructed, and a rehydrated
        /// <c>WealthDistribution</c> whose other two tiers sat at zero would be a fabricated
        /// measurement rather than an absent one.
        /// </summary>
        public const string WealthLow = "wealth.low";
        public const string WealthMiddle = "wealth.middle";
        public const string WealthHigh = "wealth.high";

        /// <summary>0–100.</summary>
        public const string Happiness = "happiness";

        /// <summary>0–1.</summary>
        public const string Unemployment = "unemployment";

        /// <summary>0–1.</summary>
        public const string CrimeRate = "crimeRate";

        /// <summary>Unweighted mean of the four pollution channels, 0–1.</summary>
        /// <remarks>
        /// Named for the mean rather than for the struct because <c>ServiceCoverage</c> below would
        /// otherwise shadow the contract type of the same name inside this class.
        /// </remarks>
        public const string PollutionMean = "pollution";

        /// <summary>Unweighted mean of the nine service coverages, 0–1.</summary>
        public const string ServiceCoverageMean = "serviceCoverage";

        /// <summary>Mean one-way commute in minutes. City scope only — see the recorder for why.</summary>
        public const string CommuteMinutes = "commuteMinutes";

        /// <summary>0–1. City scope only — see the recorder for why.</summary>
        public const string TrafficCongestion = "trafficCongestion";

        // --- The city-statistics pass (snapshot v4) -------------------------------------------
        //
        // Eighteen city-scope names and three district-scope ones, and the split is the game's rather
        // than ours: CityStatisticsSystem is keyed by (StatisticType, parameter) with no district
        // dimension, so only the three counts whose buildings carry CurrentDistrict are recorded at
        // both scopes (scout 0004 §1.4, §9).
        //
        // RECORDED CONDITIONALLY AT DISTRICT SCOPE: uncollectedGarbage, attractionCount and
        // signatureBuildingCount. A district that could not measure one of the three is named in its
        // CityFallbackFields, and that month's sample is skipped rather than filed — for the same
        // reason rent and land value are not recorded here at all (see RecordSnapshot's remarks). A
        // fabricated value written into a series poisons every window computed against it afterwards,
        // and the only thing the fallback ruling changed is which fabrication it would be: the three
        // fall back to zero rather than to the city total, which is less harmful but no more true.
        // Skipping leaves an absence, which TryValueAt reports honestly as "not measured" — so wave
        // 2's Unmeasurable is answerable off a rehydrated month and not only off the live snapshot.
        //
        // But it is answerable only by PROBING THE HISTORY, never by reading the marker off a
        // rehydrated snapshot. SnapshotRehydration reconstructs a DistrictSnapshot from recorded
        // samples alone, so its CityFallbackFields comes back empty and HasCityFallbacks false —
        // the contract defaults — whatever the original month looked like. A wave-2 consumer that
        // asks a rehydrated district "did you fall back on this?" is therefore told "no, and the
        // value is 0", which is the one wrong answer this whole arrangement exists to prevent. The
        // marker answers the question on the live snapshot; TryValueAt answers it on a historical
        // one. Two surfaces, two mechanisms, and a consumer must not reach for the wrong one.
        //
        // Only these three, and only at district scope. Population, happiness, unemployment, crime,
        // education and wealth are recorded unconditionally at both scopes, exactly as they were
        // before this pass — and population in particular must stay unconditional, because
        // SnapshotRehydration probes it to decide whether a district existed in a given month.
        //
        // NOT RECORDED, DELIBERATELY: CitySnapshot.UnlockedFeatureIds and
        // CitySnapshot.IndustryTaxRates. Both are lists, and this class stores one double per series
        // per month — there is no scalar to file. A trigger may ask what is unlocked or what a
        // resource is taxed at *today*, off the live snapshot; there is no historical series behind
        // either and therefore no honest `delta` or `windowMonths` read. This is a decision, not an
        // omission: do not go looking for a series that was never written.

        /// <summary>Homeless residents. A count.</summary>
        public const string Homeless = "homeless";

        /// <summary>Homeless share of the moved-in population, 0–1. Not the game's 0–100 percentage.</summary>
        public const string HomelessShare = "homelessShare";

        /// <summary>Citizens who moved in. A count, not a rate.</summary>
        public const string CitizensMovedIn = "citizensMovedIn";

        /// <summary>Citizens who moved away. A count.</summary>
        public const string CitizensMovedAway = "citizensMovedAway";

        /// <summary>Citizens who moved away specifically because they were unhappy. A count.</summary>
        public const string MovedAwayUnhappy = "movedAwayUnhappy";

        /// <summary>Births. A count.</summary>
        public const string Births = "births";

        /// <summary>Deaths. A count.</summary>
        public const string Deaths = "deaths";

        /// <summary>Garbage produced per day. A rate, not a stockpile — see <see cref="UncollectedGarbage"/>.</summary>
        public const string GarbageProductionRate = "garbageProductionRate";

        /// <summary>Tourists in the city. A count.</summary>
        public const string Tourists = "tourists";

        /// <summary>The city's attractiveness index. Dimensionless and stored raw, not normalised.</summary>
        public const string Attractiveness = "attractiveness";

        /// <summary>Hotel rooms occupied.</summary>
        public const string LodgingUsed = "lodgingUsed";

        /// <summary>Hotel rooms available.</summary>
        public const string LodgingTotal = "lodgingTotal";

        /// <summary>The achieved milestone. Also the city level; CS2 has only the one number.</summary>
        public const string MilestoneLevel = "milestoneLevel";

        /// <summary>Accumulated experience.</summary>
        public const string Experience = "experience";

        /// <summary>Progress toward the next milestone, 0–1.</summary>
        public const string MilestoneProgress = "milestoneProgress";

        /// <summary>
        /// Garbage sitting uncollected at producers. City and district scope. Distinct from
        /// <see cref="GarbageProductionRate"/>: production is what the city makes, this is what
        /// nobody has come to collect.
        /// </summary>
        public const string UncollectedGarbage = "uncollectedGarbage";

        /// <summary>Buildings contributing attractiveness. City and district scope.</summary>
        public const string AttractionCount = "attractionCount";

        /// <summary>Signature buildings. City and district scope. There is no landmark count.</summary>
        public const string SignatureBuildingCount = "signatureBuildingCount";
    }
}
