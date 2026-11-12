// ============================================================================
// NOTICE: Full documentation, design decisions, and fix history for this file
// live in docs/com.midmanstudio.allthingsjiggle.md, section "Runtime/Core/JiggleFfd.cs"
// ============================================================================
namespace MidManStudio.AllThingsJiggle
{
    /// <summary>A 3 by 3 matrix of floats, row by row. It carries the rotation and scale part of a transform.</summary>
    public struct JiggleMat3
    {
        /// <summary>Row 0.</summary>
        public float M00, M01, M02;
        /// <summary>Row 1.</summary>
        public float M10, M11, M12;
        /// <summary>Row 2.</summary>
        public float M20, M21, M22;

        /// <summary>Makes a matrix from its nine entries, row by row.</summary>
        public JiggleMat3(float m00, float m01, float m02, float m10, float m11, float m12, float m20, float m21, float m22)
        {
            M00 = m00;
            M01 = m01;
            M02 = m02;
            M10 = m10;
            M11 = m11;
            M12 = m12;
            M20 = m20;
            M21 = m21;
            M22 = m22;
        }

        /// <summary>The matrix that changes nothing.</summary>
        public static JiggleMat3 Identity
        {
            get { return new JiggleMat3(1f, 0f, 0f, 0f, 1f, 0f, 0f, 0f, 1f); }
        }

        /// <summary>The vector this matrix turns <paramref name="v"/> into.</summary>
        public JVec3 Mul(JVec3 v)
        {
            return new JVec3(
                M00 * v.X + M01 * v.Y + M02 * v.Z,
                M10 * v.X + M11 * v.Y + M12 * v.Z,
                M20 * v.X + M21 * v.Y + M22 * v.Z);
        }

        /// <summary>
        /// The inverse of <paramref name="m"/>. Returns false and gives the identity when the matrix cannot be inverted, which
        /// is when it flattens space (a scale of 0) or holds a value that is not a number.
        /// </summary>
        public static bool TryInvert(in JiggleMat3 m, out JiggleMat3 inverse)
        {
            float c00 = m.M11 * m.M22 - m.M12 * m.M21;
            float c01 = m.M12 * m.M20 - m.M10 * m.M22;
            float c02 = m.M10 * m.M21 - m.M11 * m.M20;
            float det = m.M00 * c00 + m.M01 * c01 + m.M02 * c02;
            if (!JiggleMath.IsFinite(det) || !(JiggleMath.Abs(det) > 1.0e-20f))
            {
                inverse = Identity;
                return false;
            }
            float k = 1.0f / det;
            inverse = new JiggleMat3(
                c00 * k, (m.M02 * m.M21 - m.M01 * m.M22) * k, (m.M01 * m.M12 - m.M02 * m.M11) * k,
                c01 * k, (m.M00 * m.M22 - m.M02 * m.M20) * k, (m.M02 * m.M10 - m.M00 * m.M12) * k,
                c02 * k, (m.M01 * m.M20 - m.M00 * m.M21) * k, (m.M00 * m.M11 - m.M01 * m.M10) * k);
            return true;
        }
    }

    /// <summary>
    /// Free form deformation of positions by a lattice of control points (Sederberg and Parry, 1986), written on
    /// pointers with no managed allocation so a Burst job can run it. A position inside the box of the lattice has
    /// coordinates (s, t, u) between 0 and 1, and moves by the sum over the control points of the product of three
    /// Bernstein weights times the displacement of the point. Control points sit evenly in the box (see
    /// <see cref="JiggleLatticeGrid"/>), so a displacement that is a straight stretch or turn of the whole box is
    /// reproduced exactly, and any displacement of a position is a weighted average of the control displacements, so
    /// it is never larger than the largest of them. Positions outside the box take the displacement of the nearest
    /// point on its surface.
    /// </summary>
    public static unsafe class JiggleFfd
    {
        /// <summary>The most control points on an axis, which is the size of a weight array.</summary>
        public const int MaxAxis = JiggleLatticeGrid.MaxDivisions;

