using System.Collections.Generic;

namespace Agora.Core.Engine.Effects
{
    /// <summary>
    /// How bad a palette finding is. Only <see cref="Error"/> makes a validation fail; warnings are
    /// authoring hints (a magnitude so small it will be dropped, a duration of zero, and so on).
    /// </summary>
    public enum EffectValidationSeverity
    {
        Warning = 0,
        Error = 1
    }

    /// <summary>
    /// A machine-readable reason. Catalog validation (packet 11) switches on these rather than
    /// matching message text, so the messages stay free to change.
    /// </summary>
    public enum EffectValidationCode
    {
        None = 0,

        /// <summary>The id is not in the closed registry. There is no such effect.</summary>
        UnknownEffectId = 1,

        /// <summary>The caller declared a scope the palette entry does not have.</summary>
        ScopeMismatch = 2,

        /// <summary>District-scoped, but no district was named and none could be inferred.</summary>
        MissingDistrictId = 3,

        /// <summary>City-scoped, but a district was named. The district is ignored.</summary>
        DistrictIdOnCityEffect = 4,

        /// <summary>NaN or infinity. Never reaches the sink.</summary>
        MagnitudeNotFinite = 5,

        /// <summary>Above the declared cap. The sink would clamp it; the catalog refuses it outright.</summary>
        MagnitudeExceedsCap = 6,

        /// <summary>Below <c>effects.minEffectiveMagnitude</c> — it would be dropped as noise.</summary>
        MagnitudeBelowMinimum = 7,

        NegativeDuration = 8,

        DurationExceedsCap = 9,

        /// <summary>Zero months: the effect would never apply.</summary>
        ZeroDuration = 10,

        /// <summary>The entry names no game modifier, so Agora.Mod has nothing to resolve.</summary>
        MissingModifier = 11,

        /// <summary>The declared fallback is not itself a registry entry.</summary>
        UnknownFallbackEffectId = 12,

        /// <summary>A city effect may not fall back to a district effect, or the reverse.</summary>
        FallbackScopeMismatch = 13,

        /// <summary>The fallback chain revisits an id — the sink would loop forever.</summary>
        FallbackCycle = 14,

        /// <summary>A cap of zero or less, or NaN. An effect that cannot move is not an effect.</summary>
        NonPositiveCap = 15,

        /// <summary>A per-effect cap looser than the global ceiling. The global one still wins.</summary>
        CapExceedsGlobalCap = 16,

        /// <summary>The master switch is off; nothing will be applied.</summary>
        PaletteDisabled = 17,

        /// <summary>In range as authored, but severity scaling pushes it past the cap.</summary>
        MagnitudeExceedsCapAtSeverity = 18,

        /// <summary>The scope has no terminal fallback, so degradation has nowhere to end.</summary>
        MissingTerminalFallback = 19
    }

    /// <summary>One finding about one effect id.</summary>
    public readonly struct EffectValidationIssue
    {
        public EffectValidationCode Code { get; }
        public EffectValidationSeverity Severity { get; }

        /// <summary>The effect the finding is about. Never null; empty when the id itself was missing.</summary>
        public string EffectId { get; }

        /// <summary>Human-readable detail. Diagnostics only — never parse it.</summary>
        public string Message { get; }

        public EffectValidationIssue(EffectValidationCode code, EffectValidationSeverity severity,
                                     string? effectId, string? message)
        {
            Code = code;
            Severity = severity;
            EffectId = effectId ?? "";
            Message = message ?? "";
        }

        public override string ToString() =>
            Severity.ToString() + " " + Code.ToString() + " [" + EffectId + "]: " + Message;
    }

    /// <summary>
    /// The result of checking one effect request, one catalog entry, or the whole registry.
    /// Issues appear in a fixed check order, so two runs over the same input produce byte-identical
    /// diagnostics (non-negotiable #3).
    /// </summary>
    public sealed class EffectValidation
    {
        private static readonly EffectValidationIssue[] NoIssues = new EffectValidationIssue[0];

        /// <summary>A clean result. Shared; the type is immutable.</summary>
        public static readonly EffectValidation Ok = new EffectValidation(NoIssues);

        private readonly IReadOnlyList<EffectValidationIssue> _issues;

        internal EffectValidation(IReadOnlyList<EffectValidationIssue> issues)
        {
            _issues = issues ?? NoIssues;

            bool valid = true;
            for (int i = 0; i < _issues.Count; i++)
            {
                if (_issues[i].Severity == EffectValidationSeverity.Error)
                {
                    valid = false;
                    break;
                }
            }
            IsValid = valid;
        }

        /// <summary>In check order.</summary>
        public IReadOnlyList<EffectValidationIssue> Issues => _issues;

        /// <summary>True when nothing of <see cref="EffectValidationSeverity.Error"/> severity was found.</summary>
        public bool IsValid { get; }

        public bool HasIssues => _issues.Count > 0;

        public bool Has(EffectValidationCode code)
        {
            for (int i = 0; i < _issues.Count; i++)
                if (_issues[i].Code == code) return true;
            return false;
        }

        /// <summary>Every issue rendered one per line, in check order. For logs and test failures.</summary>
        public string Describe()
        {
            if (_issues.Count == 0) return "ok";
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < _issues.Count; i++)
            {
                if (i > 0) sb.Append('\n');
                sb.Append(_issues[i].ToString());
            }
            return sb.ToString();
        }
    }

    /// <summary>Accumulates issues in check order. Internal so the issue order stays a palette concern.</summary>
    internal sealed class EffectValidationBuilder
    {
        private List<EffectValidationIssue>? _issues;

        internal void Error(EffectValidationCode code, string? effectId, string message) =>
            Add(new EffectValidationIssue(code, EffectValidationSeverity.Error, effectId, message));

        internal void Warn(EffectValidationCode code, string? effectId, string message) =>
            Add(new EffectValidationIssue(code, EffectValidationSeverity.Warning, effectId, message));

        private void Add(EffectValidationIssue issue)
        {
            if (_issues == null) _issues = new List<EffectValidationIssue>(4);
            _issues.Add(issue);
        }

        internal EffectValidation Build() =>
            _issues == null ? EffectValidation.Ok : new EffectValidation(_issues);
    }

    /// <summary>Invariant-culture number formatting, so diagnostics do not vary by machine locale.</summary>
    internal static class EffectFormat
    {
        internal static string Num(double value) =>
            value.ToString("G17", System.Globalization.CultureInfo.InvariantCulture);

        internal static string Int(int value) =>
            value.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }
}
