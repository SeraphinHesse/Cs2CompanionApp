using System;
using Agora.Core.Contracts;

namespace Agora.Core.Tuning
{
    /// <summary>
    /// Applies a save's player-chosen difficulty levels onto a freshly loaded
    /// <see cref="EngineTuning"/>.
    ///
    /// <para>
    /// Five settings are exposed to the player as named levels rather than as numbers:
    /// <see cref="AgoraSettings.VoteSharpness"/>, <see cref="AgoraSettings.NewsInfluence"/>,
    /// <see cref="AgoraSettings.BrandDiscipline"/>, <see cref="AgoraSettings.PowerIntensity"/> and
    /// <see cref="AgoraSettings.StoryDifficulty"/>. The <b>level</b> is per-save state and lives in
    /// the sidecar (non-negotiable #10); the <b>number</b> each level maps to is content and lives in
    /// <c>data/engine_tuning.json</c> (<c>data/CLAUDE.md</c> rule 4). Neither half is written in C#.
    /// </para>
    ///
    /// <para>
    /// <b>The last two move more than one coefficient each, and that is the difference between them
    /// and the first three.</b> A voter-model level names one number; an economy and a difficulty
    /// name a set of numbers that only make sense together. <c>PowerIntensity</c> writes four —
    /// accrual ceiling, award, loss ratio and override price — because cheapening the price without
    /// trimming the award makes buying a story off the ordinary move, and raising the loss ratio
    /// without raising the award makes engagement cost more than it pays, which is the one property
    /// <c>power.failureLossRatio</c> exists to guarantee. <c>StoryDifficulty</c> writes three — the
    /// slot count a verdict needs, the live-phase effect scale, and the one goal threshold the engine
    /// derives rather than an author. It writes <i>no authored threshold</i>: every check in
    /// <c>events_*.json</c> was hand-derived against the one-month window a story is open for, and a
    /// level that scaled them would rewrite that authoring from a file nobody was editing.
    /// </para>
    ///
    /// <para>
    /// <b>Default is not a value.</b> Every enum's <c>Default</c> member means "leave the tuning file
    /// alone", so it has no preset key and this class no-ops for it. That is what lets the shipped
    /// coefficient be retuned later and reach every save that never chose otherwise — a preset table
    /// that spelled out the default instead would freeze whatever number was current when the player
    /// first opened the settings panel.
    /// </para>
    ///
    /// <para>
    /// <b>Mutates its argument, and must be handed a pristine parse.</b> Applying a level is one-way:
    /// there is nothing here that can put an overridden coefficient back, because the original value
    /// is not retained anywhere. Callers switching a level therefore re-read the tuning file and apply
    /// to the fresh instance, which is why <c>AgoraRuntime</c> pairs every call with
    /// <c>LoadTuning()</c>. Applying twice to the same instance is harmless — the second call writes
    /// the same numbers — but applying a <i>different</i> level to an already-overridden instance
    /// would compound only if a level's preset were expressed as a multiplier, which none is.
    /// </para>
    /// </summary>
    public static class TuningPresets
    {
        /// <summary>
        /// Writes the levels in <paramref name="settings"/> onto <paramref name="tuning"/>. A null
        /// argument on either side is a no-op rather than a throw: a save whose settings failed to
        /// load must still run on the shipped coefficients.
        /// </summary>
        public static void Apply(EngineTuning tuning, AgoraSettings settings)
        {
            if (tuning == null || settings == null) return;

            ApplyVoteSharpness(tuning, settings.VoteSharpness);
            ApplyNewsInfluence(tuning, settings.NewsInfluence);
            ApplyBrandDiscipline(tuning, settings.BrandDiscipline);
            ApplyPowerIntensity(tuning, settings.PowerIntensity);
            ApplyStoryDifficulty(tuning, settings.StoryDifficulty);
        }

        /// <summary>
        /// The coefficient a level resolves to, or the tuning file's own value for
        /// <see cref="VoteSharpness.Default"/>. Public so the settings surface can show the player
        /// what a level means without duplicating the mapping.
        /// </summary>
        public static double SoftmaxTemperatureFor(EngineTuning tuning, VoteSharpness level)
        {
            if (tuning == null) throw new ArgumentNullException(nameof(tuning));

            switch (level)
            {
                case VoteSharpness.Blurred: return tuning.Affinity.SoftmaxTemperatureBlurred;
                case VoteSharpness.Sharp: return tuning.Affinity.SoftmaxTemperatureSharp;
                default: return tuning.Affinity.SoftmaxTemperature;
            }
        }

