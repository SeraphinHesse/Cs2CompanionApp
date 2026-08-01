using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using Agora.Core.Contracts;

namespace Agora.Mod.Llm
{
    /// <summary>What the provider is doing right now. For the dashboard's "thinking..." state (§13.6).</summary>
    public enum FlavorProviderState
    {
        Idle = 0,
        Running = 1,

        /// <summary>Last run produced a valid document.</summary>
        Succeeded = 2,

        /// <summary>Last run failed. The last good document, if any, still stands.</summary>
        Failed = 3,

        /// <summary>No CLI on this machine. Nothing will be attempted until the cooldown expires.</summary>
        Unavailable = 4
    }

    /// <summary>
    /// <see cref="IFlavorProvider"/> backed by the headless Claude CLI (§3:
    /// <c>claude -p --output-format json</c>).
    ///
    /// <para>
    /// <b>The whole design is one sentence: the simulation never waits, and never dies, for this.</b>
    /// Non-negotiable #7 and risk §13.6 together mean the sim thread may not block on a subprocess,
    /// so generation runs on a background thread and <see cref="TryGetFlavor"/> is a non-blocking
    /// hand-off - it takes a completed result if one is sitting there and returns null otherwise.
    /// Null is the contract's way of saying "keep what you have", so a missing CLI, a timeout, a
    /// hallucinated party or a truncated JSON stream all converge on the same harmless outcome: the
    /// caller keeps the last good prose and the political engine, which does not depend on any of
    /// this, carries on untouched.
    /// </para>
    ///
    /// <para>
    /// <b>No number from this class can reach engine state</b> (non-negotiable #1). It returns a
    /// <see cref="FlavorPayload"/>, every field of which is a string or an ID, and the response is
    /// put through <see cref="FlavorValidator"/> - schema, numeric sweep, ID catalog - before it is
    /// allowed to become one.
    /// </para>
    ///
    /// <para>
    /// <b>Threading.</b> <see cref="TryGetFlavor"/>, <see cref="RequestFlavor"/>, <see cref="State"/>
    /// and <see cref="LastGoodPayload"/> are safe to call from the sim thread at any time. One
    /// generation runs at a time; a request made while one is in flight is dropped, not queued,
    /// because a queued wake would be answering a question about a city that has since moved on.
    /// </para>
    /// </summary>
    public sealed class ClaudeCliProvider : IFlavorProvider, IDisposable
    {
        private readonly ClaudeCliOptions _options;
        private readonly ClaudeCliRunner _runner;
        private readonly FlavorValidator _validator;
        private readonly IFlavorCache _cache;
        private readonly IFlavorLog _log;

        private readonly object _gate = new object();

        // Guarded by _gate.
        private FlavorDocument _lastGoodDocument;
        private FlavorPayload _lastGoodPayload;
        private FlavorPayload _pending;
        private FlavorProviderState _state = FlavorProviderState.Idle;
        private string _lastError = string.Empty;
        private bool _running;
        private bool _disposed;
        private SimDate? _unavailableSince;
        private SimDate _lastPolledDate;
        private bool _everRequested;

        private Thread _worker;

        public ClaudeCliProvider(
            ClaudeCliOptions options,
            FlavorValidator validator,
            IFlavorCache cache,
            IFlavorLog log,
            ClaudeCliRunner runner = null)
        {
            _options = options ?? new ClaudeCliOptions();
            _log = log ?? NullFlavorLog.Instance;
            _validator = validator ?? FlavorValidator.Create(_options.SchemaFilePath, _log);
            _cache = cache ?? NullFlavorCache.Instance;
            _runner = runner ?? new ClaudeCliRunner(_log);

            RestoreFromCache();
        }

