// ============================================================================
// NOTICE: Full documentation, design decisions, and fix history for this file
// live in docs/com.midmanstudio.allthingsjiggle.md, section "Tests/Editor/JiggleUnityLayerTests.cs"
// ============================================================================
using System;
using NUnit.Framework;
using UnityEngine;

namespace MidManStudio.AllThingsJiggle.EditorTests
{
    /// <summary>
    /// Tests of the Unity layer that need the editor: the settings of a chain and the Burst compiled solver against
    /// the managed one, and the components of drop 2b. The transform jobs need a scene and are tried in the lab
    /// (MidManStudio, AllThingsJiggle, Jiggle Lab).
    /// </summary>
    public sealed unsafe class JiggleUnityLayerTests
    {
        [Test]
        public void DefaultSettingsBakeToTheDefaultsOfTheCore()
        {
            JiggleChainSettings s = new JiggleChainSettings();
            JiggleParams p = s.ToParams(0, 4);
            JiggleParams d = JiggleParams.Default();
            Assert.AreEqual(d.Stiffness, p.Stiffness, 1e-6f);
            Assert.AreEqual(d.Drag, p.Drag, 1e-6f);
            Assert.AreEqual(d.Stretch, p.Stretch, 1e-6f);
            Assert.AreEqual(d.Gravity, p.Gravity, 1e-6f);
            Assert.AreEqual(d.Blend, p.Blend, 1e-6f);
            Assert.AreEqual(d.Mass, p.Mass, 1e-6f);
            Assert.AreEqual(d.AngleLimited, p.AngleLimited);
        }

        [Test]
        public void StiffnessCurveScalesAlongTheChainAndOneBoneUsesTheStart()
        {
            JiggleChainSettings s = new JiggleChainSettings();
            s.stiffness = 0.8f;
            s.stiffnessAlong = AnimationCurve.Linear(0f, 1f, 1f, 0.25f);
            JiggleParams[] all = s.ToParams(5);
            Assert.AreEqual(5, all.Length);
            Assert.AreEqual(0.8f, all[0].Stiffness, 1e-5f);
            Assert.AreEqual(0.8f * 0.625f, all[2].Stiffness, 1e-5f);
            Assert.AreEqual(0.8f * 0.25f, all[4].Stiffness, 1e-5f);
            Assert.AreEqual(0.8f, s.ToParams(0, 1).Stiffness, 1e-5f, "a one bone chain is at the start of the curve");
            s.stiffnessAlong = null;
            Assert.AreEqual(0.8f, s.ToParams(3, 5).Stiffness, 1e-5f, "no curve means no scaling");
        }

        private static JiggleHost MakeHost(int rigs, bool coupled)
        {
            JiggleHost h = new JiggleHost(rigs, rigs * 10);
            JiggleParams p = JiggleParams.Default();
            p.Mass = coupled ? 1.0f : 0.0f;
            JiggleParams[] ps = { p, p, p, p, p, p, p, p };
            JVec3[] rest = new JVec3[9];
            for (int r = 0; r < rigs; r++)
            {
                for (int i = 0; i < rest.Length; i++)
                {
                    rest[i] = new JVec3((float)r * 0.4f, -(float)i * 0.1f, 0.0f);
                }
                h.AddChain(rest, ps, 1.0f);
            }
            h.Cfg->CouplingPasses = coupled ? 3u : 1u;
            return h;
        }

        private static void Pose(JiggleHost h, int frame)
        {
            JVec3* wp = stackalloc JVec3[10];
            for (int r = 0; r < h.RigCount; r++)
            {
                JVec3 sw = new JVec3(
                    (float)Math.Sin(frame * 0.4 + r) * 0.06f, 0.0f, (float)Math.Cos(frame * 0.3 + r * 0.2) * 0.04f);
                for (int i = 0; i <= 8; i++)
                {
                    wp[i] = new JVec3((float)r * 0.4f, -(float)i * 0.1f, 0.0f) + sw;
                }
                int off = (int)h.Rigs[r].ParticleOffset;
                JigglePose.Write(h.Rigs + r, h.PosePrev + off, h.PoseCurr + off, h.PoseRot + off, h.TipScale[r], wp, null, 0);
            }
        }

        private static float MaxDifference(JiggleHost a, JiggleHost b)
        {
            float worst = 0.0f;
            for (int i = 0; i < a.ParticleCount; i++)
            {
                JiggleParticleOut x = a.Out[i];
                JiggleParticleOut y = b.Out[i];
                worst = Math.Max(worst, Math.Abs(x.Pos.X - y.Pos.X));
                worst = Math.Max(worst, Math.Abs(x.Pos.Y - y.Pos.Y));
                worst = Math.Max(worst, Math.Abs(x.Pos.Z - y.Pos.Z));
            }
            return worst;
        }

