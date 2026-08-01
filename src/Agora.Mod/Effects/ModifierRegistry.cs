using System;
using System.Collections.Generic;
using Agora.Core.Contracts;
using Agora.Core.Engine.Effects;
using Agora.Core.Tuning;
using Game.Areas;
using Game.City;
using Game.Prefabs;

namespace Agora.Mod.Effects
{
    /// <summary>
    /// One resolved mapping from a palette entry's <see cref="EffectCap.Modifier"/> string onto a real
    /// member of <see cref="DistrictModifierType"/> or <see cref="CityModifierType"/>.
    /// </summary>
    /// <remarks>
    /// <see cref="TypeIndex"/> is the enum's numeric value, which is also the index into the
    /// <c>DistrictModifier</c> / <c>CityModifier</c> dynamic buffer — verified from
    /// <c>Game.City.CityUtils.ApplyModifier</c> and <c>Game.Areas.AreaUtils.ApplyModifier</c>, both of
    /// which do <c>modifiers[(int)type]</c>.
    /// </remarks>
    public readonly struct ModifierBinding
    {
        public EffectScope Scope { get; }

        /// <summary>The enum member name, exactly as spelled in <c>effects.perEffect[].modifier</c>.</summary>
        public string ModifierName { get; }

        /// <summary>Numeric enum value, and therefore the modifier buffer index.</summary>
        public int TypeIndex { get; }

        public ModifierValueMode Mode { get; }

        public ModifierBinding(EffectScope scope, string modifierName, int typeIndex, ModifierValueMode mode)
        {
            Scope = scope;
            ModifierName = modifierName ?? "";
            TypeIndex = typeIndex;
            Mode = mode;
        }

        public bool IsValid
        {
            get { return !string.IsNullOrEmpty(ModifierName) && TypeIndex >= 0; }
        }

        public DistrictModifierType DistrictType
        {
            get { return (DistrictModifierType)TypeIndex; }
        }

        public CityModifierType CityType
        {
            get { return (CityModifierType)TypeIndex; }
        }

        /// <summary>
        /// Turns a signed Agora magnitude into the game's <c>float2</c> delta shape.
        /// </summary>
        /// <remarks>
        /// <see cref="ModifierValueMode.Absolute"/> feeds the <c>x</c> lane and
        /// <see cref="ModifierValueMode.Relative"/> the <c>y</c> lane, matching
        /// <c>value += delta.x; value += value * delta.y;</c>.
        /// <see cref="ModifierValueMode.InverseRelative"/> is folded to relative here — the game only
        /// uses it to invert an authored slider, which Agora does not have.
        /// </remarks>
        public ModifierDelta ToDelta(double magnitude)
        {
            if (double.IsNaN(magnitude) || double.IsInfinity(magnitude)) return ModifierDelta.Zero;
            return Mode == ModifierValueMode.Absolute
                ? new ModifierDelta(magnitude, 0.0)
                : new ModifierDelta(0.0, magnitude);
        }
    }

    /// <summary>
    /// The name-to-enum table for the two game modifier enums, and the availability check Core's
    /// <see cref="EffectResolver"/> uses to decide whether an effect can actually land.
    ///
    /// <para>
    /// This table is <b>not</b> the palette. The palette (<c>effects.perEffect</c>, Core's
    /// <see cref="EffectPalette"/>) is the closed registry of what Agora is allowed to do; this is
    /// only the translation of the modifier names that registry already uses into enum values.
    /// An effect id that is not in the palette can never reach here, and a palette entry whose
    /// modifier name is not in this table is <b>reported and dropped, never invented</b> (§7).
    /// </para>
    /// </summary>
    /// <remarks>
    /// Every key is written as <c>nameof(...)</c> of the enum member itself, so a game update that
    /// renames a member breaks the build here rather than silently disabling an effect at runtime.
    /// Member lists verified with <c>tools\api-query.ps1 -Enum</c>: 14 district members, 40 city
    /// members (city value 2 is a removed member and has no name).
    /// </remarks>
    public static class ModifierRegistry
    {
        /// <summary>
        /// Every Agora effect is applied in <see cref="ModifierValueMode.Relative"/>.
        ///
        /// <para>
        /// This is a structural decision, not a tuning coefficient. Every shipped magnitude cap is a
        /// fraction (0.15–0.30), and the game consumes a relative delta as
        /// <c>value += value * delta.y</c> — so a magnitude of 0.2 means "20% more", uniformly, at
        /// both scopes and for every modifier. Absolute mode would mean wildly different things per
        /// modifier (0.2 is a rounding error on <c>Wellbeing</c>, which sits near 50, and a large
        /// change on a probability), which is exactly the kind of per-effect magic number §2 forbids.
        /// </para>
        /// </summary>
        public const ModifierValueMode AgoraValueMode = ModifierValueMode.Relative;

