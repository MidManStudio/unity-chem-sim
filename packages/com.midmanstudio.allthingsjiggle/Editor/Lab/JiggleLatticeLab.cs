// ============================================================================
// NOTICE: Full documentation, design decisions, and fix history for this file
// live in docs/com.midmanstudio.allthingsjiggle.md, section "Editor/Lab/JiggleLatticeLab.cs"
// ============================================================================
using System.Globalization;
using UnityEditor;
using UnityEngine;

namespace MidManStudio.AllThingsJiggle.EditorTools
{
    /// <summary>
    /// The lab of the lattice. It makes a cube of 20 by 20 cells on each face, puts a lattice on it, and moves it: back and
    /// forth with sudden stops, round, or up and down, so the cube has something to wobble about. Every value of the lattice can
    /// be changed while it runs, and the lab shows how many control points and vertices there are, whether the mesh was uploaded
    /// in the last frame, and how far the control points are from their rest places. It is the check that the transform jobs,
    /// the deform jobs and the mesh upload work together, which only the engine can do. Play mode only.
    /// </summary>
    internal static class JiggleLatticeLab
    {
        private static readonly string[] MotionNames = { "Still", "Shuttle with sudden stops", "Spin", "Hop" };
        private static readonly string[] AnchorNames = { "Center", "Pivot" };

        private static GameObject root;
        private static Transform prop;
        private static JiggleLattice lattice;
        private static Vector3 home;
        private static int motion = 1;
        private static float speed = 1.2f;
        private static float amplitude = 1.2f;
        private static double motionStart;
        private static float elapsed;