        [Test]
        public void BurstSolverMatchesTheManagedKernelWithinTolerance()
        {
            foreach (bool coupled in new[] { false, true })
            {
                using (JiggleHost managed = MakeHost(40, coupled))
                using (JiggleHost burst = MakeHost(40, coupled))
                {
                    float worst = 0.0f;
                    for (int f = 0; f < 120; f++)
                    {
                        Pose(managed, f);
                        Pose(burst, f);
                        Assert.AreEqual(0, managed.StepFixed(1, JiggleBackend.Managed));
                        burst.BeginFixed(1);
                        Assert.AreEqual(0, JiggleBurstSolver.RunNow(burst, 8), "the Burst solver refused the call");
                        burst.EndFrame();
                        worst = Math.Max(worst, MaxDifference(managed, burst));
                    }
                    Assert.Less(worst, 1e-4f, "Burst and managed differ by " + worst + " meters, coupled " + coupled);
                }
            }
        }

        [Test]
        public void BurstStepThenResolveEqualsTheOneJobRun()
        {
            using (JiggleHost both = MakeHost(25, true))
            using (JiggleHost split = MakeHost(25, true))
            {
                for (int f = 0; f < 40; f++)
                {
                    Pose(both, f);
                    Pose(split, f);
                    both.BeginFixed(1);
                    Assert.AreEqual(0, JiggleBurstSolver.RunNow(both, 4));
                    split.BeginFixed(1);
                    Assert.AreEqual(0, JiggleBurstSolver.RunNow(split, 4, JiggleBurstSolver.PhaseStep));
                    Assert.AreEqual(0, JiggleBurstSolver.RunNow(split, 4, JiggleBurstSolver.PhaseResolve));
                    both.EndFrame();
                    split.EndFrame();
                }
                Assert.AreEqual(0.0f, MaxDifference(both, split), "the same code in two jobs gives the same result");
            }
        }

        [Test]
        public void NewSettingsBakeToTheCoreAndDefaultToTheCoreDefaults()
        {
            JiggleChainSettings s = new JiggleChainSettings();
            JiggleParams d = JiggleParams.Default();
            JiggleParams p = s.ToParams(0, 3);
            Assert.AreEqual(d.Collide, p.Collide);
            Assert.AreEqual(d.SegmentCollision, p.SegmentCollision);
            Assert.AreEqual(d.CollisionRadius, p.CollisionRadius, 1e-6f);
            Assert.AreEqual(d.WindResponse, p.WindResponse, 1e-6f);
            Assert.AreEqual(d.ImpulseResponse, p.ImpulseResponse, 1e-6f);

            s.collide = true;
            s.segmentCollision = true;
            s.collisionRadius = 0.2f;
            s.windResponse = 0.5f;
            s.impulseResponse = 2.0f;
            JiggleParams[] all = s.ToParams(3);
            for (int i = 0; i < all.Length; i++)
            {
                Assert.IsTrue(all[i].Collide);
                Assert.IsTrue(all[i].SegmentCollision);
                Assert.AreEqual(0.2f, all[i].CollisionRadius, 1e-6f);
                Assert.AreEqual(0.5f, all[i].WindResponse, 1e-6f);
                Assert.AreEqual(2.0f, all[i].ImpulseResponse, 1e-6f);
            }
            JiggleParticleDef def = all[0].Bake(0, 0u);
            Assert.AreNotEqual(0u, def.Flags & JiggleConst.FlagCollide, "the collide setting reaches the baked flag");
            Assert.AreNotEqual(0u, def.Flags & JiggleConst.FlagSegment);
        }

        [Test]
        public void ChainLodModesResolveToTheLimitsTheyPromise()
        {
            JiggleLodSettings manager = new JiggleLodSettings();
            manager.CollisionDistance = 10f;
            manager.CullDistance = 30f;
            manager.CullOffscreen = true;
            manager.OffscreenDelay = 1f;
            manager.Hysteresis = 3f;

            JiggleChainLod lod = new JiggleChainLod();
            JiggleLodSettings inherited = lod.Resolve(manager, 3f);
            Assert.AreEqual(10f, inherited.CollisionDistance, 0f);
            Assert.AreEqual(30f, inherited.CullDistance, 0f);
            Assert.IsTrue(inherited.CullOffscreen);

            lod.mode = JiggleLodMode.AlwaysSimulate;
            JiggleLodSettings always = lod.Resolve(manager, 3f);
            Assert.AreEqual(0f, always.CollisionDistance, 0f);
            Assert.AreEqual(0f, always.CullDistance, 0f);
            Assert.IsFalse(always.CullOffscreen);

            lod.mode = JiggleLodMode.Custom;
            lod.collisionDistance = 4f;
            lod.cullDistance = 8f;
            lod.cullOffscreen = false;
            lod.offscreenDelay = 0.25f;
            JiggleLodSettings own = lod.Resolve(manager, 3f);
            Assert.AreEqual(4f, own.CollisionDistance, 0f);
            Assert.AreEqual(8f, own.CullDistance, 0f);
            Assert.IsFalse(own.CullOffscreen);
            Assert.AreEqual(0.25f, own.OffscreenDelay, 0f);
            Assert.AreEqual(3f, own.Hysteresis, 0f, "a custom chain takes the hysteresis of the manager");
        }

