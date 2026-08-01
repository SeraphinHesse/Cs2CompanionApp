using Agora.Core.Tuning;

namespace Agora.Mod.Sensors
{
    /// <summary>
    /// Holds the <see cref="EngineTuning"/> the sensors read. Exactly one coefficient is needed —
    /// <c>blocs.wealthTierThresholds</c>, the quantile cuts that split households into the three
    /// wealth tiers — and it is read from tuning rather than hardcoded so the sensor and the bloc
    /// model can never disagree about where "middle income" starts.
    ///
    /// <para>
    /// Defaults to <see cref="EngineTuning.Default"/>, which is identical to the shipped file, so a
    /// sensor works before anything has loaded <c>data/engine_tuning.json</c>. Whichever packet owns
    /// that load assigns <see cref="Active"/> during mod load; see the packet report for the exact
    /// line.
    /// </para>
    /// </summary>
    public static class SensorTuning
    {
        private static EngineTuning _active = EngineTuning.Default;

        /// <summary>
        /// The tuning in force. Assigning null restores the built-in defaults rather than leaving
        /// the sensors holding a null they would have to null-check on every read.
        /// </summary>
        public static EngineTuning Active
        {
            get { return _active; }
            set { _active = value ?? EngineTuning.Default; }
        }
    }
}
