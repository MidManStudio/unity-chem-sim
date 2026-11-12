// ============================================================================
// NOTICE: Full documentation, design decisions, and fix history for this file
// live in docs/com.midmanstudio.allthingsjiggle.md, section "Runtime/Unity/JiggleLatticeJobs.cs"
// ============================================================================
using System;
using System.Collections.Generic;
using Unity.Burst;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;

namespace MidManStudio.AllThingsJiggle
{
    /// <summary>
    /// Everything the lattice jobs read and write for one lattice of a build of the manager: where its rig is, its grid, and
    /// pointers into the buffers of its <see cref="JiggleLatticeMesh"/>. Plain unmanaged fields, so a Burst job can hold an
    /// array of them.
    /// </summary>
    internal unsafe struct JiggleLatticeData
    {
        /// <summary>Index of the rig in the host.</summary>
        public int Rig;
        /// <summary>The entry of the build whose transform is the object of the lattice, for its matrix.</summary>
        public int Entry;
        /// <summary>Control points along x, y and z.</summary>
        public int Nx, Ny, Nz;
        /// <summary>Control points in all.</summary>
        public int Points;
        /// <summary>Vertices of the mesh.</summary>
        public int Vertices;
        /// <summary>Size of the box.</summary>
        public JVec3 Size;
        /// <summary>A change of the displacement of every control point smaller than this does not move the mesh, in the space of the mesh.</summary>
        public float Epsilon;
        /// <summary>Per grid point, the particle of the rig that simulates it.</summary>
        public int* ParticleOfPoint;
        /// <summary>The control displacements of this frame.</summary>
        public JVec3* D;
        /// <summary>The control displacements the mesh shows.</summary>
        public JVec3* LastD;
        /// <summary>Lattice coordinates of the vertices.</summary>
        public JVec3* Coords;
        /// <summary>Vertex positions at rest.</summary>
        public JVec3* BasePos;
        /// <summary>Vertex normals at rest, null without normals.</summary>
        public JVec3* BaseNormal;
        /// <summary>Moved vertex positions.</summary>
        public JVec3* OutPos;
        /// <summary>Moved vertex normals, null without normals.</summary>
        public JVec3* OutNormal;
        /// <summary>Written by the control job: the length of the largest control displacement the mesh is moved to.</summary>
        public float MaxLength;
        /// <summary>Written by the control job: 1 when the mesh already shows this frame, so nothing is deformed or uploaded.</summary>
        public int Skip;
    }

    /// <summary>
    /// Turns the output of the solver into control displacements, one lattice per job index: how far each control point is from
    /// where the pose has it, measured in the space of the mesh. A frame that changes nothing the eye can see is marked to be
    /// skipped, so a lattice at rest, or asleep, costs no deformation and no upload.
    /// </summary>
    [BurstCompile(FloatMode = FloatMode.Strict, FloatPrecision = FloatPrecision.Standard)]
    internal unsafe struct JiggleLatticeControlJob : IJobParallelFor
    {
        [NativeDisableUnsafePtrRestriction] public JiggleLatticeData* Lattices;
        [NativeDisableUnsafePtrRestriction] public JiggleRig* Rigs;
        [NativeDisableUnsafePtrRestriction] public JiggleParticleOut* Out;
        [NativeDisableUnsafePtrRestriction] public JVec4* PoseCurr;
        [NativeDisableUnsafePtrRestriction] public JiggleSample* Samples;
        [NativeDisableUnsafePtrRestriction] public int* EntryTransform;

