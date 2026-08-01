using System;
using System.Collections.Generic;
using Agora.Core.Contracts;
using Agora.Core.Tuning;

namespace Agora.Core.Events.Catalog
{
    /// <summary>
    /// One catalog document handed to the loader: a label and its text. The loader never opens a file,
    /// so the label is whatever the caller finds useful in a log line — conventionally the file name.
    /// </summary>
    /// <remarks>
    /// Keeping IO outside <c>Agora.Core</c> is not fastidiousness: it is what lets the validator be
    /// driven from string literals in the test suite, on a machine with no game and no data folder.
    /// </remarks>
    public sealed class TimelineCatalogSource
    {
        public string Name { get; }

        /// <summary>The document text. Never a path.</summary>
        public string Json { get; }

        public TimelineCatalogSource(string name, string json)
        {
            Name = name ?? "";
            Json = json ?? "";
        }

        public override string ToString() => Name;
    }

    /// <summary>
    /// The validated timeline: every event that survived loading, in a fixed order.
    /// </summary>
    /// <remarks>
    /// Events are sorted by date ascending, then by id ordinal ascending. That order is contractual —
    /// the scheduler walks this list, so if it depended on which file was read first or on the order a
    /// dictionary happened to enumerate, one save would produce different history on different runs.
    /// </remarks>
    public sealed class TimelineCatalog
    {
        private readonly List<TimelineEvent> _events;
        private readonly Dictionary<string, TimelineEvent> _byId;

        internal TimelineCatalog(List<TimelineEvent> sortedEvents)
        {
            _events = sortedEvents;
            _byId = new Dictionary<string, TimelineEvent>(StringComparer.Ordinal);
            for (int i = 0; i < sortedEvents.Count; i++)
            {
                _byId[sortedEvents[i].Id] = sortedEvents[i];
            }
        }

        /// <summary>A catalog with no events. What a missing or wholly invalid data set degrades to.</summary>
        public static TimelineCatalog Empty { get; } = new TimelineCatalog(new List<TimelineEvent>());

        /// <summary>Every loaded event, by date then id. Never reordered after construction.</summary>
        public IReadOnlyList<TimelineEvent> Events => _events;

        public int Count => _events.Count;

        /// <summary>Looks an event up by its catalog id. Ordinal comparison; ids are kebab-case ASCII.</summary>
        public bool TryGetById(string id, out TimelineEvent? found)
        {
            if (string.IsNullOrEmpty(id))
            {
                found = null;
                return false;
            }

            return _byId.TryGetValue(id, out found);
        }

        /// <summary>
        /// The events a save with this theme can ever see: its own region's, plus the global set when
        /// <c>catalog.includeGlobal</c> is on. Order is preserved from <see cref="Events"/>.
        /// </summary>
        public IReadOnlyList<TimelineEvent> ForTheme(RegionTheme theme, EngineTuning tuning)
        {
            if (tuning == null) throw new ArgumentNullException(nameof(tuning));

            EventRegion regional = theme == RegionTheme.Na ? EventRegion.Na : EventRegion.Eu;
            bool includeGlobal = tuning.Catalog.IncludeGlobal;

            var selected = new List<TimelineEvent>();
            for (int i = 0; i < _events.Count; i++)
            {
                TimelineEvent e = _events[i];
                if (e.Region == regional || (includeGlobal && e.Region == EventRegion.Global))
                {
                    selected.Add(e);
                }
            }

            return selected;
        }
    }

    /// <summary>
    /// What <see cref="TimelineCatalogLoader"/> returns: the events that loaded, and every reason the
    /// ones that did not were refused.
    /// </summary>
    /// <remarks>
    /// The loader deliberately does not throw on bad content. A catalog that has drifted from the
    /// effect palette must not take a save down at load time — it degrades to the valid subset and
    /// reports, the same fail-closed posture non-negotiable #7 takes with the LLM. The build-time gate
    /// is the schema suite, which asserts <see cref="IsValid"/> on the shipped files.
    /// </remarks>
    public sealed class TimelineCatalogLoadResult
    {
        internal TimelineCatalogLoadResult(TimelineCatalog catalog, List<CatalogIssue> errors,
                                           List<CatalogIssue> warnings, int rejectedEventCount)
        {
            Catalog = catalog;
            Errors = errors;
            Warnings = warnings;
            RejectedEventCount = rejectedEventCount;
        }

        public TimelineCatalog Catalog { get; }

        /// <summary>Every rejection, in source-name then document order.</summary>
        public IReadOnlyList<CatalogIssue> Errors { get; }

        /// <summary>Authoring feedback that did not reject anything, in the same order.</summary>
        public IReadOnlyList<CatalogIssue> Warnings { get; }

        /// <summary>How many <c>events[]</c> entries were dropped. Zero for a clean data set.</summary>
        public int RejectedEventCount { get; }

        /// <summary>True when nothing was rejected. The condition the shipped catalogs must satisfy.</summary>
        public bool IsValid => Errors.Count == 0;
    }
}
