using Agora.Mod.Time;
using Xunit;

namespace Agora.Mod.Time.Tests
{
    /// <summary>
    /// Start-year delivery. Every branch here decides whether to write to a component the base game
    /// serializes into the player's save, so the tests are written around the two questions that
    /// matter: does the player's original year survive, and does AGORA's calendar land on the
    /// configured start year even when the write does not happen?
    /// </summary>
    public sealed class StartYearPlannerTests
    {
        private const int StockYear = 2021;   // TimeData.kDefaultStartingYear
        private const int AgoraYear = 1990;   // AgoraSettings.StartYear

        // ---- default mode: rewrite the game's own epoch ------------------------------------

        [Fact]
        public void Rewrite_OnAStockSave_WritesTheEpochAndRecordsTheStockYear()
        {
            StartYearPlan plan = StartYearPlanner.Plan(
                StartYearDeliveryMode.RewriteGameEpoch, AgoraYear, StockYear, null);

            Assert.Equal(StartYearAction.WriteEpoch, plan.Action);
            Assert.Equal(AgoraYear, plan.EpochYearToWrite);
            Assert.Equal(StockYear, plan.StockEpochYear);
            Assert.Equal(0, plan.PoliticalYearOffset);
        }

        [Fact]
        public void Rewrite_OnAnAlreadyRewrittenSave_DoesNothingAndKeepsTheRecordedStockYear()
        {
            StartYearPlan plan = StartYearPlanner.Plan(
                StartYearDeliveryMode.RewriteGameEpoch, AgoraYear, AgoraYear, StockYear);

            Assert.Equal(StartYearAction.None, plan.Action);
            Assert.Equal(StockYear, plan.StockEpochYear);
            Assert.Equal(0, plan.PoliticalYearOffset);
        }

        [Fact]
        public void Rewrite_IsIdempotent_ASecondPassOverItsOwnResultChangesNothing()
        {
            StartYearPlan first = StartYearPlanner.Plan(
                StartYearDeliveryMode.RewriteGameEpoch, AgoraYear, StockYear, null);

            StartYearPlan second = StartYearPlanner.Plan(
                StartYearDeliveryMode.RewriteGameEpoch, AgoraYear, first.EpochYearToWrite, first.StockEpochYear);

            Assert.Equal(StartYearAction.None, second.Action);
            Assert.Equal(first.StockEpochYear, second.StockEpochYear);
        }

        [Fact]
        public void Rewrite_ClampsAnAbsurdStartYearRatherThanHandingItToDateTime()
        {
            StartYearPlan plan = StartYearPlanner.Plan(
                StartYearDeliveryMode.RewriteGameEpoch, int.MaxValue, StockYear, null);

            Assert.Equal(SimClockMath.MaxYear, plan.EpochYearToWrite);
        }

        // ---- kill switch --------------------------------------------------------------------

        [Fact]
        public void Off_RestoresTheRecordedStockYear()
        {
            StartYearPlan plan = StartYearPlanner.Plan(
                StartYearDeliveryMode.Off, AgoraYear, AgoraYear, StockYear);

            Assert.Equal(StartYearAction.RestoreEpoch, plan.Action);
            Assert.Equal(StockYear, plan.EpochYearToWrite);
            Assert.Equal(0, plan.PoliticalYearOffset);
        }

        [Fact]
        public void Off_OnAClockItNeverTouched_LeavesItAlone()
        {
            StartYearPlan plan = StartYearPlanner.Plan(
                StartYearDeliveryMode.Off, AgoraYear, StockYear, null);

            Assert.Equal(StartYearAction.None, plan.Action);
            Assert.Equal(0, plan.PoliticalYearOffset);
        }

        [Fact]
        public void Off_WithNoRecordedStockYear_NeverGuessesAYearToWrite()
        {
            // Without a recorded stock year the original is unrecoverable. Writing a guess would
            // corrupt the player's save far worse than leaving the epoch where it is.
            StartYearPlan plan = StartYearPlanner.Plan(
                StartYearDeliveryMode.Off, AgoraYear, AgoraYear, null);

            Assert.Equal(StartYearAction.None, plan.Action);
        }

