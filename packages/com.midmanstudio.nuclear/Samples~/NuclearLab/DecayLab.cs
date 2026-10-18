// ============================================================================
// NOTICE: Full documentation, design decisions, and fix history for this file
// live in docs/com.midmanstudio.nuclear.md, section "Samples~/NuclearLab/DecayLab.cs"
// ============================================================================
using System;
using System.Collections.Generic;

namespace MidManStudio.Nuclear.Samples.NuclearLab
{
    /// <summary>Which kind of pile the decay lab runs.</summary>
    public enum PileKind
    {
        /// <summary>Whole atoms, every step an exact random draw (<see cref="NuclearSample"/>).</summary>
        Atoms = 0,
        /// <summary>Fractional amounts, every step the exact expectation (<see cref="NuclearAmounts"/>).</summary>
        Amounts = 1,
    }

    /// <summary>One nuclide in a pile.</summary>
    public struct PileRow
    {
        /// <summary>The nuclide.</summary>
        public Nuclide Nuclide;
        /// <summary>Atoms, or the amount for an expectation pile.</summary>
        public double Amount;
        /// <summary>The amount divided by the number of atoms the pile started with.</summary>
        public double PerStartAtom;
    }

    /// <summary>
    /// A pile that starts as one nuclide and is advanced through time. It owns a
    /// random context and either an atom pile or an amounts pile, and keeps
    /// the running energy total. Has no UnityEngine references. Not thread safe.
    /// </summary>
    public sealed class DecayLab : IDisposable
    {
        private NuclearContext context;
        private NuclearSample atoms;
        private NuclearAmounts amounts;
        private double startAmount;

        /// <summary>True once <see cref="Reset"/> has built a pile.</summary>
        public bool HasPile
        {
            get { return context != null; }
        }

        /// <summary>Which kind of pile is running.</summary>
        public PileKind Kind { get; private set; }

        /// <summary>The nuclide the pile started as.</summary>
        public Nuclide Start { get; private set; }

        /// <summary>The number of atoms the pile started with.</summary>
        public ulong StartCount { get; private set; }

        /// <summary>The seed of the random stream.</summary>
        public ulong Seed { get; private set; }

        /// <summary>Energy released so far in keV, for all steps and fission resolutions.</summary>
        public double EnergyReleasedKev { get; private set; }

        /// <summary>False once any step had to leave out a mass that is missing, so the energy is a lower estimate.</summary>
        public bool EnergyKnown { get; private set; }

        /// <summary>Atoms (or amount) that changed in the last step.</summary>
        public double LastChanged { get; private set; }

        /// <summary>Atoms (or amount) that moved to the pending fission list in the last step.</summary>
        public double LastNewPending { get; private set; }

        /// <summary>Fissions resolved so far.</summary>
        public double ResolvedFissions { get; private set; }

        /// <summary>Free neutrons released by resolved fissions so far.</summary>
        public double NeutronsFromFission { get; private set; }

        /// <summary>Pending fissions the last resolution had to leave waiting.</summary>
        public double Unresolved { get; private set; }

        /// <summary>Simulated seconds since the pile was built.</summary>
        public double ElapsedSeconds
        {
            get
            {
                Require();
                return Kind == PileKind.Atoms ? atoms.ElapsedSeconds : amounts.ElapsedSeconds;
            }
        }

        /// <summary>The nucleon count of the pile now, pending fissions included.</summary>
        public double Baryons
        {
            get
            {
                Require();
                return Kind == PileKind.Atoms ? (double)atoms.GetTotals().Baryons : amounts.BaryonNumber;
            }
        }

        /// <summary>The nucleon count the pile started with.</summary>
        public double StartBaryons
        {
            get { return (double)StartCount * Start.A; }
        }

        /// <summary>The number of atoms in the pile now, not counting pending fissions.</summary>
        public double AtomTotal
        {
            get
            {
                Require();
                if (Kind == PileKind.Atoms)
                {
                    return (double)atoms.GetTotals().Atoms;
                }
                double sum = 0.0;
                foreach (NuclideAmount a in amounts.GetAmounts())
                {
                    sum += a.Amount;
                }
                return sum;
            }
        }

        /// <summary>
        /// Builds a new pile of <paramref name="count"/> atoms of <paramref name="start"/>.
        /// If anything fails, the old pile is left as it was.
        /// </summary>
        public void Reset(Nuclide start, ulong count, PileKind kind, ulong seed)
        {
            if (count == 0)
            {
                throw new ArgumentOutOfRangeException("count", "The pile needs at least one atom.");
            }
            NuclearContext newContext = new NuclearContext(seed);
            NuclearSample newAtoms = null;
            NuclearAmounts newAmounts = null;
            try
            {
                if (kind == PileKind.Atoms)
                {
                    newAtoms = new NuclearSample();
                    newAtoms.Add(start, count);
                }
                else
                {
                    newAmounts = new NuclearAmounts();
                    newAmounts.Add(start, (double)count);
                }
            }
            catch
            {
                if (newAtoms != null)
                {
                    newAtoms.Dispose();
                }
                if (newAmounts != null)
                {
                    newAmounts.Dispose();
                }
                newContext.Dispose();
                throw;
            }
            Release();
            context = newContext;
            atoms = newAtoms;
            amounts = newAmounts;
            Kind = kind;
            Start = start;
            StartCount = count;
            Seed = seed;
            startAmount = (double)count;
            EnergyReleasedKev = 0.0;
            EnergyKnown = true;
            LastChanged = 0.0;
            LastNewPending = 0.0;
            ResolvedFissions = 0.0;
            NeutronsFromFission = 0.0;
            Unresolved = 0.0;
        }

