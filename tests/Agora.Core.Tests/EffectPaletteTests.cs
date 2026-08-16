using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using Agora.Core.Contracts;
using Agora.Core.Engine.Effects;
using Agora.Core.Tuning;
using Xunit;

namespace Agora.Core.Tests
{
    /// <summary>
    /// Packet 14 — the sanctioned effect palette (<c>politicsmodplan.md</c> §7).
    ///
    /// <para>
    /// Two things are being defended here. First, the registry is <b>closed</b>: 43 entries, every one
    /// backed by a real game modifier member, every one with a fallback chain that terminates. Second,
    /// the caps are <b>unbypassable</b>: every entry is driven far past its magnitude and duration cap,
    /// in both directions, and asserted to clamp (non-negotiable #5).
    /// </para>
    /// </summary>
    public class EffectPaletteTests
    {
        private static readonly SimDate Jan1990 = new SimDate(1990, 1, 1);

        private static EffectPalette Palette() => EffectPalette.From(EngineTuning.Default);

        private static EffectPalette PaletteFrom(string json) =>
            EffectPalette.From(EngineTuning.FromJson(json));

        /// <summary>Records what actually reached the game. The real one lives in Agora.Mod/Effects.</summary>
        private sealed class RecordingSink : IEffectSink
        {
            public readonly List<EffectRequest> Applied = new List<EffectRequest>();
            public void Apply(EffectRequest request) => Applied.Add(request);
        }

        private static EffectRequest Request(EffectPalette palette, string effectId, double magnitude,
                                             int durationMonths, string? sourceId = "test")
        {
            EffectScope scope;
            if (!palette.TryGetScope(effectId, out scope)) scope = EffectScope.City;
            return new EffectRequest(effectId, scope, magnitude, durationMonths,
                                     scope == EffectScope.District ? "district-a" : null, sourceId);
        }

        private static string Hash(IReadOnlyList<EffectResolution> resolutions)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < resolutions.Count; i++) sb.Append(resolutions[i].ToDebugString()).Append('\n');

