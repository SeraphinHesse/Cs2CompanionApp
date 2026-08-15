using System;
using System.IO;
using Agora.Core.Contracts;
using Agora.Core.Tuning;
using Xunit;

namespace Agora.Core.Tests
{
    /// <summary>
    /// The three voter-model levels a player can choose, and how they reach the engine.
    ///
    /// <para>
    /// The rule these tests exist to hold is that <c>Default</c> is not a value. It means "leave the
    /// tuning file alone", which is what lets a shipped coefficient be retuned later and reach every
    /// save that never chose otherwise. A preset table that spelled the default out instead would
    /// freeze whatever number happened to be current the first time the player opened the panel, and
    /// nothing would ever say so.
    /// </para>
    /// </summary>
    public class TuningPresetsTests
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

        // --- Default changes nothing ----------------------------------------------------------------

        [Fact]
        public void AllDefault_LeavesEveryCoefficientAlone()
        {
            EngineTuning baseline = Shipped();
            EngineTuning applied = Shipped();

            TuningPresets.Apply(applied, new AgoraSettings());

            Assert.Equal(baseline.Affinity.SoftmaxTemperature, applied.Affinity.SoftmaxTemperature, 12);
            Assert.Equal(baseline.Affinity.EventModifierWeight, applied.Affinity.EventModifierWeight, 12);
            Assert.Equal(baseline.Parties.AnchoredSpreadSigma, applied.Parties.AnchoredSpreadSigma, 12);
        }

        [Fact]
        public void DefaultLevel_ResolvesToTheFilesOwnValue_NotAPresetKey()
        {
            EngineTuning tuning = Shipped();

            Assert.Equal(tuning.Affinity.SoftmaxTemperature,
                         TuningPresets.SoftmaxTemperatureFor(tuning, VoteSharpness.Default), 12);
            Assert.Equal(tuning.Affinity.EventModifierWeight,
                         TuningPresets.EventModifierWeightFor(tuning, NewsInfluence.Default), 12);
            Assert.Equal(tuning.Parties.AnchoredSpreadSigma,
                         TuningPresets.AnchoredSpreadSigmaFor(tuning, BrandDiscipline.Default), 12);
        }

        // --- Non-default levels land ----------------------------------------------------------------

        [Theory]
        [InlineData(VoteSharpness.Blurred)]
        [InlineData(VoteSharpness.Sharp)]
        public void VoteSharpness_WritesItsPreset(VoteSharpness level)
        {
            EngineTuning tuning = Shipped();
            double expected = TuningPresets.SoftmaxTemperatureFor(tuning, level);

            TuningPresets.Apply(tuning, new AgoraSettings { VoteSharpness = level });

            Assert.Equal(expected, tuning.Affinity.SoftmaxTemperature, 12);
        }

        [Theory]
        [InlineData(NewsInfluence.Muted)]
        [InlineData(NewsInfluence.Loud)]
        public void NewsInfluence_WritesItsPreset(NewsInfluence level)
        {
            EngineTuning tuning = Shipped();
            double expected = TuningPresets.EventModifierWeightFor(tuning, level);

            TuningPresets.Apply(tuning, new AgoraSettings { NewsInfluence = level });

            Assert.Equal(expected, tuning.Affinity.EventModifierWeight, 12);
        }

        [Theory]
        [InlineData(BrandDiscipline.Loose)]
        [InlineData(BrandDiscipline.Locked)]
        public void BrandDiscipline_WritesItsPreset(BrandDiscipline level)
        {
            EngineTuning tuning = Shipped();
            double expected = TuningPresets.AnchoredSpreadSigmaFor(tuning, level);

            TuningPresets.Apply(tuning, new AgoraSettings { BrandDiscipline = level });

            Assert.Equal(expected, tuning.Parties.AnchoredSpreadSigma, 12);
        }

        [Fact]
        public void EachLevel_TouchesOnlyItsOwnCoefficient()
        {
            EngineTuning baseline = Shipped();
            EngineTuning tuning = Shipped();

            TuningPresets.Apply(tuning, new AgoraSettings { VoteSharpness = VoteSharpness.Sharp });

            Assert.NotEqual(baseline.Affinity.SoftmaxTemperature, tuning.Affinity.SoftmaxTemperature);
            Assert.Equal(baseline.Affinity.EventModifierWeight, tuning.Affinity.EventModifierWeight, 12);
            Assert.Equal(baseline.Parties.AnchoredSpreadSigma, tuning.Parties.AnchoredSpreadSigma, 12);
        }

