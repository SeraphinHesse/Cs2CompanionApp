// Compiled into BOTH Agora.Mod and (by <Compile Link>) tests/Agora.Core.Tests: it must stay free of
// every Game.*, Unity.* and Colossal.* type. #nullable disable keeps it warning-clean in the test
// project, which enables nullable, without annotating a file the mod compiles unannotated.
#nullable disable

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

        /// <summary>Env var overriding <see cref="Model"/>.</summary>
        public const string ModelEnvVar = "AGORA_CLAUDE_MODEL";

        /// <summary>
        /// The model the CLI is asked for, as an <b>alias</b> rather than a dated snapshot.
        /// </summary>
        /// <remarks>
        /// Haiku because the flavor path is the only thing spending tokens and nothing it returns can
        /// move a number (non-negotiable #1), so the cheapest capable model is the right one.
        /// <para>
        /// The alias, not <c>claude-haiku-4-5-20251001</c>, is the load-bearing half of that choice.
        /// An alias cannot 404: it follows the snapshot the vendor currently serves. A pin retires,
        /// and the day it does every save on every machine starts failing the call and falling back
        /// to canned prose - silently, because non-negotiable #7 says a failed call must keep the last
        /// good flavor and continue rather than surface an error. A pin therefore buys reproducibility
        /// we do not need (prose is not deterministic input) at the price of a failure mode nobody
        /// would notice for months.
        /// </para>
        /// </remarks>
        public const string DefaultModel = "claude-haiku-4-5";

        /// <summary>
        /// Longest model id accepted. Aliases and dated snapshots are well under this; the bound
        /// exists so a junk environment variable cannot push a megabyte onto the command line.
        /// </summary>
        public const int MaxModelIdLength = 64;

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

        /// <summary>
        /// Model id passed to the CLI as <c>--model</c>. Defaults to <see cref="DefaultModel"/>.
        /// </summary>
        /// <remarks>
        /// The setter <b>ignores</b> anything <see cref="IsValidModelId"/> rejects and leaves the
        /// previous value standing. That is deliberate rather than defensive habit: the value is
        /// concatenated bare into a command line that, for a <c>.cmd</c> shim, is re-parsed by
        /// <c>cmd.exe</c>, where a quote, an <c>&amp;</c> or a <c>%FOO%</c> would change what runs.
        /// Filtering here - the one place a model id can enter - is what lets
        /// <see cref="ClaudeCliRunner"/> concatenate without quoting and be right. Throwing instead
        /// would violate non-negotiable #7 for a setting that has a perfectly good default.
        /// </remarks>
        public string Model
        {
            get { return _model; }
            set { if (IsValidModelId(value)) _model = value.Trim(); }
        }

        private string _model = DefaultModel;

        /// <summary>
        /// True when <paramref name="model"/> is safe to place bare on a command line: a non-empty
        /// run of <c>[A-Za-z0-9._-]</c> no longer than <see cref="MaxModelIdLength"/>, and not
        /// starting with <c>-</c>.
        /// </summary>
        /// <remarks>
        /// Written as an explicit character sweep rather than a regex so that the accepted set is
        /// visible at the point of decision. Every shell metacharacter, every quote and the space
        /// that would split one argument into two are all outside it, which is the whole point.
        /// <para>
        /// The leading dash is excluded separately, because the sweep alone would accept one: a dash
        /// is a legal character <i>inside</i> a model id (<c>claude-haiku-4-5</c>) but a value that
        /// opens with one is no longer a value. <c>--model --dangerously-skip-permissions</c> is a
        /// command line an argument parser may read as two flags rather than a flag and its value,
        /// so an environment variable meant to carry a value must not be able to pose as a flag.
        /// </para>
        /// </remarks>
        public static bool IsValidModelId(string model)
        {
            if (string.IsNullOrEmpty(model)) return false;
            string trimmed = model.Trim();
            if (trimmed.Length == 0 || trimmed.Length > MaxModelIdLength) return false;
            if (trimmed[0] == '-') return false;

            for (int i = 0; i < trimmed.Length; i++)
            {
                char c = trimmed[i];
                bool ok = (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') ||
                          c == '.' || c == '_' || c == '-';
                if (!ok) return false;
            }

            return true;
        }

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
            return FromEnvironment(log, null);
        }

        /// <summary>
        /// As <see cref="FromEnvironment(IFlavorLog)"/>, but reading through an injected delegate.
        /// </summary>
        /// <param name="log">Debug sink. Null is fine.</param>
        /// <param name="getEnvironmentVariable">
        /// Environment reader, the same seam <see cref="ClaudeCliLocator.Resolve"/> uses. Null falls
        /// back to the process environment; a test passes a lookup instead of mutating the machine.
        /// </param>
        public static ClaudeCliOptions FromEnvironment(IFlavorLog log, Func<string, string> getEnvironmentVariable)
        {
            log = log ?? NullFlavorLog.Instance;
            var env = getEnvironmentVariable ?? Environment.GetEnvironmentVariable;
            var options = new ClaudeCliOptions();

            try
            {
                string executable = env(ExecutableEnvVar);
                if (!string.IsNullOrEmpty(executable)) options.ExecutablePath = executable.Trim();

                string schema = env(SchemaPathEnvVar);
                if (!string.IsNullOrEmpty(schema)) options.SchemaFilePath = schema.Trim();

                string model = env(ModelEnvVar);
                if (!string.IsNullOrEmpty(model))
                {
                    if (IsValidModelId(model))
                    {
                        options.Model = model;
                    }
                    else
                    {
                        // Log and carry on with the default. A typo'd model id is a bad session, not
                        // a broken one (non-negotiable #7), and letting it through would put an
                        // unquoted stranger on a command line cmd.exe re-parses.
                        log.Debug("ignoring " + ModelEnvVar + ": not a valid model id; using " + DefaultModel);
                    }
                }

                string timeout = env(TimeoutEnvVar);
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
