// ============================================================================
// NOTICE: Full documentation, design decisions, and fix history for this file
// live in docs/com.midmanstudio.nuclear.md, section "Runtime/Core/NuclearData.cs"
// ============================================================================
using System;

namespace MidManStudio.Nuclear
{
    /// <summary>A decay chain: the most likely decay followed until it stops.</summary>
    public sealed class DecayChain
    {
        /// <summary>The nuclide the chain starts from.</summary>
        public Nuclide Start;
        /// <summary>The steps, in order.</summary>
        public ChainStep[] Steps;
        /// <summary>Where the chain stopped.</summary>
        public ChainEnd End;
    }

    /// <summary>
    /// Data queries that need no pile: names, masses, binding energies,
    /// reaction energies, decay data and neutron branching. All of them throw
    /// <see cref="NuclearException"/> when the native call fails.
    /// </summary>
    public static class NuclearData
    {
        /// <summary>Number of nuclides in the AME2020 mass table.</summary>
        public static int NuclideCount
        {
            get
            {
                NuclearLibrary.Initialize();
                return NuclearNative.nuc_table_count();
            }
        }

        /// <summary>Every nuclide in the mass table, in ascending order.</summary>
        public static Nuclide[] AllNuclides()
        {
            NuclearLibrary.Initialize();
            uint[] keys = NuclearNative.ReadList<uint>(NuclearNative.nuc_table_keys);
            Nuclide[] result = new Nuclide[keys.Length];
            for (int i = 0; i < keys.Length; i++)
            {
                result[i] = Nuclide.FromKey(keys[i]);
            }
            return result;
        }

        /// <summary>The symbol of the element with <paramref name="z"/> protons, 1 to 118.</summary>
        public static string ElementSymbol(int z)
        {
            NuclearLibrary.Initialize();
            return NuclearNative.ReadText((b, c) => NuclearNative.nuc_element_symbol(z, b, c));
        }

        /// <summary>The atomic mass excess. Nuclides outside the table use the liquid drop fallback.</summary>
        public static EnergyValue GetMassExcess(Nuclide nuclide)
        {
            NuclearLibrary.Initialize();
            EnergyValue value;
            NuclearNative.Check(NuclearNative.nuc_mass_excess(nuclide.Key, out value));
            return value;
        }

        /// <summary>The total and per-nucleon binding energy.</summary>
        public static BindingValue GetBindingEnergy(Nuclide nuclide)
        {
            NuclearLibrary.Initialize();
            BindingValue value;
            NuclearNative.Check(NuclearNative.nuc_binding_energy(nuclide.Key, out value));
            return value;
        }

        /// <summary>
        /// The energy a reaction releases: mass excess in minus mass excess out.
        /// Protons and nucleons must balance, or the call throws.
        /// </summary>
        public static EnergyValue GetQValue(Nuclide[] reactants, Nuclide[] products)
        {
            if (reactants == null)
            {
                throw new ArgumentNullException("reactants");
            }
            if (products == null)
            {
                throw new ArgumentNullException("products");
            }
            NuclearLibrary.Initialize();
            EnergyValue value;
            NuclearNative.Check(NuclearNative.nuc_q_value(
                Keys(reactants), reactants.Length, Keys(products), products.Length, out value));
            return value;
        }

        /// <summary>The ground-state half-life, or false for a nuclide with no decay data.</summary>
        public static bool TryGetHalfLife(Nuclide nuclide, out HalfLife halfLife)
        {
            NuclearLibrary.Initialize();
            int status = NuclearNative.nuc_half_life(nuclide.Key, out halfLife);
            if (status == (int)NuclearStatus.NotInTable)
            {
                return false;
            }
            NuclearNative.Check(status);
            return true;
        }

        /// <summary>The ground-state half-life.</summary>
        /// <exception cref="NuclearException">The nuclide has no decay data.</exception>
        public static HalfLife GetHalfLife(Nuclide nuclide)
        {
            NuclearLibrary.Initialize();
            HalfLife halfLife;
            NuclearNative.Check(NuclearNative.nuc_half_life(nuclide.Key, out halfLife));
            return halfLife;
        }

