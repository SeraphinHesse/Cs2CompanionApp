using System.Collections.Generic;
using System.Reflection;
using Agora.Core.Contracts;
using Agora.Mod.Sensors;
using Xunit;

namespace Agora.Core.Tests
{
    /// <summary>
    /// The v4 snapshot contract — what the city-statistics pass added, and the three properties of it
    /// that are load-bearing rather than incidental.
    ///
    /// <para>
    /// 1. <b>Only three of the new fields are genuinely per-district</b>, and an unmeasured one has to
    /// say so. <c>CityStatisticsSystem</c> is keyed by <c>(StatisticType, parameter)</c> with no
    /// district dimension, so everything in <see cref="CityStatistics"/>, <see cref="TourismLevels"/>
    /// and <see cref="ProgressionState"/> is city-only at source; the three counts below are mirrored
    /// precisely because their buildings carry <c>CurrentDistrict</c>. The marking in
    /// <see cref="DistrictSnapshot.CityFallbackFields"/> is what stops the dashboard rendering a city
    /// number as a local fact and stops the mandate packet scoring a district against a figure that
    /// was never measured there.
    /// </para>
    ///
    /// <para>
    /// 2. <b>The two new lists come out sorted by their declared keys.</b> They are built by walking
    /// ECS chunks, whose order is not stable across runs, so an unsorted list is the classic silent
    /// desync: identical within a session, different across two loads of the same save.
    /// </para>
    ///
    /// <para>
    /// 3. <b>A capture taken before a save is loaded produces an empty but valid snapshot</b> rather
    /// than throwing or handing a null list on. A sensor that can throw can take the game down with
    /// it.
    /// </para>
    ///
    /// <para>
    /// Synthetic fixtures throughout, per <c>tests/CLAUDE.md</c>: they diff cleanly and they do not
    /// rot the next time the snapshot gains a field.
    /// </para>
    /// </summary>
    public class CityStatisticsContractTests
    {
        private static readonly SimDate Date = new SimDate(2034, 7, 1);

        /// <summary>
        /// The three names the fallback marker uses for the new counts. Written out rather than taken
        /// from the assembler's own constants: a test that read those would still pass if both sides
        /// were misspelled the same way, and the name is a contract with the dashboard's matcher.
        /// </summary>
        private static readonly string[] PerDistrictCountFields =
        {
            "AttractionCount", "SignatureBuildingCount", "UncollectedGarbage"
        };

        // ---- the three genuinely per-district counts ----------------------------------------------

        [Fact]
        public void ADistrictWithNoCountsMeasured_TakesTheCityFiguresAndSaysSo()
        {
            var city = new CityReading
            {
                UncollectedGarbage = 8_000.0,
                AttractionCount = 40,
                SignatureBuildingCount = 6
            };

            CitySnapshot snapshot = SnapshotAssembly.Build(
                Date, city, new List<DistrictReading> { new DistrictReading { Id = "district-01" } });

            DistrictSnapshot district = snapshot.Districts[0];

            Assert.Equal(8_000.0, district.UncollectedGarbage, 12);
            Assert.Equal(40, district.AttractionCount);
            Assert.Equal(6, district.SignatureBuildingCount);

            Assert.True(district.HasCityFallbacks);
            for (int i = 0; i < PerDistrictCountFields.Length; i++)
            {
                Assert.Contains(PerDistrictCountFields[i], district.CityFallbackFields);
            }
        }

        [Fact]
        public void ADistrictThatMeasuredItsOwnCounts_IsNotMarkedForThem()
        {
            // The half that actually matters. A fallback marker on a measured field is not a harmless
            // extra: the dashboard dims the cell and the mandate packet refuses to score against it,
            // so over-marking silently removes a real measurement from every consumer downstream.
            var city = new CityReading
            {
                UncollectedGarbage = 8_000.0,
                AttractionCount = 40,
                SignatureBuildingCount = 6
            };

            var districts = new List<DistrictReading>
            {
                new DistrictReading
                {
                    Id = "district-01",
                    Name = "Dockside",
                    UncollectedGarbage = 1_250.0,
                    AttractionCount = 3,
                    SignatureBuildingCount = 0
                }
            };

            DistrictSnapshot district = SnapshotAssembly.Build(Date, city, districts).Districts[0];

            Assert.Equal(1_250.0, district.UncollectedGarbage, 12);
            Assert.Equal(3, district.AttractionCount);

            // Zero is a measurement here, not an absence: a district can genuinely have no signature
            // building, and copying the city's six over it would invent five buildings.
            Assert.Equal(0, district.SignatureBuildingCount);

            for (int i = 0; i < PerDistrictCountFields.Length; i++)
            {
                Assert.DoesNotContain(PerDistrictCountFields[i], district.CityFallbackFields);
            }
        }

