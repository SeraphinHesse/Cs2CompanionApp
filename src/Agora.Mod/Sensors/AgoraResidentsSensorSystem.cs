using System.Collections.Generic;
using Agora.Core.Contracts;
using Colossal.Entities;
using Game.Areas;
using Game.Buildings;
using Game.Citizens;
using Game.Common;
using Game.Tools;
using Unity.Collections;
using Unity.Entities;

namespace Agora.Mod.Sensors
{
    /// <summary>
    /// The residents sensor: everything measured per person or per household — population,
    /// households, age, education, wealth, happiness, sickness, employment, commute, rent and rent
    /// burden — for the city and for each district.
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

                    for (int r = 0; r < renters.Length; r++)
                    {
                        AddHousehold(renters[r].m_Renter, cityTally, districtTally);
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

        private void AddHousehold(Entity household, DemographicTally cityTally, DemographicTally districtTally)
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

            cityTally.AddHousehold(wealth, rent, dailySalary);
            if (districtTally != null) districtTally.AddHousehold(wealth, rent, dailySalary);

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
