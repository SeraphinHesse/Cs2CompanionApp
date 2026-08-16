using System;
using System.Collections.Generic;
using Agora.Core.Contracts;
using Agora.Core.Stories;
using Agora.Core.Tuning;

namespace Agora.Mod.Core
{
    /// <summary>
    /// The player's four inbound channels on the story surface: choosing how to tackle a slot,
    /// declaring the outcome of one they took on themselves, asking for a story to resolve early, and
    /// buying a slot off with political power.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A separate file rather than four more methods on <c>AgoraRuntime.cs</c>, which is why that type
    /// is <c>partial</c> — at 3000+ lines it is the file every wave wants, and splitting is what keeps
    /// parallel lanes from colliding in it. Everything below follows the shape
    /// <see cref="AgoraRuntime.SetSetting"/> established and <see cref="AgoraRuntime.RenameParty"/>
    /// repeats: the <c>Gate</c>, the active-save guard, validation before any write, a
    /// <c>_stateVersion++</c> on every acceptance, and a catch that logs the exception and hands back
    /// <see cref="CommandOutcome.Failed"/>. No exception text ever crosses the bridge — the panel
    /// switches on the value (<c>docs/contracts/ui_bindings.md</c> §4.5).
    /// </para>
    /// <para>
    /// <b>What persists, and when.</b> Not <c>PersistSettings()</c>: a player's choice on a story is
    /// state, not a setting. Every command below mutates the live objects inside <c>_state</c>, which
    /// is the same object <c>GetStateForSave</c> hands to <c>AgoraSidecarSystem.PreSerialize</c>, so
    /// the choice reaches <c>state_*.json</c> with the player's next game save and no sooner —
    /// exactly as the party editors do, and for the reason recorded there: an out-of-band write from
    /// the UI update tick races the save path for the same file, and a choice that survives a crash
    /// the surrounding city does not is a sidecar describing a city that never existed (#6). That is
    /// also what <see cref="PlayerCommand"/>'s own remarks describe as "persisted the moment it is
    /// recorded" — the record is appended now, and the save path already runs on every save, so a
    /// choice made in month M is on disk before M+1's tick reads it.
    /// </para>
    /// <para>
    /// <b>No political logic here.</b> Nothing below scores a slot, prices an override or decides an
    /// outcome. The tier comes from <see cref="CivicEvent.TierUnder"/>, affordability from
    /// <see cref="PoliticalPower.CanAfford"/>, the debit from <see cref="PowerLedger.TrySpend"/>, the
    /// command log's ordering from <see cref="PlayerCommandLog.Append"/>, and the verdict from
    /// <c>StoryResolution</c> at resolution time. What is left here is finding the slot the player
    /// addressed, refusing what may not be done to it, and writing down what they chose.
    /// </para>
    /// <para>
    /// <b>Synchronous, and doing no ECS work</b>, for the reasons set out on
    /// <see cref="AgoraRuntime.SetSetting"/>: these run from a <c>CallBinding</c> on the UI phase,
    /// which keeps ticking while the sim is paused — and it will be, because a story card holds the
    /// pause barrier while it is up. Deferring the work to the next engine tick would mean a player
    /// who answered a story at speed zero saw nothing happen at all.
    /// </para>
    /// </remarks>
    public static partial class AgoraRuntime
    {
        /// <summary>
        /// The player chose how to tackle one slot of a live story. Backs
        /// <c>agora.stories.setResponse</c>.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <b><see cref="SlotResponse.PowerOverride"/> is not settable through here</b>, and that is
        /// the load-bearing refusal in this method rather than a tidiness rule. An override is bought,
        /// and it is bought through <see cref="SpendPowerOverride"/> where the affordability check and
        /// the debit live; accepting it as a mode would hand the player the one response that scores
        /// <see cref="SlotOutcome.Met"/> unconditionally, for nothing, with no ledger entry to show
        /// for it. It is refused as <see cref="CommandOutcome.BadValue"/> — the mode named is not a
        /// legal value for this field.
        /// </para>
        /// <para>
        /// <b><see cref="SlotResponse.Unaddressed"/> is not settable either.</b> It is the state a slot
        /// starts in, not a choice, and offering it back would make "I have not decided" a decision
        /// the log records — the same reasoning that makes an empty string a rejection rather than a
        /// reset on <see cref="CommandOutcome.ValueRequired"/>. Changing one's mind is a second
        /// <see cref="SetStoryResponse"/> naming the new mode, which the log carries as two dated
        /// commands and replays in order.
        /// </para>
        /// <para>
        /// <b>A slot already bought off may not be moved out of
        /// <see cref="SlotResponse.PowerOverride"/> at all</b>, and this is the refusal that stops a
        /// paid purchase being thrown away by a mis-click. Without it the sequence is: buy a mandatory
        /// slot, pick <c>Goal</c> on it — accepted, no refund, no ledger credit, no warning — and then
        /// press the override again, which <see cref="SpendPowerOverride"/>'s already-bought guard no
        /// longer catches, because the response is no longer <see cref="SlotResponse.PowerOverride"/>.
        /// The player is charged twice for one slot and told nothing either time. Refusal rather than
        /// a refund, because "an override is bought, not chosen" is already the rule this method
        /// enforces in the other direction, and a refund path would need a credit reason the ledger
        /// does not have. The player who genuinely wants the purchase undone has the same recourse
        /// they have for any spend: none, deliberately.
        /// </para>
        /// <para>
        /// <b><paramref name="text"/> is prose and is never parsed for a number</b> (non-negotiable
        /// #1). It is capped at <c>stories.freeTextMaxLength</c> and over-length input is rejected
        /// with <see cref="CommandOutcome.TooLong"/>; empty is accepted, because the box is the
        /// player's to fill or not and only the modes that render one will carry anything.
        /// </para>
        /// </remarks>
        public static CommandOutcome SetStoryResponse(string storyId, string eventId, string mode,
                                                      string text)
        {
            lock (Gate)
            {
                try
                {
                    Story story;
                    StorySlot slot;
                    CommandOutcome found = FindSlot(storyId, eventId, out story, out slot);
                    if (found != CommandOutcome.Ok) return found;

                    // Bought, and therefore no longer up for discussion. See the remarks: this is what
                    // stops a paid override being replaced for free and then charged a second time.
                    if (slot.Response == SlotResponse.PowerOverride) return CommandOutcome.BadValue;

                    SlotResponse response;
                    CommandOutcome parsed = ParseResponse(mode, out response);
                    if (parsed != CommandOutcome.Ok) return parsed;

                    string accepted;
                    CommandOutcome capped = CapFreeText(text, out accepted);
                    if (capped != CommandOutcome.Ok) return capped;

                    // The log entry goes down BEFORE the slot is touched, and the order is what makes
                    // the catch below honest: everything after this line is a field assignment that
                    // cannot throw, so a failure here really does leave the previous choice standing.
                    // The reverse order left a torn slot behind a message asserting nothing had moved.
                    RecordCommand(PlayerCommandKind.SetResponse, story.Id, slot.EventId, response,
                                  accepted);

                    slot.Response = response;
                    slot.PlayerText = accepted;

                    // Leaving Manual by any route drops the declaration with it. A slot the player
                    // declared and then moved to Goal would otherwise keep ManualDeclared set, and a
                    // later move back to Manual would silently restore a declaration they never
                    // repeated — the flag is the record of a declaration, not of a past one.
                    if (response != SlotResponse.Manual) slot.ManualDeclared = false;

                    _stateVersion++;
                    return CommandOutcome.Ok;
                }
                catch (Exception ex)
                {
                    AgoraMod.Log.Error(ex, "Agora: the response to '" + (eventId ?? "(null)") +
                                           "' in story '" + (storyId ?? "(null)") + "' could not be " +
                                           "recorded; the previous choice stands.");
                    return CommandOutcome.Failed;
                }
            }
        }

