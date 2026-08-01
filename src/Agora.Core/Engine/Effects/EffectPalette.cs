using System;
using System.Collections.Generic;
using Agora.Core.Contracts;
using Agora.Core.Tuning;

namespace Agora.Core.Engine.Effects
{
    /// <summary>
    /// Asks whether an effect can actually be applied right now. Agora.Mod supplies one that resolves
    /// the entry's <see cref="EffectCap.Modifier"/> against <c>Game.Areas.DistrictModifierType</c> /
    /// <c>Game.City.CityModifierType</c>; a null check means "everything in the registry is available",
    /// which is what the tests and the headless harness use.
    /// </summary>
    /// <remarks>
    /// A delegate rather than an interface on purpose: Core stays dependency-light, and the enum
    /// lookup that would drag <c>Game.*</c> into this assembly happens on the far side of it.
    /// </remarks>
    public delegate bool EffectAvailabilityCheck(string effectId);

    /// <summary>
    /// Packet 14 — the Core side of the sanctioned effect palette (<c>politicsmodplan.md</c> §7).
    ///
    /// <para>
    /// The palette is a <b>closed registry</b>: an effect id that is not in
    /// <c>effects.perEffect</c> does not exist. Events and mandates name effects by id and can never
    /// reach past this list. Every entry declares scope, magnitude cap, duration cap and a fallback,
    /// and the two terminal entries (<c>district-wellbeing</c>, <c>city-tax-happiness</c>) end every
    /// degradation chain (non-negotiable #5).
    /// </para>
    ///
    /// <para>
    /// This type is a read-only view over <see cref="EffectsTuning"/>. It holds no mutable state, it
    /// makes no random draws, and every list it returns is ordered by a documented key — so two runs
    /// over the same tuning produce byte-identical answers (non-negotiable #3).
    /// </para>
    /// </summary>
    public sealed class EffectPalette
    {
        private readonly EffectsTuning _t;
        private readonly List<string> _cityIds;
        private readonly List<string> _districtIds;

        public EffectPalette(EffectsTuning effects)
        {
            if (effects == null) throw new ArgumentNullException(nameof(effects));
            _t = effects;

            // EffectIds is already sorted ordinal ascending, so both scope lists inherit that order.
            IReadOnlyList<string> ids = _t.EffectIds;
            _cityIds = new List<string>(ids.Count);
            _districtIds = new List<string>(ids.Count);

            for (int i = 0; i < ids.Count; i++)
            {
                EffectCap cap;
                if (!_t.TryGetEffect(ids[i], out cap)) continue;
                if (cap.Scope == EffectScope.District) _districtIds.Add(ids[i]);
                else _cityIds.Add(ids[i]);
            }
        }

        public static EffectPalette From(EngineTuning tuning)
        {
            if (tuning == null) throw new ArgumentNullException(nameof(tuning));
            return new EffectPalette(tuning.Effects);
        }

        /// <summary>The tuning section behind this palette. Read-only; do not cache values off it.</summary>
        public EffectsTuning Tuning => _t;

        /// <summary>Master switch (<c>effects.enabled</c>). False computes politics but applies nothing.</summary>
        public bool Enabled => _t.Enabled;

        /// <summary>Every registered id, sorted ordinal ascending.</summary>
        public IReadOnlyList<string> Ids => _t.EffectIds;

        public int Count => _t.EffectIds.Count;

        /// <summary>City-scoped ids, sorted ordinal ascending.</summary>
        public IReadOnlyList<string> CityIds => _cityIds;

        /// <summary>District-scoped ids, sorted ordinal ascending.</summary>
        public IReadOnlyList<string> DistrictIds => _districtIds;

        public IReadOnlyList<string> IdsForScope(EffectScope scope) =>
            scope == EffectScope.District ? (IReadOnlyList<string>)_districtIds : _cityIds;

        // --- Lookup ---------------------------------------------------------------------------

        public bool Contains(string? effectId)
        {
            EffectCap ignored;
            return TryGetCap(effectId, out ignored);
        }

