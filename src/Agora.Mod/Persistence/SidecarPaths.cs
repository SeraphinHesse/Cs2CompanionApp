// Compiled into BOTH Agora.Mod and (by <Compile Link>) tests/Agora.Core.Tests: it must stay free of
// every Game.*, Unity.* and Colossal.* type. #nullable disable keeps it warning-clean in the test
// project, which enables nullable, without annotating a file the mod compiles unannotated.
#nullable disable

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Agora.Core.Contracts;

namespace Agora.Mod.Persistence
{
    /// <summary>
    /// One snapshot file on disk, identified by the (year, month) in its name.
    /// </summary>
    /// <remarks>
    /// The name carries year and month only (<c>politicsmodplan.md</c> §5), which matches the
    /// engine's monthly tick and bounds the directory at twelve files a year. The precise
    /// <see cref="SimDate"/> — including the day — lives inside the document; this type is the
    /// index, not the truth.
    /// </remarks>
    public sealed class StateFileRef : IComparable<StateFileRef>
    {
        public StateFileRef(string path, int year, int month)
        {
            Path = path;
            Year = year;
            Month = month;
        }

        public string Path { get; private set; }
        public int Year { get; private set; }
        public int Month { get; private set; }

        /// <summary>Months since year 0, matching <see cref="SimDate.TotalMonths"/>.</summary>
        public int TotalMonths
        {
            get { return Year * 12 + (Month - 1); }
        }

        /// <summary>The first of the month. A sort and comparison key, not the document's own date.</summary>
        public SimDate MonthStart
        {
            get { return new SimDate(Year, Month, 1); }
        }

        public int CompareTo(StateFileRef other)
        {
            if (other == null) return 1;

            int byMonth = TotalMonths.CompareTo(other.TotalMonths);
            if (byMonth != 0) return byMonth;

            // Two files cannot legitimately share a month, but a manual copy could produce one.
            // Ordinal on the path keeps the ordering total and machine-independent.
            return string.CompareOrdinal(Path ?? string.Empty, other.Path ?? string.Empty);
        }

        public override string ToString()
        {
            return Year.ToString("D4", CultureInfo.InvariantCulture) + "-" +
                   Month.ToString("D2", CultureInfo.InvariantCulture);
        }
    }

    /// <summary>
    /// Where the sidecar lives and what its files are called (<c>politicsmodplan.md</c> §5):
    ///
    /// <code>
    /// &lt;userData&gt;/ModsData/Agora/&lt;saveGuid&gt;/
    ///   state_&lt;year&gt;_&lt;month&gt;.json   full political state at each save point
    ///   timeline_progress.json            fired event ids
    ///   settings.json                     per-save settings (non-negotiable #10)
    ///   flavor_cache.json                 last good Claude prose
    /// </code>
    ///
    /// <para>
    /// Every method here is a pure function of its arguments — the user-data root arrives as a
    /// parameter rather than being read from the game. That is what lets the whole naming and
    /// reconciliation layer be exercised against a temp directory with no game running.
    /// </para>
    /// </summary>
    public static class SidecarPaths
    {
        public const string ModsDataFolderName = "ModsData";
        public const string AgoraFolderName = "Agora";

        public const string StatePrefix = "state_";
        public const string StateExtension = ".json";

        public const string SettingsFileName = "settings.json";
        public const string TimelineProgressFileName = "timeline_progress.json";
        public const string FlavorCacheFileName = "flavor_cache.json";

        /// <summary><c>&lt;userData&gt;/ModsData/Agora</c>.</summary>
        public static string Root(string userDataPath)
        {
            if (string.IsNullOrEmpty(userDataPath))
                throw new ArgumentException("User data path must not be empty.", "userDataPath");

            return Path.Combine(Path.Combine(userDataPath, ModsDataFolderName), AgoraFolderName);
        }

        /// <summary>
        /// The per-save directory. Keyed on Agora's own save guid, never on the save's filename —
        /// §5 is explicit that renaming or copying a save must not orphan its politics.
        /// </summary>
        public static string SaveDirectory(string root, Guid saveGuid)
        {
            if (string.IsNullOrEmpty(root)) throw new ArgumentException("Root must not be empty.", "root");

            return Path.Combine(root, FormatGuid(saveGuid));
        }

