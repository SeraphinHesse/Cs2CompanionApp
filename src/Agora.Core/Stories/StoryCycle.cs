using System;
using System.Collections.Generic;
using Agora.Core.Contracts;
using Agora.Core.Tuning;

namespace Agora.Core.Stories
{
    /// <summary>
    /// The two-month cycle: sweep what was stranded, resolve what is due, draft what is next.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Here, deliberately, and not in <c>AgoraRuntime</c>.</b> The plan put this on the Mod side.
    /// <c>AgoraRuntime</c> compiles into no test — both of wave 0's blocking defects lived there,
    /// passed the build and passed 1415 tests — and the idempotence guards and the sweep boundary are
    /// exactly the arithmetic that has to be provable. What is left on the Mod side is loading the
    /// catalog and assigning two fields.
    /// </para>
    /// <para>
    /// <b>This file drives; it does not re-derive.</b> The draw is <see cref="StoryAssembler"/>, the
    /// verdict is <see cref="StoryResolution"/>, the effects are <see cref="StoryEffects"/>, the
    /// voter-facing pressure is <see cref="StoryPressure"/> and every movement of the currency is
    /// <see cref="PowerLedger"/>. Nothing below re-implements any of them, and nothing below makes a
    /// stochastic draw of its own: the only randomness in a cycle is inside the assembler, which
    /// seeds it, and a verdict is a reading rather than a sample.
    /// </para>
    ///
    /// <para><b>The invariants this satisfies, and the comparison each one produced.</b></para>
    ///
    /// <para>
    /// <b>1. A story lives one month, not <c>cycleMonths</c>.</b> <c>StoryAssembler.NewStory</c> sets
    /// the span to <c>stories.CycleMonths - 1</c>: a story drafted on M is due on M+1, and
    /// <c>cycleMonths</c> is the <i>cadence</i> between draws rather than the window a player can
    /// influence. Nothing here recomputes either number — a story's due month is read off
    /// <see cref="Story.ResolvesDate"/>, which the drafter already set, so the two cannot disagree.
    /// This file never multiplies, adds to or subtracts from <c>cycleMonths</c> at all.
    /// </para>
    /// <para>
    /// <b>2. Resolution is idempotent, and the guard is written before anything is dispatched.</b>
    /// A story that has reached a verdict never reaches another one: it is stamped
    /// <c>Outcome != Pending</c> and moved out of <see cref="PoliticalState.LiveStories"/> in the same
    /// pass, and every later pass reads only what is still live. The stamping pass runs to completion
    /// <i>before</i> the effect pass begins, so a crash or a reload between the two loses the effects
    /// rather than the record — which is the recoverable half. <c>LastStoryDraftMonth</c> and
    /// <c>LastStoryResolveMonth</c> are the phases' own watermarks and are compared the way wave 0
    /// compares <c>LastCompletedTickMonth</c>: strictly greater, so a month already run is never run
    /// again and a save re-entered at an earlier date does not re-draft under ids it already used.
    /// </para>
    /// <para>
    /// <b>3. The sweep reaps what the clock left behind — in both directions, and nothing else.</b>
    /// Three statements fix the backward boundary, and only the third is a sweep:
    /// a story due in a <i>later</i> month must not be touched; a story due <i>this</i> month must go
    /// through the ordinary resolution pass; a story whose due month has already gone past without a
    /// resolution must not stay pending forever, because <c>TickPlanner.CatchUpDates</c> drops the
    /// <i>oldest</i> months of an over-long gap and so the month it was due on may never come round.
    /// Those three partition the number line at <c>ResolvesDate.TotalMonths</c> versus
    /// <c>today.TotalMonths</c> into greater, equal and less, so the sweep is the strict inequality —
    /// <c>ResolvesDate.TotalMonths &lt; today.TotalMonths</c> — and equality belongs to the ordinary
    /// pass. Getting that boundary wrong by one in the inclusive direction would close every story
    /// the moment the player saved and reloaded mid-month, which is the double-tick class of bug wave
    /// 0 existed to remove: an ordinary reload re-enters at a date the story is still live for.
    /// </para>
    /// <para>
    /// <b>The clock can also go backwards, and the "later month" case above is not the whole of it.</b>
    /// A rewound save keeps stories dated in a future this clock will take centuries to reach: not
    /// stranded, so the backward test skips them, and not due, so the resolution pass skips them,
    /// while <see cref="StoryEffects.ForActive"/> re-requests their active effects every month until
    /// the clock catches up. The forward bound derives from what a tick can produce — a story is
    /// opened by a tick and a tick runs only at a date the clock has reached, and a story lives
    /// <c>max(1, cycleMonths - 1)</c> months from there — so a live story satisfies both
    /// <c>OpenedDate &lt;= today</c> and <c>ResolvesDate &lt;= today + max(1, cycleMonths - 1)</c>.
    /// A story drafted on this very tick sits exactly on the second bound, which is why it is strict.
    /// See <see cref="Sweep"/>.
    /// </para>
    /// <para>
    /// <b>4. A swept story with no evidence is <see cref="StoryOutcome.Abandoned"/>, not failed.</b>
    /// Abandoned pays nothing in either direction: no award, no penalty, no resolution effects and no
    /// government credit. Charging the player for months the scheduler declined to run would be the
    /// engine billing them for its own truncation — and it is not a hypothetical charge, because
    /// silence scores <see cref="SlotOutcome.NotMet"/> (see <see cref="SlotResponse"/>), so putting a
    /// stranded story through the ordinary verdict would fail almost every one of them. A swept story
    /// is scored only when it carries its own <see cref="Story.ResolutionEvidence"/>, because then the
    /// verdict does not depend on re-reading a month that has gone.
    /// </para>
    /// <para>
    /// <b>5. Replay does neither.</b> See <see cref="StoryCycleInput.IsReplay"/> for the two hazards
    /// that decided it. One warning is emitted per cycle phase actually skipped, so the count is
    /// countable rather than invented — a single tick can only ever see its own month.
    /// </para>
    /// <para>
    /// <b>6. The archive is trimmed here</b>, by its documented sort key
    /// <c>(ResolvedMonth descending, Id ordinal)</c> and to <c>stories.archiveRetention</c>. Nothing
    /// in this file depends on the bound for correctness: the re-use cooldown is a duration held on
    /// <see cref="EventPoolEntry.LastDraftedMonth"/>, and the archive is never consulted for
    /// eligibility.
    /// </para>
    /// <para>
    /// <b>7. Every list leaves sorted</b>, by the key its contract declares. An unsorted list fails
    /// the state hash while nothing is actually wrong.
    /// </para>
    /// </remarks>
    public static class StoryCycle
    {
        private static readonly CivicEvent[] NoEvents = new CivicEvent[0];

