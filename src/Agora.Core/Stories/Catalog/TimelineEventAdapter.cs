using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using Agora.Core.Contracts;
using Agora.Core.Tuning;

namespace Agora.Core.Stories.Catalog
{
    /// <summary>
    /// What <c>data/timeline_adaptation.json</c> says should happen to one timeline event.
    /// </summary>
    /// <remarks>
    /// The three values are the two deliberate ends of the 25/50/25 split plus its middle. They are
    /// named after the file's own vocabulary so a reader can hold one word in their head across the
    /// schema, the data and the code.
    /// </remarks>
    public enum TimelineAdaptationKind
    {
        /// <summary>
        /// Never becomes a civic event. It keeps firing as a timeline event exactly as it does today.
        /// </summary>
        /// <remarks>
        /// This is the non-destructive form of the rework plan's "drop the most boring 25%": the entry
        /// stays in <c>timeline_*.json</c> and stays in the timeline, and only the story system
        /// declines it. Deleting the entries would have stopped them firing in the timeline system too.
        /// </remarks>
        None = 0,

        /// <summary>The adapter wraps it. The default, so the middle ~50% need no entry at all.</summary>
        Generic = 1,

        /// <summary>
        /// A hand-written civic event named by <c>civicEventId</c> takes over. The adapter produces
        /// nothing for these: the authored event is already in a civic catalog, with real resolution
        /// checks and all seven prose fields.
        /// </summary>
        Authored = 2
    }

    /// <summary>
    /// The parsed <c>data/timeline_adaptation.json</c>: which timeline events become civic events, and
    /// how.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>A side file rather than an edit to the timeline catalogs.</b> That is the owner's ruling at
    /// the top of wave 3, and it has one cost, paid knowingly: a second list that can drift from the
    /// first. A <c>timelineEventId</c> matching nothing would silently adapt nothing while reading as
    /// a deliberate decision, so <c>ShippedCivicEventCatalogTests.AdaptationPolicy_NamesOnlyEventsThatExist</c>
    /// pins every id here to a real timeline event at build time.
    /// </para>
    /// <para>
    /// Entries are held in one array sorted by id ordinal and found by binary search, never in a
    /// dictionary: nothing about the answer may depend on the order the file happened to list them in.
    /// </para>
    /// </remarks>
    public sealed class TimelineAdaptationPolicy
    {
        /// <summary>The only <c>schemaVersion</c> this reader accepts (non-negotiable #9).</summary>
        public const int SupportedSchemaVersion = 1;

        private readonly string[] _ids;
        private readonly TimelineAdaptationKind[] _kinds;
        private readonly string[] _civicEventIds;
        private readonly ReadOnlyCollection<string> _diagnostics;

        private TimelineAdaptationPolicy(TimelineAdaptationKind defaultKind, string[] ids,
                                         TimelineAdaptationKind[] kinds, string[] civicEventIds,
                                         List<string> diagnostics)
        {
            DefaultKind = defaultKind;
            _ids = ids;
            _kinds = kinds;
            _civicEventIds = civicEventIds;
            _diagnostics = new ReadOnlyCollection<string>(diagnostics);
        }

        /// <summary>
        /// What an event with no entry gets: <c>generic</c> unless the file says otherwise, and never
        /// <see cref="TimelineAdaptationKind.Authored"/> — see <c>TryParseDefaultKind</c>.
        /// </summary>
        public TimelineAdaptationKind DefaultKind { get; }

        /// <summary>How many events the file names explicitly.</summary>
        public int EntryCount
        {
            get { return _ids.Length; }
        }

        /// <summary>
        /// What was wrong with the document, in the order it was read. Empty for a clean file.
        /// </summary>
        /// <remarks>
        /// Both catalog loaders report their equivalent cases rather than swallowing them, and an
        /// entry skipped in silence is the worst outcome available here: a misspelt <c>policy</c>
        /// string reads as <c>generic</c>, so an event somebody meant to drop keeps being wrapped and
        /// the file says it does not. Plain strings rather than <c>CatalogIssue</c> values because
        /// this is not a catalog and no <c>CatalogIssueCode</c> describes it.
        /// </remarks>
        public IReadOnlyList<string> Diagnostics
        {
            get { return _diagnostics; }
        }

