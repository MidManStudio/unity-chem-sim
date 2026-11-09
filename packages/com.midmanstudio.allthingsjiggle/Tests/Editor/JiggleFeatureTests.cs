// ============================================================================
// NOTICE: Full documentation, design decisions, and fix history for this file
// live in docs/com.midmanstudio.allthingsjiggle.md, section "Tests/Editor/JiggleFeatureTests.cs"
// ============================================================================
using System;
using NUnit.Framework;

namespace MidManStudio.AllThingsJiggle.EditorTests
{
    /// <summary>
    /// The features of drop 2b that live in the Core: replacing colliders and fields in a host, switching rigs off and
    /// waking them, hits on one rig, the queue of one shot impulses, the level of detail rules, the view frustum test and
    /// the lists of colliders each rig gets. Every test that runs the solver runs both backends. The file runs under plain
    /// .NET in CI (see Tests~/DotnetHarness), so it must stay free of UnityEngine.
    /// </summary>
    [TestFixture]
    public unsafe class JiggleFeatureTests
    {
        private static readonly JiggleBackend[] Backends = { JiggleBackend.Managed, JiggleBackend.Native };

        // ------------------------------------------------------------------ small tools

        private static int Bits(float v)
        {
            return BitConverter.SingleToInt32Bits(v);
        }

        /// <summary>No drag and no gravity, so only what a test applies moves the chain.</summary>
        private static JiggleParams Plain()
        {
            JiggleParams p = JiggleParams.Default();
            p.Drag = 0.0f;
            p.AirDrag = 0.0f;
            p.Gravity = 0.0f;
            return p;
        }

        /// <summary>A chain going straight down from (x, 1, 0) with a bone every 0.2 meters.</summary>
        private static JVec3[] Down(int bones, float x)
        {
            JVec3[] rest = new JVec3[bones + 1];
            for (int k = 0; k <= bones; k++)
            {
                rest[k] = new JVec3(x, 1.0f - 0.2f * (float)k, 0.0f);
            }
            return rest;
        }

        private static JiggleParams[] Many(JiggleParams p, int bones)
        {
            JiggleParams[] ps = new JiggleParams[bones];
            for (int i = 0; i < bones; i++)
            {
                ps[i] = p;
            }
            return ps;
        }

        private static JiggleHost NewHost(JiggleParams p, int rigs, int bones)
        {
            JiggleHost h = new JiggleHost(2, 8);
            h.Cfg->GravityX = 0.0f;
            h.Cfg->GravityY = 0.0f;
            h.Cfg->GravityZ = 0.0f;
            for (int r = 0; r < rigs; r++)
            {
                h.AddChain(Down(bones, 10.0f * (float)r), Many(p, bones));
            }
            return h;
        }

        private static JVec3 World(JiggleHost h, int rig, int i)
        {
            return h.WorldPos(rig, i);
        }

        // Where particle i of a rig is according to the state, which is right before the first frame, when nothing was resolved yet.
        private static JVec3 StateWorld(JiggleHost h, int rig, int i)
        {
            return JVec3.From4(h.State[(int)h.Rigs[rig].ParticleOffset + i].Pos) + h.Rigs[rig].AnchorWorld;
        }

        private static float Dist(JVec3 a, JVec3 b)
        {
            return (a - b).Length();
        }

        private static void AssertSameOut(JiggleHost a, JiggleHost b, string where)
        {
            Assert.AreEqual(a.ParticleCount, b.ParticleCount, where + ": particle count");
            for (int i = 0; i < a.ParticleCount; i++)
            {
                JVec4 pa = a.Out[i].Pos;
                JVec4 pb = b.Out[i].Pos;
                JVec4 ra = a.Out[i].Rot;
                JVec4 rb = b.Out[i].Rot;
                bool same = Bits(pa.X) == Bits(pb.X) && Bits(pa.Y) == Bits(pb.Y) && Bits(pa.Z) == Bits(pb.Z)
                    && Bits(ra.X) == Bits(rb.X) && Bits(ra.Y) == Bits(rb.Y) && Bits(ra.Z) == Bits(rb.Z) && Bits(ra.W) == Bits(rb.W);
                Assert.IsTrue(same, where + ": the backends differ at particle " + i);
            }
        }

        private static JiggleField DirectionalImpulse(JVec3 dv)
        {
            float speed = dv.Length();
            return JiggleShapes.Field(
                JiggleConst.FieldDirectional, JiggleConst.FieldImpulse, JVec3.Zero, dv, 0.0f, speed, 0.0f, 0u, 0.0f, 0.0f);
        }

        // ------------------------------------------------------------------ the host