        /// <summary>Runs one cycle. See the remarks on the class for what that has to mean.</summary>
        public static StoryCycleResult Run(StoryCycleInput input)
        {
            if (input == null) throw new ArgumentNullException(nameof(input));

            var result = new StoryCycleResult();

            PoliticalState state = input.State ?? new PoliticalState();
            EngineTuning tuning = input.Tuning ?? EngineTuning.Default;
            StoriesTuning stories = tuning.Stories;
            AgoraSettings settings = state.Settings ?? new AgoraSettings();
            IReadOnlyList<CivicEvent> catalog = input.Catalog ?? NoEvents;
            StoryReadContext context = input.Context ?? new StoryReadContext();
            SimDate today = input.Today;

            if (state.LiveStories == null) state.LiveStories = new List<Story>();
            if (state.StoryArchive == null) state.StoryArchive = new List<Story>();

            // Both halves of the master switch, and off is inert rather than wound down: nothing
            // drafts, nothing resolves, nothing ages and no power moves, so turning the layer back on
            // resumes where it left off instead of handing the player a pool that pitied itself and a
            // shelf of stories abandoned while they were not looking. Same posture and same reason as
            // StoryAssembler.Draft, which is the other place the pair is read.
            if (!stories.Enabled || !settings.StoriesEnabled) return result;

            if (input.IsReplay)
            {
                // Suspended entirely, for the two hazards on StoryCycleInput.IsReplay. One line per
                // phase that would have run, so the catch-up log can count the skipped cycles rather
                // than be told a number this tick has no way of knowing. Months carrying neither
                // phase say nothing: a replayed decade would otherwise emit a hundred lines about
                // work that was never due.
                if (input.IsStoryDraft) result.Warnings.Add(Suspended(today, "draft"));
                if (input.IsStoryResolve) result.Warnings.Add(Suspended(today, "resolve"));
                return result;
            }

            // Everything that reached a verdict this tick, with the per-slot outcomes the effects and
            // the awards are read off. Held rather than acted on so that every story is stamped before
            // the first effect is emitted — invariant 2.
            var verdicts = new List<Verdict>();

            Sweep(state, catalog, context, tuning, today, verdicts, result.Warnings);
            ResolveDue(input, state, catalog, context, tuning, today, verdicts);
            Retire(state, stories, verdicts);
            Draft(input, state, catalog, context, tuning, today, result);

            state.LiveStories.Sort(CompareStoriesById);

            // The stories the voter model and the effect sink see this tick: everything still open —
            // including what was drafted a moment ago, because an argument starts moving the city in
            // the month it opens — plus the verdicts that landed on this very tick.
            List<Story> justResolved = Scored(verdicts);

            MovePower(input, state, catalog, tuning, today, justResolved, result);

            // The effect pass, strictly after the stamping pass above. (active, resolution, debt) is
            // the order StoryCycleResult.EffectRequests declares. Every one of these is capped by the
            // resolver it came from; the sink clamps again regardless (non-negotiable #5).
            result.EffectRequests.AddRange(StoryEffects.ForActive(state.LiveStories, catalog, tuning));

            // Sorted before it is walked. The sweep and the ordinary pass each contribute in id
            // order, so concatenating them is deterministic already — but it is deterministic by an
            // accident of which pass ran first, and one declared order costs a sort and cannot be
            // undone by a later reordering of the passes.
            verdicts.Sort(CompareVerdictsById);

            for (int i = 0; i < verdicts.Count; i++)
            {
                Verdict verdict = verdicts[i];

                // An abandoned story requests nothing: it has no verdict to be a consequence of, and
                // its slots never scored. Invariant 4, on the effect side.
                if (verdict.Story.Outcome == StoryOutcome.Abandoned) continue;

                result.EffectRequests.AddRange(
                    StoryEffects.ForResolution(verdict.Story, verdict.SlotOutcomes, catalog, tuning));
            }

            AppendDebtPenalty(state, settings, tuning, result);

            result.Pressures = StoryPressure.For(state.LiveStories, justResolved, catalog, tuning);
            result.ResolvedStories = Retired(verdicts);
            return result;
        }

