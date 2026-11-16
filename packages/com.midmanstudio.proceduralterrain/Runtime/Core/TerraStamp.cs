// ============================================================================
// NOTICE: Full documentation, design decisions, and fix history for this file
// live in docs/com.midmanstudio.proceduralterrain.md, section "Runtime/Core/TerraStamp.cs"
// ============================================================================
using System;

namespace MidManStudio.ProceduralTerrain
{
    /// <summary>A point in the ground plane in true metres.</summary>
    [Serializable]
    public struct TerraPoint
    {
        public double x;
        public double z;

        public TerraPoint(double x, double z)
        {
            this.x = x;
            this.z = z;
        }
    }

    public enum TerraStampKind
    {
        /// <summary>Flattens the ground inside a circle or box to one height and blends out over the falloff. A town floor.</summary>
        Pad = 0,
        /// <summary>A path along a polyline. The surface follows the terrain along the path and is level across it.</summary>
        Road = 1,
        /// <summary>Lowers the ground: a circle (a lake or a pit) or, with two or more points, a channel along a polyline (a river).</summary>
        Carve = 2,
        /// <summary>Raises the ground inside a circle or box with a soft edge. A hill, a plateau, a mound for a tower.</summary>
        Mound = 3
    }

    public enum TerraStampShape
    {
        Circle = 0,
        Box = 1
    }

    /// <summary>
    /// A deliberate change to the height field, in true coordinates. Stamps are applied in list order after the noise and the
    /// patches, so a later stamp sees the result of an earlier one. A stamp can also paint a colour over the ground it covers
    /// and keep scatter (trees, rocks) off it.
    /// </summary>
    [Serializable]
    public class TerraStamp
    {
        public string name = "Stamp";
        public bool enabled = true;
        public TerraStampKind kind = TerraStampKind.Pad;
        public TerraStampShape shape = TerraStampShape.Circle;

        /// <summary>The centre of a circle or box.</summary>
        public double x;
        public double z;
        public double radius = 40.0;
        public double halfX = 40.0;
        public double halfZ = 40.0;
        public double rotationDegrees;

        /// <summary>A road or a river channel follows these points.</summary>
        public TerraPoint[] points = new TerraPoint[0];

        /// <summary>The full width of a road or a channel.</summary>
        public double width = 6.0;

        /// <summary>The distance over which the effect fades out beyond the shape, in metres.</summary>
        public double falloff = 12.0;

        /// <summary>Pad: take the height of the terrain at the centre, before this stamp, as the target.</summary>
        public bool autoHeight = true;

        /// <summary>Pad: the target height when autoHeight is off.</summary>
        public double targetHeight;

        /// <summary>Added to the target height of a pad or a road. A road a little above the ground or sunk into it.</summary>
        public double heightOffset;

        /// <summary>Carve: how deep. Mound: how high. In metres.</summary>
        public double amount = 3.0;

        /// <summary>0..1, how much of the stamp is applied.</summary>
        public float strength = 1.0f;

        /// <summary>Paint a colour over the ground where the stamp's influence passes the threshold.</summary>
        public bool paint;
        public TerraColor paintColor = new TerraColor(0.55f, 0.45f, 0.33f, 1.0f);
        public float paintThreshold = 0.5f;

        /// <summary>Keep scatter off the ground this stamp covers.</summary>
        public bool blocksScatter = true;

        /// <summary>
        /// How strongly the stamp reaches a point, 0..1: 1 inside the shape, easing to 0 over the falloff. Also gives the nearest
        /// point of the shape's centre line, which a road reads its height from.
        /// </summary>
        public double Influence(double px, double pz, out double nearX, out double nearZ)
        {
            nearX = x;
            nearZ = z;
            double dist;
            bool polyline = kind == TerraStampKind.Road || (kind == TerraStampKind.Carve && points != null && points.Length >= 2);
            if (polyline)
            {
                if (points == null || points.Length < 2)
                {
                    return 0.0;
                }
                dist = TerraShape.PolylineDistance(px, pz, points, out nearX, out nearZ) - width * 0.5;
            }
            else if (shape == TerraStampShape.Box)
            {
                dist = TerraShape.BoxDistance(px, pz, x, z, halfX, halfZ, rotationDegrees);
            }
            else
            {
                double dx = px - x;
                double dz = pz - z;
                dist = Math.Sqrt(dx * dx + dz * dz) - radius;
            }
            return TerraShape.FalloffWeight(dist, falloff);
        }
    }

    /// <summary>How a patch combines with the ground under it.</summary>
    public enum TerraPatchMode
    {
        /// <summary>Blend the ground toward the patch height.</summary>
        Replace = 0,
        /// <summary>Add the patch height to the ground.</summary>
        Add = 1,
        /// <summary>Keep the higher of the two.</summary>
        Max = 2,
        /// <summary>Keep the lower of the two.</summary>
        Min = 3
    }

    /// <summary>
    /// A hand painted height image over a rectangle of the world. A grey-scale picture from any image editor, one number per
    /// pixel in 0..1, mapped to minHeight..maxHeight metres. This is the hand-made part of an otherwise generated landscape.
    /// </summary>
    [Serializable]
    public class TerraPatch
    {
        public string name = "Patch";
        public bool enabled = true;

        /// <summary>The south west corner and the size of the rectangle in true metres.</summary>
        public double minX;
        public double minZ;
        public double sizeX = 100.0;
        public double sizeZ = 100.0;

