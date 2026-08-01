using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace Agora.Mod.Persistence
{
    /// <summary>
    /// Write-a-file-or-leave-the-old-one-alone. Non-negotiable #6: sidecar writes are atomic — temp
    /// file plus rename, never a partial in-place write.
    ///
    /// <para>
    /// The failure this prevents is specific and unrecoverable. Saving is when the process is most
    /// likely to be killed (alt-F4 on a slow autosave, a crash in another mod's save hook), and a
    /// half-written <c>state_1997_04.json</c> is worse than no file at all: the truncation lands
    /// mid-array, the JSON still starts convincingly, and the player's thirty-year political history
    /// is gone. Renaming a fully-flushed temp over the target means the target is always either the
    /// previous good state or the new good state.
    /// </para>
    /// </summary>
    public static class AtomicFile
    {
        public const string TempSuffix = ".tmp";
        public const string CorruptSuffix = ".corrupt";

        /// <summary>How many <c>.corrupt</c>, <c>.corrupt1</c>, … names to try before reusing one.</summary>
        public const int MaxQuarantineSlots = 8;

        private static readonly UTF8Encoding Utf8NoBom = new UTF8Encoding(false);

        /// <summary>
        /// Writes <paramref name="contents"/> to <paramref name="path"/> atomically. Creates the
        /// containing directory. Throws only if the write genuinely could not happen — callers in
        /// this assembly catch and log rather than letting an IO fault reach the game's save path.
        /// </summary>
        public static void WriteAllText(string path, string contents)
        {
            if (string.IsNullOrEmpty(path)) throw new ArgumentException("Path must not be empty.", "path");
            if (contents == null) contents = string.Empty;

            string directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            string temp = path + TempSuffix;

            // FileShare.None: nothing else may observe the temp mid-write.
            using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
            using (var writer = new StreamWriter(stream, Utf8NoBom))
            {
                writer.Write(contents);
                writer.Flush();

                // Flush(true) pushes the OS write cache to the device. Without it the rename can be
                // durable while the bytes it points at are not — the exact shape of a "file exists,
                // file is zeros" report after a power loss.
                stream.Flush(true);
            }

            Promote(temp, path);
        }

        /// <summary>Renames the temp over the target. The only step that must be atomic.</summary>
        private static void Promote(string temp, string path)
        {
            if (!File.Exists(path))
            {
                File.Move(temp, path);
                return;
            }

            try
            {
                // Null backup name: no .bak left behind. ignoreMetadataErrors, because a mismatched
                // ACL or encryption attribute on the temp is not a reason to lose the write.
                File.Replace(temp, path, null, true);
                return;
            }
            catch (PlatformNotSupportedException)
            {
            }
            catch (NotSupportedException)
            {
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }

            // Degraded path, reached only where File.Replace is unavailable (some Mono filesystem
            // backends). Not atomic: there is a window with no target file. Still strictly better
            // than an in-place write, because the temp is complete before the window opens, and
            // net48 has no File.Move(src, dst, overwrite) overload to do better.
            File.Delete(path);
            File.Move(temp, path);
        }

        /// <summary>
        /// Reads a file, reporting failure rather than throwing. A missing or unreadable sidecar file
        /// is a normal, recoverable condition (non-negotiable #6: load must never desync — and it
        /// certainly must never crash).
        /// </summary>
        public static bool TryReadAllText(string path, out string contents, out Exception error)
        {
            contents = null;
            error = null;

            try
            {
                if (!File.Exists(path)) return false;
                contents = File.ReadAllText(path, Utf8NoBom);
                return true;
            }
            catch (Exception ex)
            {
                error = ex;
                return false;
            }
        }

        /// <summary>
        /// Moves a file that would not parse aside instead of deleting it, and returns the new path
        /// (null if it could not be moved).
        /// </summary>
        /// <remarks>
        /// Deleting is never right here. The file is the player's political history; if the parse
        /// failure turns out to be an Agora bug rather than real damage, the bytes are the only copy
        /// and a later version can recover them. Names are chosen by a deterministic counter rather
        /// than a timestamp — <c>DateTime.Now</c> is banned repo-wide (non-negotiable #2).
        /// </remarks>
        public static string Quarantine(string path)
        {
            try
            {
                if (!File.Exists(path)) return null;

                for (int slot = 0; slot < MaxQuarantineSlots; slot++)
                {
                    string candidate = path + CorruptSuffix +
                                       (slot == 0 ? string.Empty : slot.ToString(CultureInfo.InvariantCulture));

                    if (File.Exists(candidate)) continue;

                    File.Move(path, candidate);
                    return candidate;
                }

                // Every slot taken: overwrite the oldest rather than leaving the bad file in place,
                // where the next load would trip over it again.
                string fallback = path + CorruptSuffix;
                File.Delete(fallback);
                File.Move(path, fallback);
                return fallback;
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>
        /// Deletes <c>*.tmp</c> left behind by a process that died mid-write. Returns how many went.
        /// </summary>
        public static int CleanStaleTemps(string directory)
        {
            int removed = 0;

            try
            {
                if (!Directory.Exists(directory)) return 0;

                string[] temps = Directory.GetFiles(directory, "*" + TempSuffix);

                // GetFiles order is filesystem-dependent. Sorting is not needed for correctness here
                // — deletion is order-independent — but it keeps the log line reproducible.
                var ordered = new List<string>(temps);
                ordered.Sort(StringComparer.Ordinal);

                for (int i = 0; i < ordered.Count; i++)
                {
                    try
                    {
                        File.Delete(ordered[i]);
                        removed++;
                    }
                    catch (Exception)
                    {
                        // A temp we cannot delete is harmless: nothing reads *.tmp.
                    }
                }
            }
            catch (Exception)
            {
            }

            return removed;
        }
    }
}
