// ============================================================================
// NOTICE: Full documentation, design decisions, and fix history for this file
// live in docs/com.midmanstudio.nuclear.md, section "Runtime/Core/NuclearAmounts.cs"
// ============================================================================
using System;

namespace MidManStudio.Nuclear
{
    /// <summary>
    /// A pile of fractional amounts. It gives the exact expected result of
    /// every step instead of one random draw, so it suits large quantities and
    /// smooth curves. Not thread safe.
    /// </summary>
    public sealed class NuclearAmounts : IDisposable
    {
        private IntPtr handle;

        /// <summary>Creates an empty pile.</summary>
        public NuclearAmounts()
        {
            NuclearLibrary.Initialize();
            handle = NuclearNative.nuc_amounts_create();
            if (handle == IntPtr.Zero)
            {
                throw new InvalidOperationException("nuclear_core could not create an amounts pile.");
            }
        }

        private NuclearAmounts(IntPtr existing)
        {
            handle = existing;
        }

        /// <summary>Frees the native pile if <see cref="Dispose"/> was not called.</summary>
        ~NuclearAmounts()
        {
            Release();
        }

        private IntPtr Handle
        {
            get
            {
                if (handle == IntPtr.Zero)
                {
                    throw new ObjectDisposedException("NuclearAmounts");
                }
                return handle;
            }
        }

        /// <summary>A copy of the pile.</summary>
        public NuclearAmounts Clone()
        {
            IntPtr copy = NuclearNative.nuc_amounts_clone(Handle);
            if (copy == IntPtr.Zero)
            {
                throw new InvalidOperationException("nuclear_core could not copy the amounts pile.");
            }
            return new NuclearAmounts(copy);
        }

        /// <summary>Adds an amount of a nuclide. The amount must be finite and not negative.</summary>
        public void Add(Nuclide nuclide, double amount)
        {
            NuclearNative.Check(NuclearNative.nuc_amounts_add(Handle, nuclide.Key, amount));
        }

        /// <summary>The amount of a nuclide.</summary>
        public double Amount(Nuclide nuclide)
        {
            double amount;
            NuclearNative.Check(NuclearNative.nuc_amounts_amount(Handle, nuclide.Key, out amount));
            return amount;
        }

        /// <summary>Every nuclide in the pile with its amount, in ascending nuclide order.</summary>
        public NuclideAmount[] GetAmounts()
        {
            IntPtr h = Handle;
            return NuclearNative.ReadList<NuclideAmount>(
                (NuclideAmount[] buffer, int capacity, out int count) =>
                    NuclearNative.nuc_amounts_list(h, buffer, capacity, out count));
        }

        /// <summary>The amounts waiting for fission products, keyed by the nucleus that is waiting.</summary>
        public NuclideAmount[] GetPendingFissions()
        {
            IntPtr h = Handle;
            return NuclearNative.ReadList<NuclideAmount>(
                (NuclideAmount[] buffer, int capacity, out int count) =>
                    NuclearNative.nuc_amounts_pending(h, buffer, capacity, out count));
        }

        /// <summary>Simulated seconds since the pile was created.</summary>
        public double ElapsedSeconds
        {
            get
            {
                double seconds;
                NuclearNative.Check(NuclearNative.nuc_amounts_elapsed_seconds(Handle, out seconds));
                return seconds;
            }
        }

        /// <summary>The nucleon count, pending fissions included.</summary>
        public double BaryonNumber
        {
            get
            {
                double baryons;
                NuclearNative.Check(NuclearNative.nuc_amounts_baryon_number(Handle, out baryons));
                return baryons;
            }
        }

        /// <summary>Advances the pile by <paramref name="seconds"/> with the exact expected change.</summary>
        public AmountsStepReport Advance(NuclearContext context, double seconds)
        {
            AmountsStepReport report;
            NuclearNative.Check(NuclearNative.nuc_amounts_advance(Require(context), Handle, seconds, out report));
            return report;
        }

        /// <summary>Turns every pending fission into the expected fission products.</summary>
        public AmountsReactionReport ResolveFissions(NuclearContext context)
        {
            AmountsReactionReport report;
            NuclearNative.Check(NuclearNative.nuc_amounts_resolve_fissions(Require(context), Handle, out report));
            return report;
        }

        /// <summary>Sends an amount of neutrons at a target and applies the expected result.</summary>
        public AmountsReactionReport Irradiate(
            NuclearContext context, Nuclide target, double neutrons, double energyEv, double fissionProbability)
        {
            AmountsReactionReport report;
            NuclearNative.Check(NuclearNative.nuc_amounts_irradiate(
                Require(context), Handle, target.Key, neutrons, energyEv, fissionProbability, out report));
            return report;
        }

        /// <summary>Fuses an amount of pairs of two light nuclei at a temperature in keV and applies the expected result.</summary>
        public AmountsReactionReport Fuse(Nuclide a, Nuclide b, double events, double temperatureKev)
        {
            AmountsReactionReport report;
            NuclearNative.Check(NuclearNative.nuc_amounts_fuse(
                Handle, a.Key, b.Key, events, temperatureKev, out report));
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
                NuclearNative.nuc_amounts_destroy(h);
            }
        }
    }
}
