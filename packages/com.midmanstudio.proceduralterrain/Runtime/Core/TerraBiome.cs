// ============================================================================
// NOTICE: Full documentation, design decisions, and fix history for this file
// live in docs/com.midmanstudio.proceduralterrain.md, section "Runtime/Core/TerraBiome.cs"
// ============================================================================
using System;

namespace MidManStudio.ProceduralTerrain
{
    /// <summary>One band of the normalised value (height or noise), with the gradient that colours it. Also one level of a terrace.</summary>
    [Serializable]
    public class TerraBiome
    {
        public string name = "Biome";

        /// <summary>The value, in 0..1, where this biome ends. The biome starts where the one before it ends.</summary>
        public float upTo = 1.0f;

        /// <summary>The colours across the band, from its lower edge to its upper edge.</summary>
        public TerraGradient gradient = TerraGradient.Solid(TerraColor.Gray);

        public TerraBiome()
        {
        }

        public TerraBiome(string name, float upTo, TerraGradient gradient)
        {
            this.name = name;
            this.upTo = upTo;
            this.gradient = gradient;
        }
    }

    /// <summary>
    /// The biomes of the world as bands of the normalised value, plus the colours that are not tied to a band: rock on steep
    /// slopes, terrace walls, and the small random variation that breaks up flat colour.
    /// </summary>
    [Serializable]
    public class TerraBiomeSet
    {
        public TerraBiome[] biomes = new TerraBiome[0];

        /// <summary>The colour of steep ground.</summary>
        public TerraColor slopeColor = new TerraColor(0.42f, 0.40f, 0.38f, 1.0f);

        /// <summary>Below this slope, in degrees, the biome colour is used as it is.</summary>
        public float slopeStartDegrees = 34.0f;

        /// <summary>At this slope and above the slope colour is used as it is. In between the two blend.</summary>
        public float slopeFullDegrees = 50.0f;

        /// <summary>The colour of terrace walls.</summary>
        public TerraColor wallColor = new TerraColor(0.38f, 0.34f, 0.30f, 1.0f);

        /// <summary>Each triangle's brightness is scaled by a random factor within plus or minus this much. 0 turns it off.</summary>
        public float colorVariation = 0.04f;

        public int Count
        {
            get { return biomes == null ? 0 : biomes.Length; }
        }

        /// <summary>The index of the band a value falls in. With no biomes, 0.</summary>
        public int IndexAt(double value)
        {
            int n = Count;
            for (int i = 0; i < n - 1; i++)
            {
                if (value < biomes[i].upTo)
                {
                    return i;
                }
            }
            return n > 0 ? n - 1 : 0;
        }

        /// <summary>The lower edge of a band.</summary>
        public double Lower(int index)
        {
            return index <= 0 || Count == 0 ? 0.0 : biomes[Math.Min(index, Count) - 1].upTo;
        }

        /// <summary>The upper edge of a band. The last band ends at 1.</summary>
        public double Upper(int index)
        {
            if (Count == 0 || index >= Count - 1)
            {
                return 1.0;
            }
            return biomes[index].upTo;
        }

        /// <summary>The colour of a value inside a band, along that band's gradient.</summary>
        public TerraColor ColorAt(int band, double value)
        {
            if (Count == 0)
            {
                return TerraColor.Gray;
            }
            if (band < 0)
            {
                band = 0;
            }
            else if (band >= Count)
            {
                band = Count - 1;
            }
            double lo = Lower(band);
            double hi = Upper(band);
            double span = hi - lo;
            float t = span > 1e-9 ? (float)((value - lo) / span) : 0.0f;
            TerraGradient g = biomes[band].gradient;
            return g == null ? TerraColor.Gray : g.Evaluate(t);
        }

        /// <summary>The colour of a value, picking the band first.</summary>
        public TerraColor ColorAt(double value)
        {
            return ColorAt(IndexAt(value), value);
        }

        /// <summary>How much of the slope colour a face with this normal.y gets, 0..1.</summary>
        public float SlopeAmount(float normalY)
        {
            if (normalY >= 1.0f)
            {
                return 0.0f;
            }
            float ny = normalY < -1.0f ? -1.0f : normalY;
            double degrees = Math.Acos(ny) * 180.0 / Math.PI;
            double span = slopeFullDegrees - slopeStartDegrees;
            if (span <= 1e-6)
            {
                return degrees >= slopeStartDegrees ? 1.0f : 0.0f;
            }
            return (float)TerraShape.Smooth((degrees - slopeStartDegrees) / span);
        }

        /// <summary>A generic seven band palette from deep water to snow, so a new set is never empty.</summary>
        public static TerraBiomeSet CreateDefault()
        {
            TerraBiomeSet set = new TerraBiomeSet();
            set.biomes = new[]
            {
                new TerraBiome("Deep water", 0.16f, TerraGradient.Two(TerraColor.FromHex(0x1F4E79), TerraColor.FromHex(0x2A6A9E))),
                new TerraBiome("Shallow water", 0.24f, TerraGradient.Two(TerraColor.FromHex(0x2F7FB0), TerraColor.FromHex(0x4FA3C7))),
                new TerraBiome("Sand", 0.28f, TerraGradient.Two(TerraColor.FromHex(0xD9C58A), TerraColor.FromHex(0xE6D8A0))),
                new TerraBiome("Grass", 0.55f, TerraGradient.Two(TerraColor.FromHex(0x6FA83F), TerraColor.FromHex(0x4E8A2E))),
                new TerraBiome("Forest", 0.72f, TerraGradient.Two(TerraColor.FromHex(0x3E7A2A), TerraColor.FromHex(0x2D5F20))),
                new TerraBiome("Rock", 0.88f, TerraGradient.Two(TerraColor.FromHex(0x7C7770), TerraColor.FromHex(0x9A958D))),
                new TerraBiome("Snow", 1.0f, TerraGradient.Two(TerraColor.FromHex(0xE8EEF2), TerraColor.FromHex(0xFFFFFF)))
            };
            return set;
        }
    }
}