            using (SHA256 sha = SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(sb.ToString())));
        }

        // --- Registry shape ---------------------------------------------------------------------

        /// <summary>
        /// Golden shape of the closed registry. If this fails, an effect was added or removed — decide
        /// whether that was intended before touching the numbers (see /write-test, "golden values").
        /// </summary>
        [Fact]
        public void Registry_HasTheShippedShape()
        {
            EffectPalette p = Palette();

            // Wave 3 added three: district-street-speed-limit, city-office-software-efficiency and
            // city-highway-traffic-safety. This assertion is SUPPOSED to fail on a palette change —
            // that is what it is for — so the numbers move with a reviewed addition rather than the
            // assertion being loosened into something that would not have noticed.
            Assert.Equal(46, p.Count);
            Assert.Equal(13, p.DistrictIds.Count);
            Assert.Equal(33, p.CityIds.Count);
            Assert.Equal(p.Count, p.DistrictIds.Count + p.CityIds.Count);
        }

        [Fact]
        public void Registry_IdsAreSortedOrdinalAscending()
        {
            IReadOnlyList<string> ids = Palette().Ids;

            for (int i = 1; i < ids.Count; i++)
                Assert.True(string.CompareOrdinal(ids[i - 1], ids[i]) < 0,
                    "ids are not sorted ordinal ascending at " + ids[i]);
        }

        [Fact]
        public void Registry_TerminalsAreTerminalAndCorrectlyScoped()
        {
            EffectPalette p = Palette();

            Assert.Equal("district-wellbeing", p.TerminalFallbackId(EffectScope.District));
            Assert.Equal("city-tax-happiness", p.TerminalFallbackId(EffectScope.City));
            Assert.True(p.IsTerminal("district-wellbeing"));
            Assert.True(p.IsTerminal("city-tax-happiness"));

            // Exactly one terminal per scope: everything else must degrade somewhere.
            int districtTerminals = 0, cityTerminals = 0;
            for (int i = 0; i < p.Ids.Count; i++)
            {
                if (!p.IsTerminal(p.Ids[i])) continue;
                EffectScope scope;
                p.TryGetScope(p.Ids[i], out scope);
                if (scope == EffectScope.District) districtTerminals++; else cityTerminals++;
            }
            Assert.Equal(1, districtTerminals);
            Assert.Equal(1, cityTerminals);
        }

        /// <summary>Pins a sample of the shipped caps, so a silent widening fails here.</summary>
        [Fact]
        public void Registry_PinsKnownEntries()
        {
            EffectPalette p = Palette();

            EffectCap wellbeing;
            Assert.True(p.TryGetCap("district-wellbeing", out wellbeing));
            Assert.Equal(EffectScope.District, wellbeing.Scope);
            Assert.Equal("Wellbeing", wellbeing.Modifier);
            Assert.Equal(0.15, wellbeing.MagnitudeCap, 10);
            Assert.Equal(60, wellbeing.DurationCapMonths);
            Assert.Equal("", wellbeing.FallbackEffectId);

            EffectCap loan;
            Assert.True(p.TryGetCap("city-loan-interest", out loan));
            Assert.Equal(EffectScope.City, loan.Scope);
            Assert.Equal("LoanInterest", loan.Modifier);
            Assert.Equal(0.30, loan.MagnitudeCap, 10);
            Assert.Equal("city-tax-happiness", loan.FallbackEffectId);
        }

        /// <summary>
        /// §7's gap list — rent, land value, RCI demand, birth rate, subsidies — has no enum member
        /// behind it and deliberately ships no entry. This asserts the gap stays a gap rather than
        /// being quietly closed with an invented effect.
        /// </summary>
        [Theory]
        [InlineData("city-rent")]
        [InlineData("city-land-value")]
        [InlineData("city-rci-demand")]
        [InlineData("city-birth-rate")]
        [InlineData("city-subsidy")]
        [InlineData("district-rent")]
        [InlineData("district-land-value")]
        public void Registry_DoesNotContainUnbackedEffects(string effectId)
        {
            Assert.False(Palette().Contains(effectId));
        }

        [Fact]
        public void ValidateRegistry_ShippedPaletteIsClean()
        {
            EffectValidation v = Palette().ValidateRegistry();

            Assert.True(v.IsValid, v.Describe());
            Assert.False(v.HasIssues, v.Describe());
        }

        // --- Caps: every entry, both directions ---------------------------------------------------

        public static IEnumerable<object[]> AllEffectIds()
        {
            IReadOnlyList<string> ids = EngineTuning.Default.Effects.EffectIds;
            for (int i = 0; i < ids.Count; i++) yield return new object[] { ids[i] };
        }

        [Theory]
        [MemberData(nameof(AllEffectIds))]
        public void Cap_ClampsMagnitudeUpward(string effectId)
        {
            EffectPalette p = Palette();
            EffectCap cap;
            Assert.True(p.TryGetCap(effectId, out cap));

            double limit = p.EffectiveMagnitudeCap(cap);
            Assert.True(limit > 0.0 && limit <= 1.0);

            EffectResolution r = EffectResolver.Resolve(p, Request(p, effectId, 1_000_000.0, 6));

            Assert.Equal(EffectOutcome.Applied, r.Outcome);
            Assert.Equal(effectId, r.Request.EffectId);
            Assert.Equal(limit, r.Request.Magnitude, 12);
            Assert.True(r.MagnitudeClamped);
        }

        /// <summary>A cap that only holds for positive magnitudes is not a cap.</summary>
        [Theory]
        [MemberData(nameof(AllEffectIds))]
        public void Cap_ClampsMagnitudeDownward(string effectId)
        {
            EffectPalette p = Palette();
            EffectCap cap;
            Assert.True(p.TryGetCap(effectId, out cap));

            EffectResolution r = EffectResolver.Resolve(p, Request(p, effectId, -1_000_000.0, 6));

            Assert.Equal(EffectOutcome.Applied, r.Outcome);
            Assert.Equal(-p.EffectiveMagnitudeCap(cap), r.Request.Magnitude, 12);
            Assert.True(r.MagnitudeClamped);
        }

        [Theory]
        [MemberData(nameof(AllEffectIds))]
        public void Cap_ClampsDuration(string effectId)
        {
            EffectPalette p = Palette();
            EffectCap cap;
            Assert.True(p.TryGetCap(effectId, out cap));

            int limit = p.EffectiveDurationCapMonths(cap);
            Assert.True(limit > 0 && limit <= EngineTuning.Default.Effects.GlobalDurationCapMonths);

            EffectResolution r = EffectResolver.Resolve(p, Request(p, effectId, 0.05, 100_000));

            Assert.Equal(limit, r.Request.DurationMonths);
            Assert.True(r.DurationClamped);
        }

        [Theory]
        [MemberData(nameof(AllEffectIds))]
        public void Cap_RejectsNonFiniteAndNonPositiveDuration(string effectId)
        {
            EffectPalette p = Palette();

            EffectResolution nan = EffectResolver.Resolve(p, Request(p, effectId, double.NaN, 12));
            Assert.Equal(EffectOutcome.Dropped, nan.Outcome);
            Assert.Equal(EffectDropReason.NotFinite, nan.DropReason);

            EffectResolution infinite = EffectResolver.Resolve(p, Request(p, effectId, double.PositiveInfinity, 12));
            Assert.Equal(EffectDropReason.NotFinite, infinite.DropReason);

            EffectResolution negative = EffectResolver.Resolve(p, Request(p, effectId, 0.05, -5));
            Assert.Equal(EffectOutcome.Dropped, negative.Outcome);
            Assert.Equal(EffectDropReason.ZeroDuration, negative.DropReason);
        }

        [Fact]
        public void Cap_GlobalCeilingBeatsALooserPerEffectCap()
        {
            // A mis-authored entry claiming a cap of 5.0 must still be held to the global ceiling.
            EffectPalette p = PaletteFrom(@"{""effects"":{
                ""globalMagnitudeCap"": 0.10,
                ""globalDurationCapMonths"": 24,
                ""defaultFallbackCityEffectId"": ""city-tax-happiness"",
                ""perEffect"": {
                    ""city-tax-happiness"": { ""scope"": ""city"", ""modifier"": ""TaxHappiness"", ""magnitudeCap"": 5.0, ""durationCapMonths"": 999, ""fallbackEffectId"": """" }
                }}}");

            EffectResolution r = EffectResolver.Resolve(p, Request(p, "city-tax-happiness", 4.0, 500));

            Assert.Equal(0.10, r.Request.Magnitude, 12);
            Assert.Equal(24, r.Request.DurationMonths);

            EffectValidation v = p.ValidateRegistry();
            Assert.True(v.Has(EffectValidationCode.CapExceedsGlobalCap), v.Describe());
        }

        [Fact]
        public void Cap_DropsMagnitudesBelowTheNoiseFloor()
        {
            EffectPalette p = Palette();

            EffectResolution r = EffectResolver.Resolve(p, Request(p, "city-attractiveness", 0.0001, 12));

            Assert.Equal(EffectOutcome.Dropped, r.Outcome);
            Assert.Equal(EffectDropReason.MagnitudeBelowMinimum, r.DropReason);
        }

        // --- Fallback chains ---------------------------------------------------------------------

        [Theory]
        [MemberData(nameof(AllEffectIds))]
        public void FallbackChain_TerminatesForEveryEntry(string effectId)
        {
            EffectPalette p = Palette();
            EffectScope scope;
            Assert.True(p.TryGetScope(effectId, out scope));

            IReadOnlyList<string> chain = p.FallbackChain(effectId, scope);

            Assert.Equal(effectId, chain[0]);
            Assert.True(chain.Count <= p.Count);
            Assert.True(p.IsTerminal(chain[chain.Count - 1]),
                "chain for " + effectId + " ends at " + chain[chain.Count - 1] + ", which is not terminal");

            // Every step stays inside the scope, so degradation never changes what is being modified.
            for (int i = 0; i < chain.Count; i++)
            {
                EffectScope stepScope;
                Assert.True(p.TryGetScope(chain[i], out stepScope));
                Assert.Equal(scope, stepScope);
            }
        }

        /// <summary>
        /// §13.5: never cut an event for a missing effect. An id that is not in the registry — a typo,
        /// or one of §7's unbacked ideas — degrades onto the scope's terminal rather than vanishing.
        /// </summary>
        [Fact]
        public void Resolve_UnknownEffectDegradesToTheTerminal()
        {
            EffectPalette p = Palette();

            EffectResolution city = EffectResolver.Resolve(
                p, new EffectRequest("city-rent-control", EffectScope.City, 0.9, 12, null, "evt-1"));

            Assert.Equal(EffectOutcome.Substituted, city.Outcome);
            Assert.Equal("city-rent-control", city.RequestedEffectId);
            Assert.Equal("city-tax-happiness", city.Request.EffectId);
            Assert.Equal(1, city.FallbackDepth);
            Assert.Equal(0.15, city.Request.Magnitude, 12); // the terminal's cap, not the request's ask
            Assert.Equal("evt-1", city.Request.SourceId);

            EffectResolution district = EffectResolver.Resolve(
                p, new EffectRequest("district-land-value", EffectScope.District, 0.9, 12, "district-a", "evt-1"));

            Assert.Equal("district-wellbeing", district.Request.EffectId);
            Assert.Equal("district-a", district.Request.DistrictId);
        }

        [Fact]
        public void Resolve_UnavailableEffectFallsBackAndAdoptsTheFallbackCap()
        {
            EffectPalette p = Palette();
            EffectAvailabilityCheck availability =
                id => !string.Equals(id, "city-loan-interest", StringComparison.Ordinal);

            EffectResolution r = EffectResolver.Resolve(
                p, Request(p, "city-loan-interest", 0.28, 24), availability);

            Assert.Equal(EffectOutcome.Substituted, r.Outcome);
            Assert.Equal("city-tax-happiness", r.Request.EffectId);
            Assert.Equal("TaxHappiness", r.Modifier);
            Assert.Equal(0.15, r.Request.Magnitude, 12);
        }

        [Fact]
        public void Resolve_DropsWhenNothingInTheChainIsAvailable()
        {
            EffectPalette p = Palette();

            EffectResolution r = EffectResolver.Resolve(p, Request(p, "city-loan-interest", 0.1, 24), id => false);

            Assert.Equal(EffectOutcome.Dropped, r.Outcome);
            Assert.Equal(EffectDropReason.NoAvailableFallback, r.DropReason);
        }

        [Fact]
        public void Resolve_CityScopedRequestCarriesNoDistrict()
        {
            EffectPalette p = Palette();

            EffectResolution r = EffectResolver.Resolve(
                p, new EffectRequest("city-attractiveness", EffectScope.City, 0.1, 12, "district-a", "evt-1"));

            Assert.Equal(EffectScope.City, r.Request.Scope);
            Assert.Null(r.Request.DistrictId);
        }

        /// <summary>A mis-authored cycle must not hang the sink: the walk stops, and validation says why.</summary>
        [Fact]
        public void FallbackChain_SurvivesAMisauthoredCycle()
        {
            EffectPalette p = PaletteFrom(@"{""effects"":{
                ""defaultFallbackCityEffectId"": ""city-a"",
                ""perEffect"": {
                    ""city-a"": { ""scope"": ""city"", ""modifier"": ""TaxHappiness"", ""magnitudeCap"": 0.1, ""durationCapMonths"": 12, ""fallbackEffectId"": ""city-b"" },
                    ""city-b"": { ""scope"": ""city"", ""modifier"": ""Attractiveness"", ""magnitudeCap"": 0.1, ""durationCapMonths"": 12, ""fallbackEffectId"": ""city-a"" }
                }}}");

            IReadOnlyList<string> chain = p.FallbackChain("city-a", EffectScope.City);

            Assert.Equal(2, chain.Count);
            Assert.Equal("city-a", chain[0]);
            Assert.Equal("city-b", chain[1]);

            EffectValidation v = p.ValidateRegistry();
            Assert.False(v.IsValid);
            Assert.True(v.Has(EffectValidationCode.FallbackCycle), v.Describe());

            // And resolution still terminates, applying the head of the chain.
            EffectResolution r = EffectResolver.Resolve(p, Request(p, "city-a", 0.05, 6));
            Assert.Equal("city-a", r.Request.EffectId);
        }

        // --- Severity scaling ---------------------------------------------------------------------

        [Fact]
        public void Severity_ScalesMagnitudeButNeverPastTheCap()
        {
            EffectsTuning t = EngineTuning.Default.Effects;

            // severityMagnitudeScale = 0.20 → severity 5 is 1 + 0.20 * 4 = 1.8x.
            Assert.Equal(0.05, EffectResolver.ScaleForSeverity(t, 0.05, 1), 12);
            Assert.Equal(0.09, EffectResolver.ScaleForSeverity(t, 0.05, 5), 12);
            Assert.Equal(0.05, EffectResolver.ScaleForSeverity(t, 0.05, -3), 12);   // clamped up to 1
            Assert.Equal(0.09, EffectResolver.ScaleForSeverity(t, 0.05, 99), 12);   // clamped down to 5

            EffectPalette p = Palette();
            var authored = new TimelineEventEffect("city-attractiveness", EffectScope.City, 0.20, 24);

            EffectResolution r = EffectResolver.ResolveForEvent(p, authored, 5, "gfc-2008");

            Assert.Equal(0.25, r.Request.Magnitude, 12); // 0.20 * 1.8 = 0.36, clamped to the 0.25 cap
            Assert.True(r.MagnitudeClamped);
            Assert.Equal("gfc-2008", r.Request.SourceId);
        }

        [Fact]
        public void ResolveForEvent_UsesTheSchedulerSuppliedDistrict()
        {
            EffectPalette p = Palette();
            var authored = new TimelineEventEffect("district-crime-accumulation", EffectScope.District, 0.10, 12);

            EffectResolution placed = EffectResolver.ResolveForEvent(p, authored, 2, "riots-1992", "district-b");
            Assert.Equal("district-b", placed.Request.DistrictId);

            EffectResolution unplaced = EffectResolver.ResolveForEvent(p, authored, 2, "riots-1992");
            Assert.Equal(EffectOutcome.Dropped, unplaced.Outcome);
            Assert.Equal(EffectDropReason.MissingDistrictId, unplaced.DropReason);
        }

        // --- Decay and schedule --------------------------------------------------------------------

        [Fact]
        public void Schedule_LinearDecayRunsFromFullToZero()
        {
            EffectsTuning t = EngineTuning.Default.Effects; // decayCurve = linear

            Assert.Equal(1.0, EffectSchedule.DecayFactor(t, 0, 12), 12);
            Assert.Equal(0.5, EffectSchedule.DecayFactor(t, 6, 12), 12);
            Assert.Equal(0.0, EffectSchedule.DecayFactor(t, 12, 12), 12);
            Assert.Equal(0.0, EffectSchedule.DecayFactor(t, 40, 12), 12);

            Assert.Equal(0.10, EffectSchedule.MagnitudeAt(t, 0.20, Jan1990, 12, new SimDate(1990, 7, 1)), 12);
            Assert.Equal(0.0, EffectSchedule.MagnitudeAt(t, 0.20, Jan1990, 12, new SimDate(1991, 1, 1)), 12);
            Assert.Equal(0.0, EffectSchedule.MagnitudeAt(t, 0.20, Jan1990, 12, new SimDate(1989, 6, 1)), 12);

            // Decay shrinks toward zero; it never flips the sign.
            Assert.Equal(-0.10, EffectSchedule.MagnitudeAt(t, -0.20, Jan1990, 12, new SimDate(1990, 7, 1)), 12);
        }

        [Fact]
        public void Schedule_ExponentialDecayHalvesEveryHalfLife()
        {
            EffectsTuning t = PaletteFrom(@"{""effects"":{""decayCurve"":""exponential"",""decayHalfLifeMonths"":6}}").Tuning;

            Assert.Equal(1.0, EffectSchedule.DecayFactor(t, 0, 24), 12);
            Assert.Equal(0.5, EffectSchedule.DecayFactor(t, 6, 24), 12);
            Assert.Equal(0.25, EffectSchedule.DecayFactor(t, 12, 24), 12);
            Assert.Equal(0.0, EffectSchedule.DecayFactor(t, 24, 24), 12);
        }

        [Fact]
        public void Schedule_StepDecayHoldsFullStrengthThenStops()
        {
            EffectsTuning t = PaletteFrom(@"{""effects"":{""decayCurve"":""step""}}").Tuning;

            Assert.Equal(1.0, EffectSchedule.DecayFactor(t, 11, 12), 12);
            Assert.Equal(0.0, EffectSchedule.DecayFactor(t, 12, 12), 12);
        }

        [Fact]
        public void Schedule_ActivityWindowAndReapplyMonths()
        {
            EffectsTuning t = EngineTuning.Default.Effects; // reapplyIntervalMonths = 1

            Assert.True(EffectSchedule.IsActive(Jan1990, 12, Jan1990));
            Assert.True(EffectSchedule.IsActive(Jan1990, 12, new SimDate(1990, 12, 1)));
            Assert.False(EffectSchedule.IsActive(Jan1990, 12, new SimDate(1991, 1, 1)));
            Assert.False(EffectSchedule.IsActive(Jan1990, 12, new SimDate(1989, 12, 1)));
            Assert.False(EffectSchedule.IsActive(Jan1990, 0, Jan1990));

            Assert.Equal(new SimDate(1991, 1, 1), EffectSchedule.ExpiryDate(Jan1990, 12));

            Assert.True(EffectSchedule.IsReapplyMonth(t, Jan1990, 12, new SimDate(1990, 5, 1)));
            Assert.False(EffectSchedule.IsReapplyMonth(t, Jan1990, 12, new SimDate(1992, 5, 1)));

            EffectsTuning quarterly = PaletteFrom(@"{""effects"":{""reapplyIntervalMonths"":3}}").Tuning;
            Assert.True(EffectSchedule.IsReapplyMonth(quarterly, Jan1990, 24, new SimDate(1990, 4, 1)));
            Assert.False(EffectSchedule.IsReapplyMonth(quarterly, Jan1990, 24, new SimDate(1990, 5, 1)));
        }

        // --- Stacking ------------------------------------------------------------------------------

        /// <summary>
        /// Whether the game stacks modifier sources additively is unverified (Scout 0002 Q7), so the
        /// worst case is assumed: however many sources pile onto one modifier, their total may not
        /// exceed the cap, and no more than <c>maxStackedPerModifier</c> of them survive.
        /// </summary>
        [Fact]
        public void Stacking_TotalNeverExceedsTheCapAndTheCountIsLimited()
        {
            EffectPalette p = Palette(); // maxStackedPerModifier = 4, stackingMode = sum
            EffectCap cap;
            p.TryGetCap("city-attractiveness", out cap);
            double limit = p.EffectiveMagnitudeCap(cap);

            var requests = new List<EffectRequest>();
            for (int i = 0; i < 6; i++)
                requests.Add(new EffectRequest("city-attractiveness", EffectScope.City, 0.20 - (i * 0.01), 12, null, "evt-" + i));

            IReadOnlyList<EffectResolution> stacked = EffectDispatcher.Preview(p, requests);

            int applied = 0, dropped = 0;
            double total = 0.0;
            for (int i = 0; i < stacked.Count; i++)
            {
                if (stacked[i].IsApplicable)
                {
                    applied++;
                    total += Math.Abs(stacked[i].Request.Magnitude);
                }
                else
                {
                    dropped++;
                    Assert.Equal(EffectDropReason.StackLimit, stacked[i].DropReason);
                }
            }

            Assert.Equal(6, stacked.Count);
            Assert.Equal(4, applied);
            Assert.Equal(2, dropped);
            Assert.True(total <= limit + 1e-12, "stacked total " + total + " exceeds cap " + limit);
            Assert.Equal(limit, total, 10);
        }

        [Fact]
        public void Stacking_DoesNotMergeDifferentDistricts()
        {
            EffectPalette p = Palette();

            var requests = new List<EffectRequest>
            {
                new EffectRequest("district-wellbeing", EffectScope.District, 0.14, 12, "district-a", "evt-1"),
                new EffectRequest("district-wellbeing", EffectScope.District, 0.14, 12, "district-b", "evt-1")
            };

            IReadOnlyList<EffectResolution> stacked = EffectDispatcher.Preview(p, requests);

            Assert.True(stacked[0].IsApplicable);
            Assert.True(stacked[1].IsApplicable);
            Assert.Equal(0.14, stacked[0].Request.Magnitude, 12);
            Assert.Equal(0.14, stacked[1].Request.Magnitude, 12);
        }

        [Fact]
        public void Stacking_MaxModeKeepsOnlyTheStrongest()
        {
            EffectPalette p = PaletteFrom(@"{""effects"":{""stackingMode"":""max""}}");

            var requests = new List<EffectRequest>
            {
                new EffectRequest("city-entertainment", EffectScope.City, 0.05, 12, null, "evt-weak"),
                new EffectRequest("city-entertainment", EffectScope.City, 0.18, 12, null, "evt-strong")
            };

            IReadOnlyList<EffectResolution> stacked = EffectDispatcher.Preview(p, requests);

            Assert.False(stacked[0].IsApplicable);
            Assert.Equal(EffectDropReason.StackLimit, stacked[0].DropReason);
            Assert.True(stacked[1].IsApplicable);
            Assert.Equal(0.18, stacked[1].Request.Magnitude, 12);
        }

        // --- Dispatch ------------------------------------------------------------------------------

        [Fact]
        public void Dispatch_AppliesOnlyClampedRequests()
        {
            EffectPalette p = Palette();
            var sink = new RecordingSink();
            var dispatcher = new EffectDispatcher(p, sink);

            var requests = new List<EffectRequest>
            {
                new EffectRequest("city-attractiveness", EffectScope.City, 9.0, 900, null, "evt-1"),
                new EffectRequest("district-wellbeing", EffectScope.District, -9.0, 900, "district-a", "evt-1"),
                new EffectRequest("city-attractiveness", EffectScope.City, 0.0000001, 12, null, "evt-2")
            };

            IReadOnlyList<EffectResolution> result = dispatcher.Dispatch(requests);

            Assert.Equal(2, sink.Applied.Count);
            Assert.Equal(0.25, sink.Applied[0].Magnitude, 12);
            Assert.Equal(60, sink.Applied[0].DurationMonths);
            Assert.Equal(-0.15, sink.Applied[1].Magnitude, 12);
            Assert.Equal(60, sink.Applied[1].DurationMonths);
            Assert.Equal(EffectDropReason.MagnitudeBelowMinimum, result[2].DropReason);
        }

        [Fact]
        public void Dispatch_AppliesNothingWhenEffectsAreOff()
        {
            EffectPalette p = Palette();
            var sink = new RecordingSink();
            var requests = new List<EffectRequest>
            {
                new EffectRequest("city-attractiveness", EffectScope.City, 0.2, 12, null, "evt-1")
            };

            IReadOnlyList<EffectResolution> perSave = new EffectDispatcher(p, sink).Dispatch(requests, false);
            Assert.Empty(sink.Applied);
            Assert.Equal(EffectOutcome.Suppressed, perSave[0].Outcome);

            EffectPalette disabled = PaletteFrom(@"{""effects"":{""enabled"":false}}");
            IReadOnlyList<EffectResolution> global = new EffectDispatcher(disabled, sink).Dispatch(requests);
            Assert.Empty(sink.Applied);
            Assert.Equal(EffectOutcome.Suppressed, global[0].Outcome);
            Assert.Equal(EffectDropReason.PaletteDisabled, global[0].DropReason);
        }

        // --- Validation (packet 11 validates catalogs against this) ---------------------------------

        [Fact]
        public void Validate_RejectsIdsOutsideTheClosedRegistry()
        {
            EffectValidation v = Palette().Validate("city-rent-control", EffectScope.City, 0.1, 12, null);

            Assert.False(v.IsValid);
            Assert.True(v.Has(EffectValidationCode.UnknownEffectId));
        }

        [Fact]
        public void Validate_RejectsMagnitudeAndDurationOutsideTheCap()
        {
            EffectPalette p = Palette();

            EffectValidation magnitude = p.Validate("city-attractiveness", EffectScope.City, 0.90, 12, null);
            Assert.False(magnitude.IsValid);
            Assert.True(magnitude.Has(EffectValidationCode.MagnitudeExceedsCap));

            EffectValidation duration = p.Validate("city-attractiveness", EffectScope.City, 0.10, 600, null);
            Assert.False(duration.IsValid);
            Assert.True(duration.Has(EffectValidationCode.DurationExceedsCap));

            EffectValidation negative = p.Validate("city-attractiveness", EffectScope.City, 0.10, -1, null);
            Assert.True(negative.Has(EffectValidationCode.NegativeDuration));
        }

        [Fact]
        public void Validate_RejectsAScopeTheRegistryDoesNotAgreeWith()
        {
            EffectValidation v = Palette().Validate("district-wellbeing", EffectScope.City, 0.1, 12, null);

            Assert.False(v.IsValid);
            Assert.True(v.Has(EffectValidationCode.ScopeMismatch));
        }

        /// <summary>
        /// A catalog entry never names a district — the scheduler fills that in — so a null district on
        /// a district-scoped entry must not be a finding, or every authored event would fail to load.
        /// </summary>
        [Fact]
        public void Validate_AcceptsCatalogEntriesWithNoDistrict()
        {
            EffectValidation v = Palette().Validate(
                new TimelineEventEffect("district-crime-accumulation", EffectScope.District, 0.10, 12));

            Assert.True(v.IsValid, v.Describe());
            Assert.False(v.HasIssues, v.Describe());
        }

        [Fact]
        public void Validate_WarnsWhenSeverityScalingWouldBreachTheCap()
        {
            var authored = new TimelineEventEffect("district-wellbeing", EffectScope.District, 0.14, 12);

            EffectValidation atOne = Palette().Validate(authored, 1);
            Assert.False(atOne.HasIssues, atOne.Describe());

            EffectValidation atFive = Palette().Validate(authored, 5);
            Assert.True(atFive.IsValid, atFive.Describe()); // a warning, not a failure — the sink clamps
            Assert.True(atFive.Has(EffectValidationCode.MagnitudeExceedsCapAtSeverity));
        }

        [Fact]
        public void Validate_WarnsAboutNoiseLevelMagnitudes()
        {
            EffectValidation v = Palette().Validate("city-attractiveness", EffectScope.City, 0.00005, 12, null);

            Assert.True(v.IsValid);
            Assert.True(v.Has(EffectValidationCode.MagnitudeBelowMinimum));
        }

        // --- Determinism ---------------------------------------------------------------------------

        private static IReadOnlyList<EffectRequest> Batch(double firstMagnitude)
        {
            return new List<EffectRequest>
            {
                new EffectRequest("city-attractiveness", EffectScope.City, firstMagnitude, 36, null, "evt-a"),
                new EffectRequest("city-attractiveness", EffectScope.City, 0.19, 36, null, "evt-b"),
                new EffectRequest("city-loan-interest", EffectScope.City, -0.90, 900, null, "evt-c"),
                new EffectRequest("district-wellbeing", EffectScope.District, 0.11, 24, "district-a", "evt-d"),
                new EffectRequest("district-crime-accumulation", EffectScope.District, 0.30, 24, "district-a", "evt-d"),
                new EffectRequest("city-rent-control", EffectScope.City, 0.40, 24, null, "evt-e"),
                new EffectRequest("city-entertainment", EffectScope.City, 0.0000001, 24, null, "evt-f")
            };
        }

        [Fact]
        public void Dispatch_IsByteIdenticalAcrossRuns()
        {
            EffectPalette p = Palette();

            string first = Hash(EffectDispatcher.Preview(p, Batch(0.20)));
            string second = Hash(EffectDispatcher.Preview(p, Batch(0.20)));

            Assert.Equal(first, second);
        }

        /// <summary>
        /// The negative half of the determinism pattern. Without it, a resolver that returned a
        /// constant would pass the test above perfectly.
        /// </summary>
        [Fact]
        public void Dispatch_DiffersWhenTheInputDiffers()
        {
            EffectPalette p = Palette();

            Assert.NotEqual(Hash(EffectDispatcher.Preview(p, Batch(0.20))),
                            Hash(EffectDispatcher.Preview(p, Batch(0.05))));
        }

        /// <summary>
        /// Ordering guard: a palette built twice from the same tuning enumerates in the same order.
        /// A dictionary leaking into a returned list is the classic way determinism dies quietly.
        /// </summary>
        [Fact]
        public void Palette_ListsAreOrderStableAcrossInstances()
        {
            EffectPalette a = Palette();
            EffectPalette b = Palette();

            Assert.Equal(string.Join(",", a.Ids), string.Join(",", b.Ids));
            Assert.Equal(string.Join(",", a.CityIds), string.Join(",", b.CityIds));
            Assert.Equal(string.Join(",", a.DistrictIds), string.Join(",", b.DistrictIds));
        }
    }
}
