// ============================================================================
// NOTICE: Full documentation, design decisions, and fix history for this file
// live in docs/com.midmanstudio.nuclear.md, section "Samples~/NuclearLab/NuclearLabFormat.cs"
// ============================================================================
using System;
using System.Globalization;

namespace MidManStudio.Nuclear.Samples.NuclearLab
{
    /// <summary>
    /// Turns the numbers the library returns into short readable text, and the
    /// text a person types back into numbers. Has no UnityEngine references, so
    /// the same file runs in the .NET test harness.
    /// </summary>
    public static class NuclearLabFormat
    {
        /// <summary>Seconds in a Julian year (365.25 days).</summary>
        public const double SecondsPerYear = 31557600.0;
        /// <summary>Seconds in a day.</summary>
        public const double SecondsPerDay = 86400.0;
        /// <summary>Joules in one keV.</summary>
        public const double JoulesPerKev = 1.602176634e-16;
        /// <summary>Kelvin that correspond to one keV of thermal energy.</summary>
        public const double KelvinPerKev = 11604518.12;

        /// <summary>The units the time step field offers, in order.</summary>
        public static readonly string[] StepUnits = { "s", "min", "h", "d", "y" };

        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        private static readonly string[] ModeNames =
        {
            "beta-", "beta+ or EC", "electron capture", "positron emission", "alpha",
            "proton emission", "2p emission", "3p emission", "neutron emission", "2n emission",
            "3n emission", "spontaneous fission", "double beta-", "double beta+", "beta- + n",
            "beta- + 2n", "beta- + 3n", "beta- + 4n", "beta- + alpha", "beta- + d",
            "beta- + t", "beta- + p", "beta- + fission", "beta+ + p", "beta+ + 2p",
            "beta+ + 3p", "beta+ + alpha", "beta+ + p + alpha", "beta+ + fission",
            "cluster emission", "cluster mixture",
        };

        /// <summary>
        /// A number with <paramref name="significant"/> digits. Plain for
        /// 0.001 up to a million, scientific outside that, and no trailing zeros.
        /// </summary>
        public static string Number(double value, int significant)
        {
            if (double.IsNaN(value))
            {
                return "NaN";
            }
            if (double.IsPositiveInfinity(value))
            {
                return "inf";
            }
            if (double.IsNegativeInfinity(value))
            {
                return "-inf";
            }
            if (value == 0.0)
            {
                return "0";
            }
            if (significant < 1)
            {
                significant = 1;
            }
            double a = Math.Abs(value);
            if (a >= 1000.0 && a < 1.0e6)
            {
                return value.ToString("#,##0", Inv);
            }
            if (a >= 1.0e-3 && a < 1000.0)
            {
                return value.ToString("G" + significant.ToString(Inv), Inv);
            }
            string mantissa = significant > 1 ? "0." + new string('#', significant - 1) : "0";
            return value.ToString(mantissa + "e+0", Inv);
        }

        /// <summary>A number with four significant digits.</summary>
        public static string Number(double value)
        {
            return Number(value, 4);
        }

        /// <summary>A time span in seconds as the unit that reads best, from nanoseconds to years.</summary>
        public static string Duration(double seconds)
        {
            if (double.IsNaN(seconds))
            {
                return "NaN";
            }
            if (double.IsInfinity(seconds))
            {
                return seconds > 0.0 ? "inf" : "-inf";
            }
            if (seconds == 0.0)
            {
                return "0 s";
            }
            double a = Math.Abs(seconds);
            if (a < 1.0e-9)
            {
                return Number(seconds) + " s";
            }
            if (a < 1.0e-6)
            {
                return Number(seconds * 1.0e9) + " ns";
            }
            if (a < 1.0e-3)
            {
                return Number(seconds * 1.0e6) + " us";
            }
            if (a < 1.0)
            {
                return Number(seconds * 1.0e3) + " ms";
            }
            if (a < 60.0)
            {
                return Number(seconds) + " s";
            }
            if (a < 3600.0)
            {
                return Number(seconds / 60.0) + " min";
            }
            if (a < SecondsPerDay)
            {
                return Number(seconds / 3600.0) + " h";
            }
            if (a < SecondsPerYear)
            {
                return Number(seconds / SecondsPerDay) + " d";
            }
            return Number(seconds / SecondsPerYear) + " y";
        }

        /// <summary>The number of seconds in one of <see cref="StepUnits"/>.</summary>
        public static double UnitSeconds(int unit)
        {
            switch (unit)
            {
                case 0: return 1.0;
                case 1: return 60.0;
                case 2: return 3600.0;
                case 3: return SecondsPerDay;
                case 4: return SecondsPerYear;
                default: throw new ArgumentOutOfRangeException("unit");
            }
        }

        /// <summary>A whole number of atoms with thousands separators.</summary>
        public static string Count(ulong value)
        {
            return value.ToString("N0", Inv);
        }

        /// <summary>An amount: whole numbers print as counts, anything else as a number.</summary>
        public static string Amount(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
            {
                return Number(value);
            }
            double rounded = Math.Round(value);
            if (Math.Abs(value) < 1.0e15 && Math.Abs(value - rounded) < 1.0e-6)
            {
                return rounded.ToString("N0", Inv);
            }
            return Number(value);
        }

        /// <summary>An energy given in keV, shown as eV, keV or MeV.</summary>
        public static string Energy(double kev)
        {
            if (double.IsNaN(kev) || double.IsInfinity(kev))
            {
                return Number(kev);
            }
            double a = Math.Abs(kev);
            if (a == 0.0)
            {
                return "0 keV";
            }
            if (a < 1.0)
            {
                return Number(kev * 1000.0) + " eV";
            }
            if (a < 1000.0)
            {
                return Number(kev) + " keV";
            }
            return Number(kev / 1000.0) + " MeV";
        }

