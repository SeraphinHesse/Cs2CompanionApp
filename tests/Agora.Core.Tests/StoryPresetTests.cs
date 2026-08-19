using System;
using System.IO;
using Agora.Core.Contracts;
using Agora.Core.Engine.Effects;
using Agora.Core.Tuning;
using Xunit;

namespace Agora.Core.Tests
{
    /// <summary>
    /// The two preset ladders wave 7 landed: <see cref="PowerIntensity"/> over the political-power
    /// economy, and <see cref="StoryDifficulty"/> over the story cycle.
    ///
    /// <para>
    /// <b>Nothing here asserts a literal coefficient.</b> Every expectation is read back out of
    /// <see cref="EngineTuning"/> and every claim is about the <i>shape</i> of the relationship —
    /// lenient below default below harsh, and so on. The wave that added these ladders is also the
    /// wave that rebalanced them, so a memorised number would have gone red on its own author's next
    /// edit and taught the next person to change the test rather than the tuning.
    /// </para>
    ///
    /// <para>
    /// The rule the whole file exists to hold is the one <c>TuningPresetsTests</c> already holds for
    /// the three voter-model levels, restated for two settings that each move a <i>set</i> of
    /// coefficients: <c>Default</c> is not a value. It means "leave the tuning file alone", so a
    /// later retune of the shipped economy reaches every save that never chose otherwise. A preset
    /// table that spelled the default out would freeze whatever numbers were current the first time
    /// the player opened the settings drawer, and nothing in the save would ever say so.
    /// </para>
    ///
    /// <para>
    /// The counterpart obligation is that a non-<c>Default</c> level must actually move something.
    /// The spine opened <c>setSetting</c>'s <c>powerIntensity</c> and <c>storyDifficulty</c> keys on
    /// the undertaking that these tables landed in the same wave; a level that persisted a value and
    /// changed no number would be the exact defect W5 closed for <c>PauseOnMajorNews</c>.
    /// </para>
    /// </summary>
    public class StoryPresetTests
    {
        private static string RepoRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null)
            {
                if (File.Exists(Path.Combine(dir.FullName, "Agora.sln"))) return dir.FullName;
                dir = dir.Parent;
            }
            throw new InvalidOperationException("Could not locate the repository root.");
        }

        /// <summary>
        /// A fresh parse every time. <see cref="TuningPresets.Apply"/> mutates and is one-way, so a
        /// shared instance would leak an override from one test into the next.
        /// </summary>
        private static EngineTuning Shipped() =>
            EngineTuning.FromJson(File.ReadAllText(Path.Combine(RepoRoot(), "data", "engine_tuning.json")));

        private static EngineTuning Applied(PowerIntensity level)
        {
            EngineTuning tuning = Shipped();
            TuningPresets.Apply(tuning, new AgoraSettings { PowerIntensity = level });
            return tuning;
        }

        private static EngineTuning Applied(StoryDifficulty level)
        {
            EngineTuning tuning = Shipped();
            TuningPresets.Apply(tuning, new AgoraSettings { StoryDifficulty = level });
            return tuning;
        }

        // --- Default is not a value ------------------------------------------------------------

        [Fact]
        public void PowerIntensityDefault_LeavesTheWholeEconomyAlone()
        {
            EngineTuning baseline = Shipped();
            EngineTuning applied = Applied(PowerIntensity.Default);

            Assert.Equal(baseline.Power.MaxMonthlyGain, applied.Power.MaxMonthlyGain);
            Assert.Equal(baseline.Power.FailureLossRatio, applied.Power.FailureLossRatio, 12);
            AssertSameTiers(baseline.Power.SuccessGain, applied.Power.SuccessGain);
            AssertSameTiers(baseline.Power.OverrideCost, applied.Power.OverrideCost);

            // The dial that is deliberately NOT on the ladder. debtRevenuePenalty is the magnitude of
            // a city-service-building-upkeep request, whose palette cap is 0.20 and which
            // EffectResolver clamps to — so a level above it would persist a number and move nothing.
            // If a later wave puts it on the ladder, this line is where that decision has to argue.
            Assert.Equal(baseline.Power.DebtRevenuePenalty, applied.Power.DebtRevenuePenalty, 12);
        }

        [Fact]
        public void StoryDifficultyDefault_LeavesTheWholeStoryCycleAlone()
        {
            EngineTuning baseline = Shipped();
            EngineTuning applied = Applied(StoryDifficulty.Default);

            Assert.Equal(baseline.Stories.SuccessThreshold, applied.Stories.SuccessThreshold);
            Assert.Equal(baseline.Stories.ActiveEffectScale, applied.Stories.ActiveEffectScale, 12);
            Assert.Equal(baseline.Stories.WrappedEventHappinessGoalPoints,
                         applied.Stories.WrappedEventHappinessGoalPoints, 12);
        }

        [Fact]
        public void DefaultLevels_ResolveToTheFilesOwnValues_NotAPresetKey()
        {
            EngineTuning t = Shipped();

            Assert.Equal(t.Power.MaxMonthlyGain, TuningPresets.MaxMonthlyGainFor(t, PowerIntensity.Default));
            Assert.Equal(t.Power.FailureLossRatio,
                         TuningPresets.FailureLossRatioFor(t, PowerIntensity.Default), 12);
            AssertSameTiers(t.Power.SuccessGain, TuningPresets.SuccessGainFor(t, PowerIntensity.Default));
            AssertSameTiers(t.Power.OverrideCost, TuningPresets.OverrideCostFor(t, PowerIntensity.Default));

            Assert.Equal(t.Stories.SuccessThreshold,
                         TuningPresets.SuccessThresholdFor(t, StoryDifficulty.Default));
            Assert.Equal(t.Stories.ActiveEffectScale,
                         TuningPresets.ActiveEffectScaleFor(t, StoryDifficulty.Default), 12);
            Assert.Equal(t.Stories.WrappedEventHappinessGoalPoints,
                         TuningPresets.WrappedEventHappinessGoalPointsFor(t, StoryDifficulty.Default), 12);
        }

        [Fact]
        public void NeitherLadder_ReachesTheOthersPacket()
        {
            // A level is allowed to move its own economy and nothing else. Both are checked in the
            // same test because the failure they guard against is one edit: an Apply* method pasted
            // from its neighbour and left pointing at the neighbour's section.
            EngineTuning baseline = Shipped();

            EngineTuning power = Applied(PowerIntensity.Harsh);
            Assert.Equal(baseline.Stories.SuccessThreshold, power.Stories.SuccessThreshold);
            Assert.Equal(baseline.Stories.ActiveEffectScale, power.Stories.ActiveEffectScale, 12);

            EngineTuning stories = Applied(StoryDifficulty.Demanding);
            Assert.Equal(baseline.Power.MaxMonthlyGain, stories.Power.MaxMonthlyGain);
            Assert.Equal(baseline.Power.FailureLossRatio, stories.Power.FailureLossRatio, 12);
        }

        // --- the levels land, and land the right way round --------------------------------------

        [Theory]
        [InlineData(PowerIntensity.Lenient)]
        [InlineData(PowerIntensity.Harsh)]
        public void PowerIntensity_WritesEveryDialOfItsPreset(PowerIntensity level)
        {
            EngineTuning expected = Shipped();
            EngineTuning applied = Applied(level);

            Assert.Equal(TuningPresets.MaxMonthlyGainFor(expected, level), applied.Power.MaxMonthlyGain);
            Assert.Equal(TuningPresets.FailureLossRatioFor(expected, level), applied.Power.FailureLossRatio, 12);
            AssertSameTiers(TuningPresets.SuccessGainFor(expected, level), applied.Power.SuccessGain);
            AssertSameTiers(TuningPresets.OverrideCostFor(expected, level), applied.Power.OverrideCost);
        }

        [Theory]
        [InlineData(StoryDifficulty.Forgiving)]
        [InlineData(StoryDifficulty.Demanding)]
        public void StoryDifficulty_WritesEveryDialOfItsPreset(StoryDifficulty level)
        {
            EngineTuning expected = Shipped();
            EngineTuning applied = Applied(level);

            Assert.Equal(TuningPresets.SuccessThresholdFor(expected, level), applied.Stories.SuccessThreshold);
            Assert.Equal(TuningPresets.ActiveEffectScaleFor(expected, level),
                         applied.Stories.ActiveEffectScale, 12);
            Assert.Equal(TuningPresets.WrappedEventHappinessGoalPointsFor(expected, level),
                         applied.Stories.WrappedEventHappinessGoalPoints, 12);
        }

        [Theory]
        [InlineData(PowerIntensity.Lenient)]
        [InlineData(PowerIntensity.Harsh)]
        public void EveryPowerLevel_MovesAtLeastOneCoefficient(PowerIntensity level)
        {
            EngineTuning baseline = Shipped();
            EngineTuning applied = Applied(level);

            bool moved = applied.Power.MaxMonthlyGain != baseline.Power.MaxMonthlyGain
                         || applied.Power.FailureLossRatio != baseline.Power.FailureLossRatio
                         || !SameTiers(applied.Power.SuccessGain, baseline.Power.SuccessGain)
                         || !SameTiers(applied.Power.OverrideCost, baseline.Power.OverrideCost);

            Assert.True(moved, level + " changed no number, which makes its write key a switch that "
                               + "does nothing — the defect the key was withheld for two waves to avoid.");
        }

        [Theory]
        [InlineData(StoryDifficulty.Forgiving)]
        [InlineData(StoryDifficulty.Demanding)]
        public void EveryStoryLevel_MovesAtLeastOneCoefficient(StoryDifficulty level)
        {
            EngineTuning baseline = Shipped();
            EngineTuning applied = Applied(level);

            bool moved = applied.Stories.SuccessThreshold != baseline.Stories.SuccessThreshold
                         || applied.Stories.ActiveEffectScale != baseline.Stories.ActiveEffectScale
                         || applied.Stories.WrappedEventHappinessGoalPoints
                            != baseline.Stories.WrappedEventHappinessGoalPoints;

            Assert.True(moved, level + " changed no number, which makes its write key a switch that "
                               + "does nothing — the defect the key was withheld for two waves to avoid.");
        }

        [Fact]
        public void ThePowerLadder_RunsLenientThroughDefaultToHarsh()
        {
            // Shape, not values. Lenient must be the generous end of every dial it moves and Harsh
            // the punishing end, or the level names lie about which way they go — which is not
            // something a player can discover from the panel.
            EngineTuning t = Shipped();

            Assert.True(TuningPresets.MaxMonthlyGainFor(t, PowerIntensity.Lenient) > t.Power.MaxMonthlyGain,
                        "Lenient must accrue faster than the shipped economy.");
            Assert.True(TuningPresets.MaxMonthlyGainFor(t, PowerIntensity.Harsh) < t.Power.MaxMonthlyGain,
                        "Harsh must accrue slower than the shipped economy.");

            Assert.True(TuningPresets.FailureLossRatioFor(t, PowerIntensity.Lenient) < t.Power.FailureLossRatio,
                        "Lenient must charge less for a failure.");
            Assert.True(TuningPresets.FailureLossRatioFor(t, PowerIntensity.Harsh) > t.Power.FailureLossRatio,
                        "Harsh must charge more for a failure.");

            AssertTiersOrdered(TuningPresets.SuccessGainFor(t, PowerIntensity.Harsh), t.Power.SuccessGain,
                               TuningPresets.SuccessGainFor(t, PowerIntensity.Lenient),
                               "the award rises from Harsh through Default to Lenient");
            AssertTiersOrdered(TuningPresets.OverrideCostFor(t, PowerIntensity.Lenient), t.Power.OverrideCost,
                               TuningPresets.OverrideCostFor(t, PowerIntensity.Harsh),
                               "the override price rises from Lenient through Default to Harsh");
        }

        [Fact]
        public void TheStoryLadder_RunsForgivingThroughDefaultToDemanding()
        {
            EngineTuning t = Shipped();

            Assert.True(TuningPresets.SuccessThresholdFor(t, StoryDifficulty.Forgiving)
                        < t.Stories.SuccessThreshold, "Forgiving must ask for fewer met slots.");
            Assert.True(TuningPresets.SuccessThresholdFor(t, StoryDifficulty.Demanding)
                        > t.Stories.SuccessThreshold, "Demanding must ask for more met slots.");

            Assert.True(TuningPresets.ActiveEffectScaleFor(t, StoryDifficulty.Forgiving)
                        < t.Stories.ActiveEffectScale, "Forgiving must lean less hard on the city.");
            Assert.True(TuningPresets.ActiveEffectScaleFor(t, StoryDifficulty.Demanding)
                        > t.Stories.ActiveEffectScale, "Demanding must lean harder on the city.");

            Assert.True(TuningPresets.WrappedEventHappinessGoalPointsFor(t, StoryDifficulty.Forgiving)
                        < t.Stories.WrappedEventHappinessGoalPoints,
                        "Forgiving must ask a wrapped event for a smaller gain.");
            Assert.True(TuningPresets.WrappedEventHappinessGoalPointsFor(t, StoryDifficulty.Demanding)
                        > t.Stories.WrappedEventHappinessGoalPoints,
                        "Demanding must ask a wrapped event for a larger gain.");
        }

        // --- the invariants a level must not break ------------------------------------------------

        [Theory]
        [InlineData(PowerIntensity.Lenient)]
        [InlineData(PowerIntensity.Default)]
        [InlineData(PowerIntensity.Harsh)]
        public void EveryPowerLevel_KeepsTheOverridePriceAboveTheAward(PowerIntensity level)
        {
            // The schema states this and cannot check it. A price at or below the award means buying
            // a slot off pays for itself and the currency is free — and a ladder that breaks it
            // breaks the economy only for the players who chose that level, which is the hardest
            // version of this to notice from a play session.
            EngineTuning t = Shipped();
            PowerTierAmounts gain = TuningPresets.SuccessGainFor(t, level);
            PowerTierAmounts cost = TuningPresets.OverrideCostFor(t, level);

            Assert.True(cost.Minor > gain.Minor, level + ": minor override must cost more than it pays.");
            Assert.True(cost.Major > gain.Major, level + ": major override must cost more than it pays.");
            Assert.True(cost.Mandatory > gain.Mandatory,
                        level + ": mandatory override must cost more than it pays.");
        }

        [Theory]
        [InlineData(PowerIntensity.Lenient)]
        [InlineData(PowerIntensity.Default)]
        [InlineData(PowerIntensity.Harsh)]
        public void EveryPowerLevel_KeepsFailureCheaperThanSuccessPays(PowerIntensity level)
        {
            // failureLossRatio exists to guarantee exactly this. At 1 or above, engaging with a story
            // and getting it wrong costs at least what getting it right pays, and not playing becomes
            // strictly the better option — which inverts the premise of the whole packet.
            double ratio = TuningPresets.FailureLossRatioFor(Shipped(), level);

            Assert.True(ratio > 0.0, level + ": a failure that costs nothing makes silence free.");
            Assert.True(ratio < 1.0, level + ": a failure must not cost more than a success pays.");
        }

        [Theory]
        [InlineData(StoryDifficulty.Forgiving)]
        [InlineData(StoryDifficulty.Default)]
        [InlineData(StoryDifficulty.Demanding)]
        public void EveryStoryLevel_KeepsTheThresholdWinnable(StoryDifficulty level)
        {
            // A threshold above eventsPerStory would make a full story unwinnable outright.
            // StoryResolution clamps into [1, scoredCount], so this cannot actually strand a player —
            // but a level whose stated demand is only reachable because something downstream refuses
            // to honour it is a level that means something other than what it says.
            EngineTuning t = Shipped();
            int required = TuningPresets.SuccessThresholdFor(t, level);

            Assert.True(required >= 1, level + ": a story that needs no met slot cannot be failed.");
            Assert.True(required <= t.Stories.EventsPerStory,
                        level + ": the threshold must not exceed the slots a story ships with.");
        }

        [Theory]
        [InlineData(StoryDifficulty.Forgiving)]
        [InlineData(StoryDifficulty.Default)]
        [InlineData(StoryDifficulty.Demanding)]
        public void EveryStoryLevel_LeavesTheEffectScaleItsSeverityHeadroom(StoryDifficulty level)
        {
            // The scale is a FRACTION of each palette entry's cap, and EffectResolver then multiplies
            // by severity and clamps the result back to that cap. Past the point where the top
            // severity reaches 1.0 of the cap, severities collapse into one number and a severity-1
            // minor does exactly as much damage as a severity-5 catastrophe — the defect the shipped
            // scale was derived to avoid. The multiplier is read from EffectResolver rather than
            // written down here, so a retune of effects.severityMagnitudeScale moves this bound with
            // it instead of leaving the test asserting an arithmetic nobody performs any more.
            EngineTuning t = Shipped();
            double scale = TuningPresets.ActiveEffectScaleFor(t, level);
            double atTopSeverity = EffectResolver.ScaleForSeverity(t.Effects, scale, t.Catalog.SeverityMax);

            Assert.True(scale > 0.0, level + ": an active phase that presses on nothing is not a crisis.");
            Assert.True(atTopSeverity <= 1.0,
                        level + ": at severity " + t.Catalog.SeverityMax + " this scales to "
                        + atTopSeverity + " of the palette cap, so the top severities clamp together. "
                        + "Re-derive the ladder rather than raising it.");
        }

        // --- applying is still one-way and idempotent ---------------------------------------------

        [Fact]
        public void ApplyingBothLadders_IsIdempotent()
        {
            // No preset is expressed as a multiplier, so applying the same levels twice must write the
            // same numbers rather than compounding. If one ever becomes relative, this fails first.
            var settings = new AgoraSettings
            {
                PowerIntensity = PowerIntensity.Harsh,
                StoryDifficulty = StoryDifficulty.Forgiving
            };

            EngineTuning once = Shipped();
            EngineTuning twice = Shipped();

            TuningPresets.Apply(once, settings);
            TuningPresets.Apply(twice, settings);
            TuningPresets.Apply(twice, settings);

            Assert.Equal(once.Power.MaxMonthlyGain, twice.Power.MaxMonthlyGain);
            Assert.Equal(once.Power.FailureLossRatio, twice.Power.FailureLossRatio, 12);
            AssertSameTiers(once.Power.SuccessGain, twice.Power.SuccessGain);
            AssertSameTiers(once.Power.OverrideCost, twice.Power.OverrideCost);
            Assert.Equal(once.Stories.SuccessThreshold, twice.Stories.SuccessThreshold);
            Assert.Equal(once.Stories.ActiveEffectScale, twice.Stories.ActiveEffectScale, 12);
        }

        [Fact]
        public void ApplyingALevel_DoesNotAliasTheLadderItCameFrom()
        {
            // PowerTierAmounts is a reference type with internal setters, so writing the preset's own
            // instance onto the live field would leave the two as one object. Nothing mutates a tier
            // table today; the point is that nothing has to notice before it can.
            EngineTuning tuning = Applied(PowerIntensity.Lenient);

            Assert.NotSame(tuning.Power.SuccessGainLenient, tuning.Power.SuccessGain);
            Assert.NotSame(tuning.Power.OverrideCostLenient, tuning.Power.OverrideCost);
            AssertSameTiers(tuning.Power.SuccessGainLenient, tuning.Power.SuccessGain);
        }

        [Fact]
        public void NewSettings_DefaultToDefault()
        {
            var settings = new AgoraSettings();

            Assert.Equal(PowerIntensity.Default, settings.PowerIntensity);
            Assert.Equal(StoryDifficulty.Default, settings.StoryDifficulty);
        }

        [Fact]
        public void Clone_CarriesBothLevels()
        {
            // AgoraSettings.Clone is field-by-field and hand-maintained, so a level added to the
            // contract and forgotten there silently reverts the first time a player changes theme.
            var settings = new AgoraSettings
            {
                PowerIntensity = PowerIntensity.Harsh,
                StoryDifficulty = StoryDifficulty.Forgiving
            };

            AgoraSettings copy = settings.Clone();

            Assert.Equal(PowerIntensity.Harsh, copy.PowerIntensity);
            Assert.Equal(StoryDifficulty.Forgiving, copy.StoryDifficulty);
        }

        // --- helpers -------------------------------------------------------------------------------

        private static bool SameTiers(PowerTierAmounts a, PowerTierAmounts b) =>
            a.Minor == b.Minor && a.Major == b.Major && a.Mandatory == b.Mandatory;

        private static void AssertSameTiers(PowerTierAmounts expected, PowerTierAmounts actual)
        {
            Assert.Equal(expected.Minor, actual.Minor);
            Assert.Equal(expected.Major, actual.Major);
            Assert.Equal(expected.Mandatory, actual.Mandatory);
        }

        /// <summary>Strictly increasing across all three tiers, low through middle to high.</summary>
        private static void AssertTiersOrdered(PowerTierAmounts low, PowerTierAmounts middle,
                                               PowerTierAmounts high, string what)
        {
            Assert.True(low.Minor < middle.Minor && middle.Minor < high.Minor, "minor: " + what);
            Assert.True(low.Major < middle.Major && middle.Major < high.Major, "major: " + what);
            Assert.True(low.Mandatory < middle.Mandatory && middle.Mandatory < high.Mandatory,
                        "mandatory: " + what);
        }
    }
}
