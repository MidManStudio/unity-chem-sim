// ============================================================================
// NOTICE: Full documentation, design decisions, and fix history for this file
// live in docs/com.midmanstudio.allthingsjiggle.md, section "Runtime/Unity/JiggleLattice.cs"
// ============================================================================
using UnityEngine;

namespace MidManStudio.AllThingsJiggle
{
    /// <summary>Which control point of a lattice is held by the object and never moves from its place.</summary>
    public enum JiggleLatticeAnchor
    {
        /// <summary>The control point nearest the middle of the box. The whole body wobbles around its centre.</summary>
        Center = 0,
        /// <summary>
        /// The control point nearest the pivot of the object, which is where the object is held. A prop that stands on
        /// its pivot stays rooted there and the rest of it sways.
        /// </summary>
        Pivot = 1,
    }

    /// <summary>
    /// A jiggling lattice around an unskinned mesh. Add it to the object that has the MeshFilter and the renderer. The mesh
    /// is put in a box of 1 to 5 control points on each axis, the control points are simulated, and the vertices move with
    /// them, so a prop or an enemy wobbles as one body. Every control point pulls its neighbours (see
    /// <see cref="JiggleLatticeSettings.mass"/>), so a hit travels through the body. The component only describes the
    /// lattice. The <see cref="JiggleManager"/> simulates it with the chains, and shares the force fields, impulses, sleep and
    /// level of detail with them. The mesh must be readable, and a mesh that is skinned, or batched by static batching,
    /// cannot be deformed.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(MeshFilter))]
    [AddComponentMenu("MidManStudio/AllThingsJiggle/Jiggle Lattice")]
    public sealed class JiggleLattice : MonoBehaviour
    {
        /// <summary>The values of the control points.</summary>
        public JiggleLatticeSettings settings = new JiggleLatticeSettings();

        /// <summary>Control points along the x axis of the mesh, 1 to 5. An axis the mesh has no size on gets 1 whatever this says.</summary>
        [Range(1, JiggleLatticeGrid.MaxDivisions)] public int divisionsX = 3;

        /// <summary>Control points along the y axis of the mesh, 1 to 5.</summary>
        [Range(1, JiggleLatticeGrid.MaxDivisions)] public int divisionsY = 3;

        /// <summary>Control points along the z axis of the mesh, 1 to 5.</summary>
        [Range(1, JiggleLatticeGrid.MaxDivisions)] public int divisionsZ = 3;

        /// <summary>
        /// How far the box reaches past the bounds of the mesh on each side, as a share of the size of the bounds on that
        /// axis. 0 puts the box on the bounds. Only the control points at the corners of the box lie on the deformed surface,
        /// so a wider box makes the control points at the corners move the mesh less.
        /// </summary>
        [Range(0f, 1f)] public float padding = 0f;

        /// <summary>Which control point is held by the object. See <see cref="JiggleLatticeAnchor"/>.</summary>
        public JiggleLatticeAnchor anchorPoint = JiggleLatticeAnchor.Center;

        /// <summary>
        /// Whether the lattice may fall asleep when it is at rest, which costs nothing while it lasts. Sleeping also has to be
        /// turned on in the manager (its sleep frames).
        /// </summary>
        public bool allowSleep = true;

        /// <summary>How the lattice is simulated at a distance and when it is not seen. Follows the manager by default.</summary>
        public JiggleChainLod lod = new JiggleChainLod();

        private JiggleRuntime runtime;
        private int rig = -1;
        private JiggleLatticeMesh meshData;
        private JiggleLatticeTree tree;
        private JVec3[] restLocal = new JVec3[0];
        private float builtPadding;

        // Counts the times what the rig is built from changed: the mesh, the box, the grid or the anchor. A rebuild carries the
        // state of the rig over only while this is the same as it was when the rig was attached.
        internal int BuildStamp;
        internal int AttachedStamp;

        // What the level of detail remembers about this lattice. It stays with the lattice when the arrays are rebuilt.
        internal JiggleLodTracker LodTracker;

