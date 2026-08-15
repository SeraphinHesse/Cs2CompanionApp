// Compiled into BOTH Agora.Mod and (by <Compile Link>) tests/Agora.Core.Tests: it must stay free of
// every Game.*, Unity.* and Colossal.* type. #nullable disable keeps it warning-clean in the test
// project, which enables nullable, without annotating a file the mod compiles unannotated.
#nullable disable

using System.Collections.Generic;
using Agora.Core.Contracts;

namespace Agora.Mod.Sensors
{
    /// <summary>
    /// Rebuilds past <see cref="CitySnapshot"/>s from <see cref="MetricHistory"/>, so the engine's
    /// trend window survives a reload.
    ///
    /// <para>
    /// <c>AgoraRuntime</c> held its snapshot history in a session-static list that
    /// <c>ResetForNewSave</c> cleared at every save boundary, so <c>EngineTickInput.SnapshotHistory</c>
    /// was empty on the first tick after every load. Every <c>delta</c> and <c>windowMonths</c> read
    /// goes through exactly that list: a player who played twelve months straight saw a trend fire,
    /// and the same player quitting to menu each year never did. That is the literal definition of
    /// desync, and it is why the samples now come back off disk.
    /// </para>
    ///
    /// <para>
    /// <b>A rehydrated snapshot carries only what was recorded.</b> Every other field sits at its
    /// default, and a defaulted <c>0</c> is indistinguishable from a measured one — so the honest
    /// bound on this type is the set of fields something actually reads off a <i>historical</i>
    /// snapshot, not the set <see cref="CitySnapshot"/> happens to declare. Today that set is closed
    /// and small: <c>IndicesEngine.Compute</c> is the only reader of the history, and it takes
    /// <c>Population</c> and <c>Education</c> off the city and <c>Education</c> and
    /// <c>Wealth[WealthTier.Low]</c> off each district. Widening what is recorded is how this type
    /// grows; widening what is *returned* without recording it first is how it starts lying.
    /// </para>
    /// </summary>
    public static class SnapshotRehydration
    {
        /// <summary>
        /// The most recent <paramref name="months"/> snapshots at or before <paramref name="asOf"/>,
        /// oldest first, each carrying its own <see cref="CitySnapshot.Date"/>. Never null; an empty
        /// list is the correct answer for a save with no recorded history and is not an error.
        /// </summary>
        public static List<CitySnapshot> Restore(MetricHistory history, SimDate asOf, int months)
        {
            // AGORA-SEAM(wave-0/0b): declared in the spine so lane 0a compiles against the signature
            // while lane 0b implements it in parallel. An empty list is what the runtime already got
            // on every load, so this stub is the current behaviour rather than a regression — but it
            // is not the deliverable, and 0c's golden test is what proves it stopped being a stub.
            return new List<CitySnapshot>();
        }
    }
}
