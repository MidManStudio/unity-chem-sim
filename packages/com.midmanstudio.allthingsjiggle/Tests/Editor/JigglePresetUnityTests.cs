// ============================================================================
// NOTICE: Full documentation, design decisions, and fix history for this file
// live in docs/com.midmanstudio.allthingsjiggle.md, section "Tests/Editor/JigglePresetUnityTests.cs"
// ============================================================================
using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace MidManStudio.AllThingsJiggle.EditorTests
{
    /// <summary>
    /// The Unity layer of drop 3 in Edit Mode: that applying a preset to a chain bakes the same parameters as the Core does, and
    /// that body colliders are built from joints the way the fit says. It needs Unity, so the dotnet harness does not run it. The
    /// stub build compiles it. The maths is tested in JigglePresetTests and JiggleBodyFitTests.
    /// </summary>
    public sealed class JigglePresetUnityTests
    {
        private static readonly float[] Rates = { 30f, 60f, 120f };
        private static readonly int[] Lengths = { 1, 2, 6 };

        // ------------------------------------------------------------------ presets

        [Test]
        public void ApplyingAPresetBakesTheParametersOfTheCore()
        {
            for (int k = 0; k < JigglePresets.Count; k++)
            {
                JigglePresetKind kind = (JigglePresetKind)k;
                JigglePresetValues v = JigglePresets.Get(kind);
                for (int r = 0; r < Rates.Length; r++)
                {
                    JiggleChainSettings s = new JiggleChainSettings();
                    s.ApplyPreset(kind, Rates[r]);
                    for (int l = 0; l < Lengths.Length; l++)
                    {
                        int n = Lengths[l];
                        for (int i = 0; i < n; i++)
                        {
                            JiggleParams a = s.ToParams(i, n);
                            JiggleParams b = JigglePresets.ToParams(v, i, n, Rates[r]);
                            string at = kind + " at " + Rates[r] + " Hz, bone " + i + " of " + n;
                            Assert.AreEqual(b.Stiffness, a.Stiffness, 1e-4, "stiffness, " + at);
                            Assert.AreEqual(b.Soften, a.Soften, 1e-6, "soften, " + at);
                            Assert.AreEqual(b.RootStretch, a.RootStretch, 1e-6, "root stretch, " + at);
                            Assert.AreEqual(b.AngleLimited, a.AngleLimited, "angle limited, " + at);
                            Assert.AreEqual(b.AngleLimit, a.AngleLimit, 1e-6, "angle limit, " + at);
                            Assert.AreEqual(b.AngleLimitSoften, a.AngleLimitSoften, 1e-6, "angle limit soften, " + at);
                            Assert.AreEqual(b.Stretch, a.Stretch, 1e-6, "stretch, " + at);
                            Assert.AreEqual(b.Drag, a.Drag, 1e-6, "drag, " + at);
                            Assert.AreEqual(b.AirDrag, a.AirDrag, 1e-6, "air drag, " + at);
                            Assert.AreEqual(b.Gravity, a.Gravity, 1e-6, "gravity, " + at);
                            Assert.AreEqual(b.Blend, a.Blend, 1e-6, "blend, " + at);
                            Assert.AreEqual(b.IgnoreRootMotion, a.IgnoreRootMotion, 1e-6, "ignore root motion, " + at);
                            Assert.AreEqual(b.Mass, a.Mass, 1e-6, "mass, " + at);
                            Assert.AreEqual(b.Collide, a.Collide, "collide, " + at);
                            Assert.AreEqual(b.CollisionRadius, a.CollisionRadius, 1e-6, "collision radius, " + at);
                            Assert.AreEqual(b.SegmentCollision, a.SegmentCollision, "segment collision, " + at);
                            Assert.AreEqual(b.WindResponse, a.WindResponse, 1e-6, "wind response, " + at);
                            Assert.AreEqual(b.ImpulseResponse, a.ImpulseResponse, 1e-6, "impulse response, " + at);
                        }
                    }
                }
            }
        }

        [Test]
        public void AChainTakesTheTipScaleOfAPresetAndKeepsEverythingElse()
        {
            GameObject go = new GameObject("jiggle preset chain test");
            JigglePreset asset = null;
            try
            {
                JiggleChain c = go.AddComponent<JiggleChain>();
                c.maxBones = 7;
                c.allowSleep = false;
                c.ignoreLayers = 4;
                c.tipScale = 3f;

                c.ApplyPreset(JigglePresetKind.Horn);
                Assert.AreEqual(1.0, c.tipScale, 1e-6, "the horn preset has a tip scale of 1");
                Assert.AreEqual(JigglePresets.Get(JigglePresetKind.Horn).AngleLimit, c.settings.angleLimit, 1e-6);
                Assert.AreEqual(7, c.maxBones, "the bones the chain takes are not part of a preset");
                Assert.IsFalse(c.allowSleep, "sleeping is not part of a preset");
                Assert.AreEqual(4, c.ignoreLayers.value, "the layers are not part of a preset");

                asset = JigglePreset.Create(JigglePresetKind.Wing);
                asset.values.TipScale = 2f;
                c.ApplyPreset(asset);
                Assert.AreEqual(2.0, c.tipScale, 1e-6);
                Assert.AreEqual(JigglePresets.Get(JigglePresetKind.Wing).WindResponse, c.settings.windResponse, 1e-6);

                c.ApplyPreset((JigglePreset)null);
                Assert.AreEqual(2.0, c.tipScale, 1e-6, "a null asset does nothing");

                JigglePresetValues bad = JigglePresets.Get(JigglePresetKind.Ear);
                bad.TipScale = float.NaN;
                c.ApplyPreset(bad);
                Assert.AreEqual(1.0, c.tipScale, 1e-6, "a tip scale that is not a number counts as 1");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
                if (asset != null)
                {
                    UnityEngine.Object.DestroyImmediate(asset);
                }
            }
        }

        [Test]
        public void APresetAssetCarriesTheValuesOfItsKind()
        {
            for (int k = 0; k < JigglePresets.Count; k++)
            {
                JigglePresetKind kind = (JigglePresetKind)k;
                JigglePreset p = JigglePreset.Create(kind);
                try
                {
                    JigglePresetValues v = JigglePresets.Get(kind);
                    Assert.AreEqual(JigglePresets.Name(kind), p.name);
                    Assert.AreEqual(v.Stiffness, p.values.Stiffness, 0.0);
                    Assert.AreEqual(v.DampingPerSecond, p.values.DampingPerSecond, 0.0);
                    Assert.AreEqual(v.TipScale, p.values.TipScale, 0.0);
                    Assert.AreEqual(v.Collide, p.values.Collide);
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(p);
                }
            }
        }

        // ------------------------------------------------------------------ body colliders

        private static Transform Joint(string name, Transform parent, float x, float y, float z)
        {
            GameObject g = new GameObject(name);
            g.transform.SetParent(parent, false);
            g.transform.position = new Vector3(x, y, z);
            return g.transform;
        }

        // A standing figure in a T pose, 1.7 m tall, facing +Z, under a root with the given uniform scale. The joints sit at the same
        // world positions whatever the scale.
        private static JiggleBodyJoints Standing(Transform root)
        {
            JiggleBodyJoints j = new JiggleBodyJoints();
            Transform hips = Joint("Hips", root, 0f, 0.95f, 0f);
            Transform neck = Joint("Neck", hips, 0f, 1.48f, 0f);
            j.hips = hips;
            j.neck = neck;
            j.head = Joint("Head", neck, 0f, 1.55f, 0f);
            Transform lua = Joint("LUpperArm", hips, -0.18f, 1.42f, 0f);
            Transform lla = Joint("LLowerArm", lua, -0.46f, 1.42f, 0f);
            j.leftUpperArm = lua;
            j.leftLowerArm = lla;
            j.leftHand = Joint("LHand", lla, -0.72f, 1.42f, 0f);
            Transform rua = Joint("RUpperArm", hips, 0.18f, 1.42f, 0f);
            Transform rla = Joint("RLowerArm", rua, 0.46f, 1.42f, 0f);
            j.rightUpperArm = rua;
            j.rightLowerArm = rla;
            j.rightHand = Joint("RHand", rla, 0.72f, 1.42f, 0f);
            Transform lul = Joint("LUpperLeg", hips, -0.09f, 0.92f, 0f);
            Transform lll = Joint("LLowerLeg", lul, -0.09f, 0.5f, 0f);
            Transform lf = Joint("LFoot", lll, -0.09f, 0.08f, 0f);
            j.leftUpperLeg = lul;
            j.leftLowerLeg = lll;
            j.leftFoot = lf;
            j.leftToes = Joint("LToes", lf, -0.09f, 0.04f, 0.12f);
            Transform rul = Joint("RUpperLeg", hips, 0.09f, 0.92f, 0f);
            Transform rll = Joint("RLowerLeg", rul, 0.09f, 0.5f, 0f);
            Transform rf = Joint("RFoot", rll, 0.09f, 0.08f, 0f);
            j.rightUpperLeg = rul;
            j.rightLowerLeg = rll;
            j.rightFoot = rf;
            j.rightToes = Joint("RToes", rf, 0.09f, 0.04f, 0.12f);
            return j;
        }

        private static JiggleBodyColliders MakeBody(GameObject root, JiggleBodyJoints joints)
        {
            JiggleBodyColliders b = root.AddComponent<JiggleBodyColliders>();
            b.joints = joints;
            return b;
        }

        private static JiggleColliderShape Find(List<JiggleColliderShape> built, string label)
        {
            for (int i = 0; i < built.Count; i++)
            {
                if (built[i].gameObject.name == JiggleBodyColliders.NamePrefix + label)
                {
                    return built[i];
                }
            }
            return null;
        }

        [Test]
        public void BodyCollidersAreBuiltFromTheJointsAsTheFitSaysAtAnyUniformScale()
        {
            float[] scales = { 1f, 2f, 0.5f };
            for (int s = 0; s < scales.Length; s++)
            {
                GameObject go = new GameObject("jiggle body test");
                try
                {
                    go.transform.localScale = new Vector3(scales[s], scales[s], scales[s]);
                    JiggleBodyColliders body = MakeBody(go, Standing(go.transform));
                    List<JiggleBodyPlan> plan = new List<JiggleBodyPlan>();
                    string problem;
                    Assert.IsTrue(body.Plan(plan, out problem), "scale " + scales[s] + ": " + problem);
                    Assert.AreEqual(15, plan.Count, "a head, a neck, a trunk, three parts of each arm and of each leg");
                    Assert.AreEqual(15, body.Build());
                    Assert.AreEqual(15, body.BuiltCount);

                    List<JiggleColliderShape> built = new List<JiggleColliderShape>();
                    body.GetBuilt(built);
                    Assert.AreEqual(15, built.Count);
                    for (int i = 0; i < plan.Count; i++)
                    {
                        JiggleColliderShape shape = Find(built, plan[i].Label);
                        Assert.IsNotNull(shape, "scale " + scales[s] + ": no collider for " + plan[i].Label);
                        JiggleCollider want = plan[i].Shape.ToCollider();
                        JiggleCollider got = shape.Peek();
                        string at = "scale " + scales[s] + ", " + plan[i].Label;
                        Assert.AreEqual(want.Kind, got.Kind, at);
                        Assert.AreEqual(want.Radius, got.Radius, 1e-4, "radius, " + at);
                        Assert.AreEqual(want.HalfHeight, got.HalfHeight, 1e-4, "half height, " + at);
                        Assert.AreEqual(want.Center.X, got.Center.X, 1e-4, "center x, " + at);
                        Assert.AreEqual(want.Center.Y, got.Center.Y, 1e-4, "center y, " + at);
                        Assert.AreEqual(want.Center.Z, got.Center.Z, 1e-4, "center z, " + at);
                        if (want.Kind == JiggleConst.ColliderCapsule)
                        {
                            // The capsule is the same either way round, so the axis counts up to its sign.
                            float dot = want.Axis.X * got.Axis.X + want.Axis.Y * got.Axis.Y + want.Axis.Z * got.Axis.Z;
                            Assert.Greater(Math.Abs(dot), 0.9999, "axis, " + at);
                        }
                    }
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(go);
                }
            }
        }

        [Test]
        public void TheLayerIsAppliedAndClearRemovesTheColliders()
        {
            GameObject go = new GameObject("jiggle body test");
            try
            {
                JiggleBodyColliders body = MakeBody(go, Standing(go.transform));
                body.colliderLayer = 5;
                Assert.AreEqual(15, body.Build());
                List<JiggleColliderShape> built = new List<JiggleColliderShape>();
                body.GetBuilt(built);
                for (int i = 0; i < built.Count; i++)
                {
                    Assert.AreEqual(5, built[i].gameObject.layer, built[i].gameObject.name);
                    Assert.IsFalse(built[i].staticShape, "body colliders move with the animation");
                }
                Assert.AreEqual(15, body.Build(), "a second build replaces the first and does not add to it");
                Assert.AreEqual(15, body.BuiltCount);
                body.Clear();
                Assert.AreEqual(0, body.BuiltCount);
                body.GetBuilt(built);
                Assert.AreEqual(0, built.Count);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void ThePartMaskLimitsWhatIsBuilt()
        {
            GameObject go = new GameObject("jiggle body test");
            try
            {
                JiggleBodyColliders body = MakeBody(go, Standing(go.transform));
                body.parts = JiggleBodyPartMask.Head | JiggleBodyPartMask.Hands;
                List<JiggleBodyPlan> plan = new List<JiggleBodyPlan>();
                string problem;
                Assert.IsTrue(body.Plan(plan, out problem), problem);
                Assert.AreEqual(3, plan.Count, "the head and two hands");
                for (int i = 0; i < plan.Count; i++)
                {
                    Assert.IsTrue(plan[i].Part == JiggleBodyPart.Head || plan[i].Part == JiggleBodyPart.Hand, "part " + plan[i].Part);
                    Assert.AreEqual((int)JiggleBodyShapeKind.Sphere, (int)plan[i].Shape.Kind);
                }
                body.parts = JiggleBodyPartMask.None;
                Assert.IsFalse(body.Plan(plan, out problem), "no part selected");
                Assert.IsNotNull(problem);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void TheHeightComesFromTheAnklesThenTheTorsoAndTheOverrideWins()
        {
            GameObject go = new GameObject("jiggle body test");
            try
            {
                JiggleBodyJoints j = Standing(go.transform);
                JiggleBodyColliders body = MakeBody(go, j);
                Assert.AreEqual(1.764, body.ResolveHeight(), 1e-4, "the head joint to the middle of the ankles, a fifth added");

                body.height = 2f;
                Assert.AreEqual(2.0, body.ResolveHeight(), 1e-6, "a height that is given wins");
                body.height = 0f;

                j.leftFoot = null;
                j.rightFoot = null;
                Assert.AreEqual(1.68, body.ResolveHeight(), 1e-4, "no feet: from the hips to the head joint");

                j.hips = null;
                Assert.AreEqual(0.0, body.ResolveHeight(), 0.0, "no feet and no hips: no height");
                j.head = null;
                Assert.AreEqual(0.0, body.ResolveHeight(), 0.0);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void NoJointsMeansNoBodyAndNothingIsMade()
        {
            GameObject go = new GameObject("jiggle body test");
            try
            {
                JiggleBodyColliders body = go.AddComponent<JiggleBodyColliders>();
                List<JiggleBodyPlan> plan = new List<JiggleBodyPlan>();
                string problem;
                Assert.IsFalse(body.Plan(plan, out problem));
                Assert.IsNotNull(problem);
                Assert.AreEqual(0, plan.Count);
                Assert.AreEqual(0, body.AutoAssign(), "no animator, nothing to fill from");
                Assert.AreEqual(0, body.Build());
                Assert.AreEqual(0, body.BuiltCount);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
            }
        }
    }
}