        /// <summary>Whether the manager simulates this lattice now.</summary>
        public bool IsSimulating
        {
            get { return runtime != null && rig >= 0; }
        }

        /// <summary>How many control points the lattice has as simulated, 0 while it is not.</summary>
        public int ControlPointCount
        {
            get { return IsSimulating && tree != null ? tree.Count : 0; }
        }

        /// <summary>How much of the lattice the manager simulates now. See <see cref="lod"/>.</summary>
        public JiggleLodLevel LodLevel
        {
            get { return LodTracker.Level; }
        }

        /// <summary>Whether the lattice is asleep: at rest and not simulated until something moves it. See <see cref="allowSleep"/>.</summary>
        public bool IsAsleep
        {
            get { return IsSimulating && runtime.Host.IsAsleep(rig); }
        }

        internal JiggleRuntime Runtime
        {
            get { return runtime; }
        }

        internal int RigIndex
        {
            get { return rig; }
        }

        internal JiggleLatticeTree Tree
        {
            get { return tree; }
        }

        internal JVec3[] RestLocal
        {
            get { return restLocal; }
        }

        internal JiggleLatticeMesh MeshData
        {
            get { return meshData; }
        }

        // The limits this lattice runs with, given the limits and the hysteresis of the manager.
        internal JiggleLodSettings ResolveLod(JiggleLodSettings managerLimits, float hysteresis)
        {
            return lod == null ? managerLimits : lod.Resolve(managerLimits, hysteresis);
        }

        internal void Attach(JiggleRuntime owner, int rigIndexInOwner)
        {
            runtime = owner;
            rig = rigIndexInOwner;
            AttachedStamp = BuildStamp;
        }

        internal void Detach()
        {
            runtime = null;
            rig = -1;
        }

        private void OnEnable()
        {
            LodTracker = new JiggleLodTracker();
            JiggleManager m = JiggleManager.Instance;
            if (m != null)
            {
                m.Register(this);
            }
        }

        private void OnDisable()
        {
            JiggleManager m = JiggleManager.TryGet();
            if (m != null)
            {
                m.Unregister(this);
            }
            Detach();
            ReleaseMesh();
        }

        private void OnDestroy()
        {
            ReleaseMesh();
        }

        private void OnValidate()
        {
            if (lod == null)
            {
                lod = new JiggleChainLod();
            }
            if (settings == null)
            {
                settings = new JiggleLatticeSettings();
            }
            divisionsX = JiggleLatticeGrid.ClampDivisions(divisionsX);
            divisionsY = JiggleLatticeGrid.ClampDivisions(divisionsY);
            divisionsZ = JiggleLatticeGrid.ClampDivisions(divisionsZ);
            padding = Mathf.Clamp01(padding);
            if (Application.isPlaying && isActiveAndEnabled)
            {
                SettingsChanged();
            }
        }

        /// <summary>
        /// Tells the manager the settings, the grid, the padding or the anchor changed, so it bakes them again before the next
        /// frame. The inspector does it by itself. A script that edits <see cref="settings"/> calls it.
        /// </summary>
        public void SettingsChanged()
        {
            JiggleManager m = JiggleManager.TryGet();
            if (m != null && isActiveAndEnabled)
            {
                m.NotifyChanged(this);
            }
        }

        /// <summary>
        /// Makes the lattice snap to its pose on the next frame, without a swing. Use it after moving the object somewhere
        /// without it travelling there, for example when an object is recycled.
        /// </summary>
        public void ResetSimulation()
        {
            JiggleManager m = JiggleManager.TryGet();
            if (m != null && isActiveAndEnabled)
            {
                m.RequestReset(this);
            }
        }

        /// <summary>Wakes the lattice if it is asleep, so it is simulated from the next frame. Does nothing while it is not simulated.</summary>
        public void Wake()
        {
            if (IsSimulating)
            {
                runtime.Host.Wake(rig);
            }
        }