        /// <summary>
        /// The player declared, in their own words, how a slot they took on manually came out. Backs
        /// <c>agora.stories.declareManual</c>.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <b>Only a <see cref="SlotResponse.Manual"/> slot may be declared</b>, and a slot in any
        /// other mode is refused with <see cref="CommandOutcome.BadValue"/> rather than converted.
        /// <c>StoryResolution.ScoreSlot</c> reads <see cref="StorySlot.ManualDeclared"/> for
        /// <see cref="SlotResponse.Manual"/> and for nothing else, so silently switching the mode here
        /// would let this command take a slot off its measured check — turning the declare button into
        /// a second, unpriced way of setting a response. The player picks Manual through
        /// <see cref="SetStoryResponse"/> first.
        /// </para>
        /// <para>
        /// <b>What <paramref name="met"/> writes, and what it does not.</b> On the slot it sets
        /// <see cref="StorySlot.ManualDeclared"/> and nothing else — the slot's
        /// <see cref="StorySlot.SlotOutcome"/> stays <see cref="SlotOutcome.Pending"/> until the story
        /// resolves, because scoring is <c>Agora.Core</c>'s and writing a verdict here would put the
        /// engine's answer in this assembly's hands. A declaration of failure is recorded on the slot
        /// as the flag staying down, which is the same slot state an undeclared Manual slot is in and
        /// scores the same <see cref="SlotOutcome.NotMet"/> at the event's real tier. That is
        /// deliberate and is the property <see cref="PoliticalPowerState"/>'s remarks turn on: honest
        /// self-reporting is exactly as expensive as silence and never worse.
        /// </para>
        /// <para>
        /// <b>On the log it also writes <see cref="PlayerCommand.DeclaredMet"/>, and that field is why
        /// the two are distinguishable at all.</b> Without it a declared success and a declared
        /// failure appended rows differing in no field, so the log — which the contract says is
        /// replayed rather than re-solicited — could not reconstruct
        /// <see cref="StorySlot.ManualDeclared"/>, and a replay would score an award the player never
        /// earned. Setting it here is what makes the paragraph above true rather than merely intended.
        /// </para>
        /// <para>
        /// <b><paramref name="text"/> is required when <paramref name="met"/> is true, and optional
        /// when it is false.</b> A declared success is the one verdict in the engine nothing can
        /// check and the only control that mints an award, so the player's own account of it is the
        /// entire record and an empty one is indistinguishable from a mis-click. Requiring it on the
        /// failure side too would invert the incentive
        /// <see cref="PoliticalPower.AwardFor"/>'s one-sided cap exists to protect: that cap makes an
        /// honest self-reported failure cost exactly what silence costs and never more, so friction
        /// added to the failure path alone makes not pressing the button the cheapest play and loses
        /// the record the requirement is there to capture. Either way it is prose and is never parsed
        /// for a number (non-negotiable #1); a missing success justification is
        /// <see cref="CommandOutcome.ValueRequired"/> and an over-long box of either kind is
        /// <see cref="CommandOutcome.TooLong"/>.
        /// </para>
        /// </remarks>
        public static CommandOutcome DeclareManualOutcome(string storyId, string eventId, bool met,
                                                          string text)
        {
            lock (Gate)
            {
                try
                {
                    Story story;
                    StorySlot slot;
                    CommandOutcome found = FindSlot(storyId, eventId, out story, out slot);
                    if (found != CommandOutcome.Ok) return found;

                    // See the remarks: refused rather than converted, because converting would make
                    // this a second way of setting a response — and the one that skips the check.
                    if (slot.Response != SlotResponse.Manual) return CommandOutcome.BadValue;

                    string accepted;
                    CommandOutcome capped = CapFreeText(text, out accepted);
                    if (capped != CommandOutcome.Ok) return capped;

                    // Required on the awarding side only — see the remarks for why the failure side
                    // must stay frictionless.
                    if (met && accepted.Length == 0) return CommandOutcome.ValueRequired;

                    // Recorded before the slot moves, so the catch below is telling the truth. Nothing
                    // after this line can throw.
                    RecordCommand(PlayerCommandKind.DeclareManualOutcome, story.Id, slot.EventId,
                                  SlotResponse.Manual, accepted, met);

                    slot.ManualDeclared = met;
                    slot.PlayerText = accepted;

                    _stateVersion++;
                    return CommandOutcome.Ok;
                }
                catch (Exception ex)
                {
                    AgoraMod.Log.Error(ex, "Agora: the declared outcome for '" + (eventId ?? "(null)") +
                                           "' in story '" + (storyId ?? "(null)") + "' could not be " +
                                           "recorded; the slot is unchanged.");
                    return CommandOutcome.Failed;
                }
            }
        }

