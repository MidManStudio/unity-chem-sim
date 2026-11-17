// ============================================================================
// NOTICE: Full documentation, design decisions, and fix history for this file
// live in docs/com.midmanstudio.proceduralterrain.md, section "Editor/TerraBaker.cs"
// ============================================================================
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace MidManStudio.ProceduralTerrain.EditorTools
{
    /// <summary>What to bake: a rectangle of chunks at one level of detail.</summary>
    public sealed class TerraBakeOptions
    {
        public int minChunkX;
        public int minChunkZ;
        public int maxChunkX = 3;
        public int maxChunkZ = 3;

        /// <summary>1 is every lattice node. Must divide the cells per chunk.</summary>
        public int step = 1;

        /// <summary>Save each chunk's mesh as an asset in <see cref="folder"/>, so the scene keeps working after a reload.</summary>
        public bool saveMeshes = true;
        public string folder = "Assets/TerraBake";

        /// <summary>Place the prefabs of the profile's scatter bindings under each chunk.</summary>
        public bool scatter = true;

        public string rootName = "Terrain (baked)";

        public int ChunkCount
        {
            get { return Math.Max(0, maxChunkX - minChunkX + 1) * Math.Max(0, maxChunkZ - minChunkZ + 1); }
        }
    }

    /// <summary>
    /// Builds a rectangle of chunks into the open scene as ordinary objects, each with its mesh, material and collider, under
    /// one root. The chunks sit at their true positions (the scene origin is the world origin), so a baked area is for a
    /// world that fits in one scene or for pieces of a larger one; a streamed world is placed by <see cref="TerraStreamer"/>.
    /// </summary>
    public static class TerraBaker
    {
        public static GameObject Bake(TerraWorldProfile profile, TerraBakeOptions options)
        {
            if (profile == null)
            {
                throw new ArgumentNullException("profile");
            }
            if (options == null)
            {
                throw new ArgumentNullException("options");
            }
            if (options.ChunkCount == 0)
            {
                throw new ArgumentException("The chunk range is empty.");
            }
            TerraWorld world = profile.CreateWorld();
            int cells = world.Lattice.CellsPerChunk;
            int step = options.step < 1 ? 1 : options.step;
            if (cells % step != 0)
            {
                throw new ArgumentException("The step " + step + " does not divide the " + cells + " cells of a chunk.");
            }
            if (options.saveMeshes)
            {
                EnsureFolder(options.folder);
            }
            Material material = profile.material != null ? profile.material : TerraMaterials.Create();
            Dictionary<string, GameObject[]> bindings = Bindings(profile);

            int group = Undo.GetCurrentGroup();
            GameObject root = new GameObject(options.rootName);
            Undo.RegisterCreatedObjectUndo(root, "Bake terrain");
            TerraBakedArea area = root.AddComponent<TerraBakedArea>();
            area.profile = profile;
            area.seed = profile.settings.seed;
            area.minChunkX = options.minChunkX;
            area.minChunkZ = options.minChunkZ;
            area.maxChunkX = options.maxChunkX;
            area.maxChunkZ = options.maxChunkZ;
            area.step = step;

            int total = options.ChunkCount;
            int done = 0;
            bool cancelled = false;
            try
            {
                for (int cz = options.minChunkZ; cz <= options.maxChunkZ && !cancelled; cz++)
                {
                    for (int cx = options.minChunkX; cx <= options.maxChunkX; cx++)
                    {
                        if (EditorUtility.DisplayCancelableProgressBar("Baking terrain", "Chunk " + cx + ", " + cz, (float)done / total))
                        {
                            cancelled = true;
                            break;
                        }
                        BakeChunk(world, profile, options, root.transform, material, bindings, cx, cz, step);
                        done++;
                    }
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
            if (options.saveMeshes)
            {
                AssetDatabase.SaveAssets();
            }
            Undo.SetCurrentGroupName("Bake terrain");
            Undo.CollapseUndoOperations(group);
            return root;
        }

        private static void BakeChunk(TerraWorld world, TerraWorldProfile profile, TerraBakeOptions options, Transform parent,
            Material material, Dictionary<string, GameObject[]> bindings, int cx, int cz, int step)
        {
            TerraChunkResult chunk = world.BuildChunk(cx, cz, step);
            Mesh mesh = TerraMeshConverter.ToMesh(chunk.mesh);
            mesh.name = "TerraChunk_" + cx + "_" + cz;

            GameObject go = new GameObject("Terra " + cx + "," + cz);
            go.transform.SetParent(parent, false);
            go.transform.position = new Vector3((float)chunk.originX, 0f, (float)chunk.originZ);
            go.layer = profile.layer;
            MeshFilter filter = go.AddComponent<MeshFilter>();
            MeshRenderer renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = profile.shadowCasting;
            renderer.receiveShadows = profile.receiveShadows;

            if (options.saveMeshes)
            {
                string path = options.folder + "/" + mesh.name + "_s" + step + ".asset";
                AssetDatabase.CreateAsset(mesh, path);
            }
            filter.sharedMesh = mesh;
            if (profile.colliders && step <= profile.colliderMaxStep)
            {
                MeshCollider collider = go.AddComponent<MeshCollider>();
                collider.sharedMesh = mesh;
            }
            go.isStatic = true;

            if (options.scatter && bindings.Count > 0)
            {
                PlaceScatter(world, bindings, go.transform, chunk, cx, cz);
            }
        }

        private static void PlaceScatter(TerraWorld world, Dictionary<string, GameObject[]> bindings, Transform parent,
            TerraChunkResult chunk, int cx, int cz)
        {
            TerraScatterLayer[] layers = world.Settings.scatter;
            TerraScatterPoint[] points = world.ScatterChunk(cx, cz);
            for (int i = 0; i < points.Length; i++)
            {
                TerraScatterPoint p = points[i];
                if (layers == null || p.layer < 0 || p.layer >= layers.Length || layers[p.layer] == null)
                {
                    continue;
                }
                GameObject[] variants;
                if (!bindings.TryGetValue(layers[p.layer].name, out variants))
                {
                    continue;
                }
                GameObject prefab = variants[(p.variant & 0x7FFFFFFF) % variants.Length];
                if (prefab == null)
                {
                    continue;
                }
                GameObject go = PrefabUtility.InstantiatePrefab(prefab, parent) as GameObject;
                if (go == null)
                {
                    continue;
                }
                Transform t = go.transform;
                t.localPosition = new Vector3((float)(p.x - chunk.originX), (float)p.y, (float)(p.z - chunk.originZ));
                Quaternion yaw = Quaternion.Euler(0f, p.yawDegrees, 0f);
                t.localRotation = layers[p.layer].alignToNormal
                    ? Quaternion.FromToRotation(Vector3.up, new Vector3(p.normalX, p.normalY, p.normalZ)) * yaw
                    : yaw;
                t.localScale = prefab.transform.localScale * p.scale;
                go.isStatic = true;
            }
        }

        private static Dictionary<string, GameObject[]> Bindings(TerraWorldProfile profile)
        {
            Dictionary<string, GameObject[]> map = new Dictionary<string, GameObject[]>();
            TerraScatterBinding[] list = profile.scatterBindings;
            for (int i = 0; list != null && i < list.Length; i++)
            {
                TerraScatterBinding b = list[i];
                if (b != null && !string.IsNullOrEmpty(b.layer) && b.variants != null && b.variants.Length > 0 && !map.ContainsKey(b.layer))
                {
                    map[b.layer] = b.variants;
                }
            }
            return map;
        }

        /// <summary>Makes every folder of an asset path that does not exist yet, such as Assets/TerraBake/Hills.</summary>
        public static void EnsureFolder(string path)
        {
            string[] parts = path.Split('/');
            if (parts.Length == 0 || parts[0] != "Assets")
            {
                throw new ArgumentException("The folder must be inside Assets: " + path);
            }
            string current = "Assets";
            for (int i = 1; i < parts.Length; i++)
            {
                if (parts[i].Length == 0)
                {
                    continue;
                }
                string next = current + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next))
                {
                    AssetDatabase.CreateFolder(current, parts[i]);
                }
                current = next;
            }
        }
    }
}
