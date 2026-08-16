// Compiled into BOTH Agora.Mod and (by <Compile Link>) tests/Agora.Core.Tests: it must stay free of
// every Game.*, Unity.* and Colossal.* type. #nullable disable keeps it warning-clean in the test
// project, which enables nullable, without annotating a file the mod compiles unannotated.
#nullable disable

using System;
using System.IO;
using System.Text;
using Newtonsoft.Json.Linq;

namespace Agora.Mod.Llm
{
    /// <summary>
    /// Stores the last flavor document that passed validation, so a session that starts with the CLI
    /// missing still has prose to show.
    /// </summary>
    public interface IFlavorCache
    {
        /// <summary>The cached document, or null. Never throws.</summary>
        FlavorDocument Load();

        /// <summary>Persists a validated document. Never throws.</summary>
        void Save(FlavorDocument document, string rawJson);
    }

    /// <summary>Keeps nothing. Used when no sidecar directory is configured, and in tests.</summary>
    public sealed class NullFlavorCache : IFlavorCache
    {
        public static readonly NullFlavorCache Instance = new NullFlavorCache();
        private NullFlavorCache() { }

        public FlavorDocument Load() => null;
        public void Save(FlavorDocument document, string rawJson) { }
    }

    /// <summary>
    /// <c>flavor_cache.json</c> in the save's sidecar directory (§5).
    ///
    /// <para>
    /// <b>Atomic writes</b> (non-negotiable #6): the payload goes to a temp file in the same
    /// directory, is flushed to disk, and only then replaces the live file. A half-written cache is
    /// the exact failure this avoids - the game can be killed mid-write at any moment, and a
    /// truncated <c>flavor_cache.json</c> would mean a save that permanently loads with no prose.
    /// </para>
    ///
    /// <para>
    /// What is stored is the <i>raw validated JSON</i>, not a re-serialisation of the parsed object.
    /// Round-tripping through C# and back would be a second place the wire format is defined, free to
    /// drift from the schema; storing the bytes that passed validation means the load path can simply
    /// re-run the same validator over them and reject the file if the schema has since changed.
    /// </para>
    /// </summary>
    public sealed class FileFlavorCache : IFlavorCache
    {
        public const string FileName = "flavor_cache.json";

        private readonly string _directory;
        private readonly FlavorValidator _validator;
        private readonly FlavorCatalog _catalog;
        private readonly IFlavorLog _log;

        /// <param name="directory">The save's sidecar directory. Created on first write.</param>
        /// <param name="validator">
        /// Re-validates on load. A cache file written by an older build, hand-edited, or corrupted is
        /// exactly as untrusted as a fresh model response, and is put through the same gate.
        /// </param>
        /// <param name="catalog">
        /// IDs legal at load time. Pass the engine's current registry: a cached article about a party
        /// that has since dissolved should not come back.
        /// </param>
        public FileFlavorCache(string directory, FlavorValidator validator, FlavorCatalog catalog, IFlavorLog log)
        {
            _directory = directory;
            _validator = validator;
            _catalog = catalog ?? FlavorCatalog.Empty;
            _log = log ?? NullFlavorLog.Instance;
        }

        public string FilePath => string.IsNullOrEmpty(_directory) ? null : Path.Combine(_directory, FileName);