        [Fact]
        public void RewriteThenOff_RoundTripsBackToTheStockYear()
        {
            StartYearPlan applied = StartYearPlanner.Plan(
                StartYearDeliveryMode.RewriteGameEpoch, AgoraYear, StockYear, null);

            StartYearPlan reverted = StartYearPlanner.Plan(
                StartYearDeliveryMode.Off, AgoraYear, applied.EpochYearToWrite, applied.StockEpochYear);

            Assert.Equal(StartYearAction.RestoreEpoch, reverted.Action);
            Assert.Equal(StockYear, reverted.EpochYearToWrite);
        }

        // ---- offset-only: never touch the player's clock -----------------------------------

        [Fact]
        public void OffsetOnly_LeavesAStockClockAloneAndShiftsPoliticalDatesInstead()
        {
            StartYearPlan plan = StartYearPlanner.Plan(
                StartYearDeliveryMode.OffsetOnly, AgoraYear, StockYear, null);

            Assert.Equal(StartYearAction.None, plan.Action);
            Assert.Equal(AgoraYear - StockYear, plan.PoliticalYearOffset);
        }

        [Fact]
        public void OffsetOnly_UndoesAPreviousRewriteBeforeApplyingItsOffset()
        {
            StartYearPlan plan = StartYearPlanner.Plan(
                StartYearDeliveryMode.OffsetOnly, AgoraYear, AgoraYear, StockYear);

            Assert.Equal(StartYearAction.RestoreEpoch, plan.Action);
            Assert.Equal(StockYear, plan.EpochYearToWrite);
            Assert.Equal(AgoraYear - StockYear, plan.PoliticalYearOffset);
        }

        // ---- the offset is derived from what was observed, not from what was intended -------

        [Fact]
        public void PoliticalYearOffset_IsZeroOnceTheEpochRewriteLanded()
        {
            Assert.Equal(0, StartYearPlanner.PoliticalYearOffset(
                StartYearDeliveryMode.RewriteGameEpoch, AgoraYear, AgoraYear));
        }

        [Fact]
        public void PoliticalYearOffset_AbsorbsARefusedEpochWriteSoAgoraStillStartsIn1990()
        {
            // The write failed: the epoch is still stock. AGORA's own year must still be 1990 at
            // game year 2021 — a wrong HUD is cosmetic, a wrong political year desyncs every seed.
            int offset = StartYearPlanner.PoliticalYearOffset(
                StartYearDeliveryMode.RewriteGameEpoch, AgoraYear, StockYear);

            Assert.Equal(AgoraYear, StockYear + offset);
        }

        [Fact]
        public void PoliticalYearOffset_IsZeroWhenAgoraIsOff()
        {
            Assert.Equal(0, StartYearPlanner.PoliticalYearOffset(
                StartYearDeliveryMode.Off, AgoraYear, StockYear));
        }

        [Fact]
        public void PoliticalYearOffset_TracksTheClampedStartYear()
        {
            int offset = StartYearPlanner.PoliticalYearOffset(
                StartYearDeliveryMode.OffsetOnly, 0, StockYear);

            Assert.Equal(SimClockMath.MinYear - StockYear, offset);
        }

        // ---- an unknown mode must be inert -------------------------------------------------

        [Fact]
        public void AnUnrecognisedModeNeverWritesToThePlayersClock()
        {
            StartYearPlan plan = StartYearPlanner.Plan(
                (StartYearDeliveryMode)99, AgoraYear, StockYear, null);

            Assert.Equal(StartYearAction.None, plan.Action);
            Assert.Equal(0, plan.PoliticalYearOffset);
        }

        [Fact]
        public void EveryPlanCarriesANonEmptyReasonForTheLog()
        {
            var modes = new[]
            {
                StartYearDeliveryMode.RewriteGameEpoch,
                StartYearDeliveryMode.OffsetOnly,
                StartYearDeliveryMode.Off
            };

            foreach (StartYearDeliveryMode mode in modes)
            {
                Assert.False(string.IsNullOrEmpty(
                    StartYearPlanner.Plan(mode, AgoraYear, StockYear, null).Reason));
                Assert.False(string.IsNullOrEmpty(
                    StartYearPlanner.Plan(mode, AgoraYear, AgoraYear, StockYear).Reason));
            }
        }
    }
}
