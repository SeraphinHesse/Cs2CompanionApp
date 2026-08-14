using System.Collections.Generic;
using Agora.Core.Contracts;
using Colossal.Entities;
using Game.Areas;
using Game.Buildings;
using Game.Citizens;
using Game.City;
using Game.Common;
using Game.Prefabs;
using Game.Simulation;
using Game.Tools;
using Unity.Collections;
using Unity.Entities;

namespace Agora.Mod.Sensors
{
    /// <summary>
    /// The residents sensor: everything measured per person or per household — population,
    /// households, age, education, wealth, happiness, sickness, employment, commute, and the
    /// household budget (rent, rent burden, upkeep, goods and what is left over) — for the city and
    /// for each district.
    ///
    /// <para>
    /// One walk, many metrics, deliberately. All of these hang off the same
    /// <c>building → renter → household → citizen</c> chain, and walking it once per capture rather
    /// than once per metric is the difference between a sensor pass the simulation notices and one it
    /// does not. Scout 0001's open question 4 is answered here: residents do not carry a district
    /// reference themselves — the <b>building</b> carries <c>Game.Areas.CurrentDistrict</c>, and its
    /// <c>Renter</c> buffer leads to the households living in it.
    /// </para>
    ///
    /// <para>
    /// Wealth tiers are cut at city-wide quantiles from <c>blocs.wealthTierThresholds</c>, never at
    /// per-district ones. Cutting each district at its own quantiles would make every district
    /// exactly one third low, middle and high — erasing precisely the variation the bloc model is
    /// built to read.
    /// </para>
    /// </summary>
    public sealed partial class AgoraResidentsSensorSystem : AgoraSensorSystemBase
    {
        private EntityQuery _residentialBuildingQuery;
        private AgoraDistrictSensorSystem _districtSensor;

        /// <summary>
        /// Holds the city entity, which is where the <see cref="ServiceFee"/> buffer lives — the
        /// player's utility fee sliders. Resolved once; the buffer is read once per capture, never
        /// per building.
        /// </summary>
        private CitySystem _citySystem;

        private readonly CityReading _city = new CityReading();
        private readonly Dictionary<string, DistrictReading> _byDistrictId =
            new Dictionary<string, DistrictReading>();

        /// <summary>City-wide residents metrics from the most recent sample.</summary>
        public CityReading City => _city;

        /// <summary>
        /// Per-district residents metrics from the most recent sample, keyed by district id. Fields
        /// left null were not measurable for that district and will fall back to the city figure
        /// during assembly.
        /// </summary>
        public IReadOnlyDictionary<string, DistrictReading> Districts => _byDistrictId;

        protected override void CreateQueries()
        {
            _districtSensor = World.GetOrCreateSystemManaged<AgoraDistrictSensorSystem>();
            _citySystem = World.GetOrCreateSystemManaged<CitySystem>();

            // Renter is the buffer of tenants; Building carries the road link the other sensors need.
            // Companies rent too, so the walk re-checks each renter for a Household component rather
            // than trusting the buffer to hold only people.
            _residentialBuildingQuery = GetEntityQuery(new EntityQueryDesc
            {
                All = new[]
                {
                    ComponentType.ReadOnly<Renter>(),
                    ComponentType.ReadOnly<Game.Buildings.Building>(),
                },
                None = new[]
                {
                    ComponentType.ReadOnly<Deleted>(),
                    ComponentType.ReadOnly<Temp>(),
                },
            });
        }

        public override void Invalidate()
        {
            base.Invalidate();
            _byDistrictId.Clear();
        }

        protected override void Sample(SimDate date)
        {
            _districtSensor.EnsureSampled(date);
            IReadOnlyList<DistrictEntry> districts = _districtSensor.Districts;

            var cityTally = new DemographicTally();
            var tallyByDistrict = new Dictionary<Entity, DemographicTally>();
            for (int i = 0; i < districts.Count; i++)
            {
                tallyByDistrict[districts[i].Entity] = new DemographicTally();
            }

            Walk(cityTally, tallyByDistrict);

            SensorCalibration calibration = Calibration;
            WealthCuts cuts = WealthTiering.FromSamples(
                cityTally.HouseholdWealth,
                SensorTuning.Active.Blocs.WealthTierThresholds);

            PublishCity(cityTally, cuts, calibration);
            PublishDistricts(districts, tallyByDistrict, cuts, calibration);
        }