        // --- the sweep -------------------------------------------------------------------------

        /// <summary>
        /// Reaps the stories the clock has left behind: those whose due month has already gone past,
        /// and those dated so far ahead that no tick this clock ran could have drafted them.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <b>The backward boundary is strict, and the two cases it must not catch are why.</b> A
        /// story due this month is resolved by <see cref="ResolveDue"/> on its own terms; a story due
        /// later is in flight and is not this function's business. Only
        /// <c>ResolvesDate &lt; today</c> is stranded. The common way to arrive at a date a story is
        /// still live for is an ordinary mid-month reload, and a sweep that fired there would close
        /// every story the player ever saved next to.
        /// </para>
        /// <para>
        /// <b>The forward boundary exists because the clock can also go backwards.</b> A save rewound
        /// — an older sidecar loaded against a newer city, a restored backup, a hand-edited date —
        /// keeps its <see cref="PoliticalState.LiveStories"/>, and those stories are dated in a future
        /// the current clock will take centuries to reach. Nothing else reaps them: they are not
        /// stranded, so the strict backward test correctly skips them, and they are not due, so
        /// <see cref="ResolveDue"/> skips them too. They sit in the live list forever while
        /// <see cref="StoryEffects.ForActive"/> re-requests their active effects every single month
        /// until the clock catches up. That is an unbounded effect leak, and the earlier version of
        /// this pass had no rule for it because it was written assuming time only moves forward.
        /// </para>
        /// <para>
        /// <b>Two statements fix that boundary, and the ordinary case sits exactly on it.</b> First:
        /// a story is opened by a tick, and a tick only ever runs at a date the clock has reached, so
        /// <c>OpenedDate &lt;= today</c> must hold for every live story. Second: a story's life is
        /// fixed at draft to <c>max(1, cycleMonths - 1)</c> months by
        /// <c>StoryAssembler.NewStory</c>, so the furthest ahead a live story can be due is that many
        /// months past the month it opened — and since it opened at or before today,
        /// <c>ResolvesDate &lt;= today + max(1, cycleMonths - 1)</c>. A story drafted on this very
        /// tick is due exactly <c>max(1, cycleMonths - 1)</c> months out and therefore sits on the
        /// bound rather than past it, which is what makes the comparison strict: at the shipped
        /// cadence that is a story due next month, which is the single most ordinary thing in the
        /// system and must never be caught.
        /// </para>
        /// <para>
        /// <b>Both statements are checked, because neither implies the other.</b> A rewind of one
        /// month leaves a story whose <c>OpenedDate</c> is next month — caught by the first — while
        /// its lead may still be inside a wide cadence. A record whose two dates disagree, from a
        /// hand-edited sidecar, is caught by the second. Lowering <c>cycleMonths</c> mid-save can trip
        /// the second on a legitimately drafted story, and that false positive is accepted knowingly:
        /// the outcome is <see cref="StoryOutcome.Abandoned"/>, which pays out in neither direction,
        /// so being wrong costs the player a story rather than any power. Reading the span off the
        /// story instead would make the test vacuous, since <c>ResolvesDate</c> is derived from
        /// <c>OpenedDate</c> and the two would always agree with each other.
        /// </para>
        /// <para>
        /// <b>Runs on every tick, not only on the resolve phase.</b> Truncation drops whole months, so
        /// the first tick after a long gap is wherever the catch-up window happened to start — very
        /// often not a resolve phase at all, and a rewind lands wherever the older save left off.
        /// Waiting for a phase would leave a stranded story pending for up to another full cadence and
        /// leak effects for every month of it. It costs nothing to run always: the pass is idempotent
        /// by construction, because a story it reaps leaves
        /// <see cref="PoliticalState.LiveStories"/> in the same breath.
        /// </para>
        /// <para>
        /// <b>A swept story is scored only from evidence it already carries.</b>
        /// <see cref="Story.ResolutionEvidence"/> is a reading taken at a month the story was still
        /// live for, so replaying it is honest; measuring the city now would score a 2005 story
        /// against 2031, which is the same nonsense replay is suspended for. With no such evidence
        /// there is nothing to have a verdict about and the story is
        /// <see cref="StoryOutcome.Abandoned"/> — which pays out in neither direction, so the engine
        /// does not bill the player for its own truncation. A forward-dated story is never scored at
        /// all, whatever it carries: its evidence, if any, was recorded in a month this clock has not
        /// reached.
        /// </para>
        /// <para>
        /// <b>Nothing here is silent.</b> Every reaped story writes a warning naming its id, its
        /// dates and which rule caught it. The rewind case is the one that most needs saying out loud:
        /// while it went unhandled the log said nothing at all during the stall, which is what made it
        /// expensive to find.
        /// </para>
        /// </remarks>
        private static void Sweep(PoliticalState state, IReadOnlyList<CivicEvent> catalog,
                                  StoryReadContext context, EngineTuning tuning, SimDate today,
                                  List<Verdict> verdicts, List<string> warnings)
        {
            List<Story> live = state.LiveStories;
            int maxLead = MaxLead(tuning.Stories);

            for (int i = 0; i < live.Count; i++)
            {
                Story story = live[i];
                if (story == null || story.Outcome != StoryOutcome.Pending) continue;

                int lead = story.ResolvesDate.TotalMonths - today.TotalMonths;

                // Forward-dated: evidence of a rewound clock rather than of a story in flight.
                if (story.OpenedDate.TotalMonths > today.TotalMonths || lead > maxLead)
                {
                    Abandon(story, today);
                    verdicts.Add(new Verdict(story, NoOutcomes));

                    warnings.Add("story " + story.Id + " opened at " + story.OpenedDate + " and is due at "
                                 + story.ResolvesDate + ", which no tick at " + today
                                 + " could have drafted; the clock has been moved back. Abandoned at "
                                 + today + " rather than left live, where it would have re-requested"
                                 + " its active effects every month until the clock caught up.");
                    continue;
                }

                // In flight, or due this month and so ResolveDue's. Only the past is stranded.
                if (lead >= 0) continue;

                bool hasEvidence = story.ResolutionEvidence != null && story.ResolutionEvidence.Count > 0;

                if (hasEvidence)
                {
                    verdicts.Add(Score(story, catalog, context, tuning, today));
                    warnings.Add("story " + story.Id + " was stranded at " + story.ResolvesDate
                                 + " and scored at " + today + " from its own recorded evidence.");
                    continue;
                }

                Abandon(story, today);
                verdicts.Add(new Verdict(story, NoOutcomes));

                warnings.Add("story " + story.Id + " was due at " + story.ResolvesDate
                             + " and that month never ran; abandoned at " + today
                             + " rather than scored against a city that has since moved.");
            }
        }

