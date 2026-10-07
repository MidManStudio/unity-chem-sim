// ============================================================================
// NOTICE: Full documentation, design decisions, and fix history for this file
// live in docs/com.midmanstudio.nuclear.md, section "Runtime/Core/NuclearContext.cs"
// ============================================================================
using System;

namespace MidManStudio.Nuclear
{
    /// <summary>
    /// The state shared between piles: the random number generator and the
    /// decay and fission caches. Create one per simulation and pass it to the
    /// piles. Not thread safe: use one context from one thread at a time.
    /// </summary>
    public sealed class NuclearContext : IDisposable
    {
        private IntPtr handle;

        /// <summary>Creates a context whose random stream starts from <paramref name="seed"/>.</summary>
        public NuclearContext(ulong seed)
        {
            NuclearLibrary.Initialize();
            handle = NuclearNative.nuc_context_create(seed);
            if (handle == IntPtr.Zero)
            {
                throw new InvalidOperationException("nuclear_core could not create a context.");
            }
        }

        /// <summary>Frees the native context if <see cref="Dispose"/> was not called.</summary>
        ~NuclearContext()
        {
            Release();
        }

        internal IntPtr Handle
        {
            get
            {
                if (handle == IntPtr.Zero)
                {
                    throw new ObjectDisposedException("NuclearContext");
                }
                return handle;
            }
        }

        /// <summary>Restarts the random stream from <paramref name="seed"/>.</summary>
        public void Reseed(ulong seed)
        {
            NuclearNative.Check(NuclearNative.nuc_context_reseed(Handle, seed));
        }

        /// <summary>
        /// The four 64-bit words of the generator state. Save them with a game
        /// and assign them back to continue the same random stream.
        /// </summary>
        public ulong[] RngState
        {
            get
            {
                ulong[] words = new ulong[4];
                NuclearNative.Check(NuclearNative.nuc_context_rng_state(Handle, words));
                return words;
            }
            set
            {
                if (value == null || value.Length != 4)
                {
                    throw new ArgumentException("The generator state is four 64-bit words.", "value");
                }
                NuclearNative.Check(NuclearNative.nuc_context_set_rng_state(Handle, value));
            }
        }

        /// <summary>Empties the decay and fission caches. Results do not change, the next calls just take longer.</summary>
        public void ClearCaches()
        {
            NuclearNative.Check(NuclearNative.nuc_context_clear_caches(Handle));
        }

        /// <summary>Number of cached decay transitions.</summary>
        public int CachedTransitions
        {
            get { return ReadCacheSizes()[0]; }
        }

        /// <summary>Number of cached fission outcome lists.</summary>
        public int CachedFissionTables
        {
            get { return ReadCacheSizes()[1]; }
        }

        private int[] ReadCacheSizes()
        {
            int[] sizes = new int[2];
            NuclearNative.Check(NuclearNative.nuc_context_cache_sizes(Handle, sizes));
            return sizes;
        }

        /// <summary>
        /// The summary of the fission outcomes of a nucleus. A neutron energy of
        /// 0 means spontaneous fission of <paramref name="nuclide"/>. A positive
        /// energy in eV means a neutron absorbed by the target <paramref name="nuclide"/>.
        /// </summary>
        public FissionSummary GetFissionSummary(Nuclide nuclide, double neutronEnergyEv = 0.0)
        {
            FissionSummary summary;
            NuclearNative.Check(NuclearNative.nuc_fission_summary(Handle, nuclide.Key, neutronEnergyEv, out summary));
            return summary;
        }

        /// <summary>The channels of the fission outcomes, ordered by light then heavy fragment. Probabilities sum to 1.</summary>
        public FissionChannel[] GetFissionChannels(Nuclide nuclide, double neutronEnergyEv = 0.0)
        {
            IntPtr h = Handle;
            uint key = nuclide.Key;
            return NuclearNative.ReadList<FissionChannel>(
                (FissionChannel[] buffer, int capacity, out int count) =>
                    NuclearNative.nuc_fission_channels(h, key, neutronEnergyEv, buffer, capacity, out count));
        }

        /// <summary>The yield of each fission product per fission, in ascending nuclide order. The yields sum to 2.</summary>
        public NuclideAmount[] GetFissionYields(Nuclide nuclide, double neutronEnergyEv = 0.0)
        {
            IntPtr h = Handle;
            uint key = nuclide.Key;
            return NuclearNative.ReadList<NuclideAmount>(
                (NuclideAmount[] buffer, int capacity, out int count) =>
                    NuclearNative.nuc_fission_yields(h, key, neutronEnergyEv, buffer, capacity, out count));
        }

        /// <summary>Frees the native context. Safe to call more than once.</summary>
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
                NuclearNative.nuc_context_destroy(h);
            }
        }
    }
}
