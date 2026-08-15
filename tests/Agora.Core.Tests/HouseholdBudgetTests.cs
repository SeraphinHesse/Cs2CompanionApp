// Requires the Sensors <Compile Link> lines in Agora.Core.Tests.csproj. See the comment there for
// why they are linked rather than referenced.

using System.Collections.Generic;
using Agora.Core.Contracts;
using Agora.Mod.Sensors;
using Xunit;

namespace Agora.Core.Tests
{
    /// <summary>
    /// The household budget fields added in <c>CitySnapshot</c> v3: upkeep, goods, and the margin
    /// left over.
    ///
    /// <para>
    /// The game computes the same five lines in <c>ResidentsSection.GetHouseholdEconomyData</c> and
    /// shows them when a district is selected. Agora already read two of them — salary and rent — and
    /// walked straight past the other two while holding the component they live on. The interesting
    /// figure is not any one line but <see cref="DemographicTally.DisposableMargin"/>, because that is
    /// the one that moves when the player raises a cost, and so the one an issue model can be built
    /// on.
    /// </para>
    ///
    /// <para>
    /// Golden values throughout, per <c>/write-test</c>. A margin computed by re-deriving the formula
    /// in the assertion would pass against any formula.
    /// </para>
    /// </summary>
    public class HouseholdBudgetTests
    {
        /// <summary>Matches <c>SensorCalibration.RentPeriodDays</c>.</summary>
        private const double RentPeriod = 30.0;

        // --- The margin --------------------------------------------------------------------------

        /// <summary>
        /// One household, every figure chosen so the arithmetic is checkable by eye: 100/day in, a
        /// 600 rent over a 30-day period (20/day), 5/day of upkeep, 20/day of goods and 5/day of
        /// utility fees. 50 of 100 is committed, so half the day's income is left.
        /// </summary>
        [Fact]
        public void Margin_IsWhatIsLeftOfADaysIncome()
        {
            var tally = new DemographicTally();
            tally.AddHousehold(wealth: 1000.0, rent: 600.0, dailySalary: 100.0,
                               dailyUpkeep: 5.0, dailyResourceSpend: 20.0, dailyFees: 5.0);

            Assert.Equal(0.50, tally.DisposableMargin(RentPeriod)!.Value, 12);

            Assert.Equal(5.0, tally.MeanDailyUpkeep()!.Value, 12);
            Assert.Equal(20.0, tally.MeanDailyResourceSpend()!.Value, 12);
            Assert.Equal(5.0, tally.MeanDailyFees()!.Value, 12);
        }

        /// <summary>
        /// The margin and the rent burden are two views of the same ledger, so they must agree:
        /// margin = 1 − rentBurden − (upkeep + goods + fees)/income. If they ever disagree, one of
        /// them is dividing by something the other is not.
        /// </summary>
        [Fact]
        public void Margin_AgreesWithRentBurden()
        {
            var tally = new DemographicTally();
            tally.AddHousehold(1200.0, 900.0, 80.0, 3.0, 13.0, 4.0);

            double burden = tally.RentBurden(RentPeriod)!.Value;
            double margin = tally.DisposableMargin(RentPeriod)!.Value;

            Assert.Equal(1.0 - burden - (3.0 + 13.0 + 4.0) / 80.0, margin, 12);
        }

        /// <summary>
        /// Households spending more than they earn read negative rather than zero. Clamping here
        /// would hide exactly the households the cost-of-living issue exists to find — a district at
        /// −0.4 is not the same city as one at 0.
        /// </summary>
        [Fact]
        public void Margin_GoesNegativeRatherThanFlooringAtZero()
        {
            var tally = new DemographicTally();
            tally.AddHousehold(50.0, rent: 3000.0, dailySalary: 100.0,
                               dailyUpkeep: 10.0, dailyResourceSpend: 24.0, dailyFees: 6.0);

            // 100/day in; 100/day of rent plus 40 of everything else out.
            Assert.Equal(-0.40, tally.DisposableMargin(RentPeriod)!.Value, 12);
        }

        /// <summary>
        /// A district where nobody rents has no rent to subtract — that is a measurement, not a gap.
        /// Returning null here would hand the district the city's margin and mark it a fallback,
        /// which is a worse answer than the true one.
        /// </summary>
        [Fact]
        public void Margin_TreatsAnAbsentCostAsZeroRatherThanUnmeasurable()
        {
            var tally = new DemographicTally();
            tally.AddHousehold(500.0, rent: null, dailySalary: 40.0,
                               dailyUpkeep: 0.0, dailyResourceSpend: 8.0, dailyFees: 2.0);

            Assert.Null(tally.RentBurden(RentPeriod));
            Assert.Equal(0.75, tally.DisposableMargin(RentPeriod)!.Value, 12);
        }

