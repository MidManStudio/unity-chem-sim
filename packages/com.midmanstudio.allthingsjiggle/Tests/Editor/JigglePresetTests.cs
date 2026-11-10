// ============================================================================
// NOTICE: Full documentation, design decisions, and fix history for this file
// live in docs/com.midmanstudio.allthingsjiggle.md, section "Tests/Editor/JigglePresetTests.cs"
// ============================================================================
using System;
using NUnit.Framework;

namespace MidManStudio.AllThingsJiggle.EditorTests
{
    /// <summary>
    /// The presets and the damping conversion of drop 3: the numbers they promise, their ranges, and that every preset runs
    /// stably in the solver with the same bits on both backends. The file runs under plain .NET in CI (see
    /// Tests~/DotnetHarness), so it must stay free of UnityEngine.
    /// </summary>
    [TestFixture]
    public class JigglePresetTests
    {
        private static readonly JigglePresetKind[] Kinds =
        {
            JigglePresetKind.Hair, JigglePresetKind.Tail, JigglePresetKind.Ear,
            JigglePresetKind.Chest, JigglePresetKind.Horn, JigglePresetKind.Wing,
        };

        private const int Bones = 6;
        private const float Spacing = 0.1f;

        private static int Bits(float v)
        {
            return BitConverter.SingleToInt32Bits(v);
        }

        /// <summary>The anchor first, then the bones hanging straight down from (0, 1.5, 0).</summary>
        private static JVec3[] Rest()
        {
            JVec3[] rest = new JVec3[Bones + 1];
            for (int k = 0; k <= Bones; k++)
            {
                rest[k] = new JVec3(0.0f, 1.5f - Spacing * (float)k, 0.0f);
            }
            return rest;
        }

        private static JVec3[] Shifted(JVec3[] rest, JVec3 by)
        {
            JVec3[] moved = new JVec3[rest.Length];
            for (int i = 0; i < rest.Length; i++)
            {
                moved[i] = rest[i] + by;
            }
            return moved;
        }

        private static JiggleWorld NewWorld(JigglePresetKind kind, float hz)
        {
            JigglePresetValues v = JigglePresets.Get(kind);
            JiggleWorld w = new JiggleWorld();
            w.AddChain(Rest(), JigglePresets.ToParams(v, Bones, hz), JigglePresets.CleanTipScale(v));
            return w;
        }

        // The anchor sways sideways for two seconds and then stands still for six more. Returns the largest distance of the
        // tip from where the animation puts it, how far the tip moved over the last 20 frames (it has settled when that is
        // near 0), and the distance from the animation at the end, which is the sag of the chain under gravity.
        private static void Sway(JiggleWorld w, JiggleBackend backend, out float peak, out float drift, out float sag)
        {
            JVec3[] rest = Rest();
            peak = 0.0f;
            drift = 0.0f;
            sag = 0.0f;
            JVec3 before = JVec3.Zero;
            for (int f = 0; f < 480; f++)
            {
                float x = f < 120 ? (float)Math.Sin((double)f * 0.12) * 0.15f : 0.0f;
                JVec3[] pose = Shifted(rest, new JVec3(x, 0.0f, 0.0f));
                w.SetPose(0, pose);
                Assert.AreEqual(JiggleConst.Ok, w.StepFixed(1, backend), "frame " + f);
                JVec3 tip = w.WorldPos(0, Bones);
                float d = (tip - pose[Bones]).Length();
                if (d > peak)
                {
                    peak = d;
                }
                if (f == 459)
                {
                    before = tip;
                }
                if (f == 479)
                {
                    drift = (tip - before).Length();
                    sag = d;
                }
            }
        }

        // ------------------------------------------------------------------ damping

        [Test]
        public void DampingPerSecondBecomesAShareLostPerStepAndBack()
        {
            Assert.AreEqual(0.1087, JiggleDamping.PerStep(0.999f, 60.0f), 5e-4, "the 60 Hz value of the rig study");
            Assert.AreEqual(0.2056, JiggleDamping.PerStep(0.999f, 30.0f), 5e-4, "the 30 Hz value of the rig study");

            // Whatever the step rate, the same share is lost in a second.
            double at60 = Math.Pow(1.0 - (double)JiggleDamping.PerStep(0.995f, 60.0f), 60.0);
            double at30 = Math.Pow(1.0 - (double)JiggleDamping.PerStep(0.995f, 30.0f), 30.0);
            double at144 = Math.Pow(1.0 - (double)JiggleDamping.PerStep(0.995f, 144.0f), 144.0);
            Assert.AreEqual(0.005, at60, 1e-5);
            Assert.AreEqual(0.005, at30, 1e-5);
            Assert.AreEqual(0.005, at144, 1e-5);

            float[] seconds = { 0.0f, 0.1f, 0.5f, 0.9f, 0.999f };
            for (int i = 0; i < seconds.Length; i++)
            {
                float back = JiggleDamping.PerSecond(JiggleDamping.PerStep(seconds[i], 60.0f), 60.0f);
                Assert.AreEqual(seconds[i], back, 2e-4, "round trip of " + seconds[i]);
            }
        }

