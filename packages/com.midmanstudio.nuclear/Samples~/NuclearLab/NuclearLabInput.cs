// ============================================================================
// NOTICE: Full documentation, design decisions, and fix history for this file
// live in docs/com.midmanstudio.nuclear.md, section "Samples~/NuclearLab/NuclearLabInput.cs"
// ============================================================================
using System;

namespace MidManStudio.Nuclear.Samples.NuclearLab
{
    /// <summary>
    /// Reads the text fields of the sample. Every method throws a
    /// <see cref="FormatException"/> whose message says which field is wrong
    /// and what a good value looks like, so the panel can show it as is.
    /// </summary>
    public static class NuclearLabInput
    {
        /// <summary>Reads a nuclide name such as U-235, H-2 or n.</summary>
        public static Nuclide ParseNuclide(string text, string field)
        {
            Nuclide nuclide;
            string trimmed = text == null ? null : text.Trim();
            if (string.IsNullOrEmpty(trimmed) || !Nuclide.TryParse(trimmed, out nuclide))
            {
                throw new FormatException(field + " '" + text + "' is not a nuclide. Use a name such as U-235, H-2 or n.");
            }
            return nuclide;
        }

        /// <summary>Reads a whole number of at least <paramref name="minimum"/>.</summary>
        public static ulong ParseCount(string text, string field, ulong minimum)
        {
            ulong value;
            if (!NuclearLabFormat.TryParseCount(text, out value))
            {
                throw new FormatException(field + " must be a whole number such as 1000000 or 1e6.");
            }
            if (value < minimum)
            {
                throw new FormatException(field + " must be at least " + NuclearLabFormat.Count(minimum) + ".");
            }
            return value;
        }

        /// <summary>Reads a number. With <paramref name="positive"/> set, zero and negative values are refused.</summary>
        public static double ParseNumber(string text, string field, bool positive)
        {
            double value;
            if (!NuclearLabFormat.TryParseNumber(text, out value))
            {
                throw new FormatException(field + " must be a number such as 0.0253 or 2e6. Use a dot for decimals.");
            }
            if (positive && !(value > 0.0))
            {
                throw new FormatException(field + " must be greater than zero.");
            }
            if (!positive && value < 0.0)
            {
                throw new FormatException(field + " must not be negative.");
            }
            return value;
        }

        /// <summary>Reads a time step: a positive number of <paramref name="unit"/> from <see cref="NuclearLabFormat.StepUnits"/>, as seconds.</summary>
        public static double ParseStepSeconds(string text, int unit)
        {
            double amount = ParseNumber(text, "Step", true);
            double seconds = amount * NuclearLabFormat.UnitSeconds(unit);
            if (double.IsInfinity(seconds))
            {
                throw new FormatException("Step is too large.");
            }
            return seconds;
        }
    }
}