        /// <summary>
        /// The Bernstein weights of degree count - 1 at <paramref name="t"/>, count values written to <paramref name="w"/>.
        /// They are never negative and add up to 1. With one point the only weight is 1.
        /// </summary>
        public static void Weights(int count, float t, float* w)
        {
            w[0] = 1.0f;
            float u = 1.0f - t;
            for (int j = 1; j < count; j++)
            {
                float saved = 0.0f;
                for (int k = 0; k < j; k++)
                {
                    float tmp = w[k];
                    w[k] = saved + u * tmp;
                    saved = t * tmp;
                }
                w[j] = saved;
            }
        }

        /// <summary>
        /// The derivative of each weight of <see cref="Weights"/> with respect to t, count values written to
        /// <paramref name="dw"/>. They add up to 0. With one point the only derivative is 0.
        /// </summary>
        public static void Derivatives(int count, float t, float* dw)
        {
            if (count <= 1)
            {
                dw[0] = 0.0f;
                return;
            }
            // The degree one lower weights go into the same array, then each derivative is the difference of two of them.
            Weights(count - 1, t, dw);
            float degree = (float)(count - 1);
            for (int k = count - 1; k >= 0; k--)
            {
                float a = k > 0 ? dw[k - 1] : 0.0f;
                float b = k < count - 1 ? dw[k] : 0.0f;
                dw[k] = degree * (a - b);
            }
        }

        /// <summary>The reciprocal of a box size, or 0 for an axis with no size, so a flat axis has no effect and no division by zero.</summary>
        public static float InverseSize(float size)
        {
            return size > 1.0e-6f ? 1.0f / size : 0.0f;
        }

        /// <summary>The coordinate of <paramref name="p"/> along one axis of a box, between 0 and 1. A value that is not a number gives 0.</summary>
        public static float Coordinate(float p, float min, float inverseSize)
        {
            float t = (p - min) * inverseSize;
            if (!(t > 0.0f))
            {
                return 0.0f;
            }
            return t > 1.0f ? 1.0f : t;
        }

        /// <summary>
        /// Fits a box around <paramref name="count"/> positions, widened by <paramref name="padding"/> times the size of the
        /// positions on each side of an axis that has any size. Returns false, with a unit box at the origin, when there are
        /// no positions or one of them is not a finite number.
        /// </summary>
        public static bool FitBox(JVec3* positions, int count, float padding, out JVec3 min, out JVec3 size)
        {
            min = JVec3.Zero;
            size = new JVec3(1.0f, 1.0f, 1.0f);
            if (positions == null || count <= 0)
            {
                return false;
            }
            JVec3 lo = positions[0];
            JVec3 hi = positions[0];
            for (int i = 0; i < count; i++)
            {
                JVec3 p = positions[i];
                if (!p.IsFinite())
                {
                    return false;
                }
                if (p.X < lo.X) { lo.X = p.X; }
                if (p.Y < lo.Y) { lo.Y = p.Y; }
                if (p.Z < lo.Z) { lo.Z = p.Z; }
                if (p.X > hi.X) { hi.X = p.X; }
                if (p.Y > hi.Y) { hi.Y = p.Y; }
                if (p.Z > hi.Z) { hi.Z = p.Z; }
            }
            float pad = padding > 0.0f && JiggleMath.IsFinite(padding) ? padding : 0.0f;
            JVec3 ext = hi - lo;
            lo = new JVec3(lo.X - ext.X * pad, lo.Y - ext.Y * pad, lo.Z - ext.Z * pad);
            ext = new JVec3(ext.X * (1.0f + 2.0f * pad), ext.Y * (1.0f + 2.0f * pad), ext.Z * (1.0f + 2.0f * pad));
            min = lo;
            size = ext;
            return true;
        }

