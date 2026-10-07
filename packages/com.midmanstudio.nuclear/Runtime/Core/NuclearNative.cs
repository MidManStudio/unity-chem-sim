// ============================================================================
// NOTICE: Full documentation, design decisions, and fix history for this file
// live in docs/com.midmanstudio.nuclear.md, section "Runtime/Core/NuclearNative.cs"
// ============================================================================
using System;
using System.Runtime.InteropServices;
using System.Text;

namespace MidManStudio.Nuclear
{
    /// <summary>
    /// Every P/Invoke binding to nuclear_core, and the helpers that turn the
    /// native buffer protocols into arrays and strings. Nothing else in the
    /// package uses DllImport directly.
    /// </summary>
    internal static class NuclearNative
    {
        // iOS and WebGL link the static library into the player, so the symbols
        // are looked up in the executable itself. Everywhere else it is a
        // dynamic library named nuclear_core.
#if (UNITY_IOS || UNITY_WEBGL) && !UNITY_EDITOR
        public const string Lib = "__Internal";
#else
        public const string Lib = "nuclear_core";
#endif

        /// <summary>The C interface version this file was written against.</summary>
        public const int ExpectedAbiVersion = 1;

        // Struct kinds, as in the native NUC_KIND_* constants.
        public const int KindCount = 1;
        public const int KindAmount = 2;
        public const int KindEnergy = 3;
        public const int KindBinding = 4;
        public const int KindHalfLife = 5;
        public const int KindTotals = 6;
        public const int KindStepReport = 7;
        public const int KindAmountsStepReport = 8;
        public const int KindReactionReport = 9;
        public const int KindAmountsReactionReport = 10;
        public const int KindDecayOutcome = 11;
        public const int KindChainStep = 12;
        public const int KindChainEnd = 13;
        public const int KindFissionSummary = 14;
        public const int KindFissionChannel = 15;
        public const int KindFusionChannel = 16;

        // ---- Library ----------------------------------------------------------
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern int nuc_abi_version();

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern int nuc_library_version([Out] byte[] buffer, int capacity);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern int nuc_last_error([Out] byte[] buffer, int capacity);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern int nuc_struct_size(int kind);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern int nuc_layout_probe(int kind, [Out] byte[] buffer, int capacity);

        // ---- Context ----------------------------------------------------------
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr nuc_context_create(ulong seed);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern void nuc_context_destroy(IntPtr context);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern int nuc_context_reseed(IntPtr context, ulong seed);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern int nuc_context_rng_state(IntPtr context, [Out] ulong[] words);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern int nuc_context_set_rng_state(IntPtr context, [In] ulong[] words);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern int nuc_context_clear_caches(IntPtr context);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern int nuc_context_cache_sizes(IntPtr context, [Out] int[] sizes);

        // ---- Nuclides and masses ----------------------------------------------
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern int nuc_nuclide_parse([In] byte[] text, int length, out uint key);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern int nuc_nuclide_name(uint key, [Out] byte[] buffer, int capacity);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern int nuc_element_symbol(int z, [Out] byte[] buffer, int capacity);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern int nuc_table_count();

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern int nuc_table_keys([Out] uint[] keys, int capacity, out int count);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern int nuc_mass_excess(uint key, out EnergyValue energy);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern int nuc_binding_energy(uint key, out BindingValue binding);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern int nuc_q_value(
            [In] uint[] reactants, int reactantCount, [In] uint[] products, int productCount, out EnergyValue energy);

        // ---- Decay ------------------------------------------------------------
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern int nuc_half_life(uint key, out HalfLife halfLife);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern int nuc_abundance(uint key, out double percent);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern int nuc_decay_outcomes(uint key, [Out] DecayOutcome[] outcomes, int capacity, out int count);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern int nuc_decay_q_value(uint key, int mode, uint cluster, out EnergyValue energy);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern int nuc_chain_to_stability(
            uint key, [Out] ChainStep[] steps, int capacity, out int count, out ChainEnd end);

        // ---- Neutron absorption ----------------------------------------------
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern int nuc_neutron_branching(uint key, double energyEv, out double fissionProbability);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern int nuc_capture_q_kev(uint key, out double qKev);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern int nuc_neutron_table([Out] uint[] keys, int capacity, out int count);

