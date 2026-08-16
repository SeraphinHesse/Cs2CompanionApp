using System.Globalization;

namespace Agora.Core.Events.Catalog
{
    /// <summary>How badly a catalog entry is broken.</summary>
    /// <remarks>
    /// <para>
    /// An <see cref="Error"/> rejects the entry it is attached to — a broken event never reaches the
    /// scheduler, and a broken document contributes no events at all. A <see cref="Warning"/> is
    /// authoring feedback: the entry loads unchanged.
    /// </para>
    /// <para>
    /// <b>One error is scoped to neither an event nor a document, and the sentence above overstated
    /// the rule until it was found.</b> <see cref="CatalogIssueCode.MalformedFeatureIds"/> is raised
    /// against a document's <c>featureIds</c> allow-list, which is not an entry and whose breakage
    /// does not by itself invalidate any event: an event that actually names a feature fails
    /// separately and more precisely with <see cref="CatalogIssueCode.UnlockIdNotDeclared"/>, and one
    /// that names none is unharmed. So it reports without rejecting, and a document carrying it can
    /// have <c>RejectedEventCount == 0</c> while <c>IsClean</c> is false.
    /// </para>
    /// <para>
    /// That combination is deliberately not silent: <c>IsClean</c> going false is what fails
    /// <c>ShippedCivicEventCatalogTests</c>, so a malformed allow-list still breaks the build — it
    /// simply does so without discarding events that were never in question. Read
    /// <see cref="Error"/> as "this is wrong and the build should fail", not as "something was
    /// dropped".
    /// </para>
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
        SeverityScaledMagnitudeClamped = 60,

        // --- civic-event level (rejects one civic event) ---------------------------------------
        //
        // Wave 3. These share this enum, and the CatalogIssue struct, with the timeline loader on
        // purpose: both answer the same question — which document, which entry, which property, why —
        // and one issue vocabulary is what lets the mod log render either with one formatter.

        /// <summary><c>trigger</c> or <c>check</c> is absent or is not an object.</summary>
        MalformedSpec = 100,

        /// <summary><c>kind</c> is absent or is not one of the declared trigger kinds.</summary>
        UnknownTriggerKind = 101,

        /// <summary><c>comparison</c> is present but is not one of the four declared comparisons.</summary>
        UnknownComparison = 102,

        /// <summary><c>scope</c> is present but is not <c>city</c>, <c>anyDistrict</c> or <c>allDistricts</c>.</summary>
        UnknownTriggerScope = 103,

        /// <summary>
        /// A <c>metric</c> or <c>delta</c> spec named an id the metric registry does not carry at the
        /// declared scope. This is the check that makes an unreachable trigger a load-time catalog
        /// error rather than a runtime surprise.
        /// </summary>
        UnknownMetricId = 104,

        /// <summary><c>threshold</c> is absent or is not a finite number.</summary>
        ThresholdNotFinite = 105,

        /// <summary>A <c>delta</c> spec declared a <c>windowMonths</c> below 1 or above the history bound.</summary>
        WindowMonthsOutOfRange = 106,

        /// <summary>
        /// An absolute <c>metric</c> spec named one of the census-gated ids. Their units — per
        /// in-game day, or cumulative since the city was founded — are unresolved until wave 1's
        /// <c>AGORA-STATCOLLECTION</c> gate is walked, so an absolute threshold on one of them cannot
        /// be authored correctly and a <c>delta</c> is the only defensible reading.
        /// </summary>
        CensusGatedMetricNeedsDelta = 107,

        /// <summary>
        /// A <c>policy</c> spec. <b>No sensor writes <c>CitySnapshot.ActivePolicyIds</c></b> — the
        /// field is plumbed through <c>SensorMerge</c> and <c>SnapshotAssembly</c> and populated by
        /// nothing — so a policy trigger can never fire, and an <c>absent</c> policy trigger fires on
        /// every city forever. Rejected at load rather than shipped as a permanent truth.
        /// </summary>
        PolicyTriggerUnsupported = 108,

