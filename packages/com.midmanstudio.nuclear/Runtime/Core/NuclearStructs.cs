// ============================================================================
// NOTICE: Full documentation, design decisions, and fix history for this file
// live in docs/com.midmanstudio.nuclear.md, section "Runtime/Core/NuclearStructs.cs"
// ============================================================================
// The fields of these structs are filled in by native code, so the compiler
// sees private fields that are never assigned (CS0649) or never read (CS0169).
#pragma warning disable 0649, 0169
using System.Runtime.InteropServices;

namespace MidManStudio.Nuclear
{
    /// <summary>Where an energy value came from.</summary>
    public enum EnergySource
    {
        /// <summary>AME2020 lists the value as measured.</summary>
        Measured = 0,
        /// <summary>AME2020 lists the value, taken from systematic trends.</summary>
        Estimated = 1,
        /// <summary>Not in the table: computed with the liquid drop fallback.</summary>
        LiquidDrop = 2,
    }

    /// <summary>How to read a half-life.</summary>
    public enum HalfLifeState
    {
        /// <summary>NUBASE2020 gives neither a half-life nor a limit.</summary>
        Unknown = 0,
        /// <summary>Stable, or no finite half-life has been established.</summary>
        Stable = 1,
        /// <summary>Decays by particle emission and no half-life is given.</summary>
        ParticleUnstable = 2,
        /// <summary>A measured value.</summary>
        Exact = 3,
        /// <summary>An approximate value.</summary>
        Approximate = 4,
        /// <summary>The value is a lower limit.</summary>
        AtLeast = 5,
        /// <summary>The value is an upper limit.</summary>
        AtMost = 6,
    }

    /// <summary>A way a nuclide decays. The numbers are the native decay mode codes.</summary>
    public enum DecayMode
    {
        /// <summary>Beta-minus decay.</summary>
        BetaMinus = 0,
        /// <summary>Beta-plus decay, electron capture included.</summary>
        BetaPlus = 1,
        /// <summary>Electron capture alone.</summary>
        ElectronCapture = 2,
        /// <summary>Positron emission alone.</summary>
        PositronEmission = 3,
        /// <summary>Alpha emission.</summary>
        Alpha = 4,
        /// <summary>Proton emission.</summary>
        Proton = 5,
        /// <summary>Two-proton emission.</summary>
        TwoProtons = 6,
        /// <summary>Three-proton emission.</summary>
        ThreeProtons = 7,
        /// <summary>Neutron emission.</summary>
        Neutron = 8,
        /// <summary>Two-neutron emission.</summary>
        TwoNeutrons = 9,
        /// <summary>Three-neutron emission.</summary>
        ThreeNeutrons = 10,
        /// <summary>Spontaneous fission.</summary>
        SpontaneousFission = 11,
        /// <summary>Double beta-minus decay.</summary>
        DoubleBetaMinus = 12,
        /// <summary>Double beta-plus decay.</summary>
        DoubleBetaPlus = 13,
        /// <summary>Beta-minus decay followed by one neutron.</summary>
        BetaMinusNeutron = 14,
        /// <summary>Beta-minus decay followed by two neutrons.</summary>
        BetaMinus2Neutrons = 15,
        /// <summary>Beta-minus decay followed by three neutrons.</summary>
        BetaMinus3Neutrons = 16,
        /// <summary>Beta-minus decay followed by four neutrons.</summary>
        BetaMinus4Neutrons = 17,
        /// <summary>Beta-minus decay followed by an alpha particle.</summary>
        BetaMinusAlpha = 18,
        /// <summary>Beta-minus decay followed by a deuteron.</summary>
        BetaMinusDeuteron = 19,
        /// <summary>Beta-minus decay followed by a triton.</summary>
        BetaMinusTriton = 20,
        /// <summary>Beta-minus decay followed by a proton.</summary>
        BetaMinusProton = 21,
        /// <summary>Beta-minus decay followed by fission.</summary>
        BetaMinusFission = 22,
        /// <summary>Beta-plus decay followed by one proton.</summary>
        BetaPlusProton = 23,
        /// <summary>Beta-plus decay followed by two protons.</summary>
        BetaPlus2Protons = 24,
        /// <summary>Beta-plus decay followed by three protons.</summary>
        BetaPlus3Protons = 25,
        /// <summary>Beta-plus decay followed by an alpha particle.</summary>
        BetaPlusAlpha = 26,
        /// <summary>Beta-plus decay followed by a proton and an alpha particle.</summary>
        BetaPlusProtonAlpha = 27,
        /// <summary>Beta-plus decay followed by fission.</summary>
        BetaPlusFission = 28,
        /// <summary>Emission of a heavy cluster such as carbon-14. The cluster is in the outcome.</summary>
        Cluster = 29,
        /// <summary>Two cluster modes listed under one intensity, so there is no single daughter.</summary>
        ClusterMixture = 30,
    }