        /// <summary>
        /// The lattice coordinates of <paramref name="count"/> positions in a box, written to <paramref name="coords"/>. Each is
        /// between 0 and 1 on every axis, so a position outside the box is put on its surface.
        /// </summary>
        public static void Coordinates(JVec3* positions, int count, JVec3 min, JVec3 size, JVec3* coords)
        {
            float ix = InverseSize(size.X);
            float iy = InverseSize(size.Y);
            float iz = InverseSize(size.Z);
            for (int i = 0; i < count; i++)
            {
                JVec3 p = positions[i];
                coords[i] = new JVec3(
                    Coordinate(p.X, min.X, ix),
                    Coordinate(p.Y, min.Y, iy),
                    Coordinate(p.Z, min.Z, iz));
            }
        }

        /// <summary>
        /// The displacement of one position from the weights of its three coordinates and the displacements of the control
        /// points, which are indexed x + nx * (y + ny * z).
        /// </summary>
        public static JVec3 Displace(int nx, int ny, int nz, float* wx, float* wy, float* wz, JVec3* d)
        {
            float ax = 0.0f;
            float ay = 0.0f;
            float az = 0.0f;
            for (int k = 0; k < nz; k++)
            {
                float wk = wz[k];
                for (int j = 0; j < ny; j++)
                {
                    float wjk = wy[j] * wk;
                    JVec3* row = d + nx * (j + ny * k);
                    for (int i = 0; i < nx; i++)
                    {
                        float w = wx[i] * wjk;
                        ax += row[i].X * w;
                        ay += row[i].Y * w;
                        az += row[i].Z * w;
                    }
                }
            }
            return new JVec3(ax, ay, az);
        }

        /// <summary>
        /// The displacement of one position and its derivative with respect to each coordinate: gx is the change of the
        /// displacement for a change of s, gy for t and gz for u. The weights and the derivatives of the weights of the three
        /// coordinates go in.
        /// </summary>
        public static void DisplaceWithGradient(
            int nx, int ny, int nz, float* wx, float* wy, float* wz, float* dwx, float* dwy, float* dwz, JVec3* d,
            out JVec3 value, out JVec3 gx, out JVec3 gy, out JVec3 gz)
        {
            float vx = 0.0f, vy = 0.0f, vz = 0.0f;
            float xx = 0.0f, xy = 0.0f, xz = 0.0f;
            float yx = 0.0f, yy = 0.0f, yz = 0.0f;
            float zx = 0.0f, zy = 0.0f, zz = 0.0f;
            for (int k = 0; k < nz; k++)
            {
                float wk = wz[k];
                float dk = dwz[k];
                for (int j = 0; j < ny; j++)
                {
                    float wjk = wy[j] * wk;
                    float djk = dwy[j] * wk;
                    float wjd = wy[j] * dk;
                    JVec3* row = d + nx * (j + ny * k);
                    for (int i = 0; i < nx; i++)
                    {
                        float w = wx[i] * wjk;
                        float sx = dwx[i] * wjk;
                        float sy = wx[i] * djk;
                        float sz = wx[i] * wjd;
                        JVec3 c = row[i];
                        vx += c.X * w; vy += c.Y * w; vz += c.Z * w;
                        xx += c.X * sx; xy += c.Y * sx; xz += c.Z * sx;
                        yx += c.X * sy; yy += c.Y * sy; yz += c.Z * sy;
                        zx += c.X * sz; zy += c.Y * sz; zz += c.Z * sz;
                    }
                }
            }
            value = new JVec3(vx, vy, vz);
            gx = new JVec3(xx, xy, xz);
            gy = new JVec3(yx, yy, yz);
            gz = new JVec3(zx, zy, zz);
        }

        /// <summary>
        /// The normal after a deformation whose derivative has the columns a, b and c: the normal times the inverse transpose
        /// of that matrix, which is what keeps a normal perpendicular to the surface when the surface is stretched. It is
        /// computed from the cofactors, so no division is needed. A matrix that turns space inside out is handled, and a
        /// normal that collapses is kept as it was.
        /// </summary>
        public static JVec3 TransformNormal(JVec3 a, JVec3 b, JVec3 c, JVec3 n)
        {
            JVec3 bc = JVec3.Cross(b, c);
            JVec3 ca = JVec3.Cross(c, a);
            JVec3 ab = JVec3.Cross(a, b);
            float det = JVec3.Dot(a, bc);
            JVec3 r = bc * n.X + ca * n.Y + ab * n.Z;
            if (det < 0.0f)
            {
                r = -r;
            }
            return r.NormalizeSafe(n);
        }

