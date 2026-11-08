// ============================================================================
// NOTICE: Full documentation, design decisions, and fix history for this file
// live in docs/com.midmanstudio.allthingsjiggle.md, section "Runtime/Unity/JiggleJobs.cs"
// ============================================================================
using Unity.Burst;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using UnityEngine;
using UnityEngine.Jobs;

namespace MidManStudio.AllThingsJiggle
{
    /// <summary>What the read job takes from one transform: its world position and rotation and its matrix.</summary>
    internal struct JiggleSample
    {
        public JVec3 Pos;
        public JQuat Rot;
        public float M00, M01, M02, M03;
        public float M10, M11, M12, M13;
        public float M20, M21, M22, M23;
    }

    /// <summary>Reads the world pose of every transform that is asked for. Run read only, so it runs in parallel.</summary>
    [BurstCompile(FloatMode = FloatMode.Strict, FloatPrecision = FloatPrecision.Standard)]
    internal unsafe struct JiggleReadJob : IJobParallelForTransform
    {
        /// <summary>Flag: read the position and the rotation.</summary>
        public const byte NeedPose = 1;
        /// <summary>Flag: read the local to world matrix as well.</summary>
        public const byte NeedMatrix = 2;

        [NativeDisableUnsafePtrRestriction] public JiggleSample* Samples;
        [NativeDisableUnsafePtrRestriction] public byte* Flags;

        public void Execute(int index, TransformAccess transform)
        {
            byte f = Flags[index];
            if (f == 0 || !transform.isValid)
            {
                return;
            }
            JiggleSample* s = Samples + index;
            Vector3 p = transform.position;
            Quaternion q = transform.rotation;
            s->Pos = new JVec3(p.x, p.y, p.z);
            s->Rot = new JQuat(q.x, q.y, q.z, q.w);
            if ((f & NeedMatrix) != 0)
            {
                Matrix4x4 m = transform.localToWorldMatrix;
                s->M00 = m.m00; s->M01 = m.m01; s->M02 = m.m02; s->M03 = m.m03;
                s->M10 = m.m10; s->M11 = m.m11; s->M12 = m.m12; s->M13 = m.m13;
                s->M20 = m.m20; s->M21 = m.m21; s->M22 = m.m22; s->M23 = m.m23;
            }
        }
    }

    /// <summary>
    /// Builds the pose of every rig for the frame from the samples and hands it to <see cref="JigglePose.Write"/>.
    /// A rig in bind mode takes the pose its bones had at bind time, carried by the anchor's matrix. A rig in
    /// animated mode takes the samples of its bones.
    /// </summary>
    [BurstCompile(FloatMode = FloatMode.Strict, FloatPrecision = FloatPrecision.Standard)]
    internal unsafe struct JigglePoseJob : IJobParallelFor
    {
        [NativeDisableUnsafePtrRestriction] public JiggleRig* Rigs;
        [NativeDisableUnsafePtrRestriction] public JVec4* PosePrev;
        [NativeDisableUnsafePtrRestriction] public JVec4* PoseCurr;
        [NativeDisableUnsafePtrRestriction] public JVec4* PoseRot;
        [NativeDisableUnsafePtrRestriction] public float* TipScale;
        [NativeDisableUnsafePtrRestriction] public JiggleSample* Samples;
        [NativeDisableUnsafePtrRestriction] public int* RigEntry;
        [NativeDisableUnsafePtrRestriction] public int* EntryTransform;
        [NativeDisableUnsafePtrRestriction] public byte* RigMode;
        [NativeDisableUnsafePtrRestriction] public JVec3* BindPos;
        [NativeDisableUnsafePtrRestriction] public JQuat* BindRot;

        public void Execute(int rig)
        {
            JiggleRig* r = Rigs + rig;
            int n = (int)r->ParticleCount;
            int entries = n - 1;
            if (entries < 2 || n > JiggleConst.MaxParticles)
            {
                return;
            }
            int e0 = RigEntry[rig];
            JiggleSample* a = Samples + EntryTransform[e0];
            if (!a->Pos.IsFinite())
            {
                return;
            }
            JVec3* wp = stackalloc JVec3[JiggleConst.MaxParticles];
            JQuat* wr = stackalloc JQuat[JiggleConst.MaxParticles];
            if (RigMode[rig] == 0)
            {
                wp[0] = a->Pos;
                wr[0] = a->Rot;
                for (int k = 1; k < entries; k++)
                {
                    JVec3 b = BindPos[e0 + k];
                    wp[k] = new JVec3(
                        a->M00 * b.X + a->M01 * b.Y + a->M02 * b.Z + a->M03,
                        a->M10 * b.X + a->M11 * b.Y + a->M12 * b.Z + a->M13,
                        a->M20 * b.X + a->M21 * b.Y + a->M22 * b.Z + a->M23);
                    wr[k] = JQuat.Mul(a->Rot, BindRot[e0 + k]);
                }
            }
            else
            {
                for (int k = 0; k < entries; k++)
                {
                    JiggleSample* s = Samples + EntryTransform[e0 + k];
                    wp[k] = s->Pos;
                    wr[k] = s->Rot;
                }
            }
            int off = (int)r->ParticleOffset;
            JigglePose.Write(r, PosePrev + off, PoseCurr + off, PoseRot + off, TipScale[rig], wp, wr, entries);
        }
    }

    /// <summary>Turns the resolved world rotations of every rig into the local rotations of its bones.</summary>
    [BurstCompile(FloatMode = FloatMode.Strict, FloatPrecision = FloatPrecision.Standard)]
    internal unsafe struct JiggleLocalJob : IJobParallelFor
    {
        [NativeDisableUnsafePtrRestriction] public JiggleRig* Rigs;
        [NativeDisableUnsafePtrRestriction] public JVec4* PoseRot;
        [NativeDisableUnsafePtrRestriction] public JiggleParticleOut* Out;
        [NativeDisableUnsafePtrRestriction] public int* RigLocal;
        [NativeDisableUnsafePtrRestriction] public JQuat* Locals;

        public void Execute(int rig)
        {
            JiggleRig* r = Rigs + rig;
            int off = (int)r->ParticleOffset;
            int bones = (int)r->ParticleCount - 2;
            if (bones < 1)
            {
                return;
            }
            JigglePose.LocalRotations(PoseRot + off, Out + off, bones, Locals + RigLocal[rig]);
        }
    }

    /// <summary>Sets the local rotation of every bone that has a slot. Each bone has one writer, so it needs no ordering.</summary>
    [BurstCompile(FloatMode = FloatMode.Strict, FloatPrecision = FloatPrecision.Standard)]
    internal unsafe struct JiggleWriteJob : IJobParallelForTransform
    {
        [NativeDisableUnsafePtrRestriction] public JQuat* Locals;
        [NativeDisableUnsafePtrRestriction] public int* WriteSlot;

        public void Execute(int index, TransformAccess transform)
        {
            int slot = WriteSlot[index];
            if (slot < 0 || !transform.isValid)
            {
                return;
            }
            JQuat q = Locals[slot];
            if (!JiggleMath.IsFinite(q.X + q.Y + q.Z + q.W))
            {
                return;
            }
            transform.localRotation = new Quaternion(q.X, q.Y, q.Z, q.W);
        }
    }
}
