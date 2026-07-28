using System.Collections.Generic;

namespace Agora.Core.Contracts
{
    /// <summary>
    /// The measured state of the city at one moment — the engine's only view of the game.
    ///
    /// <para>
    /// M0 carries the minimum needed to prove the pipeline. Sensor passes in M1+ widen it; every
    /// widening bumps <see cref="SchemaVersion"/> and runs through <c>/schema-change</c>, because
    /// this type is mirrored by both the JSON contract in <c>data/schemas/</c> and the LLM prompt.
    /// </para>
    ///
    /// <para>
    /// Per-district fields are best-effort by design: Scout 0001 flags that several metrics may only
    /// exist city-wide. A sensor that cannot resolve a district value falls back to the city value
    /// and marks it, rather than throwing.
    /// </para>
    /// </summary>
    public sealed class CitySnapshot
    {
        public int SchemaVersion { get; set; } = 1;

        public SimDate Date { get; set; }

        public int Population { get; set; }

        /// <summary>0–100.</summary>
        public double Happiness { get; set; }

        /// <summary>0–1.</summary>
        public double Unemployment { get; set; }

        public long Money { get; set; }

        /// <summary>
        /// Ordered by <see cref="DistrictSnapshot.Id"/>. Order is part of the contract: the engine
        /// iterates this list, and an unstable order would make results depend on ECS chunk layout.
        /// </summary>
        public List<DistrictSnapshot> Districts { get; set; } = new List<DistrictSnapshot>();
    }

    /// <summary>One district's measured state. Districts are real ECS entities (Scout 0001 §2).</summary>
    public sealed class DistrictSnapshot
    {
        /// <summary>Stable identifier, used as the entity id in seeded sub-streams.</summary>
        public string Id { get; set; } = "";

        /// <summary>The player's name for the district, shown in the dashboard.</summary>
        public string Name { get; set; } = "";

        public int Population { get; set; }

        /// <summary>0–100.</summary>
        public double Happiness { get; set; }

        /// <summary>
        /// True when one or more fields on this district fell back to a city-wide value because the
        /// per-district metric was unavailable. The dashboard should not present these as local facts.
        /// </summary>
        public bool HasCityFallbacks { get; set; }
    }
}
