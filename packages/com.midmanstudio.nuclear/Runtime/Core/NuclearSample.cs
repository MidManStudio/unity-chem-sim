// ============================================================================
// NOTICE: Full documentation, design decisions, and fix history for this file
// live in docs/com.midmanstudio.nuclear.md, section "Runtime/Core/NuclearSample.cs"
// ============================================================================
using System;

namespace MidManStudio.Nuclear
{
    /// <summary>
    /// A pile of whole atoms that decays over time and takes part in
    /// reactions. Every outcome is an exact draw, so the same seed and the
    /// same calls give the same pile. A failed call leaves the pile unchanged.
    /// Not thread safe.
    /// </summary>
    public sealed class NuclearSample : IDisposable
    {
        private IntPtr handle;

        /// <summary>Creates an empty pile.</summary>
        public NuclearSample()
        {
            NuclearLibrary.Initialize();
            handle = NuclearNative.nuc_sample_create();
            if (handle == IntPtr.Zero)
            {
                throw new InvalidOperationException("nuclear_core could not create a sample.");
            }
        }

        private NuclearSample(IntPtr existing)
        {
            handle = existing;
        }

        /// <summary>Frees the native pile if <see cref="Dispose"/> was not called.</summary>
        ~NuclearSample()
        {
            Release();
        }

        private IntPtr Handle
        {
            get
            {
                if (handle == IntPtr.Zero)
                {
                    throw new ObjectDisposedException("NuclearSample");
                }
                return handle;
            }
        }

        /// <summary>A copy of the pile, pending fissions and elapsed time included.</summary>
        public NuclearSample Clone()
        {
            IntPtr copy = NuclearNative.nuc_sample_clone(Handle);
            if (copy == IntPtr.Zero)
            {
                throw new InvalidOperationException("nuclear_core could not copy the sample.");
            }
            return new NuclearSample(copy);
        }

        /// <summary>Adds <paramref name="count"/> atoms of a nuclide.</summary>
        public void Add(Nuclide nuclide, ulong count)
        {
            NuclearNative.Check(NuclearNative.nuc_sample_add(Handle, nuclide.Key, count));
        }

        /// <summary>The number of atoms of a nuclide.</summary>
        public ulong Count(Nuclide nuclide)
        {
            ulong count;
            NuclearNative.Check(NuclearNative.nuc_sample_count(Handle, nuclide.Key, out count));
            return count;
        }

        /// <summary>Every nuclide in the pile with its number of atoms, in ascending nuclide order.</summary>
        public NuclideCount[] GetCounts()
        {
            IntPtr h = Handle;
            return NuclearNative.ReadList<NuclideCount>(
                (NuclideCount[] buffer, int capacity, out int count) =>
                    NuclearNative.nuc_sample_counts(h, buffer, capacity, out count));
        }

        /// <summary>The atoms waiting for fission products, keyed by the nucleus that is waiting.</summary>
        public NuclideCount[] GetPendingFissions()
        {
            IntPtr h = Handle;
            return NuclearNative.ReadList<NuclideCount>(
                (NuclideCount[] buffer, int capacity, out int count) =>
                    NuclearNative.nuc_sample_pending(h, buffer, capacity, out count));
        }

        /// <summary>Simulated seconds since the pile was created.</summary>
        public double ElapsedSeconds
        {
            get
            {
                double seconds;
                NuclearNative.Check(NuclearNative.nuc_sample_elapsed_seconds(Handle, out seconds));
                return seconds;
            }
        }

        /// <summary>The atom count (pending fissions not included) and the nucleon count (included).</summary>
        public PileTotals GetTotals()
        {
            PileTotals totals;
            NuclearNative.Check(NuclearNative.nuc_sample_totals(Handle, out totals));
            return totals;
        }

        /// <summary>
        /// Advances the pile by <paramref name="seconds"/> of simulated time,
        /// drawing from the context's random stream. The interval can be
        /// anything from a microsecond to the age of the universe.
        /// </summary>
        public StepReport Advance(NuclearContext context, double seconds)
        {
            StepReport report;
            NuclearNative.Check(NuclearNative.nuc_sample_advance(Require(context), Handle, seconds, out report));
            return report;
        }

        /// <summary>Turns every pending fission into fission products.</summary>
        public ReactionReport ResolveFissions(NuclearContext context)
        {
            ReactionReport report;
            NuclearNative.Check(NuclearNative.nuc_sample_resolve_fissions(Require(context), Handle, out report));
            return report;
        }

        /// <summary>
        /// Sends neutrons of <paramref name="energyEv"/> at atoms of
        /// <paramref name="target"/>, each into a different atom. Each absorption
        /// is fission with probability <paramref name="fissionProbability"/> and
        /// capture otherwise. The neutrons come from outside the pile.
        /// </summary>
        public ReactionReport Irradiate(
            NuclearContext context, Nuclide target, ulong neutrons, double energyEv, double fissionProbability)
        {
            ReactionReport report;
            NuclearNative.Check(NuclearNative.nuc_sample_irradiate(
                Require(context), Handle, target.Key, neutrons, energyEv, fissionProbability, out report));
            return report;
        }

        /// <summary>
        /// Sends neutrons at <paramref name="target"/> using the authored fission
        /// probability for it. Throws if the target is not in the uranium and
        /// plutonium table; use the overload that takes a probability for others.
        /// </summary>
        public ReactionReport Irradiate(NuclearContext context, Nuclide target, ulong neutrons, double energyEv)
        {
            double p;
            if (!NuclearData.TryGetNeutronBranching(target, energyEv, out p))
            {
                throw new NuclearException(
                    NuclearStatus.NotInTable,
                    target + " is not in the neutron branching table. Pass a fission probability.");
            }
            return Irradiate(context, target, neutrons, energyEv, p);
        }

        /// <summary>
        /// Fuses <paramref name="events"/> pairs of two light nuclei at a
        /// temperature in keV. Each event uses up one atom of each, or two atoms
        /// when the nuclei are identical.
        /// </summary>
        public ReactionReport Fuse(NuclearContext context, Nuclide a, Nuclide b, ulong events, double temperatureKev)
        {
            ReactionReport report;
            NuclearNative.Check(NuclearNative.nuc_sample_fuse(
                Require(context), Handle, a.Key, b.Key, events, temperatureKev, out report));
            return report;
        }

        private static IntPtr Require(NuclearContext context)
        {
            if (context == null)
            {
                throw new ArgumentNullException("context");
            }
            return context.Handle;
        }

        /// <summary>Frees the native pile. Safe to call more than once.</summary>
        public void Dispose()
        {
            Release();
            GC.SuppressFinalize(this);
        }

        private void Release()
        {
            IntPtr h = handle;
            handle = IntPtr.Zero;
            if (h != IntPtr.Zero)
            {
                NuclearNative.nuc_sample_destroy(h);
            }
        }
    }
}
