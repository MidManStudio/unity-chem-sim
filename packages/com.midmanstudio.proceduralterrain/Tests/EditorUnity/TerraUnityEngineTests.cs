// ============================================================================
// NOTICE: Full documentation, design decisions, and fix history for this file
// live in docs/com.midmanstudio.proceduralterrain.md, section "Tests/EditorUnity/TerraUnityEngineTests.cs"
// ============================================================================
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using MidManStudio.ProceduralTerrain.EditorTools;

namespace MidManStudio.ProceduralTerrain.Tests
{
    /// <summary>
    /// Edit Mode tests that need the real engine: Mesh, GameObject, ScriptableObject. They compile against the stubs in CI but
    /// only run in the Unity Test Runner, so they have not run yet.
    /// </summary>
    [TestFixture]
    public class TerraUnityEngineTests
    {
        private static TerraWorldSettings SmallWorld()
        {
            TerraWorldSettings s = new TerraWorldSettings();
            s.seed = 31;
            s.height.noise.scale = 120.0;
            s.height.noise.remapMin = 0.29;
            s.height.noise.remapMax = 0.71;
            s.lattice.spacing = 4.0;
            s.lattice.cellsPerChunk = 16;
            s.scatter = new TerraScatterLayer[] { TerraPresets.Trees(0.2f, 0.9f) };
            return s;
        }

        private static TerraWorldProfile MakeProfile()
        {
            TerraWorldProfile profile = ScriptableObject.CreateInstance<TerraWorldProfile>();
            profile.settings = SmallWorld();
            profile.stream.loadDistance = 100.0;
            profile.stream.unloadMargin = 20.0;
            profile.colliders = false;
            return profile;
        }

        private static TerraStreamer MakeStreamer(TerraWorldProfile profile, out GameObject go)
        {
            go = new GameObject("Test Streamer");
            TerraStreamer streamer = go.AddComponent<TerraStreamer>();
            streamer.profile = profile;
            streamer.workerThreads = 0;
            streamer.syncBuildsPerFrame = 1000;
            streamer.maxInFlight = 1000;
            streamer.applyPerFrame = 1000;
            streamer.scatterPerFrame = 100000;
            streamer.Restart();
            return streamer;
        }

        [Test]
        public void ToMesh_CopiesEveryVertexAndIndex()
        {
            TerraMeshData data = new TerraWorld(SmallWorld()).BuildChunk(1, 1, 1).mesh;
            Mesh mesh = TerraMeshConverter.ToMesh(data);
            try
            {
                Assert.AreEqual(data.vertexCount, mesh.vertexCount);
                Assert.AreEqual(IndexFormat.UInt16, mesh.indexFormat);
                int[] triangles = mesh.triangles;
                Assert.AreEqual(data.indices.Length, triangles.Length);
                for (int i = 0; i < triangles.Length; i++)
                {
                    Assert.AreEqual(data.indices[i], triangles[i]);
                }
                Vector3[] vertices = mesh.vertices;
                Assert.AreEqual(data.positions[3], vertices[1].x);
                Assert.AreEqual(data.positions[4], vertices[1].y);
                Assert.AreEqual(data.vertexCount, mesh.colors32.Length);
            }
            finally
            {
                Object.DestroyImmediate(mesh);
            }
        }

        [Test]
        public void ToMesh_AcceptsMoreThanSixtyFiveThousandVertices()
        {
            TerraMeshData d = new TerraMeshData();
            d.vertexCount = 70000;
            d.positions = new float[70000 * 3];
            d.normals = new float[70000 * 3];
            d.colors = new float[70000 * 4];
            d.indices = new int[] { 0, 1, 2 };
            Mesh mesh = TerraMeshConverter.ToMesh(d);
            try
            {
                Assert.AreEqual(IndexFormat.UInt32, mesh.indexFormat);
                Assert.AreEqual(70000, mesh.vertexCount);
            }
            finally
            {
                Object.DestroyImmediate(mesh);
            }
        }

        [Test]
        public void Profile_MakesAWorldFromItsSettings()
        {
            TerraWorldProfile profile = ScriptableObject.CreateInstance<TerraWorldProfile>();
            try
            {
                Assert.IsNotNull(profile.settings);
                Assert.IsNotNull(profile.stream);
                TerraWorld world = profile.CreateWorld();
                Assert.IsNotNull(world);
                double h = world.HeightAt(10.0, 20.0);
                Assert.IsFalse(double.IsNaN(h));
            }
            finally
            {
                Object.DestroyImmediate(profile);
            }
        }

