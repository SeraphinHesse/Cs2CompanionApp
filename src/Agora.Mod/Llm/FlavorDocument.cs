// Compiled into BOTH Agora.Mod and (by <Compile Link>) tests/Agora.Core.Tests: it must stay free of
// every Game.*, Unity.* and Colossal.* type. #nullable disable keeps it warning-clean in the test
// project, which enables nullable, without annotating a file the mod compiles unannotated.
#nullable disable

using System;
using System.Collections.Generic;
using Agora.Core.Contracts;
using Newtonsoft.Json.Linq;

namespace Agora.Mod.Llm
{
    /// <summary>
    /// The wire shape of <c>politics_flavor.json</c>, as read from a validated token tree.
    ///
    /// <para>
    /// This exists rather than deserialising straight into <see cref="FlavorPayload"/> for two
    /// reasons. First, <see cref="FlavorPayload"/> currently carries only parties and articles, while
    /// the schema also defines <c>factionFlavor</c> and <c>eventProse</c> - dropping those at the
    /// parse boundary would lose prose the schema says is legal. Second, mapping by hand from
    /// <c>JToken</c> means a value only becomes a C# string if it *was* a JSON string:
    /// <c>JsonConvert.DeserializeObject</c> would happily coerce <c>"name": 42</c> into
    /// <c>"42"</c> and launder a number into engine-visible text.
    /// </para>
    ///
    /// <para>Pure: constructed only from an already-validated <c>JObject</c>.</para>
    /// </summary>
    public sealed class FlavorDocument
    {
        public int SchemaVersion { get; private set; }

        /// <summary>Raw <c>YYYY-MM-DD</c> as written by the model, before parsing.</summary>
        public string GeneratedAtSimDateText { get; private set; }

        /// <summary>Parsed form of <see cref="GeneratedAtSimDateText"/>, or null when unparseable.</summary>
        public SimDate? GeneratedAt { get; private set; }

        public List<PartyFlavorEntry> PartyFlavor { get; private set; }
        public List<FactionFlavorEntry> FactionFlavor { get; private set; }
        public List<ArticleEntry> Articles { get; private set; }
        public List<EventProseEntry> EventProse { get; private set; }

        /// <summary>Opening prose for live stories.</summary>
        public List<StoryProseEntry> Stories { get; private set; }

        /// <summary>Closing prose for stories that have resolved.</summary>
        public List<StoryProseEntry> Resolutions { get; private set; }

        /// <summary>
        /// Which writer produced this document. Stamped by the provider that built it, never read
        /// from the JSON — the schema has no such field and the model is not asked to name itself.
        /// </summary>
        /// <remarks>
        /// Defaults to <see cref="ProseSource.Cli"/> because <see cref="FromValidatedObject"/>'s
        /// caller is the validator, and everything that reaches the validator came off the wire. The
        /// canned pool builds its document through the same method and overwrites this immediately
        /// afterwards; that asymmetry is deliberate, because a pool document mislabelled as the
        /// model's would let canned text sit in the slot the model's prose is added to, whereas the
        /// reverse merely loses a label.
        /// </remarks>
        public ProseSource Source { get; set; } = ProseSource.Cli;

        private FlavorDocument()
        {
            GeneratedAtSimDateText = string.Empty;
            PartyFlavor = new List<PartyFlavorEntry>();
            FactionFlavor = new List<FactionFlavorEntry>();
            Articles = new List<ArticleEntry>();
            EventProse = new List<EventProseEntry>();
            Stories = new List<StoryProseEntry>();
            Resolutions = new List<StoryProseEntry>();
        }