        [Fact]
        public void Apply_IsIdempotent()
        {
            // No preset is expressed as a multiplier, so applying the same level twice must write the
            // same number rather than compounding. If a future preset ever becomes relative, this is
            // the test that fails.
            EngineTuning once = Shipped();
            EngineTuning twice = Shipped();
            var settings = new AgoraSettings
            {
                VoteSharpness = VoteSharpness.Blurred,
                NewsInfluence = NewsInfluence.Loud,
                BrandDiscipline = BrandDiscipline.Locked
            };

            TuningPresets.Apply(once, settings);
            TuningPresets.Apply(twice, settings);
            TuningPresets.Apply(twice, settings);

            Assert.Equal(once.Affinity.SoftmaxTemperature, twice.Affinity.SoftmaxTemperature, 12);
            Assert.Equal(once.Affinity.EventModifierWeight, twice.Affinity.EventModifierWeight, 12);
            Assert.Equal(once.Parties.AnchoredSpreadSigma, twice.Parties.AnchoredSpreadSigma, 12);
        }

        [Fact]
        public void NullArguments_AreNoOps()
        {
            EngineTuning tuning = Shipped();
            double before = tuning.Affinity.SoftmaxTemperature;

            TuningPresets.Apply(tuning, null);
            TuningPresets.Apply(null, new AgoraSettings());

            Assert.Equal(before, tuning.Affinity.SoftmaxTemperature, 12);
        }

        // --- The presets have to be usable ----------------------------------------------------------

        [Fact]
        public void EveryPreset_IsPositiveAndDistinctFromTheDefault()
        {
            // A softmax temperature of zero is the degenerate winner-take-all branch, and a preset
            // that equalled the default would be a level the player can select and see nothing from.
            EngineTuning t = Shipped();

            double blurred = TuningPresets.SoftmaxTemperatureFor(t, VoteSharpness.Blurred);
            double sharp = TuningPresets.SoftmaxTemperatureFor(t, VoteSharpness.Sharp);
            double shipped = t.Affinity.SoftmaxTemperature;

            Assert.True(blurred > 0.0 && sharp > 0.0, "a softmax temperature must be positive");
            Assert.True(blurred > shipped, "Blurred must be flatter than the default");
            Assert.True(sharp < shipped, "Sharp must be more decisive than the default");

            Assert.True(TuningPresets.EventModifierWeightFor(t, NewsInfluence.Muted)
                        < t.Affinity.EventModifierWeight);
            Assert.True(TuningPresets.EventModifierWeightFor(t, NewsInfluence.Loud)
                        > t.Affinity.EventModifierWeight);

            Assert.True(TuningPresets.AnchoredSpreadSigmaFor(t, BrandDiscipline.Locked)
                        < t.Parties.AnchoredSpreadSigma);
            Assert.True(TuningPresets.AnchoredSpreadSigmaFor(t, BrandDiscipline.Loose)
                        > t.Parties.AnchoredSpreadSigma);
        }

        [Fact]
        public void SharpestPreset_StaysAboveThePrThreshold()
        {
            // Sharp is 0.10 rather than lower because a five-district synthetic city puts the weakest
            // party at 5.04% there and 3.03% at 0.06, against electionsPr.thresholdShare of 5%. This
            // pins the reasoning: a sharper preset would need that measurement redone, not just a
            // smaller number typed in.
            EngineTuning t = Shipped();

            Assert.True(TuningPresets.SoftmaxTemperatureFor(t, VoteSharpness.Sharp) >= 0.10,
                        "A sharper preset than 0.10 pushes small parties under the PR threshold; " +
                        "re-measure before lowering it.");
        }

        // --- Settings plumbing ------------------------------------------------------------------------

        [Fact]
        public void Clone_CarriesTheLevels()
        {
            // AgoraSettings.Clone is field-by-field and hand-maintained, so a property added to the
            // contract and forgotten there silently reverts the first time a player changes theme.
            var settings = new AgoraSettings
            {
                VoteSharpness = VoteSharpness.Sharp,
                NewsInfluence = NewsInfluence.Muted,
                BrandDiscipline = BrandDiscipline.Locked
            };

            AgoraSettings copy = settings.Clone();

            Assert.Equal(VoteSharpness.Sharp, copy.VoteSharpness);
            Assert.Equal(NewsInfluence.Muted, copy.NewsInfluence);
            Assert.Equal(BrandDiscipline.Locked, copy.BrandDiscipline);
        }

        [Fact]
        public void NewSettings_DefaultToDefault()
        {
            var settings = new AgoraSettings();

            Assert.Equal(VoteSharpness.Default, settings.VoteSharpness);
            Assert.Equal(NewsInfluence.Default, settings.NewsInfluence);
            Assert.Equal(BrandDiscipline.Default, settings.BrandDiscipline);
        }
    }
}
