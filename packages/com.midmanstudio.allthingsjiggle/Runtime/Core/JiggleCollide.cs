// ============================================================================
// NOTICE: Full documentation, design decisions, and fix history for this file
// live in docs/com.midmanstudio.allthingsjiggle.md, section "Runtime/Core/JiggleCollide.cs"
// ============================================================================
using System;

namespace MidManStudio.AllThingsJiggle
{
    /// <summary>
    /// Pushing a particle out of a collider. A particle is a sphere of radius r.
    /// Each case takes the collider's center already moved into the rig frame and
    /// returns the particle position after it has been pushed out along the
    /// shortest way, or the same position when nothing touches.
    /// </summary>
    public static class JiggleCollide
    {
        private static float ClampF(float v, float lo, float hi)
        {
            if (v < lo)
            {
                return lo;
            }
            if (v > hi)
            {
                return hi;
            }
            return v;
        }

        private static JVec3 PushFromCore(JVec3 core, float minDist, JVec3 x)
        {
            JVec3 d = x - core;
            float l2 = d.LengthSq();
            if (l2 >= minDist * minDist)
            {
                return x;
            }
            float l = MathF.Sqrt(l2);
            JVec3 n = l > 1e-6f ? d * (1.0f / l) : JVec3.Up;
            return core + n * minDist;
        }

        /// <summary>Pushes the particle at x with radius r out of c, whose center in the rig frame is center.</summary>
        public static JVec3 PushOut(in JiggleCollider c, JVec3 center, JVec3 x, float r)
        {
            switch (c.Kind)
            {
                case JiggleConst.ColliderSphere:
                    return PushFromCore(center, c.Radius + r, x);
                case JiggleConst.ColliderCapsule:
                {
                    JVec3 axis = JVec3.From4(c.Axis);
                    float t = ClampF(JVec3.Dot(x - center, axis), -c.HalfHeight, c.HalfHeight);
                    JVec3 core = center + axis * t;
                    return PushFromCore(core, c.Radius + r, x);
                }
                case JiggleConst.ColliderPlane:
                {
                    JVec3 n = JVec3.From4(c.Axis);
                    float s = JVec3.Dot(x - center, n);
                    if (s < r)
                    {
                        return x + n * (r - s);
                    }
                    return x;
                }
                case JiggleConst.ColliderBox:
                    return PushBox(c, center, x, r);
                case JiggleConst.ColliderInsideSphere:
                {
                    JVec3 d = x - center;
                    float l = d.Length();
                    float maxDist = c.Radius - r;
                    if (maxDist < 0.0f)
                    {
                        maxDist = 0.0f;
                    }
                    if (l > maxDist && l > 1e-6f)
                    {
                        return center + d * (maxDist / l);
                    }
                    return x;
                }
                default:
                    return x;
            }
        }

        private static JVec3 PushBox(in JiggleCollider c, JVec3 center, JVec3 x, float r)
        {
            JQuat q = JQuat.From4(c.Rot);
            JQuat inv = q.Conjugate();
            JVec3 local = inv.Rotate(x - center);
            JVec3 half = JVec3.From4(c.Extents);
            float rr = c.Radius + r;
            JVec3 clamped = new JVec3(
                ClampF(local.X, -half.X, half.X),
                ClampF(local.Y, -half.Y, half.Y),
                ClampF(local.Z, -half.Z, half.Z));
            JVec3 d = local - clamped;
            float l2 = d.LengthSq();
            if (l2 > 1e-12f)
            {
                // Outside the core box: the same as a sphere around the closest point.
                if (l2 >= rr * rr)
                {
                    return x;
                }
                float l = MathF.Sqrt(l2);
                JVec3 n = d * (1.0f / l);
                JVec3 pushed = clamped + n * rr;
                return center + q.Rotate(pushed);
            }
            // Inside the core box: leave through the nearest face.
            float px = half.X - MathF.Abs(local.X);
            float py = half.Y - MathF.Abs(local.Y);
            float pz = half.Z - MathF.Abs(local.Z);
            JVec3 o = local;
            if (px <= py && px <= pz)
            {
                float s = local.X >= 0.0f ? 1.0f : -1.0f;
                o.X = s * (half.X + rr);
            }
            else if (py <= pz)
            {
                float s = local.Y >= 0.0f ? 1.0f : -1.0f;
                o.Y = s * (half.Y + rr);
            }
            else
            {
                float s = local.Z >= 0.0f ? 1.0f : -1.0f;
                o.Z = s * (half.Z + rr);
            }
            return center + q.Rotate(o);
        }
    }
}
