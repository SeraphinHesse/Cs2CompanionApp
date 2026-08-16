using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Agora.Core.Contracts;
using Agora.Core.Events.Catalog;
using Agora.Core.Stories.Catalog;
using Agora.Core.Tuning;
using Xunit;

namespace Agora.Core.Tests
{
    /// <summary>
    /// The EU third of the pressure gate: every event in <c>data/timeline_eu.json</c> the adaptation
    /// policy wraps must author an <c>issuePressure</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A wrapped event with no pressure is a story card that asks the city to argue about nothing:
    /// <c>AffinityEngine</c> dot-products the pressure against each party's platform, and
    /// <see cref="IssuePosition.Centre"/> dots to zero on every axis. The gate is per catalog file
    /// rather than catalog-wide because the three content lanes land independently; the union of
    /// this, <c>TimelineGlobalPressureTests</c> and <c>TimelineNaPressureTests</c> is the whole gate.
    /// </para>
    /// <para>
    /// <b>Presence, not values.</b> What an event presses is an authoring judgement, and a test that
    /// pinned a number would go red on the next calibration pass for a reason unrelated to what it
    /// guards. The wrap decision is walked through <see cref="TimelineAdaptationPolicy"/> rather than
    /// re-implemented, so a change to the default rule moves this test with it instead of past it.
    /// </para>
    /// </remarks>
    public class TimelineEuPressureTests
    {
        private const string CatalogFile = "timeline_eu.json";

        private static string RepoRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null)
            {
                if (File.Exists(Path.Combine(dir.FullName, "Agora.sln"))) return dir.FullName;
                dir = dir.Parent;
            }

            throw new InvalidOperationException(
                "Could not locate the repository root (no Agora.sln above " + AppContext.BaseDirectory + ").");
        }

        private static string DataPath(string fileName) => Path.Combine(RepoRoot(), "data", fileName);

        private static string ReadData(string fileName)
        {
            string path = DataPath(fileName);
            Assert.True(File.Exists(path), "data/" + fileName + " must ship.");
            return File.ReadAllText(path);
        }

        private static EngineTuning ShippedTuning() =>
            EngineTuning.FromJson(ReadData("engine_tuning.json"));

        private static TimelineAdaptationPolicy ShippedPolicy()
        {
            TimelineAdaptationPolicy policy;
            Assert.True(TimelineAdaptationPolicy.TryParse(ReadData("timeline_adaptation.json"), out policy),
                        "data/timeline_adaptation.json must parse.");
            return policy;
        }

        private static IReadOnlyList<TimelineEvent> EuEvents()
        {
            TimelineCatalogLoadResult result =
                TimelineCatalogLoader.Load(CatalogFile, ReadData(CatalogFile), ShippedTuning());

            Assert.True(result.Catalog.Count > 0, CatalogFile + " must contain events.");
            return result.Catalog.Events;
        }

        /// <summary>The ids whose event object literally carries the key, read straight from the file.</summary>
        /// <remarks>
        /// The loader reports an absent pressure and an authored all-zero one identically, as
        /// <see cref="IssuePosition.Centre"/>, so presence is asserted against the JSON and the
        /// loaded value is only used to prove the file's numbers survive the round trip.
        /// </remarks>
        private static HashSet<string> IdsCarryingPressure(string catalogJson)
        {
            var ids = new HashSet<string>(StringComparer.Ordinal);

            using JsonDocument document = JsonDocument.Parse(catalogJson);
            JsonElement events;
            if (!document.RootElement.TryGetProperty("events", out events)) return ids;

            foreach (JsonElement element in events.EnumerateArray())
            {
                JsonElement id;
                if (!element.TryGetProperty("id", out id) || id.ValueKind != JsonValueKind.String) continue;
                if (element.TryGetProperty("issuePressure", out _)) ids.Add(id.GetString() ?? "");
            }

            return ids;
        }

        private static bool IsPressed(IssuePosition pressure)
        {
            for (int i = 0; i < Issues.All.Count; i++)
            {
                if (pressure[Issues.All[i]] != 0.0) return true;
            }

            return false;
        }

        // ------------------------------------------------------------------ the gate

        /// <summary>Every generically wrapped EU event authors a pressure, and some EU event is wrapped.</summary>
        /// <remarks>
        /// The non-zero count is the guard against the vacuous pass: a misread policy that classified
        /// the whole file as <c>none</c> would otherwise satisfy the loop by iterating nothing.
        /// </remarks>
        [Fact]
        public void EveryWrappedEuEvent_AuthorsAnIssuePressure()
        {
            string catalogJson = ReadData(CatalogFile);
            TimelineAdaptationPolicy policy = ShippedPolicy();
            HashSet<string> carriesPressure = IdsCarryingPressure(catalogJson);

            int wrapped = 0;
            var missing = new List<string>();

            foreach (TimelineEvent e in EuEvents())
            {
                if (policy.KindFor(e.Id) != TimelineAdaptationKind.Generic) continue;

                wrapped++;
                if (!carriesPressure.Contains(e.Id) || !IsPressed(e.IssuePressure)) missing.Add(e.Id);
            }

            Assert.True(wrapped > 0,
                        "No event in " + CatalogFile + " is wrapped generically, so this gate would pass " +
                        "vacuously. Either the adaptation policy or the catalog was misread.");

            Assert.True(missing.Count == 0,
                        "Wrapped events press no issue and so move no vote:" + Environment.NewLine + "  " +
                        string.Join(Environment.NewLine + "  ", missing));
        }

        /// <summary>
        /// A <c>none</c> event authors no pressure. The work would be inert — it is never wrapped —
        /// and a pressure sitting on one reads as an oversight in either direction.
        /// </summary>
        [Fact]
        public void UnwrappedEuEvents_AuthorNoPressure()
        {
            TimelineAdaptationPolicy policy = ShippedPolicy();
            HashSet<string> carriesPressure = IdsCarryingPressure(ReadData(CatalogFile));

            var stray = new List<string>();

            foreach (TimelineEvent e in EuEvents())
            {
                if (policy.KindFor(e.Id) == TimelineAdaptationKind.Generic) continue;
                if (carriesPressure.Contains(e.Id)) stray.Add(e.Id);
            }

            Assert.True(stray.Count == 0,
                        "These events are never wrapped, so their pressure is never read:" +
                        Environment.NewLine + "  " + string.Join(Environment.NewLine + "  ", stray));
        }
    }
}
