// ============================================================================
// NOTICE: Full documentation, design decisions, and fix history for this file
// live in docs/com.midmanstudio.allthingsjiggle.md, section "Editor/Lab/JiggleBenchScenes.cs"
// ============================================================================
using System;
using Unity.Burst;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;

namespace MidManStudio.AllThingsJiggle.EditorTools
{
    /// <summary>One scene of the table. The table must stay equal to `scenes()` in `rust_lib/jiggle_core/benches/scene_bench.rs`.</summary>
    internal struct JiggleBenchSpec
    {
        public string Name;
        public int Rigs;
        public int Bones;
        /// <summary>Four colliders per rig: a shared ground plane and a sphere, a capsule and a box of the rig.</summary>
        public bool Colliders;
        /// <summary>A directional field with gusts and a vortex over the whole grid.</summary>
        public bool Fields;
        /// <summary>0 keeps every link one way. Otherwise every bone has a mass and this is the number of coupling passes.</summary>
        public uint Passes;
        /// <summary>Sleeping on, at 30 frames.</summary>
        public bool Sleep;
        /// <summary>The anchors do not move.</summary>
        public bool Still;
        public int Frames;
        /// <summary>Frames that run before the timing starts.</summary>
        public int Warmup;

        public int Particles
        {
            get { return Rigs * (Bones + 2); }
        }
    }

    /// <summary>
    /// The scene table, the geometry and the motion of the comparison, the same as the Rust scene bench builds, and
    /// the job that writes the pose of a frame. All three backends are run on exactly these scenes.
    /// </summary>
    internal static unsafe class JiggleBenchScenes
    {
        private static JiggleBenchSpec Spec(string name, int rigs, int bones, int frames)
        {
            JiggleBenchSpec s = new JiggleBenchSpec();
            s.Name = name;
            s.Rigs = rigs;
            s.Bones = bones;
            s.Frames = frames;
            s.Warmup = 5;
            return s;
        }

        /// <summary>The scenes, in the order of the Rust table.</summary>
        public static JiggleBenchSpec[] All()
        {
            JiggleBenchSpec[] v = new JiggleBenchSpec[12];
            v[0] = Spec("tiny", 1, 8, 20000);
            v[1] = Spec("hair-100", 100, 8, 2000);
            v[2] = Spec("hair-1k", 1000, 8, 300);
            v[3] = Spec("hair-10k", 10000, 8, 40);
            v[4] = Spec("long-64", 100, 64, 200);
            v[5] = Spec("max-126", 20, 126, 200);
            v[6] = Spec("collide-1k", 1000, 8, 200);
            v[6].Colliders = true;
            v[7] = Spec("fields-1k", 1000, 8, 300);
            v[7].Fields = true;
            v[8] = Spec("coupled-1k-p1", 1000, 8, 300);
            v[8].Passes = 1;
            v[9] = Spec("coupled-1k-p4", 1000, 8, 300);
            v[9].Passes = 4;
            v[10] = Spec("asleep-1k", 1000, 8, 300);
            v[10].Sleep = true;
            v[10].Still = true;
            v[10].Warmup = 90;
            v[11] = Spec("full-1k", 1000, 8, 200);
            v[11].Colliders = true;
            v[11].Fields = true;
            v[11].Passes = 3;
            v[11].Sleep = true;
            return v;
        }

        // The anchors sit on a grid of 100 by N, half a meter apart. The chain hangs straight down, a tenth of a meter per bone.
        public static JVec3 BaseOf(int r)
        {
            return new JVec3((float)(r % 100) * 0.5f, 0.0f, (float)(r / 100) * 0.5f);
        }

        public static JVec3 RestPoint(int r, int i)
        {
            return BaseOf(r) + new JVec3(0.0f, -(float)i * 0.1f, 0.0f);
        }

        // The whole chain moves by this much in frame f. It differs per rig so no two rigs do the same work.
        public static JVec3 Sway(int f, int r)
        {
            float ff = (float)f;
            float rr = (float)r;
            return new JVec3(
                (float)Math.Sin((double)(ff * 0.5f + rr * 0.37f)) * 0.05f,
                0.0f,
                (float)Math.Cos((double)(ff * 0.35f + rr * 0.11f)) * 0.03f);
        }