        /// <summary>
        /// The player asked for a story to be resolved now rather than on its due month. Backs
        /// <c>agora.stories.resolveNow</c>.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Records the request and nothing more. The resolution itself belongs to the story cycle,
        /// which runs inside <c>Agora.Core</c> on the next engine tick and reads
        /// <see cref="Story.ResolveEarlyRequested"/> there — resolving from here would put the verdict
        /// in this assembly and would run it against a city the sensors have not sampled this tick.
        /// The player sees the card change on the next month boundary, not on the click.
        /// </para>
        /// <para>
        /// <b>Asking twice is <see cref="CommandOutcome.Ok"/>, not an error.</b> A double-click, or a
        /// second press racing the republish, is not something the player did wrong, and a refusal
        /// there would put a worrying sentence in front of someone who did nothing — the same rule
        /// <see cref="AckAlert"/> follows for the same reason. Asking after the verdict has landed is
        /// <see cref="CommandOutcome.AlreadyResolved"/>: the story is still in the archive to read,
        /// but the window closed.
        /// </para>
        /// </remarks>
        public static CommandOutcome ResolveNow(string storyId)
        {
            lock (Gate)
            {
                try
                {
                    Story story;
                    CommandOutcome found = FindStory(storyId, out story);
                    if (found != CommandOutcome.Ok) return found;

                    // The log gets one command per press, which is what keeps replay a function of the
                    // log rather than of what the log happened to skip. Recorded first, as everywhere
                    // else on this surface, so the catch below cannot claim a state it did not leave.
                    RecordCommand(PlayerCommandKind.ResolveNow, story.Id, "",
                                  SlotResponse.Unaddressed, "");

                    // Idempotent, and it still returns Ok — see the remarks.
                    story.ResolveEarlyRequested = true;

                    _stateVersion++;
                    return CommandOutcome.Ok;
                }
                catch (Exception ex)
                {
                    AgoraMod.Log.Error(ex, "Agora: story '" + (storyId ?? "(null)") + "' could not be " +
                                           "asked to resolve early; it resolves on its due month.");
                    return CommandOutcome.Failed;
                }
            }
        }