    /// <summary>Why a decay chain stopped.</summary>
    public enum ChainEndReason
    {
        /// <summary>The last nuclide is stable.</summary>
        Stable = 0,
        /// <summary>The last nuclide decays by a mode with no single daughter.</summary>
        NoDaughter = 1,
        /// <summary>The last nuclide has no usable decay data.</summary>
        NoData = 2,
        /// <summary>The last nuclide is not in the table.</summary>
        NotInTable = 3,
        /// <summary>The last nuclide was already visited.</summary>
        Loop = 4,
        /// <summary>The chain reached its step limit.</summary>
        TooLong = 5,
    }

    /// <summary>Where the yields behind a set of fission outcomes came from.</summary>
    public enum YieldSource
    {
        /// <summary>The nucleus has an evaluated table of its own.</summary>
        Evaluated = 0,
        /// <summary>The table of the nearest evaluated nucleus, shifted to fit. A systematic guess.</summary>
        Extended = 1,
    }

    /// <summary>How a fusion reactivity is computed.</summary>
    public enum FitKind
    {
        /// <summary>The Bosch and Hale (1992) reactivity fit.</summary>
        BoschHale = 0,
        /// <summary>A Gamow-factor integral over an S-factor.</summary>
        Gamow = 1,
    }

    /// <summary>A nuclide and a whole number of atoms.</summary>
    [StructLayout(LayoutKind.Explicit, Size = 16)]
    public struct NuclideCount
    {
        [FieldOffset(0)] private uint key;
        /// <summary>Number of atoms.</summary>
        [FieldOffset(8)] public ulong Count;
        /// <summary>The nuclide.</summary>
        public Nuclide Nuclide => Nuclide.FromKey(key);
    }

    /// <summary>A nuclide and a fractional amount.</summary>
    [StructLayout(LayoutKind.Explicit, Size = 16)]
    public struct NuclideAmount
    {
        [FieldOffset(0)] private uint key;
        /// <summary>The amount.</summary>
        [FieldOffset(8)] public double Amount;
        /// <summary>The nuclide.</summary>
        public Nuclide Nuclide => Nuclide.FromKey(key);
    }

    /// <summary>An energy in keV and where it came from.</summary>
    [StructLayout(LayoutKind.Explicit, Size = 16)]
    public struct EnergyValue
    {
        /// <summary>Energy in keV.</summary>
        [FieldOffset(0)] public double Kev;
        [FieldOffset(8)] private int source;
        /// <summary>Where the value came from.</summary>
        public EnergySource Source => (EnergySource)source;
        /// <summary>Energy in MeV.</summary>
        public double Mev => Kev / 1000.0;
    }

    /// <summary>The binding energy of a nuclide.</summary>
    [StructLayout(LayoutKind.Explicit, Size = 24)]
    public struct BindingValue
    {
        /// <summary>Total binding energy in keV. Negative means unbound.</summary>
        [FieldOffset(0)] public double TotalKev;
        /// <summary>Binding energy per nucleon in keV.</summary>
        [FieldOffset(8)] public double PerNucleonKev;
        [FieldOffset(16)] private int source;
        /// <summary>Where the value came from.</summary>
        public EnergySource Source => (EnergySource)source;
    }

