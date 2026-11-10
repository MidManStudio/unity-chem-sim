// ============================================================================
// NOTICE: Full documentation, design decisions, and fix history for this file
// live in docs/com.midmanstudio.allthingsjiggle.md, section "Tests/Editor/JiggleBodyFitTests.cs"
// ============================================================================
using System;
using NUnit.Framework;

namespace MidManStudio.AllThingsJiggle.EditorTests
{
    /// <summary>
    /// The body fit of drop 3: the geometry of the shapes, what it refuses, the proportions, and that body colliders built
    /// from the joints of a standing figure keep a hair strand out of the body on both backends. The file runs under plain
    /// .NET in CI (see Tests~/DotnetHarness), so it must stay free of UnityEngine.
    /// </summary>
    [TestFixture]
    public class JiggleBodyFitTests
    {
        private static readonly JiggleBodyPart[] Parts =
        {
            JiggleBodyPart.Head, JiggleBodyPart.Neck, JiggleBodyPart.Torso, JiggleBodyPart.UpperArm, JiggleBodyPart.LowerArm,
            JiggleBodyPart.Hand, JiggleBodyPart.UpperLeg, JiggleBodyPart.LowerLeg, JiggleBodyPart.Foot,
        };

        private static int Bits(float v)
        {
            return BitConverter.SingleToInt32Bits(v);
        }

        // Distance from a point to the segment a b.
        private static float SegmentDistance(JVec3 p, JVec3 a, JVec3 b)
        {
            JVec3 ab = b - a;
            float t = JVec3.Dot(p - a, ab) / ab.LengthSq();
            t = t < 0.0f ? 0.0f : (t > 1.0f ? 1.0f : t);
            return (p - (a + ab * t)).Length();
        }

        // ------------------------------------------------------------------ geometry

        [Test]
        public void ACapsuleBetweenTwoJointsHasTheRightGeometry()
        {
            JiggleBodyShape s;
            Assert.IsTrue(JiggleBodyFit.Capsule(new JVec3(0.0f, 0.0f, 0.0f), new JVec3(0.0f, 0.4f, 0.0f), 0.05f, out s));
            Assert.AreEqual((int)JiggleBodyShapeKind.Capsule, (int)s.Kind);
            Assert.AreEqual(0.2, s.Center.Y, 1e-6);
            Assert.AreEqual(1.0, s.Axis.Y, 1e-6);
            Assert.AreEqual(0.5, s.Length, 1e-6, "joint to joint plus a radius at each end");
            Assert.AreEqual(0.05, s.Radius, 1e-7);

            JiggleCollider c = s.ToCollider();
            Assert.AreEqual(JiggleConst.ColliderCapsule, c.Kind);
            Assert.AreEqual(0.2, c.HalfHeight, 1e-6, "the segment of the solver capsule runs joint to joint");
            Assert.AreEqual(0.05, c.Radius, 1e-7);

            // A slanted capsule has a unit axis that points from the first joint to the second.
            JiggleBodyShape d;
            Assert.IsTrue(JiggleBodyFit.Capsule(new JVec3(1.0f, 2.0f, 3.0f), new JVec3(4.0f, 6.0f, 3.0f), 0.1f, out d));
            Assert.AreEqual(1.0, d.Axis.Length(), 1e-6);
            Assert.AreEqual(0.6, d.Axis.X, 1e-6);
            Assert.AreEqual(0.8, d.Axis.Y, 1e-6);
            Assert.AreEqual(2.5, d.Center.X, 1e-6);
            Assert.AreEqual(5.2, d.Length, 1e-5);

            JiggleBodyShape ball;
            Assert.IsTrue(JiggleBodyFit.Sphere(new JVec3(1.0f, 2.0f, 3.0f), 0.3f, out ball));
            Assert.AreEqual((int)JiggleBodyShapeKind.Sphere, (int)ball.Kind);
            Assert.AreEqual(0.6, ball.Length, 1e-6);
            JiggleCollider bc = ball.ToCollider();
            Assert.AreEqual(JiggleConst.ColliderSphere, bc.Kind);
            Assert.AreEqual(0.3, bc.Radius, 1e-7);
        }

