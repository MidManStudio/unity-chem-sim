// ============================================================================
// NOTICE: Full documentation, design decisions, and fix history for this file
// live in docs/com.midmanstudio.allthingsjiggle.md, section "Runtime/Unity/JiggleLatticeMesh.cs"
// ============================================================================
using System;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine;
using UnityEngine.Rendering;

namespace MidManStudio.AllThingsJiggle
{
    /// <summary>
    /// What one lattice holds of its mesh: a private copy of the mesh that the renderer shows, the vertices of the original
    /// at rest, the lattice coordinates of each vertex in the box, and the buffers the deform jobs write the moved vertices
    /// into. It belongs to the <see cref="JiggleLattice"/> component and lives as long as the component is enabled, so the
    /// arrays of the manager can be rebuilt without touching it. The jobs get raw pointers into it, so nothing here may be
    /// freed while a frame runs, and nothing is: frames are complete when they return.
    /// </summary>
    internal sealed unsafe class JiggleLatticeMesh : IDisposable
    {
        /// <summary>The mesh filter whose mesh is swapped for the private copy.</summary>
        internal readonly MeshFilter Filter;
        /// <summary>The mesh the filter had, which is given back when the lattice is disabled.</summary>
        internal readonly Mesh Source;
        /// <summary>The private copy the renderer shows while the lattice is enabled.</summary>
        internal readonly Mesh Instance;
        /// <summary>The bounds of the source mesh, which the box of the lattice is made from.</summary>
        internal readonly Bounds SourceBounds;
        /// <summary>Number of vertices.</summary>
        internal readonly int Vertices;
        /// <summary>Whether the mesh has normals, which are carried along with the vertices.</summary>
        internal readonly bool HasNormals;
        /// <summary>Smallest corner of the box in the space of the mesh.</summary>
        internal readonly JVec3 Min;
        /// <summary>Size of the box.</summary>
        internal readonly JVec3 Size;

        /// <summary>Vertex positions at rest.</summary>
        internal JVec3* BasePos;
        /// <summary>Vertex normals at rest, null without normals.</summary>
        internal JVec3* BaseNormal;
        /// <summary>The lattice coordinates of each vertex, each between 0 and 1.</summary>
        internal JVec3* Coords;
        /// <summary>Displacement of each control point of this frame, room for the most points a lattice has.</summary>
        internal JVec3* D;
        /// <summary>The displacement the mesh was last uploaded with, so a frame that changes nothing costs nothing.</summary>
        internal JVec3* LastD;
        /// <summary>Where the deform jobs write the moved vertices, and what is uploaded to the mesh.</summary>
        internal NativeArray<Vector3> OutPos;
        /// <summary>Where the deform jobs write the moved normals. Not created without normals.</summary>
        internal NativeArray<Vector3> OutNormal;

        private bool disposed;

        private JiggleLatticeMesh(MeshFilter filter, Mesh source, Mesh instance, int vertices, bool normals, JVec3 min, JVec3 size)
        {
            Filter = filter;
            Source = source;
            Instance = instance;
            SourceBounds = source.bounds;
            Vertices = vertices;
            HasNormals = normals;
            Min = min;
            Size = size;
        }

        /// <summary>
        /// The box of a lattice: the bounds of the mesh, widened on every side by <paramref name="padding"/> times the size of
        /// the bounds on that axis. The same box is drawn by the editor, so what is drawn is what is simulated.
        /// </summary>
        internal static void BoxOf(Bounds bounds, float padding, out JVec3 min, out JVec3 size)
        {
            float pad = padding > 0f && JiggleMath.IsFinite(padding) ? (padding > 1f ? 1f : padding) : 0f;
            Vector3 c = bounds.center;
            Vector3 e = bounds.extents;
            float sx = e.x * 2f;
            float sy = e.y * 2f;
            float sz = e.z * 2f;
            min = new JVec3(c.x - e.x - sx * pad, c.y - e.y - sy * pad, c.z - e.z - sz * pad);
            size = new JVec3(sx * (1f + 2f * pad), sy * (1f + 2f * pad), sz * (1f + 2f * pad));
        }

