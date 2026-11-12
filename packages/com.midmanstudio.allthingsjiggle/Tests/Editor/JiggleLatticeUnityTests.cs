// ============================================================================
// NOTICE: Full documentation, design decisions, and fix history for this file
// live in docs/com.midmanstudio.allthingsjiggle.md, section "Tests/Editor/JiggleLatticeUnityTests.cs"
// ============================================================================
using NUnit.Framework;
using UnityEngine;

namespace MidManStudio.AllThingsJiggle.EditorTests
{
    /// <summary>
    /// Tests of the Unity layer of the lattice that need the editor and no scene: the settings, the box of a mesh, the control
    /// job and the mesh data with the deform job, run on hand made arrays. The transform jobs, the manager and the upload every
    /// frame are tried in the lab (MidManStudio, AllThingsJiggle, Jiggle Lab, Lattice). The Core of the lattice is tested by
    /// JiggleLatticeTests, which also runs outside Unity.
    /// </summary>
    public sealed unsafe class JiggleLatticeUnityTests
    {
        [Test]
        public void DefaultSettingsAreTheMeasuredOnes()
        {
            JiggleLatticeSettings s = new JiggleLatticeSettings();
            Assert.AreEqual(0.4f, s.stiffness, 1e-6f);
            Assert.AreEqual(0.2f, s.drag, 1e-6f);
            Assert.AreEqual(0.3f, s.stretch, 1e-6f);
            Assert.AreEqual(0f, s.gravity, 1e-6f);
            Assert.AreEqual(0f, s.airDrag, 1e-6f);
            Assert.AreEqual(1f, s.mass, 1e-6f);
            Assert.AreEqual(1f, s.windResponse, 1e-6f);
            Assert.AreEqual(1f, s.impulseResponse, 1e-6f);

            JiggleParams p = s.ToParams();
            Assert.AreEqual(0.4f, p.Stiffness, 1e-6f);
            Assert.AreEqual(0.2f, p.Drag, 1e-6f);
            Assert.AreEqual(0f, p.RootStretch, 1e-6f);
            Assert.AreEqual(1f, p.Mass, 1e-6f);
            Assert.IsFalse(p.Collide, "a lattice does not collide");
            Assert.IsFalse(p.AngleLimited);
        }

        [Test]
        public void ToParamsGivesOneSetForEveryParticleAfterTheAnchor()
        {
            JiggleLatticeSettings s = new JiggleLatticeSettings();
            s.stiffness = 0.7f;
            JiggleParams[] all = s.ToParams(26);
            Assert.AreEqual(26, all.Length);
            for (int i = 0; i < all.Length; i++)
            {
                Assert.AreEqual(0.7f, all[i].Stiffness, 1e-6f);
            }
            Assert.AreEqual(0, s.ToParams(-3).Length, "a negative count is no particles");
        }

        [Test]
        public void TheBoxIsTheBoundsWidenedByThePadding()
        {
            Bounds b = new Bounds(new Vector3(1f, 2f, 3f), new Vector3(2f, 4f, 6f));
            JVec3 min;
            JVec3 size;
            JiggleLatticeMesh.BoxOf(b, 0f, out min, out size);
            Assert.AreEqual(0f, min.X, 1e-5f);
            Assert.AreEqual(0f, min.Y, 1e-5f);
            Assert.AreEqual(0f, min.Z, 1e-5f);
            Assert.AreEqual(2f, size.X, 1e-5f);
            Assert.AreEqual(4f, size.Y, 1e-5f);
            Assert.AreEqual(6f, size.Z, 1e-5f);

            JiggleLatticeMesh.BoxOf(b, 0.5f, out min, out size);
            Assert.AreEqual(-1f, min.X, 1e-5f);
            Assert.AreEqual(-2f, min.Y, 1e-5f);
            Assert.AreEqual(-3f, min.Z, 1e-5f);
            Assert.AreEqual(4f, size.X, 1e-5f);
            Assert.AreEqual(8f, size.Y, 1e-5f);
            Assert.AreEqual(12f, size.Z, 1e-5f);

            JiggleLatticeMesh.BoxOf(b, 7f, out min, out size);
            Assert.AreEqual(6f, size.X, 1e-5f, "the padding stops at 1");
            JiggleLatticeMesh.BoxOf(b, float.NaN, out min, out size);
            Assert.AreEqual(2f, size.X, 1e-5f, "a padding that is not a number is 0");
        }