        /// <summary>Convenience wiring: environment options, embedded schema, on-disk cache.</summary>
        public static ClaudeCliProvider Create(string sidecarDirectory, FlavorCatalog catalog, IFlavorLog log)
        {
            log = log ?? ColossalFlavorLog.Instance;

            var options = ClaudeCliOptions.FromEnvironment(log);
            options.SidecarDirectory = sidecarDirectory;

            var validator = FlavorValidator.Create(options.SchemaFilePath, log);
            IFlavorCache cache = string.IsNullOrEmpty(sidecarDirectory)
                ? (IFlavorCache)NullFlavorCache.Instance
                : new FileFlavorCache(sidecarDirectory, validator, catalog ?? FlavorCatalog.Empty, log);

            return new ClaudeCliProvider(options, validator, cache, log);
        }

        // ---- observable state ------------------------------------------------------------------

        public FlavorProviderState State
        {
            get { lock (_gate) { return _state; } }
        }

        /// <summary>Why the last attempt failed. Empty when it did not.</summary>
        public string LastError
        {
            get { lock (_gate) { return _lastError; } }
        }

        /// <summary>The last payload that passed validation, or null. Survives failed runs.</summary>
        public FlavorPayload LastGoodPayload
        {
            get { lock (_gate) { return _lastGoodPayload; } }
        }

        /// <summary>
        /// The last validated document, including the <c>factionFlavor</c> and <c>eventProse</c> that
        /// <see cref="FlavorPayload"/> has no fields for. See the report: those want contract fields.
        /// </summary>
        public FlavorDocument LastGoodDocument
        {
            get { lock (_gate) { return _lastGoodDocument; } }
        }

        public bool IsRunning
        {
            get { lock (_gate) { return _running; } }
        }

        // ---- IFlavorProvider -------------------------------------------------------------------

        /// <summary>
        /// Non-blocking hand-off. Returns a freshly validated payload exactly once, then null until
        /// the next successful run.
        /// </summary>
        /// <remarks>
        /// This deliberately does not decide when to wake the model. Cadence is per-save policy (§3)
        /// and lives in the engine, which returns it as <c>TickPlan.LlmWake</c>; a provider that
        /// generated on every poll would ignore the player's setting and spawn a subprocess every
        /// sim day.
        /// </remarks>
        public FlavorPayload TryGetFlavor(CitySnapshot snapshot, SimDate date)
        {
            bool autoRequest;

            lock (_gate)
            {
                _lastPolledDate = date;

                if (_pending != null)
                {
                    FlavorPayload fresh = _pending;
                    _pending = null;
                    return fresh;
                }

                autoRequest = _options.AutoRequestOnPoll && !_everRequested && !_running && !_disposed;
            }

            if (autoRequest)
            {
                RequestFlavor(new FlavorRequest { Date = date, Snapshot = snapshot, Reason = FlavorWakeReason.Manual });
            }

            return null;
        }

        /// <summary>The date of the most recent <see cref="TryGetFlavor"/> call. Diagnostics only.</summary>
        public SimDate LastPolledDate
        {
            get { lock (_gate) { return _lastPolledDate; } }
        }

        // ---- generation ------------------------------------------------------------------------

        /// <summary>
        /// Starts a generation on a background thread. Returns false when one is already running, the
        /// provider is disposed, or the CLI is known-missing and still inside its cooldown.
        /// </summary>
        public bool RequestFlavor(FlavorRequest request)
        {
            if (request == null) return false;

            lock (_gate)
            {
                if (_disposed || _running) return false;

                // A manual wake ignores the cooldown on purpose: the player pressing the button is
                // usually the player who just installed the CLI, and telling them to wait six sim
                // months would look exactly like the mod being broken.
                bool honourCooldown = request.Reason != FlavorWakeReason.Manual;
                if (honourCooldown && _unavailableSince.HasValue && WithinCooldown(_unavailableSince.Value, request.Date))
                {
                    _log.Debug("skipping the wake: no Claude CLI, still inside the cooldown");
                    return false;
                }

                _running = true;
                _state = FlavorProviderState.Running;
                _everRequested = true;
            }

            // A dedicated background thread rather than the thread pool. The pool is shared with the
            // game's own work, and a job that spends two minutes blocked in WaitForExit is exactly
            // the kind of thing that should not be occupying one of its threads.
            var worker = new Thread(() => RunGeneration(request))
            {
                IsBackground = true,
                Name = "Agora.Flavor"
            };
            lock (_gate) { _worker = worker; }

            try
            {
                worker.Start();
                return true;
            }
            catch (Exception ex)
            {
                lock (_gate)
                {
                    _running = false;
                    _state = FlavorProviderState.Failed;
                    _lastError = "could not start the flavor thread: " + ex.Message;
                }
                _log.Error("could not start the flavor worker thread", ex);
                return false;
            }
        }

