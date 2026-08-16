using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Agora.Core.Contracts;
using Agora.Core.Events.Catalog;

namespace Agora.Core.Stories.Catalog
{
    /// <summary>
    /// One <c>events_*.json</c> document handed to the loader: a label and its text.
    /// </summary>
    /// <remarks>
    /// The label is what every finding is addressed to, so it should name the file
    /// (<c>"events_eu.json"</c>) rather than describe it. Core never opens a file — IO belongs to
    /// <c>Agora.Mod</c>, and a loader that cannot read a disk cannot make loading depend on one.
    /// </remarks>
    public sealed class CivicEventCatalogSource
    {
        public string Name { get; }

        public string Json { get; }

        public CivicEventCatalogSource(string name, string json)
        {
            Name = name ?? "";
            Json = json ?? "";
        }
    }

    /// <summary>
    /// The accepted civic events, sorted by id ordinal ascending.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Sorted by id, not by date.</b> A civic event has no date — it is triggered by the state of
    /// the city rather than scheduled against real history, which is the whole difference between
    /// this catalog and <c>TimelineCatalog</c>. Id order is therefore the only total order available,
    /// and it is a total order, which is what the pool refresh needs: <c>EventPoolWeighting.Compare</c>
    /// breaks its last tie on <c>EventId</c> ordinal ascending and would otherwise inherit whatever
    /// order the documents happened to be read in.
    /// </para>
    /// </remarks>
    public sealed class CivicEventCatalog
    {
        private readonly ReadOnlyCollection<CivicEvent> _events;
        private readonly ReadOnlyCollection<string> _featureIds;

        public CivicEventCatalog(IList<CivicEvent> events, IList<string> declaredFeatureIds)
        {
            var ordered = new List<CivicEvent>(events ?? new List<CivicEvent>());
            ordered.Sort(CompareById);
            _events = new ReadOnlyCollection<CivicEvent>(ordered);

            var features = new List<string>(declaredFeatureIds ?? new List<string>());
            features.Sort(StringComparer.Ordinal);
            _featureIds = new ReadOnlyCollection<string>(features);
        }

        /// <summary>
        /// A catalog with nothing in it. What a save gets when the data files are missing or every
        /// document was rejected.
        /// </summary>
        /// <remarks>
        /// <b>A degraded save, not a broken one</b> — the same contract <c>TimelineCatalog.Empty</c>
        /// carries. No story can draft against it, which the story cycle reports once rather than
        /// once per cycle forever; refusing to load a city over a data file would be far worse.
        /// </remarks>
        public static readonly CivicEventCatalog Empty =
            new CivicEventCatalog(new List<CivicEvent>(), new List<string>());

        /// <summary>Every accepted event, sorted by id ordinal ascending.</summary>
        public IReadOnlyList<CivicEvent> Events
        {
            get { return _events; }
        }

        /// <summary>
        /// The union of every document's <c>featureIds</c> allow-list, sorted ordinal ascending.
        /// </summary>
        /// <remarks>
        /// Kept on the loaded catalog rather than discarded after validation because it is the only
        /// record of which progression feature names the content was authored against, and wave 1's
        /// gate 11 — "features grow, and are not the whole catalogue" — is what will eventually
        /// confirm or refute them against a real save.
        /// </remarks>
        public IReadOnlyList<string> DeclaredFeatureIds
        {
            get { return _featureIds; }
        }

        /// <summary>
        /// The events playable under a region theme: that region's own, plus every global one.
        /// </summary>
        /// <remarks>
        /// Order is preserved from <see cref="Events"/>, so the filtered list is sorted by id too.
        /// </remarks>
        public IReadOnlyList<CivicEvent> ForTheme(EventRegion region)
        {
            var matched = new List<CivicEvent>();
            for (int i = 0; i < _events.Count; i++)
            {
                CivicEvent candidate = _events[i];
                if (candidate.Region == region || candidate.Region == EventRegion.Global) matched.Add(candidate);
            }

            return new ReadOnlyCollection<CivicEvent>(matched);
        }

        private static int CompareById(CivicEvent a, CivicEvent b)
        {
            return string.CompareOrdinal(a.Id, b.Id);
        }
    }

    /// <summary>
    /// What one load produced: the valid subset, plus every finding against what was rejected.
    /// </summary>
    /// <remarks>
    /// <b>The loader never throws on bad content and never returns nothing because one entry was
    /// wrong.</b> It degrades to the valid subset and reports, exactly as <c>TimelineCatalogLoader</c>
    /// does — a corrupt catalog must not take the save down (non-negotiable #7). The shipped catalog
    /// is held to a stricter standard than this by a test, which is where a bad entry is supposed to
    /// be caught: at build time, by a red test, not in a player's log.
    /// </remarks>
    public sealed class CivicEventCatalogLoadResult
    {
        public CivicEventCatalog Catalog { get; }

        public IReadOnlyList<CatalogIssue> Errors { get; }

        public IReadOnlyList<CatalogIssue> Warnings { get; }

        /// <summary>How many events were rejected outright.</summary>
        public int RejectedEventCount { get; }

        public CivicEventCatalogLoadResult(CivicEventCatalog catalog, IList<CatalogIssue> errors,
                                           IList<CatalogIssue> warnings, int rejectedEventCount)
        {
            Catalog = catalog;
            Errors = new ReadOnlyCollection<CatalogIssue>(new List<CatalogIssue>(errors ?? new List<CatalogIssue>()));
            Warnings = new ReadOnlyCollection<CatalogIssue>(new List<CatalogIssue>(warnings ?? new List<CatalogIssue>()));
            RejectedEventCount = rejectedEventCount;
        }

        /// <summary>True when nothing was rejected and no error was raised.</summary>
        public bool IsClean
        {
            get { return Errors.Count == 0 && RejectedEventCount == 0; }
        }
    }
}