        /// <summary>
        /// The player bought a slot off with political power. <b>Backs no binding yet</b> — see the
        /// remarks.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <b>The contract has no binding for this command, and it needs one.</b>
        /// <c>docs/plans/0004-event-system-rework.md</c> §605 lists three inbound story bindings —
        /// <c>setResponse</c>, <c>declareManual</c>, <c>resolveNow</c> — on the assumption that the
        /// panel would send <see cref="SlotResponse.PowerOverride"/> through <c>setResponse</c> like
        /// any other mode. <see cref="SetStoryResponse"/> refuses it, deliberately and for the reasons
        /// given there, so a fourth call binding has to be added to
        /// <c>docs/contracts/ui_bindings.md</c> before anything can be wired to this. Named here
        /// rather than invented: whatever wave 6 calls it is what this comment should then say.
        /// </para>
        /// <para>
        /// <b>The one command on this surface that spends something, and the one that may not fail
        /// quietly.</b> Affordability is asked of <see cref="PoliticalPower.CanAfford"/> before
        /// anything is written and a refusal is surfaced as
        /// <see cref="CommandOutcome.InsufficientPower"/>; the debit itself is
        /// <see cref="PowerLedger.TrySpend"/>'s, and its returned state is what
        /// <see cref="PoliticalState.Power"/> becomes. <b>Its <c>bool</c> is the whole success
        /// signal</b>, and nothing here second-guesses it by watching the balance: a refused spend and
        /// a legitimately free one both leave the balance where it was, so a movement test would
        /// report a correctly granted zero-cost override to the player as
        /// <see cref="CommandOutcome.InsufficientPower"/>.
        /// <see cref="PoliticalPower.OverrideCost"/> is read solely for the log line.
        /// </para>
        /// <para>
        /// <b>Debt is not a refusal.</b> A negative balance still buys anything it covers, which is
        /// encoded in <see cref="PoliticalPower.CanAfford"/> and deliberately not re-decided here.
        /// With <c>power.enabled</c> off the answer is <see cref="CommandOutcome.PowerDisabled"/>
        /// rather than <see cref="CommandOutcome.InsufficientPower"/>: the balance cannot grow either,
        /// so "you cannot afford it" would point the player at a number they can never reach.
        /// </para>
        /// <para>
        /// <b>Buying a slot that is already bought is <see cref="CommandOutcome.Ok"/> and is not
        /// charged again.</b> The response is already <see cref="SlotResponse.PowerOverride"/>, so
        /// there is nothing to do and nothing to say — and a second debit for a second click is
        /// precisely the failure the check exists to prevent.
        /// </para>
        /// </remarks>
        public static CommandOutcome SpendPowerOverride(string storyId, string eventId)
        {
            lock (Gate)
            {
                try
                {
                    Story story;
                    StorySlot slot;
                    CommandOutcome found = FindSlot(storyId, eventId, out story, out slot);
                    if (found != CommandOutcome.Ok) return found;

                    // Already bought. Accepted, silent, and above all not charged twice.
                    if (slot.Response == SlotResponse.PowerOverride) return CommandOutcome.Ok;

                    EngineTuning tuning = Tuning;
                    if (!tuning.Power.Enabled) return CommandOutcome.PowerDisabled;

                    // The tier is the event's own, derived from its severity through the two tuning
                    // thresholds — never the story's, because a story is a bundle whose slots differ.
                    CivicEvent civicEvent = FindCivicEvent(slot.EventId);
                    if (civicEvent == null)
                    {
                        // The catalog no longer explains a slot it drafted: a gap on our side, not
                        // something the player addressed wrongly, so it is Failed rather than NotFound
                        // and the detail goes to the log.
                        AgoraMod.Log.Warn("Agora: story '" + story.Id + "' names event '" + slot.EventId +
                                          "', which the loaded civic catalog does not contain; the " +
                                          "override cannot be priced and was refused.");
                        return CommandOutcome.Failed;
                    }

                    StoryTier tier = civicEvent.TierUnder(tuning.Stories.MandatorySeverityThreshold,
                                                          tuning.Stories.MajorSeverityThreshold);

                    if (!PoliticalPower.CanAfford(_state.Power, tier, tuning))
                        return CommandOutcome.InsufficientPower;

                    // TrySpend clones rather than mutating, so nothing the player owns has moved until
                    // the assignments below. A false is the refusal — the same CanAfford answer asked
                    // again as a backstop — and it is surfaced rather than swallowed.
                    PoliticalPowerState debited;
                    if (!PowerLedger.TrySpend(_state.Power, story.Id, slot.EventId, tier, CommandDate(),
                                              tuning, out debited))
                    {
                        return CommandOutcome.InsufficientPower;
                    }

                    if (debited == null)
                    {
                        AgoraMod.Log.Error("Agora: the political-power ledger granted an override of '" +
                                           slot.EventId + "' and returned no state to carry it; the " +
                                           "slot has not been bought and the balance is unchanged.");
                        return CommandOutcome.Failed;
                    }

                    // Recorded before the state moves, as everywhere else on this surface: every line
                    // below is a field assignment that cannot throw, so the catch's claim that neither
                    // the slot nor the balance moved is true wherever it is reached.
                    RecordCommand(PlayerCommandKind.SpendPowerOverride, story.Id, slot.EventId,
                                  SlotResponse.PowerOverride, "");

                    // Both writes together: the slot is never bought without being paid for, and never
                    // paid for without being bought.
                    _state.Power = debited;
                    slot.Response = SlotResponse.PowerOverride;

                    // The declaration does not survive the mode change, for the reason given in
                    // SetStoryResponse: the flag is the record of a declaration in force. The text goes
                    // with it — StorySlot.PlayerText is scoped to Ignore and Manual, so a justification
                    // typed for an abandoned Manual response would otherwise sit on a bought slot,
                    // where a panel rendering "the player's own words" attributes it to the purchase.
                    slot.ManualDeclared = false;
                    slot.PlayerText = "";

                    _stateVersion++;

                    AgoraMod.Log.Info("Agora: bought off '" + slot.EventId + "' in story '" + story.Id +
                                      "' at the " + tier + " rate for " +
                                      PoliticalPower.OverrideCost(tier, tuning) +
                                      "; the balance is now " + debited.Balance + ".");

                    return CommandOutcome.Ok;
                }
                catch (Exception ex)
                {
                    AgoraMod.Log.Error(ex, "Agora: the override for '" + (eventId ?? "(null)") +
                                           "' in story '" + (storyId ?? "(null)") + "' could not be " +
                                           "bought; the slot and the balance are unchanged.");
                    return CommandOutcome.Failed;
                }
            }
        }