        [Fact]
        public void TheThreeNewFallbackNames_NameRealPropertiesOnDistrictSnapshot()
        {
            // A marker naming a property that does not exist is invisible: nothing throws, the
            // dashboard's matcher simply never fires, and a city number renders as district truth with
            // nothing anywhere reporting a problem.
            CitySnapshot snapshot = SnapshotAssembly.Build(
                Date, new CityReading(), new List<DistrictReading> { new DistrictReading { Id = "d" } });

            List<string> named = snapshot.Districts[0].CityFallbackFields;

            for (int i = 0; i < PerDistrictCountFields.Length; i++)
            {
                string field = PerDistrictCountFields[i];
                Assert.Contains(field, named);
                Assert.NotNull(typeof(DistrictSnapshot).GetProperty(field, BindingFlags.Public | BindingFlags.Instance));
            }
        }

        [Fact]
        public void ADistrictCarriesNoCopyOfTheCityOnlyBlocks()
        {
            // Deliberate absence, and the contract says why: mirroring Statistics, Tourism or
            // Progression onto a district would mean writing the city's number onto every district and
            // marking the whole block as a fallback on every capture forever. Pinned because the
            // "obvious" completion of the contract is to add them.
            var district = typeof(DistrictSnapshot);

            Assert.Null(district.GetProperty("Statistics"));
            Assert.Null(district.GetProperty("Tourism"));
            Assert.Null(district.GetProperty("Progression"));
            Assert.Null(district.GetProperty("UnlockedFeatureIds"));
            Assert.Null(district.GetProperty("IndustryTaxRates"));
        }

        // ---- the two new lists --------------------------------------------------------------------

        [Fact]
        public void UnlockedFeatureIds_ComeOutSortedOrdinal()
        {
            var city = new CityReading();
            city.UnlockedFeatureIds.Add("Feature_Zoning_Signature");
            city.UnlockedFeatureIds.Add("Feature_Basic_Roads");
            city.UnlockedFeatureIds.Add("Feature_Transport_Bus");

            CitySnapshot snapshot = SnapshotAssembly.Build(Date, city, null);

            Assert.Equal(
                new List<string> { "Feature_Basic_Roads", "Feature_Transport_Bus", "Feature_Zoning_Signature" },
                snapshot.UnlockedFeatureIds);
        }

        [Fact]
        public void IndustryTaxRates_ComeOutSortedByAreaThenResourceIndex()
        {
            // Keyed on the game's own dense resource index rather than the Resource flag value, which
            // is a bitfield up to 1 << 40 and a poor sort key. The order is declared on the contract,
            // so it is the assembler's job to hold it even when the sensor already sorted - a lane
            // that hands over collection order is relying on a sort it cannot see.
            var city = new CityReading();
            city.IndustryTaxRates.Add(new ResourceTaxRate(TaxArea.Office, 21, "Software", 0.11));
            city.IndustryTaxRates.Add(new ResourceTaxRate(TaxArea.Commercial, 4, "Food", 0.10));
            city.IndustryTaxRates.Add(new ResourceTaxRate(TaxArea.Industrial, 2, "Grain", 0.09));
            city.IndustryTaxRates.Add(new ResourceTaxRate(TaxArea.Commercial, 1, "Beverages", 0.12));

            List<ResourceTaxRate> rates = SnapshotAssembly.Build(Date, city, null).IndustryTaxRates;

            Assert.Equal(4, rates.Count);
            Assert.Equal("Beverages", rates[0].ResourceName);
            Assert.Equal("Food", rates[1].ResourceName);
            Assert.Equal("Grain", rates[2].ResourceName);
            Assert.Equal("Software", rates[3].ResourceName);
        }

