// ============================================================================
// NOTICE: Full documentation, design decisions, and fix history for this file
// live in docs/com.midmanstudio.allthingsjiggle.md, section "Tests/Editor/JiggleParityTests.cs"
// ============================================================================
using System;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using NUnit.Framework;

namespace MidManStudio.AllThingsJiggle.EditorTests
{
    /// <summary>
    /// Proves the managed kernel and the Rust library agree, bit for bit, on the same memory: the interface
    /// version, the layout of every struct field by field, random scenes of every feature, every collider and
    /// field kind, the error codes, threaded batches, and one golden scene whose hash is also asserted by the
    /// Rust test `golden_tests.rs`. The file runs under plain .NET in CI (see Tests~/DotnetHarness), so it
    /// must stay free of UnityEngine.
    /// </summary>
    [TestFixture]
    public unsafe class JiggleParityTests
    {
        // ------------------------------------------------------------------ small tools

        /// <summary>A xorshift generator: the same numbers on every platform.</summary>
        private sealed class Rng
        {
            private uint s;

            public Rng(uint seed)
            {
                unchecked
                {
                    s = seed * 2654435761u + 12345u;
                }
                if (s == 0u)
                {
                    s = 1u;
                }
            }

            public uint Next()
            {
                s ^= s << 13;
                s ^= s >> 17;
                s ^= s << 5;
                return s;
            }

            /// <summary>0 up to but not including 1.</summary>
            public float F()
            {
                return (float)(Next() >> 8) * (1.0f / 16777216.0f);
            }

            public float R(float lo, float hi)
            {
                return lo + (hi - lo) * F();
            }

            public int I(int n)
            {
                return (int)(Next() % (uint)n);
            }

            public bool B(float p)
            {
                return F() < p;
            }

            public JVec3 Unit()
            {
                JVec3 v = new JVec3(R(-1f, 1f), R(-1f, 1f), R(-1f, 1f));
                return v.NormalizeSafe(JVec3.Up);
            }
        }

        private static int Bits(float v)
        {
            return BitConverter.SingleToInt32Bits(v);
        }

        private static bool Same(JVec4 a, JVec4 b)
        {
            return Bits(a.X) == Bits(b.X) && Bits(a.Y) == Bits(b.Y) && Bits(a.Z) == Bits(b.Z) && Bits(a.W) == Bits(b.W);
        }

        private static string Fmt(JVec4 a)
        {
            return "(" + a.X.ToString("R") + ", " + a.Y.ToString("R") + ", " + a.Z.ToString("R") + ", " + a.W.ToString("R") + ")";
        }

        /// <summary>The first difference in bits between two worlds, or null when they are identical.</summary>
        private static string Diff(JiggleWorld a, JiggleWorld b)
        {
            if (a.State.Length != b.State.Length || a.Rigs.Length != b.Rigs.Length)
            {
                return "the worlds have different sizes";
            }
            for (int i = 0; i < a.State.Length; i++)
            {
                if (!Same(a.State[i].Pos, b.State[i].Pos))
                {
                    return "state.pos[" + i + "] " + Fmt(a.State[i].Pos) + " vs " + Fmt(b.State[i].Pos);
                }
                if (!Same(a.State[i].Prev, b.State[i].Prev))
                {
                    return "state.prev[" + i + "] " + Fmt(a.State[i].Prev) + " vs " + Fmt(b.State[i].Prev);
                }
                if (!Same(a.Out[i].Pos, b.Out[i].Pos))
                {
                    return "out.pos[" + i + "] " + Fmt(a.Out[i].Pos) + " vs " + Fmt(b.Out[i].Pos);
                }
                if (!Same(a.Out[i].Rot, b.Out[i].Rot))
                {
                    return "out.rot[" + i + "] " + Fmt(a.Out[i].Rot) + " vs " + Fmt(b.Out[i].Rot);
                }
            }
            for (int i = 0; i < a.Rigs.Length; i++)
            {
                if (a.Rigs[i].Flags != b.Rigs[i].Flags || a.Rigs[i].SleepCounter != b.Rigs[i].SleepCounter)
                {
                    return "rig " + i + " flags " + a.Rigs[i].Flags + "/" + a.Rigs[i].SleepCounter
                        + " vs " + b.Rigs[i].Flags + "/" + b.Rigs[i].SleepCounter;
                }
            }
            if (Bits(a.Cfg.Time) != Bits(b.Cfg.Time))
            {
                return "time " + a.Cfg.Time + " vs " + b.Cfg.Time;
            }
            return null;
        }

        private static void AssertSame(JiggleWorld managed, JiggleWorld native, string where)
        {
            string d = Diff(managed, native);
            if (d != null)
            {
                Assert.Fail("managed and native differ at " + where + ": " + d);
            }
        }

        private static void AssertAllFinite(JiggleWorld w)
        {
            for (int i = 0; i < w.State.Length; i++)
            {
                Assert.IsTrue(
                    JiggleMath.IsFinite(w.State[i].Pos.X) && JiggleMath.IsFinite(w.State[i].Pos.Y) && JiggleMath.IsFinite(w.State[i].Pos.Z)
                    && JiggleMath.IsFinite(w.State[i].Prev.X) && JiggleMath.IsFinite(w.State[i].Prev.Y) && JiggleMath.IsFinite(w.State[i].Prev.Z),
                    "state of particle " + i + " is not finite");
                Assert.IsTrue(
                    JiggleMath.IsFinite(w.Out[i].Pos.X) && JiggleMath.IsFinite(w.Out[i].Pos.Y) && JiggleMath.IsFinite(w.Out[i].Pos.Z)
                    && JiggleMath.IsFinite(w.Out[i].Rot.X) && JiggleMath.IsFinite(w.Out[i].Rot.Y)
                    && JiggleMath.IsFinite(w.Out[i].Rot.Z) && JiggleMath.IsFinite(w.Out[i].Rot.W),
                    "output of particle " + i + " is not finite");
            }
        }

        private static void StepBoth(JiggleWorld managed, JiggleWorld native, float dt, string where)
        {
            Assert.AreEqual(JiggleConst.Ok, managed.Step(dt, JiggleBackend.Managed), "managed step " + where);
            Assert.AreEqual(JiggleConst.Ok, native.Step(dt, JiggleBackend.Native), "native step " + where);
            AssertSame(managed, native, where);
        }

        private static float Dist(JVec3 a, JVec3 b)
        {
            return (a - b).Length();
        }

        // ------------------------------------------------------------------ interface and layout

        [Test]
        public void LibraryVersionAndInterfaceAgree()
        {
            Assert.AreEqual(JiggleLibrary.ExpectedAbiVersion, JiggleNative.jgl_abi_version());
            Assert.IsTrue(JiggleLibrary.Version.Length > 0);
            JiggleLibrary.Verify();
        }

        private static readonly Type[] StructKinds =
        {
            typeof(JiggleParticleDef), typeof(JiggleParticleState), typeof(JiggleRig), typeof(JiggleStepConfig),
            typeof(JiggleCollider), typeof(JiggleField), typeof(JiggleParticleOut), typeof(JiggleSolverArgs),
        };

        [Test]
        public void StructLayoutsMatchNativeFieldByField()
        {
            for (int i = 0; i < StructKinds.Length; i++)
            {
                int kind = i + 1;
                Type type = StructKinds[i];
                int size = JiggleNative.jgl_struct_size(kind);
                Assert.AreEqual(Marshal.SizeOf(type), size, type.Name + " size");
                byte[] bytes = new byte[size];
                Assert.AreEqual(size, JiggleNative.jgl_layout_probe(kind, bytes, size), type.Name + " probe");
                object value;
                GCHandle pin = GCHandle.Alloc(bytes, GCHandleType.Pinned);
                try
                {
                    value = Marshal.PtrToStructure(pin.AddrOfPinnedObject(), type);
                }
                finally
                {
                    pin.Free();
                }
                int nextInt = 1;
                int nextFloat = 1;
                CheckFields(value, ref nextInt, ref nextFloat, type.Name);
            }
        }

