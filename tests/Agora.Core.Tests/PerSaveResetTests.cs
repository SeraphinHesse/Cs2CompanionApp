// Requires the ModifierDelta.cs / EffectLedger.cs <Compile Link> lines in Agora.Core.Tests.csproj
// (see EffectApplicationTests for why they are there).

using Agora.Core.Contracts;
using Agora.Core.Engine.Effects;
using Agora.Core.Tuning;
using Agora.Mod.Effects;
using Xunit;

namespace Agora.Core.Tests
{
    /// <summary>
    /// W0 — the per-save reset seam, as far as it can be reached without the game.
    ///
    /// <para>
    /// The defect: CS2 re-uses the ECS world across "quit to menu, load another city", so every
    /// static and every system instance in <c>Agora.Mod</c> outlives a save. City A's prose, its live
    /// modifiers and its heartbeat cadence all leaked into city B, and the effect ledger in
    /// particular kept city A's modifiers running with their duration counters ticking — the one
    /// layer of the three that mutates the player's city rather than merely misreporting it.
    /// <c>AgoraRuntime.ResetForNewSave</c> is now the single seam that clears all of it.
    /// </para>
    ///
    /// <para>
    /// <b>What is not here, and why.</b> <c>AgoraRuntime</c>, <c>AgoraHeartbeatSystem</c>,
    /// <c>AgoraSidecarSystem</c> and <c>AgoraEffectApplicationSystem</c> each reference
    /// <c>Unity.Entities</c> or <c>Game.*</c>, so none of them can be linked into this suite;
    /// tests/CLAUDE.md is explicit that a test needing the game is not a test but a manual gate item.
    /// The ledger is the reachable part, and it is the part that matters most. The prose-field
    /// clearing, the cadence latches and the slot table are covered by the in-game walkthrough in
    /// fixplan.md's verification gate.
    /// </para>
    ///
    /// <para>
    /// <b>One thing here is deliberately untested rather than merely untested yet.</b> Whether the
    /// reset hands the city's modifier buffers back (<c>ResetCause.ModShutdown</c>) or drops the slot
    /// table unreverted (<c>ResetCause.SaveBoundary</c>) turns on which world the caller is resetting
    /// against, which needs a world. So it is a manual gate item — but it has to be stated as an
    /// observable property, not as the presence of a log line. Both branches run against live entities
    /// (<c>onGamePreload</c> precedes the deserialize phase <c>ClearSystem</c> destroys the outgoing
    /// city in), and "no revert line on a save boundary" is equally true of a build that never applied
    /// an effect at all, so on its own it confirms nothing.
    /// </para>
    /// <para>
    /// The two things to check in game, both of them about numbers rather than logging:
    /// <list type="number">
    /// <item><description><b>Shutdown restores stock.</b> With a city open and an Agora effect visibly
    /// moving a district or city modifier, toggle Agora off. The modifier must return to the value it
    /// had before Agora touched it — not merely stop changing — and "effects: reverted every modifier
    /// Agora was holding" should accompany it.</description></item>
    /// <item><description><b>A save boundary carries nothing across.</b> Play city A until an effect is
    /// live, quit to menu, load city B, and confirm city B's modifiers show no contribution Agora did
    /// not itself request there — <c>TrackedSlotCount</c> starts at zero and city B's first effect pass
    /// writes only city B's own aggregate. The revert being skipped on that path is only correct
    /// because city A is discarded, so the property to verify is city B's cleanliness, not city A's.
    /// </description></item>
    /// </list>
    /// </para>
    /// </summary>
    public class PerSaveResetTests
    {
        /// <summary>The month city A was last played.</summary>
        private static readonly SimDate CityALastMonth = new SimDate(1997, 6, 1);

        /// <summary>A month later: city B is loaded, and city A's entry would still be live.</summary>
        private static readonly SimDate CityBFirstMonth = new SimDate(1997, 7, 1);

        private static EffectPalette Palette() => EffectPalette.From(EngineTuning.Default);

        /// <summary>
        /// A city-scoped request at its declared caps, so the entry is comfortably still live — and
        /// still above the palette's minimum magnitude — a month after it was admitted.
        /// </summary>
        private static EffectRequest LongRunningCityEffect(EffectPalette palette, string sourceId)
        {
            foreach (string id in palette.CityIds)
            {
                EffectCap cap;
                if (!palette.TryGetCap(id, out cap)) continue;
                return new EffectRequest(id, EffectScope.City, cap.MagnitudeCap,
                                         cap.DurationCapMonths, null, sourceId);
            }

            Assert.Fail("the shipped palette declares no city-scoped effect");
            return default;
        }

        [Fact]
        public void CityAsLiveModifiersAreStillRunningAMonthLater()
        {
            // The premise the reset exists for. Without this, the two tests below would pass for the
            // wrong reason — an entry that had simply expired on its own.
            EffectPalette palette = Palette();
            var ledger = new EffectLedger(palette);

            Assert.Equal(EffectAdmission.Accepted,
                         ledger.Add(LongRunningCityEffect(palette, "city-a"), CityALastMonth));

            Assert.NotEmpty(ledger.Aggregate(CityBFirstMonth));
        }

        [Fact]
        public void ClearingTheLedgerDropsEveryModifierTheClosedSaveWasDriving()
        {
            EffectPalette palette = Palette();
            var ledger = new EffectLedger(palette);

            ledger.Add(LongRunningCityEffect(palette, "city-a"), CityALastMonth);

            // What AgoraEffects.Shutdown does, and what Attach used to skip: it early-returns on the
            // same world, and CS2 hands the same world to every city, so Initialize never ran again.
            ledger.Clear();

            Assert.Equal(0, ledger.Count);
            Assert.Empty(ledger.Aggregate(CityBFirstMonth));
        }

        [Fact]
        public void ARebuiltLedgerReportsNothingCarriedOverOnCityBsFirstMonth()
        {
            EffectPalette palette = Palette();

            var cityA = new EffectLedger(palette);
            cityA.Add(LongRunningCityEffect(palette, "city-a"), CityALastMonth);

            // IsCarriedOver is how the application system decides a modifier slot already contains a
            // contribution of ours that must be divided back out. Leaking it into city B would make
            // the first write there subtract something that was never applied.
            System.Collections.Generic.IReadOnlyList<ModifierAggregate> stale = cityA.Aggregate(CityBFirstMonth);
            Assert.NotEmpty(stale);
            Assert.True(stale[0].IsCarriedOver);

            var cityB = new EffectLedger(palette);
            cityB.Add(LongRunningCityEffect(palette, "city-b"), CityBFirstMonth);

            System.Collections.Generic.IReadOnlyList<ModifierAggregate> fresh = cityB.Aggregate(CityBFirstMonth);
            Assert.NotEmpty(fresh);
            Assert.False(fresh[0].IsCarriedOver);
        }
    }
}
