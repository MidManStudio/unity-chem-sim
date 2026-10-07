// ============================================================================
// NOTICE: Full documentation, design decisions, and fix history for this file
// live in docs/com.midmanstudio.nuclear.md, section "Runtime/Core/Nuclide.cs"
// ============================================================================
using System;
using System.Text;

namespace MidManStudio.Nuclear
{
    /// <summary>
    /// A nuclide: a count of protons and a count of neutrons. The neutron is
    /// <c>Z = 0, N = 1</c>. Crosses the native boundary as a single 32-bit key.
    /// </summary>
    public readonly struct Nuclide : IEquatable<Nuclide>, IComparable<Nuclide>
    {
        /// <summary>The key value that means "no nuclide" in native fields that can be empty.</summary>
        public const uint NoKey = uint.MaxValue;

        /// <summary>The free neutron.</summary>
        public static readonly Nuclide Neutron = new Nuclide(0, 1);
        /// <summary>Hydrogen-1, the proton.</summary>
        public static readonly Nuclide H1 = new Nuclide(1, 0);
        /// <summary>Hydrogen-2, deuterium.</summary>
        public static readonly Nuclide H2 = new Nuclide(1, 1);
        /// <summary>Hydrogen-3, tritium.</summary>
        public static readonly Nuclide H3 = new Nuclide(1, 2);
        /// <summary>Helium-3.</summary>
        public static readonly Nuclide He3 = new Nuclide(2, 1);
        /// <summary>Helium-4, the alpha particle.</summary>
        public static readonly Nuclide He4 = new Nuclide(2, 2);

        /// <summary>Number of protons.</summary>
        public readonly ushort Z;
        /// <summary>Number of neutrons.</summary>
        public readonly ushort N;

        /// <summary>Creates a nuclide from its proton and neutron counts.</summary>
        public Nuclide(int z, int n)
        {
            if (z < 0 || z > ushort.MaxValue)
            {
                throw new ArgumentOutOfRangeException("z");
            }
            if (n < 0 || n > ushort.MaxValue)
            {
                throw new ArgumentOutOfRangeException("n");
            }
            Z = (ushort)z;
            N = (ushort)n;
        }

        /// <summary>Mass number, the number of nucleons.</summary>
        public int A
        {
            get { return Z + N; }
        }

        /// <summary>The 32-bit key: protons in the high 16 bits, neutrons in the low 16.</summary>
        public uint Key
        {
            get { return ((uint)Z << 16) | N; }
        }

        /// <summary>The nuclide with the given key.</summary>
        public static Nuclide FromKey(uint key)
        {
            return new Nuclide((int)(key >> 16), (int)(key & 0xFFFFu));
        }

        /// <summary>The element symbol, such as U, or an empty string when Z is not 1 to 118.</summary>
        public string Symbol
        {
            get { return Z >= 1 && Z <= 118 ? NuclearData.ElementSymbol(Z) : string.Empty; }
        }

        /// <summary>Parses text such as <c>Fe-56</c>, <c>U-235</c> or <c>n</c>.</summary>
        /// <exception cref="FormatException">The text is not a nuclide.</exception>
        public static Nuclide Parse(string text)
        {
            Nuclide result;
            if (!TryParse(text, out result))
            {
                throw new FormatException("'" + text + "' is not a nuclide.");
            }
            return result;
        }

        /// <summary>Parses text such as <c>Fe-56</c>, <c>U-235</c> or <c>n</c>.</summary>
        public static bool TryParse(string text, out Nuclide nuclide)
        {
            nuclide = default(Nuclide);
            if (text == null)
            {
                return false;
            }
            NuclearLibrary.Initialize();
            byte[] bytes = Encoding.UTF8.GetBytes(text);
            uint key;
            int status = NuclearNative.nuc_nuclide_parse(bytes, bytes.Length, out key);
            if (status == (int)NuclearStatus.Invalid)
            {
                return false;
            }
            NuclearNative.Check(status);
            nuclide = FromKey(key);
            return true;
        }

        /// <summary>The name, such as <c>U-235</c>, or <c>n</c> for the neutron.</summary>
        public override string ToString()
        {
            NuclearLibrary.Initialize();
            uint key = Key;
            return NuclearNative.ReadText((b, c) => NuclearNative.nuc_nuclide_name(key, b, c));
        }

        /// <inheritdoc/>
        public bool Equals(Nuclide other)
        {
            return Z == other.Z && N == other.N;
        }

        /// <inheritdoc/>
        public override bool Equals(object obj)
        {
            return obj is Nuclide && Equals((Nuclide)obj);
        }

        /// <inheritdoc/>
        public override int GetHashCode()
        {
            return (int)Key;
        }

        /// <summary>Orders by protons, then neutrons, the order the native tables use.</summary>
        public int CompareTo(Nuclide other)
        {
            return Key.CompareTo(other.Key);
        }

        /// <summary>Equality.</summary>
        public static bool operator ==(Nuclide a, Nuclide b)
        {
            return a.Equals(b);
        }

        /// <summary>Inequality.</summary>
        public static bool operator !=(Nuclide a, Nuclide b)
        {
            return !a.Equals(b);
        }

        /// <summary>Ordering.</summary>
        public static bool operator <(Nuclide a, Nuclide b)
        {
            return a.CompareTo(b) < 0;
        }

        /// <summary>Ordering.</summary>
        public static bool operator >(Nuclide a, Nuclide b)
        {
            return a.CompareTo(b) > 0;
        }

        /// <summary>Ordering.</summary>
        public static bool operator <=(Nuclide a, Nuclide b)
        {
            return a.CompareTo(b) <= 0;
        }

        /// <summary>Ordering.</summary>
        public static bool operator >=(Nuclide a, Nuclide b)
        {
            return a.CompareTo(b) >= 0;
        }
    }
}