        // ------------------------------------------------------------------ shared validation

        /// <summary>
        /// Resolves a live story by id, or says why it could not be addressed.
        /// </summary>
        /// <remarks>
        /// The two rejections are deliberately different answers.
        /// <see cref="CommandOutcome.AlreadyResolved"/> is for a story that reached a verdict — it is
        /// still in the archive and the player can still read it, and merging it into
        /// <see cref="CommandOutcome.NotFound"/> would tell them their own resolved story never
        /// happened. <see cref="CommandOutcome.NotFound"/> is for an id no story in this save carries.
        /// </remarks>
        private static CommandOutcome FindStory(string storyId, out Story story)
        {
            story = null;

            if (!_attached || !_saveActive || _state == null) return CommandOutcome.NoActiveSave;
            if (string.IsNullOrEmpty(storyId)) return CommandOutcome.ValueRequired;

            List<Story> live = _state.LiveStories;
            for (int i = 0; live != null && i < live.Count; i++)
            {
                if (live[i] == null || string.CompareOrdinal(live[i].Id, storyId) != 0) continue;

                // A live story whose verdict has already been stamped: the resolution pass writes the
                // outcome before it dispatches anything, so this is reachable in the window before the
                // story is moved to the archive.
                if (live[i].Outcome != StoryOutcome.Pending) return CommandOutcome.AlreadyResolved;

                story = live[i];
                return CommandOutcome.Ok;
            }

            // Not live. If the archive holds it, the window closed rather than the story never having
            // existed, and the player is told which of the two it was.
            List<Story> archive = _state.StoryArchive;
            for (int i = 0; archive != null && i < archive.Count; i++)
            {
                if (archive[i] != null && string.CompareOrdinal(archive[i].Id, storyId) == 0)
                    return CommandOutcome.AlreadyResolved;
            }

            return CommandOutcome.NotFound;
        }