        [Test]
        public void HostReplacesAndClearsCollidersFieldsAndLists()
        {
            JiggleParams p = Plain();
            p.Collide = true;
            using (JiggleHost h = NewHost(p, 2, 3))
            {
                for (int k = 0; k < 3; k++)
                {
                    Assert.AreEqual(k, h.AddCollider(JiggleShapes.Sphere(new JVec3(0.0f, 0.5f, 0.0f), 0.1f * (float)(k + 1))));
                }
                h.SetColliderList(0, new uint[] { 0, 1, 2 });
                h.SetColliderList(1, new uint[] { 2 });
                Assert.AreEqual(4, h.RefCount);

                h.SetCollider(1, JiggleShapes.Sphere(new JVec3(1.0f, 2.0f, 3.0f), 0.75f));
                Assert.AreEqual(0.75f, h.Colliders[1].Radius, 1e-7, "replaced");
                Assert.AreEqual(1.0f, h.Colliders[1].Center.X, 1e-7, "replaced center");
                Assert.AreEqual(3, h.ColliderCount, "replacing adds nothing");
                Assert.AreEqual(4, h.RefCount, "replacing keeps the lists");
                Assert.Throws<ArgumentException>(delegate { h.SetCollider(3, JiggleShapes.Sphere(JVec3.Zero, 1.0f)); });
                Assert.Throws<ArgumentException>(delegate { h.SetCollider(-1, JiggleShapes.Sphere(JVec3.Zero, 1.0f)); });

                h.ClearColliderLists();
                Assert.AreEqual(0, h.RefCount);
                Assert.AreEqual(3, h.ColliderCount, "clearing the lists keeps the colliders");
                for (int r = 0; r < 2; r++)
                {
                    Assert.AreEqual(0u, h.Rigs[r].RefCount, "rig " + r + " list");
                    Assert.AreEqual(0u, h.Rigs[r].RefOffset, "rig " + r + " offset");
                }

                uint* raw = stackalloc uint[2];
                raw[0] = 1;
                raw[1] = 0;
                h.SetColliderList(1, raw, 2);
                Assert.AreEqual(2u, h.Rigs[1].RefCount);
                Assert.AreEqual(1u, h.Refs[h.Rigs[1].RefOffset]);
                Assert.AreEqual(0u, h.Refs[h.Rigs[1].RefOffset + 1u]);
                h.SetColliderList(0, null, 0);
                Assert.AreEqual(0u, h.Rigs[0].RefCount, "an empty list is allowed");
                Assert.Throws<ArgumentException>(delegate { h.SetColliderList(0, null, 1); });
                Assert.Throws<ArgumentException>(delegate { h.SetColliderList(0, null, -1); });
                Assert.Throws<ArgumentException>(delegate { h.SetColliderList(5, new uint[] { 0 }); });

                h.AddField(DirectionalImpulse(new JVec3(1.0f, 0.0f, 0.0f)));
                h.AddField(DirectionalImpulse(new JVec3(0.0f, 1.0f, 0.0f)));
                Assert.AreEqual(2, h.FieldCount);
                h.ClearFields();
                Assert.AreEqual(0, h.FieldCount);

                h.ClearColliders();
                Assert.AreEqual(0, h.ColliderCount);
                Assert.AreEqual(0, h.RefCount);
                foreach (JiggleBackend backend in Backends)
                {
                    Assert.AreEqual(0, h.StepFixed(2, backend), backend + ": a host with nothing in it still runs");
                }
            }
        }

        [Test]
        public void StaleListsAreRefusedUntilClearedAndClearingMakesThemValid()
        {
            // Why ClearColliders zeroes every rig: a rig that keeps a range past the end of the cleared list makes
            // the solver refuse the whole frame with ErrBadRig.
            JiggleParams p = Plain();
            p.Collide = true;
            using (JiggleHost h = NewHost(p, 1, 3))
            {
                h.AddCollider(JiggleShapes.Sphere(new JVec3(5.0f, 5.0f, 5.0f), 0.1f));
                h.SetColliderList(0, new uint[] { 0 });
                h.RefCount = 0;
                Assert.AreEqual(JiggleConst.ErrBadRig, h.StepFixed(1, JiggleBackend.Managed), "a stale list is refused");
                h.ClearColliders();
                foreach (JiggleBackend backend in Backends)
                {
                    Assert.AreEqual(0, h.StepFixed(1, backend), backend + ": cleared");
                }
            }
        }

        [Test]
        public void HostSwitchesRigsOffAndOnAndWakesThem()
        {
            JiggleParams p = Plain();
            foreach (JiggleBackend backend in Backends)
            {
                using (JiggleHost h = NewHost(p, 1, 3))
                {
                    JiggleStepConfig* cfg = h.Cfg;
                    cfg->SleepFrames = 2;
                    cfg->SleepSpeed = 0.01f;
                    cfg->SleepDeviation = 0.01f;
                    cfg->WakeMotion = 0.01f;

                    Assert.IsFalse(h.IsAsleep(0));
                    Assert.AreEqual(0, h.StepFixed(1, backend));
                    Assert.AreEqual(0, h.StepFixed(1, backend));
                    Assert.IsTrue(h.IsAsleep(0), backend + ": a chain at rest falls asleep");
                    h.Wake(0);
                    Assert.IsFalse(h.IsAsleep(0), backend + ": woken");
                    Assert.AreEqual(0u, h.Rigs[0].SleepCounter);

                    // Off: the solver skips the rig and its output is the pose.
                    int n = (int)h.Rigs[0].ParticleCount;
                    JVec4[] before = new JVec4[n];
                    for (int i = 0; i < n; i++)
                    {
                        before[i] = h.State[i].Pos;
                    }
                    h.SetDisabled(0, true);
                    Assert.IsTrue(h.IsDisabled(0));
                    h.SetDisabled(0, true);
                    Assert.AreEqual(JiggleConst.RigDisabled, h.Rigs[0].Flags & (JiggleConst.RigDisabled | JiggleConst.RigReset), "switching off twice changes nothing");
                    Assert.AreEqual(0, h.StepFixed(3, backend));
                    for (int i = 0; i < n; i++)
                    {
                        Assert.AreEqual(Bits(before[i].X), Bits(h.State[i].Pos.X), backend + ": state untouched " + i);
                        Assert.AreEqual(Bits(h.PoseCurr[i].Y), Bits(h.Out[i].Pos.Y), backend + ": output is the pose " + i);
                    }

                    // On again: it snaps to the pose once, then simulates.
                    h.SetDisabled(0, false);
                    Assert.IsFalse(h.IsDisabled(0));
                    Assert.AreNotEqual(0u, h.Rigs[0].Flags & JiggleConst.RigReset, "switching on asks for a snap");
                    Assert.AreEqual(0, h.StepFixed(1, backend));
                    Assert.AreEqual(0u, h.Rigs[0].Flags & (JiggleConst.RigReset | JiggleConst.RigDisabled));
                    Assert.Throws<ArgumentException>(delegate { h.SetDisabled(4, true); });
                    Assert.Throws<ArgumentException>(delegate { h.Wake(-1); });
                    Assert.Throws<ArgumentException>(delegate { h.IsAsleep(9); });
                }
            }
        }

        // ------------------------------------------------------------------ hits