        [Fact]
        public void TheCityOnlyBlocks_ReachTheSnapshotAsMeasured()
        {
            // The blocks pass through whole; nothing derives, rescales or re-normalises them on the
            // way. Attractiveness in particular is stored raw because it is the exact quantity the
            // shipped city-attractiveness effect moves, which makes trigger and effect two ends of one
            // number.
            var city = new CityReading
            {
                Statistics = new CityStatistics(900, 0.06, 200, 1_400, 900, 300, 250, 4_000.0),
                Tourism = new TourismLevels(36_000, 137, 950, 1_000),
                Progression = new ProgressionState(12, 40_000, 0.4)
            };

            CitySnapshot snapshot = SnapshotAssembly.Build(Date, city, null);

            Assert.Equal(900, snapshot.Statistics.Homeless);
            Assert.Equal(0.06, snapshot.Statistics.HomelessShare, 12);
            Assert.Equal(900, snapshot.Statistics.MovedAwayUnhappy);
            Assert.Equal(4_000.0, snapshot.Statistics.GarbageProductionRate, 12);
            Assert.Equal(137, snapshot.Tourism.Attractiveness);
            Assert.Equal(12, snapshot.Progression.MilestoneLevel);
            Assert.Equal(0.4, snapshot.Progression.MilestoneProgress, 12);
        }

        // ---- the empty case -----------------------------------------------------------------------

        [Fact]
        public void ADefaultCitySnapshot_ReportsZerosAndEmptyListsRatherThanNulls()
        {
            // What a consumer holds before the first capture. Every one of these is reached without a
            // sensor having run, so a null here is a NullReferenceException in the UI binding or the
            // prompt builder rather than an empty panel.
            var snapshot = new CitySnapshot();

            Assert.Equal(4, snapshot.SchemaVersion);

            Assert.Equal(0, snapshot.Statistics.Homeless);
            Assert.Equal(0.0, snapshot.Statistics.HomelessShare, 12);
            Assert.Equal(0, snapshot.Statistics.Births);
            Assert.Equal(0.0, snapshot.Statistics.GarbageProductionRate, 12);
            Assert.Equal(0, snapshot.Tourism.Tourists);
            Assert.Equal(0, snapshot.Tourism.Attractiveness);
            Assert.Equal(0, snapshot.Tourism.LodgingTotal);
            Assert.Equal(0, snapshot.Progression.MilestoneLevel);
            Assert.Equal(0.0, snapshot.Progression.MilestoneProgress, 12);
            Assert.Equal(0.0, snapshot.UncollectedGarbage, 12);
            Assert.Equal(0, snapshot.AttractionCount);
            Assert.Equal(0, snapshot.SignatureBuildingCount);

            Assert.NotNull(snapshot.UnlockedFeatureIds);
            Assert.Empty(snapshot.UnlockedFeatureIds);
            Assert.NotNull(snapshot.IndustryTaxRates);
            Assert.Empty(snapshot.IndustryTaxRates);
        }

        [Fact]
        public void AssemblyWithNothingMeasuredAtAll_IsEmptyButValid()
        {
            // A capture taken before a save is loaded: no city reading, no districts. The assembler's
            // own contract is that this produces an empty but valid snapshot rather than throwing,
            // because a sensor that can throw can take the game down.
            CitySnapshot snapshot = SnapshotAssembly.Build(Date, null, null);

            Assert.Equal(Date, snapshot.Date);
            Assert.Equal(0, snapshot.Statistics.Homeless);
            Assert.Equal(0, snapshot.Tourism.Tourists);
            Assert.Equal(0, snapshot.Progression.MilestoneLevel);
            Assert.Equal(0.0, snapshot.UncollectedGarbage, 12);
            Assert.Equal(0, snapshot.AttractionCount);
            Assert.Equal(0, snapshot.SignatureBuildingCount);

            Assert.NotNull(snapshot.UnlockedFeatureIds);
            Assert.Empty(snapshot.UnlockedFeatureIds);
            Assert.NotNull(snapshot.IndustryTaxRates);
            Assert.Empty(snapshot.IndustryTaxRates);
            Assert.Empty(snapshot.Districts);
        }
    }
}