        /// <summary>
        /// The policy a file with no entries expresses: wrap everything. Also what the caller falls
        /// back to when the file cannot be read at all.
        /// </summary>
        public static TimelineAdaptationPolicy WrapAll { get; } = new TimelineAdaptationPolicy(
            TimelineAdaptationKind.Generic,
            new string[0], new TimelineAdaptationKind[0], new string[0], new List<string>());

        /// <summary>
        /// Reads the policy document. Returns false — and hands back <see cref="WrapAll"/> — for a
        /// document that is not valid JSON, is not an object, or carries the wrong schema version.
        /// </summary>
        /// <remarks>
        /// It never throws on bad content, exactly as the two catalog loaders do not: a corrupt data
        /// file must not take the save down. Degrading to "wrap everything" is the recoverable
        /// direction — the player gets a few civic events that ought to have been left as timeline
        /// entries, rather than a story system that silently produces nothing.
        /// </remarks>
        public static bool TryParse(string json, out TimelineAdaptationPolicy policy)
        {
            policy = WrapAll;
            var diagnostics = new List<string>();

            JsonNode root;
            try
            {
                root = TuningJsonParser.Parse(json);
            }
            catch (Exception ex) when (ex is TuningFormatException || ex is FormatException || ex is OverflowException)
            {
                return false;
            }

            if (root.Kind != JsonKind.Object) return false;

            JsonNode? versionNode = Member(root, "schemaVersion");
            if (versionNode == null || versionNode.Kind != JsonKind.Number ||
                versionNode.Number != SupportedSchemaVersion)
            {
                return false;
            }

            TimelineAdaptationKind defaultKind = TimelineAdaptationKind.Generic;
            JsonNode? defaultNode = Member(root, "defaultPolicy");
            if (defaultNode != null)
            {
                TimelineAdaptationKind parsed;
                if (defaultNode.Kind == JsonKind.String && TryParseDefaultKind(defaultNode.Text, out parsed))
                {
                    defaultKind = parsed;
                }
                else
                {
                    diagnostics.Add("defaultPolicy is not one of \"none\" or \"generic\"; using \"generic\"");
                }
            }

            var entries = new List<Entry>();
            JsonNode? policiesNode = Member(root, "policies");
            if (policiesNode != null && policiesNode.Kind == JsonKind.Array && policiesNode.Items != null)
            {
                for (int i = 0; i < policiesNode.Items.Count; i++)
                {
                    string where = "policies[" + i.ToString(CultureInfo.InvariantCulture) + "]";

                    JsonNode item = policiesNode.Items[i];
                    if (item.Kind != JsonKind.Object)
                    {
                        diagnostics.Add(where + " is not an object; skipped");
                        continue;
                    }

                    JsonNode? idNode = Member(item, "timelineEventId");
                    string id = idNode != null && idNode.Kind == JsonKind.String ? (idNode.Text ?? "") : "";
                    if (id.Length == 0)
                    {
                        diagnostics.Add(where + " has no timelineEventId; skipped");
                        continue;
                    }

                    JsonNode? kindNode = Member(item, "policy");
                    TimelineAdaptationKind kind;
                    if (kindNode == null || kindNode.Kind != JsonKind.String ||
                        !TryParseKind(kindNode.Text, out kind))
                    {
                        // Reported rather than skipped in silence: an unreadable policy string falls
                        // through to the default, which is "generic", so an event somebody meant to
                        // drop would keep being wrapped with the file claiming otherwise.
                        diagnostics.Add(where + " ('" + id + "') has no recognised policy; it takes the " +
                                        "default instead of what was written");
                        continue;
                    }

                    JsonNode? civicNode = Member(item, "civicEventId");
                    string civicId = civicNode != null && civicNode.Kind == JsonKind.String
                        ? (civicNode.Text ?? "")
                        : "";

                    if (kind == TimelineAdaptationKind.Authored && civicId.Length == 0)
                    {
                        diagnostics.Add(where + " ('" + id + "') is marked authored but names no " +
                                        "civicEventId; nothing would take the event over");
                    }

                    entries.Add(new Entry(id, kind, civicId, entries.Count));
                }
            }

            // Sorted by id ordinal, with the original position as the tie-break, so a file that names
            // an id twice resolves to its first entry whatever List<T>.Sort does with equal keys.
            // (The shipped file cannot: the gate test refuses a repeated id.)
            entries.Sort(CompareEntries);

            var ids = new string[entries.Count];
            var kinds = new TimelineAdaptationKind[entries.Count];
            var civicIds = new string[entries.Count];
            for (int i = 0; i < entries.Count; i++)
            {
                ids[i] = entries[i].Id;
                kinds[i] = entries[i].Kind;
                civicIds[i] = entries[i].CivicEventId;

                if (i > 0 && string.CompareOrdinal(ids[i - 1], ids[i]) == 0)
                {
                    diagnostics.Add("'" + ids[i] + "' is named twice; the first entry wins");
                }
            }

            policy = new TimelineAdaptationPolicy(defaultKind, ids, kinds, civicIds, diagnostics);
            return true;
        }