        public static double EventModifierWeightFor(EngineTuning tuning, NewsInfluence level)
        {
            if (tuning == null) throw new ArgumentNullException(nameof(tuning));

            switch (level)
            {
                case NewsInfluence.Muted: return tuning.Affinity.EventModifierWeightMuted;
                case NewsInfluence.Loud: return tuning.Affinity.EventModifierWeightLoud;
                default: return tuning.Affinity.EventModifierWeight;
            }
        }

        /// <summary>
        /// The story term's weight at one news-influence level. The twin of
        /// <see cref="EventModifierWeightFor"/>, and it exists for the same reason: stories are the
        /// news surface the setting names, so a Muted save must not still swing on story verdicts.
        /// </summary>
        public static double StoryPressureWeightFor(EngineTuning tuning, NewsInfluence level)
        {
            if (tuning == null) throw new ArgumentNullException(nameof(tuning));

            switch (level)
            {
                case NewsInfluence.Muted: return tuning.Affinity.StoryPressureWeightMuted;
                case NewsInfluence.Loud: return tuning.Affinity.StoryPressureWeightLoud;
                default: return tuning.Affinity.StoryPressureWeight;
            }
        }

        public static double AnchoredSpreadSigmaFor(EngineTuning tuning, BrandDiscipline level)
        {
            if (tuning == null) throw new ArgumentNullException(nameof(tuning));

            switch (level)
            {
                case BrandDiscipline.Loose: return tuning.Parties.AnchoredSpreadSigmaLoose;
                case BrandDiscipline.Locked: return tuning.Parties.AnchoredSpreadSigmaLocked;
                default: return tuning.Parties.AnchoredSpreadSigma;
            }
        }

        // --- the power economy ------------------------------------------------------------------

        /// <summary>Ceiling on one month's passive accrual at one intensity level.</summary>
        public static int MaxMonthlyGainFor(EngineTuning tuning, PowerIntensity level)
        {
            if (tuning == null) throw new ArgumentNullException(nameof(tuning));

            switch (level)
            {
                case PowerIntensity.Lenient: return tuning.Power.MaxMonthlyGainLenient;
                case PowerIntensity.Harsh: return tuning.Power.MaxMonthlyGainHarsh;
                default: return tuning.Power.MaxMonthlyGain;
            }
        }

        /// <summary>What a met slot pays, by tier, at one intensity level.</summary>
        public static PowerTierAmounts SuccessGainFor(EngineTuning tuning, PowerIntensity level)
        {
            if (tuning == null) throw new ArgumentNullException(nameof(tuning));

            switch (level)
            {
                case PowerIntensity.Lenient: return tuning.Power.SuccessGainLenient;
                case PowerIntensity.Harsh: return tuning.Power.SuccessGainHarsh;
                default: return tuning.Power.SuccessGain;
            }
        }

        /// <summary>The fraction of a tier's award a not-met slot costs, at one intensity level.</summary>
        public static double FailureLossRatioFor(EngineTuning tuning, PowerIntensity level)
        {
            if (tuning == null) throw new ArgumentNullException(nameof(tuning));

            switch (level)
            {
                case PowerIntensity.Lenient: return tuning.Power.FailureLossRatioLenient;
                case PowerIntensity.Harsh: return tuning.Power.FailureLossRatioHarsh;
                default: return tuning.Power.FailureLossRatio;
            }
        }

        /// <summary>What buying a slot off costs, by tier, at one intensity level.</summary>
        public static PowerTierAmounts OverrideCostFor(EngineTuning tuning, PowerIntensity level)
        {
            if (tuning == null) throw new ArgumentNullException(nameof(tuning));

            switch (level)
            {
                case PowerIntensity.Lenient: return tuning.Power.OverrideCostLenient;
                case PowerIntensity.Harsh: return tuning.Power.OverrideCostHarsh;
                default: return tuning.Power.OverrideCost;
            }
        }

        // --- story difficulty -------------------------------------------------------------------

        /// <summary>Met slots a full story needs for a Success verdict, at one difficulty level.</summary>
        public static int SuccessThresholdFor(EngineTuning tuning, StoryDifficulty level)
        {
            if (tuning == null) throw new ArgumentNullException(nameof(tuning));

            switch (level)
            {
                case StoryDifficulty.Forgiving: return tuning.Stories.SuccessThresholdForgiving;
                case StoryDifficulty.Demanding: return tuning.Stories.SuccessThresholdDemanding;
                default: return tuning.Stories.SuccessThreshold;
            }
        }

