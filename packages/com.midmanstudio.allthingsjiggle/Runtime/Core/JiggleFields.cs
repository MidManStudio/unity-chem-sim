// ============================================================================
// NOTICE: Full documentation, design decisions, and fix history for this file
// live in docs/com.midmanstudio.allthingsjiggle.md, section "Runtime/Core/JiggleFields.cs"
// ============================================================================
namespace MidManStudio.AllThingsJiggle
{
    /// <summary>
    /// Wind and force fields. A field gives a push at a point. Acceleration
    /// fields are summed every step and scaled by the particle's wind response.
    /// Impulse fields are a velocity change that the solver applies on the first
    /// step of a frame only. Gusts use a hash based value noise, so the same time
    /// and place give the same push on every machine and in the Rust library.
    /// </summary>
    public static class JiggleFields
    {
        private static float HashUnit(int i, uint channel)
        {
            unchecked
            {
                uint h = (uint)i * 0x9E3779B1u ^ channel * 0x85EBCA6Bu;
                h ^= h >> 15;
                h *= 0x2C1B3C6Du;
                h ^= h >> 12;
                h *= 0x297A2D39u;
                h ^= h >> 15;
                return (float)(h >> 8) * (2.0f / 16777216.0f) - 1.0f;
            }
        }

        /// <summary>Smooth value noise in -1 to 1 along one axis.</summary>
        public static float Noise1(float x, uint channel)
        {
            float f = System.MathF.Floor(x);
            int i = JiggleMath.FloatToInt(f);
            float t = x - f;
            float s = t * t * (3.0f - 2.0f * t);
            float a = HashUnit(i, channel);
            float b = HashUnit(unchecked(i + 1), channel);
            return a + (b - a) * s;
        }

        /// <summary>Three noise channels as a vector, each in -1 to 1.</summary>
        public static JVec3 NoiseVec(float phase)
        {
            return new JVec3(Noise1(phase, 1), Noise1(phase + 17.31f, 2), Noise1(phase + 41.7f, 3));
        }

        private static float Falloff(float u, uint power)
        {
            if (u >= 1.0f)
            {
                return 0.0f;
            }
            float t = u < 0.0f ? 1.0f : 1.0f - u;
            float r = 1.0f;
            uint k = 0;
            while (k < power)
            {
                r *= t;
                k += 1;
            }
            return r;
        }

        /// <summary>
        /// The push of field f at a particle. centerRel is the field center and pRel the particle position, both in
        /// the rig frame. pWorld is the particle in world space and time the time in seconds, used only for gusts.
        /// </summary>
        public static JVec3 Push(in JiggleField f, JVec3 centerRel, JVec3 pRel, JVec3 pWorld, float time)
        {
            switch (f.Kind)
            {
                case JiggleConst.FieldDirectional:
                {
                    float w = 1.0f;
                    if (f.Radius > 0.0f)
                    {
                        float d = (pRel - centerRel).Length();
                        w = Falloff(d / f.Radius, f.Power);
                    }
                    if (w <= 0.0f)
                    {
                        return JVec3.Zero;
                    }
                    JVec3 v = new JVec3(f.Dir.X, f.Dir.Y, f.Dir.Z);
                    if (f.Turbulence != 0.0f)
                    {
                        JVec3 k = new JVec3(0.37f, 0.53f, 0.29f);
                        float phase = time * f.Frequency + JVec3.Dot(pWorld, k) * f.Dir.W;
                        v = v + NoiseVec(phase) * f.Turbulence;
                    }
                    return v * (f.Strength * w);
                }
                case JiggleConst.FieldRadial:
                {
                    if (f.Radius <= 0.0f)
                    {
                        return JVec3.Zero;
                    }
                    JVec3 d = pRel - centerRel;
                    float dist = d.Length();
                    float w = Falloff(dist / f.Radius, f.Power);
                    if (w <= 0.0f)
                    {
                        return JVec3.Zero;
                    }
                    JVec3 dir = d.NormalizeSafe(JVec3.Up);
                    return dir * (f.Strength * w);
                }
                case JiggleConst.FieldVortex:
                {
                    if (f.Radius <= 0.0f)
                    {
                        return JVec3.Zero;
                    }
                    JVec3 d = pRel - centerRel;
                    float dist = d.Length();
                    float w = Falloff(dist / f.Radius, f.Power);
                    if (w <= 0.0f)
                    {
                        return JVec3.Zero;
                    }
                    JVec3 axis = new JVec3(f.Dir.X, f.Dir.Y, f.Dir.Z).NormalizeSafe(JVec3.Up);
                    JVec3 radial = d - axis * JVec3.Dot(d, axis);
                    JVec3 tangent = JVec3.Cross(axis, radial).NormalizeSafe(JVec3.Zero);
                    return tangent * (f.Strength * w);
                }
                default:
                    return JVec3.Zero;
            }
        }

        /// <summary>Whether the field can reach a rig whose anchor is at anchorWorld and whose particles stay within extent of it.</summary>
        public static bool Reaches(in JiggleField f, JVec3 anchorWorld, float extent)
        {
            if (f.Strength == 0.0f)
            {
                return false;
            }
            if (f.Kind == JiggleConst.FieldDirectional && f.Radius <= 0.0f)
            {
                return true;
            }
            if (f.Radius <= 0.0f)
            {
                return false;
            }
            JVec3 c = JVec3.From4(f.Center);
            return (anchorWorld - c).Length() < f.Radius + extent;
        }
    }
}