        /// <summary>Natural isotopic abundance in percent, or 0 when the nuclide is not found in nature.</summary>
        public static double GetAbundancePercent(Nuclide nuclide)
        {
            NuclearLibrary.Initialize();
            double percent;
            NuclearNative.Check(NuclearNative.nuc_abundance(nuclide.Key, out percent));
            return percent;
        }

        /// <summary>The ways a nuclide decays, as a distribution that sums to 100 percent. Empty for a stable nuclide.</summary>
        public static DecayOutcome[] GetDecayOutcomes(Nuclide nuclide)
        {
            NuclearLibrary.Initialize();
            uint key = nuclide.Key;
            return NuclearNative.ReadList<DecayOutcome>(
                (DecayOutcome[] buffer, int capacity, out int count) =>
                    NuclearNative.nuc_decay_outcomes(key, buffer, capacity, out count));
        }

        /// <summary>The energy released by one decay mode of a nuclide.</summary>
        /// <param name="nuclide">The decaying nuclide.</param>
        /// <param name="mode">The mode.</param>
        /// <param name="cluster">The emitted cluster, required when <paramref name="mode"/> is <see cref="DecayMode.Cluster"/>.</param>
        public static EnergyValue GetDecayQValue(Nuclide nuclide, DecayMode mode, Nuclide? cluster = null)
        {
            NuclearLibrary.Initialize();
            EnergyValue value;
            uint clusterKey = cluster.HasValue ? cluster.Value.Key : Nuclide.NoKey;
            NuclearNative.Check(NuclearNative.nuc_decay_q_value(nuclide.Key, (int)mode, clusterKey, out value));
            return value;
        }

        /// <summary>Follows the most likely decay of a nuclide until it is stable or the chain cannot go on.</summary>
        public static DecayChain GetChainToStability(Nuclide nuclide)
        {
            NuclearLibrary.Initialize();
            uint key = nuclide.Key;
            ChainEnd end = default(ChainEnd);
            ChainStep[] steps = NuclearNative.ReadList<ChainStep>(
                (ChainStep[] buffer, int capacity, out int count) =>
                    NuclearNative.nuc_chain_to_stability(key, buffer, capacity, out count, out end));
            return new DecayChain { Start = nuclide, Steps = steps, End = end };
        }

        /// <summary>
        /// The authored probability that a neutron of <paramref name="energyEv"/>
        /// absorbed by <paramref name="target"/> ends in fission instead of
        /// capture. False for a target outside the small uranium and plutonium table.
        /// </summary>
        public static bool TryGetNeutronBranching(Nuclide target, double energyEv, out double fissionProbability)
        {
            NuclearLibrary.Initialize();
            int status = NuclearNative.nuc_neutron_branching(target.Key, energyEv, out fissionProbability);
            if (status == (int)NuclearStatus.NotInTable)
            {
                return false;
            }
            NuclearNative.Check(status);
            return true;
        }

        /// <summary>The energy in keV released when <paramref name="target"/> captures a neutron.</summary>
        public static double GetCaptureQKev(Nuclide target)
        {
            NuclearLibrary.Initialize();
            double q;
            NuclearNative.Check(NuclearNative.nuc_capture_q_kev(target.Key, out q));
            return q;
        }

        /// <summary>The targets in the neutron branching table.</summary>
        public static Nuclide[] GetNeutronTable()
        {
            NuclearLibrary.Initialize();
            uint[] keys = NuclearNative.ReadList<uint>(NuclearNative.nuc_neutron_table);
            Nuclide[] result = new Nuclide[keys.Length];
            for (int i = 0; i < keys.Length; i++)
            {
                result[i] = Nuclide.FromKey(keys[i]);
            }
            return result;
        }

        /// <summary>
        /// The nucleus that splits when <paramref name="waiting"/> sits on the
        /// pending fission list: the daughter for a fission that follows a beta
        /// decay, else the nuclide itself.
        /// </summary>
        public static Nuclide GetFissioningNucleus(Nuclide waiting)
        {
            NuclearLibrary.Initialize();
            return Nuclide.FromKey(NuclearNative.nuc_fissioning_nucleus(waiting.Key));
        }

        internal static uint[] Keys(Nuclide[] nuclides)
        {
            uint[] keys = new uint[nuclides.Length];
            for (int i = 0; i < nuclides.Length; i++)
            {
                keys[i] = nuclides[i].Key;
            }
            return keys;
        }
    }
}
