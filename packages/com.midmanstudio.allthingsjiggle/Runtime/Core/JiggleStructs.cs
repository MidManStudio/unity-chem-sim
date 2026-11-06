// ============================================================================
// NOTICE: Full documentation, design decisions, and fix history for this file
// live in docs/com.midmanstudio.allthingsjiggle.md, section "Runtime/Core/JiggleStructs.cs"
// ============================================================================
// The fields of these structs are shared with native code and hold padding
// and reserved slots, so the compiler sees fields that are never assigned
// (CS0649) or never read (CS0169).
#pragma warning disable 0649, 0169
using System.Runtime.InteropServices;

namespace MidManStudio.AllThingsJiggle
{
    /// <summary>
    /// What one particle is and how it is held to its parent. Built once per rig.
    /// The numbers are baked: the authoring values in 0 to 1 are already turned
    /// into what the solver uses (see <see cref="JiggleParams"/>).
    /// </summary>
    [StructLayout(LayoutKind.Explicit, Size = 80)]
    public struct JiggleParticleDef
    {
        /// <summary>Index of the parent inside the rig, smaller than this particle's own index, or -1 for particle 0.</summary>
        [FieldOffset(0)] public int Parent;
        /// <summary>FLAG bits, see <see cref="JiggleConst"/>.</summary>
        [FieldOffset(4)] public uint Flags;
        [FieldOffset(8)] public uint Reserved0;
        [FieldOffset(12)] public uint Reserved1;
        /// <summary>Pull toward the shape of the parent's pose each step, 0 to 1.</summary>
        [FieldOffset(16)] public float AngleElasticity;
        /// <summary>Pull toward the pose length of the bone each step, 0 to 1.</summary>
        [FieldOffset(20)] public float LengthElasticity;
        /// <summary>How much the angle pull fades near the pose, 0 to 1.</summary>
        [FieldOffset(24)] public float Soften;
        /// <summary>Pull of a root particle toward its pose position, 0 to 1.</summary>
        [FieldOffset(28)] public float RootElasticity;
        /// <summary>Cosine of the angle limit.</summary>
        [FieldOffset(32)] public float LimitCos;
        /// <summary>Sine of the angle limit.</summary>
        [FieldOffset(36)] public float LimitSin;
        /// <summary>How softly the angle limit corrects, 0 to 1.</summary>
        [FieldOffset(40)] public float LimitSoften;
        /// <summary>Share of the motion relative to the parent that is lost each step, 0 to 1.</summary>
        [FieldOffset(44)] public float Drag;
        /// <summary>Share of the parent's own motion that is not followed each step, 0 to 1.</summary>
        [FieldOffset(48)] public float AirDrag;
        /// <summary>Multiplier on the gravity of the step.</summary>
        [FieldOffset(52)] public float Gravity;
        /// <summary>Collision radius in world units.</summary>
        [FieldOffset(56)] public float CollisionRadius;
        /// <summary>How much of the swing is applied to the output rotation, 0 to 1.</summary>
        [FieldOffset(60)] public float Blend;
        /// <summary>Share of the anchor's motion that moves this particle along with it, 0 to 1.</summary>
        [FieldOffset(64)] public float IgnoreRootMotion;
        /// <summary>Multiplier on acceleration fields.</summary>
        [FieldOffset(68)] public float WindResponse;
        /// <summary>Multiplier on impulse fields.</summary>
        [FieldOffset(72)] public float ImpulseResponse;
        [FieldOffset(76)] public float Reserved2;
    }

    /// <summary>The simulated state of one particle, relative to the rig anchor of the last step.</summary>
    [StructLayout(LayoutKind.Explicit, Size = 32)]
    public struct JiggleParticleState
    {
        /// <summary>Position now.</summary>
        [FieldOffset(0)] public JVec4 Pos;
        /// <summary>Position one step ago.</summary>
        [FieldOffset(16)] public JVec4 Prev;
    }

