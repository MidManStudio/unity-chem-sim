// ============================================================================
// NOTICE: Full documentation, design decisions, and fix history for this file
// live in docs/com.midmanstudio.proceduralterrain.md, section "Runtime/Core/TerraDelaunay.cs"
// ============================================================================
using System;
using System.Collections.Generic;

namespace MidManStudio.ProceduralTerrain
{
    /// <summary>
    /// Delaunay triangulation of points on a plane by the Bowyer-Watson method: each point is inserted by removing the triangles
    /// whose circumscribed circle contains it and joining the hole to the point. Written for this package, so there is no
    /// third party licence to carry. Neighbour links make finding the triangle of a point a short walk and finding the hole a
    /// flood fill, so thousands of points take well under a second.
    /// </summary>
    public static class TerraDelaunay
    {
        /// <summary>
        /// Triangulates the points and returns triangles as index triples, each counter-clockwise (positive
        /// <see cref="TerraGeom.Cross"/>). Points must be distinct. The result covers the convex hull of the points; when the
        /// points include the four corners of a rectangle and the points along its edges, that is the rectangle.
        /// </summary>
        public static int[] Triangulate(double[] xs, double[] zs)
        {
            if (xs == null || zs == null || xs.Length != zs.Length)
            {
                throw new ArgumentException("xs and zs must be the same length");
            }
            if (xs.Length < 3)
            {
                return new int[0];
            }
            Mesh mesh = new Mesh(xs, zs);
            return mesh.Run();
        }

        private sealed class Mesh
        {
            private readonly int count;
            private readonly double[] px;
            private readonly double[] pz;

            // Per triangle: three vertices (counter-clockwise) and, for edge e from vertex e to vertex e + 1, the neighbour across it.
            private int[] tv;
            private int[] tn;
            private bool[] alive;
            private int[] mark;
            private int triCount;
            private readonly Stack<int> free = new Stack<int>();
            private int stamp;
            private int last;

            public Mesh(double[] xs, double[] zs)
            {
                count = xs.Length;
                px = new double[count + 3];
                pz = new double[count + 3];
                Array.Copy(xs, px, count);
                Array.Copy(zs, pz, count);
                tv = new int[3 * 64];
                tn = new int[3 * 64];
                alive = new bool[64];
                mark = new int[64];
            }

            public int[] Run()
            {
                double minX = double.MaxValue;
                double maxX = double.MinValue;
                double minZ = double.MaxValue;
                double maxZ = double.MinValue;
                for (int i = 0; i < count; i++)
                {
                    minX = Math.Min(minX, px[i]);
                    maxX = Math.Max(maxX, px[i]);
                    minZ = Math.Min(minZ, pz[i]);
                    maxZ = Math.Max(maxZ, pz[i]);
                }
                double extent = Math.Max(maxX - minX, maxZ - minZ);
                if (extent < 1e-9)
                {
                    return new int[0];
                }
                double cx = (minX + maxX) * 0.5;
                double cz = (minZ + maxZ) * 0.5;
                double m = extent * 60.0;
                px[count] = cx - m;
                pz[count] = cz - m;
                px[count + 1] = cx + m;
                pz[count + 1] = cz - m;
                px[count + 2] = cx;
                pz[count + 2] = cz + 2.0 * m;
                last = NewTriangle(count, count + 1, count + 2, -1, -1, -1);

                List<int> bad = new List<int>();
                List<int> stack = new List<int>();
                List<int> ea = new List<int>();
                List<int> eb = new List<int>();
                List<int> eo = new List<int>();
                Dictionary<int, int> byStart = new Dictionary<int, int>();

                for (int p = 0; p < count; p++)
                {
                    double x = px[p];
                    double z = pz[p];
                    int start = Locate(x, z);
                    if (start < 0)
                    {
                        continue;
                    }
                    stamp++;
                    bad.Clear();
                    stack.Clear();
                    Mark(start, bad, stack);
                    while (true)
                    {
                        while (stack.Count > 0)
                        {
                            int t = stack[stack.Count - 1];
                            stack.RemoveAt(stack.Count - 1);
                            for (int e = 0; e < 3; e++)
                            {
                                int nb = tn[t * 3 + e];
                                if (nb < 0 || mark[nb] == stamp)
                                {
                                    continue;
                                }
                                if (InCircumcircle(nb, x, z))
                                {
                                    Mark(nb, bad, stack);
                                }
                            }
                        }
                        // The hole must be star shaped around the point. A boundary edge the point is not strictly left of
                        // means the circle test was fooled by rounding, so the triangle behind that edge joins the hole.
                        bool grew = false;
                        for (int k = 0; k < bad.Count; k++)
                        {
                            int t = bad[k];
                            for (int e = 0; e < 3; e++)
                            {
                                int nb = tn[t * 3 + e];
                                if (nb >= 0 && mark[nb] == stamp)
                                {
                                    continue;
                                }
                                int a = tv[t * 3 + e];
                                int b = tv[t * 3 + (e + 1) % 3];
                                if (TerraGeom.Cross(px[a], pz[a], px[b], pz[b], x, z) <= 1e-12 && nb >= 0)
                                {
                                    Mark(nb, bad, stack);
                                    grew = true;
                                }
                            }
                        }
                        if (!grew)
                        {
                            break;
                        }
                    }

                    ea.Clear();
                    eb.Clear();
                    eo.Clear();
                    for (int k = 0; k < bad.Count; k++)
                    {
                        int t = bad[k];
                        for (int e = 0; e < 3; e++)
                        {
                            int nb = tn[t * 3 + e];
                            if (nb >= 0 && mark[nb] == stamp)
                            {
                                continue;
                            }
                            ea.Add(tv[t * 3 + e]);
                            eb.Add(tv[t * 3 + (e + 1) % 3]);
                            eo.Add(nb);
                        }
                    }
                    for (int k = 0; k < bad.Count; k++)
                    {
                        Release(bad[k]);
                    }

                    byStart.Clear();
                    int firstNew = -1;
                    int[] created = new int[ea.Count];
                    for (int k = 0; k < ea.Count; k++)
                    {
                        // The new triangle is (a, b, p): edge 0 is the old boundary edge, edges 1 and 2 touch the point.
                        int t = NewTriangle(ea[k], eb[k], p, eo[k], -1, -1);
                        created[k] = t;
                        byStart[ea[k]] = t;
                        if (firstNew < 0)
                        {
                            firstNew = t;
                        }
                        int outside = eo[k];
                        if (outside >= 0)
                        {
                            // Point the triangle on the far side of the edge at the new one.
                            for (int e = 0; e < 3; e++)
                            {
                                if (tv[outside * 3 + e] == eb[k] && tv[outside * 3 + (e + 1) % 3] == ea[k])
                                {
                                    tn[outside * 3 + e] = t;
                                    break;
                                }
                            }
                        }
                    }
                    // Edge 1 of a new triangle runs from b to p. Across it lies the new triangle that starts at b, and that
                    // triangle's edge 2 (from p to its start) is the mirror.
                    for (int k = 0; k < ea.Count; k++)
                    {
                        int t = created[k];
                        int across;
                        if (byStart.TryGetValue(eb[k], out across))
                        {
                            tn[t * 3 + 1] = across;
                            tn[across * 3 + 2] = t;
                        }
                    }
                    last = firstNew >= 0 ? firstNew : last;
                }

                List<int> result = new List<int>();
                for (int t = 0; t < triCount; t++)
                {
                    if (!alive[t])
                    {
                        continue;
                    }
                    int a = tv[t * 3];
                    int b = tv[t * 3 + 1];
                    int c = tv[t * 3 + 2];
                    if (a >= count || b >= count || c >= count)
                    {
                        continue;
                    }
                    result.Add(a);
                    result.Add(b);
                    result.Add(c);
                }
                return result.ToArray();
            }