        // ---- Fission ----------------------------------------------------------
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern uint nuc_fissioning_nucleus(uint key);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern int nuc_fission_summary(IntPtr context, uint key, double energyEv, out FissionSummary summary);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern int nuc_fission_channels(
            IntPtr context, uint key, double energyEv, [Out] FissionChannel[] channels, int capacity, out int count);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern int nuc_fission_yields(
            IntPtr context, uint key, double energyEv, [Out] NuclideAmount[] yields, int capacity, out int count);

        // ---- Fusion -----------------------------------------------------------
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern int nuc_fusion_reaction_count();

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern int nuc_fusion_reaction(int index, out FusionChannel channel);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern int nuc_fusion_name(int index, [Out] byte[] buffer, int capacity);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern int nuc_fusion_reactivity(int index, double temperatureKev, out double cm3PerSecond);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern int nuc_fusion_channels_for(uint a, uint b, [Out] int[] indices, int capacity, out int count);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern int nuc_fusion_branching(
            uint a, uint b, double temperatureKev, [Out] double[] shares, int capacity, out int count);

        // ---- Sample (whole atoms) --------------------------------------------
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr nuc_sample_create();

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern void nuc_sample_destroy(IntPtr sample);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr nuc_sample_clone(IntPtr sample);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern int nuc_sample_add(IntPtr sample, uint key, ulong count);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern int nuc_sample_count(IntPtr sample, uint key, out ulong count);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern int nuc_sample_counts(IntPtr sample, [Out] NuclideCount[] counts, int capacity, out int count);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern int nuc_sample_pending(IntPtr sample, [Out] NuclideCount[] counts, int capacity, out int count);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern int nuc_sample_elapsed_seconds(IntPtr sample, out double seconds);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern int nuc_sample_totals(IntPtr sample, out PileTotals totals);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern int nuc_sample_advance(IntPtr context, IntPtr sample, double seconds, out StepReport report);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern int nuc_sample_resolve_fissions(IntPtr context, IntPtr sample, out ReactionReport report);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern int nuc_sample_irradiate(
            IntPtr context, IntPtr sample, uint target, ulong neutrons, double energyEv,
            double fissionProbability, out ReactionReport report);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern int nuc_sample_fuse(
            IntPtr context, IntPtr sample, uint a, uint b, ulong events, double temperatureKev, out ReactionReport report);

        // ---- Amounts (fractional) --------------------------------------------
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr nuc_amounts_create();

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern void nuc_amounts_destroy(IntPtr amounts);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr nuc_amounts_clone(IntPtr amounts);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern int nuc_amounts_add(IntPtr amounts, uint key, double amount);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern int nuc_amounts_amount(IntPtr amounts, uint key, out double amount);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern int nuc_amounts_list(IntPtr amounts, [Out] NuclideAmount[] list, int capacity, out int count);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern int nuc_amounts_pending(IntPtr amounts, [Out] NuclideAmount[] list, int capacity, out int count);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern int nuc_amounts_elapsed_seconds(IntPtr amounts, out double seconds);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern int nuc_amounts_baryon_number(IntPtr amounts, out double baryons);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern int nuc_amounts_advance(IntPtr context, IntPtr amounts, double seconds, out AmountsStepReport report);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern int nuc_amounts_resolve_fissions(IntPtr context, IntPtr amounts, out AmountsReactionReport report);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern int nuc_amounts_irradiate(
            IntPtr context, IntPtr amounts, uint target, double neutrons, double energyEv,
            double fissionProbability, out AmountsReactionReport report);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern int nuc_amounts_fuse(
            IntPtr amounts, uint a, uint b, double events, double temperatureKev, out AmountsReactionReport report);

        // ---- Helpers ----------------------------------------------------------

        /// <summary>A native call that fills a buffer and reports the total count.</summary>
        public delegate int ListCall<T>(T[] buffer, int capacity, out int count);

        /// <summary>A native call that writes text and returns its length.</summary>
        public delegate int TextCall(byte[] buffer, int capacity);

        /// <summary>Throws a <see cref="NuclearException"/> if <paramref name="status"/> is an error.</summary>
        public static void Check(int status)
        {
            if (status >= 0)
            {
                return;
            }
            throw new NuclearException((NuclearStatus)status, LastError());
        }

        /// <summary>The description of the last native failure on this thread.</summary>
        public static string LastError()
        {
            int n = nuc_last_error(null, 0);
            if (n <= 0)
            {
                return string.Empty;
            }
            byte[] buffer = new byte[n + 1];
            int m = nuc_last_error(buffer, buffer.Length);
            return Encoding.UTF8.GetString(buffer, 0, Math.Min(m, n));
        }

