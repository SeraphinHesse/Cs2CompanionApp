using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using Agora.Core.Contracts;
using Agora.Core.Events.Catalog;
using Agora.Core.Tuning;
using Xunit;

namespace Agora.Core.Tests
{
    /// <summary>
    /// The gate on <c>data/timeline_eu.json</c>, <c>data/timeline_na.json</c> and
    /// <c>data/timeline_global.json</c> — 120 curated events written by three agents who could not
    /// see each other's files and could not run a build.
    ///
    /// <para>
    /// <b>Why this exists when <see cref="ShippedTuningTests.ShippedTimelineCatalog_LoadsWithNothingRejected"/>
    /// already loads each file.</b> That test loads them <i>one at a time</i>, and a per-file load
    /// cannot see the two things that actually go wrong when catalogs are authored in parallel: an id
    /// claimed in two files, and a cap that only holds until severity scaling is applied. It also
    /// treats every warning as acceptable. This suite loads all three <b>as one merged catalog</b>,
    /// the way the scheduler will, and holds the merged result to a stricter standard.
    /// </para>
    ///
    /// <para>
    /// Each catalog invariant is asserted twice on purpose: once through
    /// <see cref="TimelineCatalogLoader"/>, and once against the raw JSON with arithmetic written out
    /// here. The loader is the thing being trusted at runtime, so a test that only asks the loader
    /// whether the loader is happy proves very little — if a validation rule is ever dropped from it,
    /// the independent pass is what notices.
    /// </para>
    /// </summary>
    public class ShippedTimelineCatalogTests
    {
        private static readonly string[] CatalogFiles =
        {
            "timeline_global.json",
            "timeline_eu.json",
            "timeline_na.json",
        };

        // ------------------------------------------------------------------ fixtures

        private static string RepoRoot()
        {
            // AppContext.BaseDirectory, not Environment.CurrentDirectory: the runner's cwd varies,
            // the assembly's own location does not.
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null)
            {
                if (File.Exists(Path.Combine(dir.FullName, "Agora.sln"))) return dir.FullName;
                dir = dir.Parent;
            }

            throw new InvalidOperationException(
                "Could not locate the repository root (no Agora.sln above " + AppContext.BaseDirectory + ").");
        }

        private static string CatalogPath(string fileName) => Path.Combine(RepoRoot(), "data", fileName);

        /// <summary>
        /// The tuning the game will actually run on, not <see cref="EngineTuning.Default"/>. The
        /// caps these catalogs are measured against live in that file, and
        /// <see cref="ShippedTuningTests.ShippedTuningFile_MatchesBuiltInDefaults"/> separately proves
        /// the two agree — so reading the file here means a future tuning change is checked against
        /// the catalogs on the same run that introduces it.
        /// </summary>
        private static EngineTuning ShippedTuning() =>
            EngineTuning.FromJson(File.ReadAllText(Path.Combine(RepoRoot(), "data", "engine_tuning.json")));

        private static List<TimelineCatalogSource> AllSources()
        {
            var sources = new List<TimelineCatalogSource>(CatalogFiles.Length);
            foreach (string file in CatalogFiles)
            {
                string path = CatalogPath(file);
                Assert.True(File.Exists(path), file + " must ship.");
                sources.Add(new TimelineCatalogSource(file, File.ReadAllText(path)));
            }

            return sources;
        }

        private static TimelineCatalogLoadResult LoadMerged(EngineTuning tuning) =>
            TimelineCatalogLoader.Load(AllSources(), tuning);