            private void Mark(int t, List<int> bad, List<int> stack)
            {
                mark[t] = stamp;
                bad.Add(t);
                stack.Add(t);
            }

            private bool InCircumcircle(int t, double x, double z)
            {
                int a = tv[t * 3];
                int b = tv[t * 3 + 1];
                int c = tv[t * 3 + 2];
                return TerraGeom.InCircle(px[a], pz[a], px[b], pz[b], px[c], pz[c], x, z) > 0.0;
            }

            private int NewTriangle(int a, int b, int c, int n0, int n1, int n2)
            {
                int t;
                if (free.Count > 0)
                {
                    t = free.Pop();
                }
                else
                {
                    t = triCount++;
                    if (t >= alive.Length)
                    {
                        int size = alive.Length * 2;
                        Array.Resize(ref alive, size);
                        Array.Resize(ref mark, size);
                        Array.Resize(ref tv, size * 3);
                        Array.Resize(ref tn, size * 3);
                    }
                }
                alive[t] = true;
                mark[t] = 0;
                tv[t * 3] = a;
                tv[t * 3 + 1] = b;
                tv[t * 3 + 2] = c;
                tn[t * 3] = n0;
                tn[t * 3 + 1] = n1;
                tn[t * 3 + 2] = n2;
                return t;
            }

            private void Release(int t)
            {
                alive[t] = false;
                free.Push(t);
            }

            // Walks from the last triangle toward the point. The walk steps across any edge the point is to the right of.
            private int Locate(double x, double z)
            {
                int t = last;
                if (t < 0 || t >= triCount || !alive[t])
                {
                    t = FirstAlive();
                }
                int limit = triCount * 3 + 64;
                for (int step = 0; step < limit; step++)
                {
                    bool moved = false;
                    for (int e = 0; e < 3; e++)
                    {
                        int a = tv[t * 3 + e];
                        int b = tv[t * 3 + (e + 1) % 3];
                        if (TerraGeom.Cross(px[a], pz[a], px[b], pz[b], x, z) < 0.0)
                        {
                            int nb = tn[t * 3 + e];
                            if (nb < 0)
                            {
                                return t;
                            }
                            t = nb;
                            moved = true;
                            break;
                        }
                    }
                    if (!moved)
                    {
                        return t;
                    }
                }
                // The walk went round in a circle on a degenerate input: fall back to a scan for a triangle whose circle holds the point.
                for (int i = 0; i < triCount; i++)
                {
                    if (alive[i] && InCircumcircle(i, x, z))
                    {
                        return i;
                    }
                }
                return -1;
            }

            private int FirstAlive()
            {
                for (int i = 0; i < triCount; i++)
                {
                    if (alive[i])
                    {
                        return i;
                    }
                }
                return -1;
            }
        }
    }
}
