// Compiled into BOTH Agora.Mod and (by <Compile Link>) tests/Agora.Core.Tests: it must stay free of
// every Game.*, Unity.* and Colossal.* type. #nullable disable keeps it warning-clean in the test
// project, which enables nullable, without annotating a file the mod compiles unannotated.
#nullable disable

using System;
using System.Collections.Generic;
using Agora.Core.Contracts;

namespace Agora.Mod.Sensors
{
    /// <summary>
    /// Rebuilds past <see cref="CitySnapshot"/>s from <see cref="MetricHistory"/>, so the engine's
    /// trend window survives a reload.
    ///
    /// <para>
    /// <c>AgoraRuntime</c> held its snapshot history in a session-static list that
    /// <c>ResetForNewSave</c> cleared at every save boundary, so <c>EngineTickInput.SnapshotHistory</c>
    /// was empty on the first tick after every load. Every <c>delta</c> and <c>windowMonths</c> read
    /// goes through exactly that list: a player who played twelve months straight saw a trend fire,
    /// and the same player quitting to menu each year never did. That is the literal definition of
    /// desync, and it is why the samples now come back off disk.
    /// </para>
    ///
    /// <para>
    /// <b>A rehydrated snapshot carries only what was recorded.</b> Every other field sits at its
    /// default, and a defaulted <c>0</c> is indistinguishable from a measured one — so the honest
    /// bound on this type is the set of fields something actually reads off a <i>historical</i>
    /// snapshot, not the set <see cref="CitySnapshot"/> happens to declare. Today that set is closed
    /// and small: <c>IndicesEngine.Compute</c> is the only reader of the history, and it takes
    /// <c>Population</c> and <c>Education</c> off the city and <c>Education</c> and
    /// <c>Wealth[WealthTier.Low]</c> off each district. Widening what is recorded is how this type
    /// grows; widening what is *returned* without recording it first is how it starts lying.
    /// </para>
    ///
    /// <para>
    /// The v4 city-statistics vocabulary was added on exactly that rule: every field of
    /// <c>Statistics</c>, <c>Tourism</c> and <c>Progression</c>, and the three per-district counts,
    /// are read back here <i>because</i> <see cref="MetricHistory.RecordSnapshot"/> files them. The
    /// two lists on the snapshot — unlocked features and per-resource tax rates — are neither
    /// recorded nor returned, and that pairing is the point: a list has no scalar series behind it,
    /// so returning one here would be an invention rather than a measurement.
    /// </para>
    ///
    /// <para>
    /// The rule this file follows, uniformly: a field is written only when its series holds a sample
    /// in that month, and is otherwise left at the contract default. So the recorded set in
    /// <see cref="MetricHistory"/> is exactly the set a caller may trust here, and every other field
    /// on the returned object is a zero that means "never measured".
    /// </para>
    /// </summary>
    public static class SnapshotRehydration
    {
        /// <summary>
        /// The most recent <paramref name="months"/> snapshots at or before <paramref name="asOf"/>,
        /// oldest first, each carrying its own <see cref="CitySnapshot.Date"/>. Never null; an empty
        /// list is the correct answer for a save with no recorded history and is not an error.
        /// </summary>
        public static List<CitySnapshot> Restore(MetricHistory history, SimDate asOf, int months)
        {
            var restored = new List<CitySnapshot>();
            if (history == null || months <= 0) return restored;

            List<int> recordedMonths = history.RecordedMonths(asOf);
            if (recordedMonths.Count == 0) return restored;

            List<string> districtIds = DistrictIds(history);

            // Oldest first, and the oldest kept is the one that leaves exactly `months` behind it.
            // Trimming from the front rather than the back matters: the engine's window looks
            // backwards from today, so the months worth dropping are the ones nothing can still reach.
            int first = Math.Max(0, recordedMonths.Count - months);

            for (int i = first; i < recordedMonths.Count; i++)
            {
                // A negative month cannot name a SimDate and could only have come from a hand-edited
                // file. Dropped, not thrown on: this runs inside a load handler, and a sensor that
                // can throw there takes the game down over a typo in a JSON file.
                if (recordedMonths[i] < 0) continue;

                restored.Add(BuildSnapshot(history, recordedMonths[i], districtIds));
            }

            return restored;
        }

