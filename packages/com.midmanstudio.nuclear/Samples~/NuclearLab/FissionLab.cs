// ============================================================================
// NOTICE: Full documentation, design decisions, and fix history for this file
// live in docs/com.midmanstudio.nuclear.md, section "Samples~/NuclearLab/FissionLab.cs"
// ============================================================================
using System;
using System.Collections.Generic;

namespace MidManStudio.Nuclear.Samples.NuclearLab
{
    /// <summary>What the library says about the fission of one nucleus: the summary, the likeliest outcomes and the mass distribution.</summary>
    public sealed class FissionInfo
    {
        /// <summary>The nucleus asked about. Spontaneous fission of it, or the target a neutron is absorbed by.</summary>
        public Nuclide Target;
        /// <summary>The neutron energy in eV, 0 for spontaneous fission.</summary>
        public double EnergyEv;
        /// <summary>The summary of the outcomes.</summary>
        public FissionSummary Summary;
        /// <summary>The most probable channels, the likeliest first.</summary>
        public FissionChannel[] TopChannels;
        /// <summary>Every channel, the likeliest first. The fission animation draws its outcome from these.</summary>
        public FissionChannel[] AllChannels;
        /// <summary>The most abundant fission products by yield, the largest first.</summary>
        public NuclideAmount[] TopYields;
        /// <summary>Number of fission products with a yield.</summary>
        public int ProductCount;
        /// <summary>The lowest mass number with a yield.</summary>
        public int MassMin;
        /// <summary>The yield per fission for each mass number from <see cref="MassMin"/> up. Sums to 2.</summary>
        public double[] MassYields;
        /// <summary>True when the neutron branching table covers the target at this energy.</summary>
        public bool HasBranching;
        /// <summary>The authored chance that an absorbed neutron ends in fission. Only meaningful when <see cref="HasBranching"/>.</summary>
        public double FissionProbability;
        /// <summary>True when <see cref="CaptureQKev"/> is a value.</summary>
        public bool HasCaptureQ;
        /// <summary>The energy in keV released when the target captures a neutron.</summary>
        public double CaptureQKev;
    }

    /// <summary>The outcome of sending neutrons at a pile of one nuclide.</summary>
    public sealed class FissionRun
    {
        /// <summary>The target nuclide.</summary>
        public Nuclide Target;
        /// <summary>Atoms of the target at the start.</summary>
        public ulong Atoms;
        /// <summary>Neutrons sent in.</summary>
        public ulong Neutrons;
        /// <summary>Neutron energy in eV.</summary>
        public double EnergyEv;
        /// <summary>The report of the irradiation.</summary>
        public ReactionReport Report;
        /// <summary>The totals of the pile afterwards.</summary>
        public PileTotals Totals;
        /// <summary>Nucleons at the start, the target atoms plus the neutrons sent in.</summary>
        public double StartBaryons;
        /// <summary>Every nuclide in the pile afterwards, the most plentiful first.</summary>
        public NuclideCount[] Products;
    }

    /// <summary>
    /// Fission queries and neutron irradiation for the fission tab. Queries use
    /// a context of their own. Each <see cref="Run"/> builds and frees its own
    /// pile. Has no UnityEngine references. Not thread safe.
    /// </summary>
    public sealed class FissionLab : IDisposable
    {
        private readonly int channelLimit;
        private readonly int yieldLimit;
        private NuclearContext queryContext;

        /// <summary>Creates the lab. The limits say how many channels and products <see cref="Query"/> keeps.</summary>
        public FissionLab(int channelLimit, int yieldLimit)
        {
            if (channelLimit < 1 || yieldLimit < 1)
            {
                throw new ArgumentOutOfRangeException("channelLimit", "The limits must be at least 1.");
            }
            this.channelLimit = channelLimit;
            this.yieldLimit = yieldLimit;
            queryContext = new NuclearContext(0);
        }

