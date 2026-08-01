using System;
using System.Collections.Generic;
using Agora.Core.Contracts;
using Agora.Core.Engine.Effects;

namespace Agora.Mod.Effects
{
    /// <summary>
    /// The <see cref="IEffectSink"/> the engine talks to. It does not touch the ECS world — it admits
    /// requests to the <see cref="EffectLedger"/> and lets
    /// <see cref="AgoraEffectApplicationSystem"/> write them out on the simulation thread.
    ///
    /// <para>
    /// Two jobs. First, <b>enforce the caps again</b>: Core already clamped on the way out, and this
    /// clamps on the way in, because this is the layer that actually reaches the city and a cap that
    /// only exists upstream is a cap one refactor away from disappearing (non-negotiable #5).
    /// Second, <b>report and drop</b> anything §7's closed registry does not back — never substitute
    /// a different modifier, never invent one.
    /// </para>
    /// </summary>
    /// <remarks>
    /// Every rejection is counted and every distinct rejected effect id is remembered, so the reason
    /// an event produced no visible consequence is answerable from the log rather than by guesswork.
    /// </remarks>
    public sealed class AgoraEffectSink : IEffectSink
    {
        private readonly EffectPalette _palette;
        private readonly EffectLedger _ledger;
        private readonly IClock _clock;

        private readonly Dictionary<string, int> _rejections = new Dictionary<string, int>(StringComparer.Ordinal);
        private readonly List<string> _rejectedIds = new List<string>();

        private int _accepted;
        private int _rejected;
        private bool _clockUnavailableLogged;

        public AgoraEffectSink(EffectPalette palette, EffectLedger ledger, IClock clock)
        {
            if (palette == null) throw new ArgumentNullException("palette");
            if (ledger == null) throw new ArgumentNullException("ledger");
            if (clock == null) throw new ArgumentNullException("clock");

            _palette = palette;
            _ledger = ledger;
            _clock = clock;
        }

        public EffectLedger Ledger
        {
            get { return _ledger; }
        }

        /// <summary>Requests admitted to the ledger since load.</summary>
        public int AcceptedCount
        {
            get { return _accepted; }
        }

        /// <summary>Requests dropped for any reason since load.</summary>
        public int RejectedCount
        {
            get { return _rejected; }
        }

        /// <summary>
        /// Every distinct effect id that has been dropped, sorted ordinal ascending. This is the
        /// "reported" half of "reported and dropped, never invented".
        /// </summary>
        public IReadOnlyList<string> RejectedEffectIds
        {
            get
            {
                var ids = new List<string>(_rejectedIds);
                ids.Sort(StringComparer.Ordinal);
                return ids;
            }
        }

        public int RejectionCount(string effectId)
        {
            int count;
            return _rejections.TryGetValue(effectId ?? "", out count) ? count : 0;
        }

        public void Apply(EffectRequest request)
        {
            SimDate now;
            if (!TryReadClock(out now)) return;

            EffectAdmission admission = _ledger.Add(request, now);
            Record(request, admission);
        }

        /// <summary>
        /// Same as <see cref="Apply"/> but with the date supplied. The engine tick already knows what
        /// today is, and re-reading the clock per request would be both wasteful and, on a month
        /// boundary, inconsistent within a single batch.
        /// </summary>
        public EffectAdmission Apply(EffectRequest request, SimDate now)
        {
            EffectAdmission admission = _ledger.Add(request, now);
            Record(request, admission);
            return admission;
        }

        /// <summary>Forgets every counter. Called when a save is unloaded.</summary>
        public void Reset()
        {
            _rejections.Clear();
            _rejectedIds.Clear();
            _accepted = 0;
            _rejected = 0;
            _clockUnavailableLogged = false;
        }

        private void Record(EffectRequest request, EffectAdmission admission)
        {
            if (admission == EffectAdmission.Accepted)
            {
                _accepted++;
                return;
            }

            _rejected++;

            string effectId = request.EffectId ?? "";
            int seen;
            if (_rejections.TryGetValue(effectId, out seen))
            {
                _rejections[effectId] = seen + 1;
                return; // already logged once; do not spam a per-month log with the same line
            }

            _rejections[effectId] = 1;
            _rejectedIds.Add(effectId);

            AgoraMod.Log.Warn("effects: dropped '" + effectId + "' (" + admission + ")"
                + DescribeTarget(request) + ". "
                + (admission == EffectAdmission.NoModifierMapping
                    ? "No member of DistrictModifierType/CityModifierType is named '"
                      + _palette.ModifierFor(effectId) + "'; see politicsmodplan.md section 7."
                    : "Nothing was applied."));
        }

        private static string DescribeTarget(EffectRequest request)
        {
            return string.IsNullOrEmpty(request.DistrictId) ? "" : " for district '" + request.DistrictId + "'";
        }

        private bool TryReadClock(out SimDate now)
        {
            try
            {
                now = _clock.Today;
                return true;
            }
            catch (Exception ex)
            {
                // Outside a loaded game the sim clock is unreadable. Failing quietly keeps the main
                // menu clean; the engine has nothing to apply there anyway.
                if (!_clockUnavailableLogged)
                {
                    _clockUnavailableLogged = true;
                    AgoraMod.Log.Debug("effects: sim clock unavailable, request ignored: " + ex.Message);
                }
                now = default(SimDate);
                return false;
            }
        }
    }
}
