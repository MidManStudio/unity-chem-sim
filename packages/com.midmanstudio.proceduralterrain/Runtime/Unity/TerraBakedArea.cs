// ============================================================================
// NOTICE: Full documentation, design decisions, and fix history for this file
// live in docs/com.midmanstudio.proceduralterrain.md, section "Runtime/Unity/TerraBakedArea.cs"
// ============================================================================
using UnityEngine;

namespace MidManStudio.ProceduralTerrain
{
    /// <summary>
    /// Sits on the root of an area the editor baked into a scene, and remembers what it was made from, so the same area
    /// can be baked again after the profile changes. It does nothing at run time.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class TerraBakedArea : MonoBehaviour
    {
        public TerraWorldProfile profile;
        public int seed;
        public int minChunkX;
        public int minChunkZ;
        public int maxChunkX;
        public int maxChunkZ;
        public int step = 1;
    }
}