        /// <summary>
        /// Maps an already schema-validated object. Assumes nothing: a missing or wrong-typed field
        /// becomes an empty string rather than an exception, so this cannot be the thing that throws.
        /// </summary>
        public static FlavorDocument FromValidatedObject(JObject root)
        {
            var doc = new FlavorDocument();
            if (root == null) return doc;

            doc.SchemaVersion = IntOr(root["schemaVersion"], 0);
            doc.GeneratedAtSimDateText = Str(root["generatedAtSimDate"]);
            doc.GeneratedAt = ParseSimDate(doc.GeneratedAtSimDateText);

            foreach (var item in Items(root["partyFlavor"]))
            {
                doc.PartyFlavor.Add(new PartyFlavorEntry
                {
                    PartyId = Str(item["partyId"]),
                    Name = Str(item["name"]),
                    ShortName = Str(item["shortName"]),
                    Description = Str(item["description"]),
                    Slogan = Str(item["slogan"])
                });
            }

            foreach (var item in Items(root["factionFlavor"]))
            {
                doc.FactionFlavor.Add(new FactionFlavorEntry
                {
                    FactionId = Str(item["factionId"]),
                    PartyId = Str(item["partyId"]),
                    Name = Str(item["name"]),
                    ShortName = Str(item["shortName"]),
                    Description = Str(item["description"]),
                    LeaderName = Str(item["leaderName"])
                });
            }

            foreach (var item in Items(root["articles"]))
            {
                var refs = item["refs"] as JObject;
                doc.Articles.Add(new ArticleEntry
                {
                    Id = Str(item["id"]),
                    Outlet = Str(item["outlet"]),
                    Headline = Str(item["headline"]),
                    Body = Str(item["body"]),
                    Tone = Str(item["tone"]),
                    EventId = refs == null ? string.Empty : Str(refs["eventId"]),
                    DistrictId = refs == null ? string.Empty : Str(refs["districtId"]),
                    PartyId = refs == null ? string.Empty : Str(refs["partyId"])
                });
            }

            foreach (var item in Items(root["eventProse"]))
            {
                doc.EventProse.Add(new EventProseEntry
                {
                    EventId = Str(item["eventId"]),
                    LocalAngle = Str(item["localAngle"])
                });
            }

            foreach (var item in Items(root["stories"]))
            {
                doc.Stories.Add(new StoryProseEntry
                {
                    StoryId = Str(item["storyId"]),
                    Headline = Str(item["headline"]),
                    Article = Str(item["article"])
                });
            }

            foreach (var item in Items(root["resolutions"]))
            {
                doc.Resolutions.Add(new StoryProseEntry
                {
                    StoryId = Str(item["storyId"]),
                    Headline = Str(item["headline"]),
                    Article = Str(item["article"])
                });
            }

            return doc;
        }

        /// <summary>
        /// Projects onto the frozen boundary contract.
        /// </summary>
        /// <remarks>
        /// <c>factionFlavor</c> still has no home on <see cref="FlavorPayload"/>, so it stays on this
        /// type and is reachable through <c>ClaudeCliProvider.LastGoodDocument</c>. Adding
        /// <c>Factions</c> to the contract is a contract change and is reported rather than made
        /// here. <c>eventProse</c> <b>did</b> get that treatment — it was stranded here for three
        /// milestones and reached no surface at all — so it now crosses the boundary with the rest.
        /// </remarks>
        public FlavorPayload ToPayload(SimDate fallbackDate)
        {
            var payload = new FlavorPayload
            {
                SchemaVersion = SchemaVersion == 0 ? FlavorSchema.SupportedSchemaVersion : SchemaVersion,
                GeneratedAt = GeneratedAt.HasValue ? GeneratedAt.Value : fallbackDate
            };

            for (int i = 0; i < PartyFlavor.Count; i++)
            {
                var p = PartyFlavor[i];
                // Fully qualified on purpose: this type also has a PartyFlavor property.
                payload.Parties.Add(new Agora.Core.Contracts.PartyFlavor
                {
                    PartyId = p.PartyId,
                    Name = p.Name,
                    ShortName = p.ShortName,
                    Description = p.Description,
                    Slogan = p.Slogan
                });
            }

            for (int i = 0; i < Articles.Count; i++)
            {
                var a = Articles[i];
                payload.Articles.Add(new Agora.Core.Contracts.Article
                {
                    Id = a.Id,
                    Outlet = a.Outlet,
                    Headline = a.Headline,
                    Body = a.Body,
                    Tone = a.Tone,
                    EventId = a.EventId,
                    DistrictId = a.DistrictId,
                    PartyId = a.PartyId
                });
            }

            for (int i = 0; i < Stories.Count; i++)
            {
                payload.Stories.Add(Stories[i].ToContract(Source));
            }

            for (int i = 0; i < Resolutions.Count; i++)
            {
                payload.Resolutions.Add(Resolutions[i].ToContract(Source));
            }

            for (int i = 0; i < EventProse.Count; i++)
            {
                var e = EventProse[i];
                payload.EventProse.Add(new Agora.Core.Contracts.EventProse
                {
                    EventId = e.EventId,
                    LocalAngle = e.LocalAngle,
                    Source = Source
                });
            }

            return payload;
        }