        /// <summary>The image size and its pixels, row by row from the minimum z, 0..1.</summary>
        public int width;
        public int height;
        public float[] data = new float[0];

        public float minHeight;
        public float maxHeight = 20.0f;

        public TerraPatchMode mode = TerraPatchMode.Replace;
        public float strength = 1.0f;

        /// <summary>The distance inward from the edge over which the patch fades into the ground, in metres.</summary>
        public double feather = 10.0;

        /// <summary>The weight of the patch at a point: 0 outside the rectangle, rising to 1 over the feather.</summary>
        public double Weight(double x, double z)
        {
            if (width < 1 || height < 1 || data == null || data.Length < width * height)
            {
                return 0.0;
            }
            double inset = Math.Min(Math.Min(x - minX, minX + sizeX - x), Math.Min(z - minZ, minZ + sizeZ - z));
            if (inset <= 0.0)
            {
                return 0.0;
            }
            if (feather <= 1e-9 || inset >= feather)
            {
                return 1.0;
            }
            double t = inset / feather;
            return t * t * (3.0 - 2.0 * t);
        }

        /// <summary>The patch height in metres at a point, bilinear between pixels.</summary>
        public double SampleMetres(double x, double z)
        {
            double u = sizeX > 1e-9 ? (x - minX) / sizeX : 0.0;
            double v = sizeZ > 1e-9 ? (z - minZ) / sizeZ : 0.0;
            u = u < 0.0 ? 0.0 : (u > 1.0 ? 1.0 : u);
            v = v < 0.0 ? 0.0 : (v > 1.0 ? 1.0 : v);
            double fx = u * (width - 1);
            double fz = v * (height - 1);
            int ix = (int)Math.Floor(fx);
            int iz = (int)Math.Floor(fz);
            if (ix >= width - 1)
            {
                ix = width - 2 < 0 ? 0 : width - 2;
            }
            if (iz >= height - 1)
            {
                iz = height - 2 < 0 ? 0 : height - 2;
            }
            int jx = ix + 1 < width ? ix + 1 : ix;
            int jz = iz + 1 < height ? iz + 1 : iz;
            double tx = fx - ix;
            double tz = fz - iz;
            double a = data[iz * width + ix];
            double b = data[iz * width + jx];
            double c = data[jz * width + ix];
            double d = data[jz * width + jx];
            double top = a + (b - a) * tx;
            double bottom = c + (d - c) * tx;
            double n = top + (bottom - top) * tz;
            return minHeight + (maxHeight - minHeight) * n;
        }
    }

    /// <summary>Distances to simple shapes, used by stamps.</summary>
    public static class TerraShape
    {
        /// <summary>The smoothstep of t clamped to 0..1.</summary>
        public static double Smooth(double t)
        {
            if (t <= 0.0)
            {
                return 0.0;
            }
            if (t >= 1.0)
            {
                return 1.0;
            }
            return t * t * (3.0 - 2.0 * t);
        }

        /// <summary>The weight for a signed distance to a shape (negative inside): 1 inside, easing to 0 across the falloff.</summary>
        public static double FalloffWeight(double signedDistance, double falloff)
        {
            if (signedDistance <= 0.0)
            {
                return 1.0;
            }
            if (falloff <= 1e-9)
            {
                return 0.0;
            }
            return 1.0 - Smooth(signedDistance / falloff);
        }

        /// <summary>The signed distance to a rotated box, negative inside.</summary>
        public static double BoxDistance(double px, double pz, double cx, double cz, double halfX, double halfZ, double rotationDegrees)
        {
            double dx = px - cx;
            double dz = pz - cz;
            if (rotationDegrees != 0.0)
            {
                double a = -rotationDegrees * Math.PI / 180.0;
                double c = Math.Cos(a);
                double s = Math.Sin(a);
                double rx = dx * c - dz * s;
                double rz = dx * s + dz * c;
                dx = rx;
                dz = rz;
            }
            double qx = Math.Abs(dx) - halfX;
            double qz = Math.Abs(dz) - halfZ;
            double ox = qx > 0.0 ? qx : 0.0;
            double oz = qz > 0.0 ? qz : 0.0;
            double outside = Math.Sqrt(ox * ox + oz * oz);
            double inside = Math.Min(Math.Max(qx, qz), 0.0);
            return outside + inside;
        }

        /// <summary>The distance from a point to a polyline, and the nearest point on it.</summary>
        public static double PolylineDistance(double px, double pz, TerraPoint[] pts, out double nearX, out double nearZ)
        {
            nearX = pts[0].x;
            nearZ = pts[0].z;
            double best = double.MaxValue;
            for (int i = 0; i + 1 < pts.Length; i++)
            {
                double ax = pts[i].x;
                double az = pts[i].z;
                double bx = pts[i + 1].x;
                double bz = pts[i + 1].z;
                double ex = bx - ax;
                double ez = bz - az;
                double len2 = ex * ex + ez * ez;
                double t = len2 > 1e-12 ? ((px - ax) * ex + (pz - az) * ez) / len2 : 0.0;
                t = t < 0.0 ? 0.0 : (t > 1.0 ? 1.0 : t);
                double qx = ax + ex * t;
                double qz = az + ez * t;
                double dx = px - qx;
                double dz = pz - qz;
                double d2 = dx * dx + dz * dz;
                if (d2 < best)
                {
                    best = d2;
                    nearX = qx;
                    nearZ = qz;
                }
            }
            return Math.Sqrt(best);
        }
    }
}