        [Test]
        public void HitDoesWhatAnImpulseFieldDoesWhenDragIsOff()
        {
            JVec3 dv = new JVec3(3.0f, 0.0f, 1.0f);
            foreach (JiggleBackend backend in Backends)
            {
                using (JiggleHost hit = NewHost(Plain(), 1, 4))
                using (JiggleHost field = NewHost(Plain(), 1, 4))
                {
                    Assert.IsTrue(JiggleImpulse.Hit(hit, 0, dv));
                    field.AddField(DirectionalImpulse(dv));
                    Assert.AreEqual(0, hit.StepFixed(1, backend));
                    Assert.AreEqual(0, field.StepFixed(1, backend));
                    float moved = 0.0f;
                    for (int i = 1; i < 6; i++)
                    {
                        float d = Dist(World(hit, 0, i), World(field, 0, i));
                        Assert.AreEqual(0.0, d, 2e-5, backend + ": particle " + i);
                        JVec3 rest = new JVec3(0.0f, 1.0f - 0.2f * (float)i, 0.0f);
                        moved = Math.Max(moved, Dist(World(hit, 0, i), rest));
                    }
                    Assert.Greater(moved, 1e-3, backend + ": the hit moved the chain");
                }
            }
        }

        [Test]
        public void HitAtFadesWithDistanceSkipsTheAnchorAndWakesTheRig()
        {
            JiggleParams p = Plain();
            using (JiggleHost h = NewHost(p, 1, 4))
            {
                int n = (int)h.Rigs[0].ParticleCount;
                JVec4[] prev = new JVec4[n];
                for (int i = 0; i < n; i++)
                {
                    prev[i] = h.State[i].Prev;
                }
                // The chain hangs at x = 0 with particles every 0.2 meters from y = 1 down. A burst of radius 0.25 at the
                // second bone reaches that bone fully and its neighbours partly, and no one else.
                JVec3 center = StateWorld(h, 0, 2);
                h.Rigs[0].Flags |= JiggleConst.RigAsleep;
                bool hit = JiggleImpulse.HitAt(h, 0, center, 0.25f, 1u, new JVec3(0.0f, 0.0f, 2.0f));
                Assert.IsTrue(hit);
                Assert.IsFalse(h.IsAsleep(0), "a hit wakes the rig");
                float dt = h.FixedDt;
                for (int i = 0; i < n; i++)
                {
                    float shifted = prev[i].Z - h.State[i].Prev.Z;
                    float expected = 0.0f;
                    if (i != 0)
                    {
                        float w = JiggleFields.Weight(Dist(StateWorld(h, 0, i), center), 0.25f, 1u);
                        expected = 2.0f * dt * w;
                    }
                    Assert.AreEqual(expected, shifted, 1e-7, "particle " + i + " is moved by its share");
                }
                Assert.IsTrue(h.State[2].Prev.Z < prev[2].Z, "the center took the whole push");
                Assert.AreEqual(Bits(prev[n - 1].Z), Bits(h.State[n - 1].Prev.Z), "the far end is out of reach");

                // The same call with the point far away reaches nothing and leaves a sleeping rig asleep.
                h.Rigs[0].Flags |= JiggleConst.RigAsleep;
                Assert.IsFalse(JiggleImpulse.HitAt(h, 0, new JVec3(50.0f, 50.0f, 50.0f), 1.0f, 1u, new JVec3(0.0f, 0.0f, 2.0f)));
                Assert.IsTrue(h.IsAsleep(0), "nothing was hit, nothing woke");
            }
        }

        [Test]
        public void HitIsScaledByTheImpulseResponseOfEachParticle()
        {
            JiggleParams soft = Plain();
            soft.ImpulseResponse = 0.5f;
            JiggleParams none = Plain();
            none.ImpulseResponse = 0.0f;
            JiggleParams[] ps = { soft, none, soft };
            using (JiggleHost h = new JiggleHost(2, 8))
            {
                h.AddChain(Down(3, 0.0f), ps);
                int n = (int)h.Rigs[0].ParticleCount;
                float[] z = new float[n];
                for (int i = 0; i < n; i++)
                {
                    z[i] = h.State[i].Prev.Z;
                }
                Assert.IsTrue(JiggleImpulse.Hit(h, 0, new JVec3(0.0f, 0.0f, 6.0f)));
                float dt = h.FixedDt;
                Assert.AreEqual(3.0 * dt, z[1] - h.State[1].Prev.Z, 1e-7, "a response of 0.5 takes half");
                Assert.AreEqual(0.0, z[2] - h.State[2].Prev.Z, 1e-9, "a response of 0 takes nothing");
                Assert.AreEqual(3.0 * dt, z[3] - h.State[3].Prev.Z, 1e-7, "the last bone takes half too");
                Assert.AreEqual(3.0 * dt, z[4] - h.State[4].Prev.Z, 1e-7, "and the virtual end has the response of the last bone");
            }
        }

        [Test]
        public void HitRefusesWhatWouldBeLostAndWhatIsNotAVector()
        {
            using (JiggleHost h = NewHost(Plain(), 1, 3))
            {
                Assert.Throws<ArgumentNullException>(delegate { JiggleImpulse.Hit(null, 0, JVec3.Up); });
                Assert.Throws<ArgumentException>(delegate { JiggleImpulse.Hit(h, 1, JVec3.Up); });
                Assert.Throws<ArgumentException>(delegate { JiggleImpulse.Hit(h, -1, JVec3.Up); });
                Assert.IsFalse(JiggleImpulse.Hit(h, 0, new JVec3(float.NaN, 0.0f, 0.0f)), "not finite");
                Assert.IsFalse(JiggleImpulse.HitAt(h, 0, new JVec3(0.0f, float.PositiveInfinity, 0.0f), 1.0f, 1u, JVec3.Up), "point not finite");
                int n = (int)h.Rigs[0].ParticleCount;
                for (int i = 0; i < n; i++)
                {
                    Assert.AreEqual(Bits(h.State[i].Pos.X), Bits(h.State[i].Prev.X), "nothing changed " + i);
                }
                h.SetDisabled(0, true);
                Assert.IsFalse(JiggleImpulse.Hit(h, 0, JVec3.Up), "a rig that is off ignores hits");
                h.SetDisabled(0, false);
                Assert.IsFalse(JiggleImpulse.Hit(h, 0, JVec3.Up), "a rig that is about to snap ignores hits");
                h.StepFixed(1, JiggleBackend.Managed);
                Assert.IsTrue(JiggleImpulse.Hit(h, 0, JVec3.Up), "and takes them again after the snap");
            }
        }