        /// <summary>The declaration for an id, or false when the id is not in the registry.</summary>
        public bool TryGetCap(string? effectId, out EffectCap cap)
        {
            if (string.IsNullOrEmpty(effectId))
            {
                cap = default(EffectCap);
                return false;
            }
            return _t.TryGetEffect(effectId!, out cap);
        }

        /// <summary>
        /// The declaration for an id, or a conservative one at the global caps when the id is unknown.
        /// Never returns something uncapped.
        /// </summary>
        public EffectCap CapFor(string? effectId, EffectScope scope)
        {
            string id = string.IsNullOrEmpty(effectId) ? TerminalFallbackId(scope) : effectId!;
            return _t.CapFor(id, scope);
        }

        /// <summary>
        /// The game modifier member name an id drives, or empty for an unknown id. Core keeps this a
        /// string; Agora.Mod does the enum lookup. That is what keeps the palette declarable in data.
        /// </summary>
        public string ModifierFor(string? effectId)
        {
            EffectCap cap;
            return TryGetCap(effectId, out cap) ? cap.Modifier : "";
        }

        public bool TryGetScope(string? effectId, out EffectScope scope)
        {
            EffectCap cap;
            if (TryGetCap(effectId, out cap))
            {
                scope = cap.Scope;
                return true;
            }
            scope = EffectScope.City;
            return false;
        }

        /// <summary>The id every chain in this scope ends at. It is terminal: its own fallback is empty.</summary>
        public string TerminalFallbackId(EffectScope scope) =>
            scope == EffectScope.District ? _t.DefaultFallbackDistrictEffectId : _t.DefaultFallbackCityEffectId;

        /// <summary>True when the id is registered and declares no further fallback.</summary>
        public bool IsTerminal(string? effectId)
        {
            EffectCap cap;
            return TryGetCap(effectId, out cap) && string.IsNullOrEmpty(cap.FallbackEffectId);
        }

        /// <summary>
        /// The degradation chain for an id: the id itself, then each declared fallback, ending at a
        /// terminal entry. Never cut an event for a missing effect — walk this instead (§13.5).
        /// </summary>
        /// <remarks>
        /// An unregistered id yields <c>[id, terminal-for-scope]</c>, so even a typo in a catalog still
        /// lands on something capped rather than silently doing nothing. The walk is bounded by the
        /// registry size and stops on the first repeat, so a mis-authored cycle cannot hang the sink.
        /// </remarks>
        public IReadOnlyList<string> FallbackChain(string? effectId, EffectScope scope)
        {
            var chain = new List<string>(4);
            string current = effectId ?? "";
            chain.Add(current);

            int guard = Count + 2;
            for (int step = 0; step < guard; step++)
            {
                string next;
                EffectCap cap;
                if (TryGetCap(current, out cap))
                {
                    if (string.IsNullOrEmpty(cap.FallbackEffectId)) break; // terminal
                    next = cap.FallbackEffectId;
                }
                else
                {
                    // Unknown id: jump straight to the terminal for the requested scope.
                    next = TerminalFallbackId(scope);
                    if (string.IsNullOrEmpty(next)) break;
                }

                if (ChainContains(chain, next)) break; // cycle or self-reference; stop where we are
                chain.Add(next);
                current = next;
            }

            return chain;
        }

        private static bool ChainContains(List<string> list, string value)
        {
            for (int i = 0; i < list.Count; i++)
                if (string.Equals(list[i], value, StringComparison.Ordinal)) return true;
            return false;
        }

        // --- Caps -----------------------------------------------------------------------------

        /// <summary>
        /// The magnitude ceiling actually enforced: the tighter of the per-effect cap and
        /// <c>effects.globalMagnitudeCap</c>. Belt and braces — a mis-authored per-effect cap cannot
        /// widen the palette past the global ceiling.
        /// </summary>
        public double EffectiveMagnitudeCap(EffectCap cap)
        {
            double perEffect = Math.Abs(cap.MagnitudeCap);
            double global = Math.Abs(_t.GlobalMagnitudeCap);
            if (double.IsNaN(perEffect)) perEffect = 0.0;
            if (double.IsNaN(global)) global = 0.0;
            double limit = perEffect < global ? perEffect : global;
            return limit < 0.0 ? 0.0 : limit;
        }