        /// <summary>
        /// Resolves one slot of a live story, or says why it could not be addressed. Every rejection
        /// <see cref="FindStory"/> can make is a rejection this can make.
        /// </summary>
        private static CommandOutcome FindSlot(string storyId, string eventId, out Story story,
                                               out StorySlot slot)
        {
            slot = null;

            CommandOutcome found = FindStory(storyId, out story);
            if (found != CommandOutcome.Ok) return found;

            if (string.IsNullOrEmpty(eventId)) return CommandOutcome.ValueRequired;

            List<StorySlot> slots = story.Slots;
            for (int i = 0; slots != null && i < slots.Count; i++)
            {
                if (slots[i] == null || string.CompareOrdinal(slots[i].EventId, eventId) != 0) continue;

                slot = slots[i];
                return CommandOutcome.Ok;
            }

            // The story exists and the field named is real; it addressed a slot the story does not
            // carry, which is exactly what NotFound is for.
            return CommandOutcome.NotFound;
        }

        /// <summary>The catalog entry behind a slot, or null when the catalog no longer carries it.</summary>
        private static CivicEvent FindCivicEvent(string eventId)
        {
            IReadOnlyList<CivicEvent> events = CivicCatalog.Events;
            for (int i = 0; i < events.Count; i++)
            {
                if (events[i] != null && string.CompareOrdinal(events[i].Id, eventId) == 0) return events[i];
            }

            return null;
        }