        private static readonly Dictionary<string, ModifierBinding> DistrictByName = BuildDistrict();
        private static readonly Dictionary<string, ModifierBinding> CityByName = BuildCity();
        private static readonly List<string> DistrictNamesSorted = SortedKeys(DistrictByName);
        private static readonly List<string> CityNamesSorted = SortedKeys(CityByName);

        /// <summary>District modifier member names, sorted ordinal ascending.</summary>
        public static IReadOnlyList<string> DistrictModifierNames
        {
            get { return DistrictNamesSorted; }
        }

        /// <summary>City modifier member names, sorted ordinal ascending.</summary>
        public static IReadOnlyList<string> CityModifierNames
        {
            get { return CityNamesSorted; }
        }

        public static IReadOnlyList<string> NamesForScope(EffectScope scope)
        {
            return scope == EffectScope.District ? DistrictNamesSorted : (IReadOnlyList<string>)CityNamesSorted;
        }

        /// <summary>Resolves a modifier member name within a scope. False means "no such member".</summary>
        public static bool TryResolve(EffectScope scope, string modifierName, out ModifierBinding binding)
        {
            binding = default(ModifierBinding);
            if (string.IsNullOrEmpty(modifierName)) return false;

            Dictionary<string, ModifierBinding> table =
                scope == EffectScope.District ? DistrictByName : CityByName;

            return table.TryGetValue(modifierName, out binding);
        }

        /// <summary>Resolves the modifier a palette entry drives, given the palette entry's own scope.</summary>
        public static bool TryResolveEffect(EffectPalette palette, string effectId, out ModifierBinding binding)
        {
            binding = default(ModifierBinding);
            if (palette == null) return false;

            EffectCap cap;
            if (!palette.TryGetCap(effectId, out cap)) return false;

            return TryResolve(cap.Scope, cap.Modifier, out binding);
        }

        /// <summary>
        /// The <see cref="EffectAvailabilityCheck"/> to hand Core's resolver. An effect whose modifier
        /// this build cannot resolve is unavailable, so Core degrades it down its declared fallback
        /// chain instead of dropping the event (§13.5).
        /// </summary>
        public static EffectAvailabilityCheck AvailabilityFor(EffectPalette palette)
        {
            if (palette == null) throw new ArgumentNullException("palette");

            return delegate (string effectId)
            {
                ModifierBinding binding;
                return TryResolveEffect(palette, effectId, out binding);
            };
        }

        /// <summary>
        /// Every palette entry whose modifier name does not resolve, sorted ordinal ascending. Empty
        /// for the shipped palette; anything here is a data bug worth logging at load.
        /// </summary>
        public static IReadOnlyList<string> UnmappedPaletteEntries(EffectPalette palette)
        {
            var unmapped = new List<string>();
            if (palette == null) return unmapped;

            IReadOnlyList<string> ids = palette.Ids; // already sorted ordinal ascending
            for (int i = 0; i < ids.Count; i++)
            {
                ModifierBinding binding;
                if (!TryResolveEffect(palette, ids[i], out binding)) unmapped.Add(ids[i]);
            }
            return unmapped;
        }

        private static List<string> SortedKeys(Dictionary<string, ModifierBinding> table)
        {
            var keys = new List<string>(table.Count);
            foreach (KeyValuePair<string, ModifierBinding> pair in table) keys.Add(pair.Key);
            keys.Sort(StringComparer.Ordinal);
            return keys;
        }