        private void RunGeneration(FlavorRequest request)
        {
            try
            {
                GenerateWithRetry(request);
            }
            catch (Exception ex)
            {
                // The outermost net. An escaped exception on a background thread terminates the
                // process on .NET, which would turn a bad LLM answer into a crash to desktop.
                _log.Error("flavor generation failed unexpectedly", ex);
                lock (_gate)
                {
                    _state = FlavorProviderState.Failed;
                    _lastError = ex.Message;
                }
            }
            finally
            {
                lock (_gate) { _running = false; }
            }
        }

        private void GenerateWithRetry(FlavorRequest request)
        {
            string prompt;
            try
            {
                prompt = FlavorPromptBuilder.Build(request);
            }
            catch (Exception ex)
            {
                Fail("the prompt could not be assembled: " + ex.Message);
                return;
            }

            FlavorCatalog catalog = request.EffectiveCatalog();
            int attempts = 1 + (_options.RetryCount < 0 ? 0 : _options.RetryCount);
            var failures = new List<string>();

            for (int attempt = 1; attempt <= attempts; attempt++)
            {
                if (IsDisposed()) return;

                string executable = ClaudeCliLocator.Resolve(_options);
                if (string.IsNullOrEmpty(executable))
                {
                    MarkUnavailable(request.Date,
                        "no Claude CLI found; set " + ClaudeCliOptions.ExecutableEnvVar + " to point at one");
                    return;
                }

                CliResult cli = _runner.Run(executable, prompt, _options);

                if (cli.Outcome == CliOutcome.NotInstalled || cli.Outcome == CliOutcome.LaunchFailed)
                {
                    MarkUnavailable(request.Date, cli.Detail);
                    return;
                }

                if (!cli.IsSuccess)
                {
                    failures.Add("attempt " + attempt + ": " + cli.Detail);
                    _log.Warn("Claude CLI attempt " + attempt + " of " + attempts + " failed: " + cli.Detail);
                    if (!cli.IsRetryable) break;
                    continue;
                }

                string extractError;
                string json = ClaudeResponseReader.ExtractFlavorJson(cli.StandardOutput, out extractError);
                if (json == null)
                {
                    failures.Add("attempt " + attempt + ": " + extractError);
                    _log.Warn("Claude CLI attempt " + attempt + " returned nothing usable: " + extractError);
                    continue;
                }

                FlavorValidationResult validation = _validator.Validate(json, catalog, request.Date);
                if (!validation.IsValid)
                {
                    failures.Add("attempt " + attempt + ": " + Summarise(validation.Errors));
                    _log.Warn("Claude CLI attempt " + attempt + " failed validation: " + Summarise(validation.Errors));
                    continue;
                }

                if (validation.Discarded.Count > 0)
                {
                    _log.Info("dropped " + validation.Discarded.Count +
                              " flavor entries with unknown or duplicate ids: " + Summarise(validation.Discarded));
                }

                Succeed(validation.Document, json, request.Date);
                return;
            }

            Fail(failures.Count == 0 ? "no usable response" : Summarise(failures));
        }

