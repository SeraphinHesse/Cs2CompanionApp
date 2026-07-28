using System;

namespace Agora.Core.Determinism
{
    /// <summary>
    /// A seeded pseudo-random generator with a fixed, documented algorithm (xoshiro256**),
    /// so that a given seed produces the same sequence on every machine, runtime and .NET version.
    ///
    /// <para>
    /// <see cref="System.Random"/> is deliberately not used anywhere in Agora: its algorithm is an
    /// implementation detail that has already changed once between .NET Framework and .NET Core, and
    /// a parameterless instance seeds itself from the clock. Either property would silently break
    /// non-negotiable #3 (determinism).
    /// </para>
    ///
    /// <para>Obtain instances through <see cref="SeedStreams"/>, never by constructing a raw seed.</para>
    /// </summary>
    public sealed class DeterministicRng
    {
        private ulong _s0, _s1, _s2, _s3;

        /// <summary>The seed this generator was created from. Useful in test failure messages.</summary>
        public ulong Seed { get; }

        internal DeterministicRng(ulong seed)
        {
            Seed = seed;

            // SplitMix64 expands a single 64-bit seed into xoshiro's 256-bit state. Seeding all four
            // words from one value directly would leave low-entropy states that correlate early draws.
            ulong z = seed;
            _s0 = SplitMix64(ref z);
            _s1 = SplitMix64(ref z);
            _s2 = SplitMix64(ref z);
            _s3 = SplitMix64(ref z);
        }

        private static ulong SplitMix64(ref ulong x)
        {
            unchecked
            {
                x += 0x9E3779B97F4A7C15UL;
                ulong z = x;
                z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
                z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
                return z ^ (z >> 31);
            }
        }

        private static ulong RotateLeft(ulong x, int k) => (x << k) | (x >> (64 - k));

        /// <summary>Next raw 64-bit value. All other draws derive from this.</summary>
        public ulong NextULong()
        {
            unchecked
            {
                ulong result = RotateLeft(_s1 * 5UL, 7) * 9UL;
                ulong t = _s1 << 17;

                _s2 ^= _s0;
                _s3 ^= _s1;
                _s1 ^= _s2;
                _s0 ^= _s3;
                _s2 ^= t;
                _s3 = RotateLeft(_s3, 45);

                return result;
            }
        }

        /// <summary>Uniform double in [0, 1). Uses the top 53 bits, matching IEEE-754 mantissa width.</summary>
        public double NextDouble() => (NextULong() >> 11) * (1.0 / 9007199254740992.0);

        /// <summary>
        /// Uniform integer in [minInclusive, maxExclusive). Rejection-sampled, so the distribution is
        /// exactly uniform rather than modulo-biased — bias here would skew close elections.
        /// </summary>
        public int NextInt(int minInclusive, int maxExclusive)
        {
            if (maxExclusive <= minInclusive)
                throw new ArgumentOutOfRangeException(nameof(maxExclusive),
                    $"maxExclusive ({maxExclusive}) must exceed minInclusive ({minInclusive}).");

            ulong range = (ulong)((long)maxExclusive - minInclusive);
            ulong limit = ulong.MaxValue - (ulong.MaxValue % range);

            ulong draw;
            do
            {
                draw = NextULong();
            } while (draw >= limit);

            return (int)(minInclusive + (long)(draw % range));
        }

        /// <summary>True with the given probability. Values outside [0,1] clamp rather than throw.</summary>
        public bool NextBool(double probability) =>
            probability <= 0.0 ? false
            : probability >= 1.0 ? true
            : NextDouble() < probability;

        /// <summary>
        /// Standard-normal draw via the polar Box–Muller method. Used for poll error and affinity
        /// noise. The rejection loop keeps this deterministic — it consumes a variable number of
        /// draws, but always the same number for a given seed.
        /// </summary>
        public double NextGaussian()
        {
            double u, v, s;
            do
            {
                u = NextDouble() * 2.0 - 1.0;
                v = NextDouble() * 2.0 - 1.0;
                s = u * u + v * v;
            } while (s >= 1.0 || s == 0.0);

            return u * Math.Sqrt(-2.0 * Math.Log(s) / s);
        }

        /// <summary>
        /// Deterministic in-place Fisher–Yates shuffle.
        /// </summary>
        /// <remarks>
        /// Callers must pass a list whose order is already deterministic. Shuffling the output of a
        /// <see cref="System.Collections.Generic.Dictionary{TKey,TValue}"/> enumeration produces a
        /// stable-looking result that is not actually reproducible — sort first.
        /// </remarks>
        public void Shuffle<T>(System.Collections.Generic.IList<T> items)
        {
            if (items == null) throw new ArgumentNullException(nameof(items));

            for (int i = items.Count - 1; i > 0; i--)
            {
                int j = NextInt(0, i + 1);
                (items[i], items[j]) = (items[j], items[i]);
            }
        }
    }
}
