using System;
using System.Globalization;
using System.IO;
using System.Text;

namespace Agora.Mod.Persistence
{
    /// <summary>
    /// Where Agora's save guid comes from.
    ///
    /// <para>
    /// <c>politicsmodplan.md</c> §5 is explicit: <i>"Agora owns the save identity. Do not assume the
    /// engine exposes a stable save GUID. Instead write a GUID into the save itself via the
    /// serialization hooks and key the sidecar on that. It survives renames and copies, it cannot
    /// collide with a filename, and it retires risk §13.1 outright."</i>
    /// </para>
    ///
    /// <para>
    /// <b>Why this is derived rather than drawn.</b> The obvious way to mint an identity is
    /// <c>Guid.NewGuid()</c>, and it is banned repo-wide (non-negotiable #2). The ban is usually
    /// justified by replay determinism, which does not quite apply here — the guid is minted once and
    /// then persisted, so politics would still be identical on every subsequent launch. But a
    /// non-derivable identity is genuinely worse in one way that does matter: nothing can reproduce
    /// it, so a save whose Agora block is lost can never be reunited with its sidecar directory.
    /// Deriving from stable facts about the save means that identity can be recomputed.
    /// </para>
    ///
    /// <para>
    /// Uniqueness comes from a deterministic de-collision walk rather than from entropy: the
    /// candidate directory is claimed at mint time, and a candidate whose directory already exists
    /// belongs to a different save, so the attempt counter advances and the guid is re-derived. Two
    /// cities built from the same map with the same name therefore get different identities, without
    /// a single random bit anywhere.
    /// </para>
    ///
    /// <para>
    /// A save copied on disk keeps its guid and therefore shares a sidecar with its original. That is
    /// §5's stated intent — the identity travels with the save through renames and copies — and is
    /// not a defect.
    /// </para>
    /// </summary>
    public static class SaveIdentity
    {
        /// <summary>
        /// How far the de-collision walk goes before giving up. Reaching this needs 64 distinct saves
        /// that all hash to the same starting point; it means something is badly wrong, not that a
        /// player got unlucky.
        /// </summary>
        public const int MaxDerivationAttempts = 64;

        private const ulong FnvOffsetBasis = 14695981039346656037UL;
        private const ulong FnvPrime = 1099511628211UL;

        /// <summary>
        /// Derives a candidate guid from the save's own stable facts.
        /// </summary>
        /// <param name="instigatorX">Word 0 of the loading context's instigator hash (the map or save asset).</param>
        /// <param name="instigatorY">Word 1.</param>
        /// <param name="instigatorZ">Word 2.</param>
        /// <param name="instigatorW">Word 3.</param>
        /// <param name="cityName">The city's name at mint time. May be null or empty.</param>
        /// <param name="attempt">De-collision counter; 0 on the first try.</param>
        /// <remarks>
        /// FNV-1a, hand-rolled for the same reason <c>SeedStreams</c> hand-rolls it:
        /// <see cref="string.GetHashCode"/> is randomised per process on modern runtimes, so a guid
        /// derived through it would differ between launches and the sidecar would be orphaned every
        /// time the game restarted.
        /// </remarks>
        public static Guid Derive(uint instigatorX, uint instigatorY, uint instigatorZ, uint instigatorW,
                                  string cityName, int attempt)
        {
            // Two independent 64-bit hashes over the same canonical input, distinguished by their
            // domain-separation prefixes, give the 128 bits a guid needs.
            ulong low = HashWith("agora.save.identity.lo", instigatorX, instigatorY, instigatorZ, instigatorW,
                                 cityName, attempt);
            ulong high = HashWith("agora.save.identity.hi", instigatorX, instigatorY, instigatorZ, instigatorW,
                                  cityName, attempt);

            byte[] bytes = new byte[16];
            WriteUInt64(bytes, 0, low);
            WriteUInt64(bytes, 8, high);

            // Stamp RFC 4122 version 5 (name-based) and the standard variant, so the value is a
            // well-formed UUID rather than 16 arbitrary bytes wearing a guid's clothes.
            bytes[7] = (byte)((bytes[7] & 0x0F) | 0x50);
            bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80);

