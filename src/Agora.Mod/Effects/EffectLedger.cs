// Compiled into BOTH Agora.Mod and (by <Compile Link>) tests/Agora.Core.Tests: it must stay free
// of every Game.*, Unity.* and Colossal.* type. #nullable disable keeps it warning-clean in the
// test project, which enables nullable, without annotating a file the mod compiles unannotated.
#nullable disable

using System;
using System.Collections.Generic;
using System.Globalization;
using Agora.Core.Contracts;
using Agora.Core.Engine.Effects;
using Agora.Core.Tuning;

namespace Agora.Mod.Effects
{
    /// <summary>
    /// Whether a palette entry's modifier name resolves to a real member of
    /// <c>Game.Areas.DistrictModifierType</c> / <c>Game.City.CityModifierType</c> on this build.
    /// </summary>
    /// <remarks>
    /// A delegate rather than a direct call into <c>ModifierRegistry</c>, for the same reason
    /// Core uses <c>EffectAvailabilityCheck</c>: it keeps the enum lookup — and therefore every
    /// <c>Game.*</c> reference — on the far side of the seam, so the ledger's arithmetic is checkable
    /// on a machine with no copy of the game.
    /// </remarks>
    public delegate bool ModifierMappingCheck(EffectScope scope, string modifierName);

    /// <summary>Why the sink refused a request. Reported, never silently swallowed.</summary>
    public enum EffectAdmission
    {
        Accepted = 0,
        PaletteDisabled = 1,
        UnknownEffectId = 2,
        NoModifierMapping = 3,
        MissingDistrictId = 4,
        MagnitudeBelowMinimum = 5,
        ZeroDuration = 6,
        NotFinite = 7
    }

    /// <summary>One live effect Agora has taken responsibility for until it expires.</summary>
    public sealed class EffectLedgerEntry
    {
        public string EffectId { get; set; } = "";
        public EffectScope Scope { get; set; }

        /// <summary>Target district, or empty for city scope.</summary>
        public string DistrictId { get; set; } = "";

        /// <summary>Event or mandate that caused this. Empty when the caller named none.</summary>
        public string SourceId { get; set; } = "";

        /// <summary>Game modifier member name, already known to resolve.</summary>
        public string Modifier { get; set; } = "";

        /// <summary>Signed magnitude at full strength, already clamped to the entry's cap.</summary>
        public double Magnitude { get; set; }

        public SimDate Start { get; set; }
        public int DurationMonths { get; set; }

        /// <summary>Admission order. Ties in every sort break on this, so ordering is total.</summary>
        public long Sequence { get; set; }

        /// <summary>
        /// Separator for composite keys. The ASCII unit separator cannot occur in an effect id, a
        /// modifier name or a district name, so no two different field triples can collide into the
        /// same key by concatenation.
        /// </summary>
        internal const string Sep = "\u001f";

        /// <summary>Identity for replacement: one live entry per effect, target and cause.</summary>
        public string IdentityKey
        {
            get { return EffectId + Sep + DistrictId + Sep + SourceId; }
        }

        /// <summary>The slot this entry lands in: scope, target and modifier.</summary>
        public string SlotKey
        {
            get { return ((int)Scope).ToString(CultureInfo.InvariantCulture) + Sep + DistrictId + Sep + Modifier; }
        }

        public override string ToString()
        {
            return EffectId + " (" + Modifier + ") "
                 + Magnitude.ToString("0.###", CultureInfo.InvariantCulture)
                 + " for " + DurationMonths.ToString(CultureInfo.InvariantCulture) + "m from " + Start;
        }
    }

    /// <summary>One modifier slot's total Agora contribution at a point in time.</summary>
    public readonly struct ModifierAggregate
    {
        public EffectScope Scope { get; }

        /// <summary>Target district, or empty for city scope.</summary>
        public string DistrictId { get; }

        public string Modifier { get; }

        /// <summary>Signed, decayed, stack-limited and capped.</summary>
        public double Magnitude { get; }

        /// <summary>How many live effects contributed.</summary>
        public int Contributors { get; }

        /// <summary>
        /// True when at least one contributor started in an earlier month, i.e. Agora was already
        /// driving this slot before now. The writer needs this to tell "apply for the first time"
        /// apart from "this slot came back from a save still carrying our contribution".
        /// </summary>
        public bool IsCarriedOver { get; }

        public ModifierAggregate(EffectScope scope, string districtId, string modifier,
                                 double magnitude, int contributors, bool isCarriedOver)
        {
            Scope = scope;
            DistrictId = districtId ?? "";
            Modifier = modifier ?? "";
            Magnitude = magnitude;
            Contributors = contributors;
            IsCarriedOver = isCarriedOver;
        }
    }