        /// <summary>
        /// The furthest ahead of today a legitimately drafted story can be due: the life
        /// <c>StoryAssembler.NewStory</c> grants one, floor included.
        /// </summary>
        /// <remarks>
        /// <b>The floor is copied deliberately and is not a second definition of a story's life.</b>
        /// The drafter clamps <c>cycleMonths - 1</c> up to 1, so at a hand-edited cadence of 1 a story
        /// still lives a month; a bound here that did not would declare every story that cadence ever
        /// drafted a rewind and abandon the lot on the next tick. This is the one place the cadence
        /// arithmetic is repeated, it is repeated to match rather than to decide, and every other
        /// question about when a story is due is answered by reading
        /// <see cref="Story.ResolvesDate"/> as the drafter stamped it.
        /// </remarks>
        private static int MaxLead(StoriesTuning stories)
        {
            int months = stories.CycleMonths - 1;
            return months < 1 ? 1 : months;
        }

        // --- the ordinary resolution pass ------------------------------------------------------

        /// <summary>
        /// Resolves the stories that are due this month, plus any the player asked to resolve early.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <b>Gated on the resolve phase and on the phase's own watermark.</b> The watermark is
        /// compared strictly greater, exactly as wave 0 compares <c>LastCompletedTickMonth</c>, so a
        /// month re-entered after a reload does not reach a second verdict — and a story that has
        /// already resolved is out of <see cref="PoliticalState.LiveStories"/> anyway, which is the
        /// per-story half of the same guarantee.
        /// </para>
        /// <para>
        /// <b>Due means due, and the story is the authority on when that is.</b> The test is against
        /// <see cref="Story.ResolvesDate"/> as the drafter stamped it, never against the cadence: the
        /// draft-to-resolution span is <c>cycleMonths - 1</c> and the two differ by one, so any
        /// arithmetic here would be a second, drifting definition of a story's life.
        /// </para>
        /// <para>
        /// <b>An early resolve is honoured on whatever tick follows the request</b>, phase or no
        /// phase — a "resolve now" that waited for the cadence would not be one. It reads the evidence
        /// the command recorded rather than re-measuring, which is what keeps a command whose timing
        /// is exogenous deterministic on replay.
        /// </para>
        /// </remarks>
        private static void ResolveDue(StoryCycleInput input, PoliticalState state,
                                       IReadOnlyList<CivicEvent> catalog, StoryReadContext context,
                                       EngineTuning tuning, SimDate today, List<Verdict> verdicts)
        {
            bool phase = input.IsStoryResolve && today.TotalMonths > state.LastStoryResolveMonth;
            List<Story> live = state.LiveStories;

            for (int i = 0; i < live.Count; i++)
            {
                Story story = live[i];
                if (story == null || story.Outcome != StoryOutcome.Pending) continue;

                bool due = phase && story.ResolvesDate.TotalMonths == today.TotalMonths;
                if (!due && !story.ResolveEarlyRequested) continue;

                verdicts.Add(Score(story, catalog, context, tuning, today));
            }

            // The watermark records that the phase ran, not that it found something: a resolve month
            // with nothing due is still a month that has been resolved, and stamping it only on a
            // non-empty pass would leave the guard open on every quiet cycle — which is the half of
            // an idempotence guard that is easy to write and does nothing.
            if (phase) state.LastStoryResolveMonth = today.TotalMonths;
        }

