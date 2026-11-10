// ============================================================================
// NOTICE: Full documentation, design decisions, and fix history for this file
// live in docs/com.midmanstudio.allthingsjiggle.md, section "Runtime/Unity/JigglePreset.cs"
// ============================================================================
using UnityEngine;

namespace MidManStudio.AllThingsJiggle
{
    /// <summary>
    /// A preset kept as an asset: the values of <see cref="JigglePresetValues"/> that a chain takes when
    /// <see cref="JiggleChain.ApplyPreset(JigglePreset)"/> is called. Make one with the Create menu, start it from a built in
    /// kind in its inspector, and tune it. The built in kinds need no asset: see <see cref="JigglePresetKind"/>.
    /// </summary>
    [CreateAssetMenu(fileName = "JigglePreset", menuName = "MidManStudio/AllThingsJiggle/Jiggle Preset")]
    public sealed class JigglePreset : ScriptableObject
    {
        /// <summary>The values. A new asset starts as the hair preset.</summary>
        public JigglePresetValues values = JigglePresets.Get(JigglePresetKind.Hair);

        /// <summary>A preset that lives only in memory, with the values of a built in kind. Destroy it when done.</summary>
        public static JigglePreset Create(JigglePresetKind kind)
        {
            JigglePreset p = CreateInstance<JigglePreset>();
            p.values = JigglePresets.Get(kind);
            p.name = JigglePresets.Name(kind);
            return p;
        }
    }
}