        /// <summary>The policy for one timeline event id, or <see cref="DefaultKind"/> when unnamed.</summary>
        public TimelineAdaptationKind KindFor(string timelineEventId)
        {
            int index = IndexOf(timelineEventId);
            return index < 0 ? DefaultKind : _kinds[index];
        }

        /// <summary>
        /// The authored civic event id for a timeline event, or the empty string when the policy is
        /// not <see cref="TimelineAdaptationKind.Authored"/>.
        /// </summary>
        public string AuthoredCivicEventIdFor(string timelineEventId)
        {
            int index = IndexOf(timelineEventId);
            if (index < 0 || _kinds[index] != TimelineAdaptationKind.Authored) return "";
            return _civicEventIds[index];
        }

        private int IndexOf(string timelineEventId)
        {
            if (string.IsNullOrEmpty(timelineEventId)) return -1;

            int low = 0;
            int high = _ids.Length - 1;
            while (low <= high)
            {
                int mid = low + ((high - low) / 2);
                int cmp = string.CompareOrdinal(_ids[mid], timelineEventId);
                if (cmp == 0)
                {
                    // Back up to the first of a run of equal ids, so "the first entry wins" is true of
                    // the lookup and not only of the diagnostic that reports the duplicate.
                    while (mid > 0 && string.CompareOrdinal(_ids[mid - 1], timelineEventId) == 0) mid--;
                    return mid;
                }

                if (cmp < 0) low = mid + 1;
                else high = mid - 1;
            }

            return -1;
        }

        /// <summary>
        /// The kinds a <c>defaultPolicy</c> may take: <c>none</c> and <c>generic</c> only, matching the
        /// schema's own enum.
        /// </summary>
        /// <remarks>
        /// <b><c>authored</c> is a per-event answer and can never be a default.</b> A default of
        /// <c>authored</c> would defer every event the file does not name — about ninety of them — to a
        /// <c>civicEventId</c> that by construction does not exist, since only an entry can carry one.
        /// Read through the general parser this sailed through with no diagnostic at all, and it would
        /// have broken the outcome's guarantee that an <see cref="AdaptationOutcomeKind.Authored"/>
        /// result names something. The schema forbids it; so, now, does the reader that has to survive
        /// a hand-edited file.
        /// </remarks>
        private static bool TryParseDefaultKind(string? text, out TimelineAdaptationKind kind)
        {
            if (!TryParseKind(text, out kind)) return false;
            if (kind == TimelineAdaptationKind.Authored)
            {
                kind = TimelineAdaptationKind.Generic;
                return false;
            }

            return true;
        }

        private static bool TryParseKind(string? text, out TimelineAdaptationKind kind)
        {
            if (string.CompareOrdinal(text, "none") == 0) { kind = TimelineAdaptationKind.None; return true; }
            if (string.CompareOrdinal(text, "generic") == 0) { kind = TimelineAdaptationKind.Generic; return true; }
            if (string.CompareOrdinal(text, "authored") == 0) { kind = TimelineAdaptationKind.Authored; return true; }

            kind = TimelineAdaptationKind.Generic;
            return false;
        }