        [Test]
        public void ShapesRefuseWhatIsNotAShape()
        {
            JiggleBodyShape s;
            JVec3 o = JVec3.Zero;
            JVec3 up = new JVec3(0.0f, 1.0f, 0.0f);
            Assert.IsFalse(JiggleBodyFit.Capsule(o, o, 0.1f, out s), "the same joint");
            Assert.AreEqual(0.0, s.Radius, 0.0, "a refused shape is empty");
            Assert.IsFalse(JiggleBodyFit.Capsule(o, new JVec3(0.0f, 0.00005f, 0.0f), 0.1f, out s), "closer than the minimum");
            Assert.IsFalse(JiggleBodyFit.Capsule(o, up, 0.0f, out s), "no radius");
            Assert.IsFalse(JiggleBodyFit.Capsule(o, up, -1.0f, out s), "negative radius");
            Assert.IsFalse(JiggleBodyFit.Capsule(o, up, float.NaN, out s), "NaN radius");
            Assert.IsFalse(JiggleBodyFit.Capsule(new JVec3(float.NaN, 0.0f, 0.0f), up, 0.1f, out s), "NaN joint");
            Assert.IsFalse(JiggleBodyFit.Capsule(o, new JVec3(0.0f, float.PositiveInfinity, 0.0f), 0.1f, out s), "infinite joint");
            Assert.IsFalse(JiggleBodyFit.Sphere(o, 0.0f, out s));
            Assert.IsFalse(JiggleBodyFit.Sphere(new JVec3(float.NaN, 0.0f, 0.0f), 0.1f, out s));
            Assert.IsFalse(JiggleBodyFit.Sphere(o, float.PositiveInfinity, out s));
            Assert.IsFalse(JiggleBodyFit.Head(o, up, 0.0f, 1.0f, out s), "no height, no head");
            Assert.IsFalse(JiggleBodyFit.Head(o, up, 1.7f, 0.0f, out s), "no scale, no head");
        }

        [Test]
        public void HeightAndRadiiFollowTheProportions()
        {
            Assert.AreEqual(1.62 * 1.2, JiggleBodyFit.Height(new JVec3(0.0f, 1.62f, 0.0f), JVec3.Zero), 1e-5, "the head joint to the ankles is a fifth short of the height");
            Assert.AreEqual(0.0, JiggleBodyFit.Height(JVec3.Zero, JVec3.Zero), 0.0, "head on the feet");
            Assert.AreEqual(0.0, JiggleBodyFit.Height(new JVec3(float.NaN, 1.0f, 0.0f), JVec3.Zero), 0.0);
            Assert.AreEqual(1.68, JiggleBodyFit.HeightFromTorso(new JVec3(0.0f, 0.95f, 0.0f), new JVec3(0.0f, 1.55f, 0.0f)), 1e-5);
            Assert.AreEqual(0.0, JiggleBodyFit.HeightFromTorso(JVec3.Zero, JVec3.Zero), 0.0);

            for (int i = 0; i < Parts.Length; i++)
            {
                float f = JiggleBodyFit.RadiusFraction(Parts[i]);
                Assert.IsTrue(f > 0.0f && f < 0.1f, Parts[i] + " fraction " + f);
                Assert.AreEqual(f * 1.7f, JiggleBodyFit.Radius(Parts[i], 1.7f, 1.0f), 1e-6, "radius of " + Parts[i]);
                Assert.AreEqual(f * 1.7f * 2.0f, JiggleBodyFit.Radius(Parts[i], 1.7f, 2.0f), 1e-6, "scaled radius of " + Parts[i]);
                Assert.AreEqual(0.0, JiggleBodyFit.Radius(Parts[i], 0.0f, 1.0f), 0.0, "no height");
                Assert.AreEqual(0.0, JiggleBodyFit.Radius(Parts[i], 1.7f, -1.0f), 0.0, "negative scale");
                Assert.AreEqual(0.0, JiggleBodyFit.Radius(Parts[i], float.NaN, 1.0f), 0.0, "NaN height");
            }
            // The trunk is the thickest part and the forearm and the hand the thinnest of the limbs.
            Assert.Greater(JiggleBodyFit.RadiusFraction(JiggleBodyPart.Torso), JiggleBodyFit.RadiusFraction(JiggleBodyPart.UpperLeg));
            Assert.Greater(JiggleBodyFit.RadiusFraction(JiggleBodyPart.UpperLeg), JiggleBodyFit.RadiusFraction(JiggleBodyPart.LowerLeg));
            Assert.Greater(JiggleBodyFit.RadiusFraction(JiggleBodyPart.UpperArm), JiggleBodyFit.RadiusFraction(JiggleBodyPart.LowerArm));
        }

