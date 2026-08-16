using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using Agora.Core.Contracts;
using Agora.Core.Events.Catalog;
using Agora.Core.Stories.Catalog;
using Agora.Core.Tuning;
using Xunit;

namespace Agora.Core.Tests
{
    /// <summary>
    /// The gate on <c>data/timeline_global.json</c>: every event the adaptation policy wraps into a
    /// civic event must author an <c>issuePressure</c>.
    ///
    /// <para>
    /// Without one a wrapped event reaches <c>AffinityEngine</c> as <see cref="IssuePosition.Centre"/>,
    /// which dot-products to nothing against every platform — the player gets a story card that asks
    /// the city to argue about no issue at all and moves no vote. That is the defect this file exists
    /// to keep from coming back, one catalog at a time; <c>timeline_eu.json</c> and
    /// <c>timeline_na.json</c> carry their own gates beside their own content.
    /// </para>
    ///
    /// <para>
    /// <b>Presence, not values.</b> What each event should press is an authoring judgement, and a test
    /// that pinned a magnitude would go red on the next calibration pass for a reason unrelated to
    /// what it guards. Range is already enforced twice — by the schema and by
    /// <see cref="TimelineCatalogLoader"/> — so there is no third copy of it here.
    /// </para>
    /// </summary>
    public class TimelineGlobalPressureTests
    {
        private const string CatalogFile = "timeline_global.json";

        /// <summary>
        /// A floor rather than an exact count. The point is that a misread policy fails loudly instead
        /// of passing vacuously over an empty set; pinning the number would make adding a `none` entry
        /// to <c>timeline_adaptation.json</c> break a test about pressures.
        /// </summary>
        private const int MinimumWrappedEvents = 20;

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

        private static string DataPath(string fileName) => Path.Combine(RepoRoot(), "data", fileName);

        /// <summary>
        /// The tuning the game will actually run on rather than <see cref="EngineTuning.Default"/>, so
        /// this reads the catalog exactly as the runtime does.
        /// </summary>
        private static EngineTuning ShippedTuning() =>
            EngineTuning.FromJson(File.ReadAllText(DataPath("engine_tuning.json")));

        private static TimelineCatalog ShippedCatalog()
        {
            string path = DataPath(CatalogFile);
            Assert.True(File.Exists(path), CatalogFile + " must ship.");

            TimelineCatalogLoadResult result =
                TimelineCatalogLoader.Load(CatalogFile, File.ReadAllText(path), ShippedTuning());

            Assert.True(result.IsValid, CatalogFile + " must load with nothing rejected.");
            return result.Catalog;
        }

        /// <summary>
        /// The shipped policy, walked rather than re-implemented. Re-deriving "unlisted means generic"
        /// here would let this test and the runtime disagree about which events are wrapped, which is
        /// the one disagreement it exists to prevent.
        /// </summary>
        private static TimelineAdaptationPolicy ShippedPolicy()
        {
            Assert.True(
                TimelineAdaptationPolicy.TryParse(
                    File.ReadAllText(DataPath("timeline_adaptation.json")), out TimelineAdaptationPolicy policy),
                "timeline_adaptation.json must parse.");

            return policy;
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

        /// <summary>
        /// Every generically wrapped event in the global catalog presses at least one issue, read
        /// through the loader that the runtime uses.
        /// </summary>
        [Fact]
        public void EveryWrappedGlobalEvent_AuthorsAnIssuePressure()
        {
            TimelineCatalog catalog = ShippedCatalog();
            TimelineAdaptationPolicy policy = ShippedPolicy();

            int wrapped = 0;
            var inert = new List<string>();

            foreach (TimelineEvent evt in catalog.Events)
            {
                if (policy.KindFor(evt.Id) != TimelineAdaptationKind.Generic) continue;

                wrapped++;
                if (!IsPressed(evt.IssuePressure)) inert.Add(evt.Id);
            }

            Assert.True(wrapped >= MinimumWrappedEvents,
                CatalogFile + " should wrap at least " + MinimumWrappedEvents + " events; found " + wrapped +
                ". A count this low means the policy was misread, not that the catalog shrank.");

            Assert.True(inert.Count == 0,
                "Wrapped events with no issuePressure — each becomes a story that moves no vote:" +
                Describe(inert));
        }

        /// <summary>
        /// The same assertion taken against the raw JSON, because the loader answers
        /// <see cref="IssuePosition.Centre"/> both for a missing key and for one authored as six zeros.
        /// The first is the defect; the second would be a pointless way to satisfy the first.
        /// </summary>
        [Fact]
        public void EveryWrappedGlobalEvent_CarriesTheKeyInTheFileItself()
        {
            TimelineAdaptationPolicy policy = ShippedPolicy();

            using var document = JsonDocument.Parse(File.ReadAllText(DataPath(CatalogFile)));
            JsonElement events = document.RootElement.GetProperty("events");

            var missing = new List<string>();
            var unwrappedButAuthored = new List<string>();

            foreach (JsonElement evt in events.EnumerateArray())
            {
                string id = evt.GetProperty("id").GetString() ?? "";
                bool hasPressure = evt.TryGetProperty("issuePressure", out JsonElement _);

                if (policy.KindFor(id) == TimelineAdaptationKind.Generic)
                {
                    if (!hasPressure) missing.Add(id);
                }
                else if (hasPressure)
                {
                    unwrappedButAuthored.Add(id);
                }
            }

            Assert.True(missing.Count == 0,
                "Wrapped events with no issuePressure key in " + CatalogFile + ":" + Describe(missing));

            // A pressure on an event the policy never wraps is dead weight a reader would trust.
            Assert.True(unwrappedButAuthored.Count == 0,
                "Events marked 'none' in timeline_adaptation.json but carrying an issuePressure:" +
                Describe(unwrappedButAuthored));
        }

        private static string Describe(IReadOnlyList<string> ids)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < ids.Count; i++) sb.Append(Environment.NewLine).Append("  ").Append(ids[i]);
            return sb.ToString();
        }
    }
}
