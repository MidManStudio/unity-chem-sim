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
    /// the managed one. The transform jobs need a scene and are tried in the lab (Tools, MidMan Studio, Jiggle Lab).
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