        /// <summary>
        /// Scores one story and stamps the verdict onto it — the persisted record, written before any
        /// effect is emitted.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The per-slot outcomes are written back onto the slots because they are state: the UI reads
        /// them, <see cref="StoryPressure"/> reads them to choose between an event's success and
        /// failure pressure, and a reload has no other way to learn how a slot came out.
        /// <see cref="StoryResolutionResult.SlotOutcomes"/> is index-aligned with the story's own slot
        /// list, so the copy is positional.
        /// </para>
        /// <para>
        /// The evidence is written back for the same reason it is preferred on the way in: a verdict
        /// that recorded what it was reached on can be replayed, and one that did not sends the replay
        /// back to a city that has since moved.
        /// </para>
        /// </remarks>
        private static Verdict Score(Story story, IReadOnlyList<CivicEvent> catalog,
                                     StoryReadContext context, EngineTuning tuning, SimDate today)
        {
            StoryResolutionResult scored = StoryResolution.Resolve(story, catalog, Evidence(story, context),
                                                                   tuning);

            List<StorySlot> slots = story.Slots ?? new List<StorySlot>();
            for (int i = 0; i < slots.Count && i < scored.SlotOutcomes.Count; i++)
            {
                StorySlot slot = slots[i];
                if (slot != null) slot.SlotOutcome = scored.SlotOutcomes[i];
            }

            story.ResolutionEvidence = scored.Evidence;
            story.Outcome = scored.Outcome;
            story.ResolvedMonth = today.TotalMonths;

            return new Verdict(story, scored.SlotOutcomes);
        }

