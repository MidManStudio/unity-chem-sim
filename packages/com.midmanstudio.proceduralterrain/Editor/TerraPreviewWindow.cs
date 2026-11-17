// ============================================================================
// NOTICE: Full documentation, design decisions, and fix history for this file
// live in docs/com.midmanstudio.proceduralterrain.md, section "Editor/TerraPreviewWindow.cs"
// ============================================================================
using System;
using UnityEditor;
using UnityEngine;

namespace MidManStudio.ProceduralTerrain.EditorTools
{
    public enum TerraPreviewMode
    {
        Colour = 0,
        Height = 1,
        Slope = 2
    }

    /// <summary>
    /// A top down picture of the world a profile makes, from the same height field the chunks use, and the controls to bake
    /// an area of chunks into the open scene. North is up.
    /// </summary>
    public sealed class TerraPreviewWindow : EditorWindow
    {
        private static readonly string[] ResolutionNames = { "128", "256", "512" };
        private static readonly int[] Resolutions = { 128, 256, 512 };
        private static readonly string[] StepNames = { "1 (full)", "2", "4", "8" };
        private static readonly int[] Steps = { 1, 2, 4, 8 };

        [SerializeField] private TerraWorldProfile profile;
        [SerializeField] private double centreX;
        [SerializeField] private double centreZ;
        [SerializeField] private float sizeMetres = 1024f;
        [SerializeField] private int resolutionIndex = 1;
        [SerializeField] private TerraPreviewMode mode = TerraPreviewMode.Colour;
        [SerializeField] private bool hillshade = true;
        [SerializeField] private bool showWater = true;
        [SerializeField] private bool showChunks = true;

        [SerializeField] private int bakeMinX;
        [SerializeField] private int bakeMinZ;
        [SerializeField] private int bakeMaxX = 3;
        [SerializeField] private int bakeMaxZ = 3;
        [SerializeField] private int bakeStepIndex;
        [SerializeField] private bool bakeSave = true;
        [SerializeField] private string bakeFolder = "Assets/TerraBake";
        [SerializeField] private bool bakeScatter = true;

        private Texture2D texture;
        private string status = "";
        private Vector2 scroll;

        public static void Open(TerraWorldProfile profile)
        {
            TerraPreviewWindow window = GetWindow<TerraPreviewWindow>("Terrain Preview");
            if (profile != null)
            {
                window.profile = profile;
            }
            window.Show();
        }

        private void OnDisable()
        {
            if (texture != null)
            {
                DestroyImmediate(texture);
                texture = null;
            }
        }