        [Test]
        public void HitIsKeptByAFrameWithNoStepAndBothBackendsAgree()
        {
            JiggleHost[] hosts = new JiggleHost[2];
            for (int b = 0; b < 2; b++)
            {
                hosts[b] = NewHost(JiggleParams.Default(), 1, 4);
                Assert.IsTrue(JiggleImpulse.Hit(hosts[b], 0, new JVec3(0.0f, 0.0f, 4.0f)));
                // 1 ms is shorter than a fixed step, so this frame runs none, and the hit must still be there next frame.
                Assert.AreEqual(0, hosts[b].Step(0.001f, Backends[b]));
                Assert.AreEqual(0u, hosts[b].Cfg->Steps);
                for (int f = 0; f < 6; f++)
                {
                    Assert.AreEqual(0, hosts[b].Step(1.0f / 60.0f, Backends[b]));
                }
            }
            AssertSameOut(hosts[0], hosts[1], "after a hit");
            AssertFinite(hosts[0]);
            using (JiggleHost control = NewHost(JiggleParams.Default(), 1, 4))
            {
                for (int f = 0; f < 6; f++)
                {
                    control.Step(1.0f / 60.0f, JiggleBackend.Managed);
                }
                float apart = Dist(World(hosts[0], 0, 4), World(control, 0, 4));
                Assert.Greater(apart, 1e-3, "the hit left a mark that an unhit chain does not have");
            }
            hosts[0].Dispose();
            hosts[1].Dispose();
        }

        private static void AssertFinite(JiggleHost h)
        {
            for (int i = 0; i < h.ParticleCount; i++)
            {
                Assert.IsTrue(JVec3.From4(h.Out[i].Pos).IsFinite(), "output " + i + " is finite");
            }
        }

        // ------------------------------------------------------------------ one shot impulses

        [Test]
        public void QueueKeepsImpulsesUntilAFrameRanAStep()
        {
            JiggleFieldQueue q = new JiggleFieldQueue();
            JiggleField standing = DirectionalImpulse(new JVec3(1.0f, 0.0f, 0.0f));
            standing.Mode = JiggleConst.FieldAcceleration;
            using (JiggleHost h = NewHost(Plain(), 1, 3))
            {
                q.Add(standing);
                JiggleField shot = JiggleShapes.Burst(new JVec3(0.4f, 0.5f, 0.0f), 1.0f, 3.0f, 1u);
                shot.Mode = JiggleConst.FieldAcceleration;
                q.Enqueue(shot);
                Assert.AreEqual(1, q.PendingCount);
                q.Write(h);
                Assert.AreEqual(2, h.FieldCount, "the standing field and the impulse");
                Assert.AreEqual(JiggleConst.FieldAcceleration, h.Fields[0].Mode);
                Assert.AreEqual(JiggleConst.FieldImpulse, h.Fields[1].Mode, "queueing makes it an impulse whatever it was");

                q.Commit(0);
                Assert.AreEqual(1, q.PendingCount, "no step ran, so the impulse was not given");
                q.Write(h);
                Assert.AreEqual(2, h.FieldCount, "and it is written again");

                q.Commit(3);
                Assert.AreEqual(0, q.PendingCount, "a frame with steps gave it");
                q.Write(h);
                Assert.AreEqual(1, h.FieldCount, "only the standing field remains");

                q.Clear();
                q.Write(h);
                Assert.AreEqual(0, h.FieldCount);
            }
        }

        [Test]
        public void QueueDropsTheOldestImpulseWhenFull()
        {
            JiggleFieldQueue q = new JiggleFieldQueue();
            int total = JiggleFieldQueue.MaxPending + 40;
            for (int i = 0; i < total; i++)
            {
                q.Enqueue(JiggleShapes.Burst(JVec3.Zero, 1.0f, (float)(i + 1), 1u));
            }
            Assert.AreEqual(JiggleFieldQueue.MaxPending, q.PendingCount);
            using (JiggleHost h = NewHost(Plain(), 1, 3))
            {
                q.Write(h);
                Assert.AreEqual(JiggleFieldQueue.MaxPending, h.FieldCount);
                Assert.AreEqual((float)(total - JiggleFieldQueue.MaxPending + 1), h.Fields[0].Strength, "the oldest were dropped");
                Assert.AreEqual((float)total, h.Fields[h.FieldCount - 1].Strength, "the newest is kept");
            }
            q.DropPending();
            Assert.AreEqual(0, q.PendingCount);
        }

        [Test]
        public void BurstPushesAwayFromItsPointAndBothBackendsAgree()
        {
            JiggleHost[] hosts = new JiggleHost[2];
            for (int b = 0; b < 2; b++)
            {
                hosts[b] = NewHost(Plain(), 1, 4);
                JiggleFieldQueue q = new JiggleFieldQueue();
                q.Enqueue(JiggleShapes.Burst(new JVec3(0.3f, 0.5f, 0.0f), 1.0f, 4.0f, 1u));
                q.Write(hosts[b]);
                Assert.AreEqual(0, hosts[b].StepFixed(1, Backends[b]));
                q.Commit(hosts[b].Cfg->Steps);
                Assert.AreEqual(0, q.PendingCount);
            }
            AssertSameOut(hosts[0], hosts[1], "after a burst");
            float x = World(hosts[0], 0, 3).X;
            Assert.Less(x, -1e-4, "a burst on the +x side pushes the chain toward -x");
            hosts[0].Dispose();
            hosts[1].Dispose();
        }

        // ------------------------------------------------------------------ colliders in a set

