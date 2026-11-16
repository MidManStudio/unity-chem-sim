// ============================================================================
// NOTICE: Full documentation, design decisions, and fix history for this file
// live in docs/com.midmanstudio.proceduralterrain.md, section "Runtime/Core/TerraPoisson.cs"
// ============================================================================
using System;
using System.Collections.Generic;

namespace MidManStudio.ProceduralTerrain
{
    /// <summary>Poisson-disc sampling: scattered points no closer to each other than a radius, with no visible pattern.</summary>
    public static class TerraPoisson
    {
        /// <summary>
        /// Points over a rectangle by Bridson's method. The border of the rectangle gets fixed points at least one radius apart
        /// (the corners included) so a triangulation of the result covers the rectangle exactly. Every point, the border ones
        /// too, is at least one radius from every other, except along the border where they may be a little closer or further.
        /// The result depends on the seed and the arguments only.
        /// </summary>
        public static void Generate(
            ulong seed, double minX, double minZ, double maxX, double maxZ,
            double radius, int attempts, out double[] xs, out double[] zs)
        {
            if (!(maxX > minX) || !(maxZ > minZ))
            {
                throw new ArgumentException("the rectangle has no area");
            }
            if (radius < 1e-3)
            {
                radius = 1e-3;
            }
            if (attempts < 4)
            {
                attempts = 4;
            }
            double width = maxX - minX;
            double depth = maxZ - minZ;
            double cell = radius / Math.Sqrt(2.0);
            int gw = (int)Math.Ceiling(width / cell) + 1;
            int gh = (int)Math.Ceiling(depth / cell) + 1;
            if ((long)gw * gh > 40000000L)
            {
                throw new ArgumentException("the radius is too small for this rectangle");
            }
            int[] grid = new int[gw * gh];
            for (int i = 0; i < grid.Length; i++)
            {
                grid[i] = -1;
            }
            List<double> px = new List<double>();
            List<double> pz = new List<double>();
            List<int> active = new List<int>();
            TerraRng rng = new TerraRng(seed);

            // Border points, one radius apart or a little more.
            int nx = Math.Max(1, (int)Math.Floor(width / radius));
            int nz = Math.Max(1, (int)Math.Floor(depth / radius));
            for (int i = 0; i < nx; i++)
            {
                double x = minX + width * i / nx;
                AddFixed(px, pz, active, grid, gw, cell, minX, minZ, x, minZ);
                AddFixed(px, pz, active, grid, gw, cell, minX, minZ, maxX - width * i / nx, maxZ);
            }
            for (int j = 0; j < nz; j++)
            {
                double z = minZ + depth * j / nz;
                AddFixed(px, pz, active, grid, gw, cell, minX, minZ, maxX, z);
                AddFixed(px, pz, active, grid, gw, cell, minX, minZ, minX, maxZ - depth * j / nz);
            }

            // One free point to start inside, so a rectangle that is nothing but border still gets an interior.
            for (int tries = 0; tries < 30; tries++)
            {
                double x = minX + rng.NextDouble() * width;
                double z = minZ + rng.NextDouble() * depth;
                if (Fits(px, pz, grid, gw, gh, cell, minX, minZ, radius, x, z))
                {
                    Add(px, pz, active, grid, gw, cell, minX, minZ, x, z);
                    break;
                }
            }

            while (active.Count > 0)
            {
                int pick = rng.NextInt(active.Count);
                int index = active[pick];
                double cx = px[index];
                double cz = pz[index];
                bool placed = false;
                for (int k = 0; k < attempts; k++)
                {
                    double angle = rng.NextDouble() * 2.0 * Math.PI;
                    double dist = radius * (1.0 + rng.NextDouble());
                    double x = cx + Math.Cos(angle) * dist;
                    double z = cz + Math.Sin(angle) * dist;
                    if (x <= minX || x >= maxX || z <= minZ || z >= maxZ)
                    {
                        continue;
                    }
                    if (Fits(px, pz, grid, gw, gh, cell, minX, minZ, radius, x, z))
                    {
                        Add(px, pz, active, grid, gw, cell, minX, minZ, x, z);
                        placed = true;
                        break;
                    }
                }
                if (!placed)
                {
                    active[pick] = active[active.Count - 1];
                    active.RemoveAt(active.Count - 1);
                }
            }
            xs = px.ToArray();
            zs = pz.ToArray();
        }

        private static int CellIndex(int gw, double cell, double minX, double minZ, double x, double z)
        {
            int gx = (int)((x - minX) / cell);
            int gz = (int)((z - minZ) / cell);
            return gz * gw + gx;
        }

        private static void Add(List<double> px, List<double> pz, List<int> active, int[] grid, int gw, double cell,
            double minX, double minZ, double x, double z)
        {
            int index = px.Count;
            px.Add(x);
            pz.Add(z);
            active.Add(index);
            grid[CellIndex(gw, cell, minX, minZ, x, z)] = index;
        }

        // A border point is kept even when its cell is taken: the grid is only a search aid for the free points.
        private static void AddFixed(List<double> px, List<double> pz, List<int> active, int[] grid, int gw, double cell,
            double minX, double minZ, double x, double z)
        {
            int index = px.Count;
            px.Add(x);
            pz.Add(z);
            active.Add(index);
            int cellIndex = CellIndex(gw, cell, minX, minZ, x, z);
            if (grid[cellIndex] < 0)
            {
                grid[cellIndex] = index;
            }
        }

        private static bool Fits(List<double> px, List<double> pz, int[] grid, int gw, int gh, double cell,
            double minX, double minZ, double radius, double x, double z)
        {
            int gx = (int)((x - minX) / cell);
            int gz = (int)((z - minZ) / cell);
            double r2 = radius * radius;
            for (int dz = -2; dz <= 2; dz++)
            {
                int zz = gz + dz;
                if (zz < 0 || zz >= gh)
                {
                    continue;
                }
                for (int dx = -2; dx <= 2; dx++)
                {
                    int xx = gx + dx;
                    if (xx < 0 || xx >= gw)
                    {
                        continue;
                    }
                    int other = grid[zz * gw + xx];
                    if (other < 0)
                    {
                        continue;
                    }
                    double ex = px[other] - x;
                    double ez = pz[other] - z;
                    if (ex * ex + ez * ez < r2)
                    {
                        return false;
                    }
                }
            }
            return true;
        }
    }
}
