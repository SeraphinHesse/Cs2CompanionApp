using System;
using Agora.Core.Contracts;

namespace Agora.Core.Tuning
{
    /// <summary>
    /// Applies a save's player-chosen difficulty levels onto a freshly loaded
    /// <see cref="EngineTuning"/>.
    ///
    /// <para>
    /// Three coefficients are exposed to the player as named levels rather than as numbers:
    /// <see cref="AgoraSettings.VoteSharpness"/>, <see cref="AgoraSettings.NewsInfluence"/> and
    /// <see cref="AgoraSettings.BrandDiscipline"/>. The <b>level</b> is per-save state and lives in
    /// the sidecar (non-negotiable #10); the <b>number</b> each level maps to is content and lives in
    /// <c>data/engine_tuning.json</c> (<c>data/CLAUDE.md</c> rule 4). Neither half is written in C#.
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

        private static void ApplyVoteSharpness(EngineTuning tuning, VoteSharpness level)
        {
            if (level == VoteSharpness.Default) return;
            tuning.Affinity.SoftmaxTemperature = SoftmaxTemperatureFor(tuning, level);
        }

        private static void ApplyNewsInfluence(EngineTuning tuning, NewsInfluence level)
        {
            if (level == NewsInfluence.Default) return;
            tuning.Affinity.EventModifierWeight = EventModifierWeightFor(tuning, level);
        }

        private static void ApplyBrandDiscipline(EngineTuning tuning, BrandDiscipline level)
        {
            if (level == BrandDiscipline.Default) return;
            tuning.Parties.AnchoredSpreadSigma = AnchoredSpreadSigmaFor(tuning, level);
        }
    }
}
