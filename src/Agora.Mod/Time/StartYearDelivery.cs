namespace Agora.Mod.Time
{
    /// <summary>
    /// How AGORA's start year reaches the player.
    /// </summary>
    /// <remarks>
    /// This enum <b>is</b> the clock kill-switch that <c>politicsmodplan.md</c> §11 M1 asks for. It
    /// gates a single ECS component write rather than a Harmony patch, so "off" is a plain restore of
    /// a recorded integer with no assembly state to unwind.
    /// </remarks>
    public enum StartYearDeliveryMode
    {
        /// <summary>
        /// Default. The game's own epoch year becomes AGORA's start year, so every stock date
        /// surface — HUD, save metadata, load panel, celestial model — reads 1990 onward with no
        /// patching. See <see cref="StartYearPlanner"/> for why one write covers all of them.
        /// </summary>
        RewriteGameEpoch = 0,

        /// <summary>
        /// Leave the player's clock alone; shift dates only inside AGORA. The dashboard shows 1990+
        /// while the HUD keeps its stock year. Degraded but honest, and it never writes to a
        /// component the base game serializes.
        /// </summary>
        OffsetOnly = 1,

        /// <summary>
        /// Kill-switch. Restore the recorded stock epoch and stop shifting dates. AGORA's political
        /// calendar collapses onto the game's.
        /// </summary>
        Off = 2
    }

    /// <summary>What the delivery layer must do to the game's epoch year this load.</summary>
    public enum StartYearAction
    {
        /// <summary>The epoch is already what it should be. Touch nothing.</summary>
        None = 0,

        /// <summary>Write AGORA's start year into the epoch.</summary>
        WriteEpoch = 1,

        /// <summary>Put the player's original epoch year back.</summary>
        RestoreEpoch = 2
    }

    /// <summary>
    /// A decision about the game clock, computed with no game types in reach so it can be tested.
    /// </summary>
    public readonly struct StartYearPlan
    {
        public StartYearAction Action { get; }

        /// <summary>The value to write when <see cref="Action"/> is not <see cref="StartYearAction.None"/>.</summary>
        public int EpochYearToWrite { get; }

        /// <summary>
        /// Added to the game's year to get the political year, <i>assuming the action succeeds</i>.
        /// The live system re-derives this from the epoch it actually observes afterwards
        /// (<see cref="StartYearPlanner.PoliticalYearOffset"/>), so a failed write degrades to
        /// offset-only rather than silently rewriting history.
        /// </summary>
        public int PoliticalYearOffset { get; }

        /// <summary>
        /// The player's own epoch year — what <see cref="StartYearAction.RestoreEpoch"/> puts back.
        /// Must be persisted per save; it is unrecoverable once the epoch has been overwritten.
        /// </summary>
        public int StockEpochYear { get; }

        /// <summary>Log line. Never parsed.</summary>
        public string Reason { get; }

        public StartYearPlan(StartYearAction action, int epochYearToWrite, int politicalYearOffset,
                             int stockEpochYear, string reason)
        {
            Action = action;
            EpochYearToWrite = epochYearToWrite;
            PoliticalYearOffset = politicalYearOffset;
            StockEpochYear = stockEpochYear;
            Reason = reason ?? string.Empty;
        }
    }

    /// <summary>
    /// Decides how to deliver AGORA's start year. Pure — no <c>Game.*</c>, <c>Colossal.*</c> or
    /// <c>Unity.*</c> in sight, so every branch is unit-testable without the game installed.
    ///
    /// <para>
    /// <b>Scout finding that shapes all of this (verified 2026-07-31 against the shipped
    /// <c>Game.dll</c> and its decompilation):</b> the game has exactly one root for absolute years,
    /// <c>Game.Common.TimeData.m_StartingYear</c>. <c>TimeSystem.GetYear(...)</c> — the only two year
    /// computations in the assembly — is <c>data.m_StartingYear + floor(ticks / ticksPerYear)</c>.
    /// <c>TimeSystem.year</c>, <c>GetCurrentDateTime()</c>, <c>GetDateTime(frame)</c>, the HUD
    /// (<c>TimeUISystem.epochYear</c>), the save-metadata date shown in the load panel
    /// (<c>MenuUISystem.simulationDate</c>) and <c>PlanetarySystem</c>'s celestial year all descend
    /// from it. <c>TimeSystem.startingYear</c> — the public setter — is read in exactly one place:
    /// <c>TimeSystem.PostDeserialize</c>, and only when <c>context.purpose == Purpose.NewGame</c>,
    /// where it is copied into <c>m_StartingYear</c>.
    /// </para>
    ///
    /// <para>
    /// So: a <b>new game</b> needs nothing but the public setter, applied before deserialization. A
    /// <b>save that already started at a stock year</b> needs one write to a public <c>int</c> field
    /// on a public <c>IComponentData</c>. Neither is a Harmony patch. §13.2 does not apply.
    /// </para>
    /// </summary>
    public static class StartYearPlanner
    {
        /// <summary>
        /// Chooses the epoch action for this load.
        /// </summary>
        /// <param name="mode">Delivery mode, i.e. the kill-switch position.</param>
        /// <param name="politicalStartYear">AGORA's start year for this save (<c>AgoraSettings.StartYear</c>).</param>
        /// <param name="currentEpochYear">The epoch year read out of the live <c>TimeData</c> singleton.</param>
        /// <param name="recordedStockEpochYear">
        /// The player's original epoch year if a previous session recorded it, otherwise null. When
        /// null the current epoch is taken to be stock — which is true on the first load of any save
        /// AGORA has not already rewritten, and is why the value must be persisted before the first
        /// write rather than after it.
        /// </param>
        public static StartYearPlan Plan(StartYearDeliveryMode mode, int politicalStartYear,
                                         int currentEpochYear, int? recordedStockEpochYear)
        {
            int target = SimClockMath.ClampYear(politicalStartYear);
            bool haveStock = recordedStockEpochYear.HasValue;

            // ?? rather than `haveStock ? recordedStockEpochYear.Value : ...`, which is the same
            // thing but which the nullable analyser cannot prove safe — and this file is compiled
            // into Agora.Core.Tests, where nullable analysis is on.
            int stock = recordedStockEpochYear ?? currentEpochYear;

            switch (mode)
            {
                case StartYearDeliveryMode.Off:
                    if (haveStock && currentEpochYear != stock)
                    {
                        return new StartYearPlan(
                            StartYearAction.RestoreEpoch, stock, 0, stock,
                            $"Agora off: restoring the stock epoch year {stock} (was {currentEpochYear}).");
                    }
                    return new StartYearPlan(
                        StartYearAction.None, currentEpochYear, 0, stock,
                        $"Agora off: the clock is already stock at {currentEpochYear}.");

                case StartYearDeliveryMode.OffsetOnly:
                    if (haveStock && currentEpochYear != stock)
                    {
                        return new StartYearPlan(
                            StartYearAction.RestoreEpoch, stock, target - stock, stock,
                            $"Offset-only: restoring the stock epoch year {stock} and shifting political dates by {target - stock}.");
                    }
                    return new StartYearPlan(
                        StartYearAction.None, currentEpochYear, target - currentEpochYear, stock,
                        $"Offset-only: leaving the game clock at {currentEpochYear} and shifting political dates by {target - currentEpochYear}.");

                case StartYearDeliveryMode.RewriteGameEpoch:
                    if (currentEpochYear == target)
                    {
                        return new StartYearPlan(
                            StartYearAction.None, target, 0, stock,
                            $"Epoch already at the political start year {target}; nothing to write.");
                    }
                    return new StartYearPlan(
                        StartYearAction.WriteEpoch, target, 0, stock,
                        $"Rewriting the epoch year {currentEpochYear} -> {target} (stock recorded as {stock}).");

                default:
                    // An unknown mode must not silently rewrite the player's clock.
                    return new StartYearPlan(
                        StartYearAction.None, currentEpochYear, 0, stock,
                        $"Unrecognised delivery mode {(int)mode}; leaving the clock untouched.");
            }
        }

        /// <summary>
        /// The authoritative offset: political year = game year + this. Derived from the epoch the
        /// caller actually observes <i>after</i> applying the plan, so if the component write was
        /// refused — no <c>TimeData</c> singleton, a future build that seals the field — AGORA's own
        /// calendar still lands on the configured start year and only the HUD is wrong. That is the
        /// safe direction to fail: a mislabelled HUD is cosmetic, a mislabelled political year would
        /// desync every seeded stream.
        /// </summary>
        public static int PoliticalYearOffset(StartYearDeliveryMode mode, int politicalStartYear,
                                              int observedEpochYear)
        {
            if (mode == StartYearDeliveryMode.Off)
            {
                return 0;
            }
            return SimClockMath.ClampYear(politicalStartYear) - observedEpochYear;
        }
    }
}