        // The native probe fills integer fields with 1, 2, 3 and so on and float fields with 1.5, 2.5 and so on,
        // each counting in the order of the fields in memory. Reading the fields back through their C# offsets
        // proves every offset, not only the total size.
        private static void CheckFields(object value, ref int nextInt, ref int nextFloat, string path)
        {
            FieldInfo[] fields = value.GetType().GetFields(
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (fields.Length > 0 && OffsetOf(fields[0]) >= 0)
            {
                Array.Sort(fields, (a, b) => OffsetOf(a).CompareTo(OffsetOf(b)));
            }
            foreach (FieldInfo field in fields)
            {
                object inner = field.GetValue(value);
                string name = path + "." + field.Name;
                if (field.FieldType.IsValueType && !field.FieldType.IsPrimitive)
                {
                    CheckFields(inner, ref nextInt, ref nextFloat, name);
                    continue;
                }
                if (field.FieldType == typeof(float))
                {
                    Assert.AreEqual(nextFloat + 0.5, Convert.ToDouble(inner), 0.0, name);
                    nextFloat++;
                }
                else
                {
                    Assert.AreEqual((double)nextInt, Convert.ToDouble(inner), 0.0, name);
                    nextInt++;
                }
            }
        }

        private static int OffsetOf(FieldInfo field)
        {
            object[] attributes = field.GetCustomAttributes(typeof(FieldOffsetAttribute), false);
            return attributes.Length == 0 ? -1 : ((FieldOffsetAttribute)attributes[0]).Value;
        }

        // ------------------------------------------------------------------ random scenes

        [Flags]
        private enum Mix
        {
            None = 0,
            Colliders = 1,
            Fields = 2,
            Segments = 4,
            Sleep = 8,
            All = 15,
            // Masses and extra passes. Kept out of All so the draws of the older scenes do not move.
            Coupled = 16,
        }

        /// <summary>
        /// Two identical worlds, one for each backend, and how the rigs move. Everything that is random is drawn
        /// from the seed, so both worlds and every frame are the same.
        /// </summary>
        private sealed class Scene
        {
            public JiggleWorld Managed;
            public JiggleWorld Native;
            public JVec3[][] Rest;
            public float[] Amp;
            public float[] Freq;
            public float[] Phase;
            public JVec3[] Dir;
            public int[] JumpFrame;
            public JVec3[] JumpVec;
        }

        private static Scene BuildScene(uint seed, int rigCount, Mix mix, int maxBones)
        {
            Scene s = new Scene();
            s.Managed = new JiggleWorld();
            s.Native = new JiggleWorld();
            s.Rest = new JVec3[rigCount][];
            s.Amp = new float[rigCount];
            s.Freq = new float[rigCount];
            s.Phase = new float[rigCount];
            s.Dir = new JVec3[rigCount];
            s.JumpFrame = new int[rigCount];
            s.JumpVec = new JVec3[rigCount];
            Fill(s.Managed, s, seed, rigCount, mix, maxBones);
            Fill(s.Native, s, seed, rigCount, mix, maxBones);
            return s;
        }

        private static void Fill(JiggleWorld w, Scene s, uint seed, int rigCount, Mix mix, int maxBones)
        {
            Rng rng = new Rng(seed);
            float[] steps = { 1.0f / 60.0f, 1.0f / 30.0f, 1.0f / 120.0f };
            w.FixedDt = steps[rng.I(3)];
            w.MaxSteps = (uint)(2 + rng.I(8));
            w.Cfg.GravityY = -rng.R(2.0f, 15.0f);
            w.Cfg.GravityX = rng.B(0.3f) ? rng.R(-3.0f, 3.0f) : 0.0f;
            w.Cfg.TeleportDistance = rng.B(0.7f) ? 5.0f : 0.0f;
            if ((mix & Mix.Sleep) != 0)
            {
                w.Cfg.SleepFrames = (uint)(3 + rng.I(6));
                w.Cfg.SleepSpeed = 0.01f;
                w.Cfg.SleepDeviation = 0.5f;
                w.Cfg.WakeMotion = 0.005f;
            }
            if ((mix & Mix.Coupled) != 0)
            {
                // 0 and a count past the maximum are both clamped by the solver, so the scenes use them too.
                w.Cfg.CouplingPasses = rng.B(0.1f) ? 40u : (uint)rng.I(6);
            }

            if ((mix & Mix.Colliders) != 0)
            {
                int count = 3 + rng.I(6);
                for (int c = 0; c < count; c++)
                {
                    JiggleCollider col = new JiggleCollider();
                    col.Kind = (uint)rng.I(5);
                    JVec3 center = new JVec3(rng.R(-2f, 2f), rng.R(-2f, 2f), rng.R(-2f, 2f));
                    center.Write4(ref col.Center);
                    if (col.Kind == JiggleConst.ColliderSphere)
                    {
                        col.Radius = rng.R(0.1f, 0.5f);
                    }
                    else if (col.Kind == JiggleConst.ColliderCapsule)
                    {
                        col.Radius = rng.R(0.05f, 0.2f);
                        col.HalfHeight = rng.R(0.1f, 0.5f);
                        rng.Unit().Write4(ref col.Axis);
                    }
                    else if (col.Kind == JiggleConst.ColliderPlane)
                    {
                        rng.Unit().Write4(ref col.Axis);
                    }
                    else if (col.Kind == JiggleConst.ColliderBox)
                    {
                        col.Radius = rng.B(0.5f) ? rng.R(0.0f, 0.1f) : 0.0f;
                        col.Extents = new JVec4(rng.R(0.1f, 0.4f), rng.R(0.1f, 0.4f), rng.R(0.1f, 0.4f), 0.0f);
                        JQuat q = new JQuat(rng.R(-1f, 1f), rng.R(-1f, 1f), rng.R(-1f, 1f), rng.R(-1f, 1f)).Normalized();
                        col.Rot = q.To4();
                    }
                    else
                    {
                        col.Radius = rng.R(1.0f, 3.0f);
                    }
                    w.AddCollider(col);
                }
            }

            if ((mix & Mix.Fields) != 0)
            {
                int count = 1 + rng.I(4);
                for (int k = 0; k < count; k++)
                {
                    JiggleField f = new JiggleField();
                    f.Kind = (uint)rng.I(3);
                    f.Mode = (uint)rng.I(2);
                    f.Power = (uint)rng.I(5);
                    new JVec3(rng.R(-2f, 2f), rng.R(-2f, 2f), rng.R(-2f, 2f)).Write4(ref f.Center);
                    rng.Unit().Write4(ref f.Dir);
                    f.Dir.W = rng.R(0.0f, 3.0f);
                    f.Radius = rng.B(0.3f) ? 0.0f : rng.R(0.5f, 4.0f);
                    f.Strength = rng.R(-8.0f, 8.0f);
                    f.Turbulence = rng.B(0.5f) ? rng.R(0.0f, 1.0f) : 0.0f;
                    f.Frequency = rng.R(0.0f, 3.0f);
                    w.AddField(f);
                }
            }

            for (int r = 0; r < rigCount; r++)
            {
                int bones = 1 + rng.I(maxBones);
                JVec3[] rest = new JVec3[bones + 1];
                rest[0] = new JVec3(rng.R(-1.5f, 1.5f), rng.R(-1.5f, 1.5f), rng.R(-1.5f, 1.5f));
                JVec3 dir = rng.Unit();
                float maxLen = 4.0f / (float)bones;
                for (int i = 1; i <= bones; i++)
                {
                    dir = (dir + rng.Unit() * 0.4f).NormalizeSafe(JVec3.Up);
                    float len = rng.R(0.04f, 0.25f);
                    if (len > maxLen)
                    {
                        len = maxLen;
                    }
                    rest[i] = rest[i - 1] + dir * len;
                }
                JiggleParams[] ps = new JiggleParams[bones];
                for (int b = 0; b < bones; b++)
                {
                    JiggleParams p = JiggleParams.Default();
                    p.Stiffness = rng.B(0.1f) ? (rng.B(0.5f) ? 0.0f : 1.0f) : rng.F();
                    p.Soften = rng.F();
                    p.RootStretch = rng.F();
                    p.AngleLimit = rng.F();
                    p.AngleLimitSoften = rng.F();
                    p.Stretch = rng.B(0.1f) ? (rng.B(0.5f) ? 0.0f : 1.0f) : rng.F();
                    p.Drag = rng.F() * 0.5f;
                    p.AirDrag = rng.F() * 0.5f;
                    p.Gravity = rng.R(-0.5f, 2.0f);
                    p.CollisionRadius = rng.R(0.01f, 0.1f);
                    p.Blend = rng.F();
                    p.IgnoreRootMotion = rng.B(0.3f) ? rng.F() : 0.0f;
                    p.WindResponse = rng.R(0.0f, 2.0f);
                    p.ImpulseResponse = rng.R(0.0f, 2.0f);
                    p.AngleLimited = rng.B(0.4f);
                    p.Collide = (mix & Mix.Colliders) != 0 && rng.B(0.8f);
                    p.SegmentCollision = (mix & Mix.Segments) != 0 && rng.B(0.5f);
                    if ((mix & Mix.Coupled) != 0)
                    {
                        // Some bones stay one way, some are far heavier than their neighbours (past the ratio cap).
                        p.Mass = rng.B(0.15f) ? 0.0f : (rng.B(0.1f) ? rng.R(50.0f, 500.0f) : rng.R(0.2f, 5.0f));
                    }
                    ps[b] = p;
                }
                int rig = w.AddChain(rest, ps, rng.R(0.5f, 3.0f));
                if ((mix & Mix.Coupled) != 0 && rng.B(0.5f))
                {
                    // A weight on the virtual end.
                    w.SetMass(rig, bones + 1, rng.R(0.5f, 20.0f));
                }
                if (w.Colliders.Length > 0)
                {
                    int n = rng.I(5);
                    uint[] list = new uint[n];
                    for (int k = 0; k < n; k++)
                    {
                        list[k] = (uint)rng.I(w.Colliders.Length);
                    }
                    w.SetColliderList(rig, list);
                }
                s.Rest[r] = rest;
                s.Amp[r] = rng.B(0.3f) ? 0.0f : rng.R(0.05f, 0.5f);
                s.Freq[r] = rng.R(0.5f, 4.0f);
                s.Phase[r] = rng.R(0.0f, 6.28f);
                s.Dir[r] = rng.Unit();
                if (rng.B(0.15f))
                {
                    s.JumpFrame[r] = 10 + rng.I(30);
                    s.JumpVec[r] = rng.Unit() * rng.R(8.0f, 12.0f);
                }
                else
                {
                    s.JumpFrame[r] = -1;
                    s.JumpVec[r] = JVec3.Zero;
                }
            }
        }

        private static void PoseOf(Scene s, int rig, int frame, int nudgeFrame, out JVec3[] pos, out JQuat[] rots)
        {
            JVec3[] rest = s.Rest[rig];
            float t = (float)frame * (1.0f / 60.0f);
            float amp = s.Amp[rig];
            JVec3 offset = s.Dir[rig] * ((float)Math.Sin(t * s.Freq[rig] + s.Phase[rig]) * amp);
            if (s.JumpFrame[rig] >= 0 && frame >= s.JumpFrame[rig])
            {
                offset = offset + s.JumpVec[rig];
            }
            if (frame >= nudgeFrame)
            {
                offset = offset + new JVec3(0.05f, 0.02f, 0.0f);
            }
            pos = new JVec3[rest.Length];
            rots = new JQuat[rest.Length];
            for (int i = 0; i < rest.Length; i++)
            {
                JVec3 sway = JVec3.Zero;
                if (amp > 0.0f && i > 0)
                {
                    sway = new JVec3(
                        (float)Math.Sin(t * 3.0f + i * 0.7f) * amp * 0.2f,
                        (float)Math.Cos(t * 2.3f + i) * amp * 0.2f,
                        (float)Math.Sin(t * 1.7f + i * 1.3f) * amp * 0.2f);
                }
                pos[i] = rest[i] + offset + sway;
                rots[i] = new JQuat((float)Math.Sin(t + i) * 0.2f, (float)Math.Cos(t * 1.3f + i) * 0.2f, 0.1f, 1.0f).Normalized();
            }
        }

        private static float FrameTime(Rng rng)
        {
            float pick = rng.F();
            if (pick < 0.70f)
            {
                return rng.R(0.008f, 0.03f);
            }
            if (pick < 0.80f)
            {
                return 0.0f;
            }
            if (pick < 0.85f)
            {
                return 0.3f;
            }
            if (pick < 0.90f)
            {
                return float.NaN;
            }
            if (pick < 0.95f)
            {
                return -1.0f;
            }
            return float.PositiveInfinity;
        }

        /// <summary>Runs a scene through both backends frame by frame and compares after every frame.</summary>
        private static void RunScene(Scene s, int frames, uint seed, ref bool sawAsleep, ref bool sawWake)
        {
            Rng dtRng = new Rng(seed + 9000u);
            int rigs = s.Rest.Length;
            bool[] wasAsleep = new bool[rigs];
            int nudge = frames * 2 / 3;
            for (int f = 0; f < frames; f++)
            {
                for (int r = 0; r < rigs; r++)
                {
                    JVec3[] pos;
                    JQuat[] rots;
                    PoseOf(s, r, f, nudge, out pos, out rots);
                    s.Managed.SetPose(r, pos, rots);
                    s.Native.SetPose(r, pos, rots);
                }
                if (f == frames / 4)
                {
                    s.Managed.Cfg.GravityX = 0.5f;
                    s.Managed.Cfg.GravityZ = 0.3f;
                    s.Native.Cfg.GravityX = 0.5f;
                    s.Native.Cfg.GravityZ = 0.3f;
                }
                if (f == frames / 3)
                {
                    s.Managed.Rigs[0].Flags |= JiggleConst.RigDisabled;
                    s.Native.Rigs[0].Flags |= JiggleConst.RigDisabled;
                }
                if (f == frames / 3 + 10)
                {
                    s.Managed.Rigs[0].Flags &= ~JiggleConst.RigDisabled;
                    s.Native.Rigs[0].Flags &= ~JiggleConst.RigDisabled;
                }
                if (f == frames / 2 && rigs > 1)
                {
                    s.Managed.Rigs[1].Flags |= JiggleConst.RigReset;
                    s.Native.Rigs[1].Flags |= JiggleConst.RigReset;
                }
                float dt = FrameTime(dtRng);
                StepBoth(s.Managed, s.Native, dt, "seed " + seed + " frame " + f);
                for (int r = 0; r < rigs; r++)
                {
                    bool asleep = (s.Managed.Rigs[r].Flags & JiggleConst.RigAsleep) != 0;
                    if (asleep)
                    {
                        sawAsleep = true;
                    }
                    if (wasAsleep[r] && !asleep)
                    {
                        sawWake = true;
                    }
                    wasAsleep[r] = asleep;
                }
            }
            AssertAllFinite(s.Managed);
        }

        [Test]
        public void ManagedAndNativeAgreeOnRandomScenes()
        {
            Mix[] mixes = { Mix.All, Mix.Colliders | Mix.Segments, Mix.Fields, Mix.None };
            bool sawAsleep = false;
            bool sawWake = false;
            for (uint seed = 1; seed <= 8; seed++)
            {
                for (int m = 0; m < mixes.Length; m++)
                {
                    Mix mix = mixes[m] | Mix.Sleep;
                    Scene s = BuildScene(seed * 17u + (uint)m, 3 + (int)seed * 3, mix, 10);
                    RunScene(s, 90, seed * 17u + (uint)m, ref sawAsleep, ref sawWake);
                }
            }
            // The scenes must have used the sleep path and the wake path, or the comparison proves less than it claims.
            Assert.IsTrue(sawAsleep, "no rig ever fell asleep");
            Assert.IsTrue(sawWake, "no sleeping rig ever woke up");
        }

        [Test]
        public void ManagedAndNativeAgreeOnCoupledScenes()
        {
            Mix[] mixes =
            {
                Mix.All | Mix.Coupled, Mix.Colliders | Mix.Segments | Mix.Coupled, Mix.Fields | Mix.Coupled, Mix.Coupled,
            };
            bool sawAsleep = false;
            bool sawWake = false;
            for (uint seed = 1; seed <= 8; seed++)
            {
                for (int m = 0; m < mixes.Length; m++)
                {
                    Mix mix = mixes[m] | Mix.Sleep;
                    Scene s = BuildScene(seed * 31u + (uint)m, 3 + (int)seed * 3, mix, 10);
                    RunScene(s, 90, seed * 31u + (uint)m, ref sawAsleep, ref sawWake);
                }
            }
            Assert.IsTrue(sawAsleep, "no coupled rig ever fell asleep");
            Assert.IsTrue(sawWake, "no sleeping coupled rig ever woke up");
        }

        [Test]
        public void ManagedAndNativeAgreeOnTheLargestRig()
        {
            bool a = false;
            bool b = false;
            // 126 bones plus the anchor and the virtual end make exactly 128 particles.
            Scene big = new Scene();
            big.Managed = new JiggleWorld();
            big.Native = new JiggleWorld();
            JVec3[] rest = new JVec3[127];
            JiggleParams[] ps = new JiggleParams[126];
            for (int i = 0; i < 127; i++)
            {
                rest[i] = new JVec3(i * 0.03f, 0.0f, 0.0f);
            }
            for (int i = 0; i < 126; i++)
            {
                ps[i] = JiggleParams.Default();
                ps[i].Stiffness = 0.3f + 0.005f * i;
                ps[i].AngleLimited = i % 3 == 0;
            }
            big.Managed.AddChain(rest, ps);
            big.Native.AddChain(rest, ps);
            Assert.AreEqual(128, big.Managed.Defs.Length);
            big.Rest = new JVec3[][] { rest };
            big.Amp = new float[] { 0.3f };
            big.Freq = new float[] { 2.0f };
            big.Phase = new float[] { 0.5f };
            big.Dir = new JVec3[] { JVec3.Forward };
            big.JumpFrame = new int[] { -1 };
            big.JumpVec = new JVec3[] { JVec3.Zero };
            RunScene(big, 60, 5, ref a, ref b);
            // One bone more is too many for either backend.
            JVec3[] longer = new JVec3[128];
            for (int i = 0; i < 128; i++)
            {
                longer[i] = new JVec3(i * 0.03f, 0.0f, 0.0f);
            }
            JiggleParams[] longerParams = new JiggleParams[127];
            for (int i = 0; i < 127; i++)
            {
                longerParams[i] = JiggleParams.Default();
            }
            JiggleWorld tooBig = new JiggleWorld();
            tooBig.AddChain(longer, longerParams);
            Assert.AreEqual(JiggleConst.ErrBadRig, tooBig.StepFixed(1, JiggleBackend.Managed));
            Assert.AreEqual(JiggleConst.ErrBadRig, tooBig.StepFixed(1, JiggleBackend.Native));
        }

        // ------------------------------------------------------------------ hand written scenes

        private static readonly JVec3[] HandRest =
        {
            new JVec3(0.0f, 1.0f, 0.0f), new JVec3(0.2f, 1.0f, 0.0f), new JVec3(0.4f, 1.0f, 0.0f),
            new JVec3(0.6f, 1.0f, 0.0f), new JVec3(0.8f, 1.0f, 0.0f),
        };

        private static JiggleWorld MakeHand(
            JiggleCollider[] colliders, JiggleField[] fields, bool lists, bool segment, float stiffness, float gravity)
        {
            JiggleWorld w = new JiggleWorld();
            JiggleParams p = JiggleParams.Default();
            p.Stiffness = stiffness;
            p.Gravity = gravity;
            p.Collide = true;
            p.SegmentCollision = segment;
            p.CollisionRadius = 0.05f;
            for (int i = 0; i < colliders.Length; i++)
            {
                w.AddCollider(colliders[i]);
            }
            for (int i = 0; i < fields.Length; i++)
            {
                w.AddField(fields[i]);
            }
            int rig = w.AddChain(HandRest, new JiggleParams[] { p, p, p, p }, 1.0f);
            if (lists && colliders.Length > 0)
            {
                uint[] list = new uint[colliders.Length];
                for (int i = 0; i < list.Length; i++)
                {
                    list[i] = (uint)i;
                }
                w.SetColliderList(rig, list);
            }
            return w;
        }

        /// <summary>
        /// Runs a hand written scene through both backends, comparing after every frame, with frames of one, two
        /// and three fixed steps. Returns the managed world.
        /// </summary>
        private static JiggleWorld RunHand(
            JiggleCollider[] colliders, JiggleField[] fields, bool lists, bool segment, float stiffness, float gravity, string name)
        {
            JiggleWorld m = MakeHand(colliders, fields, lists, segment, stiffness, gravity);
            JiggleWorld n = MakeHand(colliders, fields, lists, segment, stiffness, gravity);
            for (int f = 0; f < 90; f++)
            {
                JVec3[] pose = new JVec3[HandRest.Length];
                for (int i = 0; i < pose.Length; i++)
                {
                    pose[i] = HandRest[i] + new JVec3(0.0f, (float)Math.Sin(f * 0.1 + i) * 0.03f, 0.0f);
                }
                m.SetPose(0, pose);
                n.SetPose(0, pose);
                float dt = m.FixedDt * (float)(1 + f % 3);
                StepBoth(m, n, dt, name + " frame " + f);
            }
            AssertAllFinite(m);
            return m;
        }

        private static JiggleCollider Sphere(float x, float y, float z, float radius)
        {
            JiggleCollider c = new JiggleCollider();
            c.Kind = JiggleConst.ColliderSphere;
            c.Radius = radius;
            c.Center = new JVec4(x, y, z, 0.0f);
            return c;
        }

        private static JiggleCollider[] ColliderCases(int which)
        {
            JiggleCollider c = new JiggleCollider();
            switch (which)
            {
                case 0:
                    return new JiggleCollider[] { Sphere(0.7f, 0.88f, 0.0f, 0.12f) };
                case 1:
                    c.Kind = JiggleConst.ColliderCapsule;
                    c.Radius = 0.06f;
                    c.HalfHeight = 0.2f;
                    c.Center = new JVec4(0.7f, 0.9f, 0.0f, 0.0f);
                    c.Axis = new JVec4(0.0f, 1.0f, 0.0f, 0.0f);
                    return new JiggleCollider[] { c };
                case 2:
                    c.Kind = JiggleConst.ColliderPlane;
                    c.Center = new JVec4(0.0f, 0.9f, 0.0f, 0.0f);
                    c.Axis = new JVec4(0.0f, 1.0f, 0.0f, 0.0f);
                    return new JiggleCollider[] { c };
                case 3:
                    c.Kind = JiggleConst.ColliderBox;
                    c.Radius = 0.02f;
                    c.Center = new JVec4(0.7f, 0.88f, 0.0f, 0.0f);
                    c.Extents = new JVec4(0.05f, 0.08f, 0.15f, 0.0f);
                    c.Rot = new JVec4(0.0f, 0.0f, 0.258819f, 0.9659258f);
                    return new JiggleCollider[] { c };
                default:
                    c.Kind = JiggleConst.ColliderInsideSphere;
                    c.Radius = 0.3f;
                    c.Center = new JVec4(0.5f, 1.0f, 0.0f, 0.0f);
                    return new JiggleCollider[] { c };
            }
        }

        [Test]
        public void EveryColliderKindAgreesAndMatters()
        {
            string[] names = { "sphere", "capsule", "plane", "box", "inside sphere" };
            JiggleField[] none = new JiggleField[0];
            for (int k = 0; k < names.Length; k++)
            {
                JiggleCollider[] cs = ColliderCases(k);
                JiggleWorld with = RunHand(cs, none, true, false, 0.3f, 1.0f, names[k]);
                JiggleWorld without = RunHand(cs, none, false, false, 0.3f, 1.0f, names[k] + " off");
                float apart = 0.0f;
                for (int i = 1; i <= 4; i++)
                {
                    apart = Math.Max(apart, Dist(with.WorldPos(0, i), without.WorldPos(0, i)));
                }
                Assert.Greater(apart, 0.002, "the " + names[k] + " collider did not move the chain");
            }
        }

        [Test]
        public void SegmentCollisionAgreesAndMatters()
        {
            // A stiff chain with no gravity stays on its pose. A small sphere sits in the middle of the last bone,
            // between two particles, so only a test of the whole segment can see it.
            JiggleCollider[] cs = { Sphere(0.7f, 1.0f, 0.0f, 0.03f) };
            JiggleField[] none = new JiggleField[0];
            JiggleWorld seg = RunHand(cs, none, true, true, 1.0f, 0.0f, "segment");
            JiggleWorld plain = RunHand(cs, none, true, false, 1.0f, 0.0f, "particle only");
            float apart = Dist(seg.WorldPos(0, 4), plain.WorldPos(0, 4));
            Assert.Greater(apart, 0.002, "the segment test did not see the sphere");
        }

        private static JiggleField Field(uint kind, uint mode, uint power, float strength, float radius, JVec4 center, JVec4 dir)
        {
            JiggleField f = new JiggleField();
            f.Kind = kind;
            f.Mode = mode;
            f.Power = power;
            f.Strength = strength;
            f.Radius = radius;
            f.Center = center;
            f.Dir = dir;
            return f;
        }

        [Test]
        public void EveryFieldKindAgreesAndMatters()
        {
            JVec4 origin = new JVec4(0.4f, 1.0f, 0.0f, 0.0f);
            JVec4 z = new JVec4(0.0f, 0.0f, 1.0f, 0.0f);
            JVec4 up = new JVec4(0.0f, 1.0f, 0.0f, 0.0f);
            JiggleField gusty = Field(JiggleConst.FieldDirectional, JiggleConst.FieldAcceleration, 0, 12.0f, 0.0f, origin, new JVec4(0.3f, 0.0f, 1.0f, 0.9f));
            gusty.Turbulence = 0.7f;
            gusty.Frequency = 1.4f;
            JiggleField[] cases =
            {
                Field(JiggleConst.FieldDirectional, JiggleConst.FieldAcceleration, 0, 20.0f, 0.0f, origin, z),
                Field(JiggleConst.FieldDirectional, JiggleConst.FieldImpulse, 0, 3.0f, 0.0f, origin, z),
                Field(JiggleConst.FieldDirectional, JiggleConst.FieldAcceleration, 2, 25.0f, 1.0f, origin, z),
                gusty,
                Field(JiggleConst.FieldRadial, JiggleConst.FieldAcceleration, 2, 30.0f, 2.0f, new JVec4(0.4f, 0.9f, 0.0f, 0.0f), z),
                Field(JiggleConst.FieldRadial, JiggleConst.FieldImpulse, 1, -4.0f, 2.0f, new JVec4(0.4f, 0.9f, 0.3f, 0.0f), z),
                Field(JiggleConst.FieldVortex, JiggleConst.FieldAcceleration, 1, 40.0f, 2.0f, origin, up),
                Field(JiggleConst.FieldVortex, JiggleConst.FieldImpulse, 0, 3.0f, 2.0f, origin, up),
            };
            JiggleCollider[] noColliders = new JiggleCollider[0];
            JiggleWorld still = RunHand(noColliders, new JiggleField[0], false, false, 0.5f, 1.0f, "no field");
            for (int k = 0; k < cases.Length; k++)
            {
                JiggleWorld pushed = RunHand(noColliders, new JiggleField[] { cases[k] }, false, false, 0.5f, 1.0f, "field " + k);
                float apart = 0.0f;
                for (int i = 1; i <= 4; i++)
                {
                    apart = Math.Max(apart, Dist(pushed.WorldPos(0, i), still.WorldPos(0, i)));
                }
                Assert.Greater(apart, 0.002, "field case " + k + " did not move the chain");
            }
        }

        // ------------------------------------------------------------------ special frames

        [Test]
        public void TeleportResetDisableAndZeroStepsAgree()
        {
            JiggleCollider[] noColliders = new JiggleCollider[0];
            JiggleField[] noFields = new JiggleField[0];
            JiggleWorld m = MakeHand(noColliders, noFields, false, false, 0.6f, 1.0f);
            JiggleWorld n = MakeHand(noColliders, noFields, false, false, 0.6f, 1.0f);
            JiggleWorld off = MakeHand(noColliders, noFields, false, false, 0.6f, 1.0f);
            off.Cfg.TeleportDistance = 0.0f;
            for (int f = 0; f < 40; f++)
            {
                JVec3 shift = f >= 20 ? new JVec3(9.0f, 0.0f, 0.0f) : JVec3.Zero;
                JVec3[] pose = new JVec3[HandRest.Length];
                for (int i = 0; i < pose.Length; i++)
                {
                    pose[i] = HandRest[i] + shift + new JVec3(0.0f, (float)Math.Sin(f * 0.2 + i) * 0.05f, 0.0f);
                }
                m.SetPose(0, pose);
                n.SetPose(0, pose);
                off.SetPose(0, pose);
                if (f == 10)
                {
                    m.Rigs[0].Flags |= JiggleConst.RigReset;
                    n.Rigs[0].Flags |= JiggleConst.RigReset;
                    off.Rigs[0].Flags |= JiggleConst.RigReset;
                }
                if (f == 15)
                {
                    m.Rigs[0].Flags |= JiggleConst.RigDisabled;
                    n.Rigs[0].Flags |= JiggleConst.RigDisabled;
                }
                if (f == 17)
                {
                    m.Rigs[0].Flags &= ~JiggleConst.RigDisabled;
                    n.Rigs[0].Flags &= ~JiggleConst.RigDisabled;
                }
                // Frames 30 to 33 run no fixed step at all: only the rebase and the resolve happen.
                if (f >= 30 && f < 34)
                {
                    Assert.AreEqual(JiggleConst.Ok, m.StepFixed(0, JiggleBackend.Managed));
                    Assert.AreEqual(JiggleConst.Ok, n.StepFixed(0, JiggleBackend.Native));
                }
                else
                {
                    Assert.AreEqual(JiggleConst.Ok, m.StepFixed(1, JiggleBackend.Managed));
                    Assert.AreEqual(JiggleConst.Ok, n.StepFixed(1, JiggleBackend.Native));
                }
                Assert.AreEqual(JiggleConst.Ok, off.StepFixed(1, JiggleBackend.Managed));
                AssertSame(m, n, "frame " + f);
                Assert.IsTrue((m.Rigs[0].Flags & JiggleConst.RigReset) == 0, "the reset flag is cleared by the step");
            }
            // With the teleport check on, the chain follows the jump at once. With it off, the chain is dragged along
            // by its springs, so it is somewhere else.
            Assert.Greater(Dist(m.WorldPos(0, 4), off.WorldPos(0, 4)), 0.001);
            AssertAllFinite(m);
        }

        [Test]
        public void NonFiniteInputsAreSanitisedTheSame()
        {
            bool a = false;
            bool b = false;
            Scene s = BuildScene(31, 6, Mix.All, 8);
            RunScene(s, 12, 31, ref a, ref b);
            // A NaN in the state of one particle, and a NaN in the anchor move of another rig.
            int p = (int)s.Managed.Rigs[2].ParticleOffset + 3;
            s.Managed.State[p].Pos = new JVec4(float.NaN, float.NaN, float.NaN, 0.0f);
            s.Native.State[p].Pos = new JVec4(float.NaN, float.NaN, float.NaN, 0.0f);
            for (int f = 0; f < 6; f++)
            {
                for (int r = 0; r < 6; r++)
                {
                    JVec3[] pos;
                    JQuat[] rots;
                    PoseOf(s, r, 12 + f, 1000, out pos, out rots);
                    s.Managed.SetPose(r, pos, rots);
                    s.Native.SetPose(r, pos, rots);
                }
                if (f == 2)
                {
                    s.Managed.Rigs[4].AnchorDeltaX = float.NaN;
                    s.Native.Rigs[4].AnchorDeltaX = float.NaN;
                }
                StepBoth(s.Managed, s.Native, 1.0f / 60.0f, "nan frame " + f);
            }
            AssertAllFinite(s.Managed);
            AssertAllFinite(s.Native);
        }

        // ------------------------------------------------------------------ errors

        private delegate void Corrupt(JiggleWorld w);

        private sealed class ErrorCase
        {
            public string Name;
            public int Expect;
            public Corrupt Mutate;
            public JiggleArgsHook Hook;
            public uint First;
            public uint Count = 3;
            public bool NullCfg;
            public bool NullArgs;
        }

        private static JiggleWorld SmallWorld()
        {
            JiggleCollider[] cs = ColliderCases(0);
            JiggleField[] fs = { Field(JiggleConst.FieldDirectional, JiggleConst.FieldAcceleration, 0, 5.0f, 0.0f, new JVec4(0f, 0f, 0f, 0f), new JVec4(0f, 0f, 1f, 0f)) };
            JiggleWorld w = new JiggleWorld();
            JiggleParams p = JiggleParams.Default();
            p.Collide = true;
            for (int r = 0; r < 3; r++)
            {
                JVec3[] rest = new JVec3[4];
                for (int i = 0; i < 4; i++)
                {
                    rest[i] = new JVec3(r * 0.5f + i * 0.1f, 1.0f, 0.0f);
                }
                if (r == 0)
                {
                    w.AddCollider(cs[0]);
                    w.AddField(fs[0]);
                }
                int rig = w.AddChain(rest, new JiggleParams[] { p, p, p }, 1.0f);
                w.SetColliderList(rig, new uint[] { 0 });
                w.SetPose(rig, rest);
            }
            return w;
        }

        private static ErrorCase Err(string name, int expect, Corrupt mutate, JiggleArgsHook hook)
        {
            ErrorCase e = new ErrorCase();
            e.Name = name;
            e.Expect = expect;
            e.Mutate = mutate;
            e.Hook = hook;
            return e;
        }

        private static string Snapshot(JiggleWorld w)
        {
            // State and output as one text of bit patterns, so a call that wrote anything can be told apart.
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            for (int i = 0; i < w.State.Length; i++)
            {
                sb.Append(Bits(w.State[i].Pos.X)).Append(',').Append(Bits(w.State[i].Pos.Y)).Append(',').Append(Bits(w.State[i].Pos.Z)).Append(',');
                sb.Append(Bits(w.State[i].Prev.X)).Append(',').Append(Bits(w.Out[i].Pos.X)).Append(',').Append(Bits(w.Out[i].Rot.W)).Append(';');
            }
            for (int i = 0; i < w.Rigs.Length; i++)
            {
                sb.Append(w.Rigs[i].Flags).Append(':').Append(w.Rigs[i].SleepCounter).Append(';');
            }
            return sb.ToString();
        }

        [Test]
        public void ErrorCodesAgreeAndNothingIsWrittenOnAnError()
        {
            ErrorCase[] cases =
            {
                Err("a good call", JiggleConst.Ok, null, null),
                Err("a null config", JiggleConst.ErrNull, null, null),
                Err("null arguments", JiggleConst.ErrNull, null, null),
                Err("a range past the rig count", JiggleConst.ErrRange, null, null),
                Err("a range that overflows 32 bits", JiggleConst.ErrRange, null, null),
                Err("no rig array", JiggleConst.ErrNull, null, delegate (ref JiggleSolverArgs a) { a.Rigs = 0; }),
                Err("no definition array", JiggleConst.ErrNull, null, delegate (ref JiggleSolverArgs a) { a.Defs = 0; }),
                Err("no state array", JiggleConst.ErrNull, null, delegate (ref JiggleSolverArgs a) { a.State = 0; }),
                Err("no output array", JiggleConst.ErrNull, null, delegate (ref JiggleSolverArgs a) { a.Out = 0; }),
                Err("no collider array", JiggleConst.ErrNull, null, delegate (ref JiggleSolverArgs a) { a.Colliders = 0; }),
                Err("no collider list array", JiggleConst.ErrNull, null, delegate (ref JiggleSolverArgs a) { a.Refs = 0; }),
                Err("no field array", JiggleConst.ErrNull, null, delegate (ref JiggleSolverArgs a) { a.Fields = 0; }),
                Err("a particle count that is too small", JiggleConst.ErrBadRig, null, delegate (ref JiggleSolverArgs a) { a.ParticleCount = 3; }),
                Err("rigs out of order", JiggleConst.ErrBadRig, delegate (JiggleWorld w)
                {
                    JiggleRig t = w.Rigs[0];
                    w.Rigs[0] = w.Rigs[1];
                    w.Rigs[1] = t;
                }, null),
                Err("two rigs on the same particles", JiggleConst.ErrBadRig, delegate (JiggleWorld w) { w.Rigs[1] = w.Rigs[0]; }, null),
                Err("a rig with one particle", JiggleConst.ErrBadRig, delegate (JiggleWorld w) { w.Rigs[1].ParticleCount = 1; }, null),
                Err("a rig with 129 particles", JiggleConst.ErrBadRig, delegate (JiggleWorld w) { w.Rigs[2].ParticleCount = 129; }, null),
                Err("a collider list past the end", JiggleConst.ErrBadRig, delegate (JiggleWorld w) { w.Rigs[1].RefCount = 99; }, null),
                Err("a collider list offset past the end", JiggleConst.ErrBadRig, delegate (JiggleWorld w) { w.Rigs[1].RefOffset = 3; }, null),
                Err("a first particle that is not kinematic", JiggleConst.ErrBadRig, delegate (JiggleWorld w)
                {
                    w.Defs[(int)w.Rigs[1].ParticleOffset].Flags = 0;
                }, null),
                Err("a first particle with a parent", JiggleConst.ErrBadRig, delegate (JiggleWorld w)
                {
                    w.Defs[(int)w.Rigs[1].ParticleOffset].Parent = 0;
                }, null),
                Err("a parent that comes later", JiggleConst.ErrBadRig, delegate (JiggleWorld w)
                {
                    w.Defs[(int)w.Rigs[2].ParticleOffset + 2].Parent = 3;
                }, null),
                Err("a parent that is itself", JiggleConst.ErrBadRig, delegate (JiggleWorld w)
                {
                    w.Defs[(int)w.Rigs[2].ParticleOffset + 2].Parent = 2;
                }, null),
                Err("no parent for a later particle", JiggleConst.ErrBadRig, delegate (JiggleWorld w)
                {
                    w.Defs[(int)w.Rigs[2].ParticleOffset + 2].Parent = -1;
                }, null),
            };
            cases[1].NullCfg = true;
            cases[2].NullArgs = true;
            cases[3].First = 2;
            cases[3].Count = 2;
            cases[4].First = uint.MaxValue;
            cases[4].Count = 2;

            for (int c = 0; c < cases.Length; c++)
            {
                ErrorCase e = cases[c];
                for (int pass = 0; pass < 2; pass++)
                {
                    bool resolve = pass == 1;
                    JiggleWorld m = SmallWorld();
                    JiggleWorld n = SmallWorld();
                    if (e.Mutate != null)
                    {
                        e.Mutate(m);
                        e.Mutate(n);
                    }
                    string before = Snapshot(m);
                    string label = e.Name + (resolve ? " (resolve)" : " (step)");
                    int cm = m.Invoke(JiggleBackend.Managed, e.First, e.Count, resolve, e.Hook, e.NullCfg, e.NullArgs);
                    int cn = n.Invoke(JiggleBackend.Native, e.First, e.Count, resolve, e.Hook, e.NullCfg, e.NullArgs);
                    Assert.AreEqual(e.Expect, cm, "managed code for " + label);
                    Assert.AreEqual(e.Expect, cn, "native code for " + label);
                    if (e.Expect != JiggleConst.Ok)
                    {
                        Assert.AreEqual(before, Snapshot(m), "managed wrote something on an error: " + label);
                        Assert.AreEqual(before, Snapshot(n), "native wrote something on an error: " + label);
                        Assert.IsTrue(JiggleLibrary.LastError.Length > 0, "native left no error text for " + label);
                    }
                    else
                    {
                        AssertSame(m, n, label);
                    }
                }
            }

            // An empty call is fine, even at the end of the rig array.
            JiggleWorld empty = SmallWorld();
            Assert.AreEqual(JiggleConst.Ok, empty.Invoke(JiggleBackend.Managed, 3, 0, false));
            Assert.AreEqual(JiggleConst.Ok, empty.Invoke(JiggleBackend.Native, 3, 0, false));
            Assert.AreEqual(JiggleConst.Ok, empty.Invoke(JiggleBackend.Native, 0, 0, true));
        }

        [Test]
        public void ManagedWorldRefusesBadChains()
        {
            JiggleWorld w = new JiggleWorld();
            Assert.Throws<ArgumentException>(delegate { w.AddChain(new JVec3[1], new JiggleParams[0]); });
            Assert.Throws<ArgumentException>(delegate { w.AddChain(null, null); });
            Assert.Throws<ArgumentException>(delegate
            {
                w.AddChain(new JVec3[] { JVec3.Zero, JVec3.Up, JVec3.Up }, new JiggleParams[] { JiggleParams.Default() });
            });
        }

        // ------------------------------------------------------------------ threads

        [Test]
        public void ThreadedBatchesMatchOneCall()
        {
            for (int backendPass = 0; backendPass < 2; backendPass++)
            {
                JiggleBackend backend = backendPass == 0 ? JiggleBackend.Native : JiggleBackend.Managed;
                Scene one = BuildScene(404, 40, Mix.All, 8);
                Scene many = BuildScene(404, 40, Mix.All, 8);
                uint[][] batches = { new uint[] { 0, 7 }, new uint[] { 7, 13 }, new uint[] { 20, 9 }, new uint[] { 29, 11 } };
                for (int f = 0; f < 40; f++)
                {
                    for (int r = 0; r < 40; r++)
                    {
                        JVec3[] pos;
                        JQuat[] rots;
                        PoseOf(one, r, f, 1000, out pos, out rots);
                        one.Managed.SetPose(r, pos, rots);
                        many.Managed.SetPose(r, pos, rots);
                    }
                    for (int w = 0; w < 2; w++)
                    {
                        JiggleWorld world = w == 0 ? one.Managed : many.Managed;
                        world.Cfg.Steps = 2;
                        world.Cfg.Alpha = 0.5f;
                        world.Cfg.Dt = world.FixedDt;
                    }
                    Assert.AreEqual(JiggleConst.Ok, one.Managed.Invoke(backend, 0, 40, false));
                    Assert.AreEqual(JiggleConst.Ok, one.Managed.Invoke(backend, 0, 40, true));

                    int[] codes = new int[batches.Length * 2];
                    Thread[] threads = new Thread[batches.Length];
                    JiggleWorld target = many.Managed;
                    for (int t = 0; t < batches.Length; t++)
                    {
                        int index = t;
                        threads[t] = new Thread(delegate ()
                        {
                            codes[index * 2] = target.Invoke(backend, batches[index][0], batches[index][1], false);
                            codes[index * 2 + 1] = target.Invoke(backend, batches[index][0], batches[index][1], true);
                        });
                        threads[t].Start();
                    }
                    for (int t = 0; t < threads.Length; t++)
                    {
                        threads[t].Join();
                    }
                    for (int t = 0; t < codes.Length; t++)
                    {
                        Assert.AreEqual(JiggleConst.Ok, codes[t], "batch call " + t);
                    }
                    AssertSame(one.Managed, many.Managed, "threaded " + backend + " frame " + f);
                }
            }
        }

        // ------------------------------------------------------------------ behaviour

        [Test]
        public void ChainSagsUnderGravityAndStaysNearItsPose()
        {
            JVec3[] rest =
            {
                new JVec3(0.0f, 0.0f, 0.0f), new JVec3(0.2f, 0.0f, 0.0f), new JVec3(0.4f, 0.0f, 0.0f), new JVec3(0.6f, 0.0f, 0.0f),
            };
            JiggleParams[] ps = { JiggleParams.Default(), JiggleParams.Default(), JiggleParams.Default() };
            JiggleWorld m = new JiggleWorld();
            JiggleWorld n = new JiggleWorld();
            m.AddChain(rest, ps);
            n.AddChain(rest, ps);
            for (int f = 0; f < 120; f++)
            {
                m.SetPose(0, rest);
                n.SetPose(0, rest);
                Assert.AreEqual(JiggleConst.Ok, m.StepFixed(1, JiggleBackend.Managed));
                Assert.AreEqual(JiggleConst.Ok, n.StepFixed(1, JiggleBackend.Native));
                AssertSame(m, n, "frame " + f);
            }
            JVec3 tip = m.WorldPos(0, 3);
            Assert.IsTrue(tip.Y < 0.0f && tip.Y > -0.5f, "tip y is " + tip.Y);
            // The root bone is held to its pose and the bones keep about their length.
            Assert.Less(Dist(m.WorldPos(0, 1), rest[1]), 0.05);
            for (int i = 1; i < 3; i++)
            {
                float len = Dist(m.WorldPos(0, i), m.WorldPos(0, i + 1));
                Assert.Greater(len, 0.12);
                Assert.Less(len, 0.28);
            }
        }

        [Test]
        public void BakingFollowsTheDocumentedFormulas()
        {
            JiggleParams p = JiggleParams.Default();
            p.Stiffness = 0.5f;
            p.Stretch = 0.2f;
            p.Soften = 0.5f;
            p.RootStretch = 0.3f;
            p.AngleLimit = 1.0f;
            p.AngleLimited = true;
            p.Collide = true;
            p.SegmentCollision = true;
            JiggleParticleDef d = p.Bake(2, JiggleConst.FlagRoot);
            Assert.AreEqual(0.25, d.AngleElasticity, 1e-7);
            Assert.AreEqual(0.64, d.LengthElasticity, 1e-6);
            Assert.AreEqual(0.25, d.Soften, 1e-7);
            Assert.AreEqual(0.7, d.RootElasticity, 1e-6);
            Assert.AreEqual(0.0, d.LimitCos, 1e-6);
            Assert.AreEqual(1.0, d.LimitSin, 1e-6);
            Assert.AreEqual(2, d.Parent);
            uint want = JiggleConst.FlagRoot | JiggleConst.FlagAngleLimit | JiggleConst.FlagCollide | JiggleConst.FlagSegment;
            Assert.AreEqual(want, d.Flags);
            // Out of range authoring values are clamped, not passed on.
            p.Stiffness = 7.0f;
            p.Drag = -3.0f;
            JiggleParticleDef c = p.Bake(0, 0);
            Assert.AreEqual(1.0, c.AngleElasticity, 0.0);
            Assert.AreEqual(0.0, c.Drag, 0.0);
            JiggleParticleDef a = JiggleParams.AnchorDef();
            Assert.AreEqual(-1, a.Parent);
            Assert.AreEqual(JiggleConst.FlagKinematic, a.Flags);
        }

        // ------------------------------------------------------------------ golden scene

        /// <summary>The hash of the golden scene. Keep in sync with GOLDEN in rust_lib/jiggle_core/tests/golden_tests.rs.</summary>
        private const ulong Golden = 0xbd735dd528713541UL;

        /// <summary>The hash of the coupled golden scene. Keep in sync with GOLDEN_COUPLED in rust_lib/jiggle_core/tests/golden_tests.rs.</summary>
        private const ulong GoldenCoupled = 0x86c07fa5cdc256e4UL;

        private sealed class Lcg
        {
            private uint s;

            public Lcg(uint seed)
            {
                s = seed;
            }

            public uint Next()
            {
                unchecked
                {
                    s = s * 1664525u + 1013904223u;
                }
                return s;
            }

            public float Unit()
            {
                return (float)(Next() >> 8) * (1.0f / 16777216.0f);
            }

            public float Range(float lo, float hi)
            {
                return lo + (hi - lo) * Unit();
            }
        }

        private sealed class Fnv
        {
            public ulong H = 0xcbf29ce484222325UL;

            public void Word(uint v)
            {
                for (int k = 0; k < 4; k++)
                {
                    H ^= (ulong)((v >> (8 * k)) & 0xffu);
                    unchecked
                    {
                        H *= 0x100000001b3UL;
                    }
                }
            }

            public void Float(float v)
            {
                Word(unchecked((uint)BitConverter.SingleToInt32Bits(v)));
            }

            public void Vec(JVec4 v)
            {
                Float(v.X);
                Float(v.Y);
                Float(v.Z);
                Float(v.W);
            }
        }

        private static float Tri(float x)
        {
            float w = x - MathF.Floor(x);
            return MathF.Abs(w - 0.5f) * 4.0f - 1.0f;
        }

        private const int GoldenRigs = 6;
        private const int GoldenFrames = 120;

        private static JiggleWorld BuildGolden(bool coupled, out JVec3[][] rests)
        {
            Lcg rng = new Lcg(20261105);
            JiggleWorld w = new JiggleWorld();
            w.Cfg.SleepSpeed = 0.002f;
            w.Cfg.SleepDeviation = 0.05f;
            w.Cfg.SleepFrames = 6;
            w.Cfg.WakeMotion = 0.01f;
            if (coupled)
            {
                w.Cfg.CouplingPasses = 3;
            }

            w.AddCollider(Sphere(0.4f, 0.55f, 0.0f, 0.12f));
            JiggleCollider cap = new JiggleCollider();
            cap.Kind = JiggleConst.ColliderCapsule;
            cap.Radius = 0.06f;
            cap.HalfHeight = 0.25f;
            cap.Center = new JVec4(0.9f, 0.5f, 0.0f, 0.0f);
            cap.Axis = new JVec4(0.0f, 1.0f, 0.0f, 0.0f);
            w.AddCollider(cap);
            JiggleCollider plane = new JiggleCollider();
            plane.Kind = JiggleConst.ColliderPlane;
            plane.Center = new JVec4(0.0f, 0.2f, 0.0f, 0.0f);
            plane.Axis = new JVec4(0.0f, 1.0f, 0.0f, 0.0f);
            w.AddCollider(plane);
            JiggleCollider box = new JiggleCollider();
            box.Kind = JiggleConst.ColliderBox;
            box.Radius = 0.02f;
            box.Center = new JVec4(1.6f, 0.6f, 0.0f, 0.0f);
            box.Extents = new JVec4(0.15f, 0.1f, 0.2f, 0.0f);
            box.Rot = new JVec4(0.0f, 0.38268343f, 0.0f, 0.9238795f);
            w.AddCollider(box);
            JiggleCollider inside = new JiggleCollider();
            inside.Kind = JiggleConst.ColliderInsideSphere;
            inside.Radius = 1.0f;
            inside.Center = new JVec4(2.2f, 0.8f, 0.0f, 0.0f);
            w.AddCollider(inside);

            JiggleField wind = new JiggleField();
            wind.Kind = JiggleConst.FieldDirectional;
            wind.Mode = JiggleConst.FieldAcceleration;
            wind.Dir = new JVec4(0.5f, 0.0f, 1.0f, 0.8f);
            wind.Strength = 6.0f;
            wind.Turbulence = 0.6f;
            wind.Frequency = 1.3f;
            w.AddField(wind);
            JiggleField burst = new JiggleField();
            burst.Kind = JiggleConst.FieldRadial;
            burst.Mode = JiggleConst.FieldImpulse;
            burst.Power = 2;
            burst.Center = new JVec4(1.0f, 0.5f, 0.0f, 0.0f);
            burst.Radius = 1.5f;
            burst.Strength = 3.0f;
            w.AddField(burst);
            JiggleField swirl = new JiggleField();
            swirl.Kind = JiggleConst.FieldVortex;
            swirl.Mode = JiggleConst.FieldAcceleration;
            swirl.Power = 1;
            swirl.Center = new JVec4(2.0f, 0.5f, 0.0f, 0.0f);
            swirl.Dir = new JVec4(0.0f, 1.0f, 0.0f, 0.0f);
            swirl.Radius = 2.0f;
            swirl.Strength = 4.0f;
            w.AddField(swirl);

            rests = new JVec3[GoldenRigs][];
            for (int r = 0; r < GoldenRigs; r++)
            {
                int bones = 3 + r;
                float len = 0.12f + 0.01f * (float)r;
                JVec3 anchor = new JVec3((float)r * 0.45f, 1.0f, 0.0f);
                JVec3[] rest = new JVec3[bones + 1];
                rest[0] = anchor;
                for (int i = 1; i <= bones; i++)
                {
                    rest[i] = new JVec3(anchor.X + (float)i * len, anchor.Y - 0.05f * (float)i, anchor.Z);
                }
                JiggleParams[] ps = new JiggleParams[bones];
                for (int b = 0; b < bones; b++)
                {
                    JiggleParams p = new JiggleParams();
                    p.Stiffness = rng.Range(0.2f, 0.9f);
                    p.Soften = rng.Range(0.0f, 0.5f);
                    p.RootStretch = rng.Range(0.0f, 0.3f);
                    p.AngleLimit = 0.5f;
                    p.AngleLimitSoften = rng.Range(0.0f, 0.5f);
                    p.Stretch = rng.Range(0.0f, 0.4f);
                    p.Drag = rng.Range(0.0f, 0.3f);
                    p.AirDrag = rng.Range(0.0f, 0.2f);
                    p.Gravity = rng.Range(0.5f, 1.5f);
                    p.CollisionRadius = 0.03f;
                    p.Blend = rng.Range(0.5f, 1.0f);
                    p.IgnoreRootMotion = rng.Range(0.0f, 0.3f);
                    p.WindResponse = rng.Range(0.5f, 1.5f);
                    p.ImpulseResponse = rng.Range(0.5f, 1.5f);
                    p.AngleLimited = r % 2 == 1;
                    p.Collide = true;
                    p.SegmentCollision = r % 3 == 0;
                    if (r == 4)
                    {
                        // Rig 4 is the sleeper: no gravity and no field response, so it settles at once while it is held still.
                        p.Gravity = 0.0f;
                        p.WindResponse = 0.0f;
                        p.ImpulseResponse = 0.0f;
                    }
                    ps[b] = p;
                }
                int rig = w.AddChain(rest, ps, 1.0f + (float)(r % 3));
                // The baked cosine and sine of the angle limit come from the math library of each language and may
                // differ in the last bit, so the scene sets them to fixed literals.
                int off = (int)w.Rigs[rig].ParticleOffset;
                int n = (int)w.Rigs[rig].ParticleCount;
                for (int i = off; i < off + n; i++)
                {
                    if ((w.Defs[i].Flags & JiggleConst.FlagAngleLimit) != 0)
                    {
                        w.Defs[i].LimitCos = 0.8f;
                        w.Defs[i].LimitSin = 0.6f;
                    }
                }
                if (coupled && r != 1)
                {
                    // Rig 1 stays one way. The others get a mass that grows down the strand and a heavier virtual end.
                    // No mass is more than 16 times its neighbour's, so the cap on the ratio is not what the scene tests.
                    for (int b = 1; b <= bones; b++)
                    {
                        w.SetMass(rig, b, 1.0f + 0.5f * (float)b);
                    }
                    w.SetMass(rig, bones + 1, 3.0f + (float)r);
                }
                uint[] list = r % 2 == 0 ? new uint[] { 0, 1, 2, 3, 4 } : new uint[] { 0, 2, 4 };
                w.SetColliderList(rig, list);
                rests[r] = rest;
            }
            return w;
        }

        private static void GoldenPose(JVec3[][] rests, int r, int f, out JVec3[] pos, out JQuat[] rots)
        {
            JVec3[] rest = rests[r];
            bool moving = !(r == 4 && f < 90);
            float jump = (r == 5 && f >= 80) ? 9.0f : 0.0f;
            float ph = (float)f * 0.037f + (float)r * 0.31f;
            JVec3 offset;
            if (moving)
            {
                offset = new JVec3(Tri(ph) * 0.3f + jump, Tri(ph * 1.7f + 0.2f) * 0.1f, Tri(ph * 0.6f + 0.5f) * 0.2f);
            }
            else
            {
                offset = new JVec3(jump, 0.0f, 0.0f);
            }
            pos = new JVec3[rest.Length];
            rots = new JQuat[rest.Length];
            for (int i = 0; i < rest.Length; i++)
            {
                JVec3 sway = JVec3.Zero;
                if (moving && i > 0)
                {
                    float q = ph + (float)i * 0.11f;
                    sway = new JVec3(Tri(q) * 0.04f, Tri(q * 1.3f + 0.1f) * 0.04f, Tri(q * 0.7f + 0.3f) * 0.04f);
                }
                pos[i] = rest[i] + offset + sway;
                float a = ph + (float)i * 0.17f;
                rots[i] = new JQuat(Tri(a) * 0.1f, Tri(a * 1.9f) * 0.2f, 0.0f, 1.0f).Normalized();
            }
        }

        private static float GoldenFrameTime(Lcg rng, int f)
        {
            if (f == 70)
            {
                return 0.2f;
            }
            if (f % 17 == 5)
            {
                return 0.0f;
            }
            return rng.Range(0.004f, 0.044f);
        }

        private static void Fold(Fnv h, JiggleWorld w)
        {
            for (int i = 0; i < w.State.Length; i++)
            {
                h.Vec(w.State[i].Pos);
                h.Vec(w.State[i].Prev);
            }
            for (int i = 0; i < w.Out.Length; i++)
            {
                h.Vec(w.Out[i].Pos);
                h.Vec(w.Out[i].Rot);
            }
            for (int i = 0; i < w.Rigs.Length; i++)
            {
                h.Word(w.Rigs[i].Flags);
                h.Word(w.Rigs[i].SleepCounter);
            }
        }

        private struct GoldenRun
        {
            public ulong Hash;
            public int AsleepFrames;
            public bool AwakeAfterSleep;
        }

        private static GoldenRun RunGolden(bool colliders, bool fields, JiggleBackend backend, bool coupled = false, uint passes = 0)
        {
            JVec3[][] rests;
            JiggleWorld w = BuildGolden(coupled, out rests);
            if (passes != 0)
            {
                w.Cfg.CouplingPasses = passes;
            }
            if (!colliders)
            {
                for (int i = 0; i < w.Rigs.Length; i++)
                {
                    w.Rigs[i].RefCount = 0;
                }
            }
            if (!fields)
            {
                w.Fields = new JiggleField[0];
            }
            Lcg rng = new Lcg(77);
            Fnv h = new Fnv();
            GoldenRun run = new GoldenRun();
            bool wasAsleep = false;
            for (int f = 0; f < GoldenFrames; f++)
            {
                for (int r = 0; r < GoldenRigs; r++)
                {
                    JVec3[] pos;
                    JQuat[] rots;
                    GoldenPose(rests, r, f, out pos, out rots);
                    w.SetPose(r, pos, rots);
                }
                if (f == 30)
                {
                    w.Rigs[3].Flags |= JiggleConst.RigDisabled;
                }
                if (f == 40)
                {
                    w.Rigs[3].Flags &= ~JiggleConst.RigDisabled;
                }
                if (f == 50)
                {
                    w.Rigs[2].Flags |= JiggleConst.RigReset;
                }
                float dt = GoldenFrameTime(rng, f);
                Assert.AreEqual(JiggleConst.Ok, w.Step(dt, backend));
                Fold(h, w);
                bool asleep = (w.Rigs[4].Flags & JiggleConst.RigAsleep) != 0;
                if (asleep)
                {
                    run.AsleepFrames++;
                }
                if (wasAsleep && !asleep)
                {
                    run.AwakeAfterSleep = true;
                }
                wasAsleep = asleep;
            }
            run.Hash = h.H;
            return run;
        }

        [Test]
        public void GoldenSceneMatchesTheRustWorld()
        {
            // The scene is built from exact IEEE operations and integers, so the Rust World and this managed world
            // see the same inputs. Both backends of this world must give the hash that the Rust test records too.
            GoldenRun managed = RunGolden(true, true, JiggleBackend.Managed);
            GoldenRun native = RunGolden(true, true, JiggleBackend.Native);
            Assert.AreEqual(native.Hash, managed.Hash, "managed and native hashes of the golden scene");
            Assert.AreEqual(Golden, managed.Hash, "the golden hash is now 0x" + managed.Hash.ToString("x16"));
        }

        [Test]
        public void GoldenSceneExercisesWhatItClaims()
        {
            GoldenRun full = RunGolden(true, true, JiggleBackend.Managed);
            Assert.Greater(full.AsleepFrames, 10, "rig 4 never slept");
            Assert.IsTrue(full.AwakeAfterSleep, "rig 4 never woke up");
            Assert.AreNotEqual(RunGolden(false, true, JiggleBackend.Managed).Hash, full.Hash);
            Assert.AreNotEqual(RunGolden(true, false, JiggleBackend.Managed).Hash, full.Hash);
        }

        [Test]
        public void CoupledGoldenSceneMatchesTheRustWorld()
        {
            GoldenRun managed = RunGolden(true, true, JiggleBackend.Managed, true);
            GoldenRun native = RunGolden(true, true, JiggleBackend.Native, true);
            Assert.AreEqual(native.Hash, managed.Hash, "managed and native hashes of the coupled golden scene");
            Assert.AreEqual(GoldenCoupled, managed.Hash, "the coupled golden hash is now 0x" + managed.Hash.ToString("x16"));
        }

        [Test]
        public void CoupledGoldenSceneExercisesWhatItClaims()
        {
            GoldenRun oneWay = RunGolden(true, true, JiggleBackend.Managed);
            GoldenRun coupled = RunGolden(true, true, JiggleBackend.Managed, true);
            Assert.AreNotEqual(oneWay.Hash, coupled.Hash, "the masses changed nothing");
            Assert.AreNotEqual(RunGolden(false, true, JiggleBackend.Managed, true).Hash, coupled.Hash);
            Assert.AreNotEqual(RunGolden(true, false, JiggleBackend.Managed, true).Hash, coupled.Hash);
            Assert.AreNotEqual(RunGolden(true, true, JiggleBackend.Managed, true, 1).Hash, coupled.Hash, "the pass count changed nothing");
            Assert.Greater(coupled.AsleepFrames, 10, "rig 4 never slept");
            Assert.IsTrue(coupled.AwakeAfterSleep, "rig 4 never woke up");
        }

        [Test]
        public void WithoutMassesThePassCountChangesNothing()
        {
            // The one way scene must give the same hash at any pass count, including 0 and one past the maximum.
            ulong reference = RunGolden(true, true, JiggleBackend.Managed).Hash;
            Assert.AreEqual(reference, RunGolden(true, true, JiggleBackend.Managed, false, 1).Hash);
            Assert.AreEqual(reference, RunGolden(true, true, JiggleBackend.Managed, false, 7).Hash);
            Assert.AreEqual(reference, RunGolden(true, true, JiggleBackend.Managed, false, 1000).Hash);
        }

        [Test]
        public void MassIsBakedClampedAndSetOnlyOnRealParticles()
        {
            JiggleParams p = JiggleParams.Default();
            Assert.AreEqual(0.0, p.Bake(0, 0).Mass, 0.0, "the default is one way");
            p.Mass = 2.5f;
            Assert.AreEqual(2.5, p.Bake(0, 0).Mass, 0.0);
            p.Mass = -4.0f;
            Assert.AreEqual(0.0, p.Bake(0, 0).Mass, 0.0, "a negative mass is one way");
            p.Mass = float.NaN;
            Assert.AreEqual(0.0, p.Bake(0, 0).Mass, 0.0, "NaN is one way");
            p.Mass = float.PositiveInfinity;
            Assert.AreEqual((double)JiggleParams.MaxMass, p.Bake(0, 0).Mass, 0.0, "infinity counts as the largest mass");

            JiggleWorld w = new JiggleWorld();
            JVec3[] rest = { new JVec3(0, 1, 0), new JVec3(0.1f, 1, 0), new JVec3(0.2f, 1, 0) };
            JiggleParams[] ps = { JiggleParams.Default(), JiggleParams.Default() };
            int rig = w.AddChain(rest, ps);
            w.SetMass(rig, 1, 3.0f);
            w.SetMass(rig, 3, 1.0e9f);
            int off = (int)w.Rigs[rig].ParticleOffset;
            Assert.AreEqual(3.0, w.Defs[off + 1].Mass, 0.0);
            Assert.AreEqual((double)JiggleParams.MaxMass, w.Defs[off + 3].Mass, 0.0);
            Assert.Throws<ArgumentException>(() => w.SetMass(rig, 0, 1.0f), "the anchor has no mass");
            Assert.Throws<ArgumentException>(() => w.SetMass(rig, 4, 1.0f), "past the virtual end");
        }

        // ------------------------------------------------------------------ Unity runtime support: math, host, pose

        private static void AssertSameFloat(float expected, float actual, string what)
        {
            if (float.IsNaN(expected))
            {
                Assert.IsTrue(float.IsNaN(actual), what + ": expected NaN");
                return;
            }
            Assert.AreEqual(Bits(expected), Bits(actual), what + ": " + expected + " vs " + actual);
        }

        [Test]
        public void BurstSafeMathGivesTheBitsOfMathF()
        {
            // The kernel uses JiggleMath.Sqrt, Abs and Floor because Burst rejects System.MathF. They must give the same
            // float as MathF for every input, or the managed kernel would stop matching the Rust one.
            float[] edge =
            {
                0.0f, -0.0f, 1.0f, -1.0f, 0.5f, 2.5f, -2.5f, 3.0f, 1e-38f, 1.17549435e-38f, float.Epsilon, -float.Epsilon,
                float.MaxValue, -float.MaxValue, float.PositiveInfinity, float.NegativeInfinity, float.NaN, 1e20f,
                16777216.0f, 16777217.0f, 4294967296.0f, 2147483648.0f, -2147483648.0f,
            };
            Lcg rng = new Lcg(77);
            for (int k = 0; k < edge.Length + 40000; k++)
            {
                float x = k < edge.Length ? edge[k] : BitConverter.Int32BitsToSingle(unchecked((int)rng.Next()));
                AssertSameFloat(MathF.Sqrt(x), JiggleMath.Sqrt(x), "Sqrt(" + x + ")");
                AssertSameFloat(MathF.Abs(x), JiggleMath.Abs(x), "Abs(" + x + ")");
                AssertSameFloat(MathF.Floor(x), JiggleMath.Floor(x), "Floor(" + x + ")");
                Assert.AreEqual(float.IsNaN(x), JiggleMath.IsNaN(x), "IsNaN(" + x + ")");
                Assert.AreEqual(!(float.IsNaN(x) || float.IsInfinity(x)), JiggleMath.IsFinite(x), "IsFinite(" + x + ")");
            }
        }

        private sealed class HostScene
        {
            public JVec3[][] Rest;
            public JiggleParams[][] Params;
            public float[] TipScale;
            public float[] TipMass;
        }

        private static HostScene BuildHostScene(uint seed, int rigs)
        {
            Lcg rng = new Lcg(seed);
            HostScene sc = new HostScene();
            sc.Rest = new JVec3[rigs][];
            sc.Params = new JiggleParams[rigs][];
            sc.TipScale = new float[rigs];
            sc.TipMass = new float[rigs];
            for (int r = 0; r < rigs; r++)
            {
                int bones = 1 + (int)(rng.Next() % 9u);
                JVec3 b = new JVec3(r * 0.6f, rng.Range(0.0f, 1.0f), rng.Range(-0.5f, 0.5f));
                sc.Rest[r] = new JVec3[bones + 1];
                sc.Params[r] = new JiggleParams[bones];
                for (int i = 0; i <= bones; i++)
                {
                    sc.Rest[r][i] = b + new JVec3(0.0f, -0.1f * (float)i, 0.0f);
                }
                for (int i = 0; i < bones; i++)
                {
                    JiggleParams p = JiggleParams.Default();
                    p.Stiffness = rng.Range(0.2f, 0.9f);
                    p.Drag = rng.Range(0.0f, 0.3f);
                    p.Stretch = rng.Range(0.0f, 0.5f);
                    p.Mass = rng.Unit() < 0.6f ? rng.Range(0.5f, 4.0f) : 0.0f;
                    p.AngleLimited = rng.Unit() < 0.3f;
                    p.AngleLimit = rng.Range(0.2f, 0.9f);
                    sc.Params[r][i] = p;
                }
                sc.TipScale[r] = rng.Unit() < 0.2f ? float.NaN : rng.Range(0.5f, 2.0f);
                sc.TipMass[r] = rng.Unit() < 0.5f ? rng.Range(1.0f, 9.0f) : -1.0f;
            }
            return sc;
        }

        private static void PoseFor(Lcg rng, JVec3[] rest, int frame, int rig, JVec3[] pos, JQuat[] rots)
        {
            float t = (float)frame;
            JVec3 sway = new JVec3((float)Math.Sin(t * 0.3 + rig) * 0.08f, 0.0f, (float)Math.Cos(t * 0.21 + rig * 0.5) * 0.05f);
            if (frame == 30 && rig == 0)
            {
                sway = sway + new JVec3(12.0f, 0.0f, 0.0f);
            }
            for (int i = 0; i < pos.Length; i++)
            {
                pos[i] = rest[i] + sway;
                rots[i] = new JQuat(rng.Range(-1f, 1f), rng.Range(-1f, 1f), rng.Range(-1f, 1f), rng.Range(0.2f, 1f)).Normalized();
            }
        }

        private static bool SameBytes(void* a, void* b, long len)
        {
            byte* x = (byte*)a;
            byte* y = (byte*)b;
            for (long i = 0; i < len; i++)
            {
                if (x[i] != y[i])
                {
                    return false;
                }
            }
            return true;
        }

        private static void AssertHostEqualsWorld(JiggleHost h, JiggleWorld w, string where)
        {
            Assert.AreEqual(w.Rigs.Length, h.RigCount, where + ": rig count");
            Assert.AreEqual(w.Defs.Length, h.ParticleCount, where + ": particle count");
            fixed (JiggleRig* pr = w.Rigs)
            fixed (JiggleParticleDef* pd = w.Defs)
            fixed (JiggleParticleState* ps = w.State)
            fixed (JVec4* pp = w.PosePrev)
            fixed (JVec4* pc = w.PoseCurr)
            fixed (JVec4* pq = w.PoseRot)
            fixed (JiggleParticleOut* po = w.Out)
            {
                Assert.IsTrue(SameBytes(pr, h.Rigs, (long)h.RigCount * sizeof(JiggleRig)), where + ": rigs");
                Assert.IsTrue(SameBytes(pd, h.Defs, (long)h.ParticleCount * sizeof(JiggleParticleDef)), where + ": defs");
                Assert.IsTrue(SameBytes(ps, h.State, (long)h.ParticleCount * sizeof(JiggleParticleState)), where + ": state");
                Assert.IsTrue(SameBytes(pp, h.PosePrev, (long)h.ParticleCount * sizeof(JVec4)), where + ": pose prev");
                Assert.IsTrue(SameBytes(pc, h.PoseCurr, (long)h.ParticleCount * sizeof(JVec4)), where + ": pose curr");
                Assert.IsTrue(SameBytes(pq, h.PoseRot, (long)h.ParticleCount * sizeof(JVec4)), where + ": pose rot");
                Assert.IsTrue(SameBytes(po, h.Out, (long)h.ParticleCount * sizeof(JiggleParticleOut)), where + ": out");
            }
            Assert.AreEqual(w.Colliders.Length, h.ColliderCount, where + ": collider count");
            Assert.AreEqual(w.Refs.Length, h.RefCount, where + ": ref count");
            Assert.AreEqual(w.Fields.Length, h.FieldCount, where + ": field count");
            fixed (JiggleCollider* pk = w.Colliders)
            fixed (uint* pf = w.Refs)
            fixed (JiggleField* pl = w.Fields)
            {
                Assert.IsTrue(SameBytes(pk, h.Colliders, (long)h.ColliderCount * sizeof(JiggleCollider)), where + ": colliders");
                Assert.IsTrue(SameBytes(pf, h.Refs, (long)h.RefCount * sizeof(uint)), where + ": refs");
                Assert.IsTrue(SameBytes(pl, h.Fields, (long)h.FieldCount * sizeof(JiggleField)), where + ": fields");
            }
        }

        private static void WriteHostPose(JiggleHost h, int rig, JVec3[] pos, JQuat[] rots)
        {
            JiggleRig r = h.Rigs[rig];
            int off = (int)r.ParticleOffset;
            fixed (JVec3* pPos = pos)
            fixed (JQuat* pRots = rots)
            {
                JigglePose.Write(h.Rigs + rig, h.PosePrev + off, h.PoseCurr + off, h.PoseRot + off, h.TipScale[rig], pPos, pRots, rots.Length);
            }
        }

        [Test]
        public void HostMatchesTheWorldBitForBitOnBothBackends()
        {
            foreach (JiggleBackend backend in new[] { JiggleBackend.Managed, JiggleBackend.Native })
            {
                for (uint seed = 1; seed <= 6; seed++)
                {
                    HostScene sc = BuildHostScene(seed * 13u, 2 + (int)seed * 2);
                    JiggleWorld w = new JiggleWorld();
                    w.Cfg.SleepSpeed = 0.002f;
                    w.Cfg.SleepDeviation = 0.05f;
                    w.Cfg.SleepFrames = 8;
                    w.Cfg.WakeMotion = 0.01f;
                    w.Cfg.CouplingPasses = 1 + seed % 4u;
                    using (JiggleHost h = new JiggleHost(2, 8))
                    {
                        for (int r = 0; r < sc.Rest.Length; r++)
                        {
                            int a = w.AddChain(sc.Rest[r], sc.Params[r], sc.TipScale[r]);
                            int b = h.AddChain(sc.Rest[r], sc.Params[r], sc.TipScale[r]);
                            Assert.AreEqual(a, b);
                            if (sc.TipMass[r] > 0.0f)
                            {
                                int tip = sc.Rest[r].Length;
                                w.SetMass(a, tip, sc.TipMass[r]);
                                h.SetMass(b, tip, sc.TipMass[r]);
                            }
                        }
                        *h.Cfg = w.Cfg;
                        AssertHostEqualsWorld(h, w, backend + " seed " + seed + " built");

                        Lcg rng = new Lcg(seed * 977u);
                        Lcg frames = new Lcg(seed * 31u);
                        for (int f = 0; f < 70; f++)
                        {
                            for (int r = 0; r < sc.Rest.Length; r++)
                            {
                                JVec3[] pos = new JVec3[sc.Rest[r].Length];
                                JQuat[] rots = new JQuat[pos.Length];
                                PoseFor(rng, sc.Rest[r], f, r, pos, rots);
                                w.SetPose(r, pos, rots);
                                WriteHostPose(h, r, pos, rots);
                            }
                            float dt = f % 11 == 3 ? 0.0f : (f == 40 ? 0.3f : frames.Range(0.004f, 0.04f));
                            int cw = w.Step(dt, backend);
                            int ch = h.Step(dt, backend);
                            Assert.AreEqual(cw, ch);
                            Assert.AreEqual(Bits(w.Cfg.Time), Bits(h.Cfg->Time), "time");
                            Assert.AreEqual(w.Cfg.Steps, h.Cfg->Steps, "steps");
                            Assert.AreEqual(Bits(w.Cfg.Alpha), Bits(h.Cfg->Alpha), "alpha");
                            AssertHostEqualsWorld(h, w, backend + " seed " + seed + " frame " + f);
                        }
                    }
                }
            }
        }

        [Test]
        public void ClockGivesTheStepsAndAlphaOfTheWorld()
        {
            JiggleWorld w = new JiggleWorld();
            using (JiggleHost h = new JiggleHost())
            {
                Lcg rng = new Lcg(5);
                float[] odd = { float.NaN, float.PositiveInfinity, float.NegativeInfinity, -1.0f, 0.0f, 1.0f / 60.0f, 2.0f / 60.0f, 100.0f };
                for (int k = 0; k < 600; k++)
                {
                    float dt = k < odd.Length ? odd[k] : (k % 50 == 0 ? rng.Range(0.1f, 0.5f) : rng.Range(0.0f, 0.05f));
                    w.Step(dt, JiggleBackend.Managed);
                    h.Step(dt, JiggleBackend.Managed);
                    Assert.AreEqual(w.Cfg.Steps, h.Cfg->Steps, "steps at " + k);
                    Assert.AreEqual(Bits(w.Cfg.Alpha), Bits(h.Cfg->Alpha), "alpha at " + k);
                }
            }
        }

        [Test]
        public void HostRebuiltWithStateCarriedOverKeepsSimulatingTheSame()
        {
            HostScene sc = BuildHostScene(4242u, 5);
            // The new host has two extra rigs in front, so every old rig moves two places.
            HostScene extra = BuildHostScene(99u, 2);
            using (JiggleHost a = new JiggleHost(2, 8))
            using (JiggleHost b = new JiggleHost(2, 8))
            {
                for (int r = 0; r < sc.Rest.Length; r++)
                {
                    a.AddChain(sc.Rest[r], sc.Params[r], sc.TipScale[r]);
                }
                a.Cfg->CouplingPasses = 2;
                a.Cfg->SleepFrames = 8;
                Lcg rng = new Lcg(1);
                for (int f = 0; f < 40; f++)
                {
                    for (int r = 0; r < sc.Rest.Length; r++)
                    {
                        JVec3[] pos = new JVec3[sc.Rest[r].Length];
                        JQuat[] rots = new JQuat[pos.Length];
                        PoseFor(rng, sc.Rest[r], f, r, pos, rots);
                        WriteHostPose(a, r, pos, rots);
                    }
                    Assert.AreEqual(0, a.StepFixed(1, JiggleBackend.Managed));
                }

                for (int r = 0; r < extra.Rest.Length; r++)
                {
                    b.AddChain(extra.Rest[r], extra.Params[r], extra.TipScale[r]);
                }
                for (int r = 0; r < sc.Rest.Length; r++)
                {
                    b.AddChain(sc.Rest[r], sc.Params[r], sc.TipScale[r]);
                }
                *b.Cfg = *a.Cfg;
                for (int r = 0; r < sc.Rest.Length; r++)
                {
                    b.CopyRigRuntime(a, r, r + extra.Rest.Length);
                }

                for (int f = 40; f < 80; f++)
                {
                    for (int r = 0; r < sc.Rest.Length; r++)
                    {
                        JVec3[] pos = new JVec3[sc.Rest[r].Length];
                        JQuat[] rots = new JQuat[pos.Length];
                        PoseFor(rng, sc.Rest[r], f, r, pos, rots);
                        WriteHostPose(a, r, pos, rots);
                        WriteHostPose(b, r + extra.Rest.Length, pos, rots);
                    }
                    for (int r = 0; r < extra.Rest.Length; r++)
                    {
                        JVec3[] pos = new JVec3[extra.Rest[r].Length];
                        JQuat[] rots = new JQuat[pos.Length];
                        PoseFor(rng, extra.Rest[r], f, r + 50, pos, rots);
                        WriteHostPose(b, r, pos, rots);
                    }
                    Assert.AreEqual(0, a.StepFixed(1, JiggleBackend.Managed));
                    Assert.AreEqual(0, b.StepFixed(1, JiggleBackend.Managed));
                    for (int r = 0; r < sc.Rest.Length; r++)
                    {
                        JiggleRig ra = a.Rigs[r];
                        JiggleRig rb = b.Rigs[r + extra.Rest.Length];
                        long n = ra.ParticleCount;
                        Assert.AreEqual(ra.ParticleCount, rb.ParticleCount);
                        Assert.IsTrue(
                            SameBytes(a.State + ra.ParticleOffset, b.State + rb.ParticleOffset, n * sizeof(JiggleParticleState)),
                            "state of rig " + r + " at frame " + f);
                        Assert.IsTrue(
                            SameBytes(a.Out + ra.ParticleOffset, b.Out + rb.ParticleOffset, n * sizeof(JiggleParticleOut)),
                            "output of rig " + r + " at frame " + f);
                        Assert.AreEqual(ra.Flags, rb.Flags, "flags of rig " + r);
                    }
                }
            }
        }

        [Test]
        public void LocalRotationsComposeBackToTheResolvedWorldRotations()
        {
            HostScene sc = BuildHostScene(808u, 6);
            using (JiggleHost h = new JiggleHost())
            {
                for (int r = 0; r < sc.Rest.Length; r++)
                {
                    h.AddChain(sc.Rest[r], sc.Params[r], sc.TipScale[r]);
                }
                Lcg rng = new Lcg(3);
                for (int f = 0; f < 30; f++)
                {
                    for (int r = 0; r < sc.Rest.Length; r++)
                    {
                        JVec3[] pos = new JVec3[sc.Rest[r].Length];
                        JQuat[] rots = new JQuat[pos.Length];
                        PoseFor(rng, sc.Rest[r], f, r, pos, rots);
                        WriteHostPose(h, r, pos, rots);
                    }
                    Assert.AreEqual(0, h.StepFixed(1, JiggleBackend.Managed));
                }
                for (int r = 0; r < sc.Rest.Length; r++)
                {
                    JiggleRig rig = h.Rigs[r];
                    int off = (int)rig.ParticleOffset;
                    int bones = (int)rig.ParticleCount - 2;
                    JQuat[] local = new JQuat[bones];
                    fixed (JQuat* pLocal = local)
                    {
                        JigglePose.LocalRotations(h.PoseRot + off, h.Out + off, bones, pLocal);
                    }
                    JQuat world = JQuat.From4(h.PoseRot[off]);
                    for (int b = 1; b <= bones; b++)
                    {
                        world = JQuat.Mul(world, local[b - 1]).Normalized();
                        JQuat want = JQuat.From4(h.Out[off + b].Rot);
                        float dot = world.X * want.X + world.Y * want.Y + world.Z * want.Z + world.W * want.W;
                        Assert.Greater(MathF.Abs(dot), 0.99999f, "rig " + r + " bone " + b);
                    }
                }
            }
        }

        [Test]
        public void HostGrowsKeepsItsLayoutAndRefusesUseAfterDispose()
        {
            JiggleHost h = new JiggleHost(1, 1);
            Lcg rng = new Lcg(11);
            uint expectOffset = 0;
            for (int r = 0; r < 3000; r++)
            {
                int bones = 1 + (int)(rng.Next() % 5u);
                JVec3[] rest = new JVec3[bones + 1];
                for (int i = 0; i <= bones; i++)
                {
                    rest[i] = new JVec3(r * 0.1f, -0.1f * (float)i, 0.0f);
                }
                JiggleParams[] ps = new JiggleParams[bones];
                for (int i = 0; i < bones; i++)
                {
                    ps[i] = JiggleParams.Default();
                }
                Assert.AreEqual(r, h.AddChain(rest, ps));
                Assert.AreEqual(expectOffset, h.Rigs[r].ParticleOffset);
                expectOffset += (uint)(bones + 2);
            }
            Assert.AreEqual(3000, h.RigCount);
            Assert.AreEqual((int)expectOffset, h.ParticleCount);
            Assert.AreEqual(0, h.StepFixed(1, JiggleBackend.Managed), "a grown host has to pass the solver's checks");
            Assert.Throws<ArgumentException>(delegate { h.SetMass(0, 0, 1.0f); }, "the anchor has no mass");
            Assert.Throws<ArgumentException>(delegate { h.SetMass(3000, 1, 1.0f); }, "no such rig");
            Assert.Throws<ArgumentException>(
                delegate { h.AddChain(new JVec3[1], new JiggleParams[0]); }, "one point is not a chain");
            h.Dispose();
            h.Dispose();
            Assert.Throws<ObjectDisposedException>(
                delegate { h.AddChain(new JVec3[2], new JiggleParams[1]); }, "no use after Dispose");
        }

        [Test]
        public void HostWithCollidersAndFieldsMatchesTheWorldOnBothBackends()
        {
            foreach (JiggleBackend backend in new[] { JiggleBackend.Managed, JiggleBackend.Native })
            {
                JiggleWorld w = new JiggleWorld();
                using (JiggleHost h = new JiggleHost(1, 4))
                {
                    for (int k = 0; k < 5; k++)
                    {
                        JiggleCollider[] cs = ColliderCases(k);
                        w.AddCollider(cs[0]);
                        Assert.AreEqual(k, h.AddCollider(cs[0]));
                    }
                    JiggleField gusty = Field(
                        JiggleConst.FieldDirectional, JiggleConst.FieldAcceleration, 0, 12.0f, 0.0f,
                        new JVec4(0.4f, 1.0f, 0.0f, 0.0f), new JVec4(0.3f, 0.0f, 1.0f, 0.9f));
                    gusty.Turbulence = 0.7f;
                    gusty.Frequency = 1.4f;
                    JiggleField vortex = Field(
                        JiggleConst.FieldVortex, JiggleConst.FieldAcceleration, 1, 40.0f, 2.0f,
                        new JVec4(0.4f, 1.0f, 0.0f, 0.0f), new JVec4(0.0f, 1.0f, 0.0f, 0.0f));
                    w.AddField(gusty);
                    w.AddField(vortex);
                    Assert.AreEqual(0, h.AddField(gusty));
                    Assert.AreEqual(1, h.AddField(vortex));

                    JiggleParams p = JiggleParams.Default();
                    p.Stiffness = 0.3f;
                    p.Collide = true;
                    p.SegmentCollision = true;
                    p.CollisionRadius = 0.05f;
                    JiggleParams[] ps = { p, p, p, p };
                    for (int r = 0; r < 3; r++)
                    {
                        JVec3[] rest = new JVec3[HandRest.Length];
                        for (int i = 0; i < rest.Length; i++)
                        {
                            rest[i] = HandRest[i] + new JVec3(0.0f, 0.0f, 0.3f * (float)r);
                        }
                        int a = w.AddChain(rest, ps);
                        int b = h.AddChain(rest, ps);
                        uint[] list = r == 1 ? new uint[] { 4, 2, 0 } : new uint[] { 0, 1, 2, 3, 4 };
                        w.SetColliderList(a, list);
                        h.SetColliderList(b, list);
                    }
                    *h.Cfg = w.Cfg;
                    AssertHostEqualsWorld(h, w, backend + " built");
                    for (int f = 0; f < 60; f++)
                    {
                        for (int r = 0; r < 3; r++)
                        {
                            JVec3[] pos = new JVec3[HandRest.Length];
                            for (int i = 0; i < pos.Length; i++)
                            {
                                pos[i] = HandRest[i] + new JVec3(0.0f, (float)Math.Sin(f * 0.1 + i + r) * 0.03f, 0.3f * (float)r);
                            }
                            w.SetPose(r, pos);
                            WriteHostPose(h, r, pos, new JQuat[0]);
                        }
                        float dt = w.FixedDt * (float)(1 + f % 3);
                        Assert.AreEqual(w.Step(dt, backend), h.Step(dt, backend));
                        AssertHostEqualsWorld(h, w, backend + " frame " + f);
                    }
                }
            }
        }

        [Test]
        public void HostSetParamsBakesInPlaceAndRefusesTheWrongCount()
        {
            JiggleParams a = JiggleParams.Default();
            a.Stiffness = 0.2f;
            JiggleParams b = JiggleParams.Default();
            b.Stiffness = 0.9f;
            b.Mass = 3.0f;
            b.AngleLimited = true;
            JVec3[] rest = { new JVec3(0f, 1f, 0f), new JVec3(0f, 0.9f, 0f), new JVec3(0f, 0.8f, 0f), new JVec3(0f, 0.7f, 0f) };
            using (JiggleHost changed = new JiggleHost())
            using (JiggleHost fresh = new JiggleHost())
            {
                changed.AddChain(rest, new JiggleParams[] { a, a, a });
                fresh.AddChain(rest, new JiggleParams[] { b, b, b });
                changed.StepFixed(5, JiggleBackend.Managed);
                int rigBytes = sizeof(JiggleParticleState) * changed.ParticleCount;
                byte[] before = new byte[rigBytes];
                System.Runtime.InteropServices.Marshal.Copy((IntPtr)changed.State, before, 0, rigBytes);
                changed.SetParams(0, new JiggleParams[] { b, b, b });
                Assert.IsTrue(
                    SameBytes(changed.Defs, fresh.Defs, (long)changed.ParticleCount * sizeof(JiggleParticleDef)),
                    "baked in place the same as built");
                byte[] after = new byte[rigBytes];
                System.Runtime.InteropServices.Marshal.Copy((IntPtr)changed.State, after, 0, rigBytes);
                for (int i = 0; i < rigBytes; i++)
                {
                    Assert.AreEqual(before[i], after[i], "SetParams must not touch the state");
                }
                Assert.Throws<ArgumentException>(delegate { changed.SetParams(0, new JiggleParams[] { b, b }); }, "two for three bones");
                Assert.Throws<ArgumentException>(delegate { changed.SetParams(1, new JiggleParams[] { b, b, b }); }, "no such rig");
                changed.SetTipScale(0, 2.0f);
                Assert.AreEqual(2.0f, changed.TipScale[0]);
                changed.SetTipScale(0, -1.0f);
                Assert.AreEqual(1.0f, changed.TipScale[0], "a bad tip scale counts as 1");
            }
        }

        [Test]
        public void HostResetFlagIsClearedByTheSolverAndTheClockCarriesOver()
        {
            JiggleParams p = JiggleParams.Default();
            JVec3[] rest = { new JVec3(0f, 1f, 0f), new JVec3(0f, 0.9f, 0f), new JVec3(0f, 0.8f, 0f) };
            foreach (JiggleBackend backend in new[] { JiggleBackend.Managed, JiggleBackend.Native })
            {
                using (JiggleHost h = new JiggleHost())
                {
                    h.AddChain(rest, new JiggleParams[] { p, p });
                    h.Reset(0);
                    Assert.AreNotEqual(0u, h.Rigs[0].Flags & JiggleConst.RigReset, "set");
                    Assert.AreEqual(0, h.StepFixed(1, backend));
                    Assert.AreEqual(0u, h.Rigs[0].Flags & JiggleConst.RigReset, backend + ": cleared by the solver");
                    Assert.Throws<ArgumentException>(delegate { h.Reset(1); });
                }
            }
            using (JiggleHost a = new JiggleHost())
            using (JiggleHost b = new JiggleHost())
            {
                a.AddChain(rest, new JiggleParams[] { p, p });
                b.AddChain(rest, new JiggleParams[] { p, p });
                a.FixedDt = 1.0f / 30.0f;
                a.MaxSteps = 3;
                a.Step(0.05f, JiggleBackend.Managed);
                b.CopyClock(a);
                Assert.AreEqual(Bits(a.Accumulator), Bits(b.Accumulator));
                Assert.AreEqual(Bits(a.Cfg->Time), Bits(b.Cfg->Time));
                Assert.AreEqual(Bits(a.FixedDt), Bits(b.FixedDt));
                Assert.AreEqual(a.MaxSteps, b.MaxSteps);
                a.Step(0.02f, JiggleBackend.Managed);
                b.Step(0.02f, JiggleBackend.Managed);
                Assert.AreEqual(a.Cfg->Steps, b.Cfg->Steps);
                Assert.AreEqual(Bits(a.Cfg->Alpha), Bits(b.Cfg->Alpha));
                b.Accumulator = float.NaN;
                Assert.AreEqual(0.0f, b.Accumulator, "a bad value counts as 0");
            }
        }
    }
}