        /// <summary>
        /// Parses a response mode by enum name, and refuses the two members that are not the player's
        /// to choose. See <see cref="SetStoryResponse"/> for why each is refused.
        /// </summary>
        /// <remarks>
        /// Parsed by name and case-sensitively, exactly as the settings levels are and for the same
        /// reason: <c>Enum.TryParse</c> also accepts a bare number, and a panel that sent "3" would
        /// silently select whichever member happens to sit at 3 today — which today is
        /// <see cref="SlotResponse.PowerOverride"/>, the one mode that must never arrive this way.
        /// </remarks>
        private static CommandOutcome ParseResponse(string mode, out SlotResponse response)
        {
            response = SlotResponse.Unaddressed;

            if (string.IsNullOrEmpty(mode)) return CommandOutcome.ValueRequired;

            for (int i = 0; i < mode.Length; i++)
            {
                if (mode[i] >= '0' && mode[i] <= '9') return CommandOutcome.BadValue;
            }

            SlotResponse parsed;
            if (!Enum.TryParse(mode, /*ignoreCase:*/ false, out parsed)) return CommandOutcome.BadValue;
            if (!Enum.IsDefined(typeof(SlotResponse), parsed)) return CommandOutcome.BadValue;

            // Bought, not chosen — SpendPowerOverride owns this one, with the affordability check and
            // the debit attached to it.
            if (parsed == SlotResponse.PowerOverride) return CommandOutcome.BadValue;

            // The state a slot starts in, not a choice the player may make.
            if (parsed == SlotResponse.Unaddressed) return CommandOutcome.BadValue;

            response = parsed;
            return CommandOutcome.Ok;
        }

        /// <summary>
        /// Normalises the player's free text and enforces <c>stories.freeTextMaxLength</c>.
        /// </summary>
        /// <remarks>
        /// <b>Never parsed, never measured, never turned into a number</b> (non-negotiable #1) — the
        /// only thing asked of it is its length, and the only answers are "keep it" and
        /// <see cref="CommandOutcome.TooLong"/>. Truncating instead of rejecting is deliberately not
        /// an option: the player would not be told, and the half-sentence left behind would be
        /// attributed to them.
        /// </remarks>
        private static CommandOutcome CapFreeText(string text, out string accepted)
        {
            accepted = text ?? "";

            int max = Tuning.Stories.FreeTextMaxLength;

            // A hand-edited tuning file with a non-positive cap falls back to the compiled default
            // rather than to a number written here (data/CLAUDE.md rule 4).
            if (max <= 0) max = EngineTuning.Default.Stories.FreeTextMaxLength;

            // And if that is somehow non-positive too, the cap is zero and every non-empty box is
            // refused. NOT "unbounded": the third step used to be a fall-through to no limit, which
            // let an arbitrarily long string into the sidecar on the strength of a broken tuning file.
            // A surface that refuses every justification is loud, recoverable and obviously wrong; a
            // sidecar carrying a megabyte of prose is none of those.
            if (max < 0) max = 0;

            return accepted.Length > max ? CommandOutcome.TooLong : CommandOutcome.Ok;
        }

