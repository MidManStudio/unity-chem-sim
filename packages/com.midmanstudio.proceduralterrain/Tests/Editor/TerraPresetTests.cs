// ============================================================================
// NOTICE: Full documentation, design decisions, and fix history for this file
// live in docs/com.midmanstudio.proceduralterrain.md, section "Tests/Editor/TerraPresetTests.cs"
// ============================================================================
using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace MidManStudio.ProceduralTerrain.Tests
{
    [TestFixture]
    public class TerraPresetTests
    {
        private static TerraWorldSettings[] All()
        {
            return new[] { TerraPresets.RollingHills(), TerraPresets.Archipelago(), TerraPresets.Highlands(), TerraPresets.Mesas() };
        }

        [Test]
        public void PresetRemapMatchesTheMeasuredRange()
        {
            foreach (TerraWorldSettings s in All())
            {
                TerraNoiseSettings measure = new TerraNoiseSettings();
                measure.kind = s.height.noise.kind;
                measure.mode = s.height.noise.mode;
                measure.scale = s.height.noise.scale;
                measure.octaves = s.height.noise.octaves;
                measure.persistence = s.height.noise.persistence;
                measure.lacunarity = s.height.noise.lacunarity;
                TerraNoise n = new TerraNoise(s.seed, measure);
                double low;
                double high;
                n.MeasureRange(0.01, 0.99, 80000, out low, out high);
                Assert.AreEqual(low, s.height.noise.remapMin, 0.045, "seed " + s.seed + " low");
                Assert.AreEqual(high, s.height.noise.remapMax, 0.045, "seed " + s.seed + " high");
            }
        }

        [Test]
        public void PresetsReachTheirLowAndHighBiomes()
        {
            foreach (TerraWorldSettings s in All())
            {
                if (s.height.falloff.mode != TerraFalloffMode.None)
                {
                    continue;
                }
                TerraWorld w = new TerraWorld(s);
                HashSet<int> bands = new HashSet<int>();
                for (int i = 0; i < 160; i++)
                {
                    for (int j = 0; j < 160; j++)
                    {
                        double h = w.HeightAt(i * 61.0 - 5000.0, j * 59.0 - 4700.0);
                        bands.Add(s.biomes.IndexAt(w.Field.ValueOfHeight(h)));
                    }
                }
                Assert.IsTrue(bands.Count >= 5, "seed " + s.seed + " only reaches " + bands.Count + " biomes");
            }
        }

        [Test]
        public void EveryPresetBuilds()
        {
            foreach (TerraWorldSettings s in All())
            {
                if (s.height.falloff.mode != TerraFalloffMode.None)
                {
                    s.island.minX = -150.0;
                    s.island.maxX = 150.0;
                    s.island.minZ = -150.0;
                    s.island.maxZ = 150.0;
                    s.height.falloff.radius = 140.0;
                    TerraChunkResult island = new TerraWorld(s).BuildIsland();
                    Assert.Greater(island.mesh.TriangleCount, 500);
                    continue;
                }
                TerraWorld w = new TerraWorld(s);
                TerraChunkResult c = w.BuildChunk(0, 0, 1);
                Assert.Greater(c.mesh.TriangleCount, 1000);
                Assert.Greater(w.ScatterChunk(0, 0).Length, 0, "seed " + s.seed + " has scatter");
            }
        }
    }
}