    /// <summary>A ground-state half-life.</summary>
    [StructLayout(LayoutKind.Explicit, Size = 16)]
    public struct HalfLife
    {
        /// <summary>Half-life in seconds. Zero when <see cref="State"/> carries no number.</summary>
        [FieldOffset(0)] public double Seconds;
        [FieldOffset(8)] private int state;
        [FieldOffset(12)] private byte estimated;
        /// <summary>How to read <see cref="Seconds"/>.</summary>
        public HalfLifeState State => (HalfLifeState)state;
        /// <summary>True when NUBASE2020 estimates the value from trends instead of measurement.</summary>
        public bool Estimated => estimated != 0;
        /// <summary>True when <see cref="Seconds"/> is a number.</summary>
        public bool HasNumber => State >= HalfLifeState.Exact;
    }

    /// <summary>Totals of a pile of atoms.</summary>
    [StructLayout(LayoutKind.Explicit, Size = 16)]
    public struct PileTotals
    {
        /// <summary>Number of atoms, not counting pending fissions.</summary>
        [FieldOffset(0)] public ulong Atoms;
        /// <summary>Nucleon count, pending fissions included.</summary>
        [FieldOffset(8)] public ulong Baryons;
    }

    /// <summary>What one <c>Advance</c> did to a <see cref="NuclearSample"/>.</summary>
    [StructLayout(LayoutKind.Explicit, Size = 32)]
    public struct StepReport
    {
        /// <summary>Energy released in keV (total Q).</summary>
        [FieldOffset(0)] public double EnergyReleasedKev;
        /// <summary>Atoms that are no longer the nuclide they started as.</summary>
        [FieldOffset(8)] public ulong AtomsChanged;
        /// <summary>Atoms that moved to the pending fission list.</summary>
        [FieldOffset(16)] public ulong NewPendingFissions;
        [FieldOffset(24)] private byte energyKnown;
        /// <summary>False when a mass was missing, so the energy is a lower estimate.</summary>
        public bool EnergyKnown => energyKnown != 0;
    }

    /// <summary>What one <c>Advance</c> did to a <see cref="NuclearAmounts"/>.</summary>
    [StructLayout(LayoutKind.Explicit, Size = 32)]
    public struct AmountsStepReport
    {
        /// <summary>Energy released in keV per unit of amount.</summary>
        [FieldOffset(0)] public double EnergyReleasedKev;
        /// <summary>Amount that is no longer the nuclide it started as.</summary>
        [FieldOffset(8)] public double AmountChanged;
        /// <summary>Amount that moved to the pending fission list.</summary>
        [FieldOffset(16)] public double NewPending;
        [FieldOffset(24)] private byte energyKnown;
        /// <summary>False when a mass was missing, so the energy is a lower estimate.</summary>
        public bool EnergyKnown => energyKnown != 0;
    }

    /// <summary>What one reaction call did to a <see cref="NuclearSample"/>.</summary>
    [StructLayout(LayoutKind.Explicit, Size = 56)]
    public struct ReactionReport
    {
        /// <summary>Energy released in keV (total Q, before later decay).</summary>
        [FieldOffset(0)] public double EnergyReleasedKev;
        /// <summary>Fission events.</summary>
        [FieldOffset(8)] public ulong Fissions;
        /// <summary>Neutron captures.</summary>
        [FieldOffset(16)] public ulong Captures;
        /// <summary>Fusion events.</summary>
        [FieldOffset(24)] public ulong Fusions;
        /// <summary>Free neutrons added to the pile.</summary>
        [FieldOffset(32)] public ulong NeutronsReleased;
        /// <summary>Pending fissions left waiting because the nucleus is too light to split.</summary>
        [FieldOffset(40)] public ulong Unresolved;
        [FieldOffset(48)] private byte energyKnown;
        /// <summary>False when a mass was missing, so the energy is a lower estimate.</summary>
        public bool EnergyKnown => energyKnown != 0;
    }

    /// <summary>What one reaction call did to a <see cref="NuclearAmounts"/>.</summary>
    [StructLayout(LayoutKind.Explicit, Size = 56)]
    public struct AmountsReactionReport
    {
        /// <summary>Energy released in keV per unit of amount (total Q, before later decay).</summary>
        [FieldOffset(0)] public double EnergyReleasedKev;
        /// <summary>Amount that fissioned.</summary>
        [FieldOffset(8)] public double Fissions;
        /// <summary>Amount of neutron captures.</summary>
        [FieldOffset(16)] public double Captures;
        /// <summary>Amount of fusion events.</summary>
        [FieldOffset(24)] public double Fusions;
        /// <summary>Free neutrons added to the pile.</summary>
        [FieldOffset(32)] public double NeutronsReleased;
        /// <summary>Pending fission left waiting because the nucleus is too light to split.</summary>
        [FieldOffset(40)] public double Unresolved;
        [FieldOffset(48)] private byte energyKnown;
        /// <summary>False when a mass was missing, so the energy is a lower estimate.</summary>
        public bool EnergyKnown => energyKnown != 0;
    }