        // ------------------------------------------------------------------ the command log

        /// <summary>
        /// Appends one accepted command to <see cref="PoliticalState.PlayerCommands"/>, in the order
        /// that log declares.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <b>The log is engine state, not a UI trace</b> — per the amendment to non-negotiable #3
        /// recorded on <see cref="PlayerCommand"/>, engine state at date D is a pure function of its
        /// inputs <i>and of the ordered, dated log of player commands up to D</i>. It is replayed and
        /// never re-solicited, which is what lets an asynchronous choice sit inside a deterministic
        /// engine. So every accepted command is written down even when its effect on the state is
        /// already visible in the slot beside it: the slot is the current answer, the log is how the
        /// save got there.
        /// </para>
        /// <para>
        /// <b>The ordering is <see cref="PlayerCommandLog.Append"/>'s and not this method's.</b> It
        /// stamps <see cref="PlayerCommand.Sequence"/> as the highest yet issued in the command's
        /// month — never left at zero, because two commands in one month are ordered by it and never
        /// by arrival, and arrival order is wall-clock rather than engine state — and inserts at the
        /// sort position rather than appending and re-sorting. Deciding where a record sorts in engine
        /// state is computing, not glue, and <c>src/Agora.Mod/CLAUDE.md</c> forbids that here; one
        /// documented ordering rule with an implementation on each side of the assembly boundary is
        /// how the two come to disagree. What is left in this method is filling the record in.
        /// </para>
        /// </remarks>
        /// <param name="declaredMet">
        /// Only meaningful for <see cref="PlayerCommandKind.DeclareManualOutcome"/>, and false on
        /// every other kind. It is what lets a replay tell a declared success from a declared
        /// failure — see <see cref="PlayerCommand.DeclaredMet"/>.
        /// </param>
        private static void RecordCommand(PlayerCommandKind kind, string storyId, string eventId,
                                          SlotResponse response, string freeText,
                                          bool declaredMet = false)
        {
            if (_state == null) return;

            if (_state.PlayerCommands == null) _state.PlayerCommands = new List<PlayerCommand>();

            PlayerCommandLog.Append(_state.PlayerCommands, new PlayerCommand
            {
                StoryId = storyId ?? "",
                EventId = eventId ?? "",
                Kind = kind,
                Response = response,
                FreeText = freeText ?? "",
                DeclaredMet = declaredMet,
                DecidedMonth = CommandDate().TotalMonths
            });
        }

        /// <summary>
        /// The date a command is stamped with. <b>Read, never computed</b> (non-negotiable #8).
        /// </summary>
        /// <remarks>
        /// <see cref="AgoraTimeService"/> first, because it is the only source of dates and these
        /// commands run on the UI phase, which keeps ticking while the sim is paused — so the clock is
        /// the live answer and the state's own date can be a month behind it. The fallback is
        /// <see cref="PoliticalState.Date"/>, which is itself a date the clock supplied at the last
        /// tick rather than one invented here: it is what a command gets when the clock is not
        /// readable, which is the main-menu case a save-active guard has already ruled out but which
        /// costs one comparison to survive anyway.
        /// </remarks>
        private static SimDate CommandDate()
        {
            SimDate today;
            if (_time != null && _time.TryGetToday(out today)) return today;

            return _state != null ? _state.Date : default(SimDate);
        }
    }
}
