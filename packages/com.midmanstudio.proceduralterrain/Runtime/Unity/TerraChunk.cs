// ============================================================================
// NOTICE: Full documentation, design decisions, and fix history for this file
// live in docs/com.midmanstudio.proceduralterrain.md, section "Runtime/Unity/TerraChunk.cs"
// ============================================================================
using UnityEngine;

namespace MidManStudio.ProceduralTerrain
{
    internal static class TerraObjects
    {
        // Destroy waits for the end of the frame and is refused outside play mode; DestroyImmediate is the reverse.
        internal static void Discard(UnityEngine.Object o)
        {
            if (o == null)
            {
                return;
            }
            if (Application.isPlaying)
            {
                UnityEngine.Object.Destroy(o);
            }
            else
            {
                UnityEngine.Object.DestroyImmediate(o);
            }
        }
    }

    /// <summary>
    /// One streamed chunk of the world: a game object with a mesh, a renderer and sometimes a collider. The mesh is relative
    /// to the corner of the chunk, so the object's position is that corner minus the world origin of the streamer, a small
    /// number near the viewer. Made and destroyed by <see cref="TerraStreamer"/>.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class TerraChunk : MonoBehaviour
    {
        /// <summary>The chunk's address in the lattice.</summary>
        public long chunkX;
        public long chunkZ;

        /// <summary>The level of detail of the mesh now shown: 1 is every lattice node.</summary>
        public int step;

        /// <summary>The true position of the chunk's south west corner in metres.</summary>
        public double originX;
        public double originZ;

        private Mesh mesh;
        private MeshFilter filter;
        private MeshRenderer meshRenderer;
        private MeshCollider meshCollider;

        // The scatter points of the chunk and how many of them are placed already. Used by the streamer.
        internal TerraScatterPoint[] scatterPoints;
        internal int scatterCursor;

        public Mesh Mesh
        {
            get { return mesh; }
        }

        /// <summary>The scatter points of this chunk, or null before they have arrived. True world coordinates.</summary>
        public TerraScatterPoint[] ScatterPoints
        {
            get { return scatterPoints; }
        }

        internal void Initialize(TerraChunkKey key, double cornerX, double cornerZ, TerraWorldProfile profile, Material material)
        {
            chunkX = key.x;
            chunkZ = key.z;
            originX = cornerX;
            originZ = cornerZ;
            gameObject.layer = profile.layer;
            filter = gameObject.AddComponent<MeshFilter>();
            meshRenderer = gameObject.AddComponent<MeshRenderer>();
            meshRenderer.sharedMaterial = material;
            meshRenderer.shadowCastingMode = profile.shadowCasting;
            meshRenderer.receiveShadows = profile.receiveShadows;
        }

        /// <summary>Shows a new mesh, at a level of detail, with or without a collider. The first call makes the mesh object.</summary>
        internal void SetMesh(TerraPreparedMesh prepared, int newStep, bool collide)
        {
            if (mesh == null)
            {
                mesh = new Mesh();
                mesh.name = "TerraChunk " + chunkX + "," + chunkZ;
                filter.sharedMesh = mesh;
            }
            TerraMeshConverter.Apply(prepared, mesh);
            step = newStep;
            if (collide)
            {
                if (meshCollider == null)
                {
                    meshCollider = gameObject.AddComponent<MeshCollider>();
                }
                // Assigning the same mesh again does not cook it again, so let go of it first.
                meshCollider.sharedMesh = null;
                meshCollider.sharedMesh = mesh;
                meshCollider.enabled = true;
            }
            else if (meshCollider != null)
            {
                meshCollider.sharedMesh = null;
                meshCollider.enabled = false;
            }
        }

        /// <summary>Puts the object where the chunk belongs for a world origin: the chunk's true corner minus the origin.</summary>
        public void Place(double worldOriginX, double worldOriginZ)
        {
            transform.position = new Vector3((float)(originX - worldOriginX), 0f, (float)(originZ - worldOriginZ));
        }

        /// <summary>Destroys the mesh. The streamer calls it before it destroys the object, because OnDestroy does not run outside play mode.</summary>
        public void Release()
        {
            if (mesh != null)
            {
                TerraObjects.Discard(mesh);
                mesh = null;
            }
        }

        private void OnDestroy()
        {
            Release();
        }
    }
}