        [Test]
        public void DampingEdgesAreClampedAndABadStepRateCountsAsSixty()
        {
            Assert.AreEqual(0.0, JiggleDamping.PerStep(0.0f, 60.0f), 0.0);
            Assert.AreEqual(1.0, JiggleDamping.PerStep(1.0f, 60.0f), 0.0);
            Assert.AreEqual(0.0, JiggleDamping.PerStep(-3.0f, 60.0f), 0.0, "below 0");
            Assert.AreEqual(1.0, JiggleDamping.PerStep(7.0f, 60.0f), 0.0, "above 1");
            Assert.AreEqual(0.0, JiggleDamping.PerStep(float.NaN, 60.0f), 0.0, "not a number");
            float reference = JiggleDamping.PerStep(0.9f, 60.0f);
            Assert.AreEqual(reference, JiggleDamping.PerStep(0.9f, 0.0f), 0.0, "zero Hz");
            Assert.AreEqual(reference, JiggleDamping.PerStep(0.9f, -5.0f), 0.0, "negative Hz");
            Assert.AreEqual(reference, JiggleDamping.PerStep(0.9f, float.NaN), 0.0, "NaN Hz");
            Assert.AreEqual(reference, JiggleDamping.PerStep(0.9f, float.PositiveInfinity), 0.0, "infinite Hz");
            Assert.AreEqual(0.0, JiggleDamping.PerSecond(float.NaN, 60.0f), 0.0);
            Assert.AreEqual(1.0, JiggleDamping.PerSecond(1.0f, 60.0f), 0.0);
        }

        // ------------------------------------------------------------------ the presets

        [Test]
        public void EveryPresetBakesIntoRangeAndHasAName()
        {
            string last = null;
            int[] sizes = { 1, 2, Bones, 40 };
            for (int k = 0; k < Kinds.Length; k++)
            {
                JigglePresetKind kind = Kinds[k];
                string name = JigglePresets.Name(kind);
                Assert.IsTrue(!string.IsNullOrEmpty(name), "name of " + kind);
                Assert.AreNotEqual(last, name, "names are different");
                last = name;
                JigglePresetValues v = JigglePresets.Get(kind);
                for (int s = 0; s < sizes.Length; s++)
                {
                    int n = sizes[s];
                    JiggleParams[] ps = JigglePresets.ToParams(v, n, 60.0f);
                    Assert.AreEqual(n, ps.Length);
                    for (int i = 0; i < n; i++)
                    {
                        JiggleParams p = ps[i];
                        string at = kind + " bone " + i + " of " + n;
                        Assert.IsTrue(p.Stiffness >= 0.0f && p.Stiffness <= 1.0f, at + " stiffness " + p.Stiffness);
                        Assert.IsTrue(p.Drag >= 0.0f && p.Drag <= 1.0f, at + " drag " + p.Drag);
                        Assert.IsTrue(p.AirDrag >= 0.0f && p.AirDrag <= 1.0f, at + " air drag " + p.AirDrag);
                        Assert.IsTrue(p.AngleLimit >= 0.0f && p.AngleLimit <= 1.0f, at + " angle limit");
                        Assert.IsTrue(p.Stretch >= 0.0f && p.Stretch <= 1.0f, at + " stretch");
                        Assert.IsTrue(p.Mass >= 0.0f && p.CollisionRadius >= 0.0f, at + " mass and radius");
                        Assert.IsTrue(p.WindResponse >= 0.0f && p.ImpulseResponse >= 0.0f, at + " responses");
                        Assert.IsTrue(JiggleMath.IsFinite(p.Gravity), at + " gravity");
                    }
                }
                Assert.IsTrue(JigglePresets.CleanTipScale(v) > 0.0f, "tip scale of " + kind);
            }

            // A kind that does not exist is the hair preset, and a tip scale that is not a positive number counts as 1.
            JigglePresetValues hair = JigglePresets.Get(JigglePresetKind.Hair);
            JigglePresetValues unknown = JigglePresets.Get((JigglePresetKind)99);
            Assert.AreEqual(hair.Stiffness, unknown.Stiffness, 0.0);
            Assert.AreEqual(hair.AngleLimit, unknown.AngleLimit, 0.0);
            Assert.AreEqual("Hair", JigglePresets.Name((JigglePresetKind)99));
            JigglePresetValues bad = hair;
            bad.TipScale = float.NaN;
            Assert.AreEqual(1.0, JigglePresets.CleanTipScale(bad), 0.0);
            bad.TipScale = -2.0f;
            Assert.AreEqual(1.0, JigglePresets.CleanTipScale(bad), 0.0);
            Assert.AreEqual(JigglePresets.Count, Kinds.Length);
        }