        [Test]
        public void Streamer_ShowsTheChunksAroundTheViewerAndLetsTheFarOnesGo()
        {
            TerraWorldProfile profile = MakeProfile();
            GameObject go;
            TerraStreamer streamer = MakeStreamer(profile, out go);
            try
            {
                streamer.Tick(32.0, 32.0, 1f);
                TerraStreamPlan expected = new TerraStreamPlan();
                new TerraStreamPlanner(streamer.World, profile.stream).Plan(32.0, 32.0, null, expected);
                Assert.IsTrue(expected.build.Count > 4);
                Assert.AreEqual(expected.build.Count, streamer.LoadedChunkCount);
                Assert.IsTrue(streamer.IsSettled);

                TerraChunk chunk;
                Assert.IsTrue(streamer.TryGetChunk(0, 0, out chunk));
                Assert.IsNotNull(chunk.Mesh);
                Assert.IsTrue(chunk.Mesh.vertexCount > 0);
                Assert.AreEqual(1, chunk.step);

                // Far away: the old ring is let go, a new one is built.
                streamer.Tick(100000.0, 100000.0, 1f);
                Assert.IsFalse(streamer.TryGetChunk(0, 0, out chunk));
                Assert.AreEqual(expected.build.Count, streamer.LoadedChunkCount);
            }
            finally
            {
                streamer.Shutdown();
                Object.DestroyImmediate(go);
                Object.DestroyImmediate(profile);
            }
        }

        [Test]
        public void Streamer_FollowsAShiftOfTheWorldOrigin()
        {
            TerraWorldProfile profile = MakeProfile();
            GameObject go;
            TerraStreamer streamer = MakeStreamer(profile, out go);
            try
            {
                streamer.Tick(32.0, 32.0, 1f);
                TerraChunk a;
                TerraChunk b;
                Assert.IsTrue(streamer.TryGetChunk(0, 0, out a));
                Assert.IsTrue(streamer.TryGetChunk(1, 0, out b));
                Assert.AreEqual(0f, a.transform.position.x);
                Assert.AreEqual(64f, b.transform.position.x);

                streamer.SetWorldOrigin(64.0, 0.0);
                Assert.AreEqual(-64f, a.transform.position.x);
                Assert.AreEqual(0f, b.transform.position.x);

                streamer.ShiftWorldOrigin(-64.0, 32.0);
                Assert.AreEqual(0.0, streamer.WorldOriginX);
                Assert.AreEqual(32.0, streamer.WorldOriginZ);
                Assert.AreEqual(-32f, a.transform.position.z);

                Vector3 p = streamer.WorldToScene(100.0, 5.0, 100.0);
                Assert.AreEqual(100f, p.x);
                Assert.AreEqual(5f, p.y);
                Assert.AreEqual(68f, p.z);
                double x;
                double z;
                streamer.SceneToWorld(p, out x, out z);
                Assert.AreEqual(100.0, x, 1e-4);
                Assert.AreEqual(100.0, z, 1e-4);
            }
            finally
            {
                streamer.Shutdown();
                Object.DestroyImmediate(go);
                Object.DestroyImmediate(profile);
            }
        }

        [Test]
        public void Streamer_PlacesAPrefabOnEveryScatterPointOfABoundLayer()
        {
            TerraWorldProfile profile = MakeProfile();
            GameObject prefab = new GameObject("Tree");
            TerraScatterBinding binding = new TerraScatterBinding();
            binding.layer = profile.settings.scatter[0].name;
            binding.variants = new GameObject[] { prefab };
            profile.scatterBindings = new TerraScatterBinding[] { binding };
            GameObject go;
            TerraStreamer streamer = MakeStreamer(profile, out go);
            try
            {
                streamer.Tick(32.0, 32.0, 1f);
                TerraChunk chunk;
                Assert.IsTrue(streamer.TryGetChunk(0, 0, out chunk));
                Assert.IsNotNull(chunk.ScatterPoints);
                Assert.IsTrue(chunk.ScatterPoints.Length > 0, "the test world should have trees in chunk 0");
                Assert.AreEqual(chunk.ScatterPoints.Length, chunk.transform.childCount);
            }
            finally
            {
                streamer.Shutdown();
                Object.DestroyImmediate(go);
                Object.DestroyImmediate(prefab);
                Object.DestroyImmediate(profile);
            }
        }

        [Test]
        public void Baker_MakesOneObjectPerChunkAndRemembersWhatItBaked()
        {
            TerraWorldProfile profile = MakeProfile();
            TerraBakeOptions options = new TerraBakeOptions();
            options.minChunkX = 0;
            options.minChunkZ = 0;
            options.maxChunkX = 1;
            options.maxChunkZ = 1;
            options.saveMeshes = false;
            options.scatter = false;
            GameObject root = TerraBaker.Bake(profile, options);
            try
            {
                Assert.AreEqual(4, root.transform.childCount);
                TerraBakedArea area = root.GetComponent<TerraBakedArea>();
                Assert.IsNotNull(area);
                Assert.AreEqual(profile.settings.seed, area.seed);
                Assert.AreEqual(1, area.maxChunkX);
                MeshFilter filter = root.transform.GetChild(0).GetComponent<MeshFilter>();
                Assert.IsNotNull(filter.sharedMesh);
                Assert.IsTrue(filter.sharedMesh.vertexCount > 0);
            }
            finally
            {
                Object.DestroyImmediate(root);
                Object.DestroyImmediate(profile);
            }
        }
    }
}