        private void Walk(DemographicTally cityTally, Dictionary<Entity, DemographicTally> tallyByDistrict)
        {
            // The player's utility fee sliders, read once for the whole capture. A city that has not
            // been deserialized yet has no buffer, in which case every property reports no fees —
            // which is a true statement about a city with no utilities rather than a reason to skip
            // the whole walk.
            DynamicBuffer<ServiceFee> fees = default(DynamicBuffer<ServiceFee>);
            bool hasFees = _citySystem != null && _citySystem.City != Entity.Null &&
                           EntityManager.TryGetBuffer(_citySystem.City, true, out fees);

            NativeArray<Entity> buildings = _residentialBuildingQuery.ToEntityArray(Allocator.TempJob);
            try
            {
                int stride = SubsampleStride(buildings.Length);

                for (int i = 0; i < buildings.Length; i++)
                {
                    Entity building = buildings[i];

                    // Subsampling keys off the entity index, not the loop counter. Chunk order is not
                    // stable across loads, so a positional stride would sample a different set of
                    // buildings each time and quietly make the snapshot non-reproducible.
                    if (stride > 1 && (building.Index % stride) != 0) continue;

                    DemographicTally districtTally = null;
                    CurrentDistrict currentDistrict;
                    if (EntityManager.TryGetComponent(building, out currentDistrict))
                    {
                        tallyByDistrict.TryGetValue(currentDistrict.m_District, out districtTally);
                    }

                    DynamicBuffer<Renter> renters;
                    if (!EntityManager.TryGetBuffer(building, true, out renters)) continue;

                    // Utility fees are charged to the PROPERTY, then split across its tenants, so
                    // this is computed once per building rather than once per household — which is
                    // both cheaper than the game's own per-household version and identical in result,
                    // because the property it re-resolves from PropertyRenter.m_Property is this
                    // building.
                    double feesPerRenter = hasFees ? PropertyFeesPerRenter(building, fees, renters.Length) : 0.0;

                    for (int r = 0; r < renters.Length; r++)
                    {
                        AddHousehold(renters[r].m_Renter, cityTally, districtTally, feesPerRenter);
                    }
                }
            }
            finally
            {
                buildings.Dispose();
            }
        }

        private int SubsampleStride(int buildingCount)
        {
            int cap = Calibration.MaxBuildingsPerCapture;
            if (cap <= 0 || buildingCount <= cap) return 1;

            int stride = buildingCount / cap;
            return stride < 2 ? 2 : stride;
        }

        /// <summary>
        /// What one tenant of <paramref name="building"/> pays in utility fees, in game currency.
        /// </summary>
        /// <remarks>
        /// <para>
        /// A transcription of <c>Game.UI.InGame.ResidentsSection.GetHouseholdEconomyData</c> — the
        /// method behind the "fees" row the game shows when a district is selected — kept deliberately
        /// line-for-line rather than improved. Electricity and water are billed on <i>fulfilled</i>
        /// consumption, not wanted: a property the grid never reached is not charged for the power it
        /// did not get, and using the wanted figure would invoice the player's brownouts back to their
        /// voters. Garbage is the exception and comes off the prefab's accumulation rate rather than
        /// off a consumer component, because rubbish is produced whether or not anyone collects it.
        /// </para>
        /// <para>
        /// Water is charged twice against one fee — fresh in, sewage out — which reads like a bug and
        /// is what the game does. <c>PlayerResource.Water</c> is the only fee either side pays.
        /// </para>
        /// <para>
        /// The divisor is the full <c>Renter</c> buffer length, companies included, again matching the
        /// game. A mixed-use block splits its bill across every tenant, so counting only the households
        /// would overstate what each one pays.
        /// </para>
        /// </remarks>
        private double PropertyFeesPerRenter(Entity building, DynamicBuffer<ServiceFee> fees, int renterCount)
        {
            double total = 0.0;

            ElectricityConsumer electricity;
            if (EntityManager.TryGetComponent(building, out electricity))
            {
                total += electricity.m_FulfilledConsumption *
                         ServiceFeeSystem.GetFee(PlayerResource.Electricity, fees);
            }

            WaterConsumer water;
            if (EntityManager.TryGetComponent(building, out water))
            {
                float waterFee = ServiceFeeSystem.GetFee(PlayerResource.Water, fees);
                total += water.m_FulfilledFresh * waterFee;
                total += water.m_FulfilledSewage * waterFee;
            }

            PrefabRef prefabRef;
            ConsumptionData consumption;
            if (EntityManager.TryGetComponent(building, out prefabRef) &&
                EntityManager.TryGetComponent(prefabRef.m_Prefab, out consumption))
            {
                total += consumption.m_GarbageAccumulation *
                         ServiceFeeSystem.GetFee(PlayerResource.Garbage, fees);
            }

            return renterCount > 0 ? total / renterCount : total;
        }

