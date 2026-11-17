// ============================================================================
// NOTICE: Full documentation, design decisions, and fix history for this file
// live in docs/com.midmanstudio.proceduralterrain.md, section "Tests/EditorUnity/TerraUnityPureTests.cs"
// ============================================================================
using System;
using NUnit.Framework;
using UnityEngine;
using MidManStudio.ProceduralTerrain.EditorTools;

namespace MidManStudio.ProceduralTerrain.Tests
{
    /// <summary>
    /// Tests of the Unity layer that use no engine behaviour, only plain structs (Vector3, Color32, Bounds). They run in the
    /// Unity Test Runner and, against the stub of the Unity API, in the dotnet harness.
    /// </summary>
    [TestFixture]
    public class TerraUnityPureTests
    {
        private static TerraWorld MakeWorld()
        {
            TerraWorldSettings s = new TerraWorldSettings();
            s.seed = 31;
            s.height.noise.scale = 120.0;
            s.height.noise.remapMin = 0.29;
            s.height.noise.remapMax = 0.71;
            s.lattice.spacing = 4.0;
            s.lattice.cellsPerChunk = 16;
            return new TerraWorld(s);
        }

        [Test]
        public void Prepare_CopiesVerticesNormalsColoursAndBounds()
        {
            TerraMeshData data = MakeWorld().BuildChunk(2, -3, 1).mesh;
            TerraPreparedMesh p = TerraMeshConverter.Prepare(data);
            Assert.AreEqual(data.vertexCount, p.vertices.Length);
            Assert.AreEqual(data.vertexCount, p.normals.Length);
            Assert.AreEqual(data.vertexCount, p.colors.Length);
            Assert.IsTrue(p.indices == data.indices, "the index array is shared, not copied");
            Assert.IsFalse(p.uint32);
            for (int i = 0; i < data.vertexCount; i++)
            {
                Assert.IsTrue(p.vertices[i].x == data.positions[i * 3] && p.vertices[i].y == data.positions[i * 3 + 1]
                    && p.vertices[i].z == data.positions[i * 3 + 2], "a position differs at " + i);
                Assert.IsTrue(p.normals[i].x == data.normals[i * 3] && p.normals[i].y == data.normals[i * 3 + 1]
                    && p.normals[i].z == data.normals[i * 3 + 2], "a normal differs at " + i);
            }
            Assert.AreEqual((data.minX + data.maxX) * 0.5f, p.bounds.center.x, 1e-4);
            Assert.AreEqual(data.maxY - data.minY, p.bounds.size.y, 1e-4);
            Assert.AreEqual(data.maxZ - data.minZ, p.bounds.size.z, 1e-4);
        }

        [Test]
        public void Prepare_RoundsColoursToBytesAndKeepsAlpha()
        {
            TerraMeshData d = new TerraMeshData();
            d.vertexCount = 2;
            d.positions = new float[6];
            d.normals = new float[] { 0f, 1f, 0f, 0f, 1f, 0f };
            d.colors = new float[] { 1f, 0.5f, 0f, 1f, -0.5f, 2f, 0.2f, 0.5f };
            d.indices = new int[] { 0, 1, 0 };
            TerraPreparedMesh p = TerraMeshConverter.Prepare(d);
            Assert.AreEqual(255, (int)p.colors[0].r);
            Assert.AreEqual(128, (int)p.colors[0].g);
            Assert.AreEqual(0, (int)p.colors[0].b);
            Assert.AreEqual(255, (int)p.colors[0].a);
            Assert.AreEqual(0, (int)p.colors[1].r, "below 0 clamps to 0");
            Assert.AreEqual(255, (int)p.colors[1].g, "above 1 clamps to 255");
            Assert.AreEqual(51, (int)p.colors[1].b);
            Assert.AreEqual(128, (int)p.colors[1].a);
        }

        [Test]
        public void Prepare_FlagsMeshesThatNeedThirtyTwoBitIndices()
        {
            TerraMeshData d = new TerraMeshData();
            d.vertexCount = 70000;
            d.positions = new float[70000 * 3];
            d.normals = new float[70000 * 3];
            d.colors = new float[70000 * 4];
            d.indices = new int[] { 0, 1, 2 };
            Assert.IsTrue(TerraMeshConverter.Prepare(d).uint32);
            d.vertexCount = 65535;
            Assert.IsFalse(TerraMeshConverter.Prepare(d).uint32);
        }

        [Test]
        public void PatchImport_MapsGreyToHeightsAndKeepsTheRowOrder()
        {
            Color32[] px =
            {
                new Color32(0, 0, 0, 255), new Color32(255, 255, 255, 255),
                new Color32(255, 255, 255, 255), new Color32(0, 0, 0, 255)
            };
            TerraPatch patch = TerraPatchImport.FromColors(px, 2, 2, "test", 100.0, 200.0, 50.0, 50.0, 10f, 30f);
            Assert.AreEqual(2, patch.width);
            Assert.AreEqual(2, patch.height);
            Assert.AreEqual(4, patch.data.Length);
            Assert.AreEqual(0.0, patch.data[0], 1e-5);
            Assert.AreEqual(1.0, patch.data[1], 1e-5);
            Assert.AreEqual(1.0, patch.data[2], 1e-5);
            Assert.AreEqual(0.0, patch.data[3], 1e-5);
            // Row 0 is the south edge, so the south west corner is black (10 m) and the south east corner white (30 m).
            Assert.AreEqual(10.0, patch.SampleMetres(100.0, 200.0), 1e-3);
            Assert.AreEqual(30.0, patch.SampleMetres(150.0, 200.0), 1e-3);
            Assert.AreEqual(30.0, patch.SampleMetres(100.0, 250.0), 1e-3);
            Assert.AreEqual("test", patch.name);
        }

        [Test]
        public void PatchImport_UsesTheLumaOfColour()
        {
            Color32[] px =
            {
                new Color32(255, 0, 0, 255), new Color32(0, 255, 0, 255),
                new Color32(0, 0, 255, 255), new Color32(255, 255, 255, 255)
            };
            TerraPatch patch = TerraPatchImport.FromColors(px, 2, 2, "luma", 0.0, 0.0, 10.0, 10.0, 0f, 1f);
            Assert.AreEqual(0.299, patch.data[0], 1e-4);
            Assert.AreEqual(0.587, patch.data[1], 1e-4);
            Assert.AreEqual(0.114, patch.data[2], 1e-4);
        }

        [Test]
        public void PatchImport_RefusesAPictureThatIsTooSmallOrShort()
        {
            Assert.Throws<ArgumentException>(delegate { TerraPatchImport.FromColors(new Color32[1], 1, 1, "x", 0, 0, 1, 1, 0, 1); });
            Assert.Throws<ArgumentException>(delegate { TerraPatchImport.FromColors(new Color32[3], 2, 2, "x", 0, 0, 1, 1, 0, 1); });
            Assert.Throws<ArgumentNullException>(delegate { TerraPatchImport.FromColors(null, 2, 2, "x", 0, 0, 1, 1, 0, 1); });
        }
    }
}