    /// <summary>One way a nuclide decays.</summary>
    [StructLayout(LayoutKind.Explicit, Size = 40)]
    public struct DecayOutcome
    {
        /// <summary>Share of decays in percent.</summary>
        [FieldOffset(0)] public double Percent;
        [FieldOffset(8)] private int mode;
        [FieldOffset(12)] private uint daughter;
        [FieldOffset(16)] private uint cluster;
        [FieldOffset(20)] private byte emittedCount;
        [FieldOffset(21)] private byte assumed;
        [FieldOffset(24)] private uint emitted0;
        [FieldOffset(28)] private uint emitted1;
        [FieldOffset(32)] private uint emitted2;
        [FieldOffset(36)] private uint emitted3;

        /// <summary>The decay mode.</summary>
        public DecayMode Mode => (DecayMode)mode;
        /// <summary>True when the mode leaves a single daughter nucleus.</summary>
        public bool HasDaughter => daughter != Nuclide.NoKey;
        /// <summary>The daughter. Only meaningful when <see cref="HasDaughter"/>.</summary>
        public Nuclide Daughter => Nuclide.FromKey(daughter);
        /// <summary>True when <see cref="Mode"/> is a cluster mode with a known cluster.</summary>
        public bool HasCluster => cluster != Nuclide.NoKey;
        /// <summary>The emitted cluster. Only meaningful when <see cref="HasCluster"/>.</summary>
        public Nuclide Cluster => Nuclide.FromKey(cluster);
        /// <summary>True when NUBASE2020 gives no measured intensity and the share is assumed.</summary>
        public bool Assumed => assumed != 0;
        /// <summary>How many nuclei the decay emits besides the daughter.</summary>
        public int EmittedCount => emittedCount;

        /// <summary>The emitted nucleus at <paramref name="index"/>, below <see cref="EmittedCount"/>.</summary>
        public Nuclide Emitted(int index)
        {
            if (index < 0 || index >= emittedCount)
            {
                throw new System.ArgumentOutOfRangeException("index");
            }
            switch (index)
            {
                case 0: return Nuclide.FromKey(emitted0);
                case 1: return Nuclide.FromKey(emitted1);
                case 2: return Nuclide.FromKey(emitted2);
                default: return Nuclide.FromKey(emitted3);
            }
        }
    }

    /// <summary>One step of a decay chain.</summary>
    [StructLayout(LayoutKind.Explicit, Size = 48)]
    public struct ChainStep
    {
        [FieldOffset(0)] private uint parent;
        /// <summary>The outcome that was followed.</summary>
        [FieldOffset(8)] public DecayOutcome Outcome;
        /// <summary>The nuclide that decays in this step.</summary>
        public Nuclide Parent => Nuclide.FromKey(parent);
    }

    /// <summary>Where a decay chain stopped.</summary>
    [StructLayout(LayoutKind.Explicit, Size = 16)]
    public struct ChainEnd
    {
        [FieldOffset(0)] private uint end;
        [FieldOffset(4)] private int reason;
        [FieldOffset(8)] private int mode;
        [FieldOffset(12)] private uint cluster;
        /// <summary>The last nuclide of the chain.</summary>
        public Nuclide Nuclide => Nuclide.FromKey(end);
        /// <summary>Why the chain stopped.</summary>
        public ChainEndReason Reason => (ChainEndReason)reason;
        /// <summary>The mode with no single daughter. Only meaningful for <see cref="ChainEndReason.NoDaughter"/>.</summary>
        public DecayMode Mode => (DecayMode)mode;
    }

