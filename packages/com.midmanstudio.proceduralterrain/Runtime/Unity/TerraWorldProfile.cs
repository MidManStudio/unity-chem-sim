// ============================================================================
// NOTICE: Full documentation, design decisions, and fix history for this file
// live in docs/com.midmanstudio.proceduralterrain.md, section "Runtime/Unity/TerraWorldProfile.cs"
// ============================================================================
using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace MidManStudio.ProceduralTerrain
{
    /// <summary>The things that stand on one scatter layer: the prefabs, by the layer's name. A point picks one by its variant number.</summary>
    [Serializable]
    public class TerraScatterBinding
    {
        [Tooltip("The name of the scatter layer in the world settings.")]
        public string layer = "Trees";

        [Tooltip("The prefabs of the layer. A scatter point picks one by its variant number, wrapped to the length of this list.")]
        public GameObject[] variants = new GameObject[0];
    }

    /// <summary>
    /// Everything a terrain world is made from, as an asset: the settings of the Core, how far and how finely it streams, the
    /// material and the prefabs for the scatter layers. One profile makes the same world in the editor preview, in the baker
    /// and in a streamer at run time.
    /// </summary>
    [CreateAssetMenu(fileName = "TerraWorldProfile", menuName = "MidManStudio/ProceduralTerrain/World Profile")]
    public sealed class TerraWorldProfile : ScriptableObject
    {
        public TerraWorldSettings settings = TerraPresets.RollingHills();

        public TerraStreamSettings stream = new TerraStreamSettings();

        [Header("Rendering")]
        [Tooltip("A material that reads vertex colours. Create one from the MidManStudio menu: it uses the shader of this package.")]
        public Material material;
        public ShadowCastingMode shadowCasting = ShadowCastingMode.On;
        public bool receiveShadows = true;
        [Range(0, 31)]
        public int layer = 0;

        [Header("Physics")]
        public bool colliders = true;
        [Min(1)]
        [Tooltip("Chunks at a coarser level of detail than this have no collider. Making a collider is the slow part of showing a chunk, so keep it at 1 or 2.")]
        public int colliderMaxStep = 1;

        [Header("Scatter")]
        public TerraScatterBinding[] scatterBindings = new TerraScatterBinding[0];

        /// <summary>A world compiled from the settings. Build a new one after any change to them.</summary>
        public TerraWorld CreateWorld()
        {
            return new TerraWorld(settings);
        }
    }
}
