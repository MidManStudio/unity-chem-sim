// ============================================================================
// NOTICE: Full documentation, design decisions, and fix history for this file
// live in docs/com.midmanstudio.allthingsjiggle.md, section "Runtime/Core/JiggleBodyFit.cs"
// ============================================================================
using System;

namespace MidManStudio.AllThingsJiggle
{
    /// <summary>The parts of a body that get a collider.</summary>
    public enum JiggleBodyPart
    {
        /// <summary>The head: a sphere.</summary>
        Head = 0,
        /// <summary>The neck: a capsule from the neck to the head.</summary>
        Neck = 1,
        /// <summary>The trunk: a capsule from the hips to the top of the chest.</summary>
        Torso = 2,
        /// <summary>An upper arm: a capsule from the shoulder to the elbow.</summary>
        UpperArm = 3,
        /// <summary>A forearm: a capsule from the elbow to the wrist.</summary>
        LowerArm = 4,
        /// <summary>A hand: a sphere.</summary>
        Hand = 5,
        /// <summary>A thigh: a capsule from the hip to the knee.</summary>
        UpperLeg = 6,
        /// <summary>A shin: a capsule from the knee to the ankle.</summary>
        LowerLeg = 7,
        /// <summary>A foot: a capsule from the ankle to the toes.</summary>
        Foot = 8
    }

    /// <summary>The two shapes a body collider has.</summary>
    public enum JiggleBodyShapeKind
    {
        /// <summary>A ball.</summary>
        Sphere = 0,
        /// <summary>A capsule.</summary>
        Capsule = 1
    }

    /// <summary>A body collider in world space, before it is given to the solver or made into a component.</summary>
    public struct JiggleBodyShape
    {
        /// <summary>Sphere or capsule.</summary>
        public JiggleBodyShapeKind Kind;
        /// <summary>The middle of the shape.</summary>
        public JVec3 Center;
        /// <summary>The direction of a capsule from its start to its end, a unit vector. Up for a sphere.</summary>
        public JVec3 Axis;
        /// <summary>A capsule's length from end to end, caps included. 2 * Radius for a sphere.</summary>
        public float Length;
        /// <summary>The radius, in meters.</summary>
        public float Radius;

        /// <summary>The shape as the solver takes it.</summary>
        public JiggleCollider ToCollider()
        {
            if (Kind == JiggleBodyShapeKind.Capsule)
            {
                float half = Length * 0.5f - Radius;
                return JiggleShapes.Capsule(Center, Axis, half > 0.0f ? half : 0.0f, Radius);
            }
            return JiggleShapes.Sphere(Center, Radius);
        }
    }

    /// <summary>
    /// Fits body colliders to the joints of a rig: the size of each part from the height of the body, a capsule between two
    /// joints, a sphere for the head. The proportions are those of an average adult and are starting points: every caller
    /// scales them, and the labs show them. It has no Unity references, so the maths is tested outside Unity.
    /// </summary>
    public static class JiggleBodyFit
    {
        /// <summary>The height of the body is about this many times the distance from the hips to the head joint.</summary>
        public const float HeightPerHipsToHead = 2.8f;

        /// <summary>
        /// The height of the body is about this many times the distance from the head joint to the ankles. The head joint sits at
        /// the base of the skull and the ankle joint a few centimeters above the sole, so the two distances differ by about a fifth.
        /// </summary>
        public const float HeightPerAnkleToHead = 1.2f;

        /// <summary>Joints closer than this, in meters, are the same joint and make no capsule.</summary>
        public const float MinLength = 1.0e-4f;

        /// <summary>The radius of a part as a share of the height of the body.</summary>
        public static float RadiusFraction(JiggleBodyPart part)
        {
            switch (part)
            {
                case JiggleBodyPart.Head:
                    return 0.05f;
                case JiggleBodyPart.Neck:
                    return 0.03f;
                case JiggleBodyPart.Torso:
                    return 0.085f;
                case JiggleBodyPart.UpperArm:
                    return 0.028f;
                case JiggleBodyPart.LowerArm:
                    return 0.022f;
                case JiggleBodyPart.Hand:
                    return 0.025f;
                case JiggleBodyPart.UpperLeg:
                    return 0.047f;
                case JiggleBodyPart.LowerLeg:
                    return 0.032f;
                case JiggleBodyPart.Foot:
                    return 0.025f;
                default:
                    return 0.03f;
            }
        }

        /// <summary>
        /// The height of the body from the head joint and the middle of the two ankles, or 0 when either is not a finite point or
        /// they are closer than <see cref="MinLength"/>. See <see cref="HeightPerAnkleToHead"/>.
        /// </summary>
        public static float Height(JVec3 head, JVec3 feet)
        {
            if (!head.IsFinite() || !feet.IsFinite())
            {
                return 0.0f;
            }
            float d = (head - feet).Length();
            return d >= MinLength ? d * HeightPerAnkleToHead : 0.0f;
        }