        /// <summary>
        /// Hits the whole lattice: changes the velocity of every control point by <paramref name="velocityChange"/>, in meters
        /// per second, times the impulse response. Right for a shove or a landing. A sleeping lattice wakes. Returns false
        /// and does nothing when the lattice is not simulated, is switched off by its level of detail, or is about to snap to
        /// its pose after a reset.
        /// </summary>
        public bool AddImpulse(Vector3 velocityChange)
        {
            if (!IsSimulating)
            {
                return false;
            }
            return JiggleImpulse.Hit(runtime.Host, rig, new JVec3(velocityChange.x, velocityChange.y, velocityChange.z));
        }

        /// <summary>
        /// Hits the control points near a point in world space: full at the point, fading to nothing at
        /// <paramref name="radius"/>, by <paramref name="falloff"/> as the force fields do (0 does not fade, 1 is linear). A
        /// radius of 0 hits the whole lattice. Returns whether any control point was hit, with the same refusals as
        /// <see cref="AddImpulse"/>.
        /// </summary>
        public bool AddImpulseAt(Vector3 point, Vector3 velocityChange, float radius, int falloff = 1)
        {
            if (!IsSimulating)
            {
                return false;
            }
            return JiggleImpulse.HitAt(
                runtime.Host, rig, new JVec3(point.x, point.y, point.z), radius, (uint)Mathf.Clamp(falloff, 0, 4),
                new JVec3(velocityChange.x, velocityChange.y, velocityChange.z));
        }

        /// <summary>
        /// The box of the lattice in the space of the mesh: its smallest corner and its size. It is the bounds of the mesh
        /// widened by <see cref="padding"/>. False when there is no mesh to take it from.
        /// </summary>
        public bool TryGetBox(out Vector3 min, out Vector3 size)
        {
            min = Vector3.zero;
            size = Vector3.zero;
            JVec3 m;
            JVec3 s;
            if (!ResolveBox(out m, out s))
            {
                return false;
            }
            min = new Vector3(m.X, m.Y, m.Z);
            size = new Vector3(s.X, s.Y, s.Z);
            return true;
        }

        /// <summary>
        /// The control points on each axis as the lattice would use them: <see cref="divisionsX"/> and the others, with an axis
        /// the mesh has no size on counted as 1. False when there is no mesh to take the box from.
        /// </summary>
        public bool TryGetDivisions(out int nx, out int ny, out int nz)
        {
            nx = 1;
            ny = 1;
            nz = 1;
            JVec3 m;
            JVec3 s;
            if (!ResolveBox(out m, out s))
            {
                return false;
            }
            Divisions(s, out nx, out ny, out nz);
            return true;
        }

        /// <summary>
        /// The world position of grid point <paramref name="point"/> after the last frame, where the grid index is
        /// x + nx * (y + ny * z). False when the lattice is not simulated or the index is outside the grid.
        /// </summary>
        public bool TryGetControlPoint(int point, out Vector3 world)
        {
            world = Vector3.zero;
            if (!IsSimulating || tree == null || point < 0 || point >= tree.Count)
            {
                return false;
            }
            JVec3 p = runtime.Host.WorldPos(rig, tree.ParticleOfPoint[point]);
            world = new Vector3(p.X, p.Y, p.Z);
            return true;
        }

        private bool ResolveBox(out JVec3 min, out JVec3 size)
        {
            min = JVec3.Zero;
            size = JVec3.Zero;
            if (meshData != null)
            {
                min = meshData.Min;
                size = meshData.Size;
                return true;
            }
            MeshFilter filter = GetComponent<MeshFilter>();
            Mesh mesh = filter != null ? filter.sharedMesh : null;
            if (mesh == null)
            {
                return false;
            }
            JiggleLatticeMesh.BoxOf(mesh.bounds, padding, out min, out size);
            return true;
        }