        [Test]
        public void TheHeadSphereSitsAboveTheHeadJoint()
        {
            JiggleBodyShape s;
            Assert.IsTrue(JiggleBodyFit.Head(new JVec3(0.0f, 1.5f, 0.0f), new JVec3(0.0f, 1.6f, 0.0f), 1.7f, 1.0f, out s));
            Assert.AreEqual(0.085, s.Radius, 1e-6);
            Assert.AreEqual(1.6 + 0.085 * 0.9, s.Center.Y, 1e-5);
            Assert.AreEqual(0.0, s.Center.X, 1e-6);

            // The neck leaning forward moves the sphere forward and up along it.
            Assert.IsTrue(JiggleBodyFit.Head(new JVec3(0.0f, 1.5f, 0.0f), new JVec3(0.0f, 1.6f, 0.1f), 1.7f, 1.0f, out s));
            Assert.Greater(s.Center.Z, 0.1f);

            // With the neck and the head at one point the move is straight up.
            Assert.IsTrue(JiggleBodyFit.Head(new JVec3(0.0f, 1.6f, 0.0f), new JVec3(0.0f, 1.6f, 0.0f), 1.7f, 1.0f, out s));
            Assert.AreEqual(1.6 + 0.085 * 0.9, s.Center.Y, 1e-5);
        }

        // ------------------------------------------------------------------ in the solver

        // A standing figure of 1.7 meters, facing +Z. Returns the body shapes the fit makes from its joints.
        private static JiggleBodyShape[] Figure()
        {
            JVec3 hips = new JVec3(0.0f, 0.95f, 0.0f);
            JVec3 neck = new JVec3(0.0f, 1.48f, 0.0f);
            JVec3 head = new JVec3(0.0f, 1.55f, 0.0f);
            float height = JiggleBodyFit.HeightFromTorso(hips, head);
            Assert.AreEqual(1.68, height, 1e-4);
            JiggleBodyShape h;
            JiggleBodyShape n;
            JiggleBodyShape t;
            Assert.IsTrue(JiggleBodyFit.Head(neck, head, height, 1.0f, out h));
            Assert.IsTrue(JiggleBodyFit.Capsule(neck, head, JiggleBodyFit.Radius(JiggleBodyPart.Neck, height, 1.0f), out n));
            Assert.IsTrue(JiggleBodyFit.Torso(hips, neck, height, 1.0f, out t));
            return new JiggleBodyShape[] { h, n, t };
        }

