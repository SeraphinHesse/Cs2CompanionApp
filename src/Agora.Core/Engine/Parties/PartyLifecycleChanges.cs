using System.Collections.Generic;
using Agora.Core.Contracts;

namespace Agora.Core.Engine.Parties
{
    /// <summary>Which way a party's lifecycle turned. One record per dated turn.</summary>
    public enum PartyLifecycleKind
    {
        /// <summary>The brand came into being — a new entry, or a splinter off a parent.</summary>
        Founded = 0,

        /// <summary>The brand died on its own, below threshold across consecutive elections.</summary>
        Dissolved = 1,

        /// <summary>The brand was absorbed into <see cref="Party.SuccessorPartyId"/>.</summary>
        Merged = 2
    }

    /// <summary>One dated turn in one party's life.</summary>
    public sealed class PartyLifecycleRecord
    {
        public PartyLifecycleRecord(string partyId, PartyLifecycleKind kind, SimDate date)
        {
            PartyId = partyId;
            Kind = kind;
            Date = date;
        }

        public string PartyId { get; }

        public PartyLifecycleKind Kind { get; }

        /// <summary>
        /// The date the engine stamped on the turn — <see cref="Party.FoundedDate"/> or
        /// <see cref="Party.DissolvedDate"/>. Never computed here (non-negotiable #8).
        /// </summary>
        public SimDate Date { get; }
    }

    /// <summary>
    /// Everything <see cref="PartyLifecycleChanges.Collect"/> found, plus the dates it deliberately
    /// stayed silent about so the caller can say so in the log.
    /// </summary>
    public sealed class PartyLifecycleChangeSet
    {
        internal PartyLifecycleChangeSet(List<PartyLifecycleRecord> records, List<SimDate> suppressedDates)
        {
            Records = records;
            SuppressedDates = suppressedDates;
        }

        /// <summary>
        /// Sorted ascending by date, then by party id ordinal, then by kind. A total order with no
        /// ties, so the sequence is the same on every reload of the same save.
        /// </summary>
        public IReadOnlyList<PartyLifecycleRecord> Records { get; }

        /// <summary>
        /// Dates on which the whole roster was minted at once and nothing was reported. Ascending,
        /// distinct. Empty in every normal save.
        /// </summary>
        public IReadOnlyList<SimDate> SuppressedDates { get; }
    }

    /// <summary>
    /// Reads the dated lifecycle facts already persisted on <see cref="Party"/> and answers "which
    /// parties turned, and when".
    ///
    /// <para>
    /// A query over persisted state rather than a diff against a cached previous roster, for three
    /// reasons: a diff needs cross-tick state that has to be cleared on every save boundary, a diff
    /// cannot reproduce history after a reload, and the facts are already on the contract. The
    /// dashboard's news feed is history and must survive a reload, so it is rebuilt from this on
    /// every publish exactly as the election and coalition rows are.
    /// </para>
    ///
    /// <para>
    /// Extracted into Agora.Core, and pure, because it is the only part of the news-alert lane a
    /// machine can check: everything else in that path reaches a UI binding and cannot be linked
    /// into a suite that must run with no copy of the game.
    /// </para>
    /// </summary>
    /// <remarks>
    /// <b>A revival erases its own dissolution.</b> <c>PartyLifecycle</c> clears
    /// <see cref="Party.DissolvedDate"/> when a brand returns, so a party that died and came back
    /// reports no dissolution at all — not even the one that really happened. This is accepted, not
    /// overlooked: the alternative is a persisted lifecycle log, which is a sidecar field, and the
    /// alert for the death already fired at the time it happened.
    /// </remarks>
    public static class PartyLifecycleChanges
    {
        private static readonly PartyLifecycleRecord[] NoRecords = new PartyLifecycleRecord[0];

