using UnityEngine;

namespace MidManStudio.Alembic.Rendering
{
    /// <summary>
    /// Small shared helper — both <see cref="AtomRenderer"/> and
    /// <see cref="BondRenderer"/> need a default primitive mesh
    /// (sphere/cylinder respectively) and neither wants to hand-write
    /// mesh-generation code to get one.
    /// </summary>
    internal static class AlembicMeshUtility
    {
        /// <summary>
        /// Returns an owned copy of one of Unity's built-in primitive
        /// meshes. Grabbing the real built-in mesh rather than hand-
        /// writing sphere/cylinder generation code: it's already correct
        /// (proper winding order, pole handling, UVs) by construction,
        /// which a hand-rolled generator would need real Editor/visual
        /// verification to trust — verification this was written without
        /// access to. The temporary GameObject Unity creates to expose
        /// the mesh is destroyed immediately; only the (now-owned) Mesh
        /// survives.
        /// </summary>
        public static Mesh CreatePrimitiveMesh(PrimitiveType type, string name)
        {
            var temp = GameObject.CreatePrimitive(type);
            temp.hideFlags = HideFlags.HideAndDontSave;
            Mesh mesh = Object.Instantiate(temp.GetComponent<MeshFilter>().sharedMesh);
            mesh.name = name;
            if (Application.isPlaying) Object.Destroy(temp); else Object.DestroyImmediate(temp);
            return mesh;
        }

        public static void DestroyMesh(Mesh mesh)
        {
            if (mesh == null) return;
            if (Application.isPlaying) Object.Destroy(mesh); else Object.DestroyImmediate(mesh);
        }
    }
}