        [Test]
        public void BodyCollidersKeepAHairStrandOutOfTheBodyOnBothBackends()
        {
            JiggleBodyShape[] body = Figure();
            JigglePresetValues hair = JigglePresets.Get(JigglePresetKind.Hair);
            JiggleParams[] ps = JigglePresets.ToParams(hair, 8, 60.0f);
            Assert.IsTrue(ps[0].Collide, "the hair preset collides");
            float r = ps[0].CollisionRadius;

            // A strand that hangs down the back of the figure, 0.2 m behind the middle of the body. Its pose then bends toward
            // the body, so the lower bones of the pose are inside the torso, and later it goes back to hanging straight.
            JVec3[] rest = new JVec3[9];
            for (int k = 0; k <= 8; k++)
            {
                rest[k] = new JVec3(0.0f, 1.62f - 0.09f * (float)k, -0.2f);
            }

            JiggleWorld[] worlds = { new JiggleWorld(), new JiggleWorld() };
            JiggleBackend[] backends = { JiggleBackend.Managed, JiggleBackend.Native };
            for (int w = 0; w < 2; w++)
            {
                worlds[w].AddChain(rest, ps);
                uint[] list = new uint[body.Length];
                for (int i = 0; i < body.Length; i++)
                {
                    list[i] = (uint)worlds[w].AddCollider(body[i].ToCollider());
                }
                worlds[w].SetColliderList(0, list);
            }

            float closest = float.MaxValue;
            for (int f = 0; f < 300; f++)
            {
                float bend = f < 100 ? (float)f / 100.0f : (f < 200 ? 1.0f : 0.0f);
                JVec3[] pose = new JVec3[rest.Length];
                for (int i = 0; i < rest.Length; i++)
                {
                    pose[i] = rest[i] + new JVec3(0.0f, 0.0f, 0.04f * (float)i * bend);
                }
                for (int w = 0; w < 2; w++)
                {
                    worlds[w].SetPose(0, pose);
                    Assert.AreEqual(JiggleConst.Ok, worlds[w].StepFixed(1, backends[w]), "frame " + f);
                }
                for (int i = 1; i <= 8; i++)
                {
                    JVec3 a = worlds[0].WorldPos(0, i);
                    JVec3 b = worlds[1].WorldPos(0, i);
                    Assert.IsTrue(a.IsFinite(), "frame " + f + " bone " + i + " is not finite");
                    bool same = Bits(a.X) == Bits(b.X) && Bits(a.Y) == Bits(b.Y) && Bits(a.Z) == Bits(b.Z);
                    Assert.IsTrue(same, "frame " + f + " bone " + i + ": the backends differ");
                    for (int s = 0; s < body.Length; s++)
                    {
                        float surface = Surface(body[s], a);
                        float into = r - surface;
                        if (surface < closest)
                        {
                            closest = surface;
                        }
                        // A bone is kept out of every shape by its own thickness, give or take a centimeter.
                        Assert.Less(into, 0.01f, "frame " + f + " bone " + i + " is inside shape " + s + " by " + into + " m");
                    }
                }
            }
            Assert.IsTrue(closest < r + 0.02f, "the strand never came near the body, the closest it got was " + closest);
        }

        // The distance from a point to the surface of a body shape.
        private static float Surface(JiggleBodyShape s, JVec3 p)
        {
            if (s.Kind == JiggleBodyShapeKind.Sphere)
            {
                return (p - s.Center).Length() - s.Radius;
            }
            float half = s.Length * 0.5f - s.Radius;
            return SegmentDistance(p, s.Center - s.Axis * half, s.Center + s.Axis * half) - s.Radius;
        }

        [Test]
        public void HeightFromTheAnklesAgreesWithHeightFromTheTorso()
        {
            // A standing figure with the joints of an average adult: the two ways to find the height land within a few percent.
            JVec3 hips = new JVec3(0.0f, 0.95f, 0.0f);
            JVec3 head = new JVec3(0.0f, 1.55f, 0.0f);
            JVec3 ankles = new JVec3(0.0f, 0.08f, 0.0f);
            float fromAnkles = JiggleBodyFit.Height(head, ankles);
            float fromTorso = JiggleBodyFit.HeightFromTorso(hips, head);
            Assert.AreEqual(1.764, fromAnkles, 1e-4);
            Assert.AreEqual(1.68, fromTorso, 1e-4);
            Assert.Less(Math.Abs(fromAnkles - fromTorso) / fromAnkles, 0.06f);
        }