        /// <summary>
        /// Every district id the history mentions, sorted ordinal ascending — the order
        /// <see cref="CitySnapshot.Districts"/> is contractually in.
        /// </summary>
        private static List<string> DistrictIds(MetricHistory history)
        {
            var ids = new List<string>();
            var seen = new HashSet<string>(StringComparer.Ordinal);

            // SeriesKeys is already sorted, so the ids come out sorted without a second sort: the
            // scope is the key's prefix, and a prefix order is an id order.
            List<string> keys = history.SeriesKeys();
            for (int i = 0; i < keys.Count; i++)
            {
                string scope = MetricHistory.ScopeOf(keys[i]);
                if (scope == null) continue;
                if (string.Equals(scope, MetricHistory.CityScope, StringComparison.Ordinal)) continue;
                if (seen.Add(scope)) ids.Add(scope);
            }

            return ids;
        }

        private static CitySnapshot BuildSnapshot(MetricHistory history, int totalMonths, List<string> districtIds)
        {
            var snapshot = new CitySnapshot
            {
                // A sample is keyed by month, not by day — the day of the capture was never recorded
                // and cannot be invented. The first of the month is the canonical stand-in, and it is
                // safe for the only comparisons a historical snapshot is subjected to, which are
                // whole-month distances and a strictly-earlier-than test.
                Date = new SimDate(totalMonths / 12, (totalMonths % 12) + 1, 1),
                Population = ReadInt(history, MetricHistory.CityScope, MetricHistory.Population, totalMonths),
                Happiness = Read(history, MetricHistory.CityScope, MetricHistory.Happiness, totalMonths),
                Unemployment = Read(history, MetricHistory.CityScope, MetricHistory.Unemployment, totalMonths),
                CrimeRate = Read(history, MetricHistory.CityScope, MetricHistory.CrimeRate, totalMonths),
                Education = ReadEducation(history, MetricHistory.CityScope, totalMonths),
                Wealth = ReadWealth(history, MetricHistory.CityScope, totalMonths),
                AverageLandValue = Read(history, MetricHistory.CityScope, MetricHistory.LandValue, totalMonths),
                AverageRent = Read(history, MetricHistory.CityScope, MetricHistory.Rent, totalMonths),
                AverageCommuteMinutes = Read(history, MetricHistory.CityScope, MetricHistory.CommuteMinutes, totalMonths),
                TrafficCongestion = Read(history, MetricHistory.CityScope, MetricHistory.TrafficCongestion, totalMonths),

                // The city-statistics pass. Every one of these is read back because every one of them
                // is recorded — the two sets are the same set, and they are what a wave-3 `delta` or
                // `windowMonths` trigger will read off a historical month. A field returned here that
                // the recorder does not file would be a fabricated zero for every month before the
                // current session.
                Statistics = ReadStatistics(history, totalMonths),
                Tourism = ReadTourism(history, totalMonths),
                Progression = ReadProgression(history, totalMonths),
                UncollectedGarbage = Read(history, MetricHistory.CityScope, MetricHistory.UncollectedGarbage, totalMonths),
                AttractionCount = ReadInt(history, MetricHistory.CityScope, MetricHistory.AttractionCount, totalMonths),
                SignatureBuildingCount = ReadInt(history, MetricHistory.CityScope, MetricHistory.SignatureBuildingCount, totalMonths),
            };

            for (int i = 0; i < districtIds.Count; i++)
            {
                string id = districtIds[i];

                // A district joins a month's snapshot only if it recorded something that month.
                // Districts are created and renamed mid-game, and carrying one back into a month it
                // did not exist in would hand the gentrification leg a baseline for a place that had
                // no rents yet.
                if (!RecordedIn(history, id, totalMonths)) continue;

                snapshot.Districts.Add(new DistrictSnapshot
                {
                    Id = id,
                    Name = id,
                    Population = ReadInt(history, id, MetricHistory.Population, totalMonths),
                    Happiness = Read(history, id, MetricHistory.Happiness, totalMonths),
                    Unemployment = Read(history, id, MetricHistory.Unemployment, totalMonths),
                    CrimeRate = Read(history, id, MetricHistory.CrimeRate, totalMonths),
                    Education = ReadEducation(history, id, totalMonths),
                    Wealth = ReadWealth(history, id, totalMonths),
                    AverageLandValue = Read(history, id, MetricHistory.LandValue, totalMonths),
                    AverageRent = Read(history, id, MetricHistory.Rent, totalMonths),

                    // The three v4 fields recorded at district scope. Nothing else from that pass is
                    // read back here, because nothing else is recorded here — DistrictSnapshot has no
                    // property for it and the game has no district figure behind it.
                    UncollectedGarbage = Read(history, id, MetricHistory.UncollectedGarbage, totalMonths),
                    AttractionCount = ReadInt(history, id, MetricHistory.AttractionCount, totalMonths),
                    SignatureBuildingCount = ReadInt(history, id, MetricHistory.SignatureBuildingCount, totalMonths),
                });
            }

            return snapshot;
        }