        [Test]
        public void ShapesFillTheSolverLayout()
        {
            JiggleCollider cap = JiggleShapes.Capsule(new JVec3(1.0f, 2.0f, 3.0f), new JVec3(0.0f, 4.0f, 0.0f), 0.5f, 0.25f);
            Assert.AreEqual(JiggleConst.ColliderCapsule, cap.Kind);
            Assert.AreEqual(1.0, cap.Axis.Y, 1e-6, "the axis is made a unit vector");
            Assert.AreEqual(0.5, cap.HalfHeight, 1e-7);
            Assert.AreEqual(0.25, cap.Radius, 1e-7);
            Assert.AreEqual(3.0, cap.Center.Z, 1e-7);

            JiggleCollider pl = JiggleShapes.Plane(new JVec3(0.0f, 1.0f, 0.0f), new JVec3(0.0f, 0.0f, -3.0f));
            Assert.AreEqual(JiggleConst.ColliderPlane, pl.Kind);
            Assert.AreEqual(-1.0, pl.Axis.Z, 1e-6, "the normal is a unit vector");
            Assert.AreEqual(1.0, pl.Center.Y, 1e-7, "the point is the center");

            JiggleCollider bx = JiggleShapes.Box(JVec3.Zero, new JVec3(1.0f, -2.0f, 3.0f), JQuat.Identity, -1.0f);
            Assert.AreEqual(JiggleConst.ColliderBox, bx.Kind);
            Assert.AreEqual(0.0, bx.Extents.Y, 1e-9, "a negative extent is 0");
            Assert.AreEqual(0.0, bx.Radius, 1e-9, "a negative rounding is 0");
            Assert.AreEqual(1.0, bx.Rot.W, 1e-7, "identity rotation");

            Assert.AreEqual(0.0, JiggleShapes.Sphere(JVec3.Zero, float.NaN).Radius, 1e-9, "a bad radius is 0");
            Assert.AreEqual(JiggleConst.ColliderInsideSphere, JiggleShapes.InsideSphere(JVec3.Zero, 2.0f).Kind);

            JiggleField burst = JiggleShapes.Burst(new JVec3(1.0f, 0.0f, 0.0f), 2.0f, 5.0f, 2u);
            Assert.AreEqual(JiggleConst.FieldRadial, burst.Kind);
            Assert.AreEqual(JiggleConst.FieldImpulse, burst.Mode);
            Assert.AreEqual(2u, burst.Power);
            Assert.AreEqual(5.0, burst.Strength, 1e-7);
            JiggleField gust = JiggleShapes.Field(
                JiggleConst.FieldDirectional, JiggleConst.FieldAcceleration, JVec3.Zero, new JVec3(0.0f, 0.0f, 9.0f), 0.7f, 3.0f, 0.0f, 0u, 0.5f, 2.0f);
            Assert.AreEqual(1.0, gust.Dir.Z, 1e-6, "the direction is a unit vector");
            Assert.AreEqual(0.7, gust.Dir.W, 1e-7, "the gust scale rides in W");
        }

        [Test]
        public void NearKnowsTheReachOfEveryKind()
        {
            JVec3 anchor = new JVec3(3.0f, 0.0f, 0.0f);
            JiggleCollider s = JiggleShapes.Sphere(JVec3.Zero, 0.5f);
            Assert.IsFalse(JiggleColliderSet.Near(s, anchor, 2.0f));
            Assert.IsTrue(JiggleColliderSet.Near(s, anchor, 2.6f));
            JiggleCollider c = JiggleShapes.Capsule(JVec3.Zero, JVec3.Up, 1.0f, 0.5f);
            Assert.IsFalse(JiggleColliderSet.Near(c, anchor, 1.4f));
            Assert.IsTrue(JiggleColliderSet.Near(c, anchor, 1.6f));
            JiggleCollider b = JiggleShapes.Box(JVec3.Zero, new JVec3(1.0f, 1.0f, 1.0f), JQuat.Identity, 0.5f);
            Assert.IsFalse(JiggleColliderSet.Near(b, anchor, 0.7f), "half the diagonal of the box and its rounding");
            Assert.IsTrue(JiggleColliderSet.Near(b, anchor, 1.3f));
            JVec3 far = new JVec3(1.0e6f, 0.0f, 0.0f);
            Assert.IsTrue(JiggleColliderSet.Near(JiggleShapes.Plane(JVec3.Zero, JVec3.Up), far, 0.0f), "a plane has no edge");
            Assert.IsTrue(JiggleColliderSet.Near(JiggleShapes.InsideSphere(JVec3.Zero, 1.0f), far, 0.0f), "nor does an inside sphere");
            JiggleCollider odd = new JiggleCollider();
            odd.Kind = 99;
            Assert.IsTrue(JiggleColliderSet.Near(odd, far, 0.0f), "a kind nobody knows is kept");
        }

        // Three chains 10 meters apart, each with colliding bones, and one more that does not collide.
        private static JiggleHost SetHost(out int plainRig)
        {
            JiggleParams p = Plain();
            p.Collide = true;
            p.CollisionRadius = 0.05f;
            JiggleHost h = new JiggleHost(2, 8);
            h.Cfg->GravityX = 0.0f;
            h.Cfg->GravityY = 0.0f;
            h.Cfg->GravityZ = 0.0f;
            for (int r = 0; r < 3; r++)
            {
                h.AddChain(Down(3, 10.0f * (float)r), Many(p, 3));
            }
            plainRig = h.AddChain(Down(3, 30.0f), Many(Plain(), 3));
            return h;
        }

        private static uint[] ListOf(JiggleHost h, int rig)
        {
            uint[] l = new uint[h.Rigs[rig].RefCount];
            for (int i = 0; i < l.Length; i++)
            {
                l[i] = h.Refs[h.Rigs[rig].RefOffset + (uint)i];
            }
            return l;
        }

        private static string Show(uint[] l)
        {
            return "[" + string.Join(",", Array.ConvertAll(l, delegate (uint v) { return v.ToString(); })) + "]";
        }

        private static void AssertList(JiggleHost h, int rig, string expected, string what)
        {
            Assert.AreEqual(expected, Show(ListOf(h, rig)), what);
        }