    /// <summary>One rig: the header the host fills before each frame. The solver only touches Flags and SleepCounter.</summary>
    [StructLayout(LayoutKind.Explicit, Size = 64)]
    public struct JiggleRig
    {
        /// <summary>First particle of this rig in the shared particle arrays.</summary>
        [FieldOffset(0)] public uint ParticleOffset;
        /// <summary>Number of particles, 2 to <see cref="JiggleConst.MaxParticles"/>.</summary>
        [FieldOffset(4)] public uint ParticleCount;
        /// <summary>First entry of this rig's collider list in the shared reference array.</summary>
        [FieldOffset(8)] public uint RefOffset;
        /// <summary>Number of colliders in the list.</summary>
        [FieldOffset(12)] public uint RefCount;
        /// <summary>RIG bits, see <see cref="JiggleConst"/>.</summary>
        [FieldOffset(16)] public uint Flags;
        /// <summary>Frames in a row the rig has been at rest.</summary>
        [FieldOffset(20)] public uint SleepCounter;
        /// <summary>Farthest distance of any particle from the anchor, for field tests.</summary>
        [FieldOffset(24)] public float Extent;
        [FieldOffset(28)] public uint Reserved0;
        /// <summary>X of how far the anchor moved in the world since the last frame.</summary>
        [FieldOffset(32)] public float AnchorDeltaX;
        /// <summary>Y of the anchor move.</summary>
        [FieldOffset(36)] public float AnchorDeltaY;
        /// <summary>Z of the anchor move.</summary>
        [FieldOffset(40)] public float AnchorDeltaZ;
        [FieldOffset(44)] public float Reserved1;
        /// <summary>X of the world position of the anchor now.</summary>
        [FieldOffset(48)] public float AnchorWorldX;
        /// <summary>Y of the anchor position.</summary>
        [FieldOffset(52)] public float AnchorWorldY;
        /// <summary>Z of the anchor position.</summary>
        [FieldOffset(56)] public float AnchorWorldZ;
        [FieldOffset(60)] public float Reserved2;

        /// <summary>The anchor move as a vector.</summary>
        public JVec3 AnchorDelta
        {
            get { return new JVec3(AnchorDeltaX, AnchorDeltaY, AnchorDeltaZ); }
            set { AnchorDeltaX = value.X; AnchorDeltaY = value.Y; AnchorDeltaZ = value.Z; }
        }

        /// <summary>The anchor position as a vector.</summary>
        public JVec3 AnchorWorld
        {
            get { return new JVec3(AnchorWorldX, AnchorWorldY, AnchorWorldZ); }
            set { AnchorWorldX = value.X; AnchorWorldY = value.Y; AnchorWorldZ = value.Z; }
        }
    }

    /// <summary>Settings of one call.</summary>
    [StructLayout(LayoutKind.Explicit, Size = 64)]
    public struct JiggleStepConfig
    {
        /// <summary>Fixed step length in seconds.</summary>
        [FieldOffset(0)] public float Dt;
        /// <summary>Number of fixed steps to run for each rig in this frame.</summary>
        [FieldOffset(4)] public uint Steps;
        /// <summary>Time at the start of the frame in seconds, for gusts.</summary>
        [FieldOffset(8)] public float Time;
        /// <summary>How far between the last two steps to show, 0 to 1. Used by resolve.</summary>
        [FieldOffset(12)] public float Alpha;
        /// <summary>X of gravity in world space, meters per second squared.</summary>
        [FieldOffset(16)] public float GravityX;
        /// <summary>Y of gravity.</summary>
        [FieldOffset(20)] public float GravityY;
        /// <summary>Z of gravity.</summary>
        [FieldOffset(24)] public float GravityZ;
        /// <summary>An anchor move farther than this in one frame snaps the rig to its pose. 0 turns the check off.</summary>
        [FieldOffset(28)] public float TeleportDistance;
        /// <summary>A rig is at rest only while every particle moves slower than this, in meters per step.</summary>
        [FieldOffset(32)] public float SleepSpeed;
        /// <summary>A rig is at rest only while every particle is closer to its pose than this, in meters.</summary>
        [FieldOffset(36)] public float SleepDeviation;
        /// <summary>Frames at rest before a rig falls asleep. 0 turns sleeping off.</summary>
        [FieldOffset(40)] public uint SleepFrames;
        /// <summary>A sleeping rig wakes when the anchor or the pose moves farther than this in one frame, in meters.</summary>
        [FieldOffset(44)] public float WakeMotion;
        [FieldOffset(48)] public float Reserved0;
        [FieldOffset(52)] public float Reserved1;
        [FieldOffset(56)] public float Reserved2;
        [FieldOffset(60)] public float Reserved3;

        /// <summary>Gravity as a vector.</summary>
        public JVec3 Gravity
        {
            get { return new JVec3(GravityX, GravityY, GravityZ); }
            set { GravityX = value.X; GravityY = value.Y; GravityZ = value.Z; }
        }
    }