        /// <summary>The height of the body when only the hips and the head joint are known. 0 for bad input.</summary>
        public static float HeightFromTorso(JVec3 hips, JVec3 head)
        {
            if (!hips.IsFinite() || !head.IsFinite())
            {
                return 0.0f;
            }
            float d = (head - hips).Length();
            return d >= MinLength ? d * HeightPerHipsToHead : 0.0f;
        }

        /// <summary>The radius of a part for a body of <paramref name="height"/> meters, times <paramref name="scale"/>. 0 for bad input.</summary>
        public static float Radius(JiggleBodyPart part, float height, float scale)
        {
            if (!JiggleMath.IsFinite(height) || !JiggleMath.IsFinite(scale) || height <= 0.0f || scale <= 0.0f)
            {
                return 0.0f;
            }
            return RadiusFraction(part) * height * scale;
        }

        /// <summary>A sphere. False, and a zero shape, for a center that is not finite or a radius that is not positive.</summary>
        public static bool Sphere(JVec3 center, float radius, out JiggleBodyShape shape)
        {
            shape = new JiggleBodyShape();
            shape.Axis = JVec3.Up;
            if (!center.IsFinite() || !JiggleMath.IsFinite(radius) || radius <= 0.0f)
            {
                return false;
            }
            shape.Kind = JiggleBodyShapeKind.Sphere;
            shape.Center = center;
            shape.Length = radius * 2.0f;
            shape.Radius = radius;
            return true;
        }

        /// <summary>
        /// A capsule from joint <paramref name="a"/> to joint <paramref name="b"/>. Its caps reach one radius past each joint, so
        /// two capsules that meet at a joint overlap and leave no gap. False, and a zero shape, for joints that are not finite or
        /// closer than <see cref="MinLength"/>, or a radius that is not positive.
        /// </summary>
        public static bool Capsule(JVec3 a, JVec3 b, float radius, out JiggleBodyShape shape)
        {
            shape = new JiggleBodyShape();
            shape.Axis = JVec3.Up;
            if (!a.IsFinite() || !b.IsFinite() || !JiggleMath.IsFinite(radius) || radius <= 0.0f)
            {
                return false;
            }
            JVec3 ab = b - a;
            float len = ab.Length();
            if (!(len >= MinLength))
            {
                return false;
            }
            shape.Kind = JiggleBodyShapeKind.Capsule;
            shape.Center = (a + b) * 0.5f;
            shape.Axis = ab * (1.0f / len);
            shape.Length = len + radius * 2.0f;
            shape.Radius = radius;
            return true;
        }

        /// <summary>
        /// The trunk: a capsule from the hips up to the neck joint. The top cap ends at the neck joint and does not reach past
        /// it, because a cap one radius above the neck would swallow the back of the head. When the hips and the neck are closer
        /// than a radius the capsule runs joint to joint instead.
        /// </summary>
        public static bool Torso(JVec3 hips, JVec3 neck, float height, float scale, out JiggleBodyShape shape)
        {
            float r = Radius(JiggleBodyPart.Torso, height, scale);
            if (!hips.IsFinite() || !neck.IsFinite())
            {
                return Capsule(hips, neck, r, out shape);
            }
            JVec3 up = neck - hips;
            float len = up.Length();
            if (len - r < MinLength)
            {
                return Capsule(hips, neck, r, out shape);
            }
            JVec3 top = neck - up * (r / len);
            return Capsule(hips, top, r, out shape);
        }

        /// <summary>
        /// A hand: a sphere one radius past the wrist along the forearm, so it covers the palm and not the wrist. With the elbow
        /// and the wrist at one point the sphere sits on the wrist.
        /// </summary>
        public static bool Hand(JVec3 elbow, JVec3 wrist, float height, float scale, out JiggleBodyShape shape)
        {
            float r = Radius(JiggleBodyPart.Hand, height, scale);
            JVec3 along = (wrist - elbow).NormalizeSafe(JVec3.Zero);
            return Sphere(wrist + along * r, r, out shape);
        }

        /// <summary>
        /// The head sphere. The head joint sits at the base of the skull, so the center is moved up along the neck by 0.9 of the
        /// radius. With the neck and the head at the same point the move is straight up.
        /// </summary>
        public static bool Head(JVec3 neck, JVec3 head, float height, float scale, out JiggleBodyShape shape)
        {
            float r = Radius(JiggleBodyPart.Head, height, scale);
            JVec3 up = (head - neck).NormalizeSafe(JVec3.Up);
            return Sphere(head + up * (r * 0.9f), r, out shape);
        }
    }
}
