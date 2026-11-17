// ============================================================================
// NOTICE: Full documentation, design decisions, and fix history for this file
// live in docs/com.midmanstudio.proceduralterrain.md, section "Editor/TerraWorldProfileEditor.cs"
// ============================================================================
using System;
using UnityEditor;
using UnityEngine;

namespace MidManStudio.ProceduralTerrain.EditorTools
{
    /// <summary>The inspector of a profile: the plain fields, and the buttons that save typing numbers.</summary>
    [CustomEditor(typeof(TerraWorldProfile))]
    public sealed class TerraWorldProfileEditor : Editor
    {
        private static readonly string[] PresetNames = { "Rolling hills", "Archipelago", "Highlands", "Mesas" };

        private int preset;
        private string measured = "";

        private bool showPatch;
        private Texture2D patchTexture;
        private string patchName = "Painted patch";
        private double patchMinX;
        private double patchMinZ;
        private double patchSizeX = 200.0;
        private double patchSizeZ = 200.0;
        private float patchMinHeight;
        private float patchMaxHeight = 20f;

        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            TerraWorldProfile profile = (TerraWorldProfile)target;

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Tools");

            if (profile.material == null)
            {
                EditorGUILayout.HelpBox("No material. Use MidManStudio > ProceduralTerrain > Create Vertex Color Material with this profile selected.", MessageType.Warning);
            }

            EditorGUILayout.BeginHorizontal();
            preset = EditorGUILayout.Popup("Preset", preset, PresetNames);
            if (GUILayout.Button("Replace settings", GUILayout.Width(120f)))
            {
                if (EditorUtility.DisplayDialog("Replace settings", "This replaces every number in the world settings with the preset. Stamps, patches and scatter layers go too.", "Replace", "Cancel"))
                {
                    Undo.RecordObject(profile, "Apply terrain preset");
                    profile.settings = MakePreset(preset);
                    EditorUtility.SetDirty(profile);
                }
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Random seed"))
            {
                Undo.RecordObject(profile, "Random terrain seed");
                profile.settings.seed = new System.Random().Next(1, int.MaxValue);
                EditorUtility.SetDirty(profile);
            }
            if (GUILayout.Button("Measure noise range"))
            {
                MeasureRange(profile);
            }
            if (GUILayout.Button("Open preview"))
            {
                TerraPreviewWindow.Open(profile);
            }
            EditorGUILayout.EndHorizontal();
            if (measured.Length > 0)
            {
                EditorGUILayout.HelpBox(measured, MessageType.Info);
            }

            showPatch = EditorGUILayout.Foldout(showPatch, "Import a painted height patch");
            if (showPatch)
            {
                DrawPatchImport(profile);
            }
        }

        private static TerraWorldSettings MakePreset(int index)
        {
            switch (index)
            {
                case 1:
                    return TerraPresets.Archipelago();
                case 2:
                    return TerraPresets.Highlands();
                case 3:
                    return TerraPresets.Mesas();
                default:
                    return TerraPresets.RollingHills();
            }
        }

        // Measures where the noise of the profile spends its time and writes the practical range into the remap, so the lowest
        // and highest biomes are reached. The numbers stay fixed afterwards; nothing is measured while chunks are built.
        private void MeasureRange(TerraWorldProfile profile)
        {
            TerraNoise noise = new TerraNoise(profile.settings.seed, profile.settings.height.noise);
            double low;
            double high;
            noise.MeasureRange(0.01, 0.99, 40000, out low, out high);
            if (high - low < 1e-4)
            {
                measured = "The noise has no range to measure (" + low.ToString("0.0000") + "). Check the octaves and the scale.";
                return;
            }
            Undo.RecordObject(profile, "Measure terrain noise range");
            profile.settings.height.noise.remapMin = low;
            profile.settings.height.noise.remapMax = high;
            EditorUtility.SetDirty(profile);
            measured = "Remap set to " + low.ToString("0.000") + " .. " + high.ToString("0.000")
                + " (the range of 98 per cent of 40 000 sample points).";
        }

        private void DrawPatchImport(TerraWorldProfile profile)
        {
            patchTexture = (Texture2D)EditorGUILayout.ObjectField("Grey-scale picture", patchTexture, typeof(Texture2D), false);
            patchName = EditorGUILayout.TextField("Name", patchName);
            patchMinX = EditorGUILayout.DoubleField("South west X (m)", patchMinX);
            patchMinZ = EditorGUILayout.DoubleField("South west Z (m)", patchMinZ);
            patchSizeX = EditorGUILayout.DoubleField("Size X (m)", patchSizeX);
            patchSizeZ = EditorGUILayout.DoubleField("Size Z (m)", patchSizeZ);
            patchMinHeight = EditorGUILayout.FloatField("Black is (m)", patchMinHeight);
            patchMaxHeight = EditorGUILayout.FloatField("White is (m)", patchMaxHeight);
            EditorGUILayout.HelpBox("The top of the picture is north. The texture needs Read/Write enabled in its import settings.", MessageType.None);
            if (GUILayout.Button("Add patch to profile"))
            {
                if (patchTexture == null)
                {
                    EditorUtility.DisplayDialog("Import patch", "Pick a picture first.", "Close", "");
                    return;
                }
                try
                {
                    TerraPatch patch = TerraPatchImport.FromTexture(patchTexture, patchName, patchMinX, patchMinZ,
                        patchSizeX, patchSizeZ, patchMinHeight, patchMaxHeight);
                    Undo.RecordObject(profile, "Add terrain patch");
                    TerraPatch[] old = profile.settings.height.patches ?? new TerraPatch[0];
                    TerraPatch[] grown = new TerraPatch[old.Length + 1];
                    Array.Copy(old, grown, old.Length);
                    grown[old.Length] = patch;
                    profile.settings.height.patches = grown;
                    EditorUtility.SetDirty(profile);
                }
                catch (Exception e)
                {
                    EditorUtility.DisplayDialog("Import patch", e.Message, "Close", "");
                }
            }
        }
    }
}
