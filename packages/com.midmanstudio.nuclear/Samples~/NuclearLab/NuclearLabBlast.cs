// ============================================================================
// NOTICE: Full documentation, design decisions, and fix history for this file
// live in docs/com.midmanstudio.nuclear.md, section "Samples~/NuclearLab/NuclearLabBlast.cs"
// ============================================================================
using System;

namespace MidManStudio.Nuclear.Samples.NuclearLab
{
    /// <summary>
    /// Turns an energy into the size of the flash on the fission stage and into a TNT figure. The size is
    /// logarithmic, because one fission and a kilogram of TNT are almost 18 orders of magnitude apart. Has no
    /// UnityEngine references.
    /// </summary>
    public static class NuclearLabBlast
    {
        /// <summary>Joules in one gram of TNT. A convention, not a measurement.</summary>
        public const double JoulesPerTntGram = 4184.0;

        /// <summary>The energy that draws the smallest flash, in joules. One fission of U-235 is about 3.2e-11 J.</summary>
        public const double MinJoules = 1.0e-11;

        /// <summary>The energy that draws the largest flash, in joules. One kilogram of TNT. The largest pile the lab accepts releases about a quarter of this.</summary>
        public const double MaxJoules = 4.184e6;

        private static readonly string[] TntUnits = { "g", "kg", "t", "kt", "Mt", "Gt" };

        /// <summary>
        /// The flash size from 0 to 1: 0 up to <see cref="MinJoules"/>, 1 from <see cref="MaxJoules"/>,
        /// and in between the share of the decades covered. Anything that is not a positive number gives 0.
        /// </summary>
        public static double Scale(double joules)
        {
            if (!(joules > MinJoules))
            {
                return 0.0;
            }
            if (joules >= MaxJoules)
            {
                return 1.0;
            }
            return Math.Log10(joules / MinJoules) / Math.Log10(MaxJoules / MinJoules);
        }

        /// <summary><see cref="Scale"/> of an energy given in keV.</summary>
        public static double ScaleKev(double kev)
        {
            return Scale(kev * NuclearLabFormat.JoulesPerKev);
        }

        /// <summary>
        /// The TNT equivalent as text, such as "about 640 kg of TNT", using 4184 J per gram. Empty when the
        /// energy is not a positive number.
        /// </summary>
        public static string TntText(double joules)
        {
            if (!(joules > 0.0) || double.IsInfinity(joules))
            {
                return string.Empty;
            }
            double value = joules / JoulesPerTntGram;
            int unit = 0;
            while (value >= 1000.0 && unit < TntUnits.Length - 1)
            {
                value /= 1000.0;
                unit++;
            }
            return "about " + NuclearLabFormat.Number(value) + " " + TntUnits[unit] + " of TNT";
        }
    }
}
