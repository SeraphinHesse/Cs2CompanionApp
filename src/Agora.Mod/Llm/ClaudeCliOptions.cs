using System;

namespace Agora.Mod.Llm
{
    /// <summary>
    /// Infrastructure settings for the Claude CLI subprocess.
    ///
    /// <para>
    /// <b>Why these are not in <c>engine_tuning.json</c>.</b> That file holds coefficients the engine
    /// computes with, and every one of them changes political outcomes. Nothing here does: a timeout
    /// or an executable path cannot change a vote share, because the only thing the subprocess is
    /// allowed to return is prose (non-negotiable #1). Putting them in the tuning file would imply
    /// they are part of the deterministic input set, which would be a lie - two saves with different
    /// timeouts must still produce identical politics.
    /// </para>
    ///
    /// <para>
    /// They belong in the per-save sidecar settings (non-negotiable #10) once the persistence packet
    /// lands. Until then the defaults here apply, overridable by environment variable for debugging.
    /// </para>
    /// </summary>
    public sealed class ClaudeCliOptions
    {
        /// <summary>Env var pointing at the CLI, for when it is not on PATH.</summary>
        public const string ExecutableEnvVar = "AGORA_CLAUDE_CLI";

        /// <summary>Env var overriding <see cref="TimeoutSeconds"/>.</summary>
        public const string TimeoutEnvVar = "AGORA_CLAUDE_TIMEOUT_SECONDS";

        /// <summary>Env var pointing at a repo checkout, so a dev build validates against the real schema file.</summary>
        public const string SchemaPathEnvVar = "AGORA_FLAVOR_SCHEMA";

        /// <summary>
        /// Absolute path to the CLI. Null means "resolve it" - see <see cref="ClaudeCliLocator"/>.
        /// </summary>
        public string ExecutablePath { get; set; }

        /// <summary>
        /// Wall-clock budget for one attempt, in seconds.
        /// </summary>
        /// <remarks>
        /// 120s is generous for a single prose generation and deliberately so: the call runs on a
        /// background thread and nothing waits on it, so a slow answer costs nothing but a late
        /// dashboard update. The timeout exists to stop a wedged process living forever, not to
        /// bound latency.
        /// </remarks>
        public int TimeoutSeconds { get; set; } = 120;

        /// <summary>
        /// Extra attempts after the first. §4.2 specifies retry-once, so this is 1.
        /// </summary>
        /// <remarks>
        /// One retry, not a loop. The failures worth retrying are transient (a truncated stream, a
        /// response that opened with an apology instead of a brace); the failures that are not
        /// transient - CLI missing, not authenticated - repeat identically, and hammering them would
        /// spawn a process storm against a machine that is already telling us no.
        /// </remarks>
        public int RetryCount { get; set; } = 1;

        /// <summary>
        /// After a run fails with "the CLI is not installed", wait this many <b>sim</b> months before
        /// looking again.
        /// </summary>
        /// <remarks>
        /// Sim months, not wall-clock minutes, because the wall clock is not a value Agora is allowed
        /// to read (non-negotiable #8) and a paused game should not burn through a cooldown. Without
        /// one, every wake would pay a full PATH scan and a failed <c>CreateProcess</c> to learn what
        /// the last one already knew. A manual wake ignores this entirely, so a player who installs
        /// the CLI mid-session gets an answer by pressing the button rather than by waiting.
        /// </remarks>
        public int UnavailableCooldownMonths { get; set; } = 6;

        /// <summary>
        /// Development-time path to <c>data/schemas/politics_flavor.schema.json</c>. Null in a normal
        /// install, where <see cref="FlavorSchema.EmbeddedJson"/> is used - <c>data/</c> is not deployed.
        /// </summary>
        public string SchemaFilePath { get; set; }

        /// <summary>Working directory for the subprocess. Null means the process's own.</summary>
        public string WorkingDirectory { get; set; }

        /// <summary>
        /// Directory holding <c>flavor_cache.json</c> (§5). Null disables the on-disk last-good cache
        /// and keeps it in memory only.
        /// </summary>
        public string SidecarDirectory { get; set; }

        /// <summary>
        /// When true, <c>TryGetFlavor</c> starts a run by itself if nothing is in flight and no
        /// payload has ever been produced.
        /// </summary>
        /// <remarks>
        /// Off by default. Wake cadence is the engine's job (<c>TickPlanner.Plan</c> returns it as
        /// <c>TickPlan.LlmWake</c>, §3), and a provider that fired on every poll would ignore the
        /// player's cadence setting entirely. The flag exists for a debug panel that wants one
        /// immediate generation.
        /// </remarks>
        public bool AutoRequestOnPoll { get; set; }

        /// <summary>
        /// Reads the environment-variable overrides over the top of the defaults. Never throws.
        /// </summary>
        public static ClaudeCliOptions FromEnvironment(IFlavorLog log)
        {
            log = log ?? NullFlavorLog.Instance;
            var options = new ClaudeCliOptions();

            try
            {
                string executable = Environment.GetEnvironmentVariable(ExecutableEnvVar);
                if (!string.IsNullOrEmpty(executable)) options.ExecutablePath = executable.Trim();

                string schema = Environment.GetEnvironmentVariable(SchemaPathEnvVar);
                if (!string.IsNullOrEmpty(schema)) options.SchemaFilePath = schema.Trim();

                string timeout = Environment.GetEnvironmentVariable(TimeoutEnvVar);
                int parsed;
                if (!string.IsNullOrEmpty(timeout) &&
                    int.TryParse(timeout.Trim(), System.Globalization.NumberStyles.Integer,
                                 System.Globalization.CultureInfo.InvariantCulture, out parsed) &&
                    parsed > 0 && parsed <= 3600)
                {
                    options.TimeoutSeconds = parsed;
                }
            }
            catch (Exception ex)
            {
                // Reading the environment can throw under a restrictive host. Defaults are fine.
                log.Debug("could not read LLM environment overrides: " + ex.Message);
            }

            return options;
        }
    }
}
