// ============================================================================
// NOTICE: Full documentation, design decisions, and fix history for this file
// live in docs/com.midmanstudio.proceduralterrain.md, section "Runtime/Core/TerraTriangulation.cs"
// ============================================================================
using System;

namespace MidManStudio.ProceduralTerrain
{
    /// <summary>
    /// Points on the ground plane and the triangles between them. Every triangle is stored counter-clockwise in the (x, z) plane
    /// with x to the right and z up, which is the opposite of the winding Unity draws as a front face; the mesh builder swaps it.
    /// </summary>
    public sealed class TerraTriangulation
    {
        public readonly double[] x;
        public readonly double[] z;

        /// <summary>Three vertex indices per triangle.</summary>
        public readonly int[] triangles;

        public TerraTriangulation(double[] x, double[] z, int[] triangles)
        {
            this.x = x;
            this.z = z;
            this.triangles = triangles;
        }

        public int VertexCount
        {
            get { return x.Length; }
        }

        public int TriangleCount
        {
            get { return triangles.Length / 3; }
        }
    }

    /// <summary>Small planar geometry helpers shared by the triangulators.</summary>
    public static class TerraGeom
    {
        /// <summary>Twice the signed area of a, b, c. Positive when they run counter-clockwise with x right and z up.</summary>
        public static double Cross(double ax, double az, double bx, double bz, double cx, double cz)
        {
            return (bx - ax) * (cz - az) - (bz - az) * (cx - ax);
        }

        /// <summary>Positive when d is inside the circle through a, b, c, which must run counter-clockwise.</summary>
        public static double InCircle(double ax, double az, double bx, double bz, double cx, double cz, double dx, double dz)
        {
            double adx = ax - dx;
            double adz = az - dz;
            double bdx = bx - dx;
            double bdz = bz - dz;
            double cdx = cx - dx;
            double cdz = cz - dz;
            double ad = adx * adx + adz * adz;
            double bd = bdx * bdx + bdz * bdz;
            double cd = cdx * cdx + cdz * cdz;
            return ad * (bdx * cdz - cdx * bdz) - bd * (adx * cdz - cdx * adz) + cd * (adx * bdz - bdx * adz);
        }
    }
}