    /// <summary>
    /// What Agora is currently doing to the city, and for how much longer.
    ///
    /// <para>
    /// Core's <see cref="EffectDispatcher"/> hands the sink one request at a time and then forgets
    /// about it. Somebody has to remember that a request made in March 1994 for eighteen months is
    /// still live in July 1995, decay it on the declared curve, and stop it on time. That is this
    /// class. It is also the layer that re-applies the caps: Core clamps on the way out, this clamps
    /// on the way in, and the writer clamps the aggregate once more on the way to the buffer
    /// (non-negotiable #5 — defence in depth, because the next layer is the actual city).
    /// </para>
    /// </summary>
    /// <remarks>
    /// Pure: no <c>Game.*</c>, no <c>Unity.*</c>, no clock of its own, no randomness. Every list it
    /// returns is sorted by a total key, so two identical histories produce identical aggregates.
    /// </remarks>
    public sealed class EffectLedger
    {
        private readonly EffectPalette _palette;
        private readonly ModifierMappingCheck _mapping;
        private readonly List<EffectLedgerEntry> _entries = new List<EffectLedgerEntry>();
        private long _sequence;

        /// <param name="mapping">
        /// Whether a palette entry's modifier name resolves to a real game enum member.
        /// <c>AgoraEffects</c> passes <c>ModifierRegistry.TryResolve</c>; a null check
        /// means "everything in the registry maps", which is what a build with no game installed uses.
        /// The delegate is what keeps this file free of every <c>Game.*</c> type.
        /// </param>
        public EffectLedger(EffectPalette palette, ModifierMappingCheck mapping = null)
        {
            if (palette == null) throw new ArgumentNullException("palette");
            _palette = palette;
            _mapping = mapping;
        }

        public EffectPalette Palette
        {
            get { return _palette; }
        }

        public int Count
        {
            get { return _entries.Count; }
        }

        /// <summary>
        /// Admits one request. Clamps magnitude and duration to the palette's declared caps, drops
        /// anything the registry does not back, and replaces any live entry with the same effect,
        /// target and cause rather than letting a repeated event stack on itself.
        /// </summary>
        public EffectAdmission Add(EffectRequest request, SimDate now)
        {
            if (!_palette.Enabled) return EffectAdmission.PaletteDisabled;

            string effectId = request.EffectId ?? "";
            EffectCap cap;
            if (!_palette.TryGetCap(effectId, out cap)) return EffectAdmission.UnknownEffectId;

            // Section 7 is a closed registry: an entry whose modifier this build cannot resolve is
            // dropped and reported, never approximated with a different modifier.
            if (_mapping != null && !_mapping(cap.Scope, cap.Modifier))
                return EffectAdmission.NoModifierMapping;

            string districtId = cap.Scope == EffectScope.District ? (request.DistrictId ?? "") : "";
            if (cap.Scope == EffectScope.District && districtId.Length == 0)
                return EffectAdmission.MissingDistrictId;

            if (double.IsNaN(request.Magnitude) || double.IsInfinity(request.Magnitude))
                return EffectAdmission.NotFinite;

            double magnitude = _palette.ClampMagnitude(cap, request.Magnitude);
            if (_palette.IsBelowMinimum(magnitude)) return EffectAdmission.MagnitudeBelowMinimum;

            int duration = _palette.ClampDuration(cap, request.DurationMonths);
            if (duration <= 0) return EffectAdmission.ZeroDuration;

            var entry = new EffectLedgerEntry
            {
                EffectId = effectId,
                Scope = cap.Scope,
                DistrictId = districtId,
                SourceId = request.SourceId ?? "",
                Modifier = cap.Modifier,
                Magnitude = magnitude,
                Start = now,
                DurationMonths = duration,
                Sequence = _sequence++
            };

            string identity = entry.IdentityKey;
            for (int i = 0; i < _entries.Count; i++)
            {
                if (!string.Equals(_entries[i].IdentityKey, identity, StringComparison.Ordinal)) continue;
                // Re-issued: refresh in place, keeping the original admission order so the sort is stable.
                entry.Sequence = _entries[i].Sequence;
                _entries[i] = entry;
                return EffectAdmission.Accepted;
            }

            _entries.Add(entry);
            return EffectAdmission.Accepted;
        }

        /// <summary>Removes everything that has run out. Returns how many were removed.</summary>
        public int PruneExpired(SimDate now)
        {
            int removed = 0;
            for (int i = _entries.Count - 1; i >= 0; i--)
            {
                EffectLedgerEntry e = _entries[i];
                if (EffectSchedule.IsActive(e.Start, e.DurationMonths, now)) continue;
                _entries.RemoveAt(i);
                removed++;
            }
            return removed;
        }

        public void Clear()
        {
            _entries.Clear();
        }

