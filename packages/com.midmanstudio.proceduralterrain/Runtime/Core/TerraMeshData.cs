// ============================================================================
// NOTICE: Full documentation, design decisions, and fix history for this file
// live in docs/com.midmanstudio.proceduralterrain.md, section "Runtime/Core/TerraMeshData.cs"
// ============================================================================
using System;

namespace MidManStudio.ProceduralTerrain
{
    /// <summary>
    /// A finished mesh as plain arrays, with no Unity types, so it can be built on a worker thread and tested outside Unity.
    /// Positions are relative to <see cref="originX"/> and <see cref="originZ"/> in x and z so they stay small and precise
    /// far from the world origin; y is the true height. Triangles use the winding Unity draws as front faces. Colours are in
    /// gamma space (the colours you picked); the shader converts them.
    /// </summary>
    public sealed class TerraMeshData
    {
        /// <summary>Three floats per vertex.</summary>
        public float[] positions = new float[0];

        /// <summary>Three floats per vertex.</summary>
        public float[] normals = new float[0];

        /// <summary>Four floats per vertex: red, green, blue, alpha.</summary>
        public float[] colors = new float[0];

        public int[] indices = new int[0];

        public int vertexCount;

        /// <summary>The true position of the local origin in metres.</summary>
        public double originX;
        public double originZ;

        /// <summary>The bounds of the vertices in local coordinates.</summary>
        public float minX;
        public float minY;
        public float minZ;
        public float maxX;
        public float maxY;
        public float maxZ;

        public int TriangleCount
        {
            get { return indices.Length / 3; }
        }

        /// <summary>True when a 16 bit index buffer cannot address every vertex.</summary>
        public bool RequiresUInt32
        {
            get { return vertexCount > 65535; }
        }
    }

    /// <summary>Collects vertices and triangles into a <see cref="TerraMeshData"/>.</summary>
    public sealed class TerraMeshBuilder
    {
        private readonly double originX;
        private readonly double originZ;
        private float[] positions;
        private float[] normals;
        private float[] colors;
        private int[] indices;
        private int vertexCount;
        private int indexCount;

        private bool hasBorder;
        private double borderMinX;
        private double borderMaxX;
        private double borderMinZ;
        private double borderMaxZ;
        private double[] skirts = new double[0];
        private int skirtCount;

        public TerraMeshBuilder(double originX, double originZ, int vertexCapacity)
        {
            this.originX = originX;
            this.originZ = originZ;
            int cap = vertexCapacity < 16 ? 16 : vertexCapacity;
            positions = new float[cap * 3];
            normals = new float[cap * 3];
            colors = new float[cap * 4];
            indices = new int[cap * 2];
        }

        public int VertexCount
        {
            get { return vertexCount; }
        }

        /// <summary>
        /// Sets the rectangle the mesh is cut to. Any triangle edge that lies exactly along one of its four sides is remembered
        /// so <see cref="AddSkirts"/> can hang a wall from it.
        /// </summary>
        public void SetBorder(double minX, double maxX, double minZ, double maxZ)
        {
            hasBorder = true;
            borderMinX = minX;
            borderMaxX = maxX;
            borderMinZ = minZ;
            borderMaxZ = maxZ;
        }

        /// <summary>Adds a vertex and returns its index.</summary>
        public int AddVertex(double x, double y, double z, double nx, double ny, double nz, TerraColor color)
        {
            if (vertexCount * 3 + 3 > positions.Length)
            {
                int size = positions.Length * 2;
                Array.Resize(ref positions, size);
                Array.Resize(ref normals, size);
                Array.Resize(ref colors, size / 3 * 4);
            }
            int i3 = vertexCount * 3;
            int i4 = vertexCount * 4;
            positions[i3] = (float)(x - originX);
            positions[i3 + 1] = (float)y;
            positions[i3 + 2] = (float)(z - originZ);
            normals[i3] = (float)nx;
            normals[i3 + 1] = (float)ny;
            normals[i3 + 2] = (float)nz;
            colors[i4] = color.r;
            colors[i4 + 1] = color.g;
            colors[i4 + 2] = color.b;
            colors[i4 + 3] = color.a;
            return vertexCount++;
        }

        /// <summary>Adds a triangle between three vertices that already exist, in the order given (front face winding).</summary>
        public void AddIndexed(int a, int b, int c)
        {
            if (indexCount + 3 > indices.Length)
            {
                Array.Resize(ref indices, indices.Length * 2);
            }
            indices[indexCount++] = a;
            indices[indexCount++] = b;
            indices[indexCount++] = c;
        }

