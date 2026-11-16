// ============================================================================
// NOTICE: Full documentation, design decisions, and fix history for this file
// live in docs/com.midmanstudio.proceduralterrain.md, section "Runtime/Core/TerraHash.cs"
// ============================================================================
using System;

namespace MidManStudio.ProceduralTerrain
{
    /// <summary>
    /// Integer hashing and a small seeded generator. Everything the terrain decides "by chance" comes from here, as a
    /// pure function of a seed and integer coordinates, so two chunks that look at the same lattice point agree.
    /// </summary>
    public static class TerraHash
    {
        /// <summary>2^-53, the step that turns the top 53 bits of a hash into a double in [0, 1).</summary>
        public const double Unit53 = 1.0 / 9007199254740992.0;

        /// <summary>The splitmix64 finalizer: a full avalanche of one 64 bit value.</summary>
        public static ulong Mix(ulong z)
        {
            unchecked
            {
                z += 0x9E3779B97F4A7C15UL;
                z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
                z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
                return z ^ (z >> 31);
            }
        }

        /// <summary>Hash of a seed and one integer.</summary>
        public static ulong Hash(ulong seed, long a)
        {
            unchecked
            {
                return Mix(Mix(seed ^ 0x632BE59BD9B4E019UL) ^ (ulong)a);
            }
        }

        /// <summary>Hash of a seed and two integers (a lattice index).</summary>
        public static ulong Hash(ulong seed, long a, long b)
        {
            unchecked
            {
                return Mix(Hash(seed, a) ^ ((ulong)b * 0xE7037ED1A0B428DBUL));
            }
        }

        /// <summary>Hash of a seed and three integers (a lattice index plus a salt such as an octave or a type).</summary>
        public static ulong Hash(ulong seed, long a, long b, long c)
        {
            unchecked
            {
                return Mix(Hash(seed, a, b) ^ ((ulong)c * 0xA0761D6478BD642FUL));
            }
        }

        /// <summary>The top 53 bits of a hash as a double in [0, 1).</summary>
        public static double ToUnit(ulong h)
        {
            return (h >> 11) * Unit53;
        }

        /// <summary>The top 53 bits of a hash as a double in [-1, 1).</summary>
        public static double ToSigned(ulong h)
        {
            return ToUnit(h) * 2.0 - 1.0;
        }

        /// <summary>A stable key for a lattice index, for places that need one number per vertex.</summary>
        public static ulong Key(long a, long b)
        {
            unchecked
            {
                return Mix(((ulong)a << 32) ^ ((ulong)b & 0xFFFFFFFFUL) ^ 0x2545F4914F6CDD1DUL);
            }
        }

        /// <summary>Folds a double seed value into the ulong seed the hash functions take.</summary>
        public static ulong SeedFrom(int seed)
        {
            unchecked
            {
                return Mix((ulong)(uint)seed ^ 0xD6E8FEB86659FD93UL);
            }
        }
    }

    /// <summary>A splitmix64 generator. A struct, so a copy is an independent stream with the same future.</summary>
    public struct TerraRng
    {
        private ulong state;

        public TerraRng(ulong seed)
        {
            state = seed;
        }

        public ulong NextU64()
        {
            unchecked
            {
                state += 0x9E3779B97F4A7C15UL;
                ulong z = state;
                z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
                z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
                return z ^ (z >> 31);
            }
        }

        /// <summary>A double in [0, 1).</summary>
        public double NextDouble()
        {
            return (NextU64() >> 11) * TerraHash.Unit53;
        }

        /// <summary>An int in [0, n). n must be positive.</summary>
        public int NextInt(int n)
        {
            if (n <= 0)
            {
                throw new ArgumentOutOfRangeException("n");
            }
            return (int)(NextU64() % (ulong)n);
        }

        /// <summary>A double in [min, max).</summary>
        public double Range(double min, double max)
        {
            return min + (max - min) * NextDouble();
        }
    }
}