        internal static void Draw()
        {
            EditorGUILayout.HelpBox(
                "A cube with a lattice around it is moved so that it has to wobble. Change the grid, the stiffness and the rest while it runs. The control points are drawn in the scene view while the cube is selected: blue are the rest places, orange are the simulated ones. Hit it to see the shove travel through the body. Uploads is how many meshes were given new vertices in the last frame, so it drops to 0 when the cube is at rest.",
                MessageType.None);
            if (!Application.isPlaying)
            {
                EditorGUILayout.HelpBox("Enter play mode. The setup is made there and is gone when play mode ends.", MessageType.Info);
                return;
            }
            if (GUILayout.Button(root == null ? "Create the test cube" : "Rebuild the test cube"))
            {
                Create();
            }
            if (root == null || prop == null || lattice == null)
            {
                return;
            }

            EditorGUILayout.Space();
            motion = EditorGUILayout.Popup("Motion", motion, MotionNames);
            speed = EditorGUILayout.Slider("Speed", speed, 0.2f, 6f);
            amplitude = EditorGUILayout.Slider("Distance", amplitude, 0.2f, 3f);

            EditorGUILayout.Space();
            EditorGUI.BeginChangeCheck();
            lattice.divisionsX = EditorGUILayout.IntSlider("Control points x", lattice.divisionsX, 1, JiggleLatticeGrid.MaxDivisions);
            lattice.divisionsY = EditorGUILayout.IntSlider("Control points y", lattice.divisionsY, 1, JiggleLatticeGrid.MaxDivisions);
            lattice.divisionsZ = EditorGUILayout.IntSlider("Control points z", lattice.divisionsZ, 1, JiggleLatticeGrid.MaxDivisions);
            lattice.padding = EditorGUILayout.Slider("Padding", lattice.padding, 0f, 1f);
            lattice.anchorPoint = (JiggleLatticeAnchor)EditorGUILayout.Popup("Anchor", (int)lattice.anchorPoint, AnchorNames);
            JiggleLatticeSettings s = lattice.settings;
            s.stiffness = EditorGUILayout.Slider("Stiffness", s.stiffness, 0f, 1f);
            s.drag = EditorGUILayout.Slider("Drag", s.drag, 0f, 1f);
            s.stretch = EditorGUILayout.Slider("Stretch", s.stretch, 0f, 1f);
            s.soften = EditorGUILayout.Slider("Soften", s.soften, 0f, 1f);
            s.mass = EditorGUILayout.Slider("Mass (0 is no coupling)", s.mass, 0f, 4f);
            s.gravity = EditorGUILayout.Slider("Gravity", s.gravity, 0f, 1f);
            s.airDrag = EditorGUILayout.Slider("Air drag", s.airDrag, 0f, 1f);
            s.ignoreRootMotion = EditorGUILayout.Slider("Ignore root motion", s.ignoreRootMotion, 0f, 1f);
            if (EditorGUI.EndChangeCheck())
            {
                lattice.SettingsChanged();
            }

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Hit sideways"))
            {
                lattice.AddImpulse(new Vector3(3f, 0f, 0f));
            }
            if (GUILayout.Button("Reset simulation"))
            {
                lattice.ResetSimulation();
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space();
            JiggleManager m = JiggleManager.TryGet();
            MeshFilter filter = prop.GetComponent<MeshFilter>();
            EditorGUILayout.LabelField("Simulated", lattice.IsSimulating ? "yes" : "no");
            EditorGUILayout.LabelField("Control points", lattice.ControlPointCount.ToString(CultureInfo.InvariantCulture));
            EditorGUILayout.LabelField("Vertices", filter != null && filter.sharedMesh != null ? filter.sharedMesh.vertexCount.ToString(CultureInfo.InvariantCulture) : "-");
            EditorGUILayout.LabelField("Uploads in the last frame", m != null ? m.Stats.LatticeUploads.ToString(CultureInfo.InvariantCulture) : "-");
            EditorGUILayout.LabelField("Asleep", lattice.IsAsleep ? "yes" : "no");
            EditorGUILayout.LabelField("Control point farthest from its rest place", Reach());
            if (GUILayout.Button("Remove the test cube"))
            {
                JiggleLabScene.Remove(root);
                root = null;
                prop = null;
                lattice = null;
            }
        }

        private static void Create()
        {
            JiggleLabScene.Remove(root);
            root = JiggleLabScene.Root("Jiggle Lab - Lattice");
            home = new Vector3(0f, 1f, 0f);
            GameObject cube = JiggleLabScene.CubeProp(root.transform, "Lattice cube", home, 20, 0.5f);
            prop = cube.transform;
            lattice = cube.AddComponent<JiggleLattice>();
            Selection.activeGameObject = cube;
            motionStart = EditorApplication.timeSinceStartup;
            elapsed = 0f;
        }

        // How far the simulated control points are from the places the pose has them, the largest of them, in meters.
        private static string Reach()
        {
            int nx;
            int ny;
            int nz;
            Vector3 min;
            Vector3 size;
            if (lattice == null || !lattice.IsSimulating || !lattice.TryGetDivisions(out nx, out ny, out nz) || !lattice.TryGetBox(out min, out size))
            {
                return "-";
            }
            if (lattice.ControlPointCount != nx * ny * nz)
            {
                return "-";
            }
            JVec3 lo = new JVec3(min.x, min.y, min.z);
            JVec3 extent = new JVec3(size.x, size.y, size.z);
            float best = 0f;
            for (int k = 0; k < nz; k++)
            {
                for (int j = 0; j < ny; j++)
                {
                    for (int i = 0; i < nx; i++)
                    {
                        JVec3 r = JiggleLatticeGrid.PointRest(i, j, k, nx, ny, nz, lo, extent);
                        Vector3 rest = lattice.transform.TransformPoint(new Vector3(r.X, r.Y, r.Z));
                        Vector3 now;
                        if (!lattice.TryGetControlPoint(JiggleLatticeGrid.PointIndex(i, j, k, nx, ny), out now))
                        {
                            return "-";
                        }
                        float d = Vector3.Distance(rest, now);
                        if (d > best)
                        {
                            best = d;
                        }
                    }
                }
            }
            return best.ToString("F3", CultureInfo.InvariantCulture) + " m";
        }

        /// <summary>Moves the cube. Called from the editor update, so it runs every frame of the editor.</summary>
        internal static void Tick()
        {
            if (!Application.isPlaying || root == null || prop == null)
            {
                return;
            }
            if (motion == 0)
            {
                motionStart = EditorApplication.timeSinceStartup - (double)elapsed;
                return;
            }
            float t = (float)(EditorApplication.timeSinceStartup - motionStart);
            elapsed = t;
            if (motion == 1)
            {
                // A sine clipped flat at the ends, so the cube runs, then stops at once.
                float x = Mathf.Clamp(Mathf.Sin(t * speed * 2f) * 3f, -1f, 1f) * amplitude;
                prop.position = new Vector3(home.x + x, home.y, home.z);
            }
            else if (motion == 2)
            {
                prop.rotation = Quaternion.Euler(0f, t * speed * 90f, 0f);
            }
            else
            {
                float y = Mathf.Abs(Mathf.Sin(t * speed * 3f)) * amplitude;
                prop.position = new Vector3(home.x, home.y + y, home.z);
            }
        }
    }
}
