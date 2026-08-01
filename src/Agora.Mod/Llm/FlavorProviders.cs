using System;
using Agora.Core.Contracts;

namespace Agora.Mod.Llm
{
    /// <summary>
    /// Wires the two providers into the arrangement the mod actually wants: canned prose now, model
    /// prose when it arrives.
    ///
    /// <para>
    /// A brand-new save needs party names on the very first frame it shows a dashboard, and the CLI
    /// takes tens of seconds. So <see cref="StaticPoolProvider"/> answers the first poll immediately
    /// and <see cref="ClaudeCliProvider"/> overwrites it later from its background thread. If the CLI
    /// is missing, times out, or returns something that fails validation, the canned prose simply
    /// stays - which is precisely what non-negotiable #7 asks for, with no separate failure path to
    /// get wrong.
    /// </para>
    ///
    /// <para>
    /// Both halves are optional. Pass a null CLI provider for a pure-offline build; pass a null pool
    /// provider to have the dashboard show nothing until the model answers.
    /// </para>
    /// </summary>
    public sealed class LayeredFlavorProvider : IFlavorProvider, IDisposable
    {
        private readonly StaticPoolProvider _pool;
        private readonly ClaudeCliProvider _cli;

        public LayeredFlavorProvider(StaticPoolProvider pool, ClaudeCliProvider cli)
        {
            _pool = pool;
            _cli = cli;
        }

        public StaticPoolProvider Pool => _pool;
        public ClaudeCliProvider Cli => _cli;

        /// <summary>
        /// Model prose wins when it is ready; canned prose fills every other poll. Null means the
        /// caller keeps what it already has.
        /// </summary>
        public FlavorPayload TryGetFlavor(CitySnapshot snapshot, SimDate date)
        {
            if (_cli != null)
            {
                FlavorPayload fresh = _cli.TryGetFlavor(snapshot, date);
                if (fresh != null) return fresh;
            }

            return _pool != null ? _pool.TryGetFlavor(snapshot, date) : null;
        }

        /// <summary>Starts a model generation. False when there is no CLI provider or one is in flight.</summary>
        public bool RequestFlavor(FlavorRequest request)
        {
            if (_pool != null) _pool.Roster = request;
            return _cli != null && _cli.RequestFlavor(request);
        }

        /// <summary>For the dashboard's status line.</summary>
        public FlavorProviderState State => _cli != null ? _cli.State : FlavorProviderState.Unavailable;

        public void Dispose()
        {
            if (_cli != null) _cli.Dispose();
        }
    }

    /// <summary>Standard construction, in one call.</summary>
    public static class FlavorProviders
    {
        /// <summary>
        /// Builds the production arrangement: canned pool plus CLI, sharing one validator and one
        /// sidecar cache.
        /// </summary>
        /// <param name="saveGuid">
        /// Agora's own save identity (§5). Seeds every canned-prose draw, so it must be the same
        /// GUID the engine uses - a fresh one would make the fallback names change on every load.
        /// </param>
        /// <param name="theme">EU or NA. Chooses the naming vocabulary.</param>
        /// <param name="sidecarDirectory">
        /// <c>ModsData/Agora/&lt;saveGuid&gt;/</c>. Null keeps the last-good cache in memory only.
        /// </param>
        /// <param name="catalog">
        /// IDs the engine currently recognises, used when re-validating the cache on load. Pass the
        /// live registry; <see cref="FlavorCatalog.Empty"/> would discard every cached entry.
        /// </param>
        public static LayeredFlavorProvider Create(
            Guid saveGuid,
            RegionTheme theme,
            string sidecarDirectory,
            FlavorCatalog catalog,
            IFlavorLog log = null)
        {
            log = log ?? ColossalFlavorLog.Instance;

            ClaudeCliOptions options = ClaudeCliOptions.FromEnvironment(log);
            options.SidecarDirectory = sidecarDirectory;

            var validator = FlavorValidator.Create(options.SchemaFilePath, log);

            IFlavorCache cache = string.IsNullOrEmpty(sidecarDirectory)
                ? (IFlavorCache)NullFlavorCache.Instance
                : new FileFlavorCache(sidecarDirectory, validator, catalog ?? FlavorCatalog.Empty, log);

            var cli = new ClaudeCliProvider(options, validator, cache, log);
            var pool = new StaticPoolProvider(saveGuid, theme, validator, log);

            return new LayeredFlavorProvider(pool, cli);
        }

        /// <summary>
        /// Offline arrangement: canned prose only, no subprocess. For tests, for CI, and for the
        /// post-v3 pregenerated-pool future §3 describes.
        /// </summary>
        public static LayeredFlavorProvider CreateOffline(Guid saveGuid, RegionTheme theme, IFlavorLog log = null)
        {
            log = log ?? NullFlavorLog.Instance;
            var validator = FlavorValidator.Create(null, log);
            return new LayeredFlavorProvider(new StaticPoolProvider(saveGuid, theme, validator, log), null);
        }
    }
}
