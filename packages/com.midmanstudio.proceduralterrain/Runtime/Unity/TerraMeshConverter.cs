// ============================================================================
// NOTICE: Full documentation, design decisions, and fix history for this file
// live in docs/com.midmanstudio.proceduralterrain.md, section "Runtime/Unity/TerraMeshConverter.cs"
// ============================================================================
using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace MidManStudio.ProceduralTerrain
{
    /// <summary>
    /// A chunk mesh in the types the engine wants, made on any thread. Vector3, Color32 and Bounds are plain structs, so the
    /// arrays can be filled on a worker; only <see cref="TerraMeshConverter.Apply"/> touches a Mesh and must run on the main thread.
    /// </summary>
    public sealed class TerraPreparedMesh
    {
        public Vector3[] vertices;
        public Vector3[] normals;
        public Color32[] colors;
        public int[] indices;
        public Bounds bounds;

        /// <summary>The vertex count passes 65 535, so the mesh needs a 32 bit index buffer.</summary>
        public bool uint32;
    }

    public static class TerraMeshConverter
    {
        /// <summary>Turns the Core's arrays into engine arrays. Safe on any thread. The index array is shared, not copied.</summary>
        public static TerraPreparedMesh Prepare(TerraMeshData data)
        {
            if (data == null)
            {
                throw new ArgumentNullException("data");
            }
            int n = data.vertexCount;
            Vector3[] v = new Vector3[n];
            Vector3[] nr = new Vector3[n];
            Color32[] c = new Color32[n];
            float[] p = data.positions;
            float[] q = data.normals;
            float[] k = data.colors;
            for (int i = 0; i < n; i++)
            {
                int i3 = i * 3;
                int i4 = i * 4;
                v[i] = new Vector3(p[i3], p[i3 + 1], p[i3 + 2]);
                nr[i] = new Vector3(q[i3], q[i3 + 1], q[i3 + 2]);
                c[i] = new Color32(ToByte(k[i4]), ToByte(k[i4 + 1]), ToByte(k[i4 + 2]), ToByte(k[i4 + 3]));
            }
            TerraPreparedMesh m = new TerraPreparedMesh();
            m.vertices = v;
            m.normals = nr;
            m.colors = c;
            m.indices = data.indices;
            m.uint32 = data.RequiresUInt32;
            m.bounds = new Bounds(
                new Vector3((data.minX + data.maxX) * 0.5f, (data.minY + data.maxY) * 0.5f, (data.minZ + data.maxZ) * 0.5f),
                new Vector3(data.maxX - data.minX, data.maxY - data.minY, data.maxZ - data.minZ));
            return m;
        }

        /// <summary>Replaces the contents of a mesh. Main thread only.</summary>
        public static void Apply(TerraPreparedMesh prepared, Mesh mesh)
        {
            if (prepared == null)
            {
                throw new ArgumentNullException("prepared");
            }
            if (mesh == null)
            {
                throw new ArgumentNullException("mesh");
            }
            mesh.Clear();
            // The index format has to be set before the vertices, or a mesh with more than 65 535 of them is refused.
            mesh.indexFormat = prepared.uint32 ? IndexFormat.UInt32 : IndexFormat.UInt16;
            mesh.vertices = prepared.vertices;
            mesh.normals = prepared.normals;
            mesh.colors32 = prepared.colors;
            mesh.SetIndices(prepared.indices, MeshTopology.Triangles, 0, false);
            mesh.bounds = prepared.bounds;
        }

        /// <summary>A new mesh from chunk data. Main thread only.</summary>
        public static Mesh ToMesh(TerraMeshData data)
        {
            Mesh mesh = new Mesh();
            Apply(Prepare(data), mesh);
            return mesh;
        }

        private static byte ToByte(float f)
        {
            if (f < 0f)
            {
                f = 0f;
            }
            else if (f > 1f)
            {
                f = 1f;
            }
            return (byte)(f * 255f + 0.5f);
        }
    }
}