        /// <summary>Total prose entries, for logging.</summary>
        public int EntryCount => PartyFlavor.Count + FactionFlavor.Count + Articles.Count +
                                 EventProse.Count + Stories.Count + Resolutions.Count;

        private static IEnumerable<JToken> Items(JToken token)
        {
            var array = token as JArray;
            if (array == null) yield break;
            foreach (var item in array)
            {
                if (item is JObject) yield return item;
            }
        }

        private static string Str(JToken token) =>
            token != null && token.Type == JTokenType.String ? (token.Value<string>() ?? string.Empty) : string.Empty;

        private static int IntOr(JToken token, int fallback) =>
            token != null && token.Type == JTokenType.Integer ? token.Value<int>() : fallback;

        /// <summary>
        /// <c>YYYY-MM-DD</c> to <see cref="SimDate"/> without going anywhere near <c>DateTime</c>
        /// (non-negotiable #8). Null when the text is not that exact shape or the parts are
        /// out of range.
        /// </summary>
        public static SimDate? ParseSimDate(string text)
        {
            if (string.IsNullOrEmpty(text) || text.Length != 10) return null;
            if (text[4] != '-' || text[7] != '-') return null;

            int year, month, day;
            if (!TryDigits(text, 0, 4, out year)) return null;
            if (!TryDigits(text, 5, 2, out month)) return null;
            if (!TryDigits(text, 8, 2, out day)) return null;
            if (month < 1 || month > 12 || day < 1 || day > 31) return null;

            try
            {
                return new SimDate(year, month, day);
            }
            catch (ArgumentOutOfRangeException)
            {
                return null;
            }
        }

        private static bool TryDigits(string text, int start, int length, out int value)
        {
            value = 0;
            for (int i = start; i < start + length; i++)
            {
                char c = text[i];
                if (c < '0' || c > '9') return false;
                value = value * 10 + (c - '0');
            }
            return true;
        }
    }

    public sealed class PartyFlavorEntry
    {
        public string PartyId = string.Empty;
        public string Name = string.Empty;
        public string ShortName = string.Empty;
        public string Description = string.Empty;
        public string Slogan = string.Empty;
    }

    public sealed class FactionFlavorEntry
    {
        public string FactionId = string.Empty;
        public string PartyId = string.Empty;
        public string Name = string.Empty;
        public string ShortName = string.Empty;
        public string Description = string.Empty;
        public string LeaderName = string.Empty;
    }

    public sealed class ArticleEntry
    {
        public string Id = string.Empty;
        public string Outlet = string.Empty;
        public string Headline = string.Empty;
        public string Body = string.Empty;
        public string Tone = string.Empty;
        public string EventId = string.Empty;
        public string DistrictId = string.Empty;
        public string PartyId = string.Empty;
    }

    public sealed class EventProseEntry
    {
        public string EventId = string.Empty;
        public string LocalAngle = string.Empty;
    }

    /// <summary>
    /// One story's prose as it came off the wire. Shared by <c>stories</c> and <c>resolutions</c>,
    /// which are the same shape carrying opposite ends of one narrative.
    /// </summary>
    public sealed class StoryProseEntry
    {
        public string StoryId = string.Empty;
        public string Headline = string.Empty;
        public string Article = string.Empty;

        /// <summary>Projects onto the boundary contract, stamped with the document's source.</summary>
        public StoryProse ToContract(ProseSource source) => new StoryProse
        {
            StoryId = StoryId,
            Headline = Headline,
            Article = Article,
            Source = source
        };
    }
}