        private static JsonNode? Member(JsonNode node, string key)
        {
            JsonNode child;
            if (node.Members != null && node.Members.TryGetValue(key, out child)) return child;
            return null;
        }

        private static int CompareEntries(Entry a, Entry b)
        {
            int byId = string.CompareOrdinal(a.Id, b.Id);
            return byId != 0 ? byId : a.Order.CompareTo(b.Order);
        }

        private readonly struct Entry
        {
            public Entry(string id, TimelineAdaptationKind kind, string civicEventId, int order)
            {
                Id = id;
                Kind = kind;
                CivicEventId = civicEventId;
                Order = order;
            }

            public string Id { get; }
            public TimelineAdaptationKind Kind { get; }
            public string CivicEventId { get; }
            public int Order { get; }
        }
    }

    /// <summary>Which of the four things adaptation did.</summary>
    public enum AdaptationOutcomeKind
    {
        /// <summary>Nothing was offered. Reserved for a null event: a caller bug, not a policy.</summary>
        NoEvent = 0,

        /// <summary>Policy <c>none</c>: it stays a timeline event and becomes nothing else.</summary>
        Dropped = 1,

        /// <summary>Policy <c>generic</c>: a wrapped civic event came back.</summary>
        Wrapped = 2,

        /// <summary>
        /// Policy <c>authored</c>: a hand-written civic event named by
        /// <see cref="AdaptationOutcome.AuthoredCivicEventId"/> takes over. The caller resolves it
        /// from a civic catalog.
        /// </summary>
        Authored = 3
    }

    /// <summary>
    /// What one call to <see cref="TimelineEventAdapter.Adapt"/> produced.
    /// </summary>
    /// <remarks>
    /// <b>A bare <c>null</c> would conflate three different answers</b> — dropped on purpose, deferred
    /// to an authored event, and "you passed me nothing" — and only the first of the three means "let
    /// it go". A caller that reads them all as "drop it" makes every authored event silently vanish,
    /// which is a bug that produces no error and no log line. The kind is therefore carried alongside
    /// the payload rather than inferred from its absence.
    /// </remarks>
    public readonly struct AdaptationOutcome
    {
        private readonly string? _authoredCivicEventId;

        private AdaptationOutcome(AdaptationOutcomeKind kind, CivicEvent? civicEvent, string authoredCivicEventId)
        {
            Kind = kind;
            CivicEvent = civicEvent;
            _authoredCivicEventId = authoredCivicEventId;
        }

        public AdaptationOutcomeKind Kind { get; }

        /// <summary>The wrapped event, and non-null for exactly <see cref="AdaptationOutcomeKind.Wrapped"/>.</summary>
        public CivicEvent? CivicEvent { get; }

        /// <summary>
        /// The authored civic event id, non-empty for <see cref="AdaptationOutcomeKind.Authored"/>
        /// unless the policy file marked one authored without naming it — which the policy's
        /// <see cref="TimelineAdaptationPolicy.Diagnostics"/> reports. Never null.
        /// </summary>
        /// <remarks>
        /// Backed by a field and coalesced rather than an auto-property, because a struct always has a
        /// default form nobody constructed: <c>default(AdaptationOutcome)</c> zeroes every field, and
        /// an auto-property would hand back <c>null</c> from a value whose <see cref="Kind"/> reads
        /// <see cref="AdaptationOutcomeKind.NoEvent"/> — so a caller taking <c>.Length</c> would get a
        /// <see cref="NullReferenceException"/> off an outcome the documentation promises is empty
        /// rather than absent. Cheaper to make true than to warn about.
        /// </remarks>
        public string AuthoredCivicEventId
        {
            get { return _authoredCivicEventId ?? ""; }
        }

        /// <summary>True when <see cref="CivicEvent"/> carries something.</summary>
        public bool HasCivicEvent
        {
            get { return CivicEvent != null; }
        }

        public static AdaptationOutcome NoEvent { get; } =
            new AdaptationOutcome(AdaptationOutcomeKind.NoEvent, null, "");

        public static AdaptationOutcome Dropped { get; } =
            new AdaptationOutcome(AdaptationOutcomeKind.Dropped, null, "");

        /// <summary>
        /// A wrapped event. Throws on null rather than producing a <see cref="AdaptationOutcomeKind.Wrapped"/>
        /// outcome carrying nothing, which would break the one invariant the kind exists to state.
        /// </summary>
        public static AdaptationOutcome Wrapped(CivicEvent civicEvent)
        {
            if (civicEvent == null) throw new ArgumentNullException(nameof(civicEvent));
            return new AdaptationOutcome(AdaptationOutcomeKind.Wrapped, civicEvent, "");
        }

        public static AdaptationOutcome Authored(string civicEventId) =>
            new AdaptationOutcome(AdaptationOutcomeKind.Authored, null, civicEventId ?? "");
    }

