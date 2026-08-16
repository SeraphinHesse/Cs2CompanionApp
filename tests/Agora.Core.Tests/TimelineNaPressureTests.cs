using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.Json;
using Agora.Core.Contracts;
using Agora.Core.Stories.Catalog;
using Xunit;

namespace Agora.Core.Tests
{
    /// <summary>
    /// The pressure gate for <c>data/timeline_na.json</c>: every event the adaptation policy wraps
    /// authors an <c>issuePressure</c>, so no NA story card arrives politically inert.
    /// </summary>
    /// <remarks>
    /// <para>
    /// One gate per catalog file, beside the content it guards — the reasoning is written up in
    /// <c>TimelineEventAdapterTests</c>'s <c>PressureGatesLiveWithTheirCatalogs</c> and in
    /// <c>docs/plans/0004-wave-4-lanes.md</c>. The union of this, the EU gate and the global gate is
    /// the whole catalog-wide assertion; a fourth <c>timeline_*.json</c> would need its own.
    /// </para>
    /// <para>
    /// <b>Presence, never values.</b> What each event presses is an authoring judgement, and a test
    /// that pinned a number would go red on the next calibration pass for a reason unrelated to what
    /// it guards. What is asserted is that the key is there, that it says something (an empty object
    /// presses nothing and would satisfy a naive presence check), and that every axis it states is a
    /// real axis inside the contract's range.
    /// </para>
    /// </remarks>
    public class TimelineNaPressureTests
    {
        private const string CatalogFile = "timeline_na.json";

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

        private static string CatalogJson() =>
            File.ReadAllText(Path.Combine(RepoRoot(), "data", CatalogFile));

        private static TimelineAdaptationPolicy ShippedPolicy()
        {
            string json = File.ReadAllText(Path.Combine(RepoRoot(), "data", "timeline_adaptation.json"));

            TimelineAdaptationPolicy policy;
            Assert.True(TimelineAdaptationPolicy.TryParse(json, out policy),
                "data/timeline_adaptation.json must parse at schemaVersion " +
                TimelineAdaptationPolicy.SupportedSchemaVersion.ToString(CultureInfo.InvariantCulture));

            return policy;
        }

        /// <summary>
        /// <b>The gate.</b> Every NA event the policy wraps carries an <c>issuePressure</c> with at
        /// least one axis on it.
        /// </summary>
        /// <remarks>
        /// The wrapped set is walked through <see cref="TimelineAdaptationPolicy.KindFor"/> rather than
        /// by re-deriving "not listed as none" here. Re-deriving it would make the test agree with a
        /// misreading of the policy file instead of catching one, and the default rule — unnamed means
        /// <c>generic</c> — is precisely the part a second implementation would get wrong.
        /// </remarks>
        [Fact]
        public void EveryWrappedNaEvent_AuthorsAnIssuePressure()
        {
            TimelineAdaptationPolicy policy = ShippedPolicy();

            int wrapped = 0;
            var inert = new List<string>();

            using JsonDocument doc = JsonDocument.Parse(CatalogJson());
            foreach (JsonElement e in doc.RootElement.GetProperty("events").EnumerateArray())
            {
                string id = e.GetProperty("id").GetString() ?? "";
                if (policy.KindFor(id) != TimelineAdaptationKind.Generic) continue;

                wrapped++;

                JsonElement pressure;
                if (!e.TryGetProperty("issuePressure", out pressure) ||
                    pressure.ValueKind != JsonValueKind.Object)
                {
                    inert.Add(id);
                    continue;
                }

                // An empty object is the same inertness with a key on it, so it fails the same way.
                bool statesSomething = false;
                foreach (JsonProperty axis in pressure.EnumerateObject())
                {
                    statesSomething = true;
                    break;
                }

                if (!statesSomething) inert.Add(id);
            }

            // A misread policy that wrapped nothing would satisfy the loop above vacuously.
            Assert.True(wrapped > 0, CatalogFile + " must contain at least one generically wrapped event");

            Assert.True(inert.Count == 0,
                CatalogFile + ": these wrapped events press no issue and would arrive as story cards " +
                "that ask nothing of the player — " + string.Join(", ", inert));
        }

        /// <summary>
        /// Every axis stated anywhere in the file is one of the six, and sits inside <c>[-1, +1]</c>.
        /// </summary>
        /// <remarks>
        /// The schema suite catches both of these against <c>timeline.schema.json</c>, and
        /// <c>TimelineCatalogLoader</c> catches the range again at load. Repeated here because this is
        /// the file's own gate and a typo'd axis name is silent in exactly the way the whole lane
        /// exists to fix: an unknown key is not read, so the event stays inert while reading as
        /// authored.
        /// </remarks>
        [Fact]
        public void EveryAuthoredAxis_IsARealIssueInRange()
        {
            var known = new List<string>();
            for (int i = 0; i < Issues.All.Count; i++) known.Add(Issues.ToKey(Issues.All[i]));

            using JsonDocument doc = JsonDocument.Parse(CatalogJson());
            foreach (JsonElement e in doc.RootElement.GetProperty("events").EnumerateArray())
            {
                string id = e.GetProperty("id").GetString() ?? "";

                JsonElement pressure;
                if (!e.TryGetProperty("issuePressure", out pressure)) continue;

                Assert.Equal(JsonValueKind.Object, pressure.ValueKind);

                foreach (JsonProperty axis in pressure.EnumerateObject())
                {
                    Assert.True(known.Contains(axis.Name),
                        id + " presses '" + axis.Name + "', which is not one of the six issues");

                    double value = axis.Value.GetDouble();
                    Assert.InRange(value, -1.0, 1.0);
                    Assert.NotEqual(0.0, value);
                }
            }
        }

        /// <summary>
        /// An event the policy drops never becomes a story, so a pressure on it would be authored work
        /// that nothing reads. None of them carries one.
        /// </summary>
        [Fact]
        public void DroppedNaEvents_AuthorNoPressure()
        {
            TimelineAdaptationPolicy policy = ShippedPolicy();

            int dropped = 0;
            var stray = new List<string>();

            using JsonDocument doc = JsonDocument.Parse(CatalogJson());
            foreach (JsonElement e in doc.RootElement.GetProperty("events").EnumerateArray())
            {
                string id = e.GetProperty("id").GetString() ?? "";
                if (policy.KindFor(id) != TimelineAdaptationKind.None) continue;

                dropped++;
                if (e.TryGetProperty("issuePressure", out _)) stray.Add(id);
            }

            Assert.True(dropped > 0, CatalogFile + " must contain at least one dropped event");
            Assert.True(stray.Count == 0,
                CatalogFile + ": these events are never wrapped, so their pressure is read by nothing — " +
                string.Join(", ", stray));
        }
    }
}
