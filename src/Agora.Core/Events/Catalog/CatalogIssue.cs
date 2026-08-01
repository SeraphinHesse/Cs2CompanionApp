using System.Globalization;

namespace Agora.Core.Events.Catalog
{
    /// <summary>How badly a catalog entry is broken.</summary>
    /// <remarks>
    /// An <see cref="Error"/> rejects the entry it is attached to — a broken event never reaches the
    /// scheduler, and a broken document contributes no events at all. A <see cref="Warning"/> is
    /// authoring feedback: the entry loads unchanged.
    /// </remarks>
    public enum CatalogIssueSeverity
    {
        Warning = 0,
        Error = 1
    }

    /// <summary>
    /// Why a catalog entry was rejected or flagged. A closed set, so callers (and tests) can assert on
    /// the reason rather than on message text, which is free to be reworded.
    /// </summary>
    public enum CatalogIssueCode
    {
        None = 0,

        // --- document level (rejects the whole source) ---------------------------------------

        /// <summary>The text is not valid JSON.</summary>
        MalformedJson = 1,

        /// <summary>The document's root is not a JSON object.</summary>
        RootNotObject = 2,

        /// <summary><c>schemaVersion</c> is missing, not an integer, or not the supported version.</summary>
        UnsupportedSchemaVersion = 3,

        /// <summary><c>events</c> is missing or is not an array.</summary>
        EventsMissing = 4,

        /// <summary>Two sources were handed in under the same name. Warning only.</summary>
        DuplicateSourceName = 5,

        // --- event level (rejects one event) --------------------------------------------------

        /// <summary>An <c>events[]</c> element is not a JSON object.</summary>
        EventNotObject = 20,

        /// <summary><c>id</c> is absent, not a string, or empty.</summary>
        MissingEventId = 21,

        /// <summary><c>id</c> is not lowercase kebab-case.</summary>
        MalformedEventId = 22,

        /// <summary>Another entry — in this document or an earlier one — already claimed this id.</summary>
        DuplicateEventId = 23,

        /// <summary><c>dateISO</c> is absent or is not a real <c>YYYY-MM-DD</c> date.</summary>
        MalformedDate = 24,

        /// <summary>The date falls outside <c>catalog.startYear</c>…<c>catalog.catalogEndYear</c>. Warning only.</summary>
        DateOutsideCatalogWindow = 25,

        /// <summary><c>region</c> is not <c>eu</c>, <c>na</c> or <c>global</c>.</summary>
        UnknownRegion = 26,

        /// <summary><c>title</c> is absent, not a string, or blank.</summary>
        MissingTitle = 27,

        /// <summary><c>severity</c> is not an integer in 1…<c>catalog.severityMax</c>.</summary>
        SeverityOutOfRange = 28,

        /// <summary>The event's own <c>durationMonths</c> is negative or above the catalog ceiling.</summary>
        DurationOutOfRange = 29,

        /// <summary><c>headlineBrief</c> is absent, not a string, or blank.</summary>
        MissingHeadlineBrief = 30,

        /// <summary><c>tags</c> is present but is not an array of strings.</summary>
        MalformedTags = 31,

        /// <summary>A tag is not lowercase kebab-case. Warning only — the tag is kept.</summary>
        MalformedTag = 32,

        /// <summary><c>issuePressure</c> is present but is not an object of six numbers.</summary>
        MalformedIssuePressure = 33,

        /// <summary>An <c>issuePressure</c> component is outside <c>[-1, +1]</c>.</summary>
        IssuePressureOutOfRange = 34,

        /// <summary>A property the timeline schema does not declare. Warning only — it is ignored.</summary>
        UnknownProperty = 35,

        // --- effect level (rejects the owning event) ------------------------------------------

        /// <summary><c>effects</c> is present but is not an array.</summary>
        EffectsNotArray = 50,

        /// <summary>An <c>effects[]</c> element is not a JSON object.</summary>
        EffectNotObject = 51,

        /// <summary><c>effectId</c> is absent, empty, or not in the effect palette registry.</summary>
        UnknownEffectId = 52,

        /// <summary><c>scope</c> is absent or is neither <c>city</c> nor <c>district</c>.</summary>
        UnknownEffectScope = 53,

        /// <summary>The declared scope disagrees with the palette entry's scope.</summary>
        EffectScopeMismatch = 54,

        /// <summary><c>magnitude</c> is absent, not a number, or not finite.</summary>
        MagnitudeNotFinite = 55,

        /// <summary><c>magnitude</c> is outside the effect's declared magnitude cap.</summary>
        MagnitudeOutOfCap = 56,

        /// <summary><c>durationMonths</c> is negative or outside the effect's declared duration cap.</summary>
        EffectDurationOutOfCap = 57,

        /// <summary>A catalog effect named a district. Real history does not know the player's map.</summary>
        DistrictIdNotAllowed = 58,

        /// <summary>The effect requests nothing. Warning only.</summary>
        ZeroMagnitude = 59,

        /// <summary>
        /// Severity scaling would push this magnitude past the cap, so the sink will clamp it at
        /// runtime. Warning only — the authored value is inside the cap, which is what load-time
        /// validation checks.
        /// </summary>
        SeverityScaledMagnitudeClamped = 60
    }

    /// <summary>
    /// One validation finding, addressed to whoever authored the catalog: which document, which entry,
    /// which property, and why it is wrong.
    /// </summary>
    public readonly struct CatalogIssue
    {
        public CatalogIssueSeverity Severity { get; }

        public CatalogIssueCode Code { get; }

        /// <summary>The label the caller handed in with the text, e.g. <c>"timeline_eu.json"</c>.</summary>
        public string SourceName { get; }

        /// <summary>The offending event's id, or empty when the id itself is what is broken.</summary>
        public string EventId { get; }

        /// <summary>JSON pointer-ish path, e.g. <c>events[3].effects[1].magnitude</c>.</summary>
        public string Path { get; }

        /// <summary>Human-readable explanation. Never parsed — assert on <see cref="Code"/> instead.</summary>
        public string Message { get; }

        public CatalogIssue(CatalogIssueSeverity severity, CatalogIssueCode code, string sourceName,
                            string eventId, string path, string message)
        {
            Severity = severity;
            Code = code;
            SourceName = sourceName ?? "";
            EventId = eventId ?? "";
            Path = path ?? "";
            Message = message ?? "";
        }

        /// <summary>Stable single-line form, suitable for the mod log.</summary>
        public override string ToString()
        {
            string tag = Severity == CatalogIssueSeverity.Error ? "error" : "warning";
            string where = Path.Length == 0 ? SourceName : SourceName + " " + Path;
            string who = EventId.Length == 0 ? "" : " (" + EventId + ")";
            return string.Format(CultureInfo.InvariantCulture, "[{0}] {1}{2}: {3} - {4}",
                tag, where, who, Code, Message);
        }
    }
}