    /// <summary>
    /// Turns a fired timeline event into the civic event the story system understands — the generic
    /// half of the 25/50/25 split.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>An adapted event carries <see cref="TriggerKind.Manual"/>, and that is the invariant this
    /// class exists to protect.</b> A timeline event is introduced by the timeline firing on its
    /// authored date, not by a metric crossing a threshold, so it has no trigger of its own and must
    /// not be given one it did not author — a borrowed metric trigger would let a wrapped event enter
    /// the pool and be drafted in a month when the historical event is not live at all, which is a
    /// different event with the same prose. <c>Manual</c> is the kind wave 2 defined for exactly this:
    /// never fires from the city, and <b>never a pool member</b> — the pool refresh skips it before
    /// eligibility is considered. Delivery is the introducing system's job, and here the introducing
    /// system is the timeline scheduler.
    /// </para>
    /// <para>
    /// <b>"Manual" is not "mandatory".</b> The tier still comes from <see cref="TimelineEvent.Severity"/>
    /// through <see cref="StoryTiers"/>, exactly as it does for an authored event; nothing here sets or
    /// stores a tier. Two wave-2 lanes read an earlier wording that conflated the two and built
    /// opposite things, which is why it is written down twice.
    /// </para>
    /// <para>
    /// <b>The wrapper carries no effect ids.</b> A timeline event's <c>effects[]</c> are already
    /// requested by the timeline scheduler when it fires; copying them onto the civic event would apply
    /// the same capped magnitude twice for one historical event. The wrapper therefore presses issues
    /// through <c>issuePressure</c> alone — the timeline event's own authored number, never a second
    /// one invented here — with the consequence recorded in the next paragraph.
    /// </para>
    /// <para>
    /// <b>AGORA-WAVE4(timeline issuePressure): every adapted event is politically inert today, and
    /// this is recorded rather than worked around.</b> Not one entry in <c>timeline_global.json</c>,
    /// <c>timeline_eu.json</c> or <c>timeline_na.json</c> authors an <c>issuePressure</c>, so
    /// <see cref="TimelineEvent.IssuePressure"/> is <see cref="IssuePosition.Centre"/> on all 120, all
    /// six components zero. Combined with the empty effect lists above, a wrapped event currently
    /// changes no number in the engine: it is a story the player reads and answers, and the answer
    /// moves political power through the story cycle's own <c>enfranchisementWeight</c> and
    /// <c>alienationWeight</c>, but it presses no issue.
    /// </para>
    /// <para>
    /// <b>The repair is two pieces of work and the second is the easy one to miss.</b>
    /// <c>TimelineCatalogLoader</c> reads <c>issuePressure</c>, but
    /// <c>data/schemas/timeline.schema.json</c> <b>does not declare it</b> and the event object is
    /// <c>additionalProperties: false</c> — a one-sided sync between loader and schema that predates
    /// this class. Authoring a pressure into a shipped catalog therefore fails the schema suite on the
    /// spot. Wave 4 needs a <c>/schema-change</c> declaring the property (non-negotiable #9)
    /// <b>and then</b> the authoring pass, on catalogs that are frozen this wave. The mapping below
    /// picks the numbers up the moment both exist.
    /// </para>
    /// <para>
    /// <b>The equal active / success / failure magnitudes are a placeholder ratio, not a settled
    /// shape.</b> The owner ruling fixes the <i>direction</i> — all three point the same way and
    /// nothing may flip a sign — but the authored convention is a volume knob: normally louder on
    /// failure, quieter on success. A generic wrapper has no way to know how far it should rise or
    /// fall, and any ratio invented here would be a coefficient in C# with no tuning key behind it,
    /// which is what the severity threshold below was blocked for. Carrying the one authored number
    /// unchanged on all three is the honest placeholder until wave 4 settles the shape — as a ratio in
    /// tuning, or as three authored magnitudes per timeline event. Deriving a pressure from tags was
    /// rejected on the same ground: the direction could be guessed from a tag, the magnitude could not.
    /// </para>
    /// <para>
    /// Pure and deterministic: no clock, no RNG, no dictionary iteration. The same event and tuning
    /// produce the same civic event every time.
    /// </para>
    /// </remarks>
    public sealed class TimelineEventAdapter
    {
        /// <summary>
        /// Prefixed onto the timeline id to form the adapted civic event's id.
        /// </summary>
        /// <remarks>
        /// A namespace rather than decoration. Authored civic events are prefixed <c>glob-</c>,
        /// <c>eu-</c> and <c>na-</c> by lane convention, and the story archive is keyed by event id —
        /// so a wrapped <c>na-inflation-peak</c> sharing an id with an authored one would be two
        /// different events under one key. <see cref="TimelineIdOf"/> is the inverse.
        /// </remarks>
        public const string AdaptedIdPrefix = "timeline-";

