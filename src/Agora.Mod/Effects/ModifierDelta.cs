// Compiled into BOTH Agora.Mod and (by <Compile Link>) tests/Agora.Core.Tests: it must stay free
// of every Game.*, Unity.* and Colossal.* type. #nullable disable keeps it warning-clean in the
// test project, which enables nullable, without annotating a file the mod compiles unannotated.
#nullable disable

using System;

namespace Agora.Mod.Effects
{
    /// <summary>
    /// The two lanes of a game modifier delta, in <c>double</c>. Mirrors the <c>float2 m_Delta</c> on
    /// <c>Game.Areas.DistrictModifier</c> and <c>Game.City.CityModifier</c>.
    ///
    /// <para>
    /// The game consumes a slot as <c>value += delta.x; value += value * delta.y;</c> — verified in
    /// <c>Game.City.CityUtils.ApplyModifier</c> and <c>Game.Areas.AreaUtils.ApplyModifier</c>. So
    /// <see cref="Absolute"/> is the <c>x</c> lane and <see cref="Relative"/> the <c>y</c> lane.
    /// </para>
    /// </summary>
    /// <remarks>
    /// Deliberately free of every <c>Game.*</c>, <c>Unity.*</c> and <c>Colossal.*</c> type: this is
    /// the arithmetic that decides how much of the city Agora moves, and it should be checkable
    /// without a copy of the game. See the note in <see cref="ModifierReconciler"/>.
    /// </remarks>
    public readonly struct ModifierDelta : IEquatable<ModifierDelta>
    {
        /// <summary>Below this, a composed relative lane is treated as a total wipe rather than inverted.</summary>
        private const double MinRelativeDenominator = 1e-3;

        public static readonly ModifierDelta Zero = new ModifierDelta(0.0, 0.0);

        public double Absolute { get; }
        public double Relative { get; }

        public ModifierDelta(double absolute, double relative)
        {
            Absolute = Finite(absolute);
            Relative = Finite(relative);
        }

        public bool IsZero
        {
            get { return Absolute == 0.0 && Relative == 0.0; }
        }

        /// <summary>The larger of the two lanes in absolute value. Used for noise-floor tests.</summary>
        public double Magnitude
        {
            get
            {
                double a = Math.Abs(Absolute);
                double r = Math.Abs(Relative);
                return a > r ? a : r;
            }
        }

        /// <summary>
        /// Adds one source on top of another exactly the way the game's own policy layer does
        /// (<c>DistrictModifierInitializeSystem.AddModifier</c>): absolute lanes sum, relative lanes
        /// compose multiplicatively as <c>y = y * (1 + d) + d</c>.
        /// </summary>
        public static ModifierDelta Compose(ModifierDelta baseline, ModifierDelta addition)
        {
            return new ModifierDelta(
                baseline.Absolute + addition.Absolute,
                baseline.Relative * (1.0 + addition.Relative) + addition.Relative);
        }

        /// <summary>
        /// The inverse of <see cref="Compose"/>: recovers the baseline that <paramref name="addition"/>
        /// was composed onto. False when the addition is too close to <c>-1</c> to invert, in which
        /// case the caller must fall back to treating the composed value as the baseline.
        /// </summary>
        public static bool TryDecompose(ModifierDelta composed, ModifierDelta addition, out ModifierDelta baseline)
        {
            double denominator = 1.0 + addition.Relative;
            if (Math.Abs(denominator) < MinRelativeDenominator)
            {
                baseline = composed;
                return false;
            }

            baseline = new ModifierDelta(
                composed.Absolute - addition.Absolute,
                (composed.Relative - addition.Relative) / denominator);
            return true;
        }

        /// <summary>Applies this delta to a value the way the simulation would. For diagnostics and tests.</summary>
        public double Apply(double value)
        {
            double result = value + Absolute;
            return result + (result * Relative);
        }

        /// <summary>Rounds both lanes through <c>float</c>, which is what the buffer actually stores.</summary>
        public ModifierDelta ToSinglePrecision()
        {
            return new ModifierDelta((float)Absolute, (float)Relative);
        }

        /// <summary>Clamps both lanes into <c>[-limit, +limit]</c>. The last cap before the city sees it.</summary>
        public ModifierDelta Clamped(double limit)
        {
            double bound = Math.Abs(limit);
            if (double.IsNaN(bound)) bound = 0.0;
            return new ModifierDelta(ClampOne(Absolute, bound), ClampOne(Relative, bound));
        }

        public bool Equals(ModifierDelta other)
        {
            return Absolute == other.Absolute && Relative == other.Relative;
        }

        public override bool Equals(object obj)
        {
            return obj is ModifierDelta && Equals((ModifierDelta)obj);
        }

        public override int GetHashCode()
        {
            return (Absolute.GetHashCode() * 397) ^ Relative.GetHashCode();
        }

        public override string ToString()
        {
            return "abs=" + Absolute.ToString("0.######", System.Globalization.CultureInfo.InvariantCulture)
                 + " rel=" + Relative.ToString("0.######", System.Globalization.CultureInfo.InvariantCulture);
        }

        private static double ClampOne(double value, double bound)
        {
            if (double.IsNaN(value)) return 0.0;
            if (value > bound) return bound;
            return value < -bound ? -bound : value;
        }

        private static double Finite(double value)
        {
            return double.IsNaN(value) || double.IsInfinity(value) ? 0.0 : value;
        }
    }