        /// <summary>Reads the fission data of a nucleus. A neutron energy of 0 means spontaneous fission.</summary>
        public FissionInfo Query(Nuclide target, double energyEv)
        {
            if (queryContext == null)
            {
                throw new ObjectDisposedException("FissionLab");
            }
            FissionInfo info = new FissionInfo();
            info.Target = target;
            info.EnergyEv = energyEv;
            info.Summary = queryContext.GetFissionSummary(target, energyEv);

            FissionChannel[] channels = queryContext.GetFissionChannels(target, energyEv);
            Array.Sort(channels, CompareChannels);
            info.AllChannels = channels;
            info.TopChannels = Take(channels, channelLimit);

            NuclideAmount[] yields = queryContext.GetFissionYields(target, energyEv);
            info.ProductCount = yields.Length;
            FillMassYields(info, yields);
            Array.Sort(yields, CompareYields);
            info.TopYields = Take(yields, yieldLimit);

            if (energyEv > 0.0)
            {
                double probability;
                info.HasBranching = NuclearData.TryGetNeutronBranching(target, energyEv, out probability);
                info.FissionProbability = info.HasBranching ? probability : 0.0;
                try
                {
                    info.CaptureQKev = NuclearData.GetCaptureQKev(target);
                    info.HasCaptureQ = true;
                }
                catch (NuclearException)
                {
                    info.HasCaptureQ = false;
                }
            }
            return info;
        }

        /// <summary>
        /// Builds a pile of <paramref name="atoms"/> atoms of <paramref name="target"/>
        /// and sends <paramref name="neutrons"/> neutrons of <paramref name="energyEv"/> at it, each into a
        /// different atom. With no <paramref name="fissionProbability"/>, the authored table decides how
        /// many absorptions end in fission, and a target outside that table is refused.
        /// </summary>
        public FissionRun Run(
            Nuclide target, ulong atoms, ulong neutrons, double energyEv, double? fissionProbability, ulong seed)
        {
            FissionRun run = new FissionRun();
            run.Target = target;
            run.Atoms = atoms;
            run.Neutrons = neutrons;
            run.EnergyEv = energyEv;
            run.StartBaryons = (double)atoms * target.A + (double)neutrons;
            using (NuclearContext context = new NuclearContext(seed))
            using (NuclearSample pile = new NuclearSample())
            {
                pile.Add(target, atoms);
                run.Report = fissionProbability.HasValue
                    ? pile.Irradiate(context, target, neutrons, energyEv, fissionProbability.Value)
                    : pile.Irradiate(context, target, neutrons, energyEv);
                run.Totals = pile.GetTotals();
                NuclideCount[] counts = pile.GetCounts();
                Array.Sort(counts, CompareCounts);
                run.Products = counts;
            }
            return run;
        }

        /// <summary>Frees the query context. Safe to call more than once.</summary>
        public void Dispose()
        {
            if (queryContext != null)
            {
                queryContext.Dispose();
                queryContext = null;
            }
        }

        private static void FillMassYields(FissionInfo info, NuclideAmount[] yields)
        {
            if (yields.Length == 0)
            {
                info.MassMin = 0;
                info.MassYields = new double[0];
                return;
            }
            int min = int.MaxValue;
            int max = int.MinValue;
            foreach (NuclideAmount y in yields)
            {
                int a = y.Nuclide.A;
                min = Math.Min(min, a);
                max = Math.Max(max, a);
            }
            double[] sums = new double[max - min + 1];
            foreach (NuclideAmount y in yields)
            {
                sums[y.Nuclide.A - min] += y.Amount;
            }
            info.MassMin = min;
            info.MassYields = sums;
        }

        private static T[] Take<T>(T[] source, int limit)
        {
            int n = Math.Min(limit, source.Length);
            T[] result = new T[n];
            Array.Copy(source, result, n);
            return result;
        }

        private static int CompareChannels(FissionChannel a, FissionChannel b)
        {
            int byProbability = b.Probability.CompareTo(a.Probability);
            if (byProbability != 0)
            {
                return byProbability;
            }
            int byLight = a.Light.CompareTo(b.Light);
            return byLight != 0 ? byLight : a.Heavy.CompareTo(b.Heavy);
        }

        private static int CompareYields(NuclideAmount a, NuclideAmount b)
        {
            int byAmount = b.Amount.CompareTo(a.Amount);
            return byAmount != 0 ? byAmount : a.Nuclide.CompareTo(b.Nuclide);
        }

        private static int CompareCounts(NuclideCount a, NuclideCount b)
        {
            int byCount = b.Count.CompareTo(a.Count);
            return byCount != 0 ? byCount : a.Nuclide.CompareTo(b.Nuclide);
        }
    }
}