        // An axis the box has no size on holds one control point: points that lie on one another have no direction between them,
        // and the solver would turn their neighbours by a swing that is not there. A very thin axis counts as flat too.
        private static int Effective(int wanted, float extent, float largest)
        {
            int n = JiggleLatticeGrid.ClampDivisions(wanted);
            return extent > largest * 1.0e-3f && extent > 1.0e-6f ? n : 1;
        }

        private void Divisions(JVec3 size, out int nx, out int ny, out int nz)
        {
            float largest = size.X;
            if (size.Y > largest)
            {
                largest = size.Y;
            }
            if (size.Z > largest)
            {
                largest = size.Z;
            }
            nx = Effective(divisionsX, size.X, largest);
            ny = Effective(divisionsY, size.Y, largest);
            nz = Effective(divisionsZ, size.Z, largest);
        }

        /// <summary>
        /// Takes the mesh and builds the grid and its tree. Takes a private copy of the mesh when there is none or the mesh is
        /// not the one it had. Returns false with the reason when the lattice cannot be simulated.
        /// </summary>
        internal bool Prepare(out string problem)
        {
            problem = null;
            if (settings == null)
            {
                settings = new JiggleLatticeSettings();
            }
            if (lod == null)
            {
                lod = new JiggleChainLod();
            }
            MeshFilter filter = GetComponent<MeshFilter>();
            if (filter == null)
            {
                problem = "it has no MeshFilter.";
                return false;
            }
            if (meshData != null && (filter.sharedMesh != meshData.Instance || builtPadding != padding))
            {
                ReleaseMesh();
            }
            if (meshData == null)
            {
                JiggleLatticeMesh made = JiggleLatticeMesh.Create(filter, padding, out problem);
                if (made == null)
                {
                    return false;
                }
                meshData = made;
                builtPadding = padding;
                BuildStamp++;
                tree = null;
            }
            JVec3 min = meshData.Min;
            JVec3 size = meshData.Size;
            int nx;
            int ny;
            int nz;
            Divisions(size, out nx, out ny, out nz);
            if (nx * ny * nz < JiggleLatticeGrid.MinPoints)
            {
                problem = "the box of its mesh has no size on two axes, so there is only one control point. A lattice needs at least two.";
                return false;
            }
            int root = anchorPoint == JiggleLatticeAnchor.Pivot ? JiggleLatticeGrid.NearestPoint(nx, ny, nz, min, size, JVec3.Zero) : -1;
            JiggleLatticeTree t = JiggleLatticeGrid.BuildTree(nx, ny, nz, root);
            if (tree == null || tree.Nx != t.Nx || tree.Ny != t.Ny || tree.Nz != t.Nz || tree.RootPoint != t.RootPoint)
            {
                BuildStamp++;
            }
            tree = t;
            restLocal = JiggleLatticeGrid.ParticleRest(t, min, size);
            return true;
        }

        /// <summary>The rest pose in world space for building the rig: one position for each control point, the anchor first.</summary>
        internal JVec3[] RestWorld()
        {
            JVec3[] world = new JVec3[restLocal.Length];
            Transform t = transform;
            for (int i = 0; i < world.Length; i++)
            {
                JVec3 r = restLocal[i];
                Vector3 w = t.TransformPoint(new Vector3(r.X, r.Y, r.Z));
                world[i] = new JVec3(w.x, w.y, w.z);
            }
            return world;
        }

        /// <summary>Where the anchor control point is in the world now.</summary>
        internal Vector3 AnchorWorldNow()
        {
            if (restLocal.Length == 0)
            {
                return transform.position;
            }
            JVec3 r = restLocal[0];
            return transform.TransformPoint(new Vector3(r.X, r.Y, r.Z));
        }

        // Gives the mesh back and frees what the jobs wrote into. The arrays of the manager that point into it are never run
        // again: whatever calls this has already made the manager rebuild them.
        private void ReleaseMesh()
        {
            if (meshData != null)
            {
                meshData.Dispose();
                meshData = null;
            }
        }
    }
}
