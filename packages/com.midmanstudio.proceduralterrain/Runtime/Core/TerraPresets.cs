// ============================================================================
// NOTICE: Full documentation, design decisions, and fix history for this file
// live in docs/com.midmanstudio.proceduralterrain.md, section "Runtime/Core/TerraPresets.cs"
// ============================================================================
using System;

namespace MidManStudio.ProceduralTerrain
{
    /// <summary>
    /// Ready made world settings to start from. They are plain settings objects: change anything you like. The remap numbers
    /// are measured (see <see cref="TerraNoise.MeasureRange"/>), so each preset uses the whole range from its lowest biome to
    /// its highest, and a test keeps them honest.
    /// </summary>
    public static class TerraPresets
    {
        /// <summary>Gentle hills, meadows and a few forests. Endless lattice world, flat shaded.</summary>
        public static TerraWorldSettings RollingHills()
        {
            TerraWorldSettings s = new TerraWorldSettings();
            s.seed = 1001;
            s.height.noise.kind = TerraNoiseKind.Gradient;
            s.height.noise.mode = TerraNoiseMode.Fbm;
            s.height.noise.scale = 240.0;
            s.height.noise.octaves = 4;
            s.height.noise.persistence = 0.5;
            s.height.noise.remapMin = 0.29;
            s.height.noise.remapMax = 0.71;
            s.height.baseHeight = -6.0;
            s.height.heightScale = 56.0;
            s.waterLevel = 0.0;
            s.scatter = new[]
            {
                Trees(0.30f, 0.62f),
                Rocks(0.30f, 0.9f)
            };
            return s;
        }

        /// <summary>A chain of islands in the sea: radial falloff over rolling noise. Built as one Poisson-disc island.</summary>
        public static TerraWorldSettings Archipelago()
        {
            TerraWorldSettings s = RollingHills();
            s.seed = 2002;
            s.height.noise.scale = 170.0;
            s.height.noise.octaves = 5;
            s.height.falloff.mode = TerraFalloffMode.Radial;
            s.height.falloff.radius = 480.0;
            s.height.falloff.exponent = 1.6;
            s.height.heightScale = 70.0;
            s.island.minX = -512.0;
            s.island.maxX = 512.0;
            s.island.minZ = -512.0;
            s.island.maxZ = 512.0;
            s.island.radius = 6.0;
            return s;
        }

        /// <summary>Tall ridged mountains from green foothills to snow.</summary>
        public static TerraWorldSettings Highlands()
        {
            TerraWorldSettings s = RollingHills();
            s.seed = 3003;
            s.height.noise.mode = TerraNoiseMode.Ridged;
            s.height.noise.scale = 320.0;
            s.height.noise.octaves = 5;
            s.height.noise.persistence = 0.45;
            s.height.noise.remapMin = 0.47;
            s.height.noise.remapMax = 0.94;
            s.height.heightScale = 170.0;
            s.height.baseHeight = -10.0;
            s.lattice.spacing = 4.0;
            s.lattice.cellsPerChunk = 64;
            return s;
        }

        /// <summary>Flat topped mesas stepped like a stair: terraces cut by the biome edges.</summary>
        public static TerraWorldSettings Mesas()
        {
            TerraWorldSettings s = RollingHills();
            s.seed = 4004;
            s.height.noise.scale = 200.0;
            s.height.noise.octaves = 3;
            s.height.heightScale = 90.0;
            s.terrace.mode = TerraTerraceMode.Uniform;
            s.terrace.bands = 7;
            s.terrace.tilt = 0.0f;
            s.terrace.flattenLowest = false;
            s.biomes.slopeColor = new TerraColor(0.55f, 0.36f, 0.25f, 1.0f);
            s.biomes.wallColor = new TerraColor(0.55f, 0.36f, 0.25f, 1.0f);
            return s;
        }

        /// <summary>A forest layer: tall things on the middle heights, off the steep ground and the shore.</summary>
        public static TerraScatterLayer Trees(float minValue, float maxValue)
        {
            TerraScatterLayer l = new TerraScatterLayer();
            l.name = "Trees";
            l.minValue = minValue;
            l.maxValue = maxValue;
            l.maxSlopeDegrees = 28.0f;
            l.density = 0.55f;
            l.minDistance = 7.0;
            l.scaleMin = 0.8f;
            l.scaleMax = 1.35f;
            l.variants = 3;
            l.edgeMargin = 4.0;
            return l;
        }

        /// <summary>A rock layer: small things that may sit on slopes.</summary>
        public static TerraScatterLayer Rocks(float minValue, float maxValue)
        {
            TerraScatterLayer l = new TerraScatterLayer();
            l.name = "Rocks";
            l.minValue = minValue;
            l.maxValue = maxValue;
            l.maxSlopeDegrees = 50.0f;
            l.density = 0.2f;
            l.minDistance = 10.0;
            l.scaleMin = 0.6f;
            l.scaleMax = 1.6f;
            l.variants = 4;
            l.edgeMargin = 0.0;
            l.alignToNormal = true;
            return l;
        }
    }
}