        [Test]
        public void SetGivesEachRigTheCollidersThatReachItOnLayersItDoesNotIgnore()
        {
            int plainRig;
            using (JiggleHost h = SetHost(out plainRig))
            {
                JiggleColliderSet set = new JiggleColliderSet();
                set.Add(JiggleShapes.Sphere(new JVec3(0.1f, 0.5f, 0.0f), 0.2f), 1, false);          // 0 near rig 0
                set.Add(JiggleShapes.Sphere(new JVec3(10.1f, 0.5f, 0.0f), 0.2f), 2, false);         // 1 near rig 1
                set.Add(JiggleShapes.Plane(new JVec3(0.0f, -5.0f, 0.0f), JVec3.Up), 1, false);      // 2 reaches everyone
                set.Add(JiggleShapes.Sphere(new JVec3(100.0f, 0.5f, 0.0f), 0.2f), 0, false);        // 3 near no one
                set.Add(JiggleShapes.Capsule(new JVec3(10.0f, 0.5f, 0.0f), JVec3.Up, 0.3f, 0.1f), 40, false); // 4 layer 40 counts as 0
                Assert.AreEqual(5, set.Count);

                int written = set.Write(h, null, null);
                Assert.AreEqual(5, h.ColliderCount);
                AssertList(h, 0, "[0,2]", "rig 0");
                AssertList(h, 1, "[1,2,4]", "rig 1");
                AssertList(h, 2, "[2]", "rig 2 has only the plane");
                AssertList(h, plainRig, "[]", "a rig with no colliding bone needs no list");
                Assert.AreEqual(6, written, "indices written: 2 + 3 + 1");

                // A set bit means no collision: rig 0 ignores layer 1, which removes the sphere and the plane.
                uint[] ignore = new uint[h.RigCount];
                ignore[0] = 1u << 1;
                ignore[1] = 1u << 2;
                ignore[2] = uint.MaxValue;
                set.Write(h, null, ignore);
                AssertList(h, 0, "[]", "rig 0 ignores layer 1");
                AssertList(h, 1, "[2,4]", "rig 1 ignores layer 2 and keeps layer 0 and 1");
                AssertList(h, 2, "[]", "all bits set: tests nothing");

                // The anchors given by the caller replace the ones the host has.
                JVec3[] anchors = new JVec3[h.RigCount];
                anchors[0] = new JVec3(100.0f, 1.0f, 0.0f);
                anchors[1] = new JVec3(10.0f, 1.0f, 0.0f);
                anchors[2] = new JVec3(20.0f, 1.0f, 0.0f);
                set.Write(h, anchors, null);
                AssertList(h, 0, "[2,3]", "rig 0 is now by the far sphere and not by its own");
                AssertList(h, 1, "[1,2,4]", "rig 1 did not move");

                // Writing again replaces, and a cleared set clears the host.
                int refsBefore = h.RefCount;
                set.Write(h, anchors, null);
                Assert.AreEqual(refsBefore, h.RefCount, "the lists are replaced, not appended");
                set.Clear();
                set.Write(h, null, null);
                Assert.AreEqual(0, h.ColliderCount);
                Assert.AreEqual(0, h.RefCount);
                Assert.Throws<ArgumentNullException>(delegate { set.Write(null, null, null); });
            }
        }

        [Test]
        public void SetReachGrowsWithTheMarginAndTheRigExtent()
        {
            int plainRig;
            using (JiggleHost h = SetHost(out plainRig))
            {
                JiggleColliderSet set = new JiggleColliderSet();
                // The farthest particle, the virtual end, is 0.8 from the anchor, the particles are 0.05 thick and the sphere
                // has a radius of 0.1, so the sphere is in reach up to 0.95 away plus the margin.
                set.Add(JiggleShapes.Sphere(new JVec3(1.0f, 1.0f, 0.0f), 0.1f), 0, false);
                set.Margin = 0.0f;
                set.Write(h, null, null);
                AssertList(h, 0, "[]", "out of reach without a margin: 1.0 away, against 0.8 (the virtual end counts) + 0.05 + 0.1");
                set.Margin = 0.5f;
                set.Write(h, null, null);
                AssertList(h, 0, "[0]", "in reach with a margin");
                set.Margin = -3.0f;
                set.Write(h, null, null);
                AssertList(h, 0, "[]", "a negative margin counts as 0");
            }
        }

        [Test]
        public void SetWakesSleepingRigsOnlyForNearMovedCollidersOnLayersTheyTest()
        {
            int plainRig;
            using (JiggleHost h = SetHost(out plainRig))
            {
                JiggleColliderSet set = new JiggleColliderSet();
                JiggleCollider near0 = JiggleShapes.Sphere(new JVec3(0.1f, 0.5f, 0.0f), 0.2f);
                JiggleCollider far = JiggleShapes.Sphere(new JVec3(100.0f, 0.5f, 0.0f), 0.2f);

                // Still colliders do not wake anything.
                h.Rigs[0].Flags |= JiggleConst.RigAsleep;
                set.Add(near0, 3, false);
                set.Write(h, null, null);
                Assert.IsTrue(h.IsAsleep(0), "a collider that did not move leaves the sleeper alone");

                // A moved one far away does not either.
                set.Clear();
                set.Add(far, 3, true);
                set.Write(h, null, null);
                Assert.IsTrue(h.IsAsleep(0), "a moved collider out of reach");

                // A moved one on a layer the rig ignores does not.
                set.Clear();
                set.Add(near0, 3, true);
                uint[] ignore = new uint[h.RigCount];
                ignore[0] = 1u << 3;
                set.Write(h, null, ignore);
                Assert.IsTrue(h.IsAsleep(0), "a moved collider on an ignored layer");

                // A rig that has no colliding bone never wakes for it.
                h.Rigs[plainRig].Flags |= JiggleConst.RigAsleep;
                set.Clear();
                set.Add(JiggleShapes.Sphere(new JVec3(30.1f, 0.5f, 0.0f), 0.2f), 3, true);
                set.Write(h, null, null);
                Assert.IsTrue(h.IsAsleep(plainRig), "nothing to collide, nothing to wake for");

                // A moved one near and tested does.
                set.Clear();
                set.Add(near0, 3, true);
                set.Write(h, null, null);
                Assert.IsFalse(h.IsAsleep(0), "a moving collider wakes the sleeper it is near");
                Assert.AreEqual(0u, h.Rigs[0].SleepCounter);
            }
        }