    /// <summary>Summary of one set of fission outcomes.</summary>
    [StructLayout(LayoutKind.Explicit, Size = 40)]
    public struct FissionSummary
    {
        /// <summary>Neutron energy in eV of the table used, or 0 for a spontaneous one.</summary>
        [FieldOffset(0)] public double ReferenceEnergyEv;
        /// <summary>Mean prompt neutrons per fission.</summary>
        [FieldOffset(8)] public double MeanNeutrons;
        /// <summary>Mean energy released per fission in keV.</summary>
        [FieldOffset(16)] public double MeanQKev;
        [FieldOffset(24)] private uint compound;
        [FieldOffset(28)] private uint reference;
        /// <summary>Number of channels.</summary>
        [FieldOffset(32)] public int ChannelCount;
        [FieldOffset(36)] private int source;
        /// <summary>The nucleus that fissions.</summary>
        public Nuclide Compound => Nuclide.FromKey(compound);
        /// <summary>The parent of the evaluated table behind the outcomes.</summary>
        public Nuclide Reference => Nuclide.FromKey(reference);
        /// <summary>Whether the table belongs to the nucleus or was borrowed.</summary>
        public YieldSource Source => (YieldSource)source;
    }

    /// <summary>One way a fission can end.</summary>
    [StructLayout(LayoutKind.Explicit, Size = 32)]
    public struct FissionChannel
    {
        /// <summary>Probability per fission.</summary>
        [FieldOffset(0)] public double Probability;
        /// <summary>Energy released in keV. Zero when <see cref="QKnown"/> is false.</summary>
        [FieldOffset(8)] public double QKev;
        [FieldOffset(16)] private uint light;
        [FieldOffset(20)] private uint heavy;
        /// <summary>Prompt neutrons released.</summary>
        [FieldOffset(24)] public byte Neutrons;
        [FieldOffset(25)] private byte qKnown;
        /// <summary>The lighter fragment.</summary>
        public Nuclide Light => Nuclide.FromKey(light);
        /// <summary>The heavier fragment.</summary>
        public Nuclide Heavy => Nuclide.FromKey(heavy);
        /// <summary>False when a mass was missing, so <see cref="QKev"/> is 0.</summary>
        public bool QKnown => qKnown != 0;
    }

    /// <summary>One fusion channel.</summary>
    [StructLayout(LayoutKind.Explicit, Size = 56)]
    public struct FusionChannel
    {
        /// <summary>Energy released in keV.</summary>
        [FieldOffset(0)] public double QKev;
        /// <summary>Lowest temperature of the published fit in keV. Zero for a Gamow channel.</summary>
        [FieldOffset(8)] public double FitLowKev;
        /// <summary>Highest temperature of the published fit in keV. Zero for a Gamow channel.</summary>
        [FieldOffset(16)] public double FitHighKev;
        [FieldOffset(24)] private uint reactantA;
        [FieldOffset(28)] private uint reactantB;
        [FieldOffset(32)] private uint productKey0;
        [FieldOffset(36)] private uint productKey1;
        [FieldOffset(40)] private uint productKey2;
        [FieldOffset(44)] private byte productCount0;
        [FieldOffset(45)] private byte productCount1;
        [FieldOffset(46)] private byte productCount2;
        [FieldOffset(47)] private byte productLength;
        [FieldOffset(48)] private byte fitKind;

        /// <summary>The first reactant.</summary>
        public Nuclide ReactantA => Nuclide.FromKey(reactantA);
        /// <summary>The second reactant.</summary>
        public Nuclide ReactantB => Nuclide.FromKey(reactantB);
        /// <summary>How the reactivity is computed.</summary>
        public FitKind Fit => (FitKind)fitKind;
        /// <summary>How many kinds of nuclei come out.</summary>
        public int ProductLength => productLength;

        /// <summary>The nucleus of product slot <paramref name="index"/>, below <see cref="ProductLength"/>.</summary>
        public Nuclide Product(int index)
        {
            if (index < 0 || index >= productLength)
            {
                throw new System.ArgumentOutOfRangeException("index");
            }
            return Nuclide.FromKey(index == 0 ? productKey0 : index == 1 ? productKey1 : productKey2);
        }

        /// <summary>How many of the nucleus in product slot <paramref name="index"/> come out.</summary>
        public int ProductCount(int index)
        {
            if (index < 0 || index >= productLength)
            {
                throw new System.ArgumentOutOfRangeException("index");
            }
            return index == 0 ? productCount0 : index == 1 ? productCount1 : productCount2;
        }
    }
}