        public void Execute(int index)
        {
            JiggleLatticeData* l = Lattices + index;
            l->Skip = 1;
            int off = (int)Rigs[l->Rig].ParticleOffset;
            JiggleSample* s = Samples + EntryTransform[l->Entry];
            JiggleMat3 linear = new JiggleMat3(s->M00, s->M01, s->M02, s->M10, s->M11, s->M12, s->M20, s->M21, s->M22);
            JiggleMat3 inverse;
            JiggleMat3.TryInvert(linear, out inverse);
            JiggleFfd.ControlDisplacement(Out + off, PoseCurr + off, l->ParticleOfPoint, l->Points, inverse, l->D);

            float change = 0.0f;
            for (int i = 0; i < l->Points; i++)
            {
                JVec3 d = l->D[i];
                if (!d.IsFinite())
                {
                    return;
                }
                float c = (d - l->LastD[i]).LengthSq();
                if (c > change)
                {
                    change = c;
                }
            }
            if (change <= l->Epsilon * l->Epsilon)
            {
                return;
            }
            l->MaxLength = JiggleFfd.MaxLength(l->D, l->Points);
            for (int i = 0; i < l->Points; i++)
            {
                l->LastD[i] = l->D[i];
            }
            l->Skip = 0;
        }
    }

    /// <summary>Moves the vertices of every lattice that is not skipped, one chunk of vertices per job index.</summary>
    [BurstCompile(FloatMode = FloatMode.Strict, FloatPrecision = FloatPrecision.Standard)]
    internal unsafe struct JiggleLatticeDeformJob : IJobParallelFor
    {
        /// <summary>Vertices one job index moves.</summary>
        public const int ChunkSize = 256;

        [NativeDisableUnsafePtrRestriction] public JiggleLatticeData* Lattices;
        [NativeDisableUnsafePtrRestriction] public int* ChunkLattice;
        [NativeDisableUnsafePtrRestriction] public int* ChunkFirst;

        public void Execute(int chunk)
        {
            JiggleLatticeData* l = Lattices + ChunkLattice[chunk];
            if (l->Skip != 0)
            {
                return;
            }
            int first = ChunkFirst[chunk];
            int count = l->Vertices - first;
            if (count > ChunkSize)
            {
                count = ChunkSize;
            }
            if (count <= 0)
            {
                return;
            }
            JiggleFfd.DeformRange(
                first, count, l->Nx, l->Ny, l->Nz, l->Size, l->Coords, l->D, l->BasePos, l->BaseNormal, l->OutPos, l->OutNormal);
        }
    }

    /// <summary>
    /// The lattices of one build of the manager as the jobs see them. Made by <see cref="JiggleRuntime"/> from the lattices it
    /// accepted, it schedules the control job and the deform job after the solver, and after they are complete gives the
    /// moved vertices to the meshes on the main thread. It does not own the mesh buffers: they belong to the components.
    /// </summary>
    internal sealed unsafe class JiggleLatticeBatch : IDisposable
    {
        private readonly JiggleLatticeMesh[] meshes;
        private JiggleLatticeData* data;
        private int* particleOfPoint;
        private int* chunkLattice;
        private int* chunkFirst;
        private readonly int count;
        private readonly int chunks;
        private bool disposed;

