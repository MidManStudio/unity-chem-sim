// ============================================================================
// NOTICE: Full documentation, design decisions, and fix history for this file
// live in docs/com.midmanstudio.nuclear.md, section "Samples~/NuclearLab/NuclearLabRandom.cs"
// ============================================================================
using System;

namespace MidManStudio.Nuclear.Samples.NuclearLab
{
    /// <summary>
    /// A small seeded random stream (splitmix64) for the fission animation. The same seed gives the
    /// same draws on every platform. The library draws the physics. This stream only picks which of the
    /// library's published outcomes the animation shows, and where the neutrons fly. Has no
    /// UnityEngine references. Not thread safe.
    /// </summary>
    public sealed class NuclearLabRandom
    {
        private ulong state;

        /// <summary>Creates a stream. Every seed, 0 included, gives a full-quality stream.</summary>
        public NuclearLabRandom(ulong seed)
        {
            state = seed;
        }

        /// <summary>The next 64 random bits.</summary>
        public ulong NextBits()
        {
            state += 0x9E3779B97F4A7C15UL;
            ulong z = state;
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            return z ^ (z >> 31);
        }

        /// <summary>The next number from 0 up to, but not including, 1.</summary>
        public double NextDouble()
        {
            return (NextBits() >> 11) * (1.0 / 9007199254740992.0);
        }

        /// <summary>The next number from <paramref name="min"/> up to, but not including, <paramref name="max"/>.</summary>
        public double Range(double min, double max)
        {
            return min + (max - min) * NextDouble();
        }

        /// <summary>
        /// A seed for draw number <paramref name="index"/> of a sequence that starts from
        /// <paramref name="seed"/>. Different indexes give unrelated seeds.
        /// </summary>
        public static ulong Mix(ulong seed, ulong index)
        {
            NuclearLabRandom stream = new NuclearLabRandom(seed ^ (index * 0x9E3779B97F4A7C15UL));
            return stream.NextBits();
        }
    }
}
