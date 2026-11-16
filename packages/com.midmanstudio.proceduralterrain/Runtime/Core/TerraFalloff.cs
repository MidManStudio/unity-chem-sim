// ============================================================================
// NOTICE: Full documentation, design decisions, and fix history for this file
// live in docs/com.midmanstudio.proceduralterrain.md, section "Runtime/Core/TerraFalloff.cs"
// ============================================================================
using System;

namespace MidManStudio.ProceduralTerrain
{
    public enum TerraFalloffMode
    {
        None = 0,
        /// <summary>Glazycheva's island: the noise is scaled by one minus the squared distance from the centre over the radius.</summary>
        Radial = 1,
        /// <summary>Tivolt's falloff: the noise is blended toward the ground level over a border along the edges of a rectangle.</summary>
        Edge = 2
    }

    /// <summary>
    /// Settings of the falloff that closes the map into an island. It works on the whole world, in true coordinates, never per
    /// chunk, so every chunk sees the same shoreline.
    /// </summary>
    [Serializable]
    public class TerraFalloffSettings
    {
        public TerraFalloffMode mode = TerraFalloffMode.None;

        /// <summary>The centre of the island in true metres.</summary>
        public double centerX;
        public double centerZ;

        /// <summary>Radial: the distance from the centre at which the noise has fallen to the ground value.</summary>
        public double radius = 600.0;

        /// <summary>Edge: half the size of the rectangle that is left alone, in metres, on each axis.</summary>
        public double halfExtentX = 600.0;
        public double halfExtentZ = 600.0;

        /// <summary>Edge: how wide the falloff border is, in metres, measured inward from the edge of the rectangle.</summary>
        public double border = 150.0;

        /// <summary>The power applied to the blend. 1 is linear, 2 is a softer shore.</summary>
        public double exponent = 2.0;

        /// <summary>The normalised noise value at the very edge, 0 is the lowest ground.</summary>
        public double groundValue = 0.0;
    }

    /// <summary>The falloff as a function.</summary>
    public static class TerraFalloff
    {
        /// <summary>The weight in 0..1 that keeps the noise at a point: 1 leaves it, 0 puts it at the ground value.</summary>
        public static double Weight(TerraFalloffSettings s, double x, double z)
        {
            if (s == null || s.mode == TerraFalloffMode.None)
            {
                return 1.0;
            }
            double exponent = s.exponent < 1e-6 ? 1e-6 : s.exponent;
            if (s.mode == TerraFalloffMode.Radial)
            {
                double radius = s.radius > 1e-6 ? s.radius : 1e-6;
                double dx = x - s.centerX;
                double dz = z - s.centerZ;
                double d = Math.Sqrt(dx * dx + dz * dz) / radius;
                double w = 1.0 - Math.Pow(d, exponent);
                return w < 0.0 ? 0.0 : (w > 1.0 ? 1.0 : w);
            }
            double toEdgeX = s.halfExtentX - Math.Abs(x - s.centerX);
            double toEdgeZ = s.halfExtentZ - Math.Abs(z - s.centerZ);
            double toEdge = toEdgeX < toEdgeZ ? toEdgeX : toEdgeZ;
            double border = s.border > 1e-6 ? s.border : 1e-6;
            double t = toEdge / border;
            if (t <= 0.0)
            {
                return 0.0;
            }
            if (t >= 1.0)
            {
                return 1.0;
            }
            return Math.Pow(t, exponent);
        }

        /// <summary>Applies the falloff to a normalised noise value.</summary>
        public static double Apply(TerraFalloffSettings s, double x, double z, double value01)
        {
            if (s == null || s.mode == TerraFalloffMode.None)
            {
                return value01;
            }
            double w = Weight(s, x, z);
            return s.groundValue + (value01 - s.groundValue) * w;
        }
    }
}
