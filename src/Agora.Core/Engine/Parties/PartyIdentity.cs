using Agora.Core.Contracts;

namespace Agora.Core.Engine.Parties
{
    /// <summary>
    /// What a player may type into a party's identity fields, and how flavor merges into a party
    /// that the player has taken fields of. The single enforcement point for both.
    ///
    /// <para>
    /// <see cref="ApplyFlavor"/> is lifted out of <c>Agora.Mod.AgoraRuntime.ApplyProseNames</c>
    /// deliberately. The mod's copy names no game type, but it lives in an assembly the headless
    /// suite cannot load, so the rule that decides whether a player's rename survives a flavor wake
    /// was the one rule in the mod that no test could reach. Moving it here makes the enforcement
    /// point the mod runs the enforcement point the suite tests.
    /// </para>
    ///
    /// <para>
    /// Four writes in the mod route through it, from two call sites, both in
    /// <c>Agora.Mod.AgoraRuntime</c>: one in <c>ApplyProseNames</c> and one in
    /// <c>EnsureEveryPartyNamed</c>, each inside that method's per-party loop. Two calls cover four
    /// writes because a single <see cref="ApplyFlavor"/> handles both pairs — the name/short-name
    /// pair, gated on the caller's <c>mayRename</c>, and the description/slogan pair, which moves
    /// independently of it. The callers differ only in how they compute <c>mayRename</c>; the merge
    /// rule and the player's locks live here, so a third caller gets them for free.
    /// </para>
    ///
    /// <para>
    /// Static and pure: no seeds, no clock, no tuning. The limits below are contract shapes taken
    /// from the shipped schemas, not coefficients — a change to one of them is a schema change, not
    /// a tuning edit, which is why they are consts here rather than keys in
    /// <c>engine_tuning.json</c>.
    /// </para>
    /// </summary>
    public static class PartyIdentity
    {
        // --- Limits ---------------------------------------------------------------------------------

        /// <summary>
        /// Longest party name a player may type, 80.
        ///
        /// <para>
        /// The number comes from <c>data/schemas/politics_flavor.schema.json</c>, which allows 80 for
        /// a party name, and from <c>StaticPoolProvider</c>, which caps its own draws at 80. A lower
        /// ceiling would let the generator produce a name the player is then forbidden to retype —
        /// the field would reject the very text sitting in it. An earlier plan said 60; nothing in
        /// this repo says 60, and 60 is the number that creates that contradiction.
        /// </para>
        /// </summary>
        public const int NameMax = 80;

        /// <summary>
        /// Longest short name a player may type, 12.
        ///
        /// <para>
        /// The binding constraint is <c>data/schemas/political_state.schema.json</c>'s own
        /// <c>$defs.party.shortName</c> <c>maxLength: 12</c>: a longer value makes the sidecar fail
        /// the schema it ships with, which is a load-time failure, not a cosmetic one.
        /// </para>
        ///
        /// <para>
        /// An earlier plan justified the 12 by claiming the seat chart depends on it. <b>It does
        /// not.</b> Every label that renders a short name ellipsises, so a 20-character short name
        /// would look cramped and nothing more. The schema is the real reason and the only one worth
        /// stating — <c>PartyIdentityTests</c> asserts the two numbers are equal so the claim cannot
        /// quietly stop being true.
        /// </para>
        /// </summary>
        public const int ShortNameMax = 12;

        /// <summary>
        /// Longest description a player may type, 600 — the flavor schema's limit for the same
        /// field, so a player-typed value and a generated one are subject to the same ceiling.
        /// </summary>
        public const int DescriptionMax = 600;

        /// <summary>
        /// Longest slogan a player may type, 120 — again the flavor schema's limit, for the same
        /// reason as <see cref="DescriptionMax"/>.
        /// </summary>
        public const int SloganMax = 120;

        /// <summary>
        /// The colour pattern, <b>published for the UI to use and not used by C#</b>. It is the same
        /// expression <c>political_state.schema.json</c> puts on <c>colorHex</c>, so the panel can
        /// pre-validate against exactly what the sidecar will be checked against.
        ///
        /// <para>
        /// <see cref="IsValidHex"/> validates by hand instead: <c>System.Text.RegularExpressions</c>
        /// is not on this hot path, and a compiled regex for six hex digits is not worth the
        /// allocation or the dependency.
        /// </para>
        /// </summary>
        public const string ColorPattern = "^#[0-9A-Fa-f]{6}$";

        // --- Colour normalisation --------------------------------------------------------------------

        /// <summary>
        /// The canonical form of a hex colour: trimmed and upper-cased. Null becomes <c>""</c>.
        /// </summary>
        /// <remarks>
        /// Upper case is canonical because the shipped palette
        /// (<c>parties.colorPalette</c>) is upper case and duplicate detection compares ordinally.
        /// Without a normalising step, a player who types <c>#c0392b</c> holds a colour that is
        /// byte-different from the palette's <c>#C0392B</c> and therefore invisible to
        /// <see cref="PartyRegistry.IsColorTaken"/>.
        /// </remarks>
        public static string NormalizeHex(string value)
        {
            if (value == null) return "";
            return value.Trim().ToUpperInvariant();
        }