        /// <summary>
        /// Adds one flat shaded triangle with three vertices of its own. The corners may come in either order: the triangle is
        /// turned so its face looks toward the hint direction. A triangle with no area is dropped. Returns false when dropped.
        /// </summary>
        public bool AddFlatTriangle(
            double ax, double ay, double az, double bx, double by, double bz, double cx, double cy, double cz,
            TerraColor color, double hintX, double hintY, double hintZ)
        {
            double ux = bx - ax;
            double uy = by - ay;
            double uz = bz - az;
            double vx = cx - ax;
            double vy = cy - ay;
            double vz = cz - az;
            double nx = uy * vz - uz * vy;
            double ny = uz * vx - ux * vz;
            double nz = ux * vy - uy * vx;
            double len = Math.Sqrt(nx * nx + ny * ny + nz * nz);
            if (len < 1e-9)
            {
                return false;
            }
            if (nx * hintX + ny * hintY + nz * hintZ < 0.0)
            {
                double tx = bx;
                double ty = by;
                double tz = bz;
                bx = cx;
                by = cy;
                bz = cz;
                cx = tx;
                cy = ty;
                cz = tz;
                nx = -nx;
                ny = -ny;
                nz = -nz;
            }
            nx /= len;
            ny /= len;
            nz /= len;
            int i0 = AddVertex(ax, ay, az, nx, ny, nz, color);
            int i1 = AddVertex(bx, by, bz, nx, ny, nz, color);
            int i2 = AddVertex(cx, cy, cz, nx, ny, nz, color);
            AddIndexed(i0, i1, i2);
            return true;
        }

        /// <summary>Remembers an edge of the mesh for the skirts when it lies exactly along a side of the border.</summary>
        public void NoteEdge(double ax, double ay, double az, double bx, double by, double bz, TerraColor color)
        {
            if (!hasBorder)
            {
                return;
            }
            double ox = 0.0;
            double oz = 0.0;
            if (ax == borderMinX && bx == borderMinX)
            {
                ox = -1.0;
            }
            else if (ax == borderMaxX && bx == borderMaxX)
            {
                ox = 1.0;
            }
            else if (az == borderMinZ && bz == borderMinZ)
            {
                oz = -1.0;
            }
            else if (az == borderMaxZ && bz == borderMaxZ)
            {
                oz = 1.0;
            }
            else
            {
                return;
            }
            if (skirtCount * 12 + 12 > skirts.Length)
            {
                Array.Resize(ref skirts, Math.Max(96, skirts.Length * 2));
            }
            int k = skirtCount * 12;
            skirts[k] = ax;
            skirts[k + 1] = ay;
            skirts[k + 2] = az;
            skirts[k + 3] = bx;
            skirts[k + 4] = by;
            skirts[k + 5] = bz;
            skirts[k + 6] = ox;
            skirts[k + 7] = oz;
            skirts[k + 8] = color.r;
            skirts[k + 9] = color.g;
            skirts[k + 10] = color.b;
            skirts[k + 11] = color.a;
            skirtCount++;
        }

        /// <summary>
        /// Hangs a wall of the given depth in metres under every remembered border edge, facing outward. Where two chunks of
        /// different detail meet, the wall of the finer one hides the crack between them.
        /// </summary>
        public void AddSkirts(double depth)
        {
            if (depth <= 0.0)
            {
                skirtCount = 0;
                return;
            }
            for (int s = 0; s < skirtCount; s++)
            {
                int k = s * 12;
                TerraColor color = new TerraColor((float)skirts[k + 8], (float)skirts[k + 9], (float)skirts[k + 10], (float)skirts[k + 11]);
                double hx = skirts[k + 6];
                double hz = skirts[k + 7];
                double ax = skirts[k];
                double ay = skirts[k + 1];
                double az = skirts[k + 2];
                double bx = skirts[k + 3];
                double by = skirts[k + 4];
                double bz = skirts[k + 5];
                AddFlatTriangle(ax, ay, az, bx, by, bz, bx, by - depth, bz, color, hx, 0.0, hz);
                AddFlatTriangle(ax, ay, az, bx, by - depth, bz, ax, ay - depth, az, color, hx, 0.0, hz);
            }
            skirtCount = 0;
        }

        public TerraMeshData ToMeshData()
        {
            TerraMeshData mesh = new TerraMeshData();
            mesh.vertexCount = vertexCount;
            mesh.originX = originX;
            mesh.originZ = originZ;
            mesh.positions = new float[vertexCount * 3];
            mesh.normals = new float[vertexCount * 3];
            mesh.colors = new float[vertexCount * 4];
            mesh.indices = new int[indexCount];
            Array.Copy(positions, mesh.positions, vertexCount * 3);
            Array.Copy(normals, mesh.normals, vertexCount * 3);
            Array.Copy(colors, mesh.colors, vertexCount * 4);
            Array.Copy(indices, mesh.indices, indexCount);
            if (vertexCount > 0)
            {
                float minX = float.MaxValue;
                float minY = float.MaxValue;
                float minZ = float.MaxValue;
                float maxX = float.MinValue;
                float maxY = float.MinValue;
                float maxZ = float.MinValue;
                for (int i = 0; i < vertexCount; i++)
                {
                    float x = positions[i * 3];
                    float y = positions[i * 3 + 1];
                    float z = positions[i * 3 + 2];
                    minX = Math.Min(minX, x);
                    minY = Math.Min(minY, y);
                    minZ = Math.Min(minZ, z);
                    maxX = Math.Max(maxX, x);
                    maxY = Math.Max(maxY, y);
                    maxZ = Math.Max(maxZ, z);
                }
                mesh.minX = minX;
                mesh.minY = minY;
                mesh.minZ = minZ;
                mesh.maxX = maxX;
                mesh.maxY = maxY;
                mesh.maxZ = maxZ;
            }
            return mesh;
        }
    }
}