        /// <summary>The tighter of the per-effect duration cap and <c>effects.globalDurationCapMonths</c>.</summary>
        public int EffectiveDurationCapMonths(EffectCap cap)
        {
            int limit = cap.DurationCapMonths < _t.GlobalDurationCapMonths
                ? cap.DurationCapMonths
                : _t.GlobalDurationCapMonths;
            return limit < 0 ? 0 : limit;
        }

        /// <summary>
        /// Clamps into <c>[-EffectiveMagnitudeCap, +EffectiveMagnitudeCap]</c>. NaN becomes zero:
        /// a request the engine could not compute must not reach the city as a garbage number.
        /// </summary>
        public double ClampMagnitude(EffectCap cap, double requested)
        {
            if (double.IsNaN(requested)) return 0.0;
            double limit = EffectiveMagnitudeCap(cap);
            if (requested > limit) return limit;
            if (requested < -limit) return -limit;
            return requested;
        }

        /// <summary>Clamps into <c>[0, EffectiveDurationCapMonths]</c>.</summary>
        public int ClampDuration(EffectCap cap, int requested)
        {
            if (requested < 0) return 0;
            int limit = EffectiveDurationCapMonths(cap);
            return requested > limit ? limit : requested;
        }

        /// <summary>Magnitudes this small are dropped rather than applied as noise.</summary>
        public bool IsBelowMinimum(double magnitude)
        {
            if (double.IsNaN(magnitude)) return true;
            double floor = Math.Abs(_t.MinEffectiveMagnitude);
            return Math.Abs(magnitude) < floor;
        }

        // --- Validation (packet 11 validates catalogs against this) -----------------------------

        /// <summary>
        /// Checks one authored effect reference. Errors are load-blocking; warnings are hints.
        /// </summary>
        /// <remarks>
        /// A null <paramref name="districtId"/> on a district-scoped effect is <i>not</i> a finding:
        /// catalog entries never name a district (real history does not know the player's district
        /// names) and the scheduler fills it in deterministically at fire time. Use
        /// <see cref="ValidateRequest"/> once a target has been chosen.
        /// </remarks>
        public EffectValidation Validate(string? effectId, EffectScope scope, double magnitude,
                                         int durationMonths, string? districtId)
        {
            var b = new EffectValidationBuilder();
            EffectCap cap;

            if (string.IsNullOrEmpty(effectId))
            {
                b.Error(EffectValidationCode.UnknownEffectId, effectId, "Effect id is empty.");
                return b.Build();
            }

            if (!TryGetCap(effectId, out cap))
            {
                b.Error(EffectValidationCode.UnknownEffectId, effectId,
                    "Not a member of the closed effect registry (effects.perEffect).");
                return b.Build();
            }

            if (cap.Scope != scope)
            {
                b.Error(EffectValidationCode.ScopeMismatch, effectId,
                    "Declared scope " + scope + " but the registry says " + cap.Scope + ".");
            }

            if (cap.Scope == EffectScope.City && !string.IsNullOrEmpty(districtId))
            {
                b.Warn(EffectValidationCode.DistrictIdOnCityEffect, effectId,
                    "City-scoped effect names district '" + districtId + "'; it will be ignored.");
            }

            if (string.IsNullOrEmpty(cap.Modifier))
            {
                b.Warn(EffectValidationCode.MissingModifier, effectId,
                    "Registry entry names no game modifier, so nothing can be applied.");
            }

            if (double.IsNaN(magnitude) || double.IsInfinity(magnitude))
            {
                b.Error(EffectValidationCode.MagnitudeNotFinite, effectId, "Magnitude is not a finite number.");
            }
            else
            {
                double limit = EffectiveMagnitudeCap(cap);
                if (Math.Abs(magnitude) > limit)
                {
                    b.Error(EffectValidationCode.MagnitudeExceedsCap, effectId,
                        "Magnitude " + EffectFormat.Num(magnitude) + " exceeds cap " + EffectFormat.Num(limit) + ".");
                }
                else if (IsBelowMinimum(magnitude))
                {
                    b.Warn(EffectValidationCode.MagnitudeBelowMinimum, effectId,
                        "Magnitude " + EffectFormat.Num(magnitude) + " is below effects.minEffectiveMagnitude and will be dropped.");
                }
            }

            if (durationMonths < 0)
            {
                b.Error(EffectValidationCode.NegativeDuration, effectId,
                    "Duration " + EffectFormat.Int(durationMonths) + " months is negative.");
            }
            else if (durationMonths == 0)
            {
                b.Warn(EffectValidationCode.ZeroDuration, effectId, "Duration is zero months; the effect never applies.");
            }
            else
            {
                int durationLimit = EffectiveDurationCapMonths(cap);
                if (durationMonths > durationLimit)
                {
                    b.Error(EffectValidationCode.DurationExceedsCap, effectId,
                        "Duration " + EffectFormat.Int(durationMonths) + " months exceeds cap "
                        + EffectFormat.Int(durationLimit) + ".");
                }
            }

            return b.Build();
        }