        [Test]
        public void StiffnessRunsFromTheFirstBoneToTheTipByTheTipFactor()
        {
            JigglePresetValues v = JigglePresets.Get(JigglePresetKind.Tail);
            JiggleParams first = JigglePresets.ToParams(v, 0, 5, 60.0f);
            JiggleParams mid = JigglePresets.ToParams(v, 2, 5, 60.0f);
            JiggleParams tip = JigglePresets.ToParams(v, 4, 5, 60.0f);
            Assert.AreEqual(v.Stiffness, first.Stiffness, 1e-6);
            Assert.AreEqual(v.Stiffness * (1.0f + (v.StiffnessAtTip - 1.0f) * 0.5f), mid.Stiffness, 1e-6);
            Assert.AreEqual(v.Stiffness * v.StiffnessAtTip, tip.Stiffness, 1e-6);
            // One bone is at the start of the line.
            Assert.AreEqual(v.Stiffness, JigglePresets.ToParams(v, 0, 1, 60.0f).Stiffness, 1e-6);
        }

        [Test]
        public void PresetsKeepWhatTheDocumentationPromises()
        {
            JigglePresetValues hair = JigglePresets.Get(JigglePresetKind.Hair);
            Assert.IsTrue(hair.AngleLimited, "hair has a swing limit");
            Assert.AreEqual(20.0, hair.AngleLimit * 90.0, 3.0, "about 20 degrees, from the rig study");
            Assert.AreEqual(0.999, hair.DampingPerSecond, 1e-6, "from the rig study");
            Assert.AreEqual(0.1087, JigglePresets.ToParams(hair, 0, Bones, 60.0f).Drag, 5e-4, "about 0.11 at 60 Hz");

            JigglePresetValues chest = JigglePresets.Get(JigglePresetKind.Chest);
            Assert.Less(chest.AngleLimit, hair.AngleLimit, "the chest swings less than hair");
            Assert.Greater(chest.Stiffness, hair.Stiffness, "and is pulled harder to its pose");

            for (int k = 0; k < Kinds.Length; k++)
            {
                JigglePresetKind kind = Kinds[k];
                JigglePresetValues v = JigglePresets.Get(kind);
                bool collides = kind == JigglePresetKind.Hair || kind == JigglePresetKind.Tail;
                bool coupled = kind == JigglePresetKind.Tail || kind == JigglePresetKind.Chest;
                Assert.AreEqual(collides, v.Collide, kind + " collision");
                Assert.AreEqual(coupled, v.Mass > 0.0f, kind + " coupling");
                if (kind != JigglePresetKind.Horn)
                {
                    Assert.Less(JigglePresets.Get(JigglePresetKind.Horn).AngleLimit, v.AngleLimit, "the horn swings least, against " + kind);
                    Assert.Greater(JigglePresets.Get(JigglePresetKind.Horn).Stiffness, v.Stiffness, "the horn is stiffest, against " + kind);
                }
            }
            Assert.Greater(JigglePresets.Get(JigglePresetKind.Wing).WindResponse, JigglePresets.Get(JigglePresetKind.Hair).WindResponse);
            Assert.Less(JigglePresets.Get(JigglePresetKind.Chest).WindResponse, 1.0f, "the chest takes little wind");
        }

        // ------------------------------------------------------------------ in the solver