        [Test]
        public void TheTorsoEndsAtTheNeckAndTheHandSitsPastTheWrist()
        {
            JVec3 hips = new JVec3(0.0f, 0.95f, 0.0f);
            JVec3 neck = new JVec3(0.0f, 1.48f, 0.0f);
            JiggleBodyShape t;
            Assert.IsTrue(JiggleBodyFit.Torso(hips, neck, 1.7f, 1.0f, out t));
            float r = JiggleBodyFit.Radius(JiggleBodyPart.Torso, 1.7f, 1.0f);
            Assert.AreEqual(r, t.Radius, 1e-6);
            JVec3 topCap = t.Center + t.Axis * (t.Length * 0.5f);
            JVec3 bottomCap = t.Center - t.Axis * (t.Length * 0.5f);
            Assert.AreEqual(1.48, topCap.Y, 1e-4, "the top cap stops at the neck joint");
            Assert.AreEqual(0.95 - r, bottomCap.Y, 1e-4, "the bottom cap reaches a radius below the hips");

            // Hips and neck closer than a radius: the capsule runs joint to joint and is still a shape.
            JiggleBodyShape short1;
            Assert.IsTrue(JiggleBodyFit.Torso(hips, new JVec3(0.0f, 1.0f, 0.0f), 1.7f, 1.0f, out short1));
            Assert.AreEqual(0.05 + r * 2.0, short1.Length, 1e-4);
            Assert.IsFalse(JiggleBodyFit.Torso(hips, hips, 1.7f, 1.0f, out short1));
            Assert.IsFalse(JiggleBodyFit.Torso(new JVec3(float.NaN, 0.0f, 0.0f), neck, 1.7f, 1.0f, out short1));

            JiggleBodyShape h;
            JVec3 elbow = new JVec3(0.45f, 1.42f, 0.0f);
            JVec3 wrist = new JVec3(0.72f, 1.42f, 0.0f);
            Assert.IsTrue(JiggleBodyFit.Hand(elbow, wrist, 1.7f, 1.0f, out h));
            float hr = JiggleBodyFit.Radius(JiggleBodyPart.Hand, 1.7f, 1.0f);
            Assert.AreEqual(0.72 + hr, h.Center.X, 1e-5, "one radius past the wrist along the forearm");
            Assert.AreEqual(1.42, h.Center.Y, 1e-5);
            Assert.IsTrue(JiggleBodyFit.Hand(wrist, wrist, 1.7f, 1.0f, out h), "no forearm direction puts the hand on the wrist");
            Assert.AreEqual(0.72, h.Center.X, 1e-5);
            Assert.IsFalse(JiggleBodyFit.Hand(elbow, wrist, 0.0f, 1.0f, out h));
        }

        [Test]
        public void CapsulesThatMeetAtAJointOverlapThere()
        {
            // The caps reach one radius past the joint, so a thin particle cannot slip between two capsules.
            JVec3 a = new JVec3(0.0f, 0.0f, 0.0f);
            JVec3 b = new JVec3(0.0f, 0.3f, 0.0f);
            JVec3 c = new JVec3(0.2f, 0.55f, 0.0f);
            JiggleBodyShape upper;
            JiggleBodyShape lower;
            Assert.IsTrue(JiggleBodyFit.Capsule(a, b, 0.04f, out upper));
            Assert.IsTrue(JiggleBodyFit.Capsule(b, c, 0.035f, out lower));
            JVec3 end1 = upper.Center + upper.Axis * (upper.Length * 0.5f);
            JVec3 start2 = lower.Center - lower.Axis * (lower.Length * 0.5f);
            Assert.AreEqual((b - a).Length() + 0.04, (end1 - a).Length(), 1e-5, "the first reaches past the joint");
            Assert.AreEqual((b - c).Length() + 0.035, (start2 - c).Length(), 1e-5, "the second reaches back past it");
            // A point just past the joint, on the far side of the first capsule, is inside the first.
            JVec3 probe = b + upper.Axis * 0.03f;
            Assert.Less(Surface(upper, probe), 0.0f);
            Assert.Less(Surface(lower, b - lower.Axis * 0.02f), 0.0f);
        }
    }
}