        /// <summary>Checks one catalog effect entry as authored.</summary>
        public EffectValidation Validate(TimelineEventEffect effect) =>
            Validate(effect.EffectId, effect.Scope, effect.Magnitude, effect.DurationMonths, effect.DistrictId);

        /// <summary>
        /// Checks one catalog effect entry as it would fire at the given event severity. Severity
        /// scaling can push an in-range magnitude past the cap; that is a warning, not an error,
        /// because the sink clamps it — but an author usually wants to know.
        /// </summary>
        public EffectValidation Validate(TimelineEventEffect effect, int severity)
        {
            EffectValidation baseline = Validate(effect);

            EffectCap cap;
            if (!TryGetCap(effect.EffectId, out cap)) return baseline;
            if (double.IsNaN(effect.Magnitude) || double.IsInfinity(effect.Magnitude)) return baseline;

            double scaled = EffectResolver.ScaleForSeverity(_t, effect.Magnitude, severity);
            double limit = EffectiveMagnitudeCap(cap);
            if (Math.Abs(scaled) <= limit) return baseline;

            var issues = new List<EffectValidationIssue>(baseline.Issues.Count + 1);
            for (int i = 0; i < baseline.Issues.Count; i++) issues.Add(baseline.Issues[i]);
            issues.Add(new EffectValidationIssue(
                EffectValidationCode.MagnitudeExceedsCapAtSeverity,
                EffectValidationSeverity.Warning,
                effect.EffectId,
                "At severity " + EffectFormat.Int(severity) + " the magnitude scales to "
                + EffectFormat.Num(scaled) + ", above cap " + EffectFormat.Num(limit) + "; it will be clamped."));
            return new EffectValidation(issues);
        }

        /// <summary>Checks a request that already names its target. District scope requires a district.</summary>
        public EffectValidation ValidateRequest(EffectRequest request)
        {
            EffectValidation baseline = Validate(request.EffectId, request.Scope, request.Magnitude,
                                                 request.DurationMonths, request.DistrictId);

            if (request.Scope != EffectScope.District || !string.IsNullOrEmpty(request.DistrictId))
                return baseline;

            var issues = new List<EffectValidationIssue>(baseline.Issues.Count + 1);
            for (int i = 0; i < baseline.Issues.Count; i++) issues.Add(baseline.Issues[i]);
            issues.Add(new EffectValidationIssue(
                EffectValidationCode.MissingDistrictId,
                EffectValidationSeverity.Error,
                request.EffectId,
                "District-scoped request names no district."));
            return new EffectValidation(issues);
        }