        [Test]
        public void EveryPresetRunsStablyAndBothBackendsGiveTheSameBits()
        {
            JiggleBackend[] backends = { JiggleBackend.Managed, JiggleBackend.Native };
            for (int k = 0; k < Kinds.Length; k++)
            {
                JiggleWorld m = NewWorld(Kinds[k], 60.0f);
                JiggleWorld n = NewWorld(Kinds[k], 60.0f);
                JVec3[] rest = Rest();
                for (int f = 0; f < 240; f++)
                {
                    float x = f < 120 ? (float)Math.Sin((double)f * 0.12) * 0.15f : 0.0f;
                    JVec3[] pose = Shifted(rest, new JVec3(x, 0.0f, 0.0f));
                    m.SetPose(0, pose);
                    n.SetPose(0, pose);
                    Assert.AreEqual(JiggleConst.Ok, m.StepFixed(1, backends[0]));
                    Assert.AreEqual(JiggleConst.Ok, n.StepFixed(1, backends[1]));
                    for (int i = 0; i <= Bones; i++)
                    {
                        JVec3 a = m.WorldPos(0, i);
                        JVec3 b = n.WorldPos(0, i);
                        Assert.IsTrue(a.IsFinite(), Kinds[k] + " frame " + f + " bone " + i + " is not finite");
                        bool same = Bits(a.X) == Bits(b.X) && Bits(a.Y) == Bits(b.Y) && Bits(a.Z) == Bits(b.Z);
                        Assert.IsTrue(same, Kinds[k] + " frame " + f + " bone " + i + ": the backends differ");
                    }
                    JVec3 tip = m.WorldPos(0, Bones);
                    float away = (tip - pose[Bones]).Length();
                    Assert.Less(away, Spacing * (float)Bones, Kinds[k] + " frame " + f + ": the tip is " + away + " m from its pose");
                }
            }
        }

        [Test]
        public void EveryChainSettlesAndTheHornSwingsUnderHalfAsFarAsAnyOtherPreset()
        {
            float[] peak = new float[Kinds.Length];
            for (int k = 0; k < Kinds.Length; k++)
            {
                JiggleWorld w = NewWorld(Kinds[k], 60.0f);
                float p;
                float drift;
                float sag;
                Sway(w, JiggleBackend.Managed, out p, out drift, out sag);
                peak[k] = p;
                Assert.Less(drift, 0.002f, Kinds[k] + " has not settled, its tip still moved " + drift + " m in the last 20 frames");
                Assert.Less(sag, 0.2f, Kinds[k] + " sags " + sag + " m under gravity, which is a third of the chain");
            }
            // The horn is the stiffest, with the smallest swing limit, so it moves least. Only the horn is held to this: the order
            // of the others depends on numbers that have not been judged by eye, and a test that fixed it would break on every tuning.
            float horn = peak[(int)JigglePresetKind.Horn];
            for (int k = 0; k < Kinds.Length; k++)
            {
                if (Kinds[k] != JigglePresetKind.Horn)
                {
                    Assert.Less(horn * 2.0f, peak[k], "horn " + horn + " against " + Kinds[k] + " " + peak[k]);
                }
            }
        }

        [Test]
        public void TheSwingOfAOneWayPresetStaysAboutTheSameWhenTheStepRateChanges()
        {
            // The same preset at 30 and at 60 steps per second, given the same sway in real time, swings about as far. The two
            // frictions are per second in the preset and become per step for the rate. Stiffness is per step in the solver
            // and is not changed, and neither is the coupling, so a coupled preset (tail, chest) is only tuned at 60.
            JigglePresetKind[] kinds = { JigglePresetKind.Hair, JigglePresetKind.Wing, JigglePresetKind.Ear, JigglePresetKind.Horn };
            for (int k = 0; k < kinds.Length; k++)
            {
                JiggleWorld a = NewWorld(kinds[k], 60.0f);
                JiggleWorld b = NewWorld(kinds[k], 30.0f);
                b.FixedDt = 1.0f / 30.0f;
                float peak60;
                float drift60;
                float sag60;
                Sway(a, JiggleBackend.Managed, out peak60, out drift60, out sag60);

                JVec3[] rest = Rest();
                float peak30 = 0.0f;
                for (int f = 0; f < 240; f++)
                {
                    // A step of 1/30 s covers two of the 1/60 s frames above, so the same sway is sampled every second frame.
                    int sixty = f * 2;
                    float x = sixty < 120 ? (float)Math.Sin((double)sixty * 0.12) * 0.15f : 0.0f;
                    JVec3[] pose = Shifted(rest, new JVec3(x, 0.0f, 0.0f));
                    b.SetPose(0, pose);
                    Assert.AreEqual(JiggleConst.Ok, b.StepFixed(1, JiggleBackend.Managed));
                    float d = (b.WorldPos(0, Bones) - pose[Bones]).Length();
                    if (d > peak30)
                    {
                        peak30 = d;
                    }
                }
                string at = kinds[k] + ": peak " + peak30 + " at 30 Hz against " + peak60 + " at 60 Hz";
                Assert.IsTrue(JiggleMath.IsFinite(peak60) && JiggleMath.IsFinite(peak30), at);
                Assert.Less(Math.Abs(peak30 - peak60), 0.15f * peak60 + 0.01f, at);
            }
        }
    }
}
