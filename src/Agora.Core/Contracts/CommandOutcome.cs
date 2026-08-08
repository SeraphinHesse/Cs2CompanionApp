namespace Agora.Core.Contracts
{
    /// <summary>
    /// What became of a player-initiated write. The closed vocabulary every inbound binding answers
    /// with — <c>agora.state.setSetting</c> today, the party editors W4 adds next.
    ///
    /// <para>
    /// One enum rather than one per binding, because the panel-side handling is the same in every
    /// case ("did it take, and if not, what do I tell the player?") and a second vocabulary would
    /// mean a second switch statement that has to be kept in step with this one. A new binding that
    /// needs a reason this set does not carry <b>extends this enum</b>; it does not invent a parallel
    /// convention.
    /// </para>
    ///
    /// <para>
    /// <b>Engine-authored, always.</b> A member of this enum is the only thing that may cross the
    /// bridge as the outcome of a write. Never an exception message, never
    /// <c>ex.Message</c>, never model output — the panel switches on the value, and a string that
    /// varies with the machine's locale or the shape of an <c>IOException</c> is not switchable.
    /// Same rule, and the same reason, as <c>FlavorStatus.lastError</c>
    /// (<c>docs/contracts/ui_bindings.md</c> §4.5).
    /// </para>
    /// </summary>
    public enum CommandOutcome
    {
        /// <summary>
        /// Accepted. Crosses the bridge as the <b>empty string</b>, not as <c>"Ok"</c>: the UI's test
        /// is "is there a problem to show?", and an empty string is the same falsy answer
        /// <c>lastError</c> already gives. Also returned for a request that was already true — the
        /// player asked for the state the save is in, and nothing needed to happen.
        /// </summary>
        Ok = 0,

        /// <summary>No save is loaded, or the political layer never came up for this one.</summary>
        NoActiveSave = 1,

        /// <summary>The setting or field name is not one this build recognises.</summary>
        UnknownKey = 2,

        /// <summary>The name was recognised; the value was not a legal value for it.</summary>
        BadValue = 3,

        /// <summary>
        /// The region theme is history — the save has held an election — and the choice can no
        /// longer be changed (fixplan W3).
        /// </summary>
        ThemeLocked = 4,

        /// <summary>
        /// Something the request would have torn down is in flight; try again shortly. Today that is
        /// a running <c>claude</c> generation, which a retheme would have to dispose mid-run.
        /// </summary>
        Busy = 5,

        /// <summary>
        /// It failed, and the reason is not one the player can act on. Whatever went wrong is in
        /// <c>Agora.log</c>; this is deliberately all that reaches the panel.
        /// </summary>
        Failed = 6,

        /// <summary>
        /// No party in this save carries that id. Distinct from <see cref="UnknownKey"/>, which is
        /// about the field name: the request was well-formed and named a field that exists, it just
        /// addressed a brand that does not.
        /// </summary>
        NotFound = 7,

        /// <summary>
        /// The field was left empty.
        ///
        /// <para>
        /// <b>An empty string is a rejection and never a reset.</b> Resetting a field to its
        /// engine- or flavor-owned value is a separate binding, deliberately, because "empty means
        /// reset" makes a typo — a cleared box, a paste that did not take — indistinguishable from
        /// an intention. A player who wanted the generated name back has to say so.
        /// </para>
        /// </summary>
        ValueRequired = 8,

        /// <summary>
        /// Recognised and well-formed, but over the published limit
        /// (<see cref="Agora.Core.Engine.Parties.PartyIdentity"/>).
        ///
        /// <para>
        /// Separate from <see cref="BadValue"/> so that the panel's character counter and the
        /// rejector can say the same thing. A single "bad value" would leave the counter guessing
        /// whether it was the length it was counting that failed.
        /// </para>
        /// </summary>
        TooLong = 9,

        /// <summary>
        /// <b>An acceptance, not a rejection.</b> The colour was applied; another party already
        /// wears it, and the player is being told so rather than stopped.
        ///
        /// <para>
        /// This must <b>not</b> be mapped to <c>""</c> by <see cref="CommandOutcomes.ToWire"/>: the
        /// panel cannot show the warning if it does, and a duplicate colour is invisible on a chart
        /// until the player wonders which of the two slices is theirs. Consumers test acceptance
        /// with <see cref="CommandOutcomes.IsAccepted"/>, never with <c>== Ok</c>.
        /// </para>
        /// </summary>
        OkColorInUse = 10
    }

    /// <summary>The wire form of a <see cref="CommandOutcome"/>.</summary>
    public static class CommandOutcomes
    {
        /// <summary>
        /// The string the binding returns: <c>""</c> for <see cref="CommandOutcome.Ok"/>, the C#
        /// member name for everything else (<c>docs/contracts/ui_bindings.md</c> §2 — enums cross as
        /// their member name, never as an integer).
        /// </summary>
        /// <remarks>
        /// <see cref="CommandOutcome.OkColorInUse"/> is an acceptance and still crosses as its member
        /// name, on purpose: the empty string means "nothing to tell the player", and there is
        /// something to tell them. That is why acceptance is tested with <see cref="IsAccepted"/> on
        /// the C# side and against the enum's member name — not against <c>""</c> — on the panel's.
        /// </remarks>
        public static string ToWire(CommandOutcome outcome)
        {
            return outcome == CommandOutcome.Ok ? "" : outcome.ToString();
        }

        /// <summary>
        /// Whether the write took: <see cref="CommandOutcome.Ok"/> or
        /// <see cref="CommandOutcome.OkColorInUse"/>.
        ///
        /// <para>
        /// <b>Every consumer must ask the question this way.</b> Testing
        /// <c>outcome == CommandOutcome.Ok</c>, or a wire-level <c>=== ""</c>, reads the
        /// accepted-with-warning case as a failure — the panel would roll the swatch back to the
        /// old colour while the engine kept the new one, and the two would disagree until the next
        /// republish.
        /// </para>
        /// </summary>
        public static bool IsAccepted(CommandOutcome outcome)
        {
            return outcome == CommandOutcome.Ok || outcome == CommandOutcome.OkColorInUse;
        }
    }
}
