// ============================================================================
// NOTICE: Full documentation, design decisions, and fix history for this file
// live in docs/com.midmanstudio.allthingsjiggle.md, section "Runtime/Unity/JiggleRuntimeTypes.cs"
// ============================================================================
namespace MidManStudio.AllThingsJiggle
{
    /// <summary>Which implementation of the solver runs the chains.</summary>
    public enum JiggleRuntimeBackend
    {
        /// <summary>The managed kernel compiled by Burst, run in parallel jobs. The default.</summary>
        Burst = 0,
        /// <summary>The Rust library through the C interface, called from the main thread. Falls back to Burst when the library does not load.</summary>
        Native = 1,
        /// <summary>The managed reference kernel on the main thread, not compiled by Burst.</summary>
        Managed = 2,
    }

    /// <summary>Where the pose a chain is held to comes from.</summary>
    public enum JigglePoseSource
    {
        /// <summary>
        /// The pose the bones had when the chain was enabled, carried along by the anchor. Right for bones no
        /// animation touches. The simulation never reads its own output back.
        /// </summary>
        Bind = 0,
        /// <summary>
        /// The pose the bones have each frame before the simulation runs, as an animator or another script left it.
        /// Right when the bones are animated. If nothing rewrites the bones each frame, the chain reads its own last
        /// result as the pose.
        /// </summary>
        Animated = 1,
    }

    /// <summary>What the last frame of the manager did. A plain copy, safe to keep.</summary>
    public struct JiggleFrameStats
    {
        /// <summary>Rigs simulated, chains and lattices.</summary>
        public int Rigs;
        /// <summary>How many of the rigs are lattices.</summary>
        public int Lattices;
        /// <summary>Meshes of lattices that were given new vertices in the last frame. A lattice at rest, or asleep, gives none.</summary>
        public int LatticeUploads;
        /// <summary>Particles over all rigs.</summary>
        public int Particles;
        /// <summary>Fixed steps run in the last frame.</summary>
        public uint Steps;
        /// <summary>Time of the last frame in milliseconds, from scheduling the first job to the last bone written.</summary>
        public float FrameMs;
        /// <summary>The backend that ran, which is not the one asked for when the native library did not load.</summary>
        public JiggleRuntimeBackend Backend;
        /// <summary>0, or the negative status code the solver gave.</summary>
        public int Status;
        /// <summary>How many times the arrays were rebuilt because chains came or went.</summary>
        public int Rebuilds;
        /// <summary>Colliders written to the solver in the last frame.</summary>
        public int Colliders;
        /// <summary>Collider indices in the lists of all rigs: how many collider tests a particle may make per step, summed over rigs.</summary>
        public int ColliderRefs;
        /// <summary>Force fields written to the solver in the last frame, one shot impulses included.</summary>
        public int Fields;
        /// <summary>Rigs that were not testing colliders because of their level of detail.</summary>
        public int LodNoCollision;
        /// <summary>Rigs that were not simulated because of their level of detail.</summary>
        public int LodOff;
        /// <summary>Rigs that were asleep after the last frame.</summary>
        public int Asleep;
    }
}
