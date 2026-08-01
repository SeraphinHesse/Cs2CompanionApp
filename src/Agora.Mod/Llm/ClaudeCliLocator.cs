using System;
using System.Collections.Generic;
using System.IO;

namespace Agora.Mod.Llm
{
    /// <summary>
    /// Finds the Claude CLI on disk.
    ///
    /// <para>
    /// Written against two injected delegates - one that reads an environment variable and one that
    /// tests for a file - rather than against <see cref="Environment"/> and <see cref="File"/>
    /// directly. That is what makes the search order testable without installing or uninstalling
    /// anything on the machine running the tests.
    /// </para>
    ///
    /// <para>Search order, first hit wins:</para>
    /// <list type="number">
    /// <item><description><see cref="ClaudeCliOptions.ExecutablePath"/>, when set.</description></item>
    /// <item><description><c>AGORA_CLAUDE_CLI</c>.</description></item>
    /// <item><description>Each <c>PATH</c> entry, crossed with the known executable names.</description></item>
    /// <item><description>The npm global bin, <c>%APPDATA%\npm</c> - where <c>npm i -g</c> puts <c>claude.cmd</c>.</description></item>
    /// <item><description>The native installer locations under the user profile.</description></item>
    /// </list>
    /// </summary>
    public static class ClaudeCliLocator
    {
        /// <summary>
        /// Candidate file names, most preferred first.
        /// </summary>
        /// <remarks>
        /// <c>.cmd</c> leads because the npm global install is the common case on Windows and is the
        /// shim the CLI's own installer writes. <c>.ps1</c> is deliberately absent:
        /// <see cref="ClaudeCliRunner"/> would have to route it through <c>powershell.exe</c> and
        /// inherit that host's execution policy, which is a support burden for no gain.
        /// </remarks>
        public static readonly string[] ExecutableNames = { "claude.cmd", "claude.exe", "claude.bat", "claude" };

        /// <summary>
        /// Resolves the CLI path, or null when it is not installed.
        /// </summary>
        /// <param name="options">Explicit path, if the caller has one.</param>
        /// <param name="getEnvironmentVariable">Environment reader. Null falls back to the process environment.</param>
        /// <param name="fileExists">File probe. Null falls back to <see cref="File.Exists"/>.</param>
        public static string Resolve(
            ClaudeCliOptions options,
            Func<string, string> getEnvironmentVariable = null,
            Func<string, bool> fileExists = null)
        {
            var env = getEnvironmentVariable ?? SafeGetEnvironmentVariable;
            var exists = fileExists ?? SafeFileExists;

            if (options != null && !string.IsNullOrEmpty(options.ExecutablePath))
            {
                // An explicit path is an instruction, not a hint. If it is wrong the caller should
                // hear about it rather than have a different binary silently substituted.
                return exists(options.ExecutablePath) ? options.ExecutablePath : null;
            }

            string fromEnv = env(ClaudeCliOptions.ExecutableEnvVar);
            if (!string.IsNullOrEmpty(fromEnv))
            {
                fromEnv = fromEnv.Trim().Trim('"');
                if (exists(fromEnv)) return fromEnv;
            }

            foreach (string directory in CandidateDirectories(env))
            {
                foreach (string name in ExecutableNames)
                {
                    string candidate = SafeCombine(directory, name);
                    if (candidate != null && exists(candidate)) return candidate;
                }
            }

            return null;
        }

        /// <summary>Directories to probe, in order. Public so a diagnostic panel can show the search.</summary>
        public static IEnumerable<string> CandidateDirectories(Func<string, string> env)
        {
            string path = env("PATH");
            if (!string.IsNullOrEmpty(path))
            {
                foreach (string entry in path.Split(Path.PathSeparator))
                {
                    string trimmed = entry == null ? null : entry.Trim().Trim('"');
                    if (!string.IsNullOrEmpty(trimmed)) yield return trimmed;
                }
            }

            string appData = env("APPDATA");
            if (!string.IsNullOrEmpty(appData)) yield return SafeCombine(appData, "npm");

            string localAppData = env("LOCALAPPDATA");
            if (!string.IsNullOrEmpty(localAppData))
            {
                yield return SafeCombine(localAppData, Path.Combine("Programs", "claude"));
                yield return SafeCombine(localAppData, Path.Combine("Programs", "claude-code"));
            }

            string userProfile = env("USERPROFILE");
            if (!string.IsNullOrEmpty(userProfile))
            {
                yield return SafeCombine(userProfile, Path.Combine(".local", "bin"));
                yield return SafeCombine(userProfile, Path.Combine(".claude", "local"));
                yield return SafeCombine(userProfile, Path.Combine(".bun", "bin"));
            }

            // Non-Windows hosts (the mod ships for macOS and Linux too, via ModPostProcessor).
            string home = env("HOME");
            if (!string.IsNullOrEmpty(home))
            {
                yield return SafeCombine(home, Path.Combine(".local", "bin"));
                yield return SafeCombine(home, Path.Combine(".claude", "local"));
            }
        }

        /// <summary>
        /// True when the resolved path must be run through <c>cmd.exe</c> rather than started directly.
        /// </summary>
        /// <remarks>
        /// <c>CreateProcess</c> - which is what <c>UseShellExecute = false</c> reaches - cannot
        /// execute a <c>.cmd</c> or <c>.bat</c>; it needs a real PE image. The npm shim for the CLI
        /// is a <c>.cmd</c>, so this is the normal case on Windows, not an edge one.
        /// </remarks>
        public static bool NeedsCommandShell(string executablePath)
        {
            if (string.IsNullOrEmpty(executablePath)) return false;
            string extension;
            try { extension = Path.GetExtension(executablePath); }
            catch { return false; }
            if (string.IsNullOrEmpty(extension)) return false;
            return extension.Equals(".cmd", StringComparison.OrdinalIgnoreCase) ||
                   extension.Equals(".bat", StringComparison.OrdinalIgnoreCase);
        }

        private static string SafeCombine(string directory, string name)
        {
            try { return Path.Combine(directory, name); }
            catch { return null; }
        }

        private static string SafeGetEnvironmentVariable(string name)
        {
            try { return Environment.GetEnvironmentVariable(name); }
            catch { return null; }
        }

        private static bool SafeFileExists(string path)
        {
            if (string.IsNullOrEmpty(path)) return false;
            try { return File.Exists(path); }
            catch { return false; }
        }
    }
}