        private static void AddDistrict(Dictionary<string, ModifierBinding> table,
                                        string name, DistrictModifierType type)
        {
            table[name] = new ModifierBinding(EffectScope.District, name, (int)type, AgoraValueMode);
        }

        private static void AddCity(Dictionary<string, ModifierBinding> table,
                                    string name, CityModifierType type)
        {
            table[name] = new ModifierBinding(EffectScope.City, name, (int)type, AgoraValueMode);
        }

        // --- Game.Areas.DistrictModifierType — 14 members, verified ------------------------------

        private static Dictionary<string, ModifierBinding> BuildDistrict()
        {
            var t = new Dictionary<string, ModifierBinding>(StringComparer.Ordinal);

            AddDistrict(t, nameof(DistrictModifierType.GarbageProduction), DistrictModifierType.GarbageProduction);
            AddDistrict(t, nameof(DistrictModifierType.ProductConsumption), DistrictModifierType.ProductConsumption);
            AddDistrict(t, nameof(DistrictModifierType.ParkingFee), DistrictModifierType.ParkingFee);
            AddDistrict(t, nameof(DistrictModifierType.BuildingFireHazard), DistrictModifierType.BuildingFireHazard);
            AddDistrict(t, nameof(DistrictModifierType.BuildingFireResponseTime), DistrictModifierType.BuildingFireResponseTime);
            AddDistrict(t, nameof(DistrictModifierType.BuildingUpkeep), DistrictModifierType.BuildingUpkeep);
            AddDistrict(t, nameof(DistrictModifierType.LowCommercialTax), DistrictModifierType.LowCommercialTax);
            AddDistrict(t, nameof(DistrictModifierType.Wellbeing), DistrictModifierType.Wellbeing);
            AddDistrict(t, nameof(DistrictModifierType.CrimeAccumulation), DistrictModifierType.CrimeAccumulation);
            AddDistrict(t, nameof(DistrictModifierType.StreetSpeedLimit), DistrictModifierType.StreetSpeedLimit);
            AddDistrict(t, nameof(DistrictModifierType.StreetTrafficSafety), DistrictModifierType.StreetTrafficSafety);
            AddDistrict(t, nameof(DistrictModifierType.EnergyConsumptionAwareness), DistrictModifierType.EnergyConsumptionAwareness);
            AddDistrict(t, nameof(DistrictModifierType.CarReserveProbability), DistrictModifierType.CarReserveProbability);
            AddDistrict(t, nameof(DistrictModifierType.BikeProbability), DistrictModifierType.BikeProbability);

            return t;
        }

        // --- Game.City.CityModifierType — 40 members, verified (value 2 is removed) ---------------