        private static string Describe(IReadOnlyList<CatalogIssue> issues)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < issues.Count; i++) sb.Append(Environment.NewLine).Append("  ").Append(issues[i]);
            return sb.ToString();
        }

        // ------------------------------------------------------------------ the merged load

        /// <summary>
        /// The whole timeline loads with nothing rejected. This is the load the scheduler performs,
        /// so anything it refuses is history that will never fire in a real save.
        /// </summary>
        [Fact]
        public void MergedCatalog_LoadsWithNothingRejected()
        {
            TimelineCatalogLoadResult result = LoadMerged(ShippedTuning());

            var errors = new List<CatalogIssue>();
            for (int i = 0; i < result.Errors.Count; i++) errors.Add(result.Errors[i]);

            Assert.True(errors.Count == 0, "The merged timeline has authoring errors:" + Describe(errors));
            Assert.Equal(0, result.RejectedEventCount);
            Assert.True(result.IsValid);
        }

        /// <summary>
        /// No warnings either.
        /// </summary>
        /// <remarks>
        /// Warnings do not reject an entry, which is exactly why they need a test: every one of them
        /// names something that will misbehave at runtime rather than at load — a magnitude that the
        /// sink will silently clamp, a property the loader ignored, a date outside the catalog
        /// window. Left unasserted they accumulate until nobody reads the list.
        /// </remarks>
        [Fact]
        public void MergedCatalog_LoadsWithoutWarnings()
        {
            TimelineCatalogLoadResult result = LoadMerged(ShippedTuning());

            var warnings = new List<CatalogIssue>();
            for (int i = 0; i < result.Warnings.Count; i++) warnings.Add(result.Warnings[i]);

            Assert.True(warnings.Count == 0, "The merged timeline loads with warnings:" + Describe(warnings));
        }

        /// <summary>
        /// Every event in every file survives the merge — i.e. no id is claimed twice across files.
        /// </summary>
        /// <remarks>
        /// A cross-file duplicate is the characteristic failure of authoring three catalogs in
        /// parallel, and it is invisible to a per-file test: both files load clean on their own, and
        /// the merged catalog quietly holds one fewer event than the sum of its parts. Asserting the
        /// count, rather than looking for the duplicate warning, also catches an entry lost for any
        /// other reason.
        /// </remarks>
        [Fact]
        public void MergedCatalog_KeepsEveryEventFromEveryFile()
        {
            EngineTuning tuning = ShippedTuning();

            int perFileTotal = 0;
            var seen = new Dictionary<string, string>(StringComparer.Ordinal);
            var duplicates = new List<string>();

            foreach (string file in CatalogFiles)
            {
                TimelineCatalogLoadResult one = TimelineCatalogLoader.Load(
                    file, File.ReadAllText(CatalogPath(file)), tuning);

                perFileTotal += one.Catalog.Count;

                foreach (TimelineEvent e in one.Catalog.Events)
                {
                    string? owner;
                    if (seen.TryGetValue(e.Id, out owner))
                    {
                        duplicates.Add("'" + e.Id + "' is claimed by both " + owner + " and " + file);
                    }
                    else
                    {
                        seen.Add(e.Id, file);
                    }
                }
            }

            Assert.True(duplicates.Count == 0,
                        "Timeline event ids must be unique across every catalog:" + Environment.NewLine +
                        string.Join(Environment.NewLine, duplicates));

            TimelineCatalogLoadResult merged = LoadMerged(tuning);
            Assert.Equal(perFileTotal, merged.Catalog.Count);
        }

        /// <summary>Each catalog contributes events, and every event carries its file's region.</summary>
        [Theory]
        [InlineData("timeline_global.json", EventRegion.Global)]
        [InlineData("timeline_eu.json", EventRegion.Eu)]
        [InlineData("timeline_na.json", EventRegion.Na)]
        public void EachCatalog_IsNonEmptyAndSingleRegion(string fileName, EventRegion expected)
        {
            TimelineCatalogLoadResult result = TimelineCatalogLoader.Load(
                fileName, File.ReadAllText(CatalogPath(fileName)), ShippedTuning());

            Assert.True(result.Catalog.Count > 0, fileName + " must contain events.");

            foreach (TimelineEvent e in result.Catalog.Events)
            {
                Assert.True(e.Region == expected,
                            fileName + " event '" + e.Id + "' is region " + e.Region + ", expected " + expected);
            }
        }

        // ------------------------------------------------------------------ independent re-validation

        /// <summary>
        /// Every <c>effectId</c> names an entry in the shipped effect palette, and declares that
        /// entry's scope.
        /// </summary>
        /// <remarks>
        /// The single most likely way a catalog rots: §7's palette is closed, an author reaches for
        /// a consequence it does not offer, and the event loads with one effect missing. Checked here
        /// against <c>effects.perEffect</c> read straight from the tuning file, not through the
        /// loader that already made the same check.
        /// </remarks>
        [Fact]
        public void EveryEffectReference_ExistsInTheShippedPaletteWithTheDeclaredScope()
        {
            EngineTuning tuning = ShippedTuning();
            var problems = new List<string>();

            foreach (TimelineEvent e in LoadMerged(tuning).Catalog.Events)
            {
                foreach (TimelineEventEffect effect in e.Effects)
                {
                    EffectCap cap;
                    if (!tuning.Effects.TryGetEffect(effect.EffectId, out cap))
                    {
                        problems.Add(e.Id + ": '" + effect.EffectId + "' is not in effects.perEffect");
                        continue;
                    }

                    if (cap.Scope != effect.Scope)
                    {
                        problems.Add(e.Id + ": '" + effect.EffectId + "' is declared " + effect.Scope +
                                     " but the palette says " + cap.Scope);
                    }

                    // Real history does not know the player's map (non-negotiable #4 territory): the
                    // scheduler picks a district deterministically at fire time.
                    if (effect.DistrictId != null)
                    {
                        problems.Add(e.Id + ": '" + effect.EffectId + "' names a district, which a " +
                                     "catalog entry may never do");
                    }
                }
            }

            Assert.True(problems.Count == 0,
                        "Timeline effects reference the palette incorrectly:" + Environment.NewLine +
                        string.Join(Environment.NewLine, problems));
        }

        /// <summary>
        /// Every authored magnitude and duration is inside its effect's cap and inside the global
        /// caps (non-negotiable #5).
        /// </summary>
        [Fact]
        public void EveryEffect_IsInsideItsMagnitudeAndDurationCap()
        {
            EngineTuning tuning = ShippedTuning();
            double globalMagnitudeCap = Math.Abs(tuning.Effects.GlobalMagnitudeCap);
            int globalDurationCap = tuning.Effects.GlobalDurationCapMonths;

            var problems = new List<string>();

            foreach (TimelineEvent e in LoadMerged(tuning).Catalog.Events)
            {
                foreach (TimelineEventEffect effect in e.Effects)
                {
                    EffectCap cap;
                    if (!tuning.Effects.TryGetEffect(effect.EffectId, out cap)) continue; // reported above

                    double magnitudeCap = Math.Min(Math.Abs(cap.MagnitudeCap), globalMagnitudeCap);
                    int durationCap = Math.Min(cap.DurationCapMonths, globalDurationCap);

                    if (!IsFinite(effect.Magnitude))
                    {
                        problems.Add(Where(e, effect) + ": magnitude is not finite");
                        continue;
                    }

                    if (Math.Abs(effect.Magnitude) > magnitudeCap + 1e-12)
                    {
                        problems.Add(Where(e, effect) + ": |" + Num(effect.Magnitude) + "| exceeds the cap " +
                                     Num(magnitudeCap));
                    }

                    if (Math.Abs(effect.Magnitude) < tuning.Effects.MinEffectiveMagnitude)
                    {
                        problems.Add(Where(e, effect) + ": " + Num(effect.Magnitude) +
                                     " is below effects.minEffectiveMagnitude " +
                                     Num(tuning.Effects.MinEffectiveMagnitude) + ", so it would do nothing");
                    }

                    if (effect.DurationMonths < 0 || effect.DurationMonths > durationCap)
                    {
                        problems.Add(Where(e, effect) + ": duration " + effect.DurationMonths +
                                     " months is outside 0.." + durationCap);
                    }
                }
            }

            Assert.True(problems.Count == 0,
                        "Timeline effects breach their declared caps:" + Environment.NewLine +
                        string.Join(Environment.NewLine, problems));
        }

        /// <summary>
        /// A magnitude still fits its cap <i>after</i> severity scaling, so nothing is silently
        /// clamped at runtime.
        /// </summary>
        /// <remarks>
        /// The catalog validator only checks the authored number, and the sink clamps the scaled one.
        /// Between those two is a gap an author cannot see: a severity-5 event authored at the cap
        /// requests 1.8× the cap and gets 1.0× of it, so raising the severity of an event changes its
        /// consequences by nothing at all. That is not a crash and not an error — which is precisely
        /// why it needs a test.
        /// </remarks>
        [Fact]
        public void EveryEffect_SurvivesSeverityScalingWithoutBeingClamped()
        {
            EngineTuning tuning = ShippedTuning();
            double globalMagnitudeCap = Math.Abs(tuning.Effects.GlobalMagnitudeCap);
            double scale = tuning.Effects.SeverityMagnitudeScale;

            var problems = new List<string>();

            foreach (TimelineEvent e in LoadMerged(tuning).Catalog.Events)
            {
                double factor = 1.0 + scale * (e.Severity - 1);

                foreach (TimelineEventEffect effect in e.Effects)
                {
                    EffectCap cap;
                    if (!tuning.Effects.TryGetEffect(effect.EffectId, out cap)) continue;

                    double magnitudeCap = Math.Min(Math.Abs(cap.MagnitudeCap), globalMagnitudeCap);
                    double scaled = Math.Abs(effect.Magnitude) * factor;

                    if (scaled > magnitudeCap + 1e-12)
                    {
                        problems.Add(Where(e, effect) + ": severity " + e.Severity + " scales " +
                                     Num(effect.Magnitude) + " to " + Num(scaled) + ", past the cap " +
                                     Num(magnitudeCap) + " — the sink would clamp it");
                    }
                }
            }

            Assert.True(problems.Count == 0,
                        "Timeline effects would be clamped after severity scaling:" + Environment.NewLine +
                        string.Join(Environment.NewLine, problems));
        }

        /// <summary>
        /// Every date is a real calendar date inside the catalog window, and every severity is inside
        /// <c>catalog.severityMax</c>.
        /// </summary>
        [Fact]
        public void EveryEvent_HasARealDateInsideTheWindowAndAValidSeverity()
        {
            EngineTuning tuning = ShippedTuning();
            int startYear = tuning.Catalog.StartYear;
            int endYear = tuning.Catalog.CatalogEndYear;
            int severityMax = tuning.Catalog.SeverityMax;

            var problems = new List<string>();

            foreach (TimelineEvent e in LoadMerged(tuning).Catalog.Events)
            {
                // The loader has already parsed the string into a SimDate; re-checking it against
                // DateTime is what catches a month/day the loader's own calendar accepts and the real
                // one does not (the packets flagged a Feb-29-every-year leniency).
                try
                {
                    var _ = new DateTime(e.Date.Year, e.Date.Month, e.Date.Day);
                }
                catch (ArgumentOutOfRangeException)
                {
                    problems.Add(e.Id + ": " + e.Date + " is not a real calendar date");
                }

                if (e.Date.Year < startYear || e.Date.Year > endYear)
                {
                    problems.Add(e.Id + ": " + e.Date + " is outside the catalog window " +
                                 startYear + ".." + endYear);
                }

                if (e.Severity < 1 || e.Severity > severityMax)
                {
                    problems.Add(e.Id + ": severity " + e.Severity + " is outside 1.." + severityMax);
                }

                if (string.IsNullOrEmpty(e.HeadlineBrief))
                {
                    problems.Add(e.Id + ": headlineBrief is empty, so the LLM has nothing to write from");
                }
            }

            Assert.True(problems.Count == 0,
                        "Timeline events have malformed dates, severities or briefs:" + Environment.NewLine +
                        string.Join(Environment.NewLine, problems));
        }

        /// <summary>
        /// The merged catalog is ordered by date then id, which is the order the scheduler walks.
        /// </summary>
        /// <remarks>
        /// Contractual, not cosmetic: if the order depended on which file was read first, or on a
        /// dictionary's enumeration order, one save would produce different history on different runs
        /// — the exact determinism failure <c>Agora.Core/CLAUDE.md</c> calls the most common silent one.
        /// </remarks>
        [Fact]
        public void MergedCatalog_IsOrderedByDateThenId()
        {
            IReadOnlyList<TimelineEvent> events = LoadMerged(ShippedTuning()).Catalog.Events;

            for (int i = 1; i < events.Count; i++)
            {
                TimelineEvent previous = events[i - 1];
                TimelineEvent current = events[i];

                int byDate = previous.Date.CompareTo(current.Date);
                if (byDate < 0) continue;

                Assert.True(byDate == 0,
                            "Catalog order broke at index " + i + ": " + previous.Date + " (" + previous.Id +
                            ") precedes " + current.Date + " (" + current.Id + ")");

                Assert.True(string.CompareOrdinal(previous.Id, current.Id) < 0,
                            "Same-date events are out of id order at index " + i + ": '" + previous.Id +
                            "' precedes '" + current.Id + "'");
            }
        }

        /// <summary>
        /// The merged load is a pure function of its inputs: loading twice yields the same sequence.
        /// </summary>
        [Fact]
        public void MergedCatalog_LoadsDeterministically()
        {
            EngineTuning tuning = ShippedTuning();

            string a = Fingerprint(LoadMerged(tuning).Catalog);
            string b = Fingerprint(LoadMerged(tuning).Catalog);

            Assert.Equal(a, b);
        }

        // ------------------------------------------------------------------ schema conformance

        /// <summary>
        /// Every event object carries only properties <c>data/schemas/timeline.schema.json</c>
        /// declares, since that schema sets <c>additionalProperties: false</c>.
        /// </summary>
        /// <remarks>
        /// Two packets reported the same contract mismatch here and both complied with the schema:
        /// <c>TimelineCatalogLoader</c> reads an optional <c>issuePressure</c> object that the schema
        /// forbids. No shipped event uses it, so the two agree in practice — this test is what keeps
        /// them agreeing, and what will fail the day an author writes the field the loader
        /// documents. Resolving the mismatch is a <c>/schema-change</c>, not a test change.
        /// </remarks>
        [Fact]
        public void EveryEvent_UsesOnlyPropertiesTheShippedSchemaDeclares()
        {
            string schemaPath = Path.Combine(RepoRoot(), "data", "schemas", "timeline.schema.json");
            Assert.True(File.Exists(schemaPath), "data/schemas/timeline.schema.json must ship.");

            HashSet<string> declared = DeclaredEventProperties(File.ReadAllText(schemaPath));
            Assert.True(declared.Count > 0, "Could not read the event property list out of the timeline schema.");

            var problems = new List<string>();

            foreach (string file in CatalogFiles)
            {
                foreach (string property in EventPropertiesUsedIn(File.ReadAllText(CatalogPath(file))))
                {
                    if (!declared.Contains(property))
                    {
                        problems.Add(file + ": events[] uses '" + property +
                                     "', which timeline.schema.json does not declare");
                    }
                }
            }

            Assert.True(problems.Count == 0,
                        "Timeline entries carry properties the schema forbids:" + Environment.NewLine +
                        string.Join(Environment.NewLine, problems));
        }

        // ------------------------------------------------------------------ helpers

        private static string Where(TimelineEvent e, TimelineEventEffect effect) =>
            e.Id + " / " + effect.EffectId;

        private static string Num(double value) => value.ToString("R", CultureInfo.InvariantCulture);

        private static bool IsFinite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);

        private static string Fingerprint(TimelineCatalog catalog)
        {
            var sb = new StringBuilder();
            foreach (TimelineEvent e in catalog.Events)
            {
                sb.Append(e.Date).Append('|').Append(e.Id).Append('|').Append(e.Region).Append('|')
                  .Append(e.Severity).Append('|').Append(e.DurationMonths);

                foreach (TimelineEventEffect effect in e.Effects)
                {
                    sb.Append('|').Append(effect.EffectId).Append(':').Append(effect.Scope).Append(':')
                      .Append(Num(effect.Magnitude)).Append(':').Append(effect.DurationMonths);
                }

                sb.Append('\n');
            }

            return sb.ToString();
        }

        /// <summary>
        /// The property names the timeline schema declares for one <c>events[]</c> entry.
        /// </summary>
        /// <remarks>
        /// <c>System.Text.Json</c> rather than a JSON package: it is part of net8.0, and this is the
        /// one suite that must build and pass on a bare machine with nothing installed. Agora.Core's
        /// own parser is <c>internal</c> and stays that way.
        /// </remarks>
        private static HashSet<string> DeclaredEventProperties(string schemaJson)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);

            using JsonDocument document = JsonDocument.Parse(schemaJson);

            // Tolerate either shape: a schema for the whole file ($.properties.events.items) or a
            // schema for the array alone ($.items).
            JsonElement? eventSchema = Descend(document.RootElement, "properties", "events", "items")
                                       ?? Descend(document.RootElement, "items");
            if (eventSchema == null) return names;

            JsonElement? properties = Descend(eventSchema.Value, "properties");
            if (properties == null || properties.Value.ValueKind != JsonValueKind.Object) return names;

            foreach (JsonProperty property in properties.Value.EnumerateObject()) names.Add(property.Name);
            return names;
        }

        /// <summary>Every distinct property name used by any <c>events[]</c> entry in a catalog.</summary>
        private static IEnumerable<string> EventPropertiesUsedIn(string catalogJson)
        {
            var names = new SortedSet<string>(StringComparer.Ordinal);

            using JsonDocument document = JsonDocument.Parse(catalogJson);

            JsonElement? events = Descend(document.RootElement, "events");
            if (events == null || events.Value.ValueKind != JsonValueKind.Array) return names;

            foreach (JsonElement entry in events.Value.EnumerateArray())
            {
                if (entry.ValueKind != JsonValueKind.Object) continue;
                foreach (JsonProperty property in entry.EnumerateObject()) names.Add(property.Name);
            }

            return names;
        }

        private static JsonElement? Descend(JsonElement element, params string[] path)
        {
            JsonElement current = element;

            for (int i = 0; i < path.Length; i++)
            {
                if (current.ValueKind != JsonValueKind.Object) return null;

                JsonElement next;
                if (!current.TryGetProperty(path[i], out next)) return null;
                current = next;
            }

            return current;
        }
    }
}
