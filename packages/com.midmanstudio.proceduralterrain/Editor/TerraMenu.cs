// ============================================================================
// NOTICE: Full documentation, design decisions, and fix history for this file
// live in docs/com.midmanstudio.proceduralterrain.md, section "Editor/TerraMenu.cs"
// ============================================================================
using UnityEditor;
using UnityEngine;

namespace MidManStudio.ProceduralTerrain.EditorTools
{
    /// <summary>The menu entries of the package, all under MidManStudio/ProceduralTerrain.</summary>
    public static class TerraMenu
    {
        [MenuItem("MidManStudio/ProceduralTerrain/Preview and Bake")]
        public static void OpenPreview()
        {
            TerraPreviewWindow.Open(Selection.activeObject as TerraWorldProfile);
        }

        [MenuItem("MidManStudio/ProceduralTerrain/Create Vertex Color Material")]
        public static void CreateMaterial()
        {
            Shader shader = Shader.Find(TerraMaterials.VertexColorShader);
            if (shader == null)
            {
                EditorUtility.DisplayDialog("Terrain material", "The shader " + TerraMaterials.VertexColorShader + " was not found. Is the package imported without errors?", "Close", "");
                return;
            }
            string path = EditorUtility.SaveFilePanelInProject("Create material", "TerraVertexColor", "mat", "Where to save the material");
            if (string.IsNullOrEmpty(path))
            {
                return;
            }
            Material material = new Material(shader);
            AssetDatabase.CreateAsset(material, path);
            AssetDatabase.SaveAssets();
            TerraWorldProfile profile = Selection.activeObject as TerraWorldProfile;
            if (profile != null)
            {
                Undo.RecordObject(profile, "Set terrain material");
                profile.material = material;
                EditorUtility.SetDirty(profile);
            }
            Selection.activeObject = material;
        }

        [MenuItem("MidManStudio/ProceduralTerrain/Add Terrain Streamer")]
        public static void AddStreamer()
        {
            GameObject go = new GameObject("Terrain Streamer");
            Undo.RegisterCreatedObjectUndo(go, "Add Terrain Streamer");
            TerraStreamer streamer = go.AddComponent<TerraStreamer>();
            TerraWorldProfile profile = Selection.activeObject as TerraWorldProfile;
            if (profile != null)
            {
                streamer.profile = profile;
            }
            Selection.activeGameObject = go;
        }
    }
}