        // ---- state transitions -----------------------------------------------------------------

        private void Succeed(FlavorDocument document, string rawJson, SimDate date)
        {
            FlavorPayload payload = document.ToPayload(date);

            lock (_gate)
            {
                _lastGoodDocument = document;
                _lastGoodPayload = payload;
                _pending = payload;
                _state = FlavorProviderState.Succeeded;
                _lastError = string.Empty;
                _unavailableSince = null;
            }

            // Outside the lock: a disk write must not hold up a sim-thread poll.
            _cache.Save(document, rawJson);

            _log.Info("fresh flavor for " + date + ": " + document.PartyFlavor.Count + " parties, " +
                      document.FactionFlavor.Count + " factions, " + document.Articles.Count + " articles, " +
                      document.EventProse.Count + " event notes");
        }

        private void Fail(string detail)
        {
            lock (_gate)
            {
                _state = FlavorProviderState.Failed;
                _lastError = detail ?? string.Empty;
            }

            // Info, not Error: this is an expected, designed-for outcome (#7), and logging it at
            // error level would train players to ignore genuine errors from the mod.
            _log.Info("no fresh flavor this cycle (" + detail + "); keeping the last good prose");
        }

        private void MarkUnavailable(SimDate date, string detail)
        {
            lock (_gate)
            {
                _state = FlavorProviderState.Unavailable;
                _lastError = detail ?? string.Empty;
                _unavailableSince = date;
            }

            _log.Info("Claude CLI unavailable (" + detail + "); flavor falls back to the last good prose");
        }

        private bool WithinCooldown(SimDate since, SimDate now)
        {
            // Measured on the SIM clock, not the wall clock (#8) - the wall clock is not an input the
            // engine is allowed to read, and a paused game should not burn through the cooldown.
            int cooldownMonths = _options.UnavailableCooldownMonths;
            if (cooldownMonths <= 0) return false;
            return since.MonthsUntil(now) < cooldownMonths;
        }

        private void RestoreFromCache()
        {
            try
            {
                FlavorDocument cached = _cache.Load();
                if (cached == null) return;

                lock (_gate)
                {
                    _lastGoodDocument = cached;
                    _lastGoodPayload = cached.ToPayload(cached.GeneratedAt ?? default(SimDate));
                }

                _log.Info("restored cached flavor from " +
                          (string.IsNullOrEmpty(cached.GeneratedAtSimDateText) ? "an unknown date" : cached.GeneratedAtSimDateText));
            }
            catch (Exception ex)
            {
                _log.Warn("cached flavor could not be restored: " + ex.Message);
            }
        }

        private bool IsDisposed()
        {
            lock (_gate) { return _disposed; }
        }

        private static string Summarise(IReadOnlyList<string> items)
        {
            if (items == null || items.Count == 0) return "(no detail)";
            var sb = new StringBuilder();
            for (int i = 0; i < items.Count && i < 4; i++)
            {
                if (i > 0) sb.Append("; ");
                sb.Append(items[i]);
            }
            if (items.Count > 4) sb.Append("; (+").Append(items.Count - 4).Append(" more)");
            return sb.ToString();
        }

        /// <summary>
        /// Marks the provider disposed and waits briefly for the worker.
        /// </summary>
        /// <remarks>
        /// Bounded wait, and no <c>Thread.Abort</c>. The worker is a background thread, so leaving it
        /// running cannot keep the process alive; blocking the game's unload path on a subprocess that
        /// is mid-generation would be a far worse outcome than a thread that finishes into a disposed
        /// object and drops the result on the floor.
        /// </remarks>
        public void Dispose()
        {
            Thread worker;
            lock (_gate)
            {
                if (_disposed) return;
                _disposed = true;
                worker = _worker;
            }

            if (worker != null && worker.IsAlive)
            {
                try { worker.Join(TimeSpan.FromSeconds(2)); } catch { }
            }
        }
    }
}
