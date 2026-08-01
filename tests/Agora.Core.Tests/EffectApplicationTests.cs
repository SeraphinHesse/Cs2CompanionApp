// REQUIRES A CSPROJ CHANGE. This file exercises Agora.Mod's effect-application layer, which cannot
// be project-referenced because Agora.Mod needs the game installed. The two files under test are
// deliberately free of every Game.*, Unity.* and Colossal.* type, so they are compiled in by link:
//
//   <ItemGroup>
//     <Compile Include="..\..\src\Agora.Mod\Effects\ModifierDelta.cs" Link="ModEffects\ModifierDelta.cs" />
//     <Compile Include="..\..\src\Agora.Mod\Effects\EffectLedger.cs"  Link="ModEffects\EffectLedger.cs" />
//   </ItemGroup>
//
// Without those two lines this file does not compile. The suite still runs on a machine with no copy
// of Cities: Skylines II, which is the property tests/CLAUDE.md actually protects.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Agora.Core.Contracts;
using Agora.Core.Engine.Effects;
using Agora.Core.Tuning;
using Agora.Mod.Effects;
using Xunit;

namespace Agora.Core.Tests
{
    /// <summary>
    /// Packet 22 — the effect application boundary (<c>politicsmodplan.md</c> §7).
    ///
    /// <para>
    /// Three things are defended here. First, the <b>caps hold at the sink</b>: every palette entry is
    /// driven far past its magnitude and duration cap, in both directions, and asserted to clamp — Core
    /// clamping upstream is not the thing being tested, this is the layer that touches the city
    /// (non-negotiable #5). Second, an effect whose modifier has no game mapping is <b>dropped, never
    /// substituted</b>. Third, the reconciler that re-asserts Agora's contribution against a buffer the
    /// game rebuilds from scratch neither compounds nor drifts, however many passes run.
    /// </para>
    /// </summary>
    public class EffectApplicationTests
    {
        private static readonly SimDate Jan1990 = new SimDate(1990, 1, 1);

        private static EffectPalette Palette() => EffectPalette.From(EngineTuning.Default);

        private static EffectLedger Ledger(ModifierMappingCheck? mapping = null) =>
            new EffectLedger(Palette(), mapping);

        private static EffectRequest Request(EffectPalette palette, string effectId, double magnitude,
                                             int durationMonths, string? sourceId = "test",
                                             string? districtId = "district-a")
        {
            EffectScope scope;
            if (!palette.TryGetScope(effectId, out scope)) scope = EffectScope.City;
            return new EffectRequest(effectId, scope, magnitude, durationMonths,
                                     scope == EffectScope.District ? districtId : null, sourceId);
        }

        public static IEnumerable<object[]> EveryEffectId()
        {
            foreach (string id in EngineTuning.Default.Effects.EffectIds) yield return new object[] { id };
        }

        private static ModifierAggregate Single(IReadOnlyList<ModifierAggregate> aggregates)
        {
            Assert.Single(aggregates);
            return aggregates[0];
        }