        /// <summary>
        /// Every lifecycle turn in the roster's history, oldest first, with the opening roster
        /// excluded and whole-roster mintings suppressed.
        /// </summary>
        /// <param name="parties">
        /// The save's parties. Order is not read: the result is sorted into a total order of its own,
        /// so a caller that hands these over in a different sequence gets the same answer.
        /// </param>
        /// <param name="startDate">
        /// The save's first political date. Every party the opening roster was minted with carries it
        /// as a founding date, so a founding on or before it is the roster coming into existence and
        /// not a party being founded.
        /// </param>
        public static PartyLifecycleChangeSet Collect(IReadOnlyList<Party>? parties, SimDate startDate)
        {
            var records = new List<PartyLifecycleRecord>();
            var suppressed = new List<SimDate>();
            if (parties == null || parties.Count == 0)
                return new PartyLifecycleChangeSet(new List<PartyLifecycleRecord>(NoRecords), suppressed);

            // Every founding date in the roster, including the ones on or before the start date that
            // never become records. The suppression rule below counts against these, not against
            // parties.Count, so the answer for a given date does not change as the roster grows.
            var foundings = new List<SimDate>(parties.Count);

            for (int i = 0; i < parties.Count; i++)
            {
                Party party = parties[i];
                if (party == null || string.IsNullOrEmpty(party.Id)) continue;

                foundings.Add(party.FoundedDate);

                if (party.FoundedDate > startDate)
                    records.Add(new PartyLifecycleRecord(party.Id, PartyLifecycleKind.Founded,
                                                         party.FoundedDate));

                if (!party.DissolvedDate.HasValue) continue;

                // Status is the discriminator, not the date: both endings stamp DissolvedDate, and a
                // merge into a successor is a different story from a party dying below threshold.
                // Any other status carrying a date is a state the lifecycle does not produce, and
                // inventing a headline for it would be worse than staying quiet.
                if (party.Status == PartyStatus.Merged)
                    records.Add(new PartyLifecycleRecord(party.Id, PartyLifecycleKind.Merged,
                                                         party.DissolvedDate.Value));
                else if (party.Status == PartyStatus.Dissolved)
                    records.Add(new PartyLifecycleRecord(party.Id, PartyLifecycleKind.Dissolved,
                                                         party.DissolvedDate.Value));
            }

            records.Sort(Compare);
            foundings.Sort();

            // Runs of equal dates, over a list already sorted by date — no grouping dictionary, whose
            // enumeration order would decide which date got reported first.
            //
            // What is suppressed is a date on which the *entire* roster was founded: every party that
            // existed by then came into being on that one date. Only PoliticalEngine's empty-registry
            // recovery does that — it replaces a registry it found empty, so the field it mints has no
            // elders behind it. Ordinary churn cannot look like it, because the parties it founds are
            // added to a roster that still holds everything ever founded before (nothing is removed
            // from the list; a death only stamps a status and a date). So the count of foundings on a
            // churn date is always short of the count of parties standing on or before it, however
            // many splits, merges and deaths land in the same month — and three in one month is
            // ordinary: deaths, merges and splits each loop over all their candidates and each stamps
            // the same date.
            //
            // Counted against the parties founded at or before the date rather than against the whole
            // roster, so that a founding *after* a regeneration cannot make the regeneration reappear
            // as news — this is re-derived over the full history on every publish, so an answer that
            // drifted with roster size would be a different archive every election.
            var kept = new List<PartyLifecycleRecord>(records.Count);
            int start = 0;
            int elders = 0;   // parties founded on or before the current run's date; foundings is sorted
            while (start < records.Count)
            {
                SimDate date = records[start].Date;
                int end = start + 1;
                while (end < records.Count && records[end].Date == date) end++;

                while (elders < foundings.Count && foundings[elders] <= date) elders++;

                int founded = 0;
                for (int i = start; i < end; i++)
                {
                    if (records[i].Kind == PartyLifecycleKind.Founded) founded++;
                }

                if (founded > 0 && founded >= elders) suppressed.Add(date);
                else kept.AddRange(records.GetRange(start, end - start));

                start = end;
            }

            return new PartyLifecycleChangeSet(kept, suppressed);
        }

        private static int Compare(PartyLifecycleRecord a, PartyLifecycleRecord b)
        {
            int byDate = a.Date.CompareTo(b.Date);
            if (byDate != 0) return byDate;

            int byParty = string.CompareOrdinal(a.PartyId, b.PartyId);
            return byParty != 0 ? byParty : ((int)a.Kind).CompareTo((int)b.Kind);
        }
    }
}