        private void OnGUI()
        {
            scroll = EditorGUILayout.BeginScrollView(scroll);

            profile = (TerraWorldProfile)EditorGUILayout.ObjectField("Profile", profile, typeof(TerraWorldProfile), false);
            if (profile == null)
            {
                EditorGUILayout.HelpBox("Pick a World Profile. Make one with Assets > Create > MidManStudio > ProceduralTerrain > World Profile.", MessageType.Info);
                EditorGUILayout.EndScrollView();
                return;
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Picture");
            centreX = EditorGUILayout.DoubleField("Centre X (m)", centreX);
            centreZ = EditorGUILayout.DoubleField("Centre Z (m)", centreZ);
            sizeMetres = EditorGUILayout.Slider("Width (m)", sizeMetres, 64f, 8192f);
            resolutionIndex = EditorGUILayout.Popup("Pixels", resolutionIndex, ResolutionNames);
            mode = (TerraPreviewMode)EditorGUILayout.EnumPopup("Shows", mode);
            hillshade = EditorGUILayout.Toggle("Light and shade", hillshade);
            showWater = EditorGUILayout.Toggle("Water level", showWater);
            showChunks = EditorGUILayout.Toggle("Chunk lines", showChunks);

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("West"))
            {
                centreX -= sizeMetres * 0.5;
            }
            if (GUILayout.Button("East"))
            {
                centreX += sizeMetres * 0.5;
            }
            if (GUILayout.Button("South"))
            {
                centreZ -= sizeMetres * 0.5;
            }
            if (GUILayout.Button("North"))
            {
                centreZ += sizeMetres * 0.5;
            }
            if (GUILayout.Button("Centre on 0, 0"))
            {
                centreX = 0.0;
                centreZ = 0.0;
            }
            EditorGUILayout.EndHorizontal();

            if (GUILayout.Button("Render"))
            {
                Render();
            }
            if (status.Length > 0)
            {
                EditorGUILayout.HelpBox(status, MessageType.None);
            }
            if (texture != null)
            {
                float side = Math.Min(position.width - 24f, 640f);
                Rect r = GUILayoutUtility.GetRect(side, side);
                GUI.DrawTexture(r, texture, ScaleMode.ScaleToFit);
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Bake into the open scene");
            bakeMinX = EditorGUILayout.IntField("First chunk X", bakeMinX);
            bakeMinZ = EditorGUILayout.IntField("First chunk Z", bakeMinZ);
            bakeMaxX = EditorGUILayout.IntField("Last chunk X", bakeMaxX);
            bakeMaxZ = EditorGUILayout.IntField("Last chunk Z", bakeMaxZ);
            bakeStepIndex = EditorGUILayout.Popup("Detail step", bakeStepIndex, StepNames);
            bakeSave = EditorGUILayout.Toggle("Save meshes as assets", bakeSave);
            if (bakeSave)
            {
                bakeFolder = EditorGUILayout.TextField("Folder", bakeFolder);
            }
            bakeScatter = EditorGUILayout.Toggle("Place scatter prefabs", bakeScatter);
            double chunk = profile.settings.lattice.spacing * profile.settings.lattice.cellsPerChunk;
            TerraBakeOptions options = BakeOptions();
            EditorGUILayout.LabelField("Chunks", options.ChunkCount + " of " + chunk.ToString("0") + " m");
            if (GUILayout.Button("Bake"))
            {
                Bake(options);
            }

            EditorGUILayout.EndScrollView();
        }

        private TerraBakeOptions BakeOptions()
        {
            TerraBakeOptions o = new TerraBakeOptions();
            o.minChunkX = Math.Min(bakeMinX, bakeMaxX);
            o.maxChunkX = Math.Max(bakeMinX, bakeMaxX);
            o.minChunkZ = Math.Min(bakeMinZ, bakeMaxZ);
            o.maxChunkZ = Math.Max(bakeMinZ, bakeMaxZ);
            o.step = Steps[Mathf.Clamp(bakeStepIndex, 0, Steps.Length - 1)];
            o.saveMeshes = bakeSave;
            o.folder = bakeFolder;
            o.scatter = bakeScatter;
            return o;
        }

        private void Bake(TerraBakeOptions options)
        {
            if (options.ChunkCount > 64 && !EditorUtility.DisplayDialog("Bake terrain",
                "This bakes " + options.ChunkCount + " chunks. A chunk at full detail is about 26 000 vertices. Go on?", "Bake", "Cancel"))
            {
                return;
            }
            try
            {
                GameObject root = TerraBaker.Bake(profile, options);
                Selection.activeGameObject = root;
                status = "Baked " + options.ChunkCount + " chunks under " + root.name + ".";
            }
            catch (Exception e)
            {
                status = "Bake failed: " + e.Message;
            }
        }

        private void Render()
        {
            int res = Resolutions[Mathf.Clamp(resolutionIndex, 0, Resolutions.Length - 1)];
            TerraWorld world = profile.CreateWorld();
            double size = sizeMetres;
            double pixel = size / res;
            double west = centreX - size * 0.5;
            double south = centreZ - size * 0.5;
            double chunkSize = world.Lattice.ChunkSize;
            double water = world.Settings.waterLevel;
            Color32[] pixels = new Color32[res * res];
            double lowest = double.MaxValue;
            double highest = double.MinValue;

            for (int py = 0; py < res; py++)
            {
                double z = south + (py + 0.5) * pixel;
                for (int px = 0; px < res; px++)
                {
                    double x = west + (px + 0.5) * pixel;
                    double h = world.HeightAt(x, z);
                    if (h < lowest)
                    {
                        lowest = h;
                    }
                    if (h > highest)
                    {
                        highest = h;
                    }
                    double nx;
                    double ny;
                    double nz;
                    world.Field.Normal(x, z, Math.Max(1.0, pixel * 0.5), out nx, out ny, out nz);

                    float r;
                    float g;
                    float b;
                    if (mode == TerraPreviewMode.Height)
                    {
                        float v = (float)world.Field.ValueOfHeight(h);
                        r = v;
                        g = v;
                        b = v;
                    }
                    else if (mode == TerraPreviewMode.Slope)
                    {
                        double slope = Math.Acos(ny > 1.0 ? 1.0 : ny) * 180.0 / Math.PI;
                        float t = (float)Math.Min(1.0, slope / 60.0);
                        r = t;
                        g = 1f - t;
                        b = 0.2f;
                    }
                    else
                    {
                        TerraColor c = world.Coloring.Ground(x, z, h, ny);
                        r = c.r;
                        g = c.g;
                        b = c.b;
                    }

                    if (hillshade && mode != TerraPreviewMode.Slope)
                    {
                        // Light from the north west and above.
                        double dot = nx * -0.45 + ny * 0.8 + nz * 0.4;
                        float shade = (float)(0.55 + 0.6 * Math.Max(0.0, Math.Min(1.0, dot)));
                        r *= shade;
                        g *= shade;
                        b *= shade;
                    }
                    if (showWater && mode != TerraPreviewMode.Slope && h < water)
                    {
                        r = r * 0.4f + 0.15f * 0.6f;
                        g = g * 0.4f + 0.38f * 0.6f;
                        b = b * 0.4f + 0.75f * 0.6f;
                    }
                    if (showChunks && (Math.Floor(x / chunkSize) != Math.Floor((x + pixel) / chunkSize)
                        || Math.Floor(z / chunkSize) != Math.Floor((z + pixel) / chunkSize)))
                    {
                        r *= 0.6f;
                        g *= 0.6f;
                        b *= 0.6f;
                    }
                    pixels[py * res + px] = new Color32(ToByte(r), ToByte(g), ToByte(b), 255);
                }
            }

            if (texture != null)
            {
                DestroyImmediate(texture);
            }
            texture = new Texture2D(res, res, TextureFormat.RGBA32, false);
            texture.filterMode = FilterMode.Point;
            texture.SetPixels32(pixels);
            texture.Apply();
            status = "Heights from " + lowest.ToString("0.0") + " m to " + highest.ToString("0.0") + " m. Chunks are "
                + chunkSize.ToString("0") + " m wide. Seed " + profile.settings.seed + ".";
            Repaint();
        }

        private static byte ToByte(float f)
        {
            return (byte)(Mathf.Clamp01(f) * 255f + 0.5f);
        }
    }
}