        /// <summary>
        /// The metric a wrapped event's check reads.
        /// </summary>
        /// <remarks>
        /// Happiness, at city scope, is the one reading every world event plausibly touches and the one
        /// a player can move by governing. It is also outside the census gate, so it can carry an
        /// absolute threshold — which matters here because the wrapper has no authored metric to fall
        /// back on and a check it cannot express would be scored <c>Unmeasurable</c> forever.
        /// </remarks>
        public const string CheckMetricId = MetricRegistry.Happiness;

        private readonly TimelineAdaptationPolicy _policy;

        /// <summary>Wraps every event: the behaviour of a policy file with no entries.</summary>
        public TimelineEventAdapter() : this(TimelineAdaptationPolicy.WrapAll) { }

        public TimelineEventAdapter(TimelineAdaptationPolicy policy)
        {
            _policy = policy ?? TimelineAdaptationPolicy.WrapAll;
        }

        /// <summary>The policy this adapter reads.</summary>
        public TimelineAdaptationPolicy Policy
        {
            get { return _policy; }
        }

        /// <summary>
        /// What this timeline event becomes: a wrapped civic event, nothing, or a deferral to an
        /// authored civic event named in the outcome.
        /// </summary>
        /// <remarks>
        /// The three cases are distinguished by <see cref="AdaptationOutcome.Kind"/> rather than by a
        /// null payload — see the remarks on <see cref="AdaptationOutcome"/> for why that distinction
        /// is worth a struct.
        /// </remarks>
        public AdaptationOutcome Adapt(TimelineEvent? timelineEvent, EngineTuning tuning)
        {
            if (timelineEvent == null) return AdaptationOutcome.NoEvent;

            TimelineAdaptationKind kind = _policy.KindFor(timelineEvent.Id);
            switch (kind)
            {
                case TimelineAdaptationKind.None:
                    return AdaptationOutcome.Dropped;

                case TimelineAdaptationKind.Authored:
                    return AdaptationOutcome.Authored(_policy.AuthoredCivicEventIdFor(timelineEvent.Id));

                default:
                    return AdaptationOutcome.Wrapped(Wrap(timelineEvent, tuning));
            }
        }