        /// <summary>
        /// An <c>unlock</c> spec, or an <c>absent</c> spec falling through to feature membership,
        /// named an id the document did not declare in its <c>featureIds</c> allow-list. Feature ids
        /// are raw prefab-name strings that nothing can validate against the game, so a misspelling
        /// would otherwise read as "never unlocked" — and negate to <c>Met</c> forever under
        /// <c>absent</c>.
        /// </summary>
        UnlockIdNotDeclared = 109,

        /// <summary>
        /// <c>featureIds</c> is present but is not an array of non-empty strings.
        /// </summary>
        /// <remarks>
        /// <b>Document-scoped, and rejects nothing on its own</b> — the one error in this enum that
        /// does not discard what it is attached to. See the remarks on
        /// <see cref="CatalogIssueSeverity"/> for why, and why that is not a silent failure.
        /// </remarks>
        MalformedFeatureIds = 110,

        /// <summary>One of the seven prose fields is absent, not a string, or blank.</summary>
        MissingProse = 111,

        /// <summary>An effect list is present but is not an array of strings.</summary>
        MalformedEffectList = 112,

        /// <summary><c>districtAffinity</c> is present but is not an array of strings.</summary>
        MalformedDistrictAffinity = 113,

        /// <summary>
        /// The same effect id appears twice in one list. Warning only — the duplicate is dropped,
        /// since two identical requests would stack against <c>maxStoryEffectsPerModifier</c> for no
        /// authored reason.
        /// </summary>
        DuplicateEffectId = 114,

        /// <summary>
        /// A <c>check</c> declared <c>relativeToBaseline</c> on a spec kind that has no baseline to be
        /// relative to. Warning only — the flag is ignored.
        /// </summary>
        BaselineOnNonMetricCheck = 115,

        /// <summary>
        /// A <c>check</c> declared <c>relativeToBaseline</c> at a district scope. <b>Provably
        /// unscoreable, forever.</b>
        /// </summary>
        /// <remarks>
        /// <c>StoryAssembler.Baseline</c> returns <c>null</c> for any scope other than
        /// <c>City</c> — and says why: nothing on <c>StorySlot</c> records which district the story
        /// landed on, so there is no single district whose opening reading could be captured. A
        /// relative check with no baseline resolves <c>Unmeasurable</c> on every save, in every
        /// month, forever. It is scored in neither half of the 2-of-3 and moves the power balance by
        /// zero, so the event silently contributes nothing while reading like a working goal.
        /// </remarks>
        BaselineCheckAtDistrictScope = 116,

        /// <summary>
        /// A district-scoped check reads the same metric as its district-scoped trigger, so it is
        /// answered by whichever district happens to satisfy it — not by the one the story is about.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <c>TriggerEvaluator</c>'s <c>AnyDistrict</c> walks <b>every</b> district and returns
        /// <c>Met</c> on the first that clears the bar, and nothing binds a check to the district
        /// that fired the trigger — again because <c>StorySlot</c> carries no district id. So
        /// "some district is bad" paired with "some district is good on the same metric" is
        /// typically satisfied the moment the story opens, by a district the player never touched.
        /// </para>
        /// <para>
        /// A <b>warning</b> rather than an error, because it is a judgement about shape rather than a
        /// provable impossibility — a genuinely different question at district scope can be
        /// legitimate. But the shipped-catalog gate holds the catalogs to zero warnings, so this
        /// still stops such an event shipping without someone arguing for it. The usual repairs are
        /// <c>allDistricts</c> (every district clears the bar, which is a real and rising ask) or a
        /// city-scope <c>relativeToBaseline</c> check.
        /// </para>
        /// </remarks>
        DistrictCheckNotBoundToTrigger = 117,