        /// <summary>An energy given in keV, shown in joules.</summary>
        public static string Joules(double kev)
        {
            return Number(kev * JoulesPerKev) + " J";
        }

        /// <summary>A thermal energy given in keV, shown as a temperature in kelvin.</summary>
        public static string Kelvin(double kev)
        {
            return Number(kev * KelvinPerKev) + " K";
        }

        /// <summary>A share in percent.</summary>
        public static string Percent(double percent)
        {
            return Number(percent) + " %";
        }

        /// <summary>A half-life, with the sign that says whether it is a limit or an estimate.</summary>
        public static string HalfLifeText(HalfLife halfLife)
        {
            string text;
            switch (halfLife.State)
            {
                case HalfLifeState.Stable:
                    return "stable";
                case HalfLifeState.ParticleUnstable:
                    return "particle unstable";
                case HalfLifeState.Exact:
                    text = Duration(halfLife.Seconds);
                    break;
                case HalfLifeState.Approximate:
                    text = "about " + Duration(halfLife.Seconds);
                    break;
                case HalfLifeState.AtLeast:
                    text = "at least " + Duration(halfLife.Seconds);
                    break;
                case HalfLifeState.AtMost:
                    text = "at most " + Duration(halfLife.Seconds);
                    break;
                default:
                    return "unknown";
            }
            return halfLife.Estimated ? text + " (estimated)" : text;
        }

        /// <summary>The name of a decay mode in plain text.</summary>
        public static string ModeName(DecayMode mode)
        {
            int index = (int)mode;
            return index >= 0 && index < ModeNames.Length ? ModeNames[index] : mode.ToString();
        }

        /// <summary>Why a decay chain stopped, in plain text.</summary>
        public static string EndReasonText(ChainEndReason reason)
        {
            switch (reason)
            {
                case ChainEndReason.Stable: return "stable";
                case ChainEndReason.NoDaughter: return "decays by a mode with no single daughter";
                case ChainEndReason.NoData: return "no decay data";
                case ChainEndReason.NotInTable: return "not in the table";
                case ChainEndReason.Loop: return "already visited";
                case ChainEndReason.TooLong: return "step limit reached";
                default: return reason.ToString();
            }
        }

        /// <summary>Where an energy value came from, in plain text.</summary>
        public static string SourceText(EnergySource source)
        {
            switch (source)
            {
                case EnergySource.Measured: return "measured";
                case EnergySource.Estimated: return "estimated from trends";
                case EnergySource.LiquidDrop: return "liquid drop model";
                default: return source.ToString();
            }
        }

        /// <summary>Where fission yields came from, in plain text.</summary>
        public static string YieldSourceText(YieldSource source)
        {
            switch (source)
            {
                case YieldSource.Evaluated: return "evaluated for this nucleus";
                case YieldSource.Extended: return "borrowed from the nearest evaluated nucleus";
                default: return source.ToString();
            }
        }

        /// <summary>How a fusion reactivity is computed, in plain text.</summary>
        public static string FitText(FitKind fit)
        {
            switch (fit)
            {
                case FitKind.BoschHale: return "Bosch-Hale fit";
                case FitKind.Gamow: return "Gamow integral";
                default: return fit.ToString();
            }
        }

        /// <summary>
        /// Where a value falls between a floor and a ceiling as a fraction from 0 to 1,
        /// on a linear or a logarithmic scale. Used to size bars. A value that
        /// cannot be placed on a log scale (zero or negative) gives 0.
        /// </summary>
        public static double BarFraction(double value, double min, double max, bool log)
        {
            if (double.IsNaN(value) || double.IsNaN(min) || double.IsNaN(max) || !(max > min))
            {
                return 0.0;
            }
            double fraction;
            if (log)
            {
                if (value <= 0.0 || min <= 0.0)
                {
                    return 0.0;
                }
                double low = Math.Log10(min);
                double high = Math.Log10(max);
                if (!(high > low))
                {
                    return 0.0;
                }
                fraction = (Math.Log10(value) - low) / (high - low);
            }
            else
            {
                fraction = (value - min) / (max - min);
            }
            return fraction < 0.0 ? 0.0 : (fraction > 1.0 ? 1.0 : fraction);
        }

        /// <summary>Reads a decimal number with a dot, an optional exponent and optional thousands commas.</summary>
        public static bool TryParseNumber(string text, out double value)
        {
            value = 0.0;
            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }
            string cleaned = text.Trim().Replace(",", string.Empty);
            if (!double.TryParse(cleaned, NumberStyles.Float, Inv, out value))
            {
                value = 0.0;
                return false;
            }
            if (double.IsNaN(value) || double.IsInfinity(value))
            {
                value = 0.0;
                return false;
            }
            return true;
        }

        /// <summary>Reads a whole number such as 1000000, 1,000,000 or 1e6. Negative and fractional values are refused.</summary>
        public static bool TryParseCount(string text, out ulong value)
        {
            value = 0;
            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }
            string trimmed = text.Trim();
            if (ulong.TryParse(trimmed, NumberStyles.AllowThousands, Inv, out value))
            {
                return true;
            }
            value = 0;
            double number;
            if (!TryParseNumber(trimmed, out number))
            {
                return false;
            }
            if (number < 0.0 || number != Math.Floor(number) || number >= 18446744073709551616.0)
            {
                return false;
            }
            value = (ulong)number;
            return true;
        }
    }
}