        [Test]
        public void TheControlJobMeasuresInTheSpaceOfTheMeshAndSkipsAFrameThatChangesNothing()
        {
            JiggleRig* rigs = JiggleMem.Alloc<JiggleRig>(1);
            JiggleParticleOut* output = JiggleMem.Alloc<JiggleParticleOut>(2);
            JVec4* pose = JiggleMem.Alloc<JVec4>(2);
            JiggleSample* samples = JiggleMem.Alloc<JiggleSample>(1);
            int* entryTransform = JiggleMem.Alloc<int>(1);
            JiggleLatticeData* data = JiggleMem.Alloc<JiggleLatticeData>(1);
            int* particleOfPoint = JiggleMem.Alloc<int>(2);
            JVec3* d = JiggleMem.Alloc<JVec3>(2);
            JVec3* lastD = JiggleMem.Alloc<JVec3>(2);
            try
            {
                // Two control points. The object is scaled by 2, so a world displacement of 0.5 is 0.25 in the space of the mesh.
                pose[1] = new JVec4(1f, 0f, 0f, 0f);
                output[1].Pos = new JVec4(1f, 0.5f, 0f, 0f);
                samples[0].M00 = 2f;
                samples[0].M11 = 2f;
                samples[0].M22 = 2f;
                particleOfPoint[0] = 0;
                particleOfPoint[1] = 1;
                data[0].Rig = 0;
                data[0].Entry = 0;
                data[0].Points = 2;
                data[0].Epsilon = 1e-4f;
                data[0].ParticleOfPoint = particleOfPoint;
                data[0].D = d;
                data[0].LastD = lastD;

                JiggleLatticeControlJob job = new JiggleLatticeControlJob();
                job.Lattices = data;
                job.Rigs = rigs;
                job.Out = output;
                job.PoseCurr = pose;
                job.Samples = samples;
                job.EntryTransform = entryTransform;

                job.Execute(0);
                Assert.AreEqual(0, data[0].Skip, "the first frame moved a control point");
                Assert.AreEqual(0f, d[0].Y, 1e-6f);
                Assert.AreEqual(0.25f, d[1].Y, 1e-6f);
                Assert.AreEqual(0.25f, data[0].MaxLength, 1e-6f);
                Assert.AreEqual(0.25f, lastD[1].Y, 1e-6f, "what the mesh shows is remembered");

                job.Execute(0);
                Assert.AreEqual(1, data[0].Skip, "the same displacement again changes nothing, so nothing is deformed or uploaded");

                output[1].Pos = new JVec4(1f, 0f, 0f, 0f);
                job.Execute(0);
                Assert.AreEqual(0, data[0].Skip, "the return to rest is shown");
                Assert.AreEqual(0f, data[0].MaxLength, 1e-6f);
                job.Execute(0);
                Assert.AreEqual(1, data[0].Skip, "and then nothing more is done at rest");

                output[1].Pos = new JVec4(1f, float.NaN, 0f, 0f);
                job.Execute(0);
                Assert.AreEqual(1, data[0].Skip, "a displacement that is not a number is never shown");
                Assert.AreEqual(0f, lastD[1].Y, 1e-6f);
            }
            finally
            {
                JiggleMem.Free(ref rigs);
                JiggleMem.Free(ref output);
                JiggleMem.Free(ref pose);
                JiggleMem.Free(ref samples);
                JiggleMem.Free(ref entryTransform);
                JiggleMem.Free(ref data);
                JiggleMem.Free(ref particleOfPoint);
                JiggleMem.Free(ref d);
                JiggleMem.Free(ref lastD);
            }
        }