        /// <summary>Builds the host of a scene with its colliders, fields and settings, as the Rust `build_world` does.</summary>
        public static JiggleHost Build(JiggleBenchSpec s)
        {
            JiggleHost h = new JiggleHost(s.Rigs, s.Particles);
            h.Cfg->CouplingPasses = s.Passes > 0 ? s.Passes : 1u;
            if (s.Sleep)
            {
                h.Cfg->SleepFrames = 30;
            }
            if (s.Colliders)
            {
                JiggleCollider ground = new JiggleCollider();
                ground.Kind = JiggleConst.ColliderPlane;
                ground.Center = new JVec4(0.0f, -0.7f, 0.0f, 0.0f);
                ground.Axis = new JVec4(0.0f, 1.0f, 0.0f, 0.0f);
                h.AddCollider(ground);
            }
            JiggleParams p = JiggleParams.Default();
            p.Mass = s.Passes > 0 ? 1.0f : 0.0f;
            p.Collide = s.Colliders;
            p.SegmentCollision = s.Colliders;
            p.CollisionRadius = 0.04f;
            JiggleParams[] ps = new JiggleParams[s.Bones];
            for (int i = 0; i < ps.Length; i++)
            {
                ps[i] = p;
            }
            JVec3[] rest = new JVec3[s.Bones + 1];
            for (int r = 0; r < s.Rigs; r++)
            {
                for (int i = 0; i <= s.Bones; i++)
                {
                    rest[i] = RestPoint(r, i);
                }
                int rig = h.AddChain(rest, ps, 1.0f);
                if (s.Colliders)
                {
                    JVec3 b = BaseOf(r);
                    JiggleCollider sphere = new JiggleCollider();
                    sphere.Kind = JiggleConst.ColliderSphere;
                    sphere.Radius = 0.1f;
                    sphere.Center = new JVec4(b.X, b.Y - 0.25f, b.Z + 0.06f, 0.0f);
                    JiggleCollider capsule = new JiggleCollider();
                    capsule.Kind = JiggleConst.ColliderCapsule;
                    capsule.Radius = 0.04f;
                    capsule.HalfHeight = 0.2f;
                    capsule.Center = new JVec4(b.X + 0.1f, b.Y - 0.3f, b.Z, 0.0f);
                    capsule.Axis = new JVec4(0.0f, 1.0f, 0.0f, 0.0f);
                    JiggleCollider box = new JiggleCollider();
                    box.Kind = JiggleConst.ColliderBox;
                    box.Center = new JVec4(b.X - 0.08f, b.Y - 0.5f, b.Z, 0.0f);
                    box.Extents = new JVec4(0.03f, 0.15f, 0.1f, 0.0f);
                    box.Rot = new JVec4(0.0f, 0.0f, 0.0f, 1.0f);
                    uint first = (uint)h.ColliderCount;
                    h.AddCollider(sphere);
                    h.AddCollider(capsule);
                    h.AddCollider(box);
                    h.SetColliderList(rig, new uint[] { 0u, first, first + 1u, first + 2u });
                }
            }
            if (s.Fields)
            {
                JiggleField gusty = new JiggleField();
                gusty.Kind = JiggleConst.FieldDirectional;
                gusty.Mode = JiggleConst.FieldAcceleration;
                gusty.Dir = new JVec4(1.0f, 0.0f, 0.3f, 0.7f);
                gusty.Strength = 4.0f;
                gusty.Turbulence = 0.8f;
                gusty.Frequency = 1.3f;
                h.AddField(gusty);
                JiggleField vortex = new JiggleField();
                vortex.Kind = JiggleConst.FieldVortex;
                vortex.Mode = JiggleConst.FieldAcceleration;
                vortex.Center = new JVec4(25.0f, 0.0f, 2.5f, 0.0f);
                vortex.Dir = new JVec4(0.0f, 1.0f, 0.0f, 0.0f);
                vortex.Radius = 30.0f;
                vortex.Strength = 6.0f;
                vortex.Power = 1;
                h.AddField(vortex);
            }
            return h;
        }

        /// <summary>Writes the pose of frame f into every rig of the host, in parallel, and waits.</summary>
        public static void WritePose(JiggleHost h, JiggleBenchSpec s, int frame)
        {
            JiggleBenchPoseJob job = new JiggleBenchPoseJob();
            job.Rigs = h.Rigs;
            job.PosePrev = h.PosePrev;
            job.PoseCurr = h.PoseCurr;
            job.PoseRot = h.PoseRot;
            job.TipScale = h.TipScale;
            job.Frame = frame;
            job.Still = s.Still ? (byte)1 : (byte)0;
            job.Schedule(h.RigCount, 16).Complete();
        }
    }

    /// <summary>The pose of a frame of the comparison scenes, for every rig: the same motion on all backends.</summary>
    [BurstCompile(FloatMode = FloatMode.Strict, FloatPrecision = FloatPrecision.Standard, CompileSynchronously = true)]
    internal unsafe struct JiggleBenchPoseJob : IJobParallelFor
    {
        [NativeDisableUnsafePtrRestriction] public JiggleRig* Rigs;
        [NativeDisableUnsafePtrRestriction] public JVec4* PosePrev;
        [NativeDisableUnsafePtrRestriction] public JVec4* PoseCurr;
        [NativeDisableUnsafePtrRestriction] public JVec4* PoseRot;
        [NativeDisableUnsafePtrRestriction] public float* TipScale;
        public int Frame;
        public byte Still;

        public void Execute(int rig)
        {
            JiggleRig* r = Rigs + rig;
            int n = (int)r->ParticleCount;
            int bones = n - 2;
            JVec3 sw = Still != 0 ? JVec3.Zero : JiggleBenchScenes.Sway(Frame, rig);
            JVec3* wp = stackalloc JVec3[JiggleConst.MaxParticles];
            for (int i = 0; i <= bones; i++)
            {
                wp[i] = JiggleBenchScenes.RestPoint(rig, i) + sw;
            }
            int off = (int)r->ParticleOffset;
            JigglePose.Write(r, PosePrev + off, PoseCurr + off, PoseRot + off, TipScale[rig], wp, null, 0);
        }
    }
}