        /// <summary>
        /// Income is the one exception: with no denominator there is nothing to report, and a
        /// fabricated zero would tell the engine an all-retiree district is fully committed.
        /// </summary>
        [Fact]
        public void Margin_IsNullWithoutIncome()
        {
            var tally = new DemographicTally();
            tally.AddHousehold(500.0, rent: 600.0, dailySalary: null,
                               dailyUpkeep: 5.0, dailyResourceSpend: 5.0, dailyFees: 5.0);

            Assert.Null(tally.DisposableMargin(RentPeriod));

            var empty = new DemographicTally();
            Assert.Null(empty.DisposableMargin(RentPeriod));
            Assert.Null(empty.MeanDailyUpkeep());
            Assert.Null(empty.MeanDailyResourceSpend());
            Assert.Null(empty.MeanDailyFees());
        }

        /// <summary>
        /// Zero upkeep is a real reading and is counted. Rent's convention is the opposite — a rent of
        /// zero means "pays none" and is excluded — and applying the rent rule here would drop every
        /// household with nothing to repair and report the mean of only those that did.
        /// </summary>
        [Fact]
        public void Zeros_AreCountedForUpkeepAndGoods()
        {
            var tally = new DemographicTally();
            tally.AddHousehold(100.0, 300.0, 50.0, dailyUpkeep: 0.0, dailyResourceSpend: 0.0, dailyFees: 0.0);
            tally.AddHousehold(100.0, 300.0, 50.0, dailyUpkeep: 8.0, dailyResourceSpend: 20.0, dailyFees: 6.0);

            // The mean over two households, not over the one that spent something.
            Assert.Equal(4.0, tally.MeanDailyUpkeep()!.Value, 12);
            Assert.Equal(10.0, tally.MeanDailyResourceSpend()!.Value, 12);
            Assert.Equal(3.0, tally.MeanDailyFees()!.Value, 12);
        }

        /// <summary>
        /// A ratio of means, not a mean of ratios. The distinction is invisible on a uniform sample
        /// and decides the answer on a mixed one: here a high earner and a low earner pay the same
        /// rent, and the mean of their individual burdens is not the burden of the mean household.
        /// </summary>
        [Fact]
        public void Margin_IsARatioOfMeans()
        {
            var tally = new DemographicTally();
            tally.AddHousehold(100.0, rent: 300.0, dailySalary: 10.0, 0.0, 0.0, 0.0);
            tally.AddHousehold(100.0, rent: 300.0, dailySalary: 90.0, 0.0, 0.0, 0.0);

            // Mean rent 300 → 10/day; mean income 50/day. 1 - 10/50 = 0.8.
            Assert.Equal(0.80, tally.DisposableMargin(RentPeriod)!.Value, 12);

            // The mean of the two per-household margins would be (1 - 1.0 + 1 - 1.0/9.0) / 2 ≈ 0.444,
            // which is a different claim about the same district.
            Assert.NotEqual(0.444, tally.DisposableMargin(RentPeriod)!.Value, 3);
        }

        // --- Fees --------------------------------------------------------------------------------

        /// <summary>
        /// The reason fees are worth the extra component lookups: they are the only line here the
        /// player sets, so raising the utility sliders has to move the margin. Two identical
        /// districts, one paying twice the fees, must not report the same household pressure.
        /// </summary>
        [Fact]
        public void Fees_MoveTheMarginTheWayASliderWould()
        {
            var cheap = new DemographicTally();
            cheap.AddHousehold(100.0, rent: 300.0, dailySalary: 100.0,
                               dailyUpkeep: 0.0, dailyResourceSpend: 0.0, dailyFees: 10.0);

            var dear = new DemographicTally();
            dear.AddHousehold(100.0, rent: 300.0, dailySalary: 100.0,
                              dailyUpkeep: 0.0, dailyResourceSpend: 0.0, dailyFees: 20.0);

            // 10/day rent in both. 0.90 vs 0.80: the ten extra units of fee are the whole difference.
            Assert.Equal(0.80, cheap.DisposableMargin(RentPeriod)!.Value, 12);
            Assert.Equal(0.70, dear.DisposableMargin(RentPeriod)!.Value, 12);
        }