        [Test]
        public void ColliderShapeBuildsTheSolverShapeFromItsTransform()
        {
            GameObject go = new GameObject("jiggle collider test");
            try
            {
                JiggleColliderShape s = go.AddComponent<JiggleColliderShape>();
                go.transform.position = new Vector3(1f, 2f, 3f);
                go.transform.localScale = new Vector3(2f, 2f, 2f);
                s.shape = JiggleShapeKind.Sphere;
                s.radius = 0.25f;
                JiggleCollider sphere = s.Peek();
                Assert.AreEqual(JiggleConst.ColliderSphere, sphere.Kind);
                Assert.AreEqual(0.5f, sphere.Radius, 1e-5f, "the radius follows the scale");
                Assert.AreEqual(1f, sphere.Center.X, 1e-5f);
                Assert.AreEqual(2f, sphere.Center.Y, 1e-5f);
                Assert.AreEqual(3f, sphere.Center.Z, 1e-5f);

                go.transform.localScale = new Vector3(1f, 1f, 1f);
                s.shape = JiggleShapeKind.Capsule;
                s.axis = JiggleShapeAxis.Y;
                s.height = 1f;
                s.radius = 0.1f;
                JiggleCollider capsule = s.Peek();
                Assert.AreEqual(JiggleConst.ColliderCapsule, capsule.Kind);
                Assert.AreEqual(0.4f, capsule.HalfHeight, 1e-5f, "half of the height minus the cap");
                Assert.AreEqual(0.1f, capsule.Radius, 1e-5f);
                Assert.AreEqual(1f, capsule.Axis.Y, 1e-5f);

                s.shape = JiggleShapeKind.Box;
                s.size = new Vector3(1f, 2f, 3f);
                s.radius = 0f;
                JiggleCollider box = s.Peek();
                Assert.AreEqual(JiggleConst.ColliderBox, box.Kind);
                Assert.AreEqual(0.5f, box.Extents.X, 1e-5f);
                Assert.AreEqual(1f, box.Extents.Y, 1e-5f);
                Assert.AreEqual(1.5f, box.Extents.Z, 1e-5f);

                s.shape = JiggleShapeKind.Plane;
                JiggleCollider plane = s.Peek();
                Assert.AreEqual(JiggleConst.ColliderPlane, plane.Kind);
                Assert.AreEqual(1f, plane.Axis.Y, 1e-5f, "the normal is the up direction");

                s.shape = JiggleShapeKind.InsideSphere;
                s.radius = 3f;
                Assert.AreEqual(JiggleConst.ColliderInsideSphere, s.Peek().Kind);
                Assert.AreEqual(3f, s.Peek().Radius, 1e-5f);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void ColliderShapeReportsMovementOnlyWhenItMovesAndStaticShapesAreBuiltOnce()
        {
            GameObject go = new GameObject("jiggle collider move test");
            try
            {
                JiggleColliderShape s = go.AddComponent<JiggleColliderShape>();
                bool moved;
                s.Build(out moved);
                Assert.IsTrue(moved, "the first build counts as moved");
                s.Build(out moved);
                Assert.IsFalse(moved, "nothing changed");
                go.transform.position = new Vector3(0f, 1f, 0f);
                s.Build(out moved);
                Assert.IsTrue(moved, "the shape moved");
                s.Peek();
                s.Build(out moved);
                Assert.IsFalse(moved, "looking at the shape does not change what counts as moved");

                s.staticShape = true;
                s.Build(out moved);
                go.transform.position = new Vector3(5f, 5f, 5f);
                JiggleCollider cached = s.Build(out moved);
                Assert.IsFalse(moved, "a static shape is not watched");
                Assert.AreEqual(1f, cached.Center.Y, 1e-5f, "and it keeps the shape it was built with");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void ChainThatIsNotSimulatedRefusesHitsAndAnswersQuietly()
        {
            GameObject go = new GameObject("jiggle chain test");
            try
            {
                JiggleChain c = go.AddComponent<JiggleChain>();
                Assert.IsFalse(c.IsSimulating);
                Assert.IsFalse(c.IsAsleep);
                Assert.AreEqual(JiggleLodLevel.Full, c.LodLevel);
                Assert.IsFalse(c.AddImpulse(new Vector3(1f, 0f, 0f)));
                Assert.IsFalse(c.AddImpulseAt(Vector3.zero, new Vector3(1f, 0f, 0f), 1f));
                c.Wake();
                Assert.IsNotNull(c.lod);
                Assert.IsTrue(c.allowSleep, "chains may sleep unless told not to");
                Assert.AreEqual(0, c.ignoreLayers.value, "a chain collides with every layer by default");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void BurstProbeAnswersWithoutThrowing()
        {
            bool compiled = JiggleBurstSolver.IsBurstCompiled();
            Debug.Log("Burst compiles the jiggle jobs: " + compiled);
            using (JiggleHost empty = MakeHost(0, false))
            {
                Assert.AreEqual(0, JiggleBurstSolver.RunNow(empty, 8), "no rigs is not an error");
            }
        }
    }
}