        /// <summary>Advances the pile by <paramref name="seconds"/> of simulated time.</summary>
        public void Step(double seconds)
        {
            Require();
            if (Kind == PileKind.Atoms)
            {
                StepReport report = atoms.Advance(context, seconds);
                AddEnergy(report.EnergyReleasedKev, report.EnergyKnown);
                LastChanged = report.AtomsChanged;
                LastNewPending = report.NewPendingFissions;
            }
            else
            {
                AmountsStepReport report = amounts.Advance(context, seconds);
                AddEnergy(report.EnergyReleasedKev, report.EnergyKnown);
                LastChanged = report.AmountChanged;
                LastNewPending = report.NewPending;
            }
        }

        /// <summary>Turns every pending fission into fission products.</summary>
        public void ResolveFissions()
        {
            Require();
            if (Kind == PileKind.Atoms)
            {
                ReactionReport report = atoms.ResolveFissions(context);
                AddEnergy(report.EnergyReleasedKev, report.EnergyKnown);
                ResolvedFissions += report.Fissions;
                NeutronsFromFission += report.NeutronsReleased;
                Unresolved = report.Unresolved;
            }
            else
            {
                AmountsReactionReport report = amounts.ResolveFissions(context);
                AddEnergy(report.EnergyReleasedKev, report.EnergyKnown);
                ResolvedFissions += report.Fissions;
                NeutronsFromFission += report.NeutronsReleased;
                Unresolved = report.Unresolved;
            }
        }

        /// <summary>The nuclides in the pile, the most plentiful first.</summary>
        public PileRow[] Rows()
        {
            Require();
            List<PileRow> rows = new List<PileRow>();
            if (Kind == PileKind.Atoms)
            {
                foreach (NuclideCount c in atoms.GetCounts())
                {
                    rows.Add(MakeRow(c.Nuclide, (double)c.Count));
                }
            }
            else
            {
                foreach (NuclideAmount a in amounts.GetAmounts())
                {
                    rows.Add(MakeRow(a.Nuclide, a.Amount));
                }
            }
            rows.Sort(CompareRows);
            return rows.ToArray();
        }

        /// <summary>The atoms (or amount) waiting for fission products, the most plentiful first.</summary>
        public PileRow[] Pending()
        {
            Require();
            List<PileRow> rows = new List<PileRow>();
            if (Kind == PileKind.Atoms)
            {
                foreach (NuclideCount c in atoms.GetPendingFissions())
                {
                    rows.Add(MakeRow(c.Nuclide, (double)c.Count));
                }
            }
            else
            {
                foreach (NuclideAmount a in amounts.GetPendingFissions())
                {
                    rows.Add(MakeRow(a.Nuclide, a.Amount));
                }
            }
            rows.Sort(CompareRows);
            return rows.ToArray();
        }

        /// <summary>The half-life of the starting nuclide in seconds, or false when it has no finite number.</summary>
        public bool TryGetStartHalfLife(out double seconds)
        {
            seconds = 0.0;
            HalfLife halfLife;
            if (!HasPile || !NuclearData.TryGetHalfLife(Start, out halfLife))
            {
                return false;
            }
            if (!halfLife.HasNumber || !(halfLife.Seconds > 0.0) || double.IsInfinity(halfLife.Seconds))
            {
                return false;
            }
            seconds = halfLife.Seconds;
            return true;
        }

        /// <summary>Frees the native pile and context. Safe to call more than once.</summary>
        public void Dispose()
        {
            Release();
        }

        private PileRow MakeRow(Nuclide nuclide, double amount)
        {
            PileRow row;
            row.Nuclide = nuclide;
            row.Amount = amount;
            row.PerStartAtom = amount / startAmount;
            return row;
        }

        private static int CompareRows(PileRow a, PileRow b)
        {
            int byAmount = b.Amount.CompareTo(a.Amount);
            return byAmount != 0 ? byAmount : a.Nuclide.CompareTo(b.Nuclide);
        }

        private void AddEnergy(double kev, bool known)
        {
            EnergyReleasedKev += kev;
            EnergyKnown &= known;
        }

        private void Require()
        {
            if (context == null)
            {
                throw new InvalidOperationException("Start a pile first.");
            }
        }

        private void Release()
        {
            if (atoms != null)
            {
                atoms.Dispose();
            }
            if (amounts != null)
            {
                amounts.Dispose();
            }
            if (context != null)
            {
                context.Dispose();
            }
            atoms = null;
            amounts = null;
            context = null;
        }
    }
}