        /// <summary>Canonical 8-4-4-4-12, lowercase — the form the schemas' pattern accepts.</summary>
        public static string FormatGuid(Guid saveGuid)
        {
            return saveGuid.ToString("D", CultureInfo.InvariantCulture);
        }

        public static string StateFileName(int year, int month)
        {
            return StatePrefix +
                   year.ToString("D4", CultureInfo.InvariantCulture) + "_" +
                   month.ToString("D2", CultureInfo.InvariantCulture) +
                   StateExtension;
        }

        public static string StateFileName(SimDate date)
        {
            return StateFileName(date.Year, date.Month);
        }

        public static string StatePath(string saveDirectory, SimDate date)
        {
            return Path.Combine(saveDirectory, StateFileName(date));
        }

        public static string SettingsPath(string saveDirectory)
        {
            return Path.Combine(saveDirectory, SettingsFileName);
        }

        public static string TimelineProgressPath(string saveDirectory)
        {
            return Path.Combine(saveDirectory, TimelineProgressFileName);
        }

        /// <summary>
        /// The last-good prose cache. Named here so the layout is complete in one place, but the file
        /// itself belongs to <c>Agora.Mod/Llm/FileFlavorCache</c>, which writes the raw validated
        /// JSON rather than a re-serialisation. Nothing in this packet reads or writes it.
        /// </summary>
        public static string FlavorCachePath(string saveDirectory)
        {
            return Path.Combine(saveDirectory, FlavorCacheFileName);
        }

        /// <summary>
        /// Parses <c>state_1997_04.json</c>. Rejects anything else — including the <c>.tmp</c> and
        /// <c>.corrupt</c> siblings <see cref="AtomicFile"/> leaves around, which must never be
        /// mistaken for a loadable snapshot.
        /// </summary>
        public static bool TryParseStateFileName(string fileName, out int year, out int month)
        {
            year = 0;
            month = 0;

            if (string.IsNullOrEmpty(fileName)) return false;

            // Ordinal comparisons throughout: a Turkish-locale culture-sensitive compare would not
            // match "state_" against "STATE_" the way anyone expects, and file names are data.
            if (!fileName.StartsWith(StatePrefix, StringComparison.Ordinal)) return false;
            if (!fileName.EndsWith(StateExtension, StringComparison.Ordinal)) return false;

            int bodyStart = StatePrefix.Length;
            int bodyLength = fileName.Length - StateExtension.Length - bodyStart;
            if (bodyLength <= 0) return false;

            string body = fileName.Substring(bodyStart, bodyLength);

            int separator = body.IndexOf('_');
            if (separator <= 0 || separator >= body.Length - 1) return false;

            string yearText = body.Substring(0, separator);
            string monthText = body.Substring(separator + 1);

            if (!int.TryParse(yearText, NumberStyles.None, CultureInfo.InvariantCulture, out year)) return false;
            if (!int.TryParse(monthText, NumberStyles.None, CultureInfo.InvariantCulture, out month)) return false;

            if (year < 0 || month < 1 || month > 12)
            {
                year = 0;
                month = 0;
                return false;
            }

            return true;
        }

        /// <summary>
        /// Every snapshot in a save directory, oldest first. Returns an empty list for a missing or
        /// unreadable directory rather than throwing.
        /// </summary>
        /// <remarks>
        /// The explicit sort is load-bearing. <see cref="Directory.GetFiles(string, string)"/> is
        /// documented as returning entries in no particular order, and reconciliation picks "the
        /// nearest earlier snapshot" — reading that off an unordered list is exactly the kind of
        /// quiet order-dependence <c>src/Agora.Core/CLAUDE.md</c> warns about.
        /// </remarks>
        public static List<StateFileRef> EnumerateStateFiles(string saveDirectory)
        {
            var found = new List<StateFileRef>();

            try
            {
                if (string.IsNullOrEmpty(saveDirectory) || !Directory.Exists(saveDirectory)) return found;

                string[] files = Directory.GetFiles(saveDirectory, StatePrefix + "*" + StateExtension);

                for (int i = 0; i < files.Length; i++)
                {
                    string name = Path.GetFileName(files[i]);

                    int year, month;
                    if (!TryParseStateFileName(name, out year, out month)) continue;

                    found.Add(new StateFileRef(files[i], year, month));
                }
            }
            catch (Exception)
            {
                return found;
            }

            found.Sort();
            return found;
        }
    }
}