        private static string Describe(IReadOnlyList<ModifierAggregate> aggregates)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < aggregates.Count; i++)
            {
                ModifierAggregate a = aggregates[i];
                sb.Append(a.Scope).Append('|').Append(a.DistrictId).Append('|').Append(a.Modifier)
                  .Append('|').Append(a.Magnitude.ToString("R", CultureInfo.InvariantCulture))
                  .Append('|').Append(a.Contributors).Append('\n');
            }
            return sb.ToString();
        }

        // --- Caps, at the layer that actually touches the city ------------------------------------

        /// <summary>
        /// Drive every entry ten thousand times past its cap. A test that only exercises in-range
        /// values proves nothing (/add-effect, step 4).
        /// </summary>
        [Theory]
        [MemberData(nameof(EveryEffectId))]
        public void Cap_ClampsAMagnitudeDrivenFarPastTheCeiling(string effectId)
        {
            EffectPalette palette = Palette();
            EffectLedger ledger = new EffectLedger(palette);

            Assert.Equal(EffectAdmission.Accepted, ledger.Add(Request(palette, effectId, 9999.0, 6), Jan1990));

            EffectCap cap;
            Assert.True(palette.TryGetCap(effectId, out cap));
            double limit = palette.EffectiveMagnitudeCap(cap);

            ModifierAggregate applied = Single(ledger.Aggregate(Jan1990));
            Assert.True(Math.Abs(applied.Magnitude) <= limit + 1e-12,
                effectId + " applied " + applied.Magnitude + ", cap " + limit);
            Assert.Equal(limit, applied.Magnitude, 12);
        }

        [Theory]
        [MemberData(nameof(EveryEffectId))]
        public void Cap_ClampsANegativeMagnitudeDrivenFarPastTheFloor(string effectId)
        {
            EffectPalette palette = Palette();
            EffectLedger ledger = new EffectLedger(palette);

            Assert.Equal(EffectAdmission.Accepted, ledger.Add(Request(palette, effectId, -9999.0, 6), Jan1990));

            EffectCap cap;
            Assert.True(palette.TryGetCap(effectId, out cap));
            double limit = palette.EffectiveMagnitudeCap(cap);

            ModifierAggregate applied = Single(ledger.Aggregate(Jan1990));
            Assert.Equal(-limit, applied.Magnitude, 12);
        }

        /// <summary>A century-long request must stop at the entry's declared duration cap.</summary>
        [Theory]
        [MemberData(nameof(EveryEffectId))]
        public void Cap_ClampsADurationDrivenFarPastTheCeiling(string effectId)
        {
            EffectPalette palette = Palette();
            EffectLedger ledger = new EffectLedger(palette);

            // At full strength, so the linear decay tail stays above effects.minEffectiveMagnitude
            // right up to the deadline — otherwise this would assert the noise floor, not the cap.
            Assert.Equal(EffectAdmission.Accepted, ledger.Add(Request(palette, effectId, 9999.0, 99999), Jan1990));

            EffectCap cap;
            Assert.True(palette.TryGetCap(effectId, out cap));
            int months = palette.EffectiveDurationCapMonths(cap);

            Assert.NotEmpty(ledger.Aggregate(Jan1990.AddMonths(months - 1)));
            Assert.Empty(ledger.Aggregate(Jan1990.AddMonths(months)));
            Assert.Empty(ledger.Aggregate(Jan1990.AddMonths(months + 600)));
        }

        /// <summary>
        /// Six effects at full strength on one modifier must not add up to six times the cap. The
        /// game composes relative lanes multiplicatively, so an uncapped coalition of small effects is
        /// exactly the failure mode worth pinning.
        /// </summary>
        [Fact]
        public void Cap_StackedEffectsCannotExceedTheTightestCapInTheGroup()
        {
            EffectPalette palette = Palette();
            EffectLedger ledger = new EffectLedger(palette);

            for (int i = 0; i < 6; i++)
                ledger.Add(Request(palette, "city-attractiveness", 9999.0, 12, "source-" + i), Jan1990);

            EffectCap cap;
            Assert.True(palette.TryGetCap("city-attractiveness", out cap));

            ModifierAggregate applied = Single(ledger.Aggregate(Jan1990));
            Assert.Equal(palette.EffectiveMagnitudeCap(cap), applied.Magnitude, 12);
        }

        [Fact]
        public void Cap_NoMoreThanMaxStackedPerModifierContribute()
        {
            EffectPalette palette = Palette();
            EffectLedger ledger = new EffectLedger(palette);

            for (int i = 0; i < 9; i++)
                ledger.Add(Request(palette, "city-attractiveness", 0.01 * (i + 1), 12, "source-" + i), Jan1990);

            ModifierAggregate applied = Single(ledger.Aggregate(Jan1990));
            Assert.Equal(palette.Tuning.MaxStackedPerModifier, applied.Contributors);
        }

        /// <summary>
        /// Every shipped cap must stay well inside <c>(-1, +1)</c>, because a relative lane of exactly
        /// -1 zeroes the value it multiplies and cannot be divided back out.
        /// </summary>
        [Fact]
        public void Cap_EveryShippedMagnitudeIsInvertibleAsARelativeLane()
        {
            EffectPalette palette = Palette();
            foreach (string id in palette.Ids)
            {
                EffectCap cap;
                Assert.True(palette.TryGetCap(id, out cap));
                Assert.InRange(palette.EffectiveMagnitudeCap(cap), 0.0, 0.9);
            }
        }

        // --- Reported and dropped, never invented -------------------------------------------------

        [Fact]
        public void Drop_AnEffectWhoseModifierHasNoGameMappingIsNotSubstituted()
        {
            EffectPalette palette = Palette();
            // Stand in for a game update that removed CityModifierType.Attractiveness.
            ModifierMappingCheck mapping = (scope, modifier) =>
                !string.Equals(modifier, "Attractiveness", StringComparison.Ordinal);

            EffectLedger ledger = new EffectLedger(palette, mapping);

            Assert.Equal(EffectAdmission.NoModifierMapping,
                ledger.Add(Request(palette, "city-attractiveness", 0.1, 12), Jan1990));
            Assert.Equal(0, ledger.Count);
            Assert.Empty(ledger.Aggregate(Jan1990));

            // and nothing else was quietly redirected onto a different modifier
            Assert.Equal(EffectAdmission.Accepted,
                ledger.Add(Request(palette, "city-entertainment", 0.1, 12), Jan1990));
            Assert.Equal("Entertainment", Single(ledger.Aggregate(Jan1990)).Modifier);
        }

        [Fact]
        public void Drop_AnUnregisteredEffectIdNeverReachesTheCity()
        {
            EffectLedger ledger = Ledger();
            var request = new EffectRequest("city-rent-control", EffectScope.City, 0.2, 12, null, "e");

            Assert.Equal(EffectAdmission.UnknownEffectId, ledger.Add(request, Jan1990));
            Assert.Empty(ledger.Aggregate(Jan1990));
        }

        /// <summary>
        /// The registry decides scope, not the caller. A district-scoped effect arriving in
        /// city-scoped clothing — which is the only way to get one here, since the contract refuses to
        /// construct a district request with no district — must be refused, not applied city-wide.
        /// </summary>
        [Fact]
        public void Drop_ADistrictScopedEffectWithNoTargetIsRefusedRatherThanAppliedCityWide()
        {
            EffectPalette palette = Palette();
            EffectLedger ledger = new EffectLedger(palette);

            var targeted = new EffectRequest("district-wellbeing", EffectScope.District, 0.1, 12, "d", "e");
            Assert.Equal(EffectAdmission.Accepted, ledger.Add(targeted, Jan1990));

            var untargeted = new EffectRequest("district-wellbeing", EffectScope.City, 0.1, 12, null, "e2");
            Assert.Equal(EffectAdmission.MissingDistrictId, ledger.Add(untargeted, Jan1990));

            Assert.Equal(1, ledger.Count);
            Assert.Equal("d", Single(ledger.Aggregate(Jan1990)).DistrictId);
        }

        [Fact]
        public void Drop_MagnitudesBelowTheNoiseFloorAreNotApplied()
        {
            EffectPalette palette = Palette();
            EffectLedger ledger = new EffectLedger(palette);

            Assert.Equal(EffectAdmission.MagnitudeBelowMinimum,
                ledger.Add(Request(palette, "city-attractiveness", 1e-9, 12), Jan1990));
            Assert.Equal(EffectAdmission.NotFinite,
                ledger.Add(Request(palette, "city-attractiveness", double.NaN, 12), Jan1990));
            Assert.Equal(EffectAdmission.ZeroDuration,
                ledger.Add(Request(palette, "city-attractiveness", 0.1, 0), Jan1990));

            Assert.Empty(ledger.Aggregate(Jan1990));
        }

        // --- Determinism --------------------------------------------------------------------------

        [Fact]
        public void Determinism_TheSameHistoryTwiceProducesIdenticalAggregates()
        {
            Assert.Equal(Describe(Run().Aggregate(Jan1990.AddMonths(3))),
                         Describe(Run().Aggregate(Jan1990.AddMonths(3))));

            EffectLedger Run()
            {
                EffectPalette palette = Palette();
                EffectLedger ledger = new EffectLedger(palette);
                ledger.Add(Request(palette, "city-attractiveness", 0.1, 24, "a"), Jan1990);
                ledger.Add(Request(palette, "district-wellbeing", 0.08, 24, "b", "district-b"), Jan1990);
                ledger.Add(Request(palette, "district-wellbeing", 0.05, 24, "c", "district-a"), Jan1990.AddMonths(1));
                ledger.Add(Request(palette, "city-crime-accumulation", -0.2, 24, "d"), Jan1990.AddMonths(2));
                return ledger;
            }
        }

        /// <summary>
        /// The order the engine happened to hand requests over must not change what the city gets.
        /// Every group is sorted by a total key before the stack limit is applied.
        /// </summary>
        [Fact]
        public void Determinism_AdmissionOrderDoesNotChangeTheResult()
        {
            EffectPalette palette = Palette();
            double[] magnitudes = { 0.02, 0.04, 0.06, 0.08, 0.10 };

            EffectLedger forwards = new EffectLedger(palette);
            for (int i = 0; i < magnitudes.Length; i++)
                forwards.Add(Request(palette, "city-attractiveness", magnitudes[i], 12, "s" + i), Jan1990);

            EffectLedger backwards = new EffectLedger(palette);
            for (int i = magnitudes.Length - 1; i >= 0; i--)
                backwards.Add(Request(palette, "city-attractiveness", magnitudes[i], 12, "s" + i), Jan1990);

            Assert.Equal(Describe(forwards.Aggregate(Jan1990)), Describe(backwards.Aggregate(Jan1990)));
        }

        [Fact]
        public void Ledger_ReissuingTheSameEffectRefreshesRatherThanStacks()
        {
            EffectPalette palette = Palette();
            EffectLedger ledger = new EffectLedger(palette);

            ledger.Add(Request(palette, "city-attractiveness", 0.05, 12, "storm-1994"), Jan1990);
            ledger.Add(Request(palette, "city-attractiveness", 0.05, 12, "storm-1994"), Jan1990.AddMonths(1));

            Assert.Equal(1, ledger.Count);
            Assert.Equal(1, Single(ledger.Aggregate(Jan1990.AddMonths(1))).Contributors);
        }

        [Fact]
        public void Ledger_PruneRemovesExpiredEntriesAndNothingElse()
        {
            EffectPalette palette = Palette();
            EffectLedger ledger = new EffectLedger(palette);

            ledger.Add(Request(palette, "city-attractiveness", 0.1, 6, "short"), Jan1990);
            ledger.Add(Request(palette, "city-entertainment", 0.1, 24, "long"), Jan1990);

            Assert.Equal(0, ledger.PruneExpired(Jan1990.AddMonths(5)));
            Assert.Equal(2, ledger.Count);

            Assert.Equal(1, ledger.PruneExpired(Jan1990.AddMonths(6)));
            Assert.Equal(1, ledger.Count);
        }

        [Fact]
        public void Ledger_LinearDecayShrinksTheAggregateToNothing()
        {
            EffectPalette palette = Palette();
            EffectLedger ledger = new EffectLedger(palette);
            ledger.Add(Request(palette, "city-attractiveness", 0.2, 12, "e"), Jan1990);

            double atStart = Single(ledger.Aggregate(Jan1990)).Magnitude;
            double halfway = Single(ledger.Aggregate(Jan1990.AddMonths(6))).Magnitude;

            Assert.Equal(0.2, atStart, 12);
            Assert.Equal(0.1, halfway, 12);
            Assert.Empty(ledger.Aggregate(Jan1990.AddMonths(12)));
        }

        [Fact]
        public void Ledger_CarriedOverIsFalseOnlyInTheMonthAnEffectStarts()
        {
            EffectPalette palette = Palette();
            EffectLedger ledger = new EffectLedger(palette);
            ledger.Add(Request(palette, "city-attractiveness", 0.1, 24, "e"), Jan1990);

            Assert.False(Single(ledger.Aggregate(Jan1990)).IsCarriedOver);
            Assert.True(Single(ledger.Aggregate(Jan1990.AddMonths(1))).IsCarriedOver);
        }

        // --- The reconciler: the arithmetic that reaches the buffer -------------------------------

        /// <summary>
        /// Composition must match <c>DistrictModifierInitializeSystem.AddModifier</c> and consumption
        /// must match <c>CityUtils.ApplyModifier</c>. If the game changes either, this fails loudly
        /// rather than Agora quietly applying the wrong amount.
        /// </summary>
        [Fact]
        public void Reconciler_ComposesAndAppliesExactlyAsTheGameDoes()
        {
            var policy = new ModifierDelta(2.0, 0.10);
            var agora = new ModifierDelta(0.0, 0.15);

            ModifierDelta composed = ModifierDelta.Compose(policy, agora);
            Assert.Equal(2.0, composed.Absolute, 12);
            Assert.Equal((0.10 * 1.15) + 0.15, composed.Relative, 12);

            // value += delta.x; value += value * delta.y
            double expected = (50.0 + composed.Absolute) * (1.0 + composed.Relative);
            Assert.Equal(expected, composed.Apply(50.0), 9);
        }

        /// <summary>
        /// The city buffer is rebuilt every 256 simulation ticks, so the writer re-asserts on a shorter
        /// cadence. Five hundred re-assertions must leave exactly one contribution in the slot.
        /// </summary>
        [Fact]
        public void Reconciler_RepeatedPassesNeverCompound()
        {
            var baseline = new ModifierDelta(0.0, 0.10);
            var desired = new ModifierDelta(0.0, 0.15);
            ModifierDelta expected = ModifierDelta.Compose(baseline, desired);

            ModifierDelta slot = baseline;
            ModifierDelta remembered = ModifierDelta.Zero;
            bool tracked = false;

            for (int pass = 0; pass < 500; pass++)
            {
                bool ours = tracked && slot.Equals(remembered.Composed(desired));
                ModifierDelta next = ModifierReconciler.Reconcile(
                    slot, ours, remembered, false, ModifierDelta.Zero, desired);

                remembered = ModifierReconciler.BaselineFor(slot, ours, remembered, false, ModifierDelta.Zero);
                slot = next;
                tracked = true;
            }

            Assert.Equal(expected.Relative, slot.Relative, 9);
        }

        /// <summary>The game wiping the slot must cost one pass of latency, not correctness.</summary>
        [Fact]
        public void Reconciler_RecoversWhenTheGameRebuildsTheBuffer()
        {
            var desired = new ModifierDelta(0.0, 0.15);
            var rebuiltByPolicies = new ModifierDelta(0.0, 0.30);

            ModifierDelta next = ModifierReconciler.Reconcile(
                rebuiltByPolicies, false, new ModifierDelta(0.0, 0.10), false, ModifierDelta.Zero, desired);

            Assert.Equal(ModifierDelta.Compose(rebuiltByPolicies, desired).Relative, next.Relative, 12);
        }

        /// <summary>Switching Agora off must hand back the player's own numbers, exactly.</summary>
        [Fact]
        public void Reconciler_RevertingRestoresTheBaselineExactly()
        {
            var baseline = new ModifierDelta(1.5, 0.10);
            var desired = new ModifierDelta(0.0, 0.15);

            ModifierDelta written = ModifierDelta.Compose(baseline, desired);
            ModifierDelta reverted = ModifierReconciler.Reconcile(
                written, true, baseline, false, ModifierDelta.Zero, ModifierDelta.Zero);

            Assert.Equal(baseline.Absolute, reverted.Absolute, 12);
            Assert.Equal(baseline.Relative, reverted.Relative, 12);
        }

        /// <summary>
        /// District modifiers are <c>ISerializable</c> and travel with the save, and only the city
        /// buffer is rebuilt on a timer — so a reloaded district slot can arrive still carrying Agora's
        /// contribution. Reloading ten times must not compound it ten times.
        /// </summary>
        [Fact]
        public void Reconciler_DoesNotDoubleApplyAcrossAReload()
        {
            var baseline = new ModifierDelta(0.0, 0.10);
            var desired = new ModifierDelta(0.0, 0.15);
            ModifierDelta onDisk = ModifierDelta.Compose(baseline, desired);

            ModifierDelta slot = onDisk;
            for (int reload = 0; reload < 10; reload++)
            {
                // Fresh session: nothing tracked, but the ledger says the effect predates today.
                slot = ModifierReconciler.Reconcile(slot, false, ModifierDelta.Zero,
                                                    true, desired, desired);
            }

            Assert.Equal(onDisk.Relative, slot.Relative, 9);
        }

        [Fact]
        public void Reconciler_DecomposeInvertsCompose()
        {
            var baseline = new ModifierDelta(3.0, -0.20);
            var addition = new ModifierDelta(0.5, 0.25);

            ModifierDelta recovered;
            Assert.True(ModifierDelta.TryDecompose(ModifierDelta.Compose(baseline, addition), addition, out recovered));
            Assert.Equal(baseline.Absolute, recovered.Absolute, 12);
            Assert.Equal(baseline.Relative, recovered.Relative, 12);
        }

        [Fact]
        public void Reconciler_ClampAndNonFiniteInputsCannotEscapeTheCap()
        {
            var wild = new ModifierDelta(double.NaN, double.PositiveInfinity);
            Assert.True(wild.IsZero);

            var large = new ModifierDelta(50.0, -50.0).Clamped(0.25);
            Assert.Equal(0.25, large.Absolute, 12);
            Assert.Equal(-0.25, large.Relative, 12);
        }
    }

    /// <summary>Test-local sugar so the reconciler assertions read as arithmetic rather than plumbing.</summary>
    internal static class ModifierDeltaTestExtensions
    {
        public static ModifierDelta Composed(this ModifierDelta baseline, ModifierDelta addition) =>
            ModifierDelta.Compose(baseline, addition);
    }
}