        /// <summary>Reads a list with the size-then-fill protocol.</summary>
        public static T[] ReadList<T>(ListCall<T> call)
        {
            int count;
            Check(call(null, 0, out count));
            if (count == 0)
            {
                return Array.Empty<T>();
            }
            T[] buffer = new T[count];
            int again;
            Check(call(buffer, buffer.Length, out again));
            if (again > buffer.Length)
            {
                buffer = new T[again];
                Check(call(buffer, buffer.Length, out again));
            }
            if (again < buffer.Length)
            {
                Array.Resize(ref buffer, again);
            }
            return buffer;
        }

        /// <summary>Reads text with the size-then-fill protocol.</summary>
        public static string ReadText(TextCall call)
        {
            int n = call(null, 0);
            if (n < 0)
            {
                Check(n);
            }
            if (n == 0)
            {
                return string.Empty;
            }
            byte[] buffer = new byte[n + 1];
            int m = call(buffer, buffer.Length);
            if (m < 0)
            {
                Check(m);
            }
            return Encoding.UTF8.GetString(buffer, 0, Math.Min(m, n));
        }
    }

    /// <summary>
    /// Checks that the native library and this package agree before anything
    /// else uses it. Every public entry point calls <see cref="Initialize"/>,
    /// so calling it yourself is optional.
    /// </summary>
    public static class NuclearLibrary
    {
        private static readonly object Gate = new object();
        private static bool initialized;

        /// <summary>The C interface version this package expects.</summary>
        public static int ExpectedAbiVersion
        {
            get { return NuclearNative.ExpectedAbiVersion; }
        }

        /// <summary>The version of the native library, such as 0.0.1. Loads the library.</summary>
        public static string Version
        {
            get { return NuclearNative.ReadText(NuclearNative.nuc_library_version); }
        }

        /// <summary>
        /// Loads the library once and checks its interface version and the size
        /// of every struct. Throws <see cref="InvalidOperationException"/> if a
        /// stale or mismatched binary is installed.
        /// </summary>
        public static void Initialize()
        {
            if (initialized)
            {
                return;
            }
            lock (Gate)
            {
                if (initialized)
                {
                    return;
                }
                int abi = NuclearNative.nuc_abi_version();
                if (abi != NuclearNative.ExpectedAbiVersion)
                {
                    throw new InvalidOperationException(
                        "nuclear_core reports interface version " + abi + " but this package expects "
                        + NuclearNative.ExpectedAbiVersion
                        + ". The native library in Runtime/Plugins/Native is stale or from another build. "
                        + "Rebuild it with the build-nuclear-rust-libs workflow.");
                }
                CheckSize(NuclearNative.KindCount, typeof(NuclideCount));
                CheckSize(NuclearNative.KindAmount, typeof(NuclideAmount));
                CheckSize(NuclearNative.KindEnergy, typeof(EnergyValue));
                CheckSize(NuclearNative.KindBinding, typeof(BindingValue));
                CheckSize(NuclearNative.KindHalfLife, typeof(HalfLife));
                CheckSize(NuclearNative.KindTotals, typeof(PileTotals));
                CheckSize(NuclearNative.KindStepReport, typeof(StepReport));
                CheckSize(NuclearNative.KindAmountsStepReport, typeof(AmountsStepReport));
                CheckSize(NuclearNative.KindReactionReport, typeof(ReactionReport));
                CheckSize(NuclearNative.KindAmountsReactionReport, typeof(AmountsReactionReport));
                CheckSize(NuclearNative.KindDecayOutcome, typeof(DecayOutcome));
                CheckSize(NuclearNative.KindChainStep, typeof(ChainStep));
                CheckSize(NuclearNative.KindChainEnd, typeof(ChainEnd));
                CheckSize(NuclearNative.KindFissionSummary, typeof(FissionSummary));
                CheckSize(NuclearNative.KindFissionChannel, typeof(FissionChannel));
                CheckSize(NuclearNative.KindFusionChannel, typeof(FusionChannel));
                initialized = true;
            }
        }

        private static void CheckSize(int kind, Type type)
        {
            int native = NuclearNative.nuc_struct_size(kind);
            int managed = Marshal.SizeOf(type);
            if (native != managed)
            {
                throw new InvalidOperationException(
                    "Struct " + type.Name + " is " + managed + " bytes in C# and " + native
                    + " bytes in nuclear_core. The package and the native library are out of step.");
            }
        }
    }
}