        private void AddHousehold(Entity household, DemographicTally cityTally,
                                  DemographicTally districtTally, double dailyFees)
        {
            Household householdData;
            if (!EntityManager.TryGetComponent(household, out householdData)) return;

            DynamicBuffer<HouseholdCitizen> members;
            if (!EntityManager.TryGetBuffer(household, true, out members)) return;

            double wealth = householdData.m_Resources;
            double? dailySalary = householdData.m_SalaryLastDay > 0 ? (double?)householdData.m_SalaryLastDay : null;

            double? rent = null;
            PropertyRenter propertyRenter;
            if (EntityManager.TryGetComponent(household, out propertyRenter) && propertyRenter.m_Rent > 0)
            {
                rent = propertyRenter.m_Rent;
            }

            // The other two lines of the game's own household budget, read from the component already
            // in hand — the same two fields ResidentsSection shows as "upkeep" and "resources" when a
            // district is selected. Levelling spend arrives signed (it is an outgoing) and is taken as
            // a magnitude, which is what the game's panel displays.
            double dailyUpkeep = householdData.m_MoneySpendOnBuildingLevelingLastDay < 0
                ? -(double)householdData.m_MoneySpendOnBuildingLevelingLastDay
                : householdData.m_MoneySpendOnBuildingLevelingLastDay;

            double dailyResourceSpend = householdData.m_ShoppedValuePerDay;

            cityTally.AddHousehold(wealth, rent, dailySalary, dailyUpkeep, dailyResourceSpend, dailyFees);
            if (districtTally != null)
            {
                districtTally.AddHousehold(wealth, rent, dailySalary, dailyUpkeep, dailyResourceSpend, dailyFees);
            }

            for (int i = 0; i < members.Length; i++)
            {
                AddCitizen(members[i].m_Citizen, cityTally, districtTally);
            }
        }

        private void AddCitizen(Entity citizenEntity, DemographicTally cityTally, DemographicTally districtTally)
        {
            Citizen citizen;
            if (!EntityManager.TryGetComponent(citizenEntity, out citizen)) return;

            AgeBand age = ToAgeBand(citizen.GetAge());
            EducationTier education = ToEducationTier(citizen.GetEducationLevel());
            double happiness = citizen.Happiness;

            bool isSick = EntityManager.HasComponent<HealthProblem>(citizenEntity);
            // Fully qualified: Game.Buildings.Student (a building's enrolled-student record) and
            // Game.Citizens.Student (this citizen is enrolled) are both in scope here. We want the
            // citizen one.
            bool isStudent = EntityManager.HasComponent<Game.Citizens.Student>(citizenEntity);

            // The unemployment denominator is working-age residents who are not full-time students.
            // Counting students as unemployed would report a university district as an economic
            // disaster, which is a political claim the sensor has no business making.
            bool isWorkable = (age == AgeBand.Adult || age == AgeBand.Elderly) && !isStudent;

            double? commuteMinutes = null;
            bool isEmployed = false;
            Worker worker;
            if (EntityManager.TryGetComponent(citizenEntity, out worker))
            {
                isEmployed = true;
                if (worker.m_LastCommuteTime > 0f)
                {
                    commuteMinutes = worker.m_LastCommuteTime * Calibration.CommuteTimeToMinutes;
                }
            }

            cityTally.AddCitizen(age, education, happiness, isSick, isWorkable, isEmployed, commuteMinutes);
            if (districtTally != null)
            {
                districtTally.AddCitizen(age, education, happiness, isSick, isWorkable, isEmployed, commuteMinutes);
            }
        }