        /// <summary>Every entry, live or not, sorted by the total key. For the sidecar and the dashboard.</summary>
        public IReadOnlyList<EffectLedgerEntry> Snapshot()
        {
            var copy = new List<EffectLedgerEntry>(_entries);
            copy.Sort(CompareEntries);
            return copy;
        }

        /// <summary>
        /// What every modifier slot should read right now: decayed, stack-limited, capped, and sorted
        /// by (scope, district, modifier) so the write order never depends on a hash table.
        /// </summary>
        public IReadOnlyList<ModifierAggregate> Aggregate(SimDate now)
        {
            var live = new List<EffectLedgerEntry>(_entries.Count);
            var decayed = new List<double>(_entries.Count);

            for (int i = 0; i < _entries.Count; i++)
            {
                EffectLedgerEntry e = _entries[i];
                double value = EffectSchedule.MagnitudeAt(_palette.Tuning, e.Magnitude, e.Start, e.DurationMonths, now);
                if (_palette.IsBelowMinimum(value)) continue;
                live.Add(e);
                decayed.Add(value);
            }

            // Sort both lists together by the total entry key. Selection over an index list keeps the
            // two arrays aligned without allocating a tuple per entry.
            var order = new List<int>(live.Count);
            for (int i = 0; i < live.Count; i++) order.Add(i);
            order.Sort(delegate (int a, int b) { return CompareEntries(live[a], live[b]); });

            int maxStacked = _palette.Tuning.MaxStackedPerModifier;
            if (maxStacked < 1) maxStacked = 1;
            bool maxMode = string.Equals(_palette.Tuning.StackingMode, "max", StringComparison.Ordinal);

            var results = new List<ModifierAggregate>();
            int index = 0;
            while (index < order.Count)
            {
                string slot = live[order[index]].SlotKey;
                int end = index;
                while (end < order.Count && string.Equals(live[order[end]].SlotKey, slot, StringComparison.Ordinal)) end++;

                results.Add(Fold(live, decayed, order, index, end, maxStacked, maxMode, now));
                index = end;
            }

            return results;
        }

        private ModifierAggregate Fold(List<EffectLedgerEntry> live, List<double> decayed, List<int> order,
                                       int start, int end, int maxStacked, bool maxMode, SimDate now)
        {
            EffectLedgerEntry first = live[order[start]];

            // Strongest first, then the total key. Total, so an unstable sort cannot change the answer.
            var group = new List<int>(end - start);
            for (int i = start; i < end; i++) group.Add(order[i]);
            group.Sort(delegate (int a, int b)
            {
                int byMagnitude = Math.Abs(decayed[b]).CompareTo(Math.Abs(decayed[a]));
                return byMagnitude != 0 ? byMagnitude : CompareEntries(live[a], live[b]);
            });

            int keep = maxMode ? 1 : (group.Count < maxStacked ? group.Count : maxStacked);

            double total = 0.0;
            double groupCap = Math.Abs(_palette.Tuning.GlobalMagnitudeCap);
            bool carriedOver = false;
            for (int i = 0; i < keep; i++)
            {
                int idx = group[i];
                total += decayed[idx];
                if (EffectSchedule.ElapsedMonths(live[idx].Start, now) > 0) carriedOver = true;

                EffectCap cap;
                if (!_palette.TryGetCap(live[idx].EffectId, out cap)) continue;
                double limit = _palette.EffectiveMagnitudeCap(cap);
                if (limit < groupCap) groupCap = limit;
            }

            // The group total may never exceed the tightest cap contributing to it. Whether the game
            // stacks additively or multiplicatively is now known (both, per lane) but a coalition of
            // small effects must still not add up to a large one.
            if (total > groupCap) total = groupCap;
            else if (total < -groupCap) total = -groupCap;

            return new ModifierAggregate(first.Scope, first.DistrictId, first.Modifier, total, keep, carriedOver);
        }

        /// <summary>
        /// Total order over entries: scope, district, modifier, effect id, source id, admission order.
        /// Nothing here reads a dictionary and nothing here is culture-sensitive.
        /// </summary>
        private static int CompareEntries(EffectLedgerEntry a, EffectLedgerEntry b)
        {
            int byScope = ((int)a.Scope).CompareTo((int)b.Scope);
            if (byScope != 0) return byScope;

            int byDistrict = string.CompareOrdinal(a.DistrictId, b.DistrictId);
            if (byDistrict != 0) return byDistrict;

            int byModifier = string.CompareOrdinal(a.Modifier, b.Modifier);
            if (byModifier != 0) return byModifier;

            int byEffect = string.CompareOrdinal(a.EffectId, b.EffectId);
            if (byEffect != 0) return byEffect;

            int bySource = string.CompareOrdinal(a.SourceId, b.SourceId);
            if (bySource != 0) return bySource;

            return a.Sequence.CompareTo(b.Sequence);
        }
    }
}