        /// <summary>
        /// Self-check of the whole registry: every cap positive and inside the global ceiling, every
        /// modifier named, every fallback registered, same-scoped and terminating. Runs in the test
        /// suite so a bad <c>engine_tuning.json</c> fails the build rather than the save.
        /// </summary>
        public EffectValidation ValidateRegistry()
        {
            var b = new EffectValidationBuilder();

            if (!_t.Enabled)
                b.Warn(EffectValidationCode.PaletteDisabled, "", "effects.enabled is false; no effect will be applied.");

            ValidateTerminal(b, EffectScope.City);
            ValidateTerminal(b, EffectScope.District);

            IReadOnlyList<string> ids = Ids; // sorted ordinal ascending
            for (int i = 0; i < ids.Count; i++)
            {
                string id = ids[i];
                EffectCap cap;
                if (!TryGetCap(id, out cap)) continue;

                if (string.IsNullOrEmpty(cap.Modifier))
                    b.Warn(EffectValidationCode.MissingModifier, id, "No game modifier member named.");

                if (double.IsNaN(cap.MagnitudeCap) || cap.MagnitudeCap <= 0.0)
                    b.Error(EffectValidationCode.NonPositiveCap, id,
                        "Magnitude cap " + EffectFormat.Num(cap.MagnitudeCap) + " must be greater than zero.");
                else if (Math.Abs(cap.MagnitudeCap) > Math.Abs(_t.GlobalMagnitudeCap))
                    b.Warn(EffectValidationCode.CapExceedsGlobalCap, id,
                        "Magnitude cap " + EffectFormat.Num(cap.MagnitudeCap) + " is looser than effects.globalMagnitudeCap.");

                if (cap.DurationCapMonths <= 0)
                    b.Error(EffectValidationCode.NonPositiveCap, id,
                        "Duration cap " + EffectFormat.Int(cap.DurationCapMonths) + " months must be greater than zero.");
                else if (cap.DurationCapMonths > _t.GlobalDurationCapMonths)
                    b.Warn(EffectValidationCode.CapExceedsGlobalCap, id,
                        "Duration cap " + EffectFormat.Int(cap.DurationCapMonths) + " months is looser than effects.globalDurationCapMonths.");

                if (string.IsNullOrEmpty(cap.FallbackEffectId)) continue; // terminal

                EffectCap fallback;
                if (!TryGetCap(cap.FallbackEffectId, out fallback))
                {
                    b.Error(EffectValidationCode.UnknownFallbackEffectId, id,
                        "Fallback '" + cap.FallbackEffectId + "' is not in the registry.");
                    continue;
                }

                if (fallback.Scope != cap.Scope)
                    b.Error(EffectValidationCode.FallbackScopeMismatch, id,
                        "Fallback '" + cap.FallbackEffectId + "' is " + fallback.Scope + "-scoped but this entry is " + cap.Scope + ".");

                IReadOnlyList<string> chain = FallbackChain(id, cap.Scope);
                string last = chain[chain.Count - 1];
                if (!IsTerminal(last))
                    b.Error(EffectValidationCode.FallbackCycle, id,
                        "Fallback chain ends at '" + last + "', which is not terminal — the chain loops or dead-ends.");
            }

            return b.Build();
        }

        private void ValidateTerminal(EffectValidationBuilder b, EffectScope scope)
        {
            string terminal = TerminalFallbackId(scope);
            if (string.IsNullOrEmpty(terminal))
            {
                b.Error(EffectValidationCode.MissingTerminalFallback, "",
                    "No terminal fallback declared for " + scope + " scope.");
                return;
            }

            EffectCap cap;
            if (!TryGetCap(terminal, out cap))
            {
                b.Error(EffectValidationCode.MissingTerminalFallback, terminal,
                    "Terminal fallback for " + scope + " scope is not in the registry.");
                return;
            }

            if (cap.Scope != scope)
                b.Error(EffectValidationCode.FallbackScopeMismatch, terminal,
                    "Terminal fallback for " + scope + " scope is declared " + cap.Scope + "-scoped.");

            if (!string.IsNullOrEmpty(cap.FallbackEffectId))
                b.Error(EffectValidationCode.FallbackCycle, terminal,
                    "Terminal fallback declares its own fallback '" + cap.FallbackEffectId + "'; the sink would loop.");
        }

        // AGORA-SEAM(§14 / §7 effect-palette gap): the registry deliberately contains no rent,
        // land-value, RCI-demand, birth-rate or subsidy effect — no game modifier member backs them and
        // the Harmony decision is unresolved. Requests naming one of those resolve through
        // FallbackChain to the scope's terminal entry rather than being invented here. Do not add an
        // unbacked entry to close the gap; that decision goes to Master, not into this file.
    }
}