        /// <summary>
        /// Deforms positions first to first + count of a mesh. <paramref name="coords"/> are their lattice coordinates and
        /// <paramref name="basePos"/> their positions at rest, <paramref name="d"/> the displacements of the control points in
        /// the space of the mesh, and <paramref name="size"/> the size of the box. When <paramref name="baseNormal"/> and
        /// <paramref name="outNormal"/> are not null the normals are carried along too. The ranges of different calls do not
        /// touch, so they may run on different threads.
        /// </summary>
        public static void DeformRange(
            int first, int count, int nx, int ny, int nz, JVec3 size, JVec3* coords, JVec3* d,
            JVec3* basePos, JVec3* baseNormal, JVec3* outPos, JVec3* outNormal)
        {
            bool normals = baseNormal != null && outNormal != null;
            float* scratch = stackalloc float[6 * MaxAxis];
            float* wx = scratch;
            float* wy = scratch + MaxAxis;
            float* wz = scratch + 2 * MaxAxis;
            float* dx = scratch + 3 * MaxAxis;
            float* dy = scratch + 4 * MaxAxis;
            float* dz = scratch + 5 * MaxAxis;
            float ix = InverseSize(size.X);
            float iy = InverseSize(size.Y);
            float iz = InverseSize(size.Z);
            int end = first + count;
            for (int v = first; v < end; v++)
            {
                JVec3 c = coords[v];
                Weights(nx, c.X, wx);
                Weights(ny, c.Y, wy);
                Weights(nz, c.Z, wz);
                if (!normals)
                {
                    outPos[v] = basePos[v] + Displace(nx, ny, nz, wx, wy, wz, d);
                    continue;
                }
                Derivatives(nx, c.X, dx);
                Derivatives(ny, c.Y, dy);
                Derivatives(nz, c.Z, dz);
                JVec3 value, gx, gy, gz;
                DisplaceWithGradient(nx, ny, nz, wx, wy, wz, dx, dy, dz, d, out value, out gx, out gy, out gz);
                outPos[v] = basePos[v] + value;
                JVec3 a = new JVec3(1.0f + gx.X * ix, gx.Y * ix, gx.Z * ix);
                JVec3 b = new JVec3(gy.X * iy, 1.0f + gy.Y * iy, gy.Z * iy);
                JVec3 cc = new JVec3(gz.X * iz, gz.Y * iz, 1.0f + gz.Z * iz);
                outNormal[v] = TransformNormal(a, b, cc, baseNormal[v]);
            }
        }

        /// <summary>
        /// The displacement of each control point in the space of the mesh, from the output of the solver. The displacement of
        /// a particle is where the solver put it minus where the pose has it, both measured from the anchor, so a lattice that
        /// follows its pose exactly has none. <paramref name="output"/> and <paramref name="poseCurr"/> point at the first
        /// particle of the rig, <paramref name="particleOfPoint"/> gives the particle of each grid point, and
        /// <paramref name="worldToLocal"/> turns a vector of the world into one of the mesh.
        /// </summary>
        public static void ControlDisplacement(
            JiggleParticleOut* output, JVec4* poseCurr, int* particleOfPoint, int count, in JiggleMat3 worldToLocal, JVec3* result)
        {
            for (int i = 0; i < count; i++)
            {
                int p = particleOfPoint[i];
                JVec3 world = JVec3.From4(output[p].Pos) - JVec3.From4(poseCurr[p]);
                result[i] = worldToLocal.Mul(world);
            }
        }

        /// <summary>The length of the longest of <paramref name="count"/> vectors, 0 for none.</summary>
        public static float MaxLength(JVec3* v, int count)
        {
            float best = 0.0f;
            for (int i = 0; i < count; i++)
            {
                float l = v[i].LengthSq();
                if (l > best)
                {
                    best = l;
                }
            }
            return JiggleMath.Sqrt(best);
        }
    }
}