        /// <summary>
        /// A city with no utility fee buffer — one not yet deserialized — bills nothing, and that is a
        /// measurement rather than a gap. The margin is still reported; it is simply a margin with no
        /// fees in it.
        /// </summary>
        [Fact]
        public void NoFees_IsZeroRatherThanUnmeasurable()
        {
            var tally = new DemographicTally();
            tally.AddHousehold(100.0, rent: null, dailySalary: 50.0,
                               dailyUpkeep: 0.0, dailyResourceSpend: 10.0, dailyFees: 0.0);

            Assert.Equal(0.0, tally.MeanDailyFees()!.Value, 12);
            Assert.Equal(0.80, tally.DisposableMargin(RentPeriod)!.Value, 12);
        }

        // --- Assembly ----------------------------------------------------------------------------

        /// <summary>
        /// The three new fields are best-effort like every other district number: a district too small
        /// to average takes the city's figure and says so, so the dashboard cannot present it as a
        /// local fact and the mandate packet cannot score against it.
        /// </summary>
        [Fact]
        public void AnUnmeasuredDistrict_FallsBackAndSaysSo()
        {
            var city = new CityReading
            {
                AverageHouseholdUpkeep = 6.0,
                AverageHouseholdResourceSpend = 24.0,
                AverageHouseholdFees = 9.0,
                DisposableMargin = 0.30
            };

            var districts = new List<DistrictReading>
            {
                new DistrictReading { Id = "district-01", Name = "Measured",
                                      AverageHouseholdUpkeep = 2.0,
                                      AverageHouseholdResourceSpend = 8.0,
                                      AverageHouseholdFees = 3.0,
                                      DisposableMargin = 0.70 },
                new DistrictReading { Id = "district-02", Name = "Too small" }
            };

            CitySnapshot snapshot = SnapshotAssembly.Build(new SimDate(1994, 3, 1), city, districts);

            DistrictSnapshot measured = snapshot.Districts[0];
            Assert.Equal(0.70, measured.DisposableMargin, 12);
            Assert.DoesNotContain("DisposableMargin", measured.CityFallbackFields);

            DistrictSnapshot small = snapshot.Districts[1];
            Assert.True(small.HasCityFallbacks);
            Assert.Equal(6.0, small.AverageHouseholdUpkeep, 12);
            Assert.Equal(24.0, small.AverageHouseholdResourceSpend, 12);
            Assert.Equal(9.0, small.AverageHouseholdFees, 12);
            Assert.Equal(0.30, small.DisposableMargin, 12);

            Assert.Contains("AverageHouseholdUpkeep", small.CityFallbackFields);
            Assert.Contains("AverageHouseholdResourceSpend", small.CityFallbackFields);
            Assert.Contains("AverageHouseholdFees", small.CityFallbackFields);
            Assert.Contains("DisposableMargin", small.CityFallbackFields);
        }

        /// <summary>
        /// The fallback names are property names on <c>DistrictSnapshot</c>, spelled out as constants
        /// rather than reflected. A typo there produces a marker no consumer recognises — the
        /// dashboard's matcher would not dim the value, and a city number would render as district
        /// truth with nothing anywhere reporting a problem.
        /// </summary>
        [Fact]
        public void FallbackFieldNames_MatchThePropertiesTheyName()
        {
            CitySnapshot snapshot = SnapshotAssembly.Build(
                new SimDate(1994, 3, 1), new CityReading(),
                new List<DistrictReading> { new DistrictReading { Id = "district-01" } });

            List<string> named = snapshot.Districts[0].CityFallbackFields;
            var district = typeof(DistrictSnapshot);

            for (int i = 0; i < named.Count; i++)
            {
                Assert.True(district.GetProperty(named[i]) != null,
                            "cityFallbackFields names '" + named[i] +
                            "', which is not a property of DistrictSnapshot.");
            }

            // Sorted ordinal, and every field the district could not measure is in it.
            Assert.Contains("DisposableMargin", named);
            var sorted = new List<string>(named);
            sorted.Sort(System.StringComparer.Ordinal);
            Assert.Equal(sorted, named);
        }