        /// <summary>
        /// Closes a story that has nothing to be scored on. Pays out in neither direction.
        /// </summary>
        /// <remarks>
        /// The slots keep <see cref="SlotOutcome.Pending"/> rather than being written to
        /// <see cref="SlotOutcome.NotMet"/>, and that is the point: not-met is a verdict about the
        /// player, and there was no verdict. It is not <see cref="SlotOutcome.Unmeasurable"/> either —
        /// that word means the engine could not read the city, and here the engine never looked.
        /// </remarks>
        private static void Abandon(Story story, SimDate today)
        {
            story.Outcome = StoryOutcome.Abandoned;
            story.ResolvedMonth = today.TotalMonths;
        }

        /// <summary>
        /// The context a story is scored against: its own recorded evidence when it carries any, and
        /// the month's live reading otherwise.
        /// </summary>
        /// <remarks>
        /// A fresh <see cref="StoryReadContext"/> rather than a mutation of the caller's, because one
        /// tick scores several stories and a shared context edited per story would let the first
        /// story's evidence answer the second's checks.
        /// </remarks>
        private static StoryReadContext Evidence(Story story, StoryReadContext context)
        {
            List<MetricReading>? recorded = story.ResolutionEvidence;
            if (recorded == null || recorded.Count == 0) return context;

            return new StoryReadContext
            {
                Today = context.Today,
                History = context.History,
                RecordedEvidence = recorded
            };
        }

        // --- retirement and the archive ---------------------------------------------------------

        /// <summary>
        /// Moves everything that reached a verdict out of the live list and into the archive, then
        /// trims the archive to <c>stories.archiveRetention</c>.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The sort key is <c>(ResolvedMonth descending, Id ordinal)</c>, which
        /// <see cref="PoliticalState.StoryArchive"/> documents, so the trim drops the oldest and the
        /// order is a total one rather than "whatever resolved first".
        /// </para>
        /// <para>
        /// <b>Nothing depends on the bound.</b> The archive is a record for the player, not an index:
        /// re-use is gated on the duration held in <see cref="EventPoolEntry.LastDraftedMonth"/>, for
        /// the arithmetic set out there. A retention of zero or less is read as unbounded rather than
        /// as "keep nothing", so a hand-edited file loses history rather than gaining a rule.
        /// </para>
        /// <para>
        /// Retirement runs <b>before</b> the draft, so an event whose story has just closed is no
        /// longer being told and the assembler sees the pool it will actually draw from. The cooldown
        /// still holds it back; that is a duration, and this is a fact about what is live.
        /// </para>
        /// </remarks>
        private static void Retire(PoliticalState state, StoriesTuning stories, List<Verdict> verdicts)
        {
            if (verdicts.Count == 0) return;

            var stillLive = new List<Story>();
            List<Story> live = state.LiveStories;

            for (int i = 0; i < live.Count; i++)
            {
                Story story = live[i];
                if (story == null) continue;
                if (story.Outcome == StoryOutcome.Pending) { stillLive.Add(story); continue; }

                state.StoryArchive.Add(story);
            }

            state.LiveStories = stillLive;

            state.StoryArchive.Sort(CompareArchived);

            int retention = stories.ArchiveRetention;
            if (retention > 0 && state.StoryArchive.Count > retention)
            {
                state.StoryArchive.RemoveRange(retention, state.StoryArchive.Count - retention);
            }
        }

        // --- the draft ---------------------------------------------------------------------------

