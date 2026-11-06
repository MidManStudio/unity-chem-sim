// ============================================================================
// NOTICE: Full documentation, design decisions, and fix history for this file
// live in docs/com.midmanstudio.allthingsjiggle.md, section "Runtime/Core/JiggleMath.cs"
// ============================================================================
using System;
using System.Runtime.InteropServices;

namespace MidManStudio.AllThingsJiggle
{
    /// <summary>
    /// Four floats, the padded slot used for positions in the shared arrays.
    /// The fourth float is padding for positions and the w component for rotations.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct JVec4
    {
        /// <summary>X.</summary>
        public float X;
        /// <summary>Y.</summary>
        public float Y;
        /// <summary>Z.</summary>
        public float Z;
        /// <summary>W, or padding.</summary>
        public float W;

        /// <summary>Builds a slot.</summary>
        public JVec4(float x, float y, float z, float w)
        {
            X = x;
            Y = y;
            Z = z;
            W = w;
        }
    }

    /// <summary>
    /// Three floats with the exact operation order of the Rust `V3`, so both
    /// kernels give the same bits. Plain fields and static methods only, so
    /// Burst can compile code that uses it.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct JVec3
    {
        /// <summary>X.</summary>
        public float X;
        /// <summary>Y.</summary>
        public float Y;
        /// <summary>Z.</summary>
        public float Z;

        /// <summary>Builds a vector.</summary>
        public JVec3(float x, float y, float z)
        {
            X = x;
            Y = y;
            Z = z;
        }

        /// <summary>All zeros.</summary>
        public static JVec3 Zero { get { return new JVec3(0f, 0f, 0f); } }
        /// <summary>World up, used as a fallback direction.</summary>
        public static JVec3 Up { get { return new JVec3(0f, 1f, 0f); } }
        /// <summary>Forward, used as a fallback direction.</summary>
        public static JVec3 Forward { get { return new JVec3(0f, 0f, 1f); } }

        /// <summary>Reads the first three floats of a padded slot.</summary>
        public static JVec3 From4(in JVec4 a)
        {
            return new JVec3(a.X, a.Y, a.Z);
        }

        /// <summary>Writes into a padded slot, leaving the fourth float alone.</summary>
        public void Write4(ref JVec4 a)
        {
            a.X = X;
            a.Y = Y;
            a.Z = Z;
        }

        /// <summary>Dot product.</summary>
        public static float Dot(JVec3 a, JVec3 b)
        {
            return a.X * b.X + a.Y * b.Y + a.Z * b.Z;
        }

        /// <summary>Cross product.</summary>
        public static JVec3 Cross(JVec3 a, JVec3 b)
        {
            return new JVec3(
                a.Y * b.Z - a.Z * b.Y,
                a.Z * b.X - a.X * b.Z,
                a.X * b.Y - a.Y * b.X);
        }

        /// <summary>Squared length.</summary>
        public float LengthSq()
        {
            return X * X + Y * Y + Z * Z;
        }

        /// <summary>Length.</summary>
        public float Length()
        {
            return MathF.Sqrt(LengthSq());
        }

        /// <summary>The unit vector, or <paramref name="fallback"/> when the length is too small to divide by.</summary>
        public JVec3 NormalizeSafe(JVec3 fallback)
        {
            float l2 = LengthSq();
            if (l2 > 1e-20f)
            {
                return this * (1.0f / MathF.Sqrt(l2));
            }
            return fallback;
        }

        /// <summary>a + (b - a) * t.</summary>
        public static JVec3 Lerp(JVec3 a, JVec3 b, float t)
        {
            return a + (b - a) * t;
        }

        /// <summary>Whether all three components are finite.</summary>
        public bool IsFinite()
        {
            return JiggleMath.IsFinite(X) && JiggleMath.IsFinite(Y) && JiggleMath.IsFinite(Z);
        }

        /// <summary>Sum.</summary>
        public static JVec3 operator +(JVec3 a, JVec3 b)
        {
            return new JVec3(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
        }

        /// <summary>Difference.</summary>
        public static JVec3 operator -(JVec3 a, JVec3 b)
        {
            return new JVec3(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
        }

        /// <summary>Scale.</summary>
        public static JVec3 operator *(JVec3 a, float s)
        {
            return new JVec3(a.X * s, a.Y * s, a.Z * s);
        }

        /// <summary>Negation.</summary>
        public static JVec3 operator -(JVec3 a)
        {
            return new JVec3(-a.X, -a.Y, -a.Z);
        }
    }

    /// <summary>A quaternion, w last, Hamilton product, the same layout as Unity.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct JQuat
    {
        /// <summary>X.</summary>
        public float X;
        /// <summary>Y.</summary>
        public float Y;
        /// <summary>Z.</summary>
        public float Z;
        /// <summary>W.</summary>
        public float W;

        /// <summary>Builds a quaternion.</summary>
        public JQuat(float x, float y, float z, float w)
        {
            X = x;
            Y = y;
            Z = z;
            W = w;
        }

        /// <summary>No rotation.</summary>
        public static JQuat Identity { get { return new JQuat(0f, 0f, 0f, 1f); } }

        /// <summary>Reads four floats as x, y, z, w.</summary>
        public static JQuat From4(in JVec4 a)
        {
            return new JQuat(a.X, a.Y, a.Z, a.W);
        }

        /// <summary>The four floats as a slot.</summary>
        public JVec4 To4()
        {
            return new JVec4(X, Y, Z, W);
        }

        /// <summary>Scales to unit length. An all zero quaternion becomes the identity.</summary>
        public JQuat Normalized()
        {
            float l2 = X * X + Y * Y + Z * Z + W * W;
            if (l2 > 1e-20f)
            {
                float inv = 1.0f / MathF.Sqrt(l2);
                return new JQuat(X * inv, Y * inv, Z * inv, W * inv);
            }
            return Identity;
        }

        /// <summary>a * b: b is applied first, as in Unity.</summary>
        public static JQuat Mul(JQuat a, JQuat o)
        {
            return new JQuat(
                a.W * o.X + a.X * o.W + a.Y * o.Z - a.Z * o.Y,
                a.W * o.Y - a.X * o.Z + a.Y * o.W + a.Z * o.X,
                a.W * o.Z + a.X * o.Y - a.Y * o.X + a.Z * o.W,
                a.W * o.W - a.X * o.X - a.Y * o.Y - a.Z * o.Z);
        }

        /// <summary>Rotates a vector.</summary>
        public JVec3 Rotate(JVec3 v)
        {
            JVec3 u = new JVec3(X, Y, Z);
            JVec3 t = JVec3.Cross(u, v) * 2.0f;
            return v + t * W + JVec3.Cross(u, t);
        }

        /// <summary>The conjugate, which is the inverse for a unit quaternion.</summary>
        public JQuat Conjugate()
        {
            return new JQuat(-X, -Y, -Z, W);
        }

        /// <summary>The shortest rotation taking unit vector a to unit vector b, with no trigonometry.</summary>
        public static JQuat FromTo(JVec3 a, JVec3 b)
        {
            float d = JVec3.Dot(a, b);
            if (d < -0.999999f)
            {
                JVec3 p = JiggleMath.Perpendicular(a);
                return new JQuat(p.X, p.Y, p.Z, 0.0f);
            }
            JVec3 c = JVec3.Cross(a, b);
            return new JQuat(c.X, c.Y, c.Z, 1.0f + d).Normalized();
        }

        /// <summary>Scales the rotation toward the identity by t in 0..1, by normalized lerp.</summary>
        public JQuat ScaledTowardIdentity(float t)
        {
            return new JQuat(X * t, Y * t, Z * t, (1.0f - t) + W * t).Normalized();
        }
    }

    /// <summary>Scalar helpers shared by the kernel files.</summary>
    public static class JiggleMath
    {
        /// <summary>Whether a float is neither NaN nor infinite.</summary>
        public static bool IsFinite(float v)
        {
            return !(float.IsNaN(v) || float.IsInfinity(v));
        }

        /// <summary>a + (b - a) * t.</summary>
        public static float Lerp(float a, float b, float t)
        {
            return a + (b - a) * t;
        }

        /// <summary>
        /// A unit vector perpendicular to a, chosen from the axis a points along least.
        /// </summary>
        public static JVec3 Perpendicular(JVec3 a)
        {
            float ax = MathF.Abs(a.X);
            float ay = MathF.Abs(a.Y);
            float az = MathF.Abs(a.Z);
            JVec3 axis;
            if (ax <= ay && ax <= az)
            {
                axis = new JVec3(1f, 0f, 0f);
            }
            else if (ay <= az)
            {
                axis = new JVec3(0f, 1f, 0f);
            }
            else
            {
                axis = new JVec3(0f, 0f, 1f);
            }
            return JVec3.Cross(a, axis).NormalizeSafe(JVec3.Up);
        }

        /// <summary>
        /// The float to int conversion of Rust's `as i32`: NaN gives 0 and values outside the range stick to
        /// the ends. A plain C# cast is not defined for those values and differs between runtimes.
        /// </summary>
        public static int FloatToInt(float v)
        {
            if (float.IsNaN(v))
            {
                return 0;
            }
            if (v >= 2147483648.0f)
            {
                return int.MaxValue;
            }
            if (v <= -2147483648.0f)
            {
                return int.MinValue;
            }
            return (int)v;
        }
    }
}