    /// <summary>A collider in world space.</summary>
    [StructLayout(LayoutKind.Explicit, Size = 80)]
    public struct JiggleCollider
    {
        /// <summary>Kind, see <see cref="JiggleConst"/>.</summary>
        [FieldOffset(0)] public uint Kind;
        [FieldOffset(4)] public uint Flags;
        /// <summary>Sphere, capsule or inside sphere radius, or the rounding radius of a box.</summary>
        [FieldOffset(8)] public float Radius;
        /// <summary>Half of the length of the capsule's axis between the two end sphere centers.</summary>
        [FieldOffset(12)] public float HalfHeight;
        /// <summary>Center in world space.</summary>
        [FieldOffset(16)] public JVec4 Center;
        /// <summary>Unit axis of a capsule, or the unit normal of a plane.</summary>
        [FieldOffset(32)] public JVec4 Axis;
        /// <summary>Half sizes of a box.</summary>
        [FieldOffset(48)] public JVec4 Extents;
        /// <summary>Orientation of a box as x, y, z, w.</summary>
        [FieldOffset(64)] public JVec4 Rot;
    }

    /// <summary>A force field in world space.</summary>
    [StructLayout(LayoutKind.Explicit, Size = 64)]
    public struct JiggleField
    {
        /// <summary>Kind, see <see cref="JiggleConst"/>.</summary>
        [FieldOffset(0)] public uint Kind;
        /// <summary>Acceleration or impulse.</summary>
        [FieldOffset(4)] public uint Mode;
        /// <summary>Exponent of the falloff from the center to the radius, 0 to 4. 0 is constant inside the radius.</summary>
        [FieldOffset(8)] public uint Power;
        [FieldOffset(12)] public uint Reserved;
        /// <summary>Center in world space.</summary>
        [FieldOffset(16)] public JVec4 Center;
        /// <summary>Direction, or the axis of a vortex. The fourth float is the spatial frequency of gusts.</summary>
        [FieldOffset(32)] public JVec4 Dir;
        /// <summary>Reach of the field. A directional field with 0 reaches everywhere.</summary>
        [FieldOffset(48)] public float Radius;
        /// <summary>Acceleration or velocity change.</summary>
        [FieldOffset(52)] public float Strength;
        /// <summary>Share of the strength added as random gusts, 0 for none.</summary>
        [FieldOffset(56)] public float Turbulence;
        /// <summary>Gusts per second.</summary>
        [FieldOffset(60)] public float Frequency;
    }

    /// <summary>What resolve gives back for one particle.</summary>
    [StructLayout(LayoutKind.Explicit, Size = 32)]
    public struct JiggleParticleOut
    {
        /// <summary>Position relative to the anchor of this frame. Add the anchor's world position for a world position.</summary>
        [FieldOffset(0)] public JVec4 Pos;
        /// <summary>World rotation as x, y, z, w.</summary>
        [FieldOffset(16)] public JVec4 Rot;
    }

    /// <summary>
    /// The addresses and counts of one solver call. The addresses are 64 bit so the layout does not
    /// depend on the pointer size of the target.
    /// </summary>
    [StructLayout(LayoutKind.Explicit, Size = 112)]
    public struct JiggleSolverArgs
    {
        /// <summary>Address of RigCount rig records.</summary>
        [FieldOffset(0)] public ulong Rigs;
        /// <summary>Address of ParticleCount particle definitions.</summary>
        [FieldOffset(8)] public ulong Defs;
        /// <summary>Address of ParticleCount particle states.</summary>
        [FieldOffset(16)] public ulong State;
        /// <summary>Address of ParticleCount pose positions of the last frame, four floats each.</summary>
        [FieldOffset(24)] public ulong PosePrev;
        /// <summary>Address of ParticleCount pose positions of this frame, four floats each.</summary>
        [FieldOffset(32)] public ulong PoseCurr;
        /// <summary>Address of ParticleCount pose rotations of this frame, four floats each.</summary>
        [FieldOffset(40)] public ulong PoseRot;
        /// <summary>Address of ParticleCount output records.</summary>
        [FieldOffset(48)] public ulong Out;
        /// <summary>Address of ColliderCount colliders.</summary>
        [FieldOffset(56)] public ulong Colliders;
        /// <summary>Address of RefCount collider indices.</summary>
        [FieldOffset(64)] public ulong Refs;
        /// <summary>Address of FieldCount fields.</summary>
        [FieldOffset(72)] public ulong Fields;
        /// <summary>Number of rigs.</summary>
        [FieldOffset(80)] public uint RigCount;
        /// <summary>Number of particles over all rigs.</summary>
        [FieldOffset(84)] public uint ParticleCount;
        /// <summary>Number of colliders.</summary>
        [FieldOffset(88)] public uint ColliderCount;
        /// <summary>Number of collider indices over all rigs.</summary>
        [FieldOffset(92)] public uint RefCount;
        /// <summary>Number of fields.</summary>
        [FieldOffset(96)] public uint FieldCount;
        [FieldOffset(100)] public uint Reserved0;
        [FieldOffset(104)] public uint Reserved1;
        [FieldOffset(108)] public uint Reserved2;
    }
}