        /// <summary>
        /// True for exactly <c>#</c> followed by six hex digits. Either case is accepted, because the
        /// state schema's pattern accepts either.
        /// </summary>
        public static bool IsValidHex(string value)
        {
            if (value == null || value.Length != 7) return false;
            if (value[0] != '#') return false;

            for (int i = 1; i < 7; i++)
            {
                char c = value[i];
                bool hex = (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');
                if (!hex) return false;
            }
            return true;
        }

        // --- Validation -------------------------------------------------------------------------------
        //
        // Every validator here rejects. None of them truncates, trims into shape, or otherwise alters
        // the input: a silent fix-up is a write the player did not ask for, and the one time it
        // matters — a name cut off mid-word — they would have no way to tell it happened.

        /// <summary>
        /// Whether a rename is legal. Both fields are validated together because
        /// <see cref="PartyOverrides.NameLocked"/> covers both: a rename that could not also set the
        /// short name would take ownership of the short name and then freeze it permanently, since
        /// flavor is barred from writing it from that moment on.
        /// </summary>
        public static CommandOutcome ValidateName(string name, string shortName)
        {
            if (IsBlank(name) || IsBlank(shortName)) return CommandOutcome.ValueRequired;
            if (name.Length > NameMax || shortName.Length > ShortNameMax) return CommandOutcome.TooLong;
            return CommandOutcome.Ok;
        }

        /// <summary>
        /// Whether a description edit is legal. Both fields together, for the same reason
        /// <see cref="ValidateName"/> takes both: <see cref="PartyOverrides.DescriptionLocked"/>
        /// covers the slogan too.
        /// </summary>
        public static CommandOutcome ValidateDescription(string description, string slogan)
        {
            if (IsBlank(description) || IsBlank(slogan)) return CommandOutcome.ValueRequired;
            if (description.Length > DescriptionMax || slogan.Length > SloganMax)
                return CommandOutcome.TooLong;
            return CommandOutcome.Ok;
        }

        /// <summary>
        /// Whether a colour is a legal colour.
        /// </summary>
        /// <remarks>
        /// It does <b>not</b> check whether another party already holds it. A duplicate is an
        /// acceptance with a warning (<see cref="CommandOutcome.OkColorInUse"/>) and needs the whole
        /// roster to detect, which makes it <see cref="PartyRegistry"/>'s question. Validity is a
        /// property of the string alone.
        /// </remarks>
        public static CommandOutcome ValidateColor(string colorHex)
        {
            if (IsBlank(colorHex)) return CommandOutcome.ValueRequired;
            if (!IsValidHex(colorHex)) return CommandOutcome.BadValue;
            return CommandOutcome.Ok;
        }

        // netstandard2.0 has string.IsNullOrWhiteSpace; kept behind a name so the three validators
        // read as one rule rather than three copies of it.
        private static bool IsBlank(string value) => string.IsNullOrWhiteSpace(value);

        // --- The lock-aware merge -----------------------------------------------------------------------

        /// <summary>
        /// Merges one <see cref="PartyFlavor"/> into one <see cref="Party"/>, honouring
        /// <see cref="Party.PlayerOverrides"/>.
        /// </summary>
        /// <param name="party">The party to write into. Null is a no-op.</param>
        /// <param name="flavor">The prose to merge. Null is a no-op.</param>
        /// <param name="mayRename">
        /// The caller's own naming rule: false once a name is settled, true while the party is
        /// unnamed or wearing a provisional name from the canned pool. Independent of the lock — the
        /// lock is the player's answer, this is the flavor pipeline's.
        /// </param>
        /// <param name="wroteName">
        /// True when the name was written, so the caller can maintain its provisional-name
        /// bookkeeping without re-deriving whether a write happened.
        /// </param>
        /// <remarks>
        /// <para>
        /// The two flags are <b>independent</b>. A locked name must not stop an unlocked description
        /// from moving with the politics, and a locked description must not freeze a name the player
        /// never claimed. They are tested separately here for that reason and nowhere combined.
        /// </para>
        /// <para>
        /// The description gate is the part that did not exist in the mod: description and slogan
        /// were rewritten on every successful generation, so a player-written description was gone at
        /// the very next flavor wake, minutes later, with no message and nothing in the log.
        /// </para>
        /// <para>
        /// Colour is never touched. <see cref="PartyFlavor"/> carries none — colour is engine-assigned
        /// from the palette and player-overridable, and has never been flavor-owned.
        /// </para>
        /// </remarks>
        public static void ApplyFlavor(Party party, PartyFlavor flavor, bool mayRename, out bool wroteName)
        {
            wroteName = false;
            if (party == null || flavor == null) return;

            if ((party.PlayerOverrides & PartyOverrides.NameLocked) == 0 &&
                mayRename &&
                !string.IsNullOrEmpty(flavor.Name))
            {
                party.Name = flavor.Name;
                if (!string.IsNullOrEmpty(flavor.ShortName)) party.ShortName = flavor.ShortName;
                wroteName = true;
            }

            if ((party.PlayerOverrides & PartyOverrides.DescriptionLocked) == 0)
            {
                // Each field guarded on its own: a document that carried a description but no slogan
                // must not blank the slogan the party already had.
                if (!string.IsNullOrEmpty(flavor.Description)) party.Description = flavor.Description;
                if (!string.IsNullOrEmpty(flavor.Slogan)) party.Slogan = flavor.Slogan;
            }
        }
    }
}