        /// <summary>
        /// The live-phase effect scale at one difficulty level — how hard the crisis presses on the
        /// city while the story is still open.
        /// </summary>
        public static double ActiveEffectScaleFor(EngineTuning tuning, StoryDifficulty level)
        {
            if (tuning == null) throw new ArgumentNullException(nameof(tuning));

            switch (level)
            {
                case StoryDifficulty.Forgiving: return tuning.Stories.ActiveEffectScaleForgiving;
                case StoryDifficulty.Demanding: return tuning.Stories.ActiveEffectScaleDemanding;
                default: return tuning.Stories.ActiveEffectScale;
            }
        }

        /// <summary>
        /// The happiness gain a severity-1 generically wrapped timeline event asks for, at one
        /// difficulty level. In points on the 0–100 scale, not a multiplier.
        /// </summary>
        public static double WrappedEventHappinessGoalPointsFor(EngineTuning tuning, StoryDifficulty level)
        {
            if (tuning == null) throw new ArgumentNullException(nameof(tuning));

            switch (level)
            {
                case StoryDifficulty.Forgiving: return tuning.Stories.WrappedEventHappinessGoalPointsForgiving;
                case StoryDifficulty.Demanding: return tuning.Stories.WrappedEventHappinessGoalPointsDemanding;
                default: return tuning.Stories.WrappedEventHappinessGoalPoints;
            }
        }

        private static void ApplyVoteSharpness(EngineTuning tuning, VoteSharpness level)
        {
            if (level == VoteSharpness.Default) return;
            tuning.Affinity.SoftmaxTemperature = SoftmaxTemperatureFor(tuning, level);
        }

        private static void ApplyNewsInfluence(EngineTuning tuning, NewsInfluence level)
        {
            if (level == NewsInfluence.Default) return;
            tuning.Affinity.EventModifierWeight = EventModifierWeightFor(tuning, level);

            // Both, always. One without the other is the setting half-applied: the player turns the
            // news down, the timeline stops moving votes, and the story verdicts keep swinging them
            // at full strength — which reads as the setting not working rather than as a design.
            tuning.Affinity.StoryPressureWeight = StoryPressureWeightFor(tuning, level);
        }

        private static void ApplyBrandDiscipline(EngineTuning tuning, BrandDiscipline level)
        {
            if (level == BrandDiscipline.Default) return;
            tuning.Parties.AnchoredSpreadSigma = AnchoredSpreadSigmaFor(tuning, level);
        }

        private static void ApplyPowerIntensity(EngineTuning tuning, PowerIntensity level)
        {
            if (level == PowerIntensity.Default) return;

            // All four, always. The economy is one system and a half-applied level inverts it: a
            // cheaper override beside an unchanged award makes buying a story off the ordinary move,
            // and a heavier loss ratio beside an unchanged award makes engagement cost more than it
            // pays — the one property failureLossRatio exists to guarantee.
            tuning.Power.MaxMonthlyGain = MaxMonthlyGainFor(tuning, level);
            tuning.Power.FailureLossRatio = FailureLossRatioFor(tuning, level);

            // Copies, not the ladder's own instances. PowerTierAmounts is a reference type with
            // internal setters, so assigning it directly would leave the shipped tier table and the
            // preset it came from as the same object — and a later edit to one would silently be an
            // edit to the other, on a class whose whole job is to be read after it is written.
            tuning.Power.SuccessGain = Copy(SuccessGainFor(tuning, level));
            tuning.Power.OverrideCost = Copy(OverrideCostFor(tuning, level));
        }

        /// <summary>
        /// A detached copy of a tier table. Never null: <c>PowerTierAmounts.Read</c> always builds an
        /// instance and every ladder field carries a compiled default, so there is no parse that
        /// leaves one absent.
        /// </summary>
        private static PowerTierAmounts Copy(PowerTierAmounts source) =>
            new PowerTierAmounts(source.Minor, source.Major, source.Mandatory);

        private static void ApplyStoryDifficulty(EngineTuning tuning, StoryDifficulty level)
        {
            if (level == StoryDifficulty.Default) return;

            tuning.Stories.SuccessThreshold = SuccessThresholdFor(tuning, level);
            tuning.Stories.ActiveEffectScale = ActiveEffectScaleFor(tuning, level);
            tuning.Stories.WrappedEventHappinessGoalPoints =
                WrappedEventHappinessGoalPointsFor(tuning, level);
        }
    }
}