        /// <summary>
        /// The wrapping itself, with no policy consulted. Exposed for the caller that has already
        /// decided — and so the mapping can be tested without a policy document in the way.
        /// </summary>
        public static CivicEvent Wrap(TimelineEvent timelineEvent, EngineTuning tuning)
        {
            if (timelineEvent == null) throw new ArgumentNullException(nameof(timelineEvent));
            if (tuning == null) throw new ArgumentNullException(nameof(tuning));

            int severity = ClampSeverity(timelineEvent.Severity, tuning);
            IssuePosition pressure = timelineEvent.IssuePressure.Clamped();

            return new CivicEvent
            {
                Id = AdaptedIdPrefix + timelineEvent.Id,
                Severity = severity,
                Region = timelineEvent.Region,
                Trigger = ManualTrigger(),
                Check = SeverityCheck(severity, tuning),

                // Empty by decision, not by omission — see the class remarks on double application.
                ActiveEffects = new List<string>(),
                SuccessEffects = new List<string>(),
                FailureEffects = new List<string>(),

                // Salience, not credit (the owner ruling on CivicEvent.ActivePressure): all three
                // point the SAME way and differ only in magnitude, because an issue does not change
                // sides depending on the outcome. A mirror-negated success would not release the
                // issue — AffinityEngine.EventTerm dot-products the position against each party's
                // platform, so it would move voters to the opposite pole and reward the party that
                // was against fixing it. The three are equal here rather than merely same-signed
                // because a generic wrapper has no way to know how far salience rises on failure or
                // falls on success, and any ratio it picked would be a coefficient with no tuning key
                // behind it. An authored civic event states all three magnitudes; this one carries the
                // one number it was given. Clamped like every other pressure producer.
                ActivePressure = pressure,
                SuccessPressure = pressure,
                FailurePressure = pressure,

                // Empty means "felt evenly". Real history does not know the player's districts, which
                // is the same reason a catalog effect never names one.
                DistrictAffinity = new List<string>(),
                Tags = SortedDistinct(timelineEvent.Tags),

                Name = timelineEvent.Title,
                Description = timelineEvent.HeadlineBrief,
                IgnoreText = IgnoreProse,
                GoalText = GoalProse,
                PowerOverrideText = PowerOverrideProse,
                SuccessText = SuccessProse,
                FailText = FailProse
            };
        }

        /// <summary>The timeline event id an adapted civic event id came from. The inverse of the prefix.</summary>
        public static string TimelineIdOf(string civicEventId)
        {
            if (string.IsNullOrEmpty(civicEventId)) return "";
            if (civicEventId.Length <= AdaptedIdPrefix.Length) return "";
            if (string.CompareOrdinal(civicEventId, 0, AdaptedIdPrefix, 0, AdaptedIdPrefix.Length) != 0) return "";

            return civicEventId.Substring(AdaptedIdPrefix.Length);
        }

        // ------------------------------------------------------------------------------ the mapping

        private static TriggerSpec ManualTrigger()
        {
            // MetricId stays empty: a Manual spec reads no city state, and an id on one is ignored
            // anyway (the civic loader warns about exactly that).
            return new TriggerSpec
            {
                Kind = TriggerKind.Manual,
                MetricId = "",
                Comparison = Comparison.GreaterThanOrEqual,
                Threshold = 0.0,
                WindowMonths = 0,
                Scope = TriggerScope.City
            };
        }