        /// <summary>
        /// Takes the mesh of a filter and makes the private copy. Returns null with the reason when the mesh cannot be deformed:
        /// there is none, it has no vertices, or it is not readable, which a build of the game needs the import setting
        /// Read/Write for. The filter shows the copy from here on.
        /// </summary>
        internal static JiggleLatticeMesh Create(MeshFilter filter, float padding, out string problem)
        {
            problem = null;
            Mesh source = filter.sharedMesh;
            if (source == null)
            {
                problem = "its MeshFilter has no mesh.";
                return null;
            }
            if (!source.isReadable)
            {
                problem = "the mesh '" + source.name + "' is not readable. Turn on Read/Write in its import settings.";
                return null;
            }
            int count = source.vertexCount;
            if (count < 1)
            {
                problem = "the mesh '" + source.name + "' has no vertices.";
                return null;
            }
            Vector3[] vertices = source.vertices;
            Vector3[] normals = source.normals;
            if (vertices == null || vertices.Length != count)
            {
                problem = "the mesh '" + source.name + "' could not be read.";
                return null;
            }
            bool hasNormals = normals != null && normals.Length == count;

            JVec3 min;
            JVec3 size;
            BoxOf(source.bounds, padding, out min, out size);
            if (!min.IsFinite() || !size.IsFinite())
            {
                problem = "the bounds of the mesh '" + source.name + "' are not finite numbers.";
                return null;
            }

            Mesh instance = UnityEngine.Object.Instantiate(source);
            instance.name = source.name + " (jiggle lattice)";
            instance.MarkDynamic();

            JiggleLatticeMesh m = new JiggleLatticeMesh(filter, source, instance, count, hasNormals, min, size);
            m.BasePos = JiggleMem.Alloc<JVec3>(count);
            m.Coords = JiggleMem.Alloc<JVec3>(count);
            m.D = JiggleMem.Alloc<JVec3>(JiggleLatticeGrid.MaxPoints);
            m.LastD = JiggleMem.Alloc<JVec3>(JiggleLatticeGrid.MaxPoints);
            for (int i = 0; i < count; i++)
            {
                Vector3 v = vertices[i];
                m.BasePos[i] = new JVec3(v.x, v.y, v.z);
            }
            JiggleFfd.Coordinates(m.BasePos, count, min, size, m.Coords);
            m.OutPos = new NativeArray<Vector3>(count, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
            m.OutPos.CopyFrom(vertices);
            if (hasNormals)
            {
                m.BaseNormal = JiggleMem.Alloc<JVec3>(count);
                for (int i = 0; i < count; i++)
                {
                    Vector3 n = normals[i];
                    m.BaseNormal[i] = new JVec3(n.x, n.y, n.z);
                }
                m.OutNormal = new NativeArray<Vector3>(count, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
                m.OutNormal.CopyFrom(normals);
            }
            filter.sharedMesh = instance;
            return m;
        }

        /// <summary>The pointer the deform jobs write the moved vertices to.</summary>
        internal JVec3* OutPosPtr()
        {
            return (JVec3*)NativeArrayUnsafeUtility.GetUnsafePtr(OutPos);
        }

        /// <summary>The pointer the deform jobs write the moved normals to, null without normals.</summary>
        internal JVec3* OutNormalPtr()
        {
            return HasNormals ? (JVec3*)NativeArrayUnsafeUtility.GetUnsafePtr(OutNormal) : null;
        }

        /// <summary>
        /// Gives the moved vertices, and the normals, to the mesh the renderer shows. <paramref name="reach"/> is the largest
        /// displacement of a control point, in the space of the mesh. No vertex moves farther than that, so the bounds of the
        /// source widened by it hold every vertex and the mesh is not culled while it is on screen. Call it on the main thread
        /// once the jobs of the frame are complete.
        /// </summary>
        internal void Upload(float reach)
        {
            if (disposed || Instance == null)
            {
                return;
            }
            Instance.SetVertices(OutPos, 0, Vertices, MeshUpdateFlags.DontRecalculateBounds);
            if (HasNormals)
            {
                Instance.SetNormals(OutNormal, 0, Vertices, MeshUpdateFlags.DontRecalculateBounds);
            }
            float r = reach > 0f && JiggleMath.IsFinite(reach) ? reach : 0f;
            Vector3 c = SourceBounds.center;
            Vector3 e = SourceBounds.extents;
            Instance.bounds = new Bounds(c, new Vector3((e.x + r) * 2f, (e.y + r) * 2f, (e.z + r) * 2f));
        }

        /// <summary>Puts the original mesh back on the filter if the filter still shows the copy.</summary>
        internal void Restore()
        {
            if (Filter != null && Filter.sharedMesh == Instance)
            {
                Filter.sharedMesh = Source;
            }
        }

        /// <summary>Gives the original mesh back, destroys the copy and frees the buffers.</summary>
        public void Dispose()
        {
            if (disposed)
            {
                return;
            }
            disposed = true;
            Restore();
            if (Instance != null)
            {
                if (Application.isPlaying)
                {
                    UnityEngine.Object.Destroy(Instance);
                }
                else
                {
                    UnityEngine.Object.DestroyImmediate(Instance);
                }
            }
            JiggleMem.Free(ref BasePos);
            JiggleMem.Free(ref BaseNormal);
            JiggleMem.Free(ref Coords);
            JiggleMem.Free(ref D);
            JiggleMem.Free(ref LastD);
            if (OutPos.IsCreated)
            {
                OutPos.Dispose();
            }
            if (OutNormal.IsCreated)
            {
                OutNormal.Dispose();
            }
        }
    }
}
