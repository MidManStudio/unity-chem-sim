// ============================================================================
// NOTICE: Full documentation, design decisions, and fix history for this file
// live in docs/com.midmanstudio.nuclear.md, section "Samples~/NuclearLab/FusionLab.cs"
// ============================================================================
using System;

namespace MidManStudio.Nuclear.Samples.NuclearLab
{
    /// <summary>One fusion channel of the library, as the channel table shows it.</summary>
    public struct FusionRow
    {
        /// <summary>The channel index.</summary>
        public int Index;
        /// <summary>The name, such as D + T -> He-4 + n.</summary>
        public string Name;
        /// <summary>Energy released in keV.</summary>
        public double QKev;
        /// <summary>How the reactivity is computed.</summary>
        public FitKind Fit;
        /// <summary>Lowest temperature of the published fit in keV, 0 for a Gamow channel.</summary>
        public double FitLowKev;
        /// <summary>Highest temperature of the published fit in keV, 0 for a Gamow channel.</summary>
        public double FitHighKev;
    }

    /// <summary>One channel of a pair of nuclei at one temperature.</summary>
    public struct PairChannel
    {
        /// <summary>The channel and its description.</summary>
        public FusionRow Row;
        /// <summary>The share of fusion events that take this channel, 0 to 1.</summary>
        public double Share;
        /// <summary>The thermal reactivity in cm^3/s.</summary>
        public double Reactivity;
        /// <summary>False when the temperature is outside the published range of a Bosch-Hale fit, so the value is an extrapolation.</summary>
        public bool InsideFit;
    }

    /// <summary>The channels of a pair of nuclei at one temperature.</summary>
    public sealed class FusionPair
    {
        /// <summary>The first nucleus.</summary>
        public Nuclide A;
        /// <summary>The second nucleus.</summary>
        public Nuclide B;
        /// <summary>The temperature in keV.</summary>
        public double TemperatureKev;
        /// <summary>The channels of the pair, empty when the pair has none.</summary>
        public PairChannel[] Channels;
    }

    /// <summary>The outcome of fusing a pile of two nuclei.</summary>
    public sealed class FusionRun
    {
        /// <summary>The report of the fusion.</summary>
        public ReactionReport Report;
        /// <summary>The totals of the pile afterwards.</summary>
        public PileTotals Totals;
        /// <summary>Nucleons at the start.</summary>
        public double StartBaryons;
        /// <summary>Every nuclide in the pile afterwards, the most plentiful first.</summary>
        public NuclideCount[] Products;
    }

    /// <summary>
    /// Fusion queries, a reactivity curve, and a fusion run for the fusion tab.
    /// Has no UnityEngine references. Not thread safe.
    /// </summary>
    public static class FusionLab
    {
        /// <summary>Every fusion channel of the library in index order.</summary>
        public static FusionRow[] AllChannels()
        {
            int count = NuclearFusion.ReactionCount;
            FusionRow[] rows = new FusionRow[count];
            for (int i = 0; i < count; i++)
            {
                rows[i] = MakeRow(i);
            }
            return rows;
        }

        /// <summary>The channels of a pair of nuclei at a temperature in keV, with shares and reactivities.</summary>
        public static FusionPair QueryPair(Nuclide a, Nuclide b, double temperatureKev)
        {
            if (!(temperatureKev > 0.0))
            {
                throw new ArgumentOutOfRangeException("temperatureKev", "The temperature must be greater than zero.");
            }
            FusionPair pair = new FusionPair();
            pair.A = a;
            pair.B = b;
            pair.TemperatureKev = temperatureKev;
            int[] indices = NuclearFusion.GetChannelsFor(a, b);
            double[] shares = NuclearFusion.GetBranching(a, b, temperatureKev);
            pair.Channels = new PairChannel[indices.Length];
            for (int i = 0; i < indices.Length; i++)
            {
                PairChannel channel;
                channel.Row = MakeRow(indices[i]);
                channel.Share = i < shares.Length ? shares[i] : 0.0;
                channel.Reactivity = NuclearFusion.GetReactivity(indices[i], temperatureKev);
                channel.InsideFit = channel.Row.Fit != FitKind.BoschHale
                    || (temperatureKev >= channel.Row.FitLowKev && temperatureKev <= channel.Row.FitHighKev);
                pair.Channels[i] = channel;
            }
            return pair;
        }

        /// <summary>
        /// The temperatures of a curve: <paramref name="points"/> values spaced evenly on a log scale
        /// from <paramref name="lowKev"/> to <paramref name="highKev"/>, both ends included.
        /// </summary>
        public static double[] Temperatures(double lowKev, double highKev, int points)
        {
            if (!(lowKev > 0.0) || !(highKev > lowKev) || points < 2)
            {
                throw new ArgumentOutOfRangeException("points", "A curve needs 0 < low < high and at least two points.");
            }
            double[] result = new double[points];
            double low = Math.Log10(lowKev);
            double step = (Math.Log10(highKev) - low) / (points - 1);
            for (int i = 0; i < points; i++)
            {
                result[i] = i == points - 1 ? highKev : Math.Pow(10.0, low + step * i);
            }
            result[0] = lowKev;
            return result;
        }

        /// <summary>The thermal reactivity of a channel at each of the temperatures.</summary>
        public static double[] ReactivityCurve(int channelIndex, double[] temperaturesKev)
        {
            double[] values = new double[temperaturesKev.Length];
            for (int i = 0; i < values.Length; i++)
            {
                values[i] = NuclearFusion.GetReactivity(channelIndex, temperaturesKev[i]);
            }
            return values;
        }

        /// <summary>
        /// Builds a pile with <paramref name="countA"/> atoms of <paramref name="a"/> and
        /// <paramref name="countB"/> of <paramref name="b"/> (one kind only when they are the same
        /// nucleus) and fuses <paramref name="events"/> pairs at the temperature.
        /// </summary>
        public static FusionRun Run(
            Nuclide a, ulong countA, Nuclide b, ulong countB, ulong events, double temperatureKev, ulong seed)
        {
            FusionRun run = new FusionRun();
            bool same = a == b;
            run.StartBaryons = (double)countA * a.A + (same ? 0.0 : (double)countB * b.A);
            using (NuclearContext context = new NuclearContext(seed))
            using (NuclearSample pile = new NuclearSample())
            {
                pile.Add(a, countA);
                if (!same)
                {
                    pile.Add(b, countB);
                }
                run.Report = pile.Fuse(context, a, b, events, temperatureKev);
                run.Totals = pile.GetTotals();
                NuclideCount[] counts = pile.GetCounts();
                Array.Sort(counts, CompareCounts);
                run.Products = counts;
            }
            return run;
        }

        private static FusionRow MakeRow(int index)
        {
            FusionChannel channel = NuclearFusion.GetReaction(index);
            FusionRow row;
            row.Index = index;
            row.Name = NuclearFusion.GetName(index);
            row.QKev = channel.QKev;
            row.Fit = channel.Fit;
            row.FitLowKev = channel.FitLowKev;
            row.FitHighKev = channel.FitHighKev;
            return row;
        }

        private static int CompareCounts(NuclideCount a, NuclideCount b)
        {
            int byCount = b.Count.CompareTo(a.Count);
            return byCount != 0 ? byCount : a.Nuclide.CompareTo(b.Nuclide);
        }
    }
}
