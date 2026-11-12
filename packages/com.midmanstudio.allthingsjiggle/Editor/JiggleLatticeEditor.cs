// ============================================================================
// NOTICE: Full documentation, design decisions, and fix history for this file
// live in docs/com.midmanstudio.allthingsjiggle.md, section "Editor/JiggleLatticeEditor.cs"
// ============================================================================
using System.Globalization;
using UnityEditor;
using UnityEngine;

namespace MidManStudio.AllThingsJiggle.EditorTools
{
    /// <summary>
    /// The inspector of a lattice: the settings, what the grid would be for the mesh, the problems that would stop the lattice, and
    /// the buttons for play mode. The box and the control points are drawn in the scene view while the object is selected.
    /// </summary>
    [CustomEditor(typeof(JiggleLattice))]
    public sealed class JiggleLatticeEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            JiggleLattice lattice = (JiggleLattice)target;
            EditorGUILayout.Space();

            MeshFilter filter = lattice.GetComponent<MeshFilter>();
            Mesh mesh = filter != null ? filter.sharedMesh : null;
            if (mesh == null)
            {
                EditorGUILayout.HelpBox("The MeshFilter has no mesh, so there is nothing to deform.", MessageType.Error);
                return;
            }
            if (!mesh.isReadable)
            {
                EditorGUILayout.HelpBox(
                    "The mesh '" + mesh.name + "' is not readable. Turn on Read/Write in its import settings, or the lattice cannot read its vertices.",
                    MessageType.Error);
                return;
            }

            int nx;
            int ny;
            int nz;
            Vector3 min;
            Vector3 size;
            if (!lattice.TryGetDivisions(out nx, out ny, out nz) || !lattice.TryGetBox(out min, out size))
            {
                return;
            }
            int points = nx * ny * nz;
            if (points < JiggleLatticeGrid.MinPoints)
            {
                EditorGUILayout.HelpBox(
                    "The box of the mesh has no size on two axes, so the grid has one control point. A lattice needs at least two.",
                    MessageType.Error);
                return;
            }
            EditorGUILayout.HelpBox(
                "Grid: " + nx + " x " + ny + " x " + nz + " = " + points + " control points, one of them the anchor.\n"
                + "Box: " + size.x.ToString("F2", CultureInfo.InvariantCulture) + " x " + size.y.ToString("F2", CultureInfo.InvariantCulture)
                + " x " + size.z.ToString("F2", CultureInfo.InvariantCulture) + " in the space of the mesh.\n"
                + "Vertices: " + mesh.vertexCount + ". The box and the control points are drawn in the scene view while this is selected.",
                MessageType.None);
            if (nx != lattice.divisionsX || ny != lattice.divisionsY || nz != lattice.divisionsZ)
            {
                EditorGUILayout.HelpBox(
                    "An axis the mesh has no size on has one control point, whatever its divisions say. Points that lie on one another have no direction between them.",
                    MessageType.Info);
            }
            if (!Application.isPlaying)
            {
                return;
            }
            EditorGUILayout.LabelField("Simulated", lattice.IsSimulating ? "yes" : "no");
            EditorGUILayout.LabelField("Level of detail", lattice.LodLevel.ToString());
            EditorGUILayout.LabelField("Asleep", lattice.IsAsleep ? "yes" : "no");
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Reset simulation"))
            {
                lattice.ResetSimulation();
            }
            if (GUILayout.Button("Wake"))
            {
                lattice.Wake();
            }
            EditorGUILayout.EndHorizontal();
            if (GUILayout.Button("Hit sideways"))
            {
                Vector3 side = lattice.transform.right;
                lattice.AddImpulse(new Vector3(side.x * 3f, side.y * 3f, side.z * 3f));
            }
        }
    }

    /// <summary>Draws the box and the control points of a selected lattice, and the simulated control points while the game runs.</summary>
    internal static class JiggleLatticeGizmos
    {
        [DrawGizmo(GizmoType.Selected | GizmoType.Active)]
        private static void Draw(JiggleLattice lattice, GizmoType type)
        {
            int nx;
            int ny;
            int nz;
            Vector3 min;
            Vector3 size;
            if (!lattice.TryGetDivisions(out nx, out ny, out nz) || !lattice.TryGetBox(out min, out size))
            {
                return;
            }
            float largest = Mathf.Max(size.x, Mathf.Max(size.y, size.z));
            float radius = largest > 0f ? largest * 0.025f : 0.01f;
            JVec3 lo = new JVec3(min.x, min.y, min.z);
            JVec3 extent = new JVec3(size.x, size.y, size.z);

            // The box and the rest places of the control points, in the space of the mesh.
            Matrix4x4 old = Gizmos.matrix;
            Gizmos.matrix = lattice.transform.localToWorldMatrix;
            Gizmos.color = new Color(0.3f, 0.7f, 1f);
            Gizmos.DrawWireCube(new Vector3(min.x + size.x * 0.5f, min.y + size.y * 0.5f, min.z + size.z * 0.5f), size);
            for (int k = 0; k < nz; k++)
            {
                for (int j = 0; j < ny; j++)
                {
                    for (int i = 0; i < nx; i++)
                    {
                        JVec3 p = JiggleLatticeGrid.PointRest(i, j, k, nx, ny, nz, lo, extent);
                        Gizmos.DrawWireSphere(new Vector3(p.X, p.Y, p.Z), radius);
                    }
                }
            }
            Gizmos.matrix = old;

            if (!Application.isPlaying || !lattice.IsSimulating || lattice.ControlPointCount != nx * ny * nz)
            {
                return;
            }

            // The simulated control points in the world, joined along the grid.
            Vector3[] world = new Vector3[nx * ny * nz];
            for (int n = 0; n < world.Length; n++)
            {
                if (!lattice.TryGetControlPoint(n, out world[n]))
                {
                    return;
                }
            }
            Gizmos.color = new Color(1f, 0.55f, 0.1f);
            for (int k = 0; k < nz; k++)
            {
                for (int j = 0; j < ny; j++)
                {
                    for (int i = 0; i < nx; i++)
                    {
                        Vector3 a = world[JiggleLatticeGrid.PointIndex(i, j, k, nx, ny)];
                        Gizmos.DrawSphere(a, radius * 0.6f);
                        if (i + 1 < nx)
                        {
                            Gizmos.DrawLine(a, world[JiggleLatticeGrid.PointIndex(i + 1, j, k, nx, ny)]);
                        }
                        if (j + 1 < ny)
                        {
                            Gizmos.DrawLine(a, world[JiggleLatticeGrid.PointIndex(i, j + 1, k, nx, ny)]);
                        }
                        if (k + 1 < nz)
                        {
                            Gizmos.DrawLine(a, world[JiggleLatticeGrid.PointIndex(i, j, k + 1, nx, ny)]);
                        }
                    }
                }
            }
        }
    }
}