        private static Dictionary<string, ModifierBinding> BuildCity()
        {
            var t = new Dictionary<string, ModifierBinding>(StringComparer.Ordinal);

            AddCity(t, nameof(CityModifierType.Attractiveness), CityModifierType.Attractiveness);
            AddCity(t, nameof(CityModifierType.CrimeAccumulation), CityModifierType.CrimeAccumulation);
            AddCity(t, nameof(CityModifierType.DisasterWarningTime), CityModifierType.DisasterWarningTime);
            AddCity(t, nameof(CityModifierType.DisasterDamageRate), CityModifierType.DisasterDamageRate);
            AddCity(t, nameof(CityModifierType.DiseaseProbability), CityModifierType.DiseaseProbability);
            AddCity(t, nameof(CityModifierType.ParkEntertainment), CityModifierType.ParkEntertainment);
            AddCity(t, nameof(CityModifierType.CriminalMonitorProbability), CityModifierType.CriminalMonitorProbability);
            AddCity(t, nameof(CityModifierType.IndustrialAirPollution), CityModifierType.IndustrialAirPollution);
            AddCity(t, nameof(CityModifierType.IndustrialGroundPollution), CityModifierType.IndustrialGroundPollution);
            AddCity(t, nameof(CityModifierType.IndustrialGarbage), CityModifierType.IndustrialGarbage);
            AddCity(t, nameof(CityModifierType.RecoveryFailChange), CityModifierType.RecoveryFailChange);
            AddCity(t, nameof(CityModifierType.OreResourceAmount), CityModifierType.OreResourceAmount);
            AddCity(t, nameof(CityModifierType.OilResourceAmount), CityModifierType.OilResourceAmount);
            AddCity(t, nameof(CityModifierType.UniversityInterest), CityModifierType.UniversityInterest);
            AddCity(t, nameof(CityModifierType.OfficeSoftwareDemand), CityModifierType.OfficeSoftwareDemand);
            AddCity(t, nameof(CityModifierType.IndustrialElectronicsDemand), CityModifierType.IndustrialElectronicsDemand);
            AddCity(t, nameof(CityModifierType.OfficeSoftwareEfficiency), CityModifierType.OfficeSoftwareEfficiency);
            AddCity(t, nameof(CityModifierType.IndustrialElectronicsEfficiency), CityModifierType.IndustrialElectronicsEfficiency);
            AddCity(t, nameof(CityModifierType.TelecomCapacity), CityModifierType.TelecomCapacity);
            AddCity(t, nameof(CityModifierType.Entertainment), CityModifierType.Entertainment);
            AddCity(t, nameof(CityModifierType.HighwayTrafficSafety), CityModifierType.HighwayTrafficSafety);
            AddCity(t, nameof(CityModifierType.PrisonTime), CityModifierType.PrisonTime);
            AddCity(t, nameof(CityModifierType.CrimeProbability), CityModifierType.CrimeProbability);
            AddCity(t, nameof(CityModifierType.CollegeGraduation), CityModifierType.CollegeGraduation);
            AddCity(t, nameof(CityModifierType.UniversityGraduation), CityModifierType.UniversityGraduation);
            AddCity(t, nameof(CityModifierType.ImportCost), CityModifierType.ImportCost);
            AddCity(t, nameof(CityModifierType.LoanInterest), CityModifierType.LoanInterest);
            AddCity(t, nameof(CityModifierType.BuildingLevelingCost), CityModifierType.BuildingLevelingCost);
            AddCity(t, nameof(CityModifierType.ExportCost), CityModifierType.ExportCost);
            AddCity(t, nameof(CityModifierType.TaxiStartingFee), CityModifierType.TaxiStartingFee);
            AddCity(t, nameof(CityModifierType.IndustrialEfficiency), CityModifierType.IndustrialEfficiency);
            AddCity(t, nameof(CityModifierType.OfficeEfficiency), CityModifierType.OfficeEfficiency);
            AddCity(t, nameof(CityModifierType.PollutionHealthAffect), CityModifierType.PollutionHealthAffect);
            AddCity(t, nameof(CityModifierType.HospitalEfficiency), CityModifierType.HospitalEfficiency);
            AddCity(t, nameof(CityModifierType.IndustrialFishInputEfficiency), CityModifierType.IndustrialFishInputEfficiency);
            AddCity(t, nameof(CityModifierType.IndustrialFishHubEfficiency), CityModifierType.IndustrialFishHubEfficiency);
            AddCity(t, nameof(CityModifierType.CityServiceImportCost), CityModifierType.CityServiceImportCost);
            AddCity(t, nameof(CityModifierType.CityServiceBuildingBaseUpkeepCost), CityModifierType.CityServiceBuildingBaseUpkeepCost);
            AddCity(t, nameof(CityModifierType.CrimeResponseTime), CityModifierType.CrimeResponseTime);
            AddCity(t, nameof(CityModifierType.TaxHappiness), CityModifierType.TaxHappiness);

            return t;
        }

        // AGORA-SEAM(§7 effect-palette gap): there is deliberately no entry here for rent, land value,
        // RCI demand, birth rate, subsidies/fines or pollution decay. No member of either enum backs
        // them, and the Harmony decision is unresolved. Do NOT synthesise one — a request naming a
        // missing capability degrades down its fallback chain to district-wellbeing / city-tax-happiness.
    }
}