        [Test]
        public void SetCanBeRewrittenEveryFrameWithoutGrowingTheHost()
        {
            int plainRig;
            using (JiggleHost h = SetHost(out plainRig))
            {
                JiggleColliderSet set = new JiggleColliderSet();
                int refs = -1;
                for (int f = 0; f < 500; f++)
                {
                    set.Clear();
                    for (int k = 0; k < 40; k++)
                    {
                        set.Add(JiggleShapes.Sphere(new JVec3(0.05f * (float)k, 0.5f, 0.0f), 0.1f), k % 32, (f & 1) == 1);
                    }
                    set.Write(h, null, null);
                    if (refs < 0)
                    {
                        refs = h.RefCount;
                    }
                    Assert.AreEqual(refs, h.RefCount, "frame " + f + ": the same scene writes the same lists");
                    Assert.AreEqual(40, h.ColliderCount);
                }
                Assert.AreEqual(0, h.StepFixed(2, JiggleBackend.Managed));
            }
        }

        [Test]
        public void CollidersFromTheSetHoldAChainOffAndAnIgnoredLayerLetsItThrough()
        {
            foreach (JiggleBackend backend in Backends)
            {
                JiggleHost[] hosts = new JiggleHost[2];
                for (int variant = 0; variant < 2; variant++)
                {
                    JiggleParams p = Plain();
                    p.Collide = true;
                    p.CollisionRadius = 0.05f;
                    p.Stiffness = 1.0f;
                    hosts[variant] = new JiggleHost(2, 8);
                    hosts[variant].Cfg->GravityX = 0.0f;
                    hosts[variant].Cfg->GravityY = 0.0f;
                    hosts[variant].Cfg->GravityZ = 0.0f;
                    hosts[variant].AddChain(Down(4, 0.0f), Many(p, 4));
                    JiggleColliderSet set = new JiggleColliderSet();
                    // A ball sitting on the hanging chain at the height of its third bone.
                    set.Add(JiggleShapes.Sphere(new JVec3(0.0f, 0.4f, 0.0f), 0.15f), 7, false);
                    uint[] ignore = new uint[1];
                    ignore[0] = variant == 0 ? 0u : (1u << 7);
                    set.Write(hosts[variant], null, ignore);
                    for (int f = 0; f < 4; f++)
                    {
                        Assert.AreEqual(0, hosts[variant].StepFixed(1, backend));
                    }
                }
                JVec3 ball = new JVec3(0.0f, 0.4f, 0.0f);
                bool anyInside = false;
                for (int i = 1; i <= 4; i++)
                {
                    float held = Dist(World(hosts[0], 0, i), ball);
                    Assert.IsTrue(held >= 0.15f + 0.05f - 1e-4f, backend + ": bone " + i + " is outside the ball, at " + held);
                    if (Dist(World(hosts[1], 0, i), ball) < 0.15f + 0.05f - 1e-3f)
                    {
                        anyInside = true;
                    }
                }
                Assert.IsTrue(anyInside, backend + ": with the layer ignored a bone is inside the ball");
                hosts[0].Dispose();
                hosts[1].Dispose();
            }
        }

        // ------------------------------------------------------------------ level of detail

        [Test]
        public void LodGoesDownAtTheLimitsAndBackUpOnlyPastTheHysteresis()
        {
            JiggleLodSettings s = new JiggleLodSettings();
            s.CollisionDistance = 10.0f;
            s.CullDistance = 20.0f;
            s.Hysteresis = 2.0f;
            JiggleLodTracker t = new JiggleLodTracker();
            float dt = 0.016f;
            Assert.AreEqual(JiggleLodLevel.Full, JiggleLod.Decide(s, ref t, 5.0f, true, dt));
            Assert.AreEqual(JiggleLodLevel.Full, JiggleLod.Decide(s, ref t, 9.99f, true, dt));
            Assert.AreEqual(JiggleLodLevel.NoCollision, JiggleLod.Decide(s, ref t, 10.0f, true, dt));
            Assert.AreEqual(JiggleLodLevel.NoCollision, JiggleLod.Decide(s, ref t, 8.5f, true, dt), "inside the hysteresis band");
            Assert.AreEqual(JiggleLodLevel.Full, JiggleLod.Decide(s, ref t, 7.9f, true, dt), "past it");
            Assert.AreEqual(JiggleLodLevel.Off, JiggleLod.Decide(s, ref t, 20.0f, true, dt));
            Assert.AreEqual(JiggleLodLevel.Off, JiggleLod.Decide(s, ref t, 18.5f, true, dt), "culled stays culled in the band");
            Assert.AreEqual(JiggleLodLevel.NoCollision, JiggleLod.Decide(s, ref t, 17.9f, true, dt), "back from off to the middle level");
            Assert.AreEqual(JiggleLodLevel.NoCollision, JiggleLod.Decide(s, ref t, 8.5f, true, dt));
            Assert.AreEqual(JiggleLodLevel.Full, JiggleLod.Decide(s, ref t, 1.0f, true, dt));
            Assert.AreEqual(JiggleLodLevel.Off, JiggleLod.Decide(s, ref t, 500.0f, true, dt), "straight to off");
            Assert.AreEqual(JiggleLodLevel.Full, JiggleLod.Decide(s, ref t, 1.0f, true, dt), "and straight back");
        }

        [Test]
        public void LodLimitsOfZeroAreOffAndBadNumbersKeepTheLevel()
        {
            JiggleLodSettings none = JiggleLodSettings.Always();
            JiggleLodTracker t = new JiggleLodTracker();
            Assert.AreEqual(JiggleLodLevel.Full, JiggleLod.Decide(none, ref t, 1.0e9f, false, 1.0f), "no limits, no offscreen cull");

            JiggleLodSettings s = new JiggleLodSettings();
            s.CollisionDistance = 10.0f;
            s.Hysteresis = -5.0f;
            t = new JiggleLodTracker();
            Assert.AreEqual(JiggleLodLevel.NoCollision, JiggleLod.Decide(s, ref t, 12.0f, true, 0.016f));
            Assert.AreEqual(JiggleLodLevel.NoCollision, JiggleLod.Decide(s, ref t, float.NaN, true, 0.016f), "a bad distance keeps the level");
            Assert.AreEqual(JiggleLodLevel.NoCollision, JiggleLod.Decide(s, ref t, float.PositiveInfinity, true, 0.016f));
            Assert.AreEqual(JiggleLodLevel.Full, JiggleLod.Decide(s, ref t, 9.99f, true, 0.016f), "a negative hysteresis counts as 0");
            t = new JiggleLodTracker();
            Assert.AreEqual(JiggleLodLevel.Full, JiggleLod.Decide(s, ref t, float.NaN, true, 0.016f), "and a first frame with one is full");
        }