        /// <summary>
        /// An outcome pressure points the opposite way on an issue from the event's own
        /// <c>activePressure</c>. Pressures are salience, not credit — see the remarks on
        /// <c>CivicEvent.ActivePressure</c>.
        /// </summary>
        /// <remarks>
        /// The only consumer of an event's <c>IssuePosition</c> is <c>AffinityEngine.EventTerm</c>,
        /// which dot-products it against each party's platform. So a mirror-negated success pressure
        /// does not release the issue — it moves voters to the <b>opposite pole</b>, rewarding the
        /// party that opposed doing anything. All three wave-3 content lanes independently invented
        /// a mirroring convention, which is why the rule is machine-checked rather than only written
        /// down.
        /// </remarks>
        PressureSignFlip = 118,

        /// <summary>
        /// A district check's threshold is tighter than its trigger's, leaving a band of districts
        /// that never contributed to the trigger but can still fail the check.
        /// </summary>
        /// <remarks>
        /// <para>
        /// An <c>allDistricts</c> check returns <c>NotMet</c> the instant one measured district fails.
        /// So if the trigger fires at <c>&gt;= 0.40</c> and the check demands <c>&lt; 0.35</c>, a
        /// district sitting at 0.37 contributed nothing to the trigger, is invisible in the prose —
        /// the description says the rest of the city is fine — and fails the story anyway. The player
        /// fixes the district the story is about and still loses, over one they were never told
        /// about, with nothing surfacing why.
        /// </para>
        /// <para>
        /// Reported only on a genuine numeric gap, so an exact complement passes whatever the
        /// strictness of the two comparisons. A warning rather than an error because a deliberate
        /// "clean up the whole city, not just the worst block" event is a legitimate design — it just
        /// has to be argued for, and the shipped gate holds catalogs to zero warnings.
        /// </para>
        /// <para>
        /// Distinct from <see cref="DistrictCheckNotBoundToTrigger"/>, which is about scope: this one
        /// is invisible to that rule, because the defect lives between two thresholds rather than
        /// between two scopes. Lane 3a hit it eight times.
        /// </para>
        /// </remarks>
        CheckThresholdLeavesTrapBand = 119,

        /// <summary>
        /// A <c>delta</c> check reads back further than the story has existed, so part of its verdict
        /// was decided before the player saw the card.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <b>A story lives one month, not <c>cycleMonths</c>.</b>
        /// <c>StoryAssembler.NewStory</c> sets <c>months = stories.CycleMonths - 1</c> — draft on M,
        /// resolve on M+1, next batch at M+2 — so at the shipped cadence of 2 the window the player
        /// can influence is a single month. A check reading back further scores the player on months
        /// that predate their decision, and the further back it reads the smaller their share of the
        /// verdict.
        /// </para>
        /// <para>
        /// This is the distinction that makes <c>cycleMonths</c> a trap for content authors: it is the
        /// <i>cadence</i>, not the story's life, and the two differ by one. Every wave-3 content lane
        /// was handed the cadence and authored against it.
        /// </para>
        /// </remarks>
        CheckWindowOutrunsStoryLife = 120,

        /// <summary>
        /// A threshold sits above the highest value its metric's sensor can actually produce.
        /// </summary>
        /// <remarks>
        /// Two shipped metrics are means over channels the game does not all expose, so the
        /// unmeasured ones are hard-zeroed and drag the mean down permanently: <c>serviceCoverage</c>
        /// tops out at 5/9 ≈ 0.5556 and <c>pollution</c> at 0.75. A <c>gte</c> above the ceiling can
        /// never be met and a <c>lt</c> above it is met by every city always — either way the spec
        /// says nothing about the city. See <c>CivicEventCatalogLoader.AttainableMaximum</c>, which
        /// also records why a threshold merely <i>near</i> the ceiling is a judgement for review
        /// rather than something this loader can decide.
        /// </remarks>
        ThresholdAboveAttainableMaximum = 121
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