        /// <summary>
        /// Draws this cycle's stories, if this is the draft phase and the phase has not already run.
        /// </summary>
        /// <remarks>
        /// The watermark is compared strictly greater, like every other guard here. It matters more on
        /// this side than on the resolution side: story ids are derived from the drafting month, so a
        /// second draft in one month would open a second set of stories under ids the first set
        /// already holds — a duplicate in <see cref="PoliticalState.LiveStories"/> rather than a
        /// harmless repeat.
        /// </remarks>
        private static void Draft(StoryCycleInput input, PoliticalState state,
                                  IReadOnlyList<CivicEvent> catalog, StoryReadContext context,
                                  EngineTuning tuning, SimDate today, StoryCycleResult result)
        {
            if (!input.IsStoryDraft) return;
            if (today.TotalMonths <= state.LastStoryDraftMonth) return;

            StoryDraftResult drafted =
                StoryAssembler.Draft(state, catalog, context, input.SaveGuid, today, tuning);

            state.EventPool = drafted.UpdatedPool;
            state.LastStoryDraftMonth = today.TotalMonths;

            for (int i = 0; i < drafted.DraftedStories.Count; i++)
            {
                Story story = drafted.DraftedStories[i];
                if (story != null) state.LiveStories.Add(story);
            }

            // A degradation is never an error — a promoted minor or a short story is a valid cycle —
            // but it is the only account of why a cycle came out the shape it did, so it travels as a
            // warning because that is the one channel the tick logs.
            for (int i = 0; i < drafted.Degradations.Count; i++)
            {
                result.Warnings.Add("story draft at " + today + ": " + drafted.Degradations[i]);
            }

            result.DraftedStories = new List<Story>(drafted.DraftedStories);
            result.DraftedStories.Sort(CompareStoriesById);
        }

        // --- the currency -------------------------------------------------------------------------

        /// <summary>
        /// Accrues the month, pays out this tick's verdicts, and reports the net movement.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <b>The per-save switch is guarded here as a stopgap, and this call site is not where it
        /// belongs.</b> <c>power.enabled</c> is read inside <see cref="PoliticalPower"/> on every
        /// figure it produces, so every entrance to the currency honours it automatically. The
        /// per-save <see cref="AgoraSettings.PoliticalPowerEnabled"/> cannot be, because
        /// <see cref="PowerLedger"/> is handed <see cref="EngineTuning"/> and never
        /// <see cref="AgoraSettings"/> — so the seam is deficient, and every caller has to remember a
        /// guard the seam should have enforced once. Non-negotiable #10 puts the player's answer above
        /// the tuning default, so the check has to happen somewhere, and this is it until the seam
        /// takes the setting.
        /// </para>
        /// <para>
        /// <b>The other entrances each carry their own copy of this guard, which is the smell.</b> The
        /// command surface raises <c>CommandOutcome.PowerDisabled</c> from its own check, and wave 6's
        /// cost quote will need a third. The fix is to pass the setting into <see cref="PowerLedger"/>
        /// and delete all three; it is deferred to wave 5/6 by owner decision rather than overlooked,
        /// because no UI reads the quote yet and churning a merged seam this late buys nothing. When
        /// that lands, this guard goes with it — it is defence in depth, not the home of the rule.
        /// </para>
        /// <para>
        /// <b>Accrual first, then awards in story-id order, then nothing.</b> The order is declared
        /// rather than incidental because each call returns a new state built from the last, so a
        /// different order is a different ledger — and the ledger is what explains a balance after the
        /// fact.
        /// </para>
        /// <para>
        /// <b>An abandoned story is not paid out.</b> Its slots never scored, so
        /// <see cref="PoliticalPower.AwardFor"/> would return zero for every one of them and the call
        /// would be a no-op — but skipping it is the honest statement of invariant 4 rather than a
        /// reliance on arithmetic that happens to come out to nothing.
        /// </para>
        /// <para>
        /// <see cref="StoryCycleResult.PowerDelta"/> is measured as the difference between the
        /// balances rather than accumulated from the individual movements, so it cannot disagree with
        /// the ledger it is describing.
        /// </para>
        /// </remarks>
        private static void MovePower(StoryCycleInput input, PoliticalState state,
                                      IReadOnlyList<CivicEvent> catalog, EngineTuning tuning,
                                      SimDate today, List<Story> scored, StoryCycleResult result)
        {
            AgoraSettings settings = state.Settings ?? new AgoraSettings();
            if (!settings.PoliticalPowerEnabled) return;

            if (state.Power == null) state.Power = new PoliticalPowerState();
            int before = state.Power.Balance;

            state.Power = PowerLedger.Accrue(state.Power, input.GoverningVoteShare, today, tuning);

            for (int i = 0; i < scored.Count; i++)
            {
                state.Power = PowerLedger.AwardForStory(state.Power, scored[i], catalog, today, tuning);
            }

            if (state.Power == null) state.Power = new PoliticalPowerState();
            result.PowerDelta = state.Power.Balance - before;
        }

