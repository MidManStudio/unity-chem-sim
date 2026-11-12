// ============================================================================
// NOTICE: Full documentation, design decisions, and fix history for this file
// live in docs/com.midmanstudio.allthingsjiggle.md, section "Editor/Lab/JiggleLabScene.cs"
// ============================================================================
using UnityEditor;
using UnityEngine;

namespace MidManStudio.AllThingsJiggle.EditorTools
{
    /// <summary>
    /// Builds the objects the labs play with. They are made in play mode only, so they vanish when play mode ends and the
    /// scene of the project is never touched. Everything hangs under one root object that a lab removes with its button.
    /// </summary>
    internal static class JiggleLabScene
    {
        /// <summary>A new empty root object for a lab, selected so the person can find it.</summary>
        internal static GameObject Root(string name)
        {
            GameObject go = new GameObject(name);
            Selection.activeGameObject = go;
            return go;
        }

        /// <summary>
        /// A chain of <paramref name="bones"/> bones hanging straight down from <paramref name="position"/>, each
        /// <paramref name="spacing"/> meters below the last. The anchor is an empty object at the position, the component is on
        /// the first bone below it, as the inspector asks. The settings are the ones the chain starts with.
        /// </summary>
        internal static JiggleChain Chain(
            Transform parent, string name, Vector3 position, int bones, float spacing, JiggleChainSettings settings)
        {
            GameObject anchor = new GameObject(name);
            anchor.transform.SetParent(parent, false);
            anchor.transform.position = position;
            Transform above = anchor.transform;
            Transform first = null;
            for (int i = 0; i < bones; i++)
            {
                GameObject bone = new GameObject(name + " bone " + (i + 1));
                bone.transform.SetParent(above, false);
                bone.transform.localPosition = new Vector3(0f, -spacing, 0f);
                above = bone.transform;
                if (i == 0)
                {
                    first = bone.transform;
                }
            }
            JiggleChain chain = first.gameObject.AddComponent<JiggleChain>();
            chain.settings = settings;
            return chain;
        }

        /// <summary>The tip of a chain, the last simulated particle before the virtual end, or false when it is not simulated.</summary>
        internal static bool Tip(JiggleChain chain, out Vector3 tip)
        {
            tip = Vector3.zero;
            return chain != null && chain.IsSimulating && chain.TryGetParticle(chain.BoneCount, out tip);
        }

        /// <summary>
        /// A cube of side 2 * <paramref name="half"/> centred on the origin with <paramref name="cells"/> cells along each edge of every
        /// face, so a lattice has vertices to bend. Each face has its own vertices and its flat normal, and the mesh is readable.
        /// </summary>
        internal static Mesh SubdividedCube(int cells, float half)
        {
            int n = cells < 1 ? 1 : cells;
            int side = n + 1;
            int perFace = side * side;
            Vector3[] vertices = new Vector3[6 * perFace];
            Vector3[] normals = new Vector3[6 * perFace];
            int[] triangles = new int[6 * n * n * 6];
            int t = 0;
            for (int face = 0; face < 6; face++)
            {
                // The face is on the axis a, on the side s. b and c are the next two axes in a cycle, so b cross c points along a.
                int a = face / 2;
                float s = face % 2 == 0 ? 1f : -1f;
                int b = (a + 1) % 3;
                int c = (a + 2) % 3;
                int first = face * perFace;
                for (int j = 0; j < side; j++)
                {
                    for (int i = 0; i < side; i++)
                    {
                        float[] p = new float[3];
                        float[] nrm = new float[3];
                        p[a] = s * half;
                        p[b] = -half + 2f * half * (float)i / (float)n;
                        p[c] = -half + 2f * half * (float)j / (float)n;
                        nrm[a] = s;
                        vertices[first + i + side * j] = new Vector3(p[0], p[1], p[2]);
                        normals[first + i + side * j] = new Vector3(nrm[0], nrm[1], nrm[2]);
                    }
                }
                for (int j = 0; j < n; j++)
                {
                    for (int i = 0; i < n; i++)
                    {
                        int v00 = first + i + side * j;
                        int v10 = v00 + 1;
                        int v01 = v00 + side;
                        int v11 = v01 + 1;
                        if (s > 0f)
                        {
                            triangles[t++] = v00; triangles[t++] = v10; triangles[t++] = v01;
                            triangles[t++] = v10; triangles[t++] = v11; triangles[t++] = v01;
                        }
                        else
                        {
                            triangles[t++] = v00; triangles[t++] = v01; triangles[t++] = v10;
                            triangles[t++] = v10; triangles[t++] = v01; triangles[t++] = v11;
                        }
                    }
                }
            }
            Mesh mesh = new Mesh();
            mesh.name = "Jiggle Lab cube";
            mesh.vertices = vertices;
            mesh.normals = normals;
            mesh.triangles = triangles;
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>
        /// A cube made of <see cref="SubdividedCube"/> with the default material of the project, a child of
        /// <paramref name="parent"/> at <paramref name="position"/>. It has no collider and no lattice.
        /// </summary>
        internal static GameObject CubeProp(Transform parent, string name, Vector3 position, int cells, float half)
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.position = position;
            Collider collider = go.GetComponent<Collider>();
            if (collider != null)
            {
                Object.DestroyImmediate(collider);
            }
            go.GetComponent<MeshFilter>().sharedMesh = SubdividedCube(cells, half);
            return go;
        }

        /// <summary>Removes a lab's objects. Safe to call with null.</summary>
        internal static void Remove(GameObject root)
        {
            if (root != null)
            {
                Object.DestroyImmediate(root);
            }
        }
    }
}