        /// <summary>
        /// The version <see cref="CitySnapshot"/> declares is the version
        /// <c>data/schemas/snapshot.schema.json</c> pins with its <c>const</c> —
        /// <c>/schema-change</c> steps 1 and 4, which are two sides of one change.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Deliberately <b>not</b> written as <c>Assert.Equal(4, …)</c>. A test that memorises the
        /// current number goes red on the next bump for a reason that has nothing to do with what it
        /// guards, and the fix for it is to type the new number in two places — which is exactly the
        /// one-sided bump this test exists to catch. Comparing the two sides to each other instead
        /// means the assertion survives every future bump and fails only when they disagree.
        /// </para>
        /// <para>
        /// The one-sided bump is a real failure mode here rather than a theoretical one: the C#
        /// default and the schema <c>const</c> are edited in different files by different steps of
        /// the checklist, nothing compiles them together, and <c>SidecarSchema</c>'s own remarks
        /// record that <c>PoliticalState</c>'s default and <c>CurrentStateVersion</c> had already
        /// drifted apart once for precisely that reason.
        /// </para>
        /// </remarks>
        [Fact]
        public void TheSnapshotContractVersion_MatchesTheShippedSchema()
        {
            string path = System.IO.Path.Combine(RepoRoot(), "data", "schemas", "snapshot.schema.json");
            Assert.True(System.IO.File.Exists(path),
                        "data/schemas/snapshot.schema.json is the wire mirror of CitySnapshot; it must ship.");

            var root = Newtonsoft.Json.Linq.JObject.Parse(System.IO.File.ReadAllText(path));
            Newtonsoft.Json.Linq.JToken? pinned = root["properties"]?["schemaVersion"]?["const"];

            Assert.True(pinned != null,
                        "snapshot.schema.json must pin schemaVersion with a const, or nothing checks " +
                        "that the two sides of the contract agree.");

            // Explicit conversion rather than Value<int>(): the fluent form is an extension method on
            // IEnumerable<JToken> and is not in scope here, where the type is fully qualified.
            Assert.Equal((int)pinned!, new CitySnapshot().SchemaVersion);
        }

        /// <summary>
        /// Walks up from the test binary to the repository root, the same way
        /// <c>ShippedTuningTests</c> does and for the same reason: the runner's working directory
        /// varies, so <c>AppContext.BaseDirectory</c> is the only stable anchor.
        /// </summary>
        private static string RepoRoot()
        {
            var dir = new System.IO.DirectoryInfo(System.AppContext.BaseDirectory);
            while (dir != null)
            {
                if (System.IO.File.Exists(System.IO.Path.Combine(dir.FullName, "Agora.sln"))) return dir.FullName;
                dir = dir.Parent;
            }

            throw new System.InvalidOperationException(
                "Could not locate the repository root (no Agora.sln above " + System.AppContext.BaseDirectory + ").");
        }

        /// <summary>
        /// The six budget properties keep the exact names the Districts panel passes as its
        /// <c>field</c> props.
        /// </summary>
        /// <remarks>
        /// This is the one binding-contract failure that is invisible on both sides. The panel's
        /// <c>makeFallbackSet</c> matches <c>cityFallbackFields</c> — which carries these C# property
        /// names — case-insensitively against the string each cell declares. Rename a property here
        /// and the C# still compiles, the TypeScript still compiles, the panel still renders, and the
        /// only symptom is that a city number quietly stops being dimmed and starts reading as a local
        /// fact. That is exactly what §6 forbids, so the names are pinned as literals: this test is
        /// the compile error the two build systems cannot give each other.
        /// <para>
        /// If one has to change, change it here, in <c>DistrictBudgetView</c> in
        /// <c>ui/types/bindings.d.ts</c>, in the <c>UiJson.Number</c> calls in
        /// <c>AgoraUiPayloads.DistrictDetailPayload.Write</c>, and in the <c>field</c> props in
        /// <c>ui/src/panels/Districts/DistrictDetail.tsx</c> — in that order, per
        /// <c>docs/contracts/ui_bindings.md</c> §4.4.
        /// </para>
        /// </remarks>
        [Fact]
        public void TheBudgetFieldNames_ArePinnedForTheDashboard()
        {
            string[] pinned =
            {
                "AverageRent",
                "RentBurden",
                "AverageHouseholdUpkeep",
                "AverageHouseholdResourceSpend",
                "AverageHouseholdFees",
                "DisposableMargin",
            };

            var district = typeof(DistrictSnapshot);

            for (int i = 0; i < pinned.Length; i++)
            {
                Assert.True(district.GetProperty(pinned[i]) != null,
                            "DistrictSnapshot." + pinned[i] + " is what the Districts panel passes as " +
                            "its `field` prop. Renaming it silently stops that cell being marked as a " +
                            "city fallback — see docs/contracts/ui_bindings.md section 4.4.");
            }

            // ...and they are the names that actually reach the panel, not just names that exist.
            CitySnapshot snapshot = SnapshotAssembly.Build(
                new SimDate(1994, 3, 1), new CityReading(),
                new List<DistrictReading> { new DistrictReading { Id = "district-01" } });

            for (int i = 0; i < pinned.Length; i++)
            {
                Assert.Contains(pinned[i], snapshot.Districts[0].CityFallbackFields);
            }
        }
    }
}