        public FlavorDocument Load()
        {
            string path = FilePath;
            if (string.IsNullOrEmpty(path) || _validator == null) return null;

            try
            {
                if (!File.Exists(path)) return null;

                string json = File.ReadAllText(path, new UTF8Encoding(false));

                // Before the validator, not after: a schema error is fatal to the whole document
                // there, so an over-long article written by an older build would otherwise take
                // every party name in the file down with it.
                int fromVersion, pruned;
                json = FlavorCacheMigration.UpgradeToCurrent(json, _log, out fromVersion, out pruned);
                if (pruned > 0)
                {
                    _log.Warn("cached flavor upgraded from schemaVersion " + fromVersion +
                              "; dropped " + pruned + " article(s) longer than the new limits");
                }

                var date = FlavorDocument.ParseSimDate(PeekDate(json));
                var result = _validator.Validate(json, _catalog, date ?? default(Agora.Core.Contracts.SimDate));

                if (!result.IsValid)
                {
                    _log.Warn("cached flavor at " + path + " failed validation and was ignored: " +
                              Join(result.Errors));
                    return null;
                }

                // The other face of the emptied round. Every flavor_cache.json written before the
                // refs check existed holds city-branch articles with no refs at all, so the first
                // load after that change filters the lot away - and returning the remains would
                // install an article-less document as the last good one, which then survives every
                // reload. Null is the honest answer: it means "no last good yet", and the canned pool
                // serves until the next wake writes a cache that does carry refs. Party names go with
                // it, which is the cost of the safe direction; a cache is a derived artefact and the
                // next generation rebuilds it.
                if (result.ArticlesAllDiscarded)
                {
                    _log.Warn("cached flavor at " + path + " lost all " + result.ArticlesReceived +
                              " of its articles to the catalog filter and was ignored: " +
                              Join(result.Discarded));
                    return null;
                }

                // Said out loud, at Warn, on the success path. A load that drops cached entries one
                // at a time is the failure this whole class is written around, and until now the
                // only branch that reported a discard was ArticlesAllDiscarded - so every OTHER
                // total loss returned a document, logged "restored N entries" at Debug, and looked
                // exactly like a clean load.
                //
                // The case that made this necessary: story ids are minted per cycle, so a load that
                // rebuilds state without its stories - a lost state_*.json beside an intact
                // flavor_cache.json, or a rewind - hands the filter a catalog that recognises no
                // story at all. Every story and resolution entry goes, the party names survive, and
                // the player simply finds the prose gone. Reporting the count rather than staying
                // silent is the difference between a one-line diagnosis and an unfalsifiable
                // "the mod lost my story text".
                if (result.Discarded != null && result.Discarded.Count > 0)
                {
                    _log.Warn("cached flavor at " + path + " lost " + result.Discarded.Count +
                              " entries to the catalog filter (the rest was kept): " +
                              Join(result.Discarded));
                }

                _log.Debug("restored " + result.Document.EntryCount + " cached flavor entries from " + path);
                return result.Document;
            }
            catch (Exception ex)
            {
                _log.Warn("cached flavor could not be read (" + ex.Message + "); continuing without it");
                return null;
            }
        }

        public void Save(FlavorDocument document, string rawJson)
        {
            string path = FilePath;
            if (string.IsNullOrEmpty(path) || string.IsNullOrEmpty(rawJson)) return;

            string temp = path + ".tmp";
            try
            {
                Directory.CreateDirectory(_directory);

                using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
                using (var writer = new StreamWriter(stream, new UTF8Encoding(false)))
                {
                    writer.Write(rawJson);
                    writer.Flush();
                    // Push it past the OS cache before the rename. Without this the rename can land
                    // while the contents are still buffered, and a power cut leaves a zero-length file
                    // where a valid one used to be.
                    stream.Flush(true);
                }

                if (File.Exists(path))
                {
                    // File.Replace is the atomic swap. It needs the destination to exist, hence the
                    // branch; File.Move onto an existing path throws on .NET Framework.
                    File.Replace(temp, path, null, true);
                }
                else
                {
                    File.Move(temp, path);
                }

                _log.Debug("flavor cache written to " + path);
            }
            catch (Exception ex)
            {
                _log.Warn("flavor cache could not be written (" + ex.Message + "); the in-memory copy still stands");
                try { if (File.Exists(temp)) File.Delete(temp); } catch { }
            }
        }

        /// <summary>
        /// Reads <c>generatedAtSimDate</c> out of the raw text so <see cref="Load"/> can validate
        /// against the document's own date rather than a date it has not got yet.
        /// </summary>
        private static string PeekDate(string json)
        {
            try
            {
                var root = FlavorJsonReader.ParseObject(json);
                if (root == null) return null;
                JToken token = root["generatedAtSimDate"];
                return token != null && token.Type == JTokenType.String
                    ? token.Value<string>()
                    : null;
            }
            catch
            {
                return null;
            }
        }

        private static string Join(System.Collections.Generic.IReadOnlyList<string> items)
        {
            if (items == null || items.Count == 0) return "(no detail)";
            var sb = new StringBuilder();
            for (int i = 0; i < items.Count && i < 5; i++)
            {
                if (i > 0) sb.Append("; ");
                sb.Append(items[i]);
            }
            if (items.Count > 5) sb.Append("; (+").Append(items.Count - 5).Append(" more)");
            return sb.ToString();
        }
    }
}
