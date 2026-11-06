// ============================================================================
// NOTICE: Full documentation, design decisions, and fix history for this file
// live in docs/com.midmanstudio.allthingsjiggle.md, section "Runtime/Core/JiggleConst.cs"
// ============================================================================
namespace MidManStudio.AllThingsJiggle
{
    /// <summary>
    /// The constants of the native interface, with the same names and values as
    /// the Rust crate (`types.rs` and `ffi/mod.rs`).
    /// </summary>
    public static class JiggleConst
    {
        /// <summary>Largest particle count of one rig, anchor and virtual end particles included.</summary>
        public const int MaxParticles = 128;

        /// <summary>Most coupling passes one step runs, see <see cref="JiggleStepConfig.CouplingPasses"/>.</summary>
        public const uint MaxCouplingPasses = 16;

        /// <summary>
        /// Largest mass ratio between a particle and its parent that the coupling uses. A link whose masses differ
        /// more is split as if they differed by exactly this much. Position based solvers become erratic well
        /// beyond it: a pinned strand stays smooth to about 64 to 1, a soft one starts to ring past 32 to 1.
        /// </summary>
        public const float MaxMassRatio = 16.0f;

        /// <summary>Particle flag: follows the pose exactly, never simulated. Particle 0 always has it.</summary>
        public const uint FlagKinematic = 1;
        /// <summary>Particle flag: its parent is the anchor, so the root spring holds it to the pose.</summary>
        public const uint FlagRoot = 2;
        /// <summary>Particle flag: has no transform of its own, only gives the last bone a direction.</summary>
        public const uint FlagVirtual = 4;
        /// <summary>Particle flag: the angle limit applies.</summary>
        public const uint FlagAngleLimit = 8;
        /// <summary>Particle flag: collides with the rig's colliders.</summary>
        public const uint FlagCollide = 16;
        /// <summary>Particle flag: tests the bone segment, not only the particle, against colliders.</summary>
        public const uint FlagSegment = 32;

        /// <summary>Rig flag: skip this rig completely.</summary>
        public const uint RigDisabled = 1;
        /// <summary>Rig flag: asleep, so the output is the pose. The solver sets and clears it.</summary>
        public const uint RigAsleep = 2;
        /// <summary>Rig flag: snap to the pose on the next step. The solver clears it.</summary>
        public const uint RigReset = 4;

        /// <summary>Collider kind: sphere.</summary>
        public const uint ColliderSphere = 0;
        /// <summary>Collider kind: capsule along an axis.</summary>
        public const uint ColliderCapsule = 1;
        /// <summary>Collider kind: one sided plane, the normal side is free.</summary>
        public const uint ColliderPlane = 2;
        /// <summary>Collider kind: oriented box, optionally rounded.</summary>
        public const uint ColliderBox = 3;
        /// <summary>Collider kind: particles are kept inside the sphere.</summary>
        public const uint ColliderInsideSphere = 4;

        /// <summary>Field kind: a constant push along a direction, optionally limited to a sphere and gusty.</summary>
        public const uint FieldDirectional = 0;
        /// <summary>Field kind: pushes away from a center, or pulls toward it when the strength is negative.</summary>
        public const uint FieldRadial = 1;
        /// <summary>Field kind: pushes around an axis through the center.</summary>
        public const uint FieldVortex = 2;

        /// <summary>Field mode: strength is an acceleration, applied every step.</summary>
        public const uint FieldAcceleration = 0;
        /// <summary>Field mode: strength is a velocity change, applied on the first step of a frame only.</summary>
        public const uint FieldImpulse = 1;

        /// <summary>Success.</summary>
        public const int Ok = 0;
        /// <summary>A required address was null.</summary>
        public const int ErrNull = -1;
        /// <summary>A range or an offset is outside its array.</summary>
        public const int ErrRange = -2;
        /// <summary>A panic was caught in the native library.</summary>
        public const int ErrPanic = -3;
        /// <summary>A rig has a bad particle count, a bad collider list, overlaps another rig or has a bad topology.</summary>
        public const int ErrBadRig = -4;
        /// <summary>An argument was not valid.</summary>
        public const int ErrArg = -5;

        /// <summary>Struct kind for the native size and layout probe: <see cref="JiggleParticleDef"/>.</summary>
        public const int KindParticleDef = 1;
        /// <summary>Struct kind: <see cref="JiggleParticleState"/>.</summary>
        public const int KindParticleState = 2;
        /// <summary>Struct kind: <see cref="JiggleRig"/>.</summary>
        public const int KindRig = 3;
        /// <summary>Struct kind: <see cref="JiggleStepConfig"/>.</summary>
        public const int KindStepConfig = 4;
        /// <summary>Struct kind: <see cref="JiggleCollider"/>.</summary>
        public const int KindCollider = 5;
        /// <summary>Struct kind: <see cref="JiggleField"/>.</summary>
        public const int KindField = 6;
        /// <summary>Struct kind: <see cref="JiggleParticleOut"/>.</summary>
        public const int KindParticleOut = 7;
        /// <summary>Struct kind: <see cref="JiggleSolverArgs"/>.</summary>
        public const int KindSolverArgs = 8;

        /// <summary>The native interface version this code was written against.</summary>
        public const int ExpectedAbiVersion = 2;
    }
}
