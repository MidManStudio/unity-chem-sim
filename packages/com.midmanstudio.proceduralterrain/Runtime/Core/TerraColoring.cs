// ============================================================================
// NOTICE: Full documentation, design decisions, and fix history for this file
// live in docs/com.midmanstudio.proceduralterrain.md, section "Runtime/Core/TerraColoring.cs"
// ============================================================================
using System;

namespace MidManStudio.ProceduralTerrain
{
    /// <summary>
    /// Works out the colour of a face or a vertex of the ground: the biome at its height, rock on steep slopes, paint from stamps
    /// and a small hashed variation. All of it is a function of true position, so two chunks colour a shared border alike.
    /// </summary>
    public sealed class TerraColoring
    {
        private readonly TerraHeightField field;
        private readonly TerraBiomeSet biomes;
        private readonly ulong seed;

        public TerraColoring(int seed, TerraHeightField field, TerraBiomeSet biomes)
        {
            this.seed = TerraHash.SeedFrom(seed) ^ 0x2545F4914F6CDD1DUL;
            this.field = field;
            this.biomes = biomes ?? TerraBiomeSet.CreateDefault();
        }

        public TerraBiomeSet Biomes
        {
            get { return biomes; }
        }

        /// <summary>The colour of ground at a position and height whose normal points up by normalY.</summary>
        public TerraColor Ground(double x, double z, double height, double normalY)
        {
            double value = field.ValueOfHeight(height);
            TerraColor c = biomes.ColorAt(value);
            float slope = biomes.SlopeAmount((float)normalY);
            if (slope > 0.0f)
            {
                c = TerraColor.Lerp(c, biomes.slopeColor, slope);
            }
            c = field.ApplyPaint(x, z, c);
            return Vary(c, x, z);
        }

        /// <summary>The colour of a terrace wall at a position.</summary>
        public TerraColor Wall(double x, double z, double height)
        {
            return Vary(biomes.wallColor, x, z);
        }

        // Scales the brightness by a hashed factor of the position, quantised so a triangle keeps one factor.
        private TerraColor Vary(TerraColor c, double x, double z)
        {
            float amount = biomes.colorVariation;
            if (amount <= 0.0f)
            {
                return c;
            }
            long qx = (long)Math.Floor(x * 4.0);
            long qz = (long)Math.Floor(z * 4.0);
            double r = TerraHash.ToSigned(TerraHash.Hash(seed, qx, qz));
            float k = 1.0f + (float)r * amount;
            TerraColor v = c.Scaled(k);
            v.a = c.a;
            return v;
        }
    }
}