        [Test]
        public void TheMeshDataCopiesTheMeshDeformsItAndGivesTheMeshBack()
        {
            GameObject go = new GameObject("Jiggle lattice test");
            try
            {
                MeshFilter filter = go.AddComponent<MeshFilter>();
                Mesh quad = new Mesh();
                quad.name = "test quad";
                quad.vertices = new Vector3[]
                {
                    new Vector3(-1f, -1f, 0f), new Vector3(1f, -1f, 0f), new Vector3(-1f, 1f, 0f), new Vector3(1f, 1f, 0f),
                };
                quad.normals = new Vector3[]
                {
                    new Vector3(0f, 0f, -1f), new Vector3(0f, 0f, -1f), new Vector3(0f, 0f, -1f), new Vector3(0f, 0f, -1f),
                };
                quad.triangles = new int[] { 0, 2, 1, 2, 3, 1 };
                quad.RecalculateBounds();
                filter.sharedMesh = quad;

                string problem;
                JiggleLatticeMesh m = JiggleLatticeMesh.Create(filter, 0f, out problem);
                Assert.IsNotNull(m, problem);
                Assert.AreEqual(4, m.Vertices);
                Assert.IsTrue(m.HasNormals);
                Assert.IsTrue(filter.sharedMesh == m.Instance, "the filter shows the private copy");
                Assert.IsFalse(filter.sharedMesh == quad, "and not the original");
                Assert.AreEqual(2f, m.Size.X, 1e-5f);
                Assert.AreEqual(2f, m.Size.Y, 1e-5f);
                Assert.AreEqual(0f, m.Size.Z, 1e-5f);

                // Two by two control points on the flat quad, every one moved by 1 along z. A straight move of the whole box is
                // reproduced exactly, so every vertex ends up at z = 1.
                JiggleLatticeData* data = JiggleMem.Alloc<JiggleLatticeData>(1);
                try
                {
                    for (int i = 0; i < 4; i++)
                    {
                        m.D[i] = new JVec3(0f, 0f, 1f);
                    }
                    data[0].Nx = 2;
                    data[0].Ny = 2;
                    data[0].Nz = 1;
                    data[0].Points = 4;
                    data[0].Vertices = 4;
                    data[0].Size = m.Size;
                    data[0].D = m.D;
                    data[0].Coords = m.Coords;
                    data[0].BasePos = m.BasePos;
                    data[0].BaseNormal = m.BaseNormal;
                    data[0].OutPos = m.OutPosPtr();
                    data[0].OutNormal = m.OutNormalPtr();
                    data[0].Skip = 0;
                    int* chunkLattice = JiggleMem.Alloc<int>(1);
                    int* chunkFirst = JiggleMem.Alloc<int>(1);
                    try
                    {
                        JiggleLatticeDeformJob deform = new JiggleLatticeDeformJob();
                        deform.Lattices = data;
                        deform.ChunkLattice = chunkLattice;
                        deform.ChunkFirst = chunkFirst;
                        deform.Execute(0);
                    }
                    finally
                    {
                        JiggleMem.Free(ref chunkLattice);
                        JiggleMem.Free(ref chunkFirst);
                    }
                }
                finally
                {
                    JiggleMem.Free(ref data);
                }
                m.Upload(1f);

                Vector3[] moved = m.Instance.vertices;
                Vector3[] at = quad.vertices;
                for (int i = 0; i < 4; i++)
                {
                    Assert.AreEqual(at[i].x, moved[i].x, 1e-5f);
                    Assert.AreEqual(at[i].y, moved[i].y, 1e-5f);
                    Assert.AreEqual(1f, moved[i].z, 1e-5f);
                }
                Assert.AreEqual(0f, quad.vertices[0].z, 1e-6f, "the original mesh is never touched");
                Assert.IsTrue(m.Instance.bounds.extents.z >= 1f - 1e-5f, "the bounds of the copy reach the moved vertices");

                m.Dispose();
                Assert.IsTrue(filter.sharedMesh == quad, "the original is back on the filter");
                Object.DestroyImmediate(quad);
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void ARefusedMeshGivesAReasonAndLeavesTheFilterAlone()
        {
            GameObject go = new GameObject("Jiggle lattice test");
            try
            {
                MeshFilter filter = go.AddComponent<MeshFilter>();
                string problem;
                JiggleLatticeMesh m = JiggleLatticeMesh.Create(filter, 0f, out problem);
                Assert.IsTrue(m == null, "no mesh, no lattice");
                Assert.IsNotNull(problem);
                Assert.IsTrue(filter.sharedMesh == null);
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }
    }
}
