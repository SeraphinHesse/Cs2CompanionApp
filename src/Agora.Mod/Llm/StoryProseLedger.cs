// Compiled into BOTH Agora.Mod and (by <Compile Link>) tests/Agora.Core.Tests: it must stay free of
// every Game.*, Unity.* and Colossal.* type. #nullable disable keeps it warning-clean in the test
// project, which enables nullable, without annotating a file the mod compiles unannotated.
#nullable disable

using System;
using System.Collections.Generic;
using Agora.Core.Contracts;

namespace Agora.Mod.Llm
{
    /// <summary>
    /// Which end of a story a piece of prose belongs to.
    /// </summary>
    public enum StoryProseKind
    {
        /// <summary>The article the card opens with.</summary>
        Opening = 0,

        /// <summary>The piece written when the story resolved.</summary>
        Resolution = 1
    }

    /// <summary>
    /// Holds the prose the player has been shown for each story, and adds the model's prose beside it
    /// rather than over it.
    ///
    /// <para>
    /// <b>The problem this exists for.</b> Two writers produce story prose on completely different
    /// rhythms. The canned pool answers <i>every</i> poll — several times a sim month, from a roster
    /// rebuilt at each month boundary — and it always has an answer. The CLI answers only after a
    /// wake that succeeded, tens of seconds later, and often not at all. So a payload-shaped
    /// "latest wins" rule does not merely risk losing the model's prose: it loses it reliably, on the
    /// very next poll, every time. And a "best wins" rule has the opposite failure — the player opens
    /// a card, reads a headline, and finds a different headline there a minute later with nothing to
    /// explain it.
    /// </para>
    ///
    /// <para>
    /// <b>The rule.</b> One slot per source per story per end, and <b>the first writer into a slot
    /// keeps it</b>. The pool's text is what the card was opened with and it never changes. The
    /// model's text lands in its own slot when it arrives and is shown in addition. Nothing is ever
    /// overwritten, so nothing a player has read can change under them, and nothing the model wrote
    /// can be swept away by a canned poll a second later.
    /// </para>
    ///
    /// <para>
    /// <b>Not persisted, and it does not need to be.</b> Pool prose is a pure function of the story
    /// and the catalog, so it rebuilds identically on the first poll after a load. Model prose
    /// survives in <c>flavor_cache.json</c> and is re-absorbed from the cached document the same way.
    /// A ledger that rebuilt itself differently would be a determinism problem; one that rebuilds
    /// itself identically is just a cache.
    /// </para>
    ///
    /// <para>
    /// <b>Threading.</b> Sim thread only. <c>CollectProse</c> is the sole caller and runs there; the
    /// CLI worker never touches this — it hands over a document, and the sim thread absorbs it.
    /// </para>
    /// </summary>
    public sealed class StoryProseLedger
    {
        private readonly Dictionary<string, StoryProse> _entries =
            new Dictionary<string, StoryProse>(StringComparer.Ordinal);

        /// <summary>How many prose slots are filled. For logging and for tests.</summary>
        public int Count => _entries.Count;

        /// <summary>
        /// Files every story entry on <paramref name="payload"/> that has no slot yet, and returns
        /// how many were newly filled. An entry whose slot is taken is left alone — see the class
        /// remarks; that is the whole rule, not an optimisation.
        /// </summary>
        public int Absorb(FlavorPayload payload)
        {
            if (payload == null) return 0;

            return Absorb(payload.Stories, StoryProseKind.Opening)
                 + Absorb(payload.Resolutions, StoryProseKind.Resolution);
        }

        private int Absorb(List<StoryProse> entries, StoryProseKind kind)
        {
            if (entries == null) return 0;

            int added = 0;
            for (int i = 0; i < entries.Count; i++)
            {
                StoryProse entry = entries[i];
                if (entry == null || string.IsNullOrEmpty(entry.StoryId)) continue;

                // Prose with neither half is not prose. The validator drops malformed entries long
                // before this, but the pool builds its own document without passing through it, and
                // an empty slot claimed here could never be filled by the writer that had something
                // to say - first-write-wins would hold the emptiness forever.
                if (string.IsNullOrEmpty(entry.Headline) && string.IsNullOrEmpty(entry.Article)) continue;

                string key = KeyFor(entry.StoryId, kind, entry.Source);
                if (_entries.ContainsKey(key)) continue;

                _entries[key] = entry;
                added++;
            }

            return added;
        }

        /// <summary>
        /// The prose one writer produced for one end of one story, or null when that writer has not
        /// written it.
        /// </summary>
        public StoryProse Get(string storyId, StoryProseKind kind, ProseSource source)
        {
            if (string.IsNullOrEmpty(storyId)) return null;

            StoryProse found;
            return _entries.TryGetValue(KeyFor(storyId, kind, source), out found) ? found : null;
        }

        /// <summary>
        /// Drops every slot belonging to <paramref name="storyId"/>.
        /// </summary>
        /// <remarks>
        /// Called when a story falls out of the archive. Without it this grows for the life of the
        /// save — slowly (a handful of short strings per cycle) but without bound, and a leak that
        /// takes thirty sim years to matter is still a leak. It also keeps the ledger honest about
        /// what exists: a story id is reachable again only through the archive, so prose that
        /// outlived its story could never be shown and is only taking room.
        /// </remarks>
        public void Forget(string storyId)
        {
            if (string.IsNullOrEmpty(storyId)) return;

            _entries.Remove(KeyFor(storyId, StoryProseKind.Opening, ProseSource.Pool));
            _entries.Remove(KeyFor(storyId, StoryProseKind.Opening, ProseSource.Cli));
            _entries.Remove(KeyFor(storyId, StoryProseKind.Resolution, ProseSource.Pool));
            _entries.Remove(KeyFor(storyId, StoryProseKind.Resolution, ProseSource.Cli));
        }

        /// <summary>
        /// Drops every slot whose story id is not in <paramref name="liveIds"/>.
        /// </summary>
        /// <remarks>
        /// The sweep form of <see cref="Forget"/>, for the caller that knows the whole live set
        /// rather than the one story that left. A null or empty set clears everything, which is
        /// correct: no stories exist, so no story prose can be shown.
        /// </remarks>
        public void RetainOnly(ICollection<string> liveIds)
        {
            var doomed = new List<string>();

            foreach (var pair in _entries)
            {
                string storyId = StoryIdOf(pair.Key);
                if (liveIds == null || !liveIds.Contains(storyId)) doomed.Add(pair.Key);
            }

            for (int i = 0; i < doomed.Count; i++) _entries.Remove(doomed[i]);
        }

        /// <summary>
        /// The key layout puts the story id LAST, so <see cref="StoryIdOf"/> can recover it with a
        /// single index lookup no matter what characters the id contains. Story ids are engine-minted
        /// and would not contain the separator anyway, but recovering a key by splitting on a
        /// character an id might one day carry is the kind of assumption that holds until it does not.
        /// </summary>
        private static string KeyFor(string storyId, StoryProseKind kind, ProseSource source) =>
            (kind == StoryProseKind.Opening ? "o" : "r") +
            (source == ProseSource.Cli ? "c" : "p") + "|" + storyId;

        private static string StoryIdOf(string key) => key.Substring(3);
    }
}