        [Test]
        public void LodOffscreenWaitsForTheDelayAndComesBackAtOnce()
        {
            JiggleLodSettings s = new JiggleLodSettings();
            s.CullOffscreen = true;
            s.OffscreenDelay = 0.5f;
            JiggleLodTracker t = new JiggleLodTracker();
            Assert.AreEqual(JiggleLodLevel.Full, JiggleLod.Decide(s, ref t, 3.0f, true, 0.2f));
            Assert.AreEqual(JiggleLodLevel.Full, JiggleLod.Decide(s, ref t, 3.0f, false, 0.2f));
            Assert.AreEqual(JiggleLodLevel.Full, JiggleLod.Decide(s, ref t, 3.0f, false, 0.2f), "0.4 seconds unseen");
            Assert.AreEqual(JiggleLodLevel.Off, JiggleLod.Decide(s, ref t, 3.0f, false, 0.2f), "0.6 seconds unseen");
            Assert.AreEqual(JiggleLodLevel.Off, JiggleLod.Decide(s, ref t, 3.0f, false, 0.2f));
            Assert.AreEqual(JiggleLodLevel.Full, JiggleLod.Decide(s, ref t, 3.0f, true, 0.2f), "seen again");
            Assert.AreEqual(0.0f, t.OffscreenTime, "the clock starts over");
            Assert.AreEqual(JiggleLodLevel.Full, JiggleLod.Decide(s, ref t, 3.0f, false, 0.2f), "0.2 seconds unseen again");

            s.OffscreenDelay = 0.0f;
            t = new JiggleLodTracker();
            Assert.AreEqual(JiggleLodLevel.Off, JiggleLod.Decide(s, ref t, 3.0f, false, 0.016f), "a delay of 0 is at once");

            s.OffscreenDelay = 0.5f;
            t = new JiggleLodTracker();
            JiggleLod.Decide(s, ref t, 3.0f, false, float.NaN);
            JiggleLod.Decide(s, ref t, 3.0f, false, -1.0f);
            Assert.AreEqual(0.0f, t.OffscreenTime, "a bad frame time adds nothing");

            s.CullOffscreen = false;
            t = new JiggleLodTracker();
            Assert.AreEqual(JiggleLodLevel.Full, JiggleLod.Decide(s, ref t, 3.0f, false, 100.0f), "not culling offscreen ignores the camera");
        }

        [Test]
        public void LodOffscreenAndDistanceTakeTheLowerLevel()
        {
            JiggleLodSettings s = new JiggleLodSettings();
            s.CollisionDistance = 10.0f;
            s.CullOffscreen = true;
            s.OffscreenDelay = 0.0f;
            JiggleLodTracker t = new JiggleLodTracker();
            Assert.AreEqual(JiggleLodLevel.Off, JiggleLod.Decide(s, ref t, 50.0f, false, 0.016f), "far and unseen");
            Assert.AreEqual(JiggleLodLevel.NoCollision, JiggleLod.Decide(s, ref t, 50.0f, true, 0.016f), "far and seen");
            Assert.AreEqual(JiggleLodLevel.Off, JiggleLod.Decide(s, ref t, 1.0f, false, 0.016f), "near and unseen");
        }

        [Test]
        public void FrustumTestUsesTheUnityPlaneConvention()
        {
            // The cube from -1 to 1 on every axis, with the normals pointing in as Unity's planes do.
            JVec4[] cube =
            {
                new JVec4(1.0f, 0.0f, 0.0f, 1.0f), new JVec4(-1.0f, 0.0f, 0.0f, 1.0f),
                new JVec4(0.0f, 1.0f, 0.0f, 1.0f), new JVec4(0.0f, -1.0f, 0.0f, 1.0f),
                new JVec4(0.0f, 0.0f, 1.0f, 1.0f), new JVec4(0.0f, 0.0f, -1.0f, 1.0f)
            };
            Assert.IsTrue(JiggleLod.SphereInFrustum(cube, 6, JVec3.Zero, 0.1f), "inside");
            Assert.IsTrue(JiggleLod.SphereInFrustum(cube, 6, new JVec3(1.5f, 0.0f, 0.0f), 1.0f), "straddling a face");
            Assert.IsFalse(JiggleLod.SphereInFrustum(cube, 6, new JVec3(3.0f, 0.0f, 0.0f), 1.0f), "outside the +x face");
            Assert.IsFalse(JiggleLod.SphereInFrustum(cube, 6, new JVec3(0.0f, -3.0f, 0.0f), 1.0f), "outside the -y face");
            Assert.IsFalse(JiggleLod.SphereInFrustum(cube, 6, new JVec3(0.0f, 0.0f, 5.0f), 0.5f), "outside the +z face");
            Assert.IsTrue(JiggleLod.SphereInFrustum(cube, 6, new JVec3(0.0f, 0.0f, 5.0f), 10.0f), "a big sphere reaches in");
            Assert.IsTrue(JiggleLod.SphereInFrustum(cube, 2, new JVec3(0.0f, 3.0f, 0.0f), 0.0f), "only the first two planes are tested");
            Assert.IsTrue(JiggleLod.SphereInFrustum(cube, 100, JVec3.Zero, 0.1f), "a count past the array is cut to it");
            Assert.IsTrue(JiggleLod.SphereInFrustum(null, 6, new JVec3(99.0f, 0.0f, 0.0f), 0.0f), "no planes sees everything");
            Assert.IsTrue(JiggleLod.SphereInFrustum(cube, 0, new JVec3(99.0f, 0.0f, 0.0f), 0.0f), "no planes sees everything");
            Assert.IsTrue(JiggleLod.SphereInFrustum(cube, 6, new JVec3(float.NaN, 0.0f, 0.0f), 1.0f), "a bad sphere counts as seen");
            Assert.IsFalse(JiggleLod.SphereInFrustum(cube, 6, new JVec3(3.0f, 0.0f, 0.0f), -5.0f), "a negative radius counts as 0");
        }
    }
}
