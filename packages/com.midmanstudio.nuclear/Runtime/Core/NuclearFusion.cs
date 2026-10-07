// ============================================================================
// NOTICE: Full documentation, design decisions, and fix history for this file
// live in docs/com.midmanstudio.nuclear.md, section "Runtime/Core/NuclearFusion.cs"
// ============================================================================
namespace MidManStudio.Nuclear
{
    /// <summary>
    /// Fusion channels between light nuclei and their thermal reactivity.
    /// Channels are identified by an index from 0 to <see cref="ReactionCount"/>
    /// minus one, in a fixed order.
    /// </summary>
    public static class NuclearFusion
    {
        /// <summary>Number of fusion channels.</summary>
        public static int ReactionCount
        {
            get
            {
                NuclearLibrary.Initialize();
                return NuclearNative.nuc_fusion_reaction_count();
            }
        }

        /// <summary>The description of channel <paramref name="index"/>.</summary>
        public static FusionChannel GetReaction(int index)
        {
            NuclearLibrary.Initialize();
            FusionChannel channel;
            NuclearNative.Check(NuclearNative.nuc_fusion_reaction(index, out channel));
            return channel;
        }

        /// <summary>The name of channel <paramref name="index"/>, such as <c>D + T -&gt; He-4 + n</c>.</summary>
        public static string GetName(int index)
        {
            NuclearLibrary.Initialize();
            return NuclearNative.ReadText((b, c) => NuclearNative.nuc_fusion_name(index, b, c));
        }

        /// <summary>
        /// The thermal reactivity of channel <paramref name="index"/> in cm^3/s at a
        /// temperature in keV. Outside the published range of a Bosch and Hale fit
        /// it is an extrapolation. For identical reactants the reaction rate
        /// density is n squared times the reactivity over two.
        /// </summary>
        public static double GetReactivity(int index, double temperatureKev)
        {
            NuclearLibrary.Initialize();
            double value;
            NuclearNative.Check(NuclearNative.nuc_fusion_reactivity(index, temperatureKev, out value));
            return value;
        }

        /// <summary>The indices of the channels for a pair of nuclei, in either order. Empty when the pair has none.</summary>
        public static int[] GetChannelsFor(Nuclide a, Nuclide b)
        {
            NuclearLibrary.Initialize();
            uint ka = a.Key;
            uint kb = b.Key;
            return NuclearNative.ReadList<int>(
                (int[] buffer, int capacity, out int count) =>
                    NuclearNative.nuc_fusion_channels_for(ka, kb, buffer, capacity, out count));
        }

        /// <summary>
        /// The share of fusion events that take each channel of a pair at a
        /// temperature, in the order of <see cref="GetChannelsFor"/>. The shares
        /// sum to 1, or are all 0 when no channel runs.
        /// </summary>
        public static double[] GetBranching(Nuclide a, Nuclide b, double temperatureKev)
        {
            NuclearLibrary.Initialize();
            uint ka = a.Key;
            uint kb = b.Key;
            return NuclearNative.ReadList<double>(
                (double[] buffer, int capacity, out int count) =>
                    NuclearNative.nuc_fusion_branching(ka, kb, temperatureKev, buffer, capacity, out count));
        }
    }
}