        /// <summary>
        /// Adds the debt penalty, when a negative balance has earned one.
        /// </summary>
        /// <remarks>
        /// Last in the request list, matching the (active, resolution, debt) order
        /// <see cref="StoryCycleResult.EffectRequests"/> declares. The request is the shipped capped
        /// palette entry and arrives already resolved; it is not clamped a second time here, because a
        /// cap enforced in two places is a cap that will eventually disagree with itself.
        /// <para>
        /// The per-save switch is guarded again, and it is the second of this file's two copies of a
        /// check the <see cref="PowerLedger"/> seam should own — see <see cref="MovePower"/> for why
        /// there are copies at all. Both go when the seam takes the setting.
        /// </para>
        /// </remarks>
        private static void AppendDebtPenalty(PoliticalState state, AgoraSettings settings,
                                              EngineTuning tuning, StoryCycleResult result)
        {
            if (!settings.PoliticalPowerEnabled) return;

            EffectRequest request;
            if (PowerLedger.TryDebtPenalty(state.Power, tuning, out request))
            {
                result.EffectRequests.Add(request);
            }
        }

        // --- shared shapes -------------------------------------------------------------------------

        private static readonly SlotOutcome[] NoOutcomes = new SlotOutcome[0];

        /// <summary>
        /// The stories from this tick that reached a real verdict, sorted by <c>Id</c> ordinal.
        /// </summary>
        /// <remarks>
        /// <b>Abandoned is excluded, and one rule covers both ways of reaching it</b> — the sweep, and
        /// an ordinary resolution every one of whose slots was unreadable. Neither has a verdict, so
        /// neither pays power and neither credits or blames the government: a story nobody could score
        /// is not one the mayor delivered, and it is not one they failed either.
        /// </remarks>
        private static List<Story> Scored(List<Verdict> verdicts)
        {
            var scored = new List<Story>();
            for (int i = 0; i < verdicts.Count; i++)
            {
                Story story = verdicts[i].Story;
                if (story.Outcome == StoryOutcome.Success || story.Outcome == StoryOutcome.Failure)
                {
                    scored.Add(story);
                }
            }

            scored.Sort(CompareStoriesById);
            return scored;
        }

        /// <summary>
        /// Everything retired this tick, abandoned stories included, sorted by <c>Id</c> ordinal —
        /// which is what <see cref="StoryCycleResult.ResolvedStories"/> promises its reader.
        /// </summary>
        private static List<Story> Retired(List<Verdict> verdicts)
        {
            var retired = new List<Story>();
            for (int i = 0; i < verdicts.Count; i++) retired.Add(verdicts[i].Story);

            retired.Sort(CompareStoriesById);
            return retired;
        }

        private static string Suspended(SimDate today, string phase) =>
            "story " + phase + " suspended at " + today
            + ": the month is being replayed, so no story is drafted, scored or paid for.";

        private static int CompareStoriesById(Story a, Story b) => string.CompareOrdinal(a.Id, b.Id);

        private static int CompareVerdictsById(Verdict a, Verdict b) =>
            string.CompareOrdinal(a.Story.Id, b.Story.Id);

        /// <summary>
        /// The archive's documented order: newest resolution first, ties broken by ordinal id.
        /// </summary>
        private static int CompareArchived(Story a, Story b)
        {
            int byMonth = b.ResolvedMonth.CompareTo(a.ResolvedMonth);
            return byMonth != 0 ? byMonth : string.CompareOrdinal(a.Id, b.Id);
        }

        /// <summary>
        /// A story and the per-slot outcomes its verdict was built from, index-aligned with the
        /// story's own slot list. Empty for an abandoned sweep, which had no outcomes to align.
        /// </summary>
        private sealed class Verdict
        {
            public Verdict(Story story, IReadOnlyList<SlotOutcome> slotOutcomes)
            {
                Story = story;
                SlotOutcomes = slotOutcomes;
            }

            public Story Story { get; }

            public IReadOnlyList<SlotOutcome> SlotOutcomes { get; }
        }
    }
}