        /// <summary>
        /// Whether <paramref name="districtId"/> has any sample at all in that month. Population is
        /// the probe: it is recorded unconditionally for every district in every assembled snapshot,
        /// where rent and land value are recorded only where they were measured.
        /// </summary>
        private static bool RecordedIn(MetricHistory history, string districtId, int totalMonths)
        {
            double ignored;
            return history.TryValueAt(
                MetricHistory.DistrictKey(districtId, MetricHistory.Population), totalMonths, out ignored);
        }

        private static EducationDistribution ReadEducation(MetricHistory history, string scope, int totalMonths)
        {
            return new EducationDistribution(
                Read(history, scope, MetricHistory.EducationUneducated, totalMonths),
                Read(history, scope, MetricHistory.EducationPoorlyEducated, totalMonths),
                Read(history, scope, MetricHistory.EducationEducated, totalMonths),
                Read(history, scope, MetricHistory.EducationWellEducated, totalMonths),
                Read(history, scope, MetricHistory.EducationHighlyEducated, totalMonths));
        }

        /// <summary>
        /// The city-statistics block. City scope only, for the reason the contract type itself gives:
        /// the game's statistics system has no district dimension, so there is no district series to
        /// read and a district copy would be the city's number under a local name.
        /// </summary>
        private static CityStatistics ReadStatistics(MetricHistory history, int totalMonths)
        {
            const string Scope = MetricHistory.CityScope;

            return new CityStatistics(
                ReadInt(history, Scope, MetricHistory.Homeless, totalMonths),
                Read(history, Scope, MetricHistory.HomelessShare, totalMonths),
                ReadInt(history, Scope, MetricHistory.CitizensMovedIn, totalMonths),
                ReadInt(history, Scope, MetricHistory.CitizensMovedAway, totalMonths),
                ReadInt(history, Scope, MetricHistory.MovedAwayUnhappy, totalMonths),
                ReadInt(history, Scope, MetricHistory.Births, totalMonths),
                ReadInt(history, Scope, MetricHistory.Deaths, totalMonths),
                Read(history, Scope, MetricHistory.GarbageProductionRate, totalMonths));
        }

        private static TourismLevels ReadTourism(MetricHistory history, int totalMonths)
        {
            const string Scope = MetricHistory.CityScope;

            return new TourismLevels(
                ReadInt(history, Scope, MetricHistory.Tourists, totalMonths),
                ReadInt(history, Scope, MetricHistory.Attractiveness, totalMonths),
                ReadInt(history, Scope, MetricHistory.LodgingUsed, totalMonths),
                ReadInt(history, Scope, MetricHistory.LodgingTotal, totalMonths));
        }

        private static ProgressionState ReadProgression(MetricHistory history, int totalMonths)
        {
            const string Scope = MetricHistory.CityScope;

            return new ProgressionState(
                ReadInt(history, Scope, MetricHistory.MilestoneLevel, totalMonths),
                ReadInt(history, Scope, MetricHistory.Experience, totalMonths),
                Read(history, Scope, MetricHistory.MilestoneProgress, totalMonths));
        }

        private static WealthDistribution ReadWealth(MetricHistory history, string scope, int totalMonths)
        {
            return new WealthDistribution(
                Read(history, scope, MetricHistory.WealthLow, totalMonths),
                Read(history, scope, MetricHistory.WealthMiddle, totalMonths),
                Read(history, scope, MetricHistory.WealthHigh, totalMonths));
        }

        private static double Read(MetricHistory history, string scope, string metric, int totalMonths)
        {
            double value;
            return history.TryValueAt(MetricHistory.ScopedKey(scope, metric), totalMonths, out value)
                ? value
                : 0.0;
        }

        /// <summary>
        /// A count, stored as a double and read back as the integer it was. Rounded rather than
        /// truncated: a population that serialised as 41999.999999999993 is forty-two thousand
        /// people, and truncation would quietly lose one of them on every reload.
        /// </summary>
        private static int ReadInt(MetricHistory history, string scope, string metric, int totalMonths)
        {
            double value = Read(history, scope, metric, totalMonths);
            if (value <= int.MinValue) return int.MinValue;
            if (value >= int.MaxValue) return int.MaxValue;
            return (int)Math.Round(value, MidpointRounding.AwayFromZero);
        }
    }
}