        /// <summary>
        /// The check derived from severity: hold the city's mood up through what is happening to it.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Relative to the baseline captured when the story opened, because that is the only fair
        /// question to ask about an event the city did not cause: not "is the city happy", which a
        /// world crisis can settle on its own, but "is it happier than when this landed".
        /// </para>
        /// <para>
        /// Severity sets how much better than the baseline counts as an answer, and the demand falls
        /// linearly with severity: <c>goalPoints × (severityMax − severity + 1) / severityMax</c>, so a
        /// severity-1 event is asked for the full <c>stories.wrappedEventHappinessGoalPoints</c> and
        /// the most severe is asked for one <c>severityMax</c>-th of it.
        /// </para>
        /// <para>
        /// <b>It falls to a nonzero floor, and that is an owner ruling rather than a rounding
        /// choice.</b> An earlier version terminated at exactly +0.0, which made the top of the scale
        /// "did the population mean happen to not drift down", decided by noise and — with
        /// <see cref="Comparison.GreaterThanOrEqual"/> — passing an exactly flat city. That is the
        /// <see cref="StoryTier.Mandatory"/> tier, the highest-stakes story class there is, so it is
        /// the last one that should be settled by a coin flip. A floor makes the most severe events
        /// ask for something small and real instead of for nothing.
        /// </para>
        /// <para>
        /// <b>The unit is happiness points on the 0–100 scale, and it has its own key for that
        /// reason.</b> This was first written against <c>catalog.severityEffectScale</c>, which is a
        /// dimensionless effect-magnitude multiplier everywhere else it appears — that produced a
        /// severity-1-to-5 spread of 0.8 points out of 100, narrower than the month-to-month drift of
        /// a population mean, so all five severities collapsed into "has happiness not fallen" and the
        /// most severe asked for nothing at all. It also left a trap: raising the multiplier to make
        /// severe timeline events hit harder would have quietly made minor wrapped goals harder, in a
        /// different unit, from a file the tuner was not editing. Avoiding a literal by borrowing a
        /// number that means something else is not the same as reading a value from tuning.
        /// </para>
        /// </remarks>
        private static CheckSpec SeverityCheck(int severity, EngineTuning tuning)
        {
            int severityMax = tuning.Catalog.SeverityMax;
            if (severityMax < 1) severityMax = 1;

            double goalPoints = tuning.Stories.WrappedEventHappinessGoalPoints;
            if (!IsFinite(goalPoints) || goalPoints < 0.0) goalPoints = 0.0;

            // One step above the headroom, which is what keeps the most severe events off zero: at
            // severity == severityMax this is 1 rather than 0. Clamped at 1 rather than 0 for the same
            // reason, so a severity above the tuned maximum still asks for the floor and not for
            // nothing. severityMax is already at least 1, so the division is defined — and at
            // severityMax == 1 the single tier gets the full demand, which is the only sensible
            // reading of a scale with one point on it.
            int steps = severityMax - severity + 1;
            if (steps < 1) steps = 1;
            if (steps > severityMax) steps = severityMax;

            double threshold = goalPoints * steps / severityMax;

            return new CheckSpec
            {
                Spec = new TriggerSpec
                {
                    Kind = TriggerKind.Metric,
                    MetricId = CheckMetricId,
                    Comparison = Comparison.GreaterThanOrEqual,
                    Threshold = threshold,
                    WindowMonths = 0,
                    Scope = TriggerScope.City
                },
                RelativeToBaseline = true
            };
        }

        private static int ClampSeverity(int severity, EngineTuning tuning)
        {
            int severityMax = tuning.Catalog.SeverityMax;
            if (severityMax < 1) severityMax = 1;

            if (severity < 1) return 1;
            if (severity > severityMax) return severityMax;
            return severity;
        }

        /// <summary>Sorted ordinal and de-duplicated, the order <see cref="CivicEvent.Tags"/> declares.</summary>
        private static List<string> SortedDistinct(List<string>? tags)
        {
            var sorted = new List<string>();
            if (tags == null) return sorted;

            for (int i = 0; i < tags.Count; i++)
            {
                if (!string.IsNullOrEmpty(tags[i])) sorted.Add(tags[i]);
            }

            sorted.Sort(StringComparer.Ordinal);

            var distinct = new List<string>(sorted.Count);
            for (int i = 0; i < sorted.Count; i++)
            {
                if (i > 0 && string.CompareOrdinal(sorted[i - 1], sorted[i]) == 0) continue;
                distinct.Add(sorted[i]);
            }

            return distinct;
        }

        private static bool IsFinite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);

        // ------------------------------------------------------------------------- the four responses
        //
        // Five prose fields the timeline event does not carry, and every one of them is rendered on a
        // surface the player acts from: an empty GoalText is a button with no label. They are the same
        // for every wrapped event on purpose. A generic wrapper knows the headline and nothing else —
        // it has no effect ids and no authored resolution — so prose naming a specific remedy would
        // promise something the event cannot do, which is the rule the content lanes are held to as
        // well. Name and Description carry all the specificity there is, and wave 5's flavor pass may
        // rewrite how these read without any number depending on them.

        private const string IgnoreProse =
            "Say nothing. Let it be somebody else's emergency and hope the city is not asked about it.";

        private const string GoalProse =
            "Answer it locally: get out in front of the disruption and hold the city's mood up while it lasts.";

        private const string PowerOverrideProse =
            "Spend the administration's standing to be seen ahead of this, whatever the city itself does about it.";

        private const string SuccessProse =
            "The city came through it in better shape than it started, and the administration is credited with steadiness.";

        private const string FailProse =
            "The city's mood slipped while the administration talked, and the opposition has the quote to prove it.";
    }
}