    /// <summary>
    /// Works out what to write into a modifier slot so that Agora's contribution is present exactly
    /// once, whatever else has happened to the slot since the last write.
    ///
    /// <para>
    /// This matters because the game <b>rebuilds these buffers from scratch</b>. Both
    /// <c>DistrictModifierInitializeSystem.RefreshDistrictModifiers</c> and
    /// <c>CityModifierUpdateSystem.RefreshCityModifiers</c> begin with <c>modifiers.Clear()</c> and
    /// re-derive every lane from the active policy list — and <c>CityModifierUpdateSystem</c> does it
    /// unconditionally every 256 simulation ticks. A naive "add our delta once" would be erased
    /// within a few in-game days; a naive "add our delta every pass" would compound without bound.
    /// </para>
    /// </summary>
    /// <remarks>
    /// Pure and game-free on purpose. Whether multiple modifier sources stack additively or
    /// multiplicatively was Scout 0002's open question 7; the answer, from the two <c>AddModifier</c>
    /// bodies, is <i>both</i> — absolute lanes sum, relative lanes compose. <see cref="ModifierDelta.Compose"/>
    /// reproduces that, so Agora stacks on top of the player's policies the same way a policy would.
    /// </remarks>
    public static class ModifierReconciler
    {
        /// <summary>
        /// What the slot would read if Agora were not contributing to it.
        /// </summary>
        /// <param name="current">What is in the slot right now.</param>
        /// <param name="currentIsOurLastWrite">
        /// True when <paramref name="current"/> is bit-identical to what we last stored there. Then
        /// the baseline is simply the one we remembered — no inverse arithmetic, so repeated passes
        /// cannot drift.
        /// </param>
        /// <param name="rememberedBaseline">The baseline recorded alongside that write.</param>
        /// <param name="mayAlreadyContain">
        /// True when the slot is untracked but Agora is known to have been contributing to it before
        /// this session — the reload case. <c>DistrictModifier</c> and <c>CityModifier</c> are
        /// <c>ISerializable</c> and travel with the save, and only the city buffer is rebuilt on a
        /// timer, so a district slot can come back from disk still carrying our last contribution.
        /// Without this the contribution would be composed on top of itself once per load.
        /// </param>
        /// <param name="previousContribution">Best estimate of what we contributed before the reload.</param>
        public static ModifierDelta BaselineFor(ModifierDelta current, bool currentIsOurLastWrite,
                                                ModifierDelta rememberedBaseline,
                                                bool mayAlreadyContain, ModifierDelta previousContribution)
        {
            if (currentIsOurLastWrite) return rememberedBaseline;
            if (!mayAlreadyContain || previousContribution.IsZero) return current;

            ModifierDelta baseline;
            return ModifierDelta.TryDecompose(current, previousContribution, out baseline) ? baseline : current;
        }

        /// <summary>The value to write: the non-Agora baseline with this pass's contribution on top.</summary>
        public static ModifierDelta Reconcile(ModifierDelta current, bool currentIsOurLastWrite,
                                              ModifierDelta rememberedBaseline,
                                              bool mayAlreadyContain, ModifierDelta previousContribution,
                                              ModifierDelta desired)
        {
            ModifierDelta baseline = BaselineFor(current, currentIsOurLastWrite, rememberedBaseline,
                                                 mayAlreadyContain, previousContribution);
            return ModifierDelta.Compose(baseline, desired);
        }
    }
}
