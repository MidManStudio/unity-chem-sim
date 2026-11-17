// ============================================================================
// NOTICE: Full documentation, design decisions, and fix history for this file
// live in docs/com.midmanstudio.proceduralterrain.md, section "Runtime/Unity/TerraMaterials.cs"
// ============================================================================
using UnityEngine;

namespace MidManStudio.ProceduralTerrain
{
    /// <summary>Finds the shader of the package and makes a stand in material when a profile has none.</summary>
    public static class TerraMaterials
    {
        public const string VertexColorShader = "MidManStudio/ProceduralTerrain/Vertex Color Lit";

        /// <summary>
        /// The shader of the package, or the first of a few common shaders that exists. Shader.Find reaches every shader in
        /// the project in the editor, but in a player only those a material or the Always Included list pulls in, so a build
        /// should use a material asset and not rely on this.
        /// </summary>
        public static Shader FindShader()
        {
            string[] names = { VertexColorShader, "Universal Render Pipeline/Lit", "Standard" };
            for (int i = 0; i < names.Length; i++)
            {
                Shader s = Shader.Find(names[i]);
                if (s != null)
                {
                    return s;
                }
            }
            return null;
        }

        /// <summary>A new material from <see cref="FindShader"/>, or null when none is found.</summary>
        public static Material Create()
        {
            Shader s = FindShader();
            return s == null ? null : new Material(s);
        }
    }
}