        private void PublishCity(DemographicTally tally, WealthCuts cuts, SensorCalibration calibration)
        {
            _city.Population = (int)tally.Residents;
            _city.Households = (int)tally.Households;
            _city.Happiness = tally.MeanHappiness();
            _city.Unemployment = tally.Unemployment();
            _city.Wealth = tally.WealthShares(cuts);
            _city.Education = tally.EducationShares();
            _city.Age = tally.AgeShares();
            _city.SickRate = tally.SickRate();
            _city.AverageCommuteMinutes = tally.MeanCommuteMinutes();
            _city.AverageRent = tally.MeanRent();
            _city.RentBurden = tally.RentBurden(calibration.RentPeriodDays);
            _city.AverageHouseholdUpkeep = tally.MeanDailyUpkeep();
            _city.AverageHouseholdResourceSpend = tally.MeanDailyResourceSpend();
            _city.AverageHouseholdFees = tally.MeanDailyFees();
            _city.DisposableMargin = tally.DisposableMargin(calibration.RentPeriodDays);
        }

        private void PublishDistricts(IReadOnlyList<DistrictEntry> districts,
                                      Dictionary<Entity, DemographicTally> tallyByDistrict,
                                      WealthCuts cuts,
                                      SensorCalibration calibration)
        {
            _byDistrictId.Clear();

            int floor = calibration.MinDistrictPopulationForLocalValues;

            // Iterating the sorted district list, never the tally dictionary. Dictionary order is
            // unspecified, and building the snapshot from it would make the output depend on hashing.
            for (int i = 0; i < districts.Count; i++)
            {
                DistrictEntry entry = districts[i];

                DemographicTally tally;
                if (!tallyByDistrict.TryGetValue(entry.Entity, out tally)) continue;

                var reading = new DistrictReading { Id = entry.Id, Name = entry.Name };

                // Population and household counts are meaningful at any size — a district with three
                // residents genuinely has three. Averages are not: leaving them null hands them the
                // city figure and marks them as such, which is what §6 asks for.
                reading.Population = (int)tally.Residents;
                reading.Households = (int)tally.Households;

                if (tally.Residents >= floor)
                {
                    reading.Happiness = tally.MeanHappiness();
                    reading.Unemployment = tally.Unemployment();
                    reading.Education = tally.EducationShares();
                    reading.Age = tally.AgeShares();
                    reading.SickRate = tally.SickRate();
                    reading.AverageCommuteMinutes = tally.MeanCommuteMinutes();
                }

                if (tally.Households >= 1 && tally.Residents >= floor)
                {
                    reading.Wealth = tally.WealthShares(cuts);
                    reading.AverageRent = tally.MeanRent();
                    reading.RentBurden = tally.RentBurden(calibration.RentPeriodDays);
                    reading.AverageHouseholdUpkeep = tally.MeanDailyUpkeep();
                    reading.AverageHouseholdResourceSpend = tally.MeanDailyResourceSpend();
                    reading.AverageHouseholdFees = tally.MeanDailyFees();
                    reading.DisposableMargin = tally.DisposableMargin(calibration.RentPeriodDays);
                }

                _byDistrictId[entry.Id] = reading;
            }
        }

        /// <summary>
        /// Maps the game's <c>CitizenAge</c> onto the contract's <c>AgeBand</c>. Written as a switch
        /// rather than a cast: the two enums agree today, and a switch is what turns a future
        /// divergence into a compile-time or clearly-wrong-value problem instead of a silent
        /// off-by-one across every bloc in the city.
        /// </summary>
        private static AgeBand ToAgeBand(CitizenAge age)
        {
            switch (age)
            {
                case CitizenAge.Child: return AgeBand.Child;
                case CitizenAge.Teen: return AgeBand.Teen;
                case CitizenAge.Adult: return AgeBand.Adult;
                case CitizenAge.Elderly: return AgeBand.Elderly;
                default: return AgeBand.Adult;
            }
        }

        /// <summary>
        /// Maps <c>Citizen.GetEducationLevel()</c> (0–4) onto <c>EducationTier</c>. Out-of-range
        /// levels clamp to the nearest tier rather than throwing.
        /// </summary>
        private static EducationTier ToEducationTier(int level)
        {
            switch (level)
            {
                case 0: return EducationTier.Uneducated;
                case 1: return EducationTier.PoorlyEducated;
                case 2: return EducationTier.Educated;
                case 3: return EducationTier.WellEducated;
                case 4: return EducationTier.HighlyEducated;
                default: return level < 0 ? EducationTier.Uneducated : EducationTier.HighlyEducated;
            }
        }
    }
}