            return new Guid(bytes);
        }

        private static ulong HashWith(string domain, uint x, uint y, uint z, uint w, string cityName, int attempt)
        {
            unchecked
            {
                ulong hash = FnvOffsetBasis;

                hash = MixString(hash, domain);
                hash = MixUInt32(hash, x);
                hash = MixUInt32(hash, y);
                hash = MixUInt32(hash, z);
                hash = MixUInt32(hash, w);

                // Normalised deliberately: the same city renamed between two mints of the same map
                // should not be treated as a different save just because of casing or padding, and
                // ToUpperInvariant avoids the Turkish dotted-i trap that ToLowerInvariant has.
                string name = cityName == null ? string.Empty : cityName.Trim().ToUpperInvariant();
                hash = MixString(hash, name);

                hash = MixUInt32(hash, (uint)attempt);

                return hash;
            }
        }

        private static ulong MixString(ulong hash, string value)
        {
            unchecked
            {
                byte[] bytes = new UTF8Encoding(false).GetBytes(value ?? string.Empty);
                for (int i = 0; i < bytes.Length; i++)
                {
                    hash = (hash ^ bytes[i]) * FnvPrime;
                }

                // A length terminator, so ("ab", "c") and ("a", "bc") cannot hash alike.
                hash = (hash ^ 0xFF) * FnvPrime;
                return hash;
            }
        }

        private static ulong MixUInt32(ulong hash, uint value)
        {
            unchecked
            {
                for (int shift = 0; shift < 32; shift += 8)
                {
                    hash = (hash ^ (byte)(value >> shift)) * FnvPrime;
                }

                return hash;
            }
        }

        private static void WriteUInt64(byte[] target, int offset, ulong value)
        {
            unchecked
            {
                for (int i = 0; i < 8; i++)
                {
                    target[offset + i] = (byte)(value >> (i * 8));
                }
            }
        }

        /// <summary>
        /// Derives a guid and claims its sidecar directory, stepping the attempt counter past any
        /// candidate whose directory already belongs to another save.
        /// </summary>
        /// <param name="root">The sidecar root, from <see cref="SidecarPaths.Root"/>.</param>
        /// <param name="explanation">A log-ready sentence describing what was minted and why.</param>
        /// <remarks>
        /// Claiming the directory is what makes the walk terminate correctly: minting only ever
        /// happens for a save that has no identity yet, so an existing directory can only belong to
        /// somebody else.
        /// </remarks>
        public static Guid Mint(string root, uint instigatorX, uint instigatorY, uint instigatorZ,
                                uint instigatorW, string cityName, out string explanation)
        {
            Guid first = Derive(instigatorX, instigatorY, instigatorZ, instigatorW, cityName, 0);

            for (int attempt = 0; attempt < MaxDerivationAttempts; attempt++)
            {
                Guid candidate = attempt == 0
                    ? first
                    : Derive(instigatorX, instigatorY, instigatorZ, instigatorW, cityName, attempt);

                string directory = SidecarPaths.SaveDirectory(root, candidate);

                try
                {
                    if (Directory.Exists(directory)) continue;

                    Directory.CreateDirectory(directory);
                }
                catch (Exception ex)
                {
                    // The identity is still usable — it goes into the save either way, and the
                    // directory gets created on the first write. Say so rather than failing a load.
                    explanation = "Minted save identity " + SidecarPaths.FormatGuid(candidate) +
                                  " but could not claim its sidecar directory: " + ex.Message;
                    return candidate;
                }

                explanation = "Minted save identity " + SidecarPaths.FormatGuid(candidate) +
                              (attempt == 0
                                  ? "."
                                  : " after " + attempt.ToString(CultureInfo.InvariantCulture) +
                                    " directory collision(s).");
                return candidate;
            }

            explanation = "Minted save identity " + SidecarPaths.FormatGuid(first) + " after exhausting " +
                          MaxDerivationAttempts.ToString(CultureInfo.InvariantCulture) +
                          " de-collision attempts; this save may share a sidecar directory with another.";
            return first;
        }
    }
}