        /// <summary>
        /// Makes the batch of <paramref name="lattices"/>, which must all have been prepared. <paramref name="rigs"/> and
        /// <paramref name="entries"/> give, for each, its rig in the host and its first entry of the runtime.
        /// </summary>
        internal JiggleLatticeBatch(List<JiggleLattice> lattices, List<int> rigs, List<int> entries)
        {
            count = lattices.Count;
            meshes = new JiggleLatticeMesh[count];
            int points = 0;
            int total = 0;
            for (int i = 0; i < count; i++)
            {
                meshes[i] = lattices[i].MeshData;
                points += lattices[i].Tree.Count;
                total += (meshes[i].Vertices + JiggleLatticeDeformJob.ChunkSize - 1) / JiggleLatticeDeformJob.ChunkSize;
            }
            chunks = total;
            data = JiggleMem.Alloc<JiggleLatticeData>(count);
            particleOfPoint = JiggleMem.Alloc<int>(points);
            chunkLattice = JiggleMem.Alloc<int>(total);
            chunkFirst = JiggleMem.Alloc<int>(total);

            int pointCursor = 0;
            int chunkCursor = 0;
            for (int i = 0; i < count; i++)
            {
                JiggleLatticeTree tree = lattices[i].Tree;
                JiggleLatticeMesh m = meshes[i];
                JiggleLatticeData l = new JiggleLatticeData();
                l.Rig = rigs[i];
                l.Entry = entries[i];
                l.Nx = tree.Nx;
                l.Ny = tree.Ny;
                l.Nz = tree.Nz;
                l.Points = tree.Count;
                l.Vertices = m.Vertices;
                l.Size = m.Size;
                float largest = m.Size.X;
                if (m.Size.Y > largest)
                {
                    largest = m.Size.Y;
                }
                if (m.Size.Z > largest)
                {
                    largest = m.Size.Z;
                }
                l.Epsilon = largest * 1.0e-5f;
                l.ParticleOfPoint = particleOfPoint + pointCursor;
                for (int k = 0; k < tree.Count; k++)
                {
                    particleOfPoint[pointCursor + k] = tree.ParticleOfPoint[k];
                }
                pointCursor += tree.Count;
                l.D = m.D;
                l.LastD = m.LastD;
                l.Coords = m.Coords;
                l.BasePos = m.BasePos;
                l.BaseNormal = m.BaseNormal;
                l.OutPos = m.OutPosPtr();
                l.OutNormal = m.OutNormalPtr();
                l.MaxLength = 0.0f;
                l.Skip = 1;
                data[i] = l;
                for (int first = 0; first < m.Vertices; first += JiggleLatticeDeformJob.ChunkSize)
                {
                    chunkLattice[chunkCursor] = i;
                    chunkFirst[chunkCursor] = first;
                    chunkCursor++;
                }
            }
        }

        /// <summary>The number of lattices.</summary>
        internal int Count
        {
            get { return count; }
        }

        /// <summary>
        /// Schedules the control job and then the deform job behind <paramref name="dependsOn"/>, which must be the handle
        /// of the solver, and returns the handle of the last. The arrays are the ones of the runtime the batch belongs to.
        /// </summary>
        internal JobHandle Schedule(JiggleHost host, JiggleSample* samples, int* entryTransform, JobHandle dependsOn)
        {
            if (disposed || count == 0)
            {
                return dependsOn;
            }
            JiggleLatticeControlJob control = new JiggleLatticeControlJob();
            control.Lattices = data;
            control.Rigs = host.Rigs;
            control.Out = host.Out;
            control.PoseCurr = host.PoseCurr;
            control.Samples = samples;
            control.EntryTransform = entryTransform;
            JobHandle jh = control.Schedule(count, 1, dependsOn);
            JiggleLatticeDeformJob deform = new JiggleLatticeDeformJob();
            deform.Lattices = data;
            deform.ChunkLattice = chunkLattice;
            deform.ChunkFirst = chunkFirst;
            return deform.Schedule(chunks, 1, jh);
        }

        /// <summary>
        /// Gives the moved vertices to every mesh whose frame was not skipped. Call it on the main thread once the jobs of the
        /// frame are complete. Returns how many meshes were uploaded.
        /// </summary>
        internal int Apply()
        {
            if (disposed)
            {
                return 0;
            }
            int uploads = 0;
            for (int i = 0; i < count; i++)
            {
                if (data[i].Skip != 0)
                {
                    continue;
                }
                meshes[i].Upload(data[i].MaxLength);
                uploads++;
            }
            return uploads;
        }

        /// <summary>Frees the arrays of the batch. The meshes are not touched.</summary>
        public void Dispose()
        {
            if (disposed)
            {
                return;
            }
            disposed = true;
            JiggleMem.Free(ref data);
            JiggleMem.Free(ref particleOfPoint);
            JiggleMem.Free(ref chunkLattice);
            JiggleMem.Free(ref chunkFirst);
        }
    }
}
