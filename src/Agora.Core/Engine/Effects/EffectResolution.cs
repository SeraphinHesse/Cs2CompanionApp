using Agora.Core.Contracts;

namespace Agora.Core.Engine.Effects
{
    /// <summary>What happened to one requested effect on the way to the sink.</summary>
    public enum EffectOutcome
    {
        /// <summary>The requested effect is applied, clamped to its caps.</summary>
        Applied = 0,

        /// <summary>The requested effect was unavailable; something further down its chain is applied.</summary>
        Substituted = 1,

        /// <summary>Nothing is applied. <see cref="EffectResolution.DropReason"/> says why.</summary>
        Dropped = 2,

        /// <summary>Effects are switched off for this save. The politics still happened.</summary>
        Suppressed = 3
    }

    /// <summary>Why a request produced nothing. Diagnostics and the news feed both read this.</summary>
    public enum EffectDropReason
    {
        None = 0,

        /// <summary><c>effects.enabled</c> or the per-save switch is off.</summary>
        PaletteDisabled = 1,

        /// <summary>Not in the closed registry and no terminal fallback could take it.</summary>
        UnknownEffectId = 2,

        /// <summary>Registry scope and request scope disagree and no chain entry fits.</summary>
        ScopeMismatch = 3,

        /// <summary>District-scoped with no district named.</summary>
        MissingDistrictId = 4,

        /// <summary>Smaller than <c>effects.minEffectiveMagnitude</c> — noise, not an effect.</summary>
        MagnitudeBelowMinimum = 5,

        /// <summary>Zero months after clamping.</summary>
        ZeroDuration = 6,

        /// <summary>NaN or infinity. Never reaches the city.</summary>
        NotFinite = 7,

        /// <summary>Every entry in the chain, terminal included, was unavailable.</summary>
        NoAvailableFallback = 8,

        /// <summary>Too many effects already stacked on this modifier (<c>effects.maxStackedPerModifier</c>).</summary>
        StackLimit = 9
    }

    /// <summary>
    /// The engine's answer for one effect request: what will actually be applied, or why nothing will.
    ///
    /// <para>
    /// Immutable and self-describing on purpose — the dashboard shows it, the log records it, and the
    /// determinism suite hashes <see cref="ToDebugString"/> rather than picking fields by hand.
    /// </para>
    /// </summary>
    public readonly struct EffectResolution
    {
        public EffectOutcome Outcome { get; }

        /// <summary>The id the caller asked for, before any degradation. Never null.</summary>
        public string RequestedEffectId { get; }

        /// <summary>The clamped request for the sink. Meaningful only when <see cref="IsApplicable"/>.</summary>
        public EffectRequest Request { get; }

        /// <summary>Game modifier member name of the applied entry, or empty. Agora.Mod resolves it.</summary>
        public string Modifier { get; }

        /// <summary>True when the requested magnitude was outside the cap and got clamped.</summary>
        public bool MagnitudeClamped { get; }

        /// <summary>True when the requested duration was outside the cap and got clamped.</summary>
        public bool DurationClamped { get; }

        /// <summary>Steps walked down the fallback chain. 0 = the requested effect itself.</summary>
        public int FallbackDepth { get; }

        public EffectDropReason DropReason { get; }

        private EffectResolution(EffectOutcome outcome, string requestedEffectId, EffectRequest request,
                                 string modifier, bool magnitudeClamped, bool durationClamped,
                                 int fallbackDepth, EffectDropReason dropReason)
        {
            Outcome = outcome;
            RequestedEffectId = requestedEffectId ?? "";
            Request = request;
            Modifier = modifier ?? "";
            MagnitudeClamped = magnitudeClamped;
            DurationClamped = durationClamped;
            FallbackDepth = fallbackDepth;
            DropReason = dropReason;
        }

        /// <summary>True when <see cref="Request"/> should be handed to the sink.</summary>
        public bool IsApplicable => Outcome == EffectOutcome.Applied || Outcome == EffectOutcome.Substituted;

        internal static EffectResolution Apply(string requestedEffectId, EffectRequest request, string modifier,
                                               bool magnitudeClamped, bool durationClamped, int fallbackDepth)
        {
            EffectOutcome outcome = fallbackDepth == 0 ? EffectOutcome.Applied : EffectOutcome.Substituted;
            return new EffectResolution(outcome, requestedEffectId, request, modifier,
                                        magnitudeClamped, durationClamped, fallbackDepth, EffectDropReason.None);
        }

        internal static EffectResolution Drop(string? requestedEffectId, EffectDropReason reason) =>
            new EffectResolution(EffectOutcome.Dropped, requestedEffectId ?? "", default(EffectRequest), "",
                                 false, false, 0, reason);

        internal static EffectResolution Suppress(string? requestedEffectId) =>
            new EffectResolution(EffectOutcome.Suppressed, requestedEffectId ?? "", default(EffectRequest), "",
                                 false, false, 0, EffectDropReason.PaletteDisabled);

        /// <summary>Same resolution at a different magnitude. Used when stacking scales a group down.</summary>
        internal EffectResolution WithMagnitude(double magnitude)
        {
            if (!IsApplicable) return this;
            var scaled = new EffectRequest(Request.EffectId, Request.Scope, magnitude, Request.DurationMonths,
                                           Request.DistrictId, Request.SourceId);
            return new EffectResolution(Outcome, RequestedEffectId, scaled, Modifier,
                                        MagnitudeClamped, DurationClamped, FallbackDepth, DropReason);
        }

        internal EffectResolution AsDropped(EffectDropReason reason) =>
            new EffectResolution(EffectOutcome.Dropped, RequestedEffectId, default(EffectRequest), "",
                                 MagnitudeClamped, DurationClamped, FallbackDepth, reason);

        /// <summary>
        /// Canonical, culture-invariant one-line form. The determinism suite hashes this, so any field
        /// added to the type must be added here too.
        /// </summary>
        public string ToDebugString()
        {
            var sb = new System.Text.StringBuilder();
            sb.Append(RequestedEffectId).Append('|')
              .Append(Outcome.ToString()).Append('|')
              .Append(DropReason.ToString()).Append('|')
              .Append(EffectFormat.Int(FallbackDepth)).Append('|')
              .Append(MagnitudeClamped ? '1' : '0')
              .Append(DurationClamped ? '1' : '0').Append('|')
              .Append(Modifier).Append('|');

            if (IsApplicable)
            {
                sb.Append(Request.EffectId).Append('|')
                  .Append(Request.Scope.ToString()).Append('|')
                  .Append(Request.DistrictId ?? "").Append('|')
                  .Append(EffectFormat.Num(Request.Magnitude)).Append('|')
                  .Append(EffectFormat.Int(Request.DurationMonths)).Append('|')
                  .Append(Request.SourceId ?? "");
            }
            else
            {
                sb.Append("-|-|-|-|-|-");
            }

            return sb.ToString();
        }

        public override string ToString() => ToDebugString();
    }
}
